"""Verify release archive metadata, resources and checksums before publishing."""
from pathlib import Path, PurePosixPath
import plistlib
import struct
import sys
import zipfile

from apple_packages import read_checksums, version

ARM64, X86_64 = 0x0100000C, 0x01000007


def require(condition, message):
    if not condition:
        raise ValueError(message)


def architectures(data):
    """Read thin or universal Mach-O CPU types without requiring a Mac."""
    magic = data[:4]
    if magic in (b'\xcf\xfa\xed\xfe', b'\xfe\xed\xfa\xcf'):
        order = '<' if magic[0] == 0xcf else '>'
        return {struct.unpack_from(order + 'I', data, 4)[0]}
    if magic in (b'\xca\xfe\xba\xbe', b'\xbe\xba\xfe\xca',
                 b'\xca\xfe\xba\xbf', b'\xbf\xba\xfe\xca'):
        order = '>' if magic[0] == 0xca else '<'
        count = struct.unpack_from(order + 'I', data, 4)[0]
        require(0 < count <= 16, 'Invalid universal Mach-O architecture count')
        stride = 32 if magic in (b'\xca\xfe\xba\xbf', b'\xbf\xba\xfe\xca') else 20
        return {struct.unpack_from(order + 'I', data, 8 + stride * i)[0] for i in range(count)}
    raise ValueError('Package executable is not a Mach-O binary')


def executable(archive, name, expected):
    require((archive.getinfo(name).external_attr >> 16) & 0o111, 'Executable mode missing: ' + name)
    with archive.open(name) as stream:
        actual = architectures(stream.read(1024))
    require(actual == expected, f'{name}: wrong architecture {actual}, expected {expected}')


def verify(folder, app_version=None):
    app_version = app_version or version()
    for name in read_checksums(folder, app_version):
        path = folder / name
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            require(len(names) == len(set(names)), 'Duplicate archive entries: ' + name)
            for entry in names:
                parts = PurePosixPath(entry).parts
                require(not entry.startswith('/') and '..' not in parts and '\\' not in entry,
                        'Unsafe archive path: ' + entry)
                require(not entry.endswith(('host.json', 'host.pfx', 'sessions.json', 'operations.jsonl',
                                            '.keystore', '.p12', '.mobileprovision'))
                        and 'xcuserdata' not in parts and '.env' not in parts,
                        'Private state in package: ' + entry)
            if 'macOS-' in name:
                prefix = 'CodeLink.app/Contents/'
                info = plistlib.loads(archive.read(prefix + 'Info.plist'))
                require(info['CFBundleShortVersionString'] == app_version, 'Mac version mismatch')
                expected = {ARM64} if 'AppleSilicon' in name else {X86_64}
                for binary in ('MacOS/CodeLink', 'Resources/backend/CodeLink.Host',
                               'Resources/backend/clink', 'Resources/backend/codelink-pty'):
                    executable(archive, prefix + binary, expected)
                for required in ('backend/CodeLink.Host.dll', 'backend/clink.dll',
                                 'backend/libcoreclr.dylib', 'backend/libhostfxr.dylib',
                                 'backend/libhostpolicy.dylib', 'Terminal/terminal.html', 'Terminal/xterm.js'):
                    require(prefix + 'Resources/' + required in names, 'Missing Mac resource: ' + required)
                require(prefix + '_CodeSignature/CodeResources' in names, 'Mac signature missing')
            elif 'Simulator' in name:
                info = plistlib.loads(archive.read('CodeLink.app/Info.plist'))
                require(info['CFBundleShortVersionString'] == app_version, 'Simulator version mismatch')
                require(info['DTPlatformName'] == 'iphonesimulator', 'Expected simulator SDK')
                executable(archive, 'CodeLink.app/CodeLink', {ARM64, X86_64})
                require('CodeLink.app/Terminal/terminal.html' in names, 'Simulator terminal missing')
                require('CodeLink.app/_CodeSignature/CodeResources' in names, 'Simulator signature missing')
            elif 'Source' in name:
                for required in ('apple/CodeLink.xcodeproj/project.pbxproj', 'docs/APPLE.md',
                                 'docs/assets/apple-overview.png', 'scripts/build-apple.sh',
                                 'scripts/apple_packages.py', 'desktop/CodeLink.Host/CodeLink.Host.csproj',
                                 'localization/en.json', 'android/app/src/main/AndroidManifest.xml',
                                 'SOURCE-FILES.txt'):
                    require('CodeLink-Apple/' + required in names, 'Incomplete source archive: ' + required)
            elif 'unsigned-archive' in name:
                app_info = next(n for n in names if n.endswith('/Products/Applications/CodeLink.app/Info.plist'))
                info = plistlib.loads(archive.read(app_info))
                require(info['CFBundleShortVersionString'] == app_version, 'Device version mismatch')
                require(info['DTPlatformName'] == 'iphoneos', 'Expected device SDK')
                require(info['UIDeviceFamily'] == [1, 2], 'Device archive must support iPhone and iPad')
                executable(archive, app_info.removesuffix('Info.plist') + 'CodeLink', {ARM64})
        print('Verified', name, path.stat().st_size)


if __name__ == '__main__':
    verify(Path(sys.argv[1]))
