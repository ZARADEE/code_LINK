"""Fetch pinned Windows build inputs; verify downloads before extracting them."""
from pathlib import Path
import hashlib
import shutil
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent.parent
TOOLS = ROOT / ".tools"
INPUTS = [
    ("build-tools-35.zip", "https://dl.google.com/android/repository/build-tools_r35_windows.zip",
     "5753c679a1b90bcf6fbc9945a2ce39dfb9e74f1df0831a1e1866ae5b594326f0", "android-sdk/build-tools"),
    ("platform-35.zip", "https://dl.google.com/android/repository/platform-35_r02.zip",
     "0988cacad01b38a18a47bac14a0695f246bc76c1b06c0eeb8eb0dc825ab0c8e0", "android-sdk/platforms"),
    ("platform-tools-latest-windows.zip", "https://dl.google.com/android/repository/platform-tools_r37.0.1-win.zip",
     "45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99", "."),
    ("zxing-core.jar", "https://repo.maven.apache.org/maven2/com/google/zxing/core/3.5.3/core-3.5.3.jar",
     "8d8064c1636fdaef7189dd9055c7d59950a8940a12f2293956446ec3c109fd82", None),
]


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    TOOLS.mkdir(exist_ok=True)
    for name, url, digest, destination in INPUTS:
        archive = TOOLS / name
        if not archive.exists() or sha256(archive) != digest:
            partial = archive.with_suffix(archive.suffix + ".part")
            print("Downloading", name, flush=True)
            with urllib.request.urlopen(url, timeout=90) as response, partial.open("wb") as output:
                shutil.copyfileobj(response, output)
            if sha256(partial) != digest:
                raise RuntimeError("SHA256 mismatch: " + name)
            partial.replace(archive)
        if destination is not None:
            target = (TOOLS / destination).resolve()
            with zipfile.ZipFile(archive) as zipped:
                for member in zipped.infolist():
                    if not (target / member.filename).resolve().is_relative_to(target):
                        raise RuntimeError("Unsafe archive entry: " + member.filename)
                zipped.extractall(target)
        print("Verified", name, flush=True)


if __name__ == "__main__":
    main()
