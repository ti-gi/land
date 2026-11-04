# Decisions

Why things are the way they are. Newest at the bottom; when a decision changes, add a new entry that replaces the
old one rather than editing history.

### 1. Zones come from the 2025 map PDF, not from a GIS download (Oct 2026)
The 2025 amendments (*izmjene i dopune 2025*) were not published as GIS data. The land-use sheet is a vector GeoPDF
with an embedded HTRS96/TM georeference, so the zone outlines can be read exactly (residual ≈ 0.1 m) instead of
being traced. Labels on the sheets give the zone codes; ~8 % of polygons have no label and take the code of a
touching neighbour (`code_source` says so). → `tools/gup-extract`.

### 2. Zone rules are hand-extracted from the consolidated text (Oct 2026)
Permitted uses per zone (articles 8–23, 32–33) were turned into JSON by reading the *pročišćeni tekst*, not by
automated parsing – the text is prose with exceptions. Building parameters (height, coverage, kis) from the
urban-rules sheet 4a are **not** included yet. Everything shown is marked "not legally binding".

### 3. Web map: MapLibre GL JS + small Express server (Oct 2026)
Open source, no vendor lock-in, vanilla JS (no build step), renders all 4 457 zone polygons at full detail smoothly.
MapLibre v6 ships only as an ES module. Google Map Tiles API for Map/Satellite/Terrain backgrounds (key in `.env`,
browser key restricted by HTTP referrer), OpenStreetMap (OpenFreeMap) as the keyless fallback.

### 4. Collecting listings is a separate local app, started by hand (Oct 2026)
DataFetch runs on the owner's computer, from a home connection, when the owner starts it – no scheduler, no server.
Collection from a data-centre server would likely be blocked and would look like what it is: automated scraping.
The planned public web app (with a .NET backend) must not depend on scraping; see 9.

### 5. Njuškalo: visible browser at human pace, filters in the URL (Oct 2026)
Playwright drives a real, visible Chromium with its own profile; random 4–8 s pause before each page load; at most
100 new ads per run. The search filter (Grad Zagreb `geo[locationIds]=1153`, građevinsko `landTypeId=235`,
`sort=new`) is in the URL – the app does not click the dropdowns. A CAPTCHA, access-denied or unknown page **stops**
the run and asks the person at the keyboard.

### 6. nekretnine.hr is the primary source, read through its listing API (29 Oct 2026)
The site's own `/api-next/search-list/listings/` returns all land for sale in Zagreb (≈ 45 pages × 25) as JSON,
works without cookies, and has coordinates and utilities. A run reads every page with **no pause** (owner's decision;
`DelayMs` exists if that changes). Land type is filtered in the app (typology 261) because the API's
`idTipologia[]` parameter breaks the location filter. The site uses DataDome: any non-JSON answer, error or bot
check stops the run at once – **no retries**. The app sends an honest `User-Agent: DataFetch/1.0 (personal research)`.

### 7. No bot-protection evasion – ever (Oct 2026)
No residential or rotating proxies, no fake browser fingerprints or headers, no CAPTCHA solving, no retry loops
around blocks. If a site blocks the app, the answer is to stop, slow down, or get the data with permission.

### 8. Raw data only in DataFetch; no GUP matching there (Oct 2026)
DataFetch stores what the sites publish. Matching a listing to a GUP zone happens where the zones are: the web
server (`zone` in `/api/listings`, `/api/zone`). Keeps DataFetch simple and the matching re-computable.

### 9. Personal data and republishing (Oct 2026)
Seller and agent names and phone numbers are never stored; phones and e-mails in descriptions are masked; the
API's `supervisor`/`phones` blocks are stripped. Agency names (businesses) are kept. Snapshots from `--probe` are
full page copies and may contain personal data – delete them after use. Collected listings are for personal
research: showing them publicly needs the sites' permission or a licensed data feed.

### 10. JSON files instead of a database (Oct 2026)
A few thousand listings fit in one file; atomic write (temp file + rename) after every page. Revisit when the
.NET backend arrives or history grows large.

### 11. One repository, data outside git (1 Nov 2026)
`land` holds all parts because they share file formats (`docs/data-contract.md`). Collected data, snapshots,
the browser profile and `.env` are git-ignored. The generated `data/gup` files are committed: they change only
with a new plan edition and the web map needs them to start.

### 12. .NET 8 for now (Oct 2026)
Chosen so the project opens in Visual Studio 2022. .NET 8 support ends **10 November 2026**; moving to
`net10.0` needs Visual Studio 2026 and no code changes.

### 13. Google is the only background map (4 Nov 2026)
OpenStreetMap was removed from the background choices at the owner's request. Backgrounds are Google Map/Satellite/
Terrain through the **Map Tiles API** (must be enabled in the key's Google Cloud project) or none. Without a key, or
if Google fails, the zones show on a plain background. MapLibre stays the map engine (replaces part of decision 3).

### 14. Address search uses Google (4 Nov 2026)
Replaces OpenStreetMap Nominatim at the owner's request. Suggestions while typing come from Places API (New)
autocomplete (billed per session, which ends when a suggestion is picked); Enter on typed text uses Geocoding.
Both run in the browser through the Maps JavaScript API with the same referrer-restricted key – Google's web-service
endpoints don't accept referrer-restricted keys, so there is no server-side proxy. Results are limited to Zagreb.

### 15. DataFetch and the web map are separate; listings are copied by hand (4 Nov 2026)
DataFetch writes only to the repository's `data/listings/` and never into `src/web`. The map reads
`src/web/data/listings-nekretnine.json` (git-ignored); the owner copies a new DataFetch result there when they want
the map updated. Keeps the two apps independent – the map shows exactly the file the owner chose.
