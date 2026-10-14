"""Build graphify-out/layers.html: API -> Application -> Infrastructure view from graph.json.

Run `graphify update .` first, then `python graphify-layers.py`.
"""
import json
import re
import collections
import pathlib

root = pathlib.Path(__file__).parent
g = json.load(open(root / 'graphify-out/graph.json', encoding='utf-8'))
N = {n['id']: n for n in g['nodes']}
PORT_DIRS = ('Repositories', 'Storage', 'Security', 'Notifications')


def parts(n):
    f = n.get('source_file', '')
    m = re.match(r'src/LegacyVault\.(\w+)/(\w+)/([^/]+)\.cs$', f)
    return m.groups() if m else (None, None, None)


def column(n):
    """0 API, 1 service interface, 2 service impl, 3 port interface, 4 infra impl."""
    if not n['label'].startswith('.'):
        return None
    layer, folder, stem = parts(n)
    iface = bool(re.match(r'I[A-Z]', stem or ''))
    if layer == 'Api' and folder == 'Controllers':
        return 0
    if layer == 'Application':
        if folder == 'Services':
            return 1 if iface else 2
        if folder in PORT_DIRS and iface:
            return 3
    if layer == 'Infrastructure' and folder in PORT_DIRS + ('Jobs',):
        return 4
    return None


def name(n):
    return parts(n)[2] + '.' + n['label'].strip('.').replace('()', '')


col = {i: column(n) for i, n in N.items()}
ALLOWED = {(0, 1), (1, 2), (2, 3), (3, 4)}
edges = {
    (e['source'], e['target'])
    for e in g['links']
    if e['relation'] in ('calls', 'dispatches_to')
    and col.get(e['source']) is not None
    and col.get(e['target']) is not None
    and (col[e['source']], col[e['target']]) in ALLOWED
}


def read_routes():
    """Map 'Controller.Method' -> 'VERB /route' by scanning controller attributes."""
    routes = {}
    for f in sorted((root / 'src/LegacyVault.Api/Controllers').glob('*.cs')):
        base, pending = None, None
        for line in f.read_text(encoding='utf-8').splitlines():
            m = re.match(r'\s*\[Route\("([^"]*)"\)\]', line)
            if m and base is None:
                base = '/' + m.group(1).strip('/')
            m = re.match(r'\s*\[Http(Get|Post|Put|Delete|Patch)(?:\("([^"]*)"\))?', line)
            if m:
                pending = (m.group(1).upper(), (m.group(2) or '').strip('/'))
                continue
            m = re.match(r'\s*public\s+(?:async\s+)?[\w<>\[\],.? ]+?\s+(\w+)\s*\(', line)
            if m and pending and base:
                path = re.sub(r'\{(\w+):[^}]+\}', lambda mm: '{' + mm.group(1) + '}', base + ('/' + pending[1] if pending[1] else ''))
                routes[f.stem + '.' + m.group(1)] = pending[0] + ' ' + path
                pending = None
    return routes


routes = read_routes()
used = {x for e in edges for x in e}
nodes = [{'id': i, 'name': name(N[i]), 'cls': parts(N[i])[2], 'col': col[i], **({'api': routes[name(N[i])]} if name(N[i]) in routes else {})}
         for i in sorted(used, key=lambda i: name(N[i]))]
explain_file = root / 'api-explanations.json'
explain = json.load(open(explain_file, encoding='utf-8')) if explain_file.exists() else {}
for n in nodes:
    if n['name'] in explain:
        n['explain'] = explain[n['name']]
missing = [n['name'] for n in nodes if 'api' in n and 'explain' not in n]
if missing:
    print('WARNING: no explanation for', missing)
data = {'nodes': nodes, 'edges': [list(e) for e in sorted(edges)]}
print(sum('api' in n for n in nodes), 'api endpoints;', len(nodes), 'nodes', len(edges), 'edges', dict(collections.Counter(n['col'] for n in nodes)))
tpl = (root / 'graphify-layers.template.html').read_text(encoding='utf-8')
(root / 'graphify-out/layers.html').write_text(tpl.replace('__DATA__', json.dumps(data)), encoding='utf-8')
