"""Package an allowlist of built application files, excluding machine state."""
from pathlib import Path
import hashlib
import json
import os
import shutil
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent.parent


def main():
    version = ET.parse(ROOT / 'android/app/src/main/AndroidManifest.xml').getroot().get(
        '{http://schemas.android.com/apk/res/android}versionName')
    desktop = ROOT / os.environ.get('CODELINK_BUILD_OUTPUT', 'dist/desktop')
    apk = ROOT / 'dist/CodeLink-Android.apk'
    required = ['CodeLink.exe', 'clink.exe', 'CodeLink.Host.exe', 'CodeLink.ConsoleBridge.exe',
                'CodeLink.Core.dll', 'assets/icon.png', 'assets/app.ico', 'assets/startup.gif',
                'tools/adb.exe', 'tools/AdbWinApi.dll', 'tools/AdbWinUsbApi.dll',
                'tools/ADB-NOTICE.txt', 'LICENSE', 'THIRD_PARTY_NOTICES.md']
    missing = [name for name in required if not (desktop / name).is_file()]
    if missing or not apk.is_file():
        raise SystemExit('Build both clients first. Missing: ' + ', '.join(missing + ([] if apk.is_file() else [str(apk)])))
    output = ROOT / 'dist/releases' / ('v' + version)
    output.mkdir(parents=True, exist_ok=True)
    archive = output / f'CodeLink-Windows-v{version}.zip'
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as zipped:
        for path in sorted(desktop.iterdir()):
            if path.is_file() and (path.suffix.lower() in {'.exe', '.dll'} or
                                   path.name.endswith(('.deps.json', '.runtimeconfig.json')) or
                                   path.name in {'LICENSE', 'THIRD_PARTY_NOTICES.md'}):
                zipped.write(path, path.name)
        for directory in ['assets', 'tools', 'licenses']:
            for path in sorted((desktop / directory).rglob('*')):
                if path.is_file():
                    zipped.write(path, path.relative_to(desktop).as_posix())
        # Always generate clean defaults; never copy a developer's runtime or pairing state.
        zipped.writestr('runtime.json', json.dumps({'instance': 'main', 'dataDirectory': 'state'}) + '\n')
        zipped.write(ROOT / 'docs/INSTALL.md', 'START-HERE.md')
        zipped.write(ROOT / 'assets/README.md', 'ARTWORK-NOTICE.md')
    packaged_apk = output / apk.name
    shutil.copyfile(apk, packaged_apk)
    sums = []
    for path in [archive, packaged_apk]:
        with path.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        sums.append(f'{digest}  {path.name}')
        print(f'{path.name}: {path.stat().st_size:,} bytes')
    (output / 'SHA256SUMS.txt').write_text('\n'.join(sums) + '\n', encoding='utf-8')
    print(f'Release files: {output}')


if __name__ == '__main__':
    main()
