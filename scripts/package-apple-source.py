"""Produce an Xcode project that can be opened without XcodeGen installed."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

root = Path(__file__).resolve().parent.parent
out = root / 'dist/apple/CodeLink-Apple-Source-v1.3.0.zip'
with ZipFile(out, 'w', ZIP_DEFLATED) as archive:
    for path in sorted((root / 'apple').rglob('*')):
        if path.is_file() and not any(part in ('xcuserdata', '.DS_Store') for part in path.parts):
            archive.write(path, Path('CodeLink-Apple') / path.relative_to(root))
    for name in ('LICENSE', 'licenses/xterm.txt', 'docs/APPLE.md'):
        archive.write(root / name, Path('CodeLink-Apple') / name)
print(out.name)
