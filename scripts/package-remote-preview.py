"""Package the reviewed remote preview using the clean publish manifests."""
import hashlib
import json
from pathlib import Path
import shutil
import zipfile

ROOT=Path(__file__).resolve().parents[1]
out=ROOT/'dist/remote-preview'
out.mkdir(parents=True,exist_ok=True)
artifacts=[]
for language,dirname in [('zh-CN','desktop-remote-final'),('en','desktop-remote-final-en')]:
    source=ROOT/'dist'/dirname
    manifest=json.loads((source/'build-files.json').read_text(encoding='utf-8-sig'))
    assert 'codelink_remote.dll' in manifest
    destination=out/('CodeLink-Windows-remote-'+language+'.zip')
    with zipfile.ZipFile(destination,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
        for name in manifest:
            file=(source/name).resolve()
            assert file.is_relative_to(source.resolve()),name
            assert not {'state','.local','.git'}.intersection(Path(name).parts),name
            assert file.suffix.lower() not in ('.pfx','.key','.keystore','.pdb'),name
            archive.write(file,name)
        archive.writestr('runtime.json',json.dumps({'instance':'main','dataDirectory':'state'}))
        archive.writestr('LANGUAGE.txt',language)
        archive.write(ROOT/'docs/REMOTE.md','docs/REMOTE.md')
        for name in ('remote-acceptance.json','remote-security-acceptance.json','scroll-acceptance.json','phone-terminal-acceptance.json','phone-ui-acceptance.json'):
            archive.write(ROOT/'research'/name,'research/'+name)
        archive.writestr('REMOTE-README.txt','Run CodeLink.exe, enable remote access in the pairing window, and scan its remote QR code with the matching Android APK. See docs/REMOTE.md for tested scope and limitations.\n')
    apk=out/('CodeLink-Android-remote-'+language+'.apk')
    shutil.copy2(ROOT/'dist'/('CodeLink-Android-'+language+'.apk'),apk)
    artifacts.extend([destination,apk])
checks=[]
for file in artifacts:
    with file.open('rb') as stream:digest=hashlib.file_digest(stream,'sha256').hexdigest()
    checks.append(digest+'  '+file.name)
(out/'SHA256SUMS.txt').write_text('\n'.join(checks)+'\n',encoding='ascii')
print('Prepared '+str(len(artifacts))+' preview artifacts in '+str(out))
