"""
GUP 2025 land-use map (vector PDF) → zone polygons as GeoJSON.

    python extract_zones.py --pdf path/to/1_namjena_GUPa_2025_.pdf

Steps (intermediate results are cached in ./work, so a later step can be re-run with --from-step):
  1 sheets    read every map sheet: zone clip-polygons, hatch colours, zone labels, georeferencing  → work/raw.pkl
  2 polygons  polygons in HTRS96/TM (EPSG:3765) with the labels that fall inside them               → work/recs.pkl
  3 zones     zone code per polygon (label → neighbour → group default), dissolve across sheets    → out/*_EPSG3765.geojson, out/*_WGS84.geojson
  4 web       WGS84 copy (6 decimals ≈ 0.1 m) with numeric ids for the web map                             → ../../data/gup/zones.geojson
"""
import argparse
import json
import math
import pickle
import time
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np
import pdfplumber
import shapely
from pypdf import PdfReader
from pyproj import Transformer
from shapely import STRtree, set_precision
from shapely.geometry import Point, Polygon, mapping
from shapely.ops import unary_union
from shapely.validation import make_valid

import gupparse

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
WORK = HERE / "work"
OUT = HERE / "out"
WEB_ZONES = REPO / "data" / "gup" / "zones.geojson"

FIRST_SHEET, LAST_SHEET = 2, 50          # page 1 is the title/legend sheet
VIEWPORT = "GUP2024"                     # name of the georeferenced viewport (/VP) on each sheet

# Hatch stroke colour (RGB 0–1) → zone group, and the zone codes allowed in each group
GROUP = {(1.0, 1.0, 0.0): "stambena", (1.0, 0.498, 0.0): "mjesovita", (1.0, 0.0, 0.0): "javna_drustvena",
         (1.0, 0.0, 1.0): "gospodarska", (0.0, 1.0, 0.0): "zelene_sport", (0.0, 1.0, 1.0): "vode"}
