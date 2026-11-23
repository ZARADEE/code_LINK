"""Download Android compilation inputs into this workspace and verify upstream hashes.

The existing Rust compiler and MSVC installation remain read-only.
Run from remote-env.ps1 so RUSTC selects the reused compiler when applicable.
"""
import hashlib
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import tomllib
import urllib.request
import zipfile

ROOT=Path(__file__).resolve().parents[1]
DOWNLOADS=ROOT/'.tools/remote-downloads'
DOWNLOADS.mkdir(parents=True,exist_ok=True)

def download(url,path,digest=None,algorithm='sha256'):
    def matches():
        return path.exists() and (not digest or hashlib.file_digest(path.open('rb'),algorithm).hexdigest()==digest)
    if not matches():
        temporary=path.with_suffix(path.suffix+'.part')
        print('Downloading '+url,flush=True)
        with urllib.request.urlopen(url,timeout=90) as response,temporary.open('wb') as output:
            shutil.copyfileobj(response,output)
        temporary.replace(path)
    if not matches():
        raise RuntimeError('Checksum mismatch: '+str(path))

version=subprocess.check_output([os.environ.get('RUSTC','rustc'),'--version'],text=True).split()[1]
if not re.fullmatch(r'\d+\.\d+\.\d+',version):
    raise RuntimeError('A stable Rust release is required')
manifest=DOWNLOADS/('channel-rust-'+version+'.toml')
download('https://static.rust-lang.org/dist/channel-rust-'+version+'.toml',manifest)
component=tomllib.loads(manifest.read_text(encoding='utf-8'))['pkg']['rust-std']['target']['aarch64-linux-android']
archive=DOWNLOADS/('rust-std-'+version+'-android.tar.xz')
sysroot=ROOT/'.tools/android-rust-sysroot'
marker=sysroot/'version.txt'
if not marker.exists() or marker.read_text().strip()!=version:
    download(component['xz_url'],archive,component['xz_hash'])
    prefix='rust-std-'+version+'-aarch64-linux-android/rust-std-aarch64-linux-android/'
    with tarfile.open(archive,'r:xz') as package:
        for member in package.getmembers():
            if not member.isfile() or not member.name.startswith(prefix+'lib/'):
                continue
            target=(sysroot/member.name[len(prefix):]).resolve()
            if not target.is_relative_to(sysroot.resolve()):
                raise RuntimeError('Unsafe Rust archive path')
            target.parent.mkdir(parents=True,exist_ok=True)
            with package.extractfile(member) as source,target.open('wb') as output:
                shutil.copyfileobj(source,output)
    marker.write_text(version,encoding='ascii')

ndk=Path(os.environ.get('CODELINK_ANDROID_NDK',str(ROOT/'.tools/android-ndk-r27c')))
if not (ndk/'toolchains/llvm/prebuilt/windows-x86_64/bin/clang.exe').exists():
    archive=DOWNLOADS/'android-ndk-r27c-windows.zip'
    download('https://dl.google.com/android/repository/android-ndk-r27c-windows.zip',archive,'ac5f7762764b1f15341094e148ad4f847d050c38','sha1')
    if ndk!=(ROOT/'.tools/android-ndk-r27c'):
        raise RuntimeError('CODELINK_ANDROID_NDK must point to an existing NDK installation')
    # ZIP paths are checked before extraction; no destination can escape .tools.
    with zipfile.ZipFile(archive) as package:
        for item in package.infolist():
            if not (ROOT/'.tools'/item.filename).resolve().is_relative_to((ROOT/'.tools').resolve()):
                raise RuntimeError('Unsafe NDK archive path')
        package.extractall(ROOT/'.tools')
print('Remote toolchain ready: Rust '+version+', Android NDK '+str(ndk),flush=True)
