"""Collect licenses for locked Rust crates available in the Windows/Android build cache."""
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import tomllib
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
registry=Path(os.environ.get('CARGO_HOME',str(ROOT/'.tools/cargo')))/'registry/src'
cache=ROOT/'.tools/remote-license-cache'
cache.mkdir(parents=True,exist_ok=True)
packages=tomllib.loads((ROOT/'native/remote/Cargo.lock').read_text())['package']

def collect(package):
    name=package['name']+'-'+package['version']
    found=list(registry.glob('*/'+name))
    if not found:return None
    directory=found[0]
    manifest=tomllib.loads((directory/'Cargo.toml').read_text(encoding='utf-8'))['package']
    notices=[]
    for file in sorted(directory.rglob('*')):
        if file.is_file() and file.name.upper().startswith(('LICENSE','LICENCE','COPYING','COPYRIGHT','NOTICE')) and file.suffix not in ('.rs','.c','.h'):
            try: text=file.read_text(encoding='utf-8')
            except UnicodeError:continue
            notices.append((file.relative_to(directory).as_posix(),text))
    # Some workspace crates omit the repository-level dual-license text.
    if not notices or package['name'] in ('iroh','iroh-relay'):
        repo=manifest.get('repository','').removesuffix('.git').rstrip('/')
        vcs=directory/'.cargo_vcs_info.json'
        commit=json.loads(vcs.read_text())['git']['sha1'] if vcs.exists() else 'master'
        if not repo.startswith('https://github.com/'):
            raise RuntimeError('Missing license: '+name)
        for filename in ('LICENSE-MIT','LICENSE','LICENSE.md','LICENSE.txt','LICENSE-APACHE','LICENSE_MIT','LICENSE-MIT.txt','LICENSE-APACHE.txt'):
            url='https://raw.githubusercontent.com/'+repo.removeprefix('https://github.com/')+'/'+commit+'/'+filename
            saved=cache/(name+'-'+filename.replace('/','_'))
            try:
                if not saved.exists():
                    with urllib.request.urlopen(url,timeout=20) as response:
                        saved.write_bytes(response.read())
                notices.append((url,saved.read_text(encoding='utf-8')))
                break
            except urllib.error.HTTPError as error:
                if error.code!=404:raise
        else:
            # enum-assoc declares Apache-2.0 as an option in Cargo.toml but
            # publishes no standalone license file. Retain its declaration
            # and the complete Apache license supplied alongside these notices.
            if name!='enum-assoc-1.4.2':raise RuntimeError('No upstream license found: '+name)
            notices.append(('Cargo.toml license declaration','MIT OR Apache-2.0\nAuthors: '+', '.join(manifest.get('authors',[]))+'\nDistributed under Apache-2.0; full text follows.'))
            notices.append(('Apache-2.0', (ROOT/'licenses/remote-APACHE-2.0.txt').read_text(encoding='utf-8')))
    return name,manifest.get('license','See upstream'),manifest.get('repository',''),notices

with ThreadPoolExecutor(max_workers=6) as pool:
    records=sorted(record for record in pool.map(collect,packages) if record)
lines=['Code Link native remote transport — third-party notices',
       'Sources: versions pinned in native/remote/Cargo.lock.',
       'Includes build-time dependencies and Windows/Android dependencies present in the build cache.',
       'Dual-license packages may be used under either offered license. Upstream notices follow.','']
for name,license_id,repo,notices in records:
    lines += ['='*78,name+' | '+license_id,repo,'']
    for filename,text in notices:lines += [filename,'',text,'']
destination=ROOT/'licenses/remote-third-party.txt'
destination.write_text('\n'.join(line.rstrip() for line in '\n'.join(lines).splitlines()).rstrip()+'\n',encoding='utf-8')
print('Collected notices for',len(records),'locked packages;',destination.stat().st_size,'bytes')