VALID = {"stambena": {"S"}, "mjesovita": {"M", "M0", "M1", "M2", "TR"},
         "javna_drustvena": {"D", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8"},
         "gospodarska": {"K1", "K2", "I", "G", "T", "N"},
         "zelene_sport": {"Z", "Z1", "Z2", "Z3", "Z4", "R1", "R2", "R", "ZI", "IS"},
         "vode": {"V", "V1", "V2", "V3", "IS"}}
FIX = {"ZI": "ZIS"}                      # label split by the PDF
NAMES = {
    "S": "Stambena namjena", "M": "Mješovita namjena", "M0": "Mješovita namjena gradskog središta",
    "M1": "Mješovita - pretežito stambena", "M2": "Mješovita - pretežito poslovna", "TR": "Tržnica",
    "D": "Javna i društvena (sve)", "D1": "Javna i društvena - upravna", "D2": "- socijalna", "D3": "- zdravstvena",
    "D4": "- predškolska", "D5": "- školska", "D6": "- visoko učilište i znanost", "D7": "- kulturna", "D8": "- vjerska",
    "I": "Gospodarska - proizvodna", "K1": "Gospodarska - poslovna", "K2": "Gospodarska - trgovački kompleksi",
    "T": "Gospodarska - ugostiteljsko-turistička", "G": "Gospodarska (sve)", "N": "Posebna namjena",
    "R1": "Sport i rekreacija - sport s gradnjom", "R2": "Sport i rekreacija - sport bez gradnje", "R": "Sport i rekreacija",
    "Z": "Zaštitne zelene površine", "Z1": "Javni park", "Z2": "Gradske park-šume", "Z3": "Tematski park",
    "Z4": "Javne gradske površine - tematske zone", "ZIS": "Površine zelenog i infrastrukturnog sustava",
    "IS": "Površine infrastrukturnih sustava", "V": "Vode i vodna dobra", "V1": "Površine pod vodom",
    "V2": "Površine povremeno pod vodom", "V3": "Vodotok sa zaštitnim koridorom",
}
META = dict(
    source="Grad Zagreb, GUP izmjene i dopune 2025, sheet 1 Korištenje i namjena prostora 1:5000 (vector PDF, 49 sheets)",
    method="clip-path polygons from PDF georeferenced via embedded HTRS96/TM viewports; zone code from sheet labels, colour group as check",
)


# ---------------------------------------------------------------- step 1
def georef(reader, to3765, pn):
    """Affine transform PDF points → EPSG:3765 from the sheet's GeoPDF viewport (GPTS/LPTS)."""
    pg = reader.pages[pn - 1]
    for vp in pg["/VP"]:
        vp = vp.get_object()
        if vp["/Name"] != VIEWPORT:
            continue
        bx0, by0, bx1, by1 = [float(x) for x in vp["/BBox"]]
        m = vp["/Measure"].get_object()
        g = [float(x) for x in m["/GPTS"]]
        l = [float(x) for x in m["/LPTS"]]
        pts = []
        for i in range(0, 8, 2):
            lat, lon = g[i], g[i + 1]
            u, v = l[i], l[i + 1]
            E, N = to3765.transform(lon, lat)
            pts.append((bx0 + u * (bx1 - bx0), by0 + v * (by1 - by0), E, N))
        A = np.array([[p[0], p[1], 1] for p in pts])
        B = np.array([[p[2], p[3]] for p in pts])
        coef, *_ = np.linalg.lstsq(A, B, rcond=None)
        return coef, float(np.abs(A @ coef - B).max())
    raise KeyError(f"sheet {pn}: no {VIEWPORT} viewport")


def labels_for(plpage):
    """Zone code labels (Arial bold 8–10 pt) as (text, x, y) in PDF coordinates (origin bottom-left)."""
    chars = [c for c in plpage.chars if "Arial-BoldMT" in c["fontname"] and 8.0 < c["size"] < 10]
    chars.sort(key=lambda c: (round(c["y0"]), c["x0"]))
    words, cur = [], []
    for c in chars:
        if cur and abs(c["y0"] - cur[-1]["y0"]) < 1.5 and 0 <= c["x0"] - cur[-1]["x1"] < 1.5:
            cur.append(c)
        else:
            if cur:
                words.append(cur)
            cur = [c]
    if cur:
        words.append(cur)
    # pdfplumber's char y0/y1 are already bottom-up (PDF space); "top"/"bottom" would be top-down.
    # Converting only once here matters: flipping twice puts every label in the wrong polygon.
    return [("".join(c["text"] for c in w),
             (min(c["x0"] for c in w) + max(c["x1"] for c in w)) / 2,
             (min(c["y0"] for c in w) + max(c["y1"] for c in w)) / 2) for w in words]


def ring_key(p):
    return tuple((len(rg), round(rg[0][0], 1), round(rg[0][1], 1)) for rg in p.rings)


def step_sheets(pdf_path):
    reader = PdfReader(str(pdf_path))
    to3765 = Transformer.from_crs(4258, 3765, always_xy=True)
    raw_file = WORK / "raw.pkl"
    data = pickle.load(open(raw_file, "rb")) if raw_file.exists() else {}
    t0 = time.time()
    with pdfplumber.open(str(pdf_path)) as pdf:
        for pn in range(FIRST_SHEET, min(LAST_SHEET, len(reader.pages)) + 1):
            if pn in data:
                continue
            merged = {}
            for p in gupparse.parse_page(reader, pn):
                if p.nhatch == 0:
                    continue
                m = merged.setdefault(ring_key(p), dict(rings=p.rings, evenodd=p.evenodd, colors={}, widths={},
                                                        angles={}, dashes={}, nhatch=0))
                for name in ("colors", "widths", "angles", "dashes"):
                    for c, n in getattr(p, name).items():
                        m[name][c] = m[name].get(c, 0) + n
                m["nhatch"] += p.nhatch
            coef, res = georef(reader, to3765, pn)
            page = pdf.pages[pn - 1]
            labels = labels_for(page)
            page.close()                     # pdfplumber keeps every parsed page otherwise (~6 GB for 49 sheets)
            data[pn] = dict(polys=list(merged.values()), labels=labels, coef=coef, res=res, label_origin="bottom-left")
            print(f"  sheet {pn:2}: {len(merged):4} polygons, {len(data[pn]['labels']):4} labels, "
                  f"georef residual {res:.2f} m  ({time.time() - t0:.0f} s)", flush=True)
            pickle.dump(data, open(raw_file, "wb"))
    return data


# ---------------------------------------------------------------- step 2
def tf(coef, pt):
    return (pt[0] * coef[0, 0] + pt[1] * coef[1, 0] + coef[2, 0], pt[0] * coef[0, 1] + pt[1] * coef[1, 1] + coef[2, 1])


def shape_of(rings, coef):
    g = None
    for r in rings:
        if len(r) < 4:
            continue
        p = Polygon([tf(coef, q) for q in r])
        if not p.is_valid:
            p = make_valid(p)
        g = p if g is None else g.symmetric_difference(p)
    return g


def step_polygons():
    d = pickle.load(open(WORK / "raw.pkl", "rb"))
    recs = []
    for pn, v in d.items():
        if v.get("label_origin") != "bottom-left":
            raise SystemExit("work/raw.pkl was written by an older version (labels top-down). Delete it and run step 1 again.")
        labs = [(t, Point(tf(v["coef"], (x, y)))) for t, x, y in v["labels"]]
        for p in v["polys"]:
            g = shape_of(p["rings"], v["coef"])
            if g is None or g.is_empty or g.area < 1:
                continue
            col = max(p["colors"], key=p["colors"].get)
            inside = [t for t, pt in labs if g.contains(pt)]
            recs.append(dict(sheet=pn, geom=g, color=col, labels=inside, area=g.area, nh=p["nhatch"]))
    print(f"  {len(recs)} polygons, {sum(r['area'] for r in recs) / 1e6:.1f} km²")
    # Sanity check: labels should belong to the polygon's colour group
    ok = sum(1 for r in recs if r["color"] in GROUP for l in r["labels"] if l in VALID[GROUP[r["color"]]])
    bad = sum(1 for r in recs if r["color"] in GROUP for l in r["labels"] if l not in VALID[GROUP[r["color"]]])
    print(f"  labels matching their polygon's colour group: {ok}, not matching: {bad}")
    if bad > ok:
        raise SystemExit("Most labels fall in polygons of the wrong colour group – the label coordinates are flipped.")
    pickle.dump(recs, open(WORK / "recs.pkl", "wb"))


# ---------------------------------------------------------------- step 3
def step_zones():
    recs = pickle.load(open(WORK / "recs.pkl", "rb"))
    rows = []
    for r in recs:
        grp = GROUP.get(r["color"])
        if grp is None or r["geom"].is_empty:
            continue                                         # black = frames / legend
        cands = [l for l in r["labels"] if l in VALID[grp]]
        code = how = None
        if cands:
            c = Counter(cands).most_common()
            code, how = c[0][0], ("label" if len(c) == 1 else "label_multi")
        rows.append(dict(geom=r["geom"].buffer(0), grp=grp, code=FIX.get(code, code), how=how, sheet=r["sheet"]))

    # Unlabelled polygons: take the code of touching neighbours of the same group (weighted by shared length)
    tree = STRtree([x["geom"] for x in rows])
    for _ in range(3):
        for x in rows:
            if x["code"]:
                continue
            votes = Counter()
            for j in tree.query(x["geom"].buffer(1.0)):
                y = rows[j]
                if y is x or y["grp"] != x["grp"] or not y["code"]:
                    continue
                votes[y["code"]] += x["geom"].buffer(1.0).intersection(y["geom"]).area
            if votes:
                x["code"], x["how"] = votes.most_common(1)[0][0], "inferred_neighbour"
    for x in rows:
        if not x["code"]:
            d = {"stambena": "S"}.get(x["grp"])
            x["code"], x["how"] = (d, "group_default") if d else (f"?{x['grp']}", "unresolved")
    print("  code source:", dict(Counter(x["how"] for x in rows)))

    # Dissolve per code across sheet seams
    by = defaultdict(list)
    for x in rows:
        by[x["code"]].append(x)
    tw = Transformer.from_crs(3765, 4326, always_xy=True)
    f3765, f4326 = [], []
    for code, xs in by.items():
        g = set_precision(unary_union([x["geom"] for x in xs]).buffer(0.3).buffer(-0.3), 0.01)
        for p in (list(g.geoms) if g.geom_type == "MultiPolygon" else [g]):
            if p.is_empty or p.area < 5:
                continue
            how = Counter(x["how"] for x in xs if x["geom"].intersects(p)).most_common(1)[0][0]
            props = dict(code=code, name_hr=NAMES.get(code, code), group=xs[0]["grp"], code_source=how, area_m2=round(p.area, 1))
            f3765.append(dict(type="Feature", properties=props, geometry=mapping(p)))
            q = shapely.transform(p, lambda a: np.column_stack(tw.transform(a[:, 0], a[:, 1])))
            f4326.append(dict(type="Feature", properties=props, geometry=mapping(set_precision(q, 1e-7))))
    OUT.mkdir(exist_ok=True)
    json.dump(dict(type="FeatureCollection", name="gup_2025_namjena",
                   crs=dict(type="name", properties=dict(name="urn:ogc:def:crs:EPSG::3765")), metadata=META, features=f3765),
              open(OUT / "zagreb_gup2025_namjena_EPSG3765.geojson", "w", encoding="utf-8"), ensure_ascii=False)
    json.dump(dict(type="FeatureCollection", name="gup_2025_namjena", metadata=META, features=f4326),
              open(OUT / "zagreb_gup2025_namjena_WGS84.geojson", "w", encoding="utf-8"), ensure_ascii=False)
    print(f"  {len(f3765)} zone polygons, {sum(f['properties']['area_m2'] for f in f3765) / 1e6:.1f} km² → {OUT}")


# ---------------------------------------------------------------- step 4
def step_web(dest=WEB_ZONES, tolerance_m=0.0):
    gj = json.load(open(OUT / "zagreb_gup2025_namjena_EPSG3765.geojson", encoding="utf-8"))
    tw = Transformer.from_crs(3765, 4326, always_xy=True)
    out, fid = [], 0
    for f in gj["features"]:
        g = shapely.geometry.shape(f["geometry"])
        if tolerance_m > 0:
            g = g.simplify(tolerance_m, preserve_topology=True)
        if g.is_empty:
            continue
        g = set_precision(shapely.transform(g, lambda a: np.column_stack(tw.transform(a[:, 0], a[:, 1]))), 1e-6)
        if g.is_empty:
            continue
        fid += 1
        p = dict(f["properties"], id=fid, area_m2=round(f["properties"]["area_m2"]))
        out.append(dict(type="Feature", id=fid, properties=p, geometry=mapping(g)))
    s = json.dumps(dict(type="FeatureCollection", name="gup_2025_namjena", metadata=gj.get("metadata"), features=out),
                   ensure_ascii=False, separators=(",", ":"))
    Path(dest).parent.mkdir(parents=True, exist_ok=True)
    Path(dest).write_text(s, encoding="utf-8")
    print(f"  {len(out)} features, {len(s) / 1e6:.1f} MB → {dest}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pdf", type=Path, help="the GUP 2025 land-use PDF (needed for step 1)")
    ap.add_argument("--from-step", type=int, default=1, choices=[1, 2, 3, 4])
    ap.add_argument("--web-out", type=Path, default=WEB_ZONES, help=f"web map zones file (default {WEB_ZONES.relative_to(REPO)})")
    ap.add_argument("--simplify", type=float, default=0.0, metavar="METRES",
                    help="simplify the web copy (e.g. 0.5 gives ~4 MB instead of ~26 MB); default 0 = full detail")
    a = ap.parse_args()
    WORK.mkdir(exist_ok=True)
    if a.from_step <= 1:
        if not a.pdf:
            ap.error("--pdf is required for step 1")
        print("Step 1: reading sheets"); step_sheets(a.pdf)
    if a.from_step <= 2:
        print("Step 2: polygons"); step_polygons()
    if a.from_step <= 3:
        print("Step 3: zone codes"); step_zones()
    print("Step 4: web copy"); step_web(a.web_out, a.simplify)


if __name__ == "__main__":
    main()
