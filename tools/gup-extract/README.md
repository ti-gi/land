# gup-extract

Turns Zagreb's GUP 2025 land-use map (a vector PDF) and the plan text into the two files the web map uses:

| Output | Script | From |
|---|---|---|
| `data/gup/zones.geojson` – 4 457 zone polygons, WGS84 | `extract_zones.py` | map sheet 1 *Korištenje i namjena prostora* (PDF, 49 sheets 1:5000) |
| `data/gup/rules.json` – permitted uses per zone code | `build_rules.py` | `source/zagreb_gup_zones_v0.json` (written by hand from the consolidated text) |

Both outputs are committed, so you only need this when the city publishes a new plan edition or the extraction changes.

## Setup

```powershell
cd tools\gup-extract
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
```

## Source documents (not in the repository)

- Map sheet 1, `1_namjena_GUPa_2025_.pdf` (~44 MB vector GeoPDF) – published with the adopted plan
  (*GUP 2025 – donesen plan*) on zagreb.hr.
- Consolidated text: [GUP GZ – pročišćeni tekst 2025](https://www.zagreb.hr/userdocsimages/arhiva/prostorni_planovi/izid%20gup%20grada%20zagreba_2023-/gup_2025_donesen%20plan/GUP%20GZ%20-%20pro%C4%8Di%C5%A1%C4%87eni%20tekst%202025%20(1).pdf)
  – articles 8–23 and 32–33 are the basis of `source/zagreb_gup_zones_v0.json`.

Put downloaded PDFs in `data/gup/source/` (git-ignored).

## Zones

```powershell
python extract_zones.py --pdf ..\..\data\gup\source\1_namjena_GUPa_2025_.pdf
```

About 2 minutes and ~0.7 GB RAM. Steps, with intermediates cached in `work/` (git-ignored):

1. **sheets** – for each sheet (pages 2–50): the zone clip-paths in the `namjena_poly` layer, the hatch colour inside each,
   the zone labels (Arial Bold 8–10 pt), and an affine transform PDF → HTRS96/TM (EPSG:3765) from the GeoPDF viewport
   `GUP2024` (`/VP` → `/Measure` GPTS/LPTS; residual ≈ 0.1 m).
2. **polygons** – polygons in EPSG:3765 with the labels inside them. Checks that labels match the polygon's colour group
   and stops if most don't (that means flipped label coordinates).
3. **zones** – zone code per polygon: its label → else the code of touching neighbours of the same colour group →
   else `S` for residential, otherwise `?group` (`code_source` records which). Polygons are dissolved per code across
   sheet seams. Writes `out/zagreb_gup2025_namjena_EPSG3765.geojson` and `_WGS84.geojson`.
4. **web** – WGS84 with 6 decimals (~0.1 m) and numeric `id`s → `data/gup/zones.geojson` (~13 MB).
   `--simplify 0.5` gives ~4 MB.

Re-run from a later step without reading the PDF again: `python extract_zones.py --from-step 3`.

Expected result for the 2025 edition: 4 457 polygons, 191.0 km²; code source ≈ 4 900 label, 385 neighbour,
67 unresolved, 5 group default, 54 several labels.

## Rules

```powershell
python build_rules.py
```

Restores Croatian diacritics in the hand-written ASCII source and writes `data/gup/rules.json` (29 zone codes,
article references, buildability classes, uses allowed everywhere). When the plan text changes, edit
`source/zagreb_gup_zones_v0.json` and rebuild.

## Other scripts

- `render_pdf.py` – printable A3 PDF (overview, centre close-up, area per code) from `out/…EPSG3765.geojson`.
- `inspect_layers.py <pdf> <page> …` – lists the PDF's layers, colours and text on given sheets; start here if a new
  edition changes layer names, colours or fonts.
- `gupparse.py` – the PDF content-stream parser used by step 1.
