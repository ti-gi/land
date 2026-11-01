"""Parse one sheet of the GUP 2025 land-use PDF: zone clip-polygons + hatch signature."""
import math
from collections import Counter
from pypdf.generic import ContentStream

def _bez(p0, p1, p2, p3, n=6):
    out = []
    for i in range(1, n + 1):
        t = i / n; a = (1-t)**3; b = 3*(1-t)**2*t; c = 3*(1-t)*t*t; d = t**3
        out.append((a*p0[0]+b*p1[0]+c*p2[0]+d*p3[0], a*p0[1]+b*p1[1]+c*p2[1]+d*p3[1]))
    return out

class Poly:
    def __init__(self, rings, evenodd, order):
        self.rings = rings; self.evenodd = evenodd; self.order = order
        self.colors = Counter(); self.widths = Counter(); self.dashes = Counter(); self.angles = Counter()
        self.nhatch = 0; self.outline_cols = Counter()

def parse_page(reader, pn, layer_name="namjena_poly"):
    pg = reader.pages[pn - 1]
    props = pg["/Resources"]["/Properties"]
    ocname = {k: props[k].get_object().get("/Name") for k in props}
    cs = ContentStream(pg.get_contents(), reader)
    layer_stack = []
    gs_stack = [dict(clip=None, poly=None, rgb=(0, 0, 0), w=1.0, dash=None)]
    path, cur, pending, polys = [], None, None, []
    for operands, op in cs.operations:
        op = op.decode() if isinstance(op, bytes) else op
        if op == "BDC":
            tag, prop = operands
            layer_stack.append(ocname.get(str(prop)) if str(tag) == "/OC" else None); continue
        if op == "BMC": layer_stack.append(None); continue
        if op == "EMC":
            if layer_stack: layer_stack.pop()
            continue
        in_layer = layer_name in layer_stack
        if op == "q": gs_stack.append(dict(gs_stack[-1])); continue
        if op == "Q":
            if len(gs_stack) > 1: gs_stack.pop()
            continue
        gs = gs_stack[-1]
        if op == "RG": gs["rgb"] = tuple(round(float(x), 3) for x in operands); continue
        if op == "w": gs["w"] = round(float(operands[0]), 3); continue
        if op == "d":
            arr = [round(float(x), 2) for x in operands[0]]
            gs["dash"] = tuple(arr) if arr else None; continue
        if op == "m": cur = [(float(operands[0]), float(operands[1]))]; path.append(cur); continue
        if op == "l":
            pt = (float(operands[0]), float(operands[1]))
            if cur is None: cur = [pt]; path.append(cur)
            else: cur.append(pt)
            continue
        if op in ("c", "v", "y") and cur:
            p0 = cur[-1]; v = [float(x) for x in operands]
            if op == "c": cur.extend(_bez(p0, (v[0], v[1]), (v[2], v[3]), (v[4], v[5])))
            elif op == "v": cur.extend(_bez(p0, p0, (v[0], v[1]), (v[2], v[3])))
            else: cur.extend(_bez(p0, (v[0], v[1]), (v[2], v[3]), (v[2], v[3])))
            continue
        if op == "re":
            x, y, w, h = [float(t) for t in operands]
            cur = [(x, y), (x+w, y), (x+w, y+h), (x, y+h)]; path.append(cur); continue
        if op == "h":
            if cur and cur[0] != cur[-1]: cur.append(cur[0])
            continue
        if op in ("W", "W*"): pending = (op == "W*"); continue
        if op == "n":
            if pending is not None and in_layer and path:
                gs["clip"] = [r for r in path if len(r) >= 3]
                p = Poly(gs["clip"], pending, len(polys)); gs["poly"] = p; polys.append(p)
            pending = None; path, cur = [], None; continue
        if op in ("S", "s"):
            if in_layer and path:
                p = gs["poly"] if gs["clip"] is not None else None
                if p is not None and len(path) == 1 and len(path[0]) == 2:
                    a, b = path[0]
                    p.colors[gs["rgb"]] += 1; p.widths[gs["w"]] += 1; p.dashes[gs["dash"]] += 1
                    p.angles[round((math.degrees(math.atan2(b[1]-a[1], b[0]-a[0])) % 180) / 5) * 5 % 180] += 1
                    p.nhatch += 1
            path, cur = [], None; continue
        if op in ("f", "F", "f*", "B", "B*", "b", "b*"):
            path, cur = [], None
    return polys
