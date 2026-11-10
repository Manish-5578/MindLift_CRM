"""Generates the C# and Dart copies of the business card rules from shared/card-rules.json.

Run from anywhere:  python scripts/gen-card-rules.py
Add --check to only report whether the generated files are up to date (exit code 1 when they are not).
"""
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, 'shared', 'card-rules.json')
CS_OUT = os.path.join(ROOT, 'src', 'MindLift.Infrastructure', 'Leads', 'CardRules.g.cs')
DART_OUT = os.path.join(ROOT, 'mobile', 'packages', 'mindlift_mobile', 'lib', 'features', 'leads', 'presentation', 'card_rules.g.dart')

# (json key, C# name, Dart name, what it is)
LISTS = [
    ('strongAddressTokens', 'StrongAddressTokens', 'strongAddressTokens', 'Words that only appear in addresses. One of them is enough.'),
    ('weakAddressTokens', 'WeakAddressTokens', 'weakAddressTokens', 'Usually address words, but common in names and company names too. They count only beside a digit or a comma.'),
    ('placeNames', 'PlaceNames', 'placeNames', 'Cities, states and countries. A line made only of these is an address.'),
    ('addressLabels', 'AddressLabels', 'addressLabels', 'Labels that introduce an address ("Address:", "Regd. Office:").'),
    ('otherLabels', 'OtherLabels', 'otherLabels', 'Labels that introduce something other than an address ("Tel:", "Email:").'),
    ('honorifics', 'Honorifics', 'honorifics', 'Titles that come before a name (Mr, Dr, Sheikh).'),
    ('titleKeywords', 'TitleKeywords', 'titleKeywords', 'Words that make a line a job title.'),
    ('companyKeywords', 'CompanyKeywords', 'companyKeywords', 'Words that make a line a company name (LLC, Ltd, Arabic legal forms).'),
]
PATTERNS = [
    ('noise', 'Noise', 'noise', 'A line that is not contact data: social handle or page, certification mark, copyright footer.'),
    ('founded', 'Founded', 'founded', 'An "Est. 1998" style tagline.'),
    ('socialSite', 'SocialSite', 'socialSite', 'A web address that is a social media page, not the company site.'),
]
HEADER = 'Generated from shared/card-rules.json by scripts/gen-card-rules.py. Do not edit by hand.'


def cs_str(value):
    return '"' + value.replace('\\', '\\\\').replace('"', '\\"') + '"'


def dart_str(value):
    return "'" + value.replace('\\', '\\\\').replace("'", "\\'").replace('$', '\\$') + "'"


def build_cs(rules):
    out = [f'// {HEADER}', 'namespace MindLift.Infrastructure.Leads;', '',
           '/// <summary>The business card reading rules shared with the phone app (see shared/card-rules.json).</summary>',
           'public static class CardRules', '{']
    for key, name, _, doc in LISTS:
        out.append(f'    /// <summary>{doc}</summary>')
        out.append(f'    public static readonly string[] {name} =')
        out.append('    [')
        out.extend(f'        {cs_str(v)},' for v in rules[key])
        out.append('    ];')
        out.append('')
    for key, name, _, doc in PATTERNS:
        out.append(f'    /// <summary>{doc} Case-insensitive.</summary>')
        out.append(f'    public const string {name}Pattern = @{chr(34)}{rules["patterns"][key].replace(chr(34), chr(34) * 2)}{chr(34)};')
        out.append('')
    out.pop()
    out.append('}')
    return '\r\n'.join(out) + '\r\n'


def build_dart(rules):
    out = [f'// {HEADER}', '', '/// The business card reading rules shared with the server (see shared/card-rules.json).',
           'class CardRules {', '  const CardRules._();', '']
    for key, _, name, doc in LISTS:
        out.append(f'  /// {doc}')
        out.append(f'  static const List<String> {name} = [')
        out.extend(f'    {dart_str(v)},' for v in rules[key])
        out.append('  ];')
        out.append('')
    for key, _, name, doc in PATTERNS:
        out.append(f'  /// {doc} Case-insensitive.')
        out.append(f"  static const String {name}Pattern = r'{rules['patterns'][key]}';")
        out.append('')
    out.pop()
    out.append('}')
    return '\r\n'.join(out) + '\r\n'


def main():
    with open(SOURCE, encoding='utf-8') as f:
        rules = json.load(f)
    targets = {CS_OUT: build_cs(rules), DART_OUT: build_dart(rules)}
    stale = []
    for path, text in targets.items():
        current = open(path, encoding='utf-8', newline='').read() if os.path.exists(path) else None
        if current != text:
            stale.append(path)
            if '--check' not in sys.argv:
                with open(path, 'w', encoding='utf-8', newline='') as f:
                    f.write(text)
    if '--check' in sys.argv:
        for p in stale:
            print('out of date:', os.path.relpath(p, ROOT))
        sys.exit(1 if stale else 0)
    print('written:' if stale else 'already up to date:', ', '.join(os.path.relpath(p, ROOT) for p in (stale or targets)))


if __name__ == '__main__':
    main()
