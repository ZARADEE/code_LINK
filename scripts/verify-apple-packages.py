"""Verify release archive metadata, resources and checksums before publishing."""
from pathlib import Path
import hashlib,json,plistlib,zipfile,sys

folder=Path(sys.argv[1])
lines=(folder/'APPLE-SHA256SUMS.txt').read_text().splitlines()
expected={f'CodeLink-{platform}-v1.3.0-en.zip' for platform in ('macOS-AppleSilicon','macOS-Intel','iOS-Simulator','iOS-unsigned-archive','Apple-Source')}
assert {line.split('  ',1)[1] for line in lines}==expected,'Missing or unexpected Apple language package'
for line in lines:
    digest,name=line.split('  ',1)
    path=folder/name
    with path.open('rb') as stream: assert hashlib.file_digest(stream,'sha256').hexdigest()==digest,name
    with zipfile.ZipFile(path) as archive:
        names=archive.namelist()
        assert not any(n.endswith(('host.json','host.pfx','sessions.json','operations.jsonl','.keystore','.p12')) for n in names),name
        if 'macOS-' in name:
            info=plistlib.loads(archive.read('CodeLink.app/Contents/Info.plist'))
            assert info['CFBundleShortVersionString']=='1.3.0',info
            for required in ['Contents/MacOS/CodeLink','Contents/Resources/backend/CodeLink.Host','Contents/Resources/backend/codelink-pty','Contents/Resources/Terminal/terminal.html','Contents/Resources/Terminal/xterm.js']:
                assert 'CodeLink.app/'+required in names,(name,required)
            executable=archive.getinfo('CodeLink.app/Contents/Resources/backend/CodeLink.Host')
            assert (executable.external_attr >> 16) & 0o111,'Mac host executable mode missing'
        elif 'Simulator' in name:
            info=plistlib.loads(archive.read('CodeLink.app/Info.plist'))
            assert info['CFBundleShortVersionString']=='1.3.0'
            assert info['DTPlatformName']=='iphonesimulator',info
            assert 'CodeLink.app/Terminal/terminal.html' in names
            assert 'CodeLink.app/_CodeSignature/CodeResources' in names
        elif 'Source' in name:
            assert 'CodeLink-Apple/apple/CodeLink.xcodeproj/project.pbxproj' in names
            assert 'CodeLink-Apple/docs/APPLE.md' in names
            assert 'CodeLink-Apple/docs/assets/apple-overview.png' in names
        elif 'unsigned-archive' in name:
            assert any(n.endswith('/Products/Applications/CodeLink.app/CodeLink') for n in names)
            app_info=next(n for n in names if n.endswith('/Products/Applications/CodeLink.app/Info.plist'))
            info=plistlib.loads(archive.read(app_info))
            assert info['CFBundleShortVersionString']=='1.3.0',info
            assert info['DTPlatformName']=='iphoneos',info
    print('Verified',name,path.stat().st_size)
