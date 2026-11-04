# Zagreb GUP 2025 – land-use web map

Part of the `land` repository – see the root README. Interactive map of the zones in **Generalni urbanistički plan grada Zagreba, izmjene i dopune 2025.**
(sheet 1 *Korištenje i namjena prostora*), built with **MapLibre GL JS** and a small **Node.js / Express** server.

## Run it

Requires [Node.js](https://nodejs.org) 18 or newer.

```bash
cd src/web
npm install
npm start
```

Then open <http://localhost:3000>.
Use `npm run dev` to restart automatically when you edit `server.js`, or set another port with `PORT=8080 npm start`
(on Windows PowerShell: `$env:PORT=8080; npm start`).

## Google Maps background (Map / Satellite / Terrain)

The background switcher (top right) offers Google roadmap, satellite and terrain via the
[Google Maps Platform Map Tiles API](https://developers.google.com/maps/documentation/tile/2d-tiles-overview),
plus no background.

1. In [Google Cloud Console](https://console.cloud.google.com/), create or pick a project, link a billing account,
   and enable **Map Tiles API** (backgrounds) plus **Maps JavaScript API**, **Places API (New)** and
   **Geocoding API** (address search).
2. Create an API key. Restrict it:
   - *Application restrictions* → **Websites (HTTP referrers)**: `http://localhost:3000/*` (add your real domain later)
   - *API restrictions* → those four APIs only
3. Copy `.env.example` to `.env` and set `GOOGLE_MAPS_API_KEY=your-key`, then restart `npm start`.

Without a key the Google buttons are greyed out and the zones show on a plain background.
The key is used by the browser to load tiles, so it is visible to visitors – that is why the restrictions matter.
Google bills Map Tiles API usage per tile request (with a free monthly allowance); check
[current pricing](https://developers.google.com/maps/billing-and-pricing/pricing) and set a budget alert.
Google's terms require the "Google Maps" attribution and copyright text shown bottom right – don't hide it.
For production, replace the text mark in `public/basemaps.js` (`GoogleAttribution`) with the official
Google Maps logo from Google's brand resources.

## Land for sale (DataFetch listings)

DataFetch writes `data/listings/listings-nekretnine.json` (repository root), which this server reads directly
(another file: set `LISTINGS_FILE` in `.env`). The map then shows
building land for sale in Zagreb as dark pins (one colour – the zone colours underneath carry the meaning):

- **filled pin** – the advertiser placed an exact pin;
- **ring** – approximate: only the neighbourhood centre (or the address is hidden), so the GUP zone at that point is
  *not* necessarily the plot's zone;
- **bigger pin, thick white border** – several ads share the same pin.

The "Land for sale" box in the left panel switches the layer, approximate pins and ads no longer
listed on/off. Click a pin for the listing(s) there, a link to the ad, and the GUP zone and its rules at that point.
The server re-reads the file when it changes – after a new DataFetch run just reload the page.
`GET /api/listings` returns the pins as GeoJSON.

## What it does

- Colours all 4,457 zone polygons (full detail, ~0.1 m) by GUP code (S, M1, M2, D5, Z1, …).
- Zones legend on the right, below the background switcher: colours and areas per code, grouped by family
  (read-only), a "Show zones" switch, opacity, and collapsible (the choice is remembered in the browser).
  When open it runs down to the bottom of the map.
- Zone and listing details open on the left, below "Land for sale".
- Click anywhere (or search an address) to see the zone, its area, and the permitted uses,
  limits and prohibitions from the GUP text, with the article reference.
- Coordinates shown in WGS84 and HTRS96/TM (EPSG:3765).
- Background: Google Maps (map / satellite / terrain, needs an API key) or none.
- Address search: Google – suggestions while typing (Places API (New)), Enter looks up the typed address
  (Geocoding API); limited to the City of Zagreb. Disabled without a key.
  Both need internet; without it the zones still display on a plain background.

## API

| Endpoint | Returns |
|---|---|
| `GET /api/zone?lon=15.977&lat=45.813` | Zones at a point, with their rules and HTRS96/TM coordinates |
| `GET /api/stats` | Polygon count and area per zone code |
| `GET /api/rules` | All extracted zone rules |
| `GET /data/zones.geojson` | The zone polygons (WGS84) |

## Files

```
server.js              Express server, .env loading, point-lookup API
.env.example           Template for your Google Maps API key
public/index.html      Page layout
public/app.js          Map logic (vanilla JS)
public/basemaps.js     Background maps: Google Map Tiles API, none
public/zone-styles.js  Zone colours and names
public/style.css       Styling
../../data/gup/zones.geojson   Zone polygons (WGS84, ~13 MB, ~3 MB gzipped download) – made by tools/gup-extract
../../data/gup/rules.json      Permitted uses per zone code – made by tools/gup-extract
../../data/listings/           Listings collected by DataFetch (git-ignored)
```

## Data notes

- Polygons were extracted (`tools/gup-extract`) from the vector PDF of the 2025 map sheets (georeferenced, HTRS96/TM).
  `code_source` tells how each code was assigned: `label` (read from the map), `label_multi`,
  `inferred_neighbour` (no label – taken from a neighbouring zone) or `unresolved` (codes starting with `?`).
- Streets are not zoned in the plan, so they appear as gaps.
- Rules cover GUP articles 8–23 and 32–33. Building parameters (height, coverage, kis) come from the
  urban-rules sheet (4a) and are not included yet.
- Not legally binding. Always check the official plan and any detailed plan (UPU) for a location.
