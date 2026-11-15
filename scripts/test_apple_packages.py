"""Exercise release corruption, architecture and source/privacy regressions."""
from contextlib import redirect_stdout
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import plistlib
import struct
import tempfile
import unittest
from unittest.mock import patch
import zipfile

from apple_packages import checksums, package_names, read_checksums, validate_tag


def module(filename):
    spec = importlib.util.spec_from_file_location(filename, Path(__file__).parent / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


verifier = module('verify-apple-packages.py')
source = module('package-apple-source.py')
publisher = module('publish-apple-release.py')
VERSION = '2.7.3'


def thin(cpu):
    return b'\xcf\xfa\xed\xfe' + struct.pack('<I', cpu) + bytes(24)


def universal():
    return b'\xca\xfe\xba\xbe' + struct.pack('>I', 2) + b''.join(
        struct.pack('>IIIII', cpu, 0, 0, 0, 0) for cpu in (verifier.ARM64, verifier.X86_64))


def fixture(folder, *, wrong_cpu=False, extra_path=None):
    for name in package_names(VERSION):
        files = {}
        executable = []
        info = {'CFBundleShortVersionString': VERSION}
        if 'macOS-' in name:
            prefix = 'CodeLink.app/Contents/'
            cpu = verifier.ARM64 if 'AppleSilicon' in name else verifier.X86_64
            if wrong_cpu:
                cpu = verifier.X86_64 if cpu == verifier.ARM64 else verifier.ARM64
            files[prefix + 'Info.plist'] = plistlib.dumps(info)
            for binary in ('MacOS/CodeLink', 'Resources/backend/CodeLink.Host',
                           'Resources/backend/clink', 'Resources/backend/codelink-pty'):
                executable.append(prefix + binary)
                files[prefix + binary] = thin(cpu)
            for resource in ('backend/CodeLink.Host.dll', 'backend/clink.dll', 'backend/libcoreclr.dylib',
                             'backend/libhostfxr.dylib', 'backend/libhostpolicy.dylib',
                             'Terminal/terminal.html', 'Terminal/xterm.js'):
                files[prefix + 'Resources/' + resource] = b'resource'
            files[prefix + '_CodeSignature/CodeResources'] = b'signature'
        elif 'Simulator' in name:
            info['DTPlatformName'] = 'iphonesimulator'
            files['CodeLink.app/Info.plist'] = plistlib.dumps(info)
            files['CodeLink.app/CodeLink'] = universal()
            executable.append('CodeLink.app/CodeLink')
            files['CodeLink.app/Terminal/terminal.html'] = b'terminal'
            files['CodeLink.app/_CodeSignature/CodeResources'] = b'signature'
        elif 'unsigned-archive' in name:
            prefix = 'CodeLink.xcarchive/Products/Applications/CodeLink.app/'
            info.update(DTPlatformName='iphoneos', UIDeviceFamily=[1, 2])
            files[prefix + 'Info.plist'] = plistlib.dumps(info)
            files[prefix + 'CodeLink'] = thin(verifier.ARM64)
            executable.append(prefix + 'CodeLink')
        else:
            for path in ('apple/CodeLink.xcodeproj/project.pbxproj', 'docs/APPLE.md',
                         'docs/assets/apple-overview.png', 'scripts/build-apple.sh', 'scripts/apple_packages.py',
                         'desktop/CodeLink.Host/CodeLink.Host.csproj', 'localization/en.json',
                         'android/app/src/main/AndroidManifest.xml', 'SOURCE-FILES.txt'):
                files['CodeLink-Apple/' + path] = b'source'
        if extra_path:
            files[extra_path] = b'private or invalid'
        with zipfile.ZipFile(folder / name, 'w') as archive:
            for path, data in files.items():
                entry = zipfile.ZipInfo(path)
                entry.external_attr = (0o100755 if path in executable else 0o100644) << 16
                archive.writestr(entry, data)
    write_sums(folder)


def write_sums(folder):
    (folder / 'APPLE-SHA256SUMS.txt').write_text(''.join(
        f'{digest}  {name}\n' for name, digest in checksums(folder, VERSION).items()), encoding='utf-8')


class FakeGitHub:
    def __init__(self, release=None, corrupt_upload=False):
        self.release = release
        self.calls = []
        self.corrupt_upload = corrupt_upload

    def request(self, method, path, body=None, *, data=None):
        self.calls.append((method, path, body))
        if method == 'GET':
            return self.release
        if path == '/releases':
            self.release = dict(body, id=1, assets=[], upload_url='https://uploads.github.com/test{?name}', html_url='https://github.com/test/release')
            return self.release
        if path.startswith('https://uploads.github.com/'):
            return {'digest': 'sha256:' + ('0' * 64 if self.corrupt_upload else hashlib.sha256(data).hexdigest()), 'size': len(data)}
        if method == 'PATCH':
            self.release.update(body)
            return self.release


class ApplePackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)

    def verify(self):
        with redirect_stdout(io.StringIO()):
            verifier.verify(self.folder, VERSION)

    def test_complete_packages_accept_any_valid_version(self):
        fixture(self.folder)
        self.verify()

    def test_corrupted_download_is_rejected(self):
        fixture(self.folder)
        next(self.folder.glob('*.zip')).write_bytes(b'corrupt')
        with self.assertRaisesRegex(ValueError, 'checksum mismatch'):
            self.verify()

    def test_duplicate_checksum_entry_is_rejected(self):
        fixture(self.folder)
        sums = self.folder / 'APPLE-SHA256SUMS.txt'
        sums.write_text(sums.read_text() + sums.read_text().splitlines()[0] + '\n')
        with self.assertRaisesRegex(ValueError, 'duplicate'):
            read_checksums(self.folder, VERSION)

    def test_checksum_cannot_traverse_outside_folder(self):
        fixture(self.folder)
        sums = self.folder / 'APPLE-SHA256SUMS.txt'
        sums.write_text(sums.read_text().replace('CodeLink-Apple-Source', '../CodeLink-Apple-Source'))
        with self.assertRaisesRegex(ValueError, 'unexpected'):
            read_checksums(self.folder, VERSION)

    def test_wrong_mac_architecture_is_rejected(self):
        fixture(self.folder, wrong_cpu=True)
        with self.assertRaisesRegex(ValueError, 'wrong architecture'):
            self.verify()

    def test_private_state_and_unsafe_paths_are_rejected(self):
        for path in ('CodeLink.app/host.json', 'CodeLink-Apple/apple/.env', '../escape'):
            with self.subTest(path=path):
                fixture(self.folder, extra_path=path)
                with self.assertRaisesRegex(ValueError, 'Private state|Unsafe archive'):
                    self.verify()

    def test_source_excludes_untracked_credentials_and_user_settings(self):
        for name in ('apple/Shared/App.swift', 'apple/.env', 'apple/secret.p12',
                     'apple/CodeLink.xcodeproj/project.pbxproj',
                     'apple/CodeLink.xcodeproj/xcshareddata/xcschemes/CodeLinkiOS.xcscheme',
                     'apple/CodeLink.xcodeproj/xcuserdata/me.xcuserdatad/data',
                     'apple/Generated/Mac-Info.plist', 'apple/Generated/iOS-Info.plist'):
            path = self.folder / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('fixture')
        (self.folder / '.git').mkdir()
        with patch.object(source.subprocess, 'check_output', return_value=b'apple/Shared/App.swift\0'):
            names = {p.relative_to(self.folder).as_posix() for p in source.source_files(self.folder)}
        self.assertNotIn('apple/.env', names)
        self.assertNotIn('apple/secret.p12', names)
        self.assertFalse(any('xcuserdata' in name for name in names))
        self.assertIn('apple/Shared/App.swift', names)
        self.assertIn('apple/CodeLink.xcodeproj/project.pbxproj', names)

    def test_source_manifest_cannot_escape_extracted_directory(self):
        (self.folder / 'SOURCE-FILES.txt').write_text('../private\n')
        with self.assertRaisesRegex(ValueError, 'Invalid source manifest'):
            list(source.source_files(self.folder))

    def test_tag_must_match_application_version(self):
        validate_tag('v' + VERSION, VERSION)
        validate_tag('v' + VERSION + '-apple.1', VERSION)
        for tag in ('v1.0.0', 'main', 'v' + VERSION + '-apple.0'):
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                validate_tag(tag, VERSION)

    def test_release_stays_draft_until_all_six_uploads_pass(self):
        fixture(self.folder)
        api = FakeGitHub()
        with redirect_stdout(io.StringIO()):
            result = publisher.publish(api, self.folder, 'v' + VERSION + '-apple.1', VERSION, 'commit', 'notes')
        self.assertFalse(result['draft'])
        self.assertTrue(result['prerelease'])
        uploads = [c for c in api.calls if c[1].startswith('https://uploads.github.com/')]
        self.assertEqual(6, len(uploads))
        self.assertEqual('PATCH', api.calls[-1][0])

    def test_bad_upload_never_publishes_draft(self):
        fixture(self.folder)
        api = FakeGitHub(corrupt_upload=True)
        with self.assertRaisesRegex(ValueError, 'Uploaded asset checksum'):
            publisher.publish(api, self.folder, 'v' + VERSION, VERSION, 'commit', 'notes')
        self.assertTrue(api.release['draft'])
        self.assertFalse(any(c[0] == 'PATCH' for c in api.calls))

    def test_existing_public_release_asset_is_never_overwritten(self):
        fixture(self.folder)
        name = sorted(package_names(VERSION))[0]
        api = FakeGitHub({'draft': False, 'assets': [{'id': 12, 'name': name, 'digest': 'different'}]})
        with self.assertRaisesRegex(ValueError, 'Published release asset differs'):
            publisher.publish(api, self.folder, 'v' + VERSION, VERSION, 'commit', 'notes')
        self.assertEqual(['GET'], [c[0] for c in api.calls])


if __name__ == '__main__':
    unittest.main()
