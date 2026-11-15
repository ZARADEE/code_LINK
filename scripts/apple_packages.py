"""Shared Apple package version, names and checksums."""
from pathlib import Path
import hashlib
import os
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PLATFORMS = ('macOS-AppleSilicon', 'macOS-Intel', 'iOS-Simulator',
             'iOS-unsigned-archive', 'Apple-Source')


def version(root=ROOT):
    value = ET.parse(root / 'android/app/src/main/AndroidManifest.xml').getroot().get(
        '{http://schemas.android.com/apk/res/android}versionName', '')
    if not re.fullmatch(r'\d+\.\d+\.\d+', value):
        raise ValueError('Invalid application version: ' + value)
    return value


def package_names(app_version):
    return {f'CodeLink-{platform}-v{app_version}-en.zip' for platform in PLATFORMS}


def validate_tag(tag, app_version):
    # Apple rebuilds do not move stable tags or replace signed Android packages.
    if not re.fullmatch(r'v' + re.escape(app_version) + r'(?:-apple\.[1-9]\d*)?', tag):
        raise ValueError(f'Tag {tag!r} does not match application version {app_version}')


def checksums(folder, app_version):
    result = {}
    for name in sorted(package_names(app_version)):
        with (folder / name).open('rb') as stream:
            result[name] = hashlib.file_digest(stream, 'sha256').hexdigest()
    return result


def read_checksums(folder, app_version):
    entries = {}
    for line in (folder / 'APPLE-SHA256SUMS.txt').read_text(encoding='utf-8').splitlines():
        digest, name = line.split('  ', 1)
        if not re.fullmatch(r'[0-9a-f]{64}', digest) or name in entries:
            raise ValueError('Invalid or duplicate checksum entry: ' + name)
        entries[name] = digest
    if set(entries) != package_names(app_version):
        raise ValueError('Missing or unexpected Apple package')
    if entries != checksums(folder, app_version):
        raise ValueError('Apple package checksum mismatch')
    return entries


if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=('version', 'check-version', 'write-checksums'))
    args = parser.parse_args()
    app_version = version()
    if args.mode == 'version':
        print(app_version)
    elif args.mode == 'check-version':
        project = (ROOT / 'apple/project.yml').read_text(encoding='utf-8')
        match = re.search(r"MARKETING_VERSION:\s*['\"]([^'\"]+)['\"]", project)
        if not match or match[1] != app_version:
            raise SystemExit('Apple MARKETING_VERSION differs from the application version')
        if os.environ.get('GITHUB_REF_TYPE') == 'tag':
            validate_tag(os.environ['GITHUB_REF_NAME'], app_version)
        print('Apple application version:', app_version)
    else:
        folder = ROOT / 'dist/apple'
        (folder / 'APPLE-SHA256SUMS.txt').write_text(''.join(
            f'{digest}  {name}\n' for name, digest in checksums(folder, app_version).items()),
            encoding='utf-8')
