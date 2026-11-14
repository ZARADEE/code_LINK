"""Validate repository documentation links and the published site's metadata."""
from pathlib import Path
from html.parser import HTMLParser
import json
import re
import sys
import xml.etree.ElementTree as ET
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parent.parent
ERRORS = []


def check_link(source, target):
    parsed = urlsplit(target)
    if parsed.scheme or target.startswith(("#", "//")):
        return
    path = unquote(parsed.path)
    if not path:
        return
    if source.parent == ROOT / "site" and path.startswith("assets/"):
        resolved = ROOT / "docs" / path
    else:
        resolved = source.parent / path
    if not resolved.exists():
        ERRORS.append(f"{source.relative_to(ROOT)}: missing {target}")


class Page(HTMLParser):
    def __init__(self, source):
        super().__init__()
        self.source = source
        self.canonical = ""
        self.description = ""
        self.has_h1 = False
        self.has_robots = False

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "h1":
            self.has_h1 = True
        if tag == "link" and attrs.get("rel") == "canonical":
            self.canonical = attrs.get("href", "")
        if tag == "meta" and attrs.get("name") == "description":
            self.description = attrs.get("content", "")
        if tag == "meta" and attrs.get("name") == "robots":
            self.has_robots = "index,follow" in attrs.get("content", "")
        if tag == "img" and "alt" not in attrs:
            ERRORS.append(f"{self.source.name}: image missing alt text")
        for key in ("href", "src"):
            if key in attrs:
                check_link(self.source, attrs[key])


def main():
    documents = [*ROOT.glob("*.md"), *ROOT.joinpath("docs").rglob("*.md"), ROOT / "assets/README.md"]
    for source in documents:
        text = re.sub(r"```.*?```", "", source.read_text(encoding="utf-8"), flags=re.S)
        for target in re.findall(r"\[[^\]]*\]\(([^\s)]+)(?:\s+\"[^\"]*\")?\)", text):
            check_link(source, target)
        parser = Page(source)
        parser.feed(text)

    source = ROOT / "site/index.html"
    html = source.read_text(encoding="utf-8")
    page = Page(source)
    page.feed(html)
    canonical = "https://zaradee.github.io/code_LINK/"
    if page.canonical != canonical or not page.description or not page.has_h1 or not page.has_robots:
        ERRORS.append("site/index.html: missing or invalid search metadata")
    data = json.loads(re.search(r'<script type="application/ld\+json">(.*?)</script>', html, re.S)[1])
    manifest = ET.parse(ROOT / "android/app/src/main/AndroidManifest.xml").getroot()
    version = manifest.get("{http://schemas.android.com/apk/res/android}versionName")
    if data.get("softwareVersion") != version or data.get("url") != canonical:
        ERRORS.append("site/index.html: structured data differs from release or canonical URL")
    sitemap = ET.parse(ROOT / "site/sitemap.xml")
    urls = [element.text for element in sitemap.iter() if element.tag.endswith("}loc")]
    if canonical not in urls:
        ERRORS.append("site/sitemap.xml: canonical URL missing")
    if ERRORS:
        print("\n".join(ERRORS))
        return 1
    print(f"OK: {len(documents)} Markdown documents, local assets and website metadata")
    return 0


if __name__ == "__main__":
    sys.exit(main())
