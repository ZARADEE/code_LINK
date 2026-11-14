"""Create English build inputs without modifying canonical sources or user data.

Only source string literals and XML attributes are translated. Protocol values,
terminal content and multilingual activity-detection patterns are kept intact.
"""
from pathlib import Path
import argparse
import json
import re
import shutil

ROOT = Path(__file__).resolve().parent.parent
CATALOG = ROOT / 'localization/en.json'
HAN = re.compile(r'[\u3400-\u9fff]')
EXCLUDED = {'desktop/CodeLink.Core/WindowActivity.cs'}


def literals(text, xml=False):
    """Yield literal contents, including a whole interpolated C# string."""
    if xml:
        for match in re.finditer(r'="([^"\n]*)"', text):
            yield match.start(1), match.end(1)
        return
    size = len(text)

    def quoted(start, verbatim=False, interpolated=False):
        i = start + 1
        depth = 0
        while i < size:
            char = text[i]
            if depth:
                if char in ('"', "'"):
                    i = quoted(i)
                    continue
                if char == '{': depth += 1
                if char == '}': depth -= 1
            elif char == text[start]:
                if verbatim and i + 1 < size and text[i + 1] == char:
                    i += 2
                    continue
                return i + 1
            elif interpolated and char == '{':
                if i + 1 < size and text[i + 1] == '{':
                    i += 2
                    continue
                depth = 1
            if char == '\\' and not verbatim:
                i += 2
            else:
                i += 1
        raise ValueError('Unclosed source literal near ' + text[start:start + 60])

    i = 0
    while i < size:
        if text.startswith('//', i):
            end = text.find('\n', i)
            i = size if end < 0 else end + 1
        elif text.startswith('/*', i):
            i = text.index('*/', i + 2) + 2
        elif text.startswith('"""', i):
            end = text.index('"""', i + 3)
            yield i + 3, end
            i = end + 3
        elif text[i] == '"':
            prefix = text[max(0, i - 2):i]
            end = quoted(i, '@' in prefix, '$' in prefix)
            yield i + 1, end - 1
            i = end
        elif text[i] == "'":
            i = quoted(i)
        else:
            i += 1


def sources():
    for directory in ('desktop', 'android/app/src/main'):
        for path in sorted((ROOT / directory).rglob('*')):
            relative = path.relative_to(ROOT).as_posix()
            if path.suffix in ('.cs', '.java', '.xaml', '.xml') and not {'bin', 'obj'}.intersection(path.parts) and 'CodeLink.Smoke' not in path.parts and relative not in EXCLUDED:
                yield path


def messages():
    result = {}
    for path in sources():
        content = path.read_text(encoding='utf-8-sig')
        for start, end in literals(content, path.suffix in ('.xaml', '.xml')):
            value = content[start:end]
            if HAN.search(value): result.setdefault(value, []).append(path.relative_to(ROOT).as_posix())
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=('extract', 'check', 'generate'))
    args = parser.parse_args()
    entries = messages()
    if args.mode == 'extract':
        out = ROOT / '.local/localization-messages.json'
        out.parent.mkdir(exist_ok=True)
        out.write_text(json.dumps(entries, ensure_ascii=False, indent=2), encoding='utf-8')
        print(f'{len(entries)} messages extracted to {out}')
        return
    catalog = json.loads(CATALOG.read_text(encoding='utf-8'))
    missing = [value for value in entries if not catalog.get(value)]
    if missing: raise SystemExit('Missing English translations:\n' + '\n'.join(missing))
    for original in entries:
        translated = catalog[original]
        if HAN.search(translated): raise SystemExit('Untranslated text: ' + original)
        # Code expressions in interpolated messages must be preserved verbatim.
        expressions = re.findall(r'(?<!\{)\{[^{}]+\}(?!\})', original)
        if expressions != re.findall(r'(?<!\{)\{[^{}]+\}(?!\})', translated):
            raise SystemExit('Interpolation changed: ' + original)
    if args.mode == 'check':
        print(f'OK: {len(entries)} English translations; interpolation expressions preserved')
        return
    destination = ROOT / '.local/localized/en'
    destination.mkdir(parents=True, exist_ok=True)
    manifest_path = destination / '.source-files.json'
    previous = set(json.loads(manifest_path.read_text(encoding='utf-8'))) if manifest_path.exists() else set()
    copied = set()
    for directory in ('desktop', 'android/app/src/main', 'assets', 'licenses'):
        source = ROOT / directory
        for path in source.rglob('*'):
            if not path.is_file() or {'bin', 'obj'}.intersection(path.parts): continue
            target = destination / path.relative_to(ROOT)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target)
            copied.add(path.relative_to(ROOT).as_posix())
    for name in ('LICENSE', 'THIRD_PARTY_NOTICES.md'):
        shutil.copy2(ROOT / name, destination / name)
        copied.add(name)
    for stale in previous - copied:
        target = (destination / stale).resolve()
        if not target.is_relative_to(destination.resolve()) or {'bin', 'obj'}.intersection(target.parts):
            raise ValueError('Invalid generated-source manifest path')
        if target.is_file(): target.unlink()
    manifest_path.write_text(json.dumps(sorted(copied)), encoding='utf-8')
    for path in sources():
        content = path.read_text(encoding='utf-8-sig')
        spans = list(literals(content, path.suffix in ('.xaml', '.xml')))
        for start, end in reversed(spans):
            original = content[start:end]
            if HAN.search(original): content = content[:start] + catalog[original] + content[end:]
        (destination / path.relative_to(ROOT)).write_text(content, encoding='utf-8')
    manifest = destination / 'android/app/src/main/AndroidManifest.xml'
    manifest.write_text(manifest.read_text(encoding='utf-8').replace('android:value="zh-CN"', 'android:value="en"'), encoding='utf-8')
    # A mobile client may connect to a host built in another language. Translate
    # known app messages only; never translate terminal output, user input or names.
    pairs = []
    csharp_pairs = []
    for original, paths in entries.items():
        if (not any(path.startswith('desktop/CodeLink.Core/') for path in paths) and original != '蓝牙') or '{' in original:
            continue
        try:
            source_text = json.loads('"' + original + '"')
            target_text = json.loads('"' + catalog[original] + '"')
        except json.JSONDecodeError:
            continue
        pairs.append('  TEXT.put(' + json.dumps(source_text) + ',' + json.dumps(target_text) + ');')
        csharp_pairs.append('  [' + json.dumps(source_text) + ']=' + json.dumps(target_text) + ',')
    java = '\n'.join([
        'package com.codelink.app;',
        'public final class UiMessages {',
        ' private static final java.util.Map<String,String> TEXT=new java.util.HashMap<>();',
        ' static {', *pairs, ' }',
        ' private UiMessages() {}',
        ' public static String server(String message) { return TEXT.containsKey(message)?TEXT.get(message):message; }',
        '}', ''])
    (destination / 'android/app/src/main/java/com/codelink/app/UiMessages.java').write_text(java, encoding='utf-8')
    csharp = '\n'.join([
        'namespace CodeLink;',
        'public static class UiMessages {',
        ' private static readonly Dictionary<string,string> Text = new() {',
        *csharp_pairs, ' };',
        ' public static string Server(string message) => Text.TryGetValue(message,out var translated)?translated:message;',
        '}', ''])
    (destination / 'desktop/CodeLink.Core/UiMessages.cs').write_text(csharp, encoding='utf-8')
    print(f'Generated English build sources: {destination}')


if __name__ == '__main__': main()
