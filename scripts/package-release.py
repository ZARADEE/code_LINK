"""Package an allowlist of built application files, excluding machine state."""
from pathlib import Path
import hashlib
import json
import os
import re
import shutil
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent.parent


def main():
    version = ET.parse(ROOT / 'android/app/src/main/AndroidManifest.xml').getroot().get(
        '{http://schemas.android.com/apk/res/android}versionName')
    output = ROOT / 'dist/releases' / ('v' + version)
    output.mkdir(parents=True, exist_ok=True)
    packages = []
    for language in ('zh-CN', 'en'):
        variable = 'CODELINK_BUILD_OUTPUT_' + language.replace('-', '_').upper()
        default = os.environ.get('CODELINK_BUILD_OUTPUT', 'dist/desktop') if language == 'zh-CN' else 'dist/desktop-en'
        desktop = ROOT / os.environ.get(variable, default)
        apk = ROOT / f'dist/CodeLink-Android-{language}.apk'
        packages.extend(package_language(version, language, desktop, apk, output))
    sums = []
    for path in packages:
        with path.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        sums.append(f'{digest}  {path.name}')
        print(f'{path.name}: {path.stat().st_size:,} bytes')
    (output / 'SHA256SUMS.txt').write_text('\n'.join(sums) + '\n', encoding='utf-8')
    print(f'Release files: {output}')


def package_language(version, language, desktop, apk, output):
    required = ['CodeLink.exe', 'clink.exe', 'CodeLink.Host.exe', 'CodeLink.ConsoleBridge.exe',
                'CodeLink.Core.dll', 'codelink_remote.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'PresentationFramework.dll',
                'assets/icon.png', 'assets/app.ico', 'assets/startup.gif',
                'tools/adb.exe', 'tools/AdbWinApi.dll', 'tools/AdbWinUsbApi.dll',
                'tools/ADB-NOTICE.txt', 'LICENSE', 'THIRD_PARTY_NOTICES.md',
                'licenses/remote-APACHE-2.0.txt', 'licenses/remote-third-party.txt']
    missing = [name for name in required if not (desktop / name).is_file()]
    if missing or not apk.is_file():
        raise SystemExit('Build both clients first. Missing: ' + ', '.join(missing + ([] if apk.is_file() else [str(apk)])))
    if (desktop / 'build-language.txt').read_text(encoding='utf-8-sig').strip() != language:
        raise SystemExit(f'Build language does not match {language}: {desktop}')
    with zipfile.ZipFile(apk) as android:
        if 'lib/arm64-v8a/libcodelink_remote.so' not in android.namelist():
            raise SystemExit(f'Android remote library is missing: {apk}')
    build_files = json.loads((desktop / 'build-files.json').read_text(encoding='utf-8-sig'))
    for name in required:
        if name not in build_files:
            raise SystemExit(f'Required file is absent from the current build: {name}')
    for app in ('CodeLink', 'CodeLink.Host', 'clink', 'CodeLink.ConsoleBridge'):
        options = json.loads((desktop / f'{app}.runtimeconfig.json').read_text())['runtimeOptions']
        if not options.get('includedFrameworks') or options.get('frameworks') or options.get('framework'):
            raise SystemExit(f'{app} must include its .NET runtime')
    archive = output / f'CodeLink-Windows-v{version}-{language}.zip'
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as zipped:
        for name in sorted(set(build_files)):
            path = (desktop / name).resolve()
            parts = Path(name).parts
            if not path.is_relative_to(desktop.resolve()) or not path.is_file() or '\\' in name:
                raise SystemExit(f'Invalid build file: {name}')
            allowed = (len(parts) == 1 and (path.suffix.lower() in {'.exe', '.dll'} or
                       name.endswith(('.deps.json', '.runtimeconfig.json')) or
                       name in {'LICENSE', 'THIRD_PARTY_NOTICES.md', 'LICENSE.txt', 'ThirdPartyNotices.txt'})) or (
                       len(parts) > 1 and (parts[0] in {'assets', 'tools', 'licenses', 'runtimes'} or
                       re.fullmatch(r'[a-z]{2}(?:-[A-Za-z]{2,4})?', parts[0])))
            if not allowed:
                raise SystemExit(f'Unexpected file in build manifest: {name}')
            zipped.write(path, name)
        # Always generate clean defaults; never copy a developer's runtime or pairing state.
        zipped.writestr('runtime.json', json.dumps({'instance': 'main', 'dataDirectory': 'state'}) + '\n')
        guide = 'docs/INSTALL.en.md' if language == 'en' else 'docs/INSTALL.md'
        # This guide lives at the ZIP root, without the repository's docs tree.
        guide_text = (ROOT / guide).read_text(encoding='utf-8')
        guide_text = re.sub(r'\]\((?![a-z]+:|#)([^)]+\.md(?:#[^)]*)?)\)',
                            r'](https://github.com/ZARADEE/code_LINK/blob/v' + version + r'/docs/\1)', guide_text)
        zipped.writestr('START-HERE.md', guide_text)
        zipped.writestr('LANGUAGE.txt', language + '\n')
        zipped.write(ROOT / 'assets/README.md', 'ARTWORK-NOTICE.md')
    packaged_apk = output / f'CodeLink-Android-v{version}-{language}.apk'
    shutil.copyfile(apk, packaged_apk)
    return [archive, packaged_apk]


if __name__ == '__main__':
    main()
