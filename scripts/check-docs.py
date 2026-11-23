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
        self.alternates = {}

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "h1":
            self.has_h1 = True
        if tag == "link" and attrs.get("rel") == "canonical":
            self.canonical = attrs.get("href", "")
        if tag == "link" and attrs.get("rel") == "alternate" and "hreflang" in attrs:
            self.alternates[attrs["hreflang"]] = attrs.get("href", "")
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
    documents = [*ROOT.glob("*.md"), *ROOT.joinpath("docs").rglob("*.md"),
                 *ROOT.joinpath(".github").rglob("*.md"), ROOT / "assets/README.md"]
    for source in documents:
        text = re.sub(r"```.*?```", "", source.read_text(encoding="utf-8"), flags=re.S)
        for target in re.findall(r"\[[^\]]*\]\(([^\s)]+)(?:\s+\"[^\"]*\")?\)", text):
            check_link(source, target)
        parser = Page(source)
        parser.feed(text)

    canonical = "https://zaradee.github.io/code_LINK/"
    manifest = ET.parse(ROOT / "android/app/src/main/AndroidManifest.xml").getroot()
    version = manifest.get("{http://schemas.android.com/apk/res/android}versionName")
    desktop = ET.parse(ROOT / "desktop/Directory.Build.props").getroot()
    if desktop.findtext("PropertyGroup/Version") != version:
        ERRORS.append("desktop/Directory.Build.props: version differs from Android")
    apple = (ROOT / "apple/project.yml").read_text(encoding="utf-8")
    if not re.search(r'''MARKETING_VERSION:\s*["']?''' + re.escape(version) + r'''["']?\s*$''', apple, re.M):
        ERRORS.append("apple/project.yml: version differs from Android")
    page_urls = {"index.html": canonical, "en.html": canonical + "en.html",
                 "windows-android-terminal.html": canonical + "windows-android-terminal.html",
                 "windows-android-terminal.en.html": canonical + "windows-android-terminal.en.html"}
    for name, url in page_urls.items():
        source = ROOT / "site" / name
        html = source.read_text(encoding="utf-8")
        page = Page(source)
        page.feed(html)
        if page.canonical != url or not page.description or not page.has_h1 or not page.has_robots:
            ERRORS.append(f"site/{name}: missing or invalid search metadata")
        article = name.startswith("windows-android-terminal")
        expected = ({"zh-CN": page_urls["windows-android-terminal.html"],
                     "en": page_urls["windows-android-terminal.en.html"],
                     "x-default": page_urls["windows-android-terminal.en.html"]} if article else
                    {"zh-CN": canonical, "en": page_urls["en.html"], "x-default": canonical})
        if page.alternates != expected:
            ERRORS.append(f"site/{name}: missing reciprocal language links")
        structured = re.search(r'<script type="application/ld\+json">(.*?)</script>', html, re.S)
        try:
            data = json.loads(structured[1]) if structured else {}
        except json.JSONDecodeError:
            data = {}
        valid_type = (data.get("@type") == "TechArticle" and bool(data.get("headline")) and
                      bool(data.get("dateModified"))) if article else (
                      data.get("@type") == "SoftwareApplication" and data.get("softwareVersion") == version)
        if not valid_type or data.get("url") != url:
            ERRORS.append(f"site/{name}: structured data differs from release or canonical URL")
    sitemap = ET.parse(ROOT / "site/sitemap.xml")
    urls = [element.text for element in sitemap.iter() if element.tag.endswith("}loc")]
    if not set(page_urls.values()).issubset(urls):
        ERRORS.append("site/sitemap.xml: language page URL missing")
    if ERRORS:
        print("\n".join(ERRORS))
        return 1
    print(f"OK: {len(documents)} Markdown documents, local assets and website metadata")
    return 0


if __name__ == "__main__":
    sys.exit(main())
