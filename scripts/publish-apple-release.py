"""Publish verified Apple packages after the workflow's integration job passes."""
from pathlib import Path
import hashlib
import json
import os
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request

from apple_packages import ROOT, read_checksums, validate_tag, version


class GitHub:
    def __init__(self, repository, token):
        self.base = 'https://api.github.com/repos/' + repository
        self.token = token

    def request(self, method, path, body=None, *, data=None):
        headers = {'Authorization': 'Bearer ' + self.token,
                   'Accept': 'application/vnd.github+json',
                   'X-GitHub-Api-Version': '2022-11-28',
                   'User-Agent': 'CodeLink-Apple-Release'}
        if data is not None:
            headers['Content-Type'] = 'application/octet-stream'
        elif body is not None:
            headers['Content-Type'] = 'application/json'
            data = json.dumps(body).encode('utf-8')
        url = path if path.startswith('https://uploads.github.com/') else self.base + path
        request = urllib.request.Request(url, data=data, headers=headers, method=method)
        try:
            with urllib.request.urlopen(request, timeout=180) as response:
                raw = response.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            if error.code == 404 and method == 'GET':
                return None
            raise RuntimeError(f'GitHub {method} failed with HTTP {error.code}') from None


def asset_digest(path):
    with path.open('rb') as stream:
        return 'sha256:' + hashlib.file_digest(stream, 'sha256').hexdigest()


def publish(api, folder, tag, app_version, commit, notes):
    validate_tag(tag, app_version)
    sums = read_checksums(folder, app_version)
    files = [folder / name for name in sorted(sums)] + [folder / 'APPLE-SHA256SUMS.txt']
    release = api.request('GET', '/releases/tags/' + urllib.parse.quote(tag, safe=''))
    if release is None:
        release = api.request('POST', '/releases', {
            'tag_name': tag, 'target_commitish': commit, 'name': 'Code Link Apple ' + tag,
            'body': notes, 'draft': True, 'prerelease': '-apple.' in tag,
        })
    present = {asset['name']: asset for asset in release['assets']}
    # Check the entire set before changing anything. Published assets are immutable.
    for path in files:
        current = present.get(path.name)
        if current and current.get('digest') != asset_digest(path) and not release['draft']:
            raise ValueError('Published release asset differs: ' + path.name)
    for path in files:
        current = present.get(path.name)
        digest = asset_digest(path)
        if current and current.get('digest') == digest:
            print('Already verified:', path.name, flush=True)
            continue
        if current:
            api.request('DELETE', '/releases/assets/' + str(current['id']))
        url = release['upload_url'].split('{')[0] + '?name=' + urllib.parse.quote(path.name)
        uploaded = api.request('POST', url, data=path.read_bytes())
        if uploaded.get('digest') != digest or uploaded['size'] != path.stat().st_size:
            raise ValueError('Uploaded asset checksum mismatch: ' + path.name)
        print('Uploaded and SHA-256 verified:', path.name, flush=True)
    if release['draft']:
        release = api.request('PATCH', '/releases/' + str(release['id']),
                              {'draft': False, 'make_latest': 'false'})
    print('Published:', release['html_url'], flush=True)
    return release


def main():
    tag = os.environ['GITHUB_REF_NAME']
    app_version = version()
    validate_tag(tag, app_version)
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    tagged = subprocess.check_output(['git', 'rev-parse', tag + '^{commit}'], cwd=ROOT, text=True).strip()
    if commit != tagged or commit != os.environ['GITHUB_SHA']:
        raise SystemExit('Release tag, checkout and validated workflow commit differ')
    from importlib.util import spec_from_file_location, module_from_spec
    spec = spec_from_file_location('verify_apple_packages', ROOT / 'scripts/verify-apple-packages.py')
    verifier = module_from_spec(spec)
    spec.loader.exec_module(verifier)
    folder = Path(sys.argv[1])
    verifier.verify(folder, app_version)
    notes = (ROOT / 'docs/APPLE-RELEASE.md').read_text(encoding='utf-8')
    notes += f'\n\nBuild commit: `{commit}`\nValidation: {os.environ["GITHUB_SERVER_URL"]}/{os.environ["GITHUB_REPOSITORY"]}/actions/runs/{os.environ["GITHUB_RUN_ID"]}\n'
    publish(GitHub(os.environ['GITHUB_REPOSITORY'], os.environ['GITHUB_TOKEN']),
            folder, tag, app_version, commit, notes)


if __name__ == '__main__':
    main()
