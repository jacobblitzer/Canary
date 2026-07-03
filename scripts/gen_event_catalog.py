#!/usr/bin/env python3
"""Regenerate the NDJSON event catalog in docs/session-flight-recorder.md (R1.6, G6 appendix).

Greps the sibling Penumbra + CPig checkouts for every literal NdjsonLog.Log("kind"...) /
NdjsonLog.Action("kind"...) emission and rewrites the block between the BEGIN/END markers.
Hand-writing ~180 kinds would rot; the greps are the source of truth (all observed call sites
use a literal string first argument, except dynamic prefixes recorded as `prefix*` — verified 2026-07-03).

Usage: python scripts/gen_event_catalog.py    (from C:/Repos/Canary; needs sibling checkouts)
"""
import os
import re
import sys
from collections import defaultdict

DOC = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'docs', 'session-flight-recorder.md'))
BEGIN = '<!-- BEGIN GENERATED EVENT CATALOG (scripts/gen_event_catalog.py) -->'
END = '<!-- END GENERATED EVENT CATALOG -->'

SCAN = [
    ('Penumbra', r'C:\Repos\Penumbra\hosts'),
    ('Penumbra', r'C:\Repos\Penumbra\packages'),
    ('CPig', r'C:\Repos\CPig\CPig.Rhino'),
    ('CPig', r'C:\Repos\CPig\CPig.Grasshopper'),
]
# SafeNdjson = StartupDiagnostics' local wrapper (banner/diagnostics) — same first-arg shape.
KIND_RE = re.compile(r'(?:NdjsonLog\.(?:Log|Action)|SafeNdjson)\(\s*"([^"]+)"(?P<dyn>\s*\+)?')


def collect():
    kinds = defaultdict(set)   # kind -> {repos}
    for repo, root in SCAN:
        if not os.path.isdir(root):
            print(f'WARN: {root} absent — catalog incomplete for {repo}', file=sys.stderr)
            continue
        for dirpath, _dirnames, filenames in os.walk(root):
            if 'node_modules' in dirpath or '.native-build' in dirpath:
                continue
            for f in filenames:
                if not f.endswith('.cs'):
                    continue
                try:
                    text = open(os.path.join(dirpath, f), encoding='utf-8', errors='replace').read()
                except OSError:
                    continue
                for m in KIND_RE.finditer(text):
                    # A `+` after the literal = DYNAMIC kind (e.g. "rep." + rep in the
                    # deprecated OOP command) — record as an explicit wildcard entry.
                    name = m.group(1) + ('*' if m.group('dyn') else '')
                    kinds[name].add(repo)
    return kinds


def main() -> None:
    kinds = collect()
    fams = defaultdict(list)
    for k in sorted(kinds):
        fams[k.split('.')[0]].append(k)

    out = [BEGIN, '']
    out.append(f'_{len(kinds)} distinct NDJSON kinds, grep-generated from the sibling Penumbra + CPig '
               f'checkouts. Regenerate: `python scripts/gen_event_catalog.py`. Filter any of these through '
               f'`get_session_telemetry(eventPrefix=…)` — tailed records carry the kind at `Data.event`._')
    out.append('')
    for fam in sorted(fams):
        ks = fams[fam]
        out.append(f'### `{fam}.*` ({len(ks)})')
        out.append('')
        for k in ks:
            repos = '+'.join(sorted(kinds[k]))
            out.append(f'- `{k}` ({repos})')
        out.append('')
    out.append(END)

    doc = open(DOC, encoding='utf-8').read()
    b, e = doc.find(BEGIN), doc.find(END)
    if b < 0 or e < 0:
        sys.exit(f'markers missing in {DOC}')
    open(DOC, 'w', encoding='utf-8', newline='\n').write(doc[:b] + '\n'.join(out) + doc[e + len(END):])
    print(f'wrote {len(kinds)} kinds across {len(fams)} families into {DOC}')


if __name__ == '__main__':
    main()
