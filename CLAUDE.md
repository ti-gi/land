# CLAUDE.md – land

Personal research project: what may be built where in the City of Zagreb, and which building land is for sale.
Owner: Tomislav. Working language with the owner: English; data and UI labels are partly Croatian.

## Layout

| Path | What | Stack |
|---|---|---|
| `src/DataFetch/` | Collects land-for-sale listings → `data/listings/*.json` | .NET 8 console app, Playwright (Njuškalo only) |
| `src/web/` | Web map: GUP zones + rules + listings | Node 18+, Express, MapLibre GL JS v6 (ESM), vanilla JS |
| `tools/gup-extract/` | GUP 2025 PDF → `data/gup/zones.geojson`, plan text → `data/gup/rules.json` | Python 3.10+, pypdf, pdfplumber, shapely, pyproj |
| `tests/DataFetch.Tests/` | Self-checking tests with in-memory fakes of both sites | .NET 8, no packages |
| `tests/fixtures/` | Synthetic sample data for the fakes | |
| `data/gup/` | zones + rules (committed) | |
| `data/listings/`, `data/datafetch/` | collected data, snapshots, browser profile (**git-ignored**) | |
| `docs/data-contract.md` | Every shared file format and the web API – **read before changing any field** | |
| `docs/decisions.md` | Why things are as they are – add an entry for new decisions | |

## Commands

```powershell
# DataFetch (from src/DataFetch; paths in appsettings.json are relative to it)
dotnet build
dotnet run -- --probe                 # nekretnine.hr: first page only, writes a snapshot
dotnet run                            # nekretnine.hr: full scan → data/listings/listings-nekretnine.json
dotnet run -- --source njuskalo --probe
dotnet run -- --install-browser       # once, Njuškalo only

# Tests (must pass before every commit that touches DataFetch)
cd tests/DataFetch.Tests; dotnet run

# Web map (from src/web)
npm install; npm start                # http://localhost:3000, Google key in src/web/.env (optional)

# GUP extraction (from tools/gup-extract, venv with requirements.txt) – only for a new plan edition
python extract_zones.py --pdf ../../data/gup/source/1_namjena_GUPa_2025_.pdf
python build_rules.py
```

VS solution: `src/DataFetch/DataFetch.sln` (DataFetch + DataFetch.Tests).

## Rules – do not drift

1. **No bot-protection evasion.** No proxies/IP rotation, no fake browser headers or fingerprints, no stealth
   plugins, no CAPTCHA solving, no automatic retries after a block. A block, error, non-JSON answer or CAPTCHA
   **stops the run**; Njuškalo asks the person at the keyboard. Keep the honest `User-Agent: DataFetch/1.0 (personal research)`.
2. **Collection runs only on the owner's computer, started by hand.** Don't add schedulers, cloud jobs or server-side scraping.
3. **No personal data.** Never store seller/agent names or phone numbers; mask phones/e-mails in free text
   (`NjuskaloParser.MaskContacts`); strip `supervisor`, `phones`, `agentPhone`, `email` from raw API records
   (`NekretnineCollector.Sanitize`). Agency (business) names are fine. Tests check this – keep them.
4. **Never commit** `.env`, anything under `data/listings/` or `data/datafetch/`, real page snapshots or real API
   responses. Test fixtures are synthetic only.
5. **Collected listings are not republished.** Anything public-facing needs the sites' permission or a licensed feed.
6. **DataFetch stores raw data only** – GUP matching belongs in `src/web` (decision 8).
7. **Shared formats change together**: producer + consumer + `docs/data-contract.md` + tests in one commit.
   DataFetch writes camelCase JSON and omits null fields.
8. Show GUP information as **not legally binding**; the official plan and any UPU decide.

## Facts that cost time to find out

- **nekretnine.hr API**: `https://www.nekretnine.hr/api-next/search-list/listings/?fkRegione=HR_3&idProvincia=HR_10&idComune=743&idNazione=HR&idContratto=1&idCategoria=24&__lang=hr&criterio=data&ordine=desc&path=%2Fprodaja-zemljista%2Fzagreb%2F&pag=N`
  – 25 per page, `count`/`maxPages` in the answer, newest first. No CORS (server-side only), DataDome bot protection.
  - Typology **261 = Građevinsko zemljište** (269 ≈ agricultural). Adding `idTipologia[]=261` **breaks the
    location filter** (returns ~7 700 mixed ads) → filter in the app.
  - No publication date in the API; descriptions are mostly empty (only promoted ads, ≤ 600 chars).
  - `location.marker`: `marker` = real pin, `only_area` = microzone centre, `no_map` = hidden. Many ads share one
    pin. Treat non-`marker` coordinates as approximate (the GUP zone there is not the plot's zone).
  - `ga4features` are Italian labels (`allaccio elettricità/acqua/fogne/gas`) → mapped to Croatian.
- **Njuškalo**: Grad Zagreb `geo[locationIds]=1153` (1170 = Zagrebačka županija), građevinsko `landTypeId=235`,
  newest `sort=new`, paging `page=N`. Results are `ul.listings > li.listing` – the "latest ads" sidebar must not be
  read. Ad details are `dt`/`dd` pairs; coordinates in page JSON as `latitude`/`longitude` + `isApproximateLocationOnMap`.
- **GUP 2025 PDF**: 49 map sheets (pages 2–50), zones are clip paths in layer `namjena_poly`, hatch stroke colour =
  zone group, labels Arial-Bold 8–10 pt. Georeference: `/VP` named `GUP2024`, `/Measure` GPTS/LPTS → HTRS96/TM
  **EPSG:3765**. pdfplumber `y0/y1` are bottom-up – convert label y exactly once. Call `page.close()` per sheet
  (otherwise ~6 GB RAM).
- **Google APIs used by the web map** (one browser key, `src/web/.env`): Map Tiles API (backgrounds, via MapLibre),
  Maps JavaScript API + Places API (New) + Geocoding API (address search, `public/google-search.js`).
  Autocomplete uses a session token that ends when a suggestion is picked (cheaper billing).
- **MapLibre GL JS v6 is ESM-only**: `import * as maplibregl from '/vendor/maplibre-gl/maplibre-gl.mjs'`.
  Google Map Tiles: create a session per map type; terrain needs `layerTypes: ["layerRoadmap"]`; show the
  attribution from `/tile/v1/viewport`.
- `.NET 8` ends support **10 Nov 2026**; VS 2022 can't target .NET 10. Upgrade = VS 2026 + `net10.0`, no code changes.
- `ProjectReference` to DataFetch would pull in Playwright; the test project compiles DataFetch's sources directly instead.

## Working conventions

- Test changes to collectors against the fakes in `tests/` first; touch the real sites only with `--probe` or a
  normal run started by the owner. When a site changes, update the fake from a `--probe` snapshot, then the parser.
- Keep both apps runnable without a key or internet where possible (plain background without a Google key, empty listings layer).
- Small, focused commits; message says what and why. Don't push or rewrite history unless asked.
- After changing the web app, check it in a browser (zones render, click a zone, click a listing pin).
