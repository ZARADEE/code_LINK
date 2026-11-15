"""Package tracked build inputs and generated, shared Xcode project metadata."""
from pathlib import Path
import subprocess
from zipfile import ZipFile, ZIP_DEFLATED

from apple_packages import ROOT, version


def source_files(root=ROOT):
    # git's allowlist excludes local credentials, compiled output and user data.
    tracked = subprocess.check_output([
        'git', 'ls-files', '-z', '--', 'apple', 'desktop', 'android/app/src/main',
        'assets', 'licenses', 'localization', 'scripts', 'LICENSE',
        'THIRD_PARTY_NOTICES.md', 'docs/APPLE.md', 'docs/APPLE-RELEASE.md', 'docs/assets/apple-overview.png',
    ], cwd=root).decode('utf-8').split('\0') if (root / '.git').exists() else (root / 'SOURCE-FILES.txt').read_text(encoding='utf-8').splitlines()
    for name in tracked:
        if name and not (root / name).resolve().is_relative_to(root.resolve()):
            raise ValueError('Invalid source manifest path: ' + name)
    paths = {root / name for name in tracked if name}
    project = root / 'apple/CodeLink.xcodeproj'
    generated = [project / 'project.pbxproj', root / 'apple/Generated/Mac-Info.plist',
                 root / 'apple/Generated/iOS-Info.plist']
    generated.extend(sorted((project / 'xcshareddata/xcschemes').glob('*.xcscheme')))
    for path in generated:
        if not path.is_file():
            raise FileNotFoundError('Generate the Xcode project first: ' + str(path))
        paths.add(path)
    if not list((project / 'xcshareddata/xcschemes').glob('*.xcscheme')):
        raise FileNotFoundError('Generated shared Xcode schemes are missing')
    for path in sorted(paths):
        if path.is_symlink() or not path.is_file():
            raise ValueError('Source package requires regular files: ' + str(path))
        yield path


def main():
    out = ROOT / f'dist/apple/CodeLink-Apple-Source-v{version()}-en.zip'
    out.parent.mkdir(parents=True, exist_ok=True)
    with ZipFile(out, 'w', ZIP_DEFLATED) as archive:
        paths = list(source_files())
        for path in paths:
            archive.write(path, Path('CodeLink-Apple') / path.relative_to(ROOT))
        archive.writestr('CodeLink-Apple/SOURCE-FILES.txt', ''.join(
            path.relative_to(ROOT).as_posix() + '\n' for path in paths))
    print(out.name)


if __name__ == '__main__':
    main()
