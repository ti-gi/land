"""
Diagnostic: list the PDF's optional-content layers on given sheets, with stroke/fill colours and text samples.
Useful when a new GUP edition changes layer names or colours.

    python inspect_layers.py path/to/plan.pdf 20 30
"""
import sys
from collections import Counter, defaultdict
from pypdf import PdfReader
from pypdf.generic import ContentStream
r = PdfReader(sys.argv[1])
for pn in map(int, sys.argv[2:]):
    pg = r.pages[pn - 1]
    props = pg['/Resources']['/Properties']
    ocname = {k: props[k].get_object().get('/Name') for k in props}
    stack = []; stats = defaultdict(Counter); texts = defaultdict(list)
    fill = stroke = None; w = None; font = None
    for operands, op in ContentStream(pg.get_contents(), r).operations:
        op = op.decode() if isinstance(op, bytes) else op
        if op == 'BDC':
            stack.append(ocname.get(str(operands[1])) if str(operands[0]) == '/OC' else None); continue
        if op == 'BMC': stack.append(None); continue
        if op == 'EMC':
            if stack: stack.pop()
            continue
        layer = next((s for s in reversed(stack) if s), 'none')
        if op == 'rg': fill = tuple(round(float(x), 2) for x in operands)
        elif op == 'RG': stroke = tuple(round(float(x), 2) for x in operands)
        elif op == 'w': w = round(float(operands[0]), 2)
        elif op == 'Tf': font = (str(operands[0]), round(float(operands[1]), 1))
        elif op in ('f', 'f*', 'B', 'B*', 'b', 'b*'): stats[layer][('fill', fill)] += 1
        elif op in ('S', 's'): stats[layer][('stroke', stroke, w)] += 1
        elif op in ('Tj', 'TJ'):
            t = operands[0]
            s = ''.join(str(x) for x in t if not isinstance(x, (int, float))) if op == 'TJ' else str(t)
            texts[layer].append(s)
    print(f'===== page {pn}')
    for layer in sorted(set(stats) | set(texts)):
        print(f'[{layer}]', dict(stats[layer].most_common(6)))
        if texts[layer]:
            print('   text ops:', len(texts[layer]), 'sample:', texts[layer][:15])
