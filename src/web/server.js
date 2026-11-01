// Zagreb GUP 2025 map – small Express server
// Serves the web page, the zone data and a point-lookup API.
import express from 'express';
import compression from 'compression';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import proj4 from 'proj4';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

// Read settings from .env (KEY=value lines) without overriding real environment variables
const envFile = path.join(__dirname, '.env');
if (fs.existsSync(envFile)) {
  for (const line of fs.readFileSync(envFile, 'utf8').split(/\r?\n/)) {
    const m = line.match(/^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$/);
    if (m && !line.trimStart().startsWith('#') && process.env[m[1]] === undefined) {
      process.env[m[1]] = m[2].replace(/^(['"])(.*)\1$/, '$2');
    }
  }
}

const PORT = process.env.PORT || 3000;
const GOOGLE_MAPS_API_KEY = process.env.GOOGLE_MAPS_API_KEY || '';
const GOOGLE_TILE_BASE = process.env.GOOGLE_TILE_BASE || 'https://tile.googleapis.com';
// Data lives in the repository's data/ folder: gup/ (committed: zones + rules) and listings/ (DataFetch output, git-ignored)
const REPO_DATA = path.resolve(__dirname, '../../data');
const DATA_DIR = path.resolve(__dirname, process.env.GUP_DATA_DIR || path.join(REPO_DATA, 'gup'));

// HTRS96/TM (EPSG:3765) – official Croatian projection
proj4.defs('EPSG:3765', '+proj=tmerc +lat_0=0 +lon_0=16.5 +k=0.9999 +x_0=500000 +y_0=0 +ellps=GRS80 +towgs84=0,0,0,0,0,0,0 +units=m +no_defs');

// ---------------------------------------------------------------------------
// Load data once at startup
// ---------------------------------------------------------------------------
const zones = JSON.parse(fs.readFileSync(path.join(DATA_DIR, 'zones.geojson'), 'utf8'));
const rules = JSON.parse(fs.readFileSync(path.join(DATA_DIR, 'rules.json'), 'utf8'));

// Bounding box per feature for fast point lookups
const index = zones.features.map((f) => {
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  const polys = f.geometry.type === 'Polygon' ? [f.geometry.coordinates] : f.geometry.coordinates;
  for (const poly of polys) for (const ring of poly) for (const [x, y] of ring) {
    if (x < minX) minX = x; if (x > maxX) maxX = x;
    if (y < minY) minY = y; if (y > maxY) maxY = y;
  }
  return { f, polys, bbox: [minX, minY, maxX, maxY] };
});

// Even-odd ray casting; holes are handled because every ring toggles "inside"
function pointInPolygon(x, y, poly) {
  let inside = false;
  for (const ring of poly) {
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
      const [xi, yi] = ring[i], [xj, yj] = ring[j];
      if ((yi > y) !== (yj > y) && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) inside = !inside;
    }
  }
  return inside;
}

function zonesAt(lon, lat) {
  const hits = [];
  for (const it of index) {
    const [a, b, c, d] = it.bbox;
    if (lon < a || lon > c || lat < b || lat > d) continue;
    if (it.polys.some((p) => pointInPolygon(lon, lat, p))) hits.push(it.f);
  }
  return hits;
}

// Rules for a zone code (D1–D8 fall back to the shared D entry, etc.)
function rulesFor(code) {
  if (!code) return null;
  if (rules.zones[code]) return rules.zones[code];
  if (/^D\d$/.test(code)) return rules.zones.D || null;
  return null;
}

// ---------------------------------------------------------------------------
// Land for sale – listings collected by DataFetch (nekretnine.hr)
// The file is re-read whenever it changes, so a new DataFetch run shows up after a page reload.
// ---------------------------------------------------------------------------
const LISTINGS_FILE = path.resolve(__dirname, process.env.LISTINGS_FILE || path.join(REPO_DATA, 'listings', 'listings-nekretnine.json'));
const UTILITY_NAMES = { 'illuminazione pubblica': 'javna rasvjeta', recintato: 'ograđeno' };
let listingsCache = { mtimeMs: -1, body: null };

function buildListings() {
  const doc = JSON.parse(fs.readFileSync(LISTINGS_FILE, 'utf8').replace(/^﻿/, ''));
  const all = doc.listings || [];
  const located = all.filter((l) => Number.isFinite(l.lat) && Number.isFinite(l.lon));

  // How many listings share exactly the same pin (agencies often reuse one point for several plots)
  const stack = new Map();
  for (const l of located) { const k = `${l.lat},${l.lon}`; stack.set(k, (stack.get(k) || 0) + 1); }

  const features = located.map((l) => {
    const marker = l.raw?.realEstate?.properties?.[0]?.location?.marker;   // marker | only_area | no_map
    const hist = l.priceHistory || [];
    const prev = hist.length > 1 ? hist[hist.length - 2] : null;
    const zone = zonesAt(l.lon, l.lat)[0]?.properties.code ?? null;
    return {
      type: 'Feature',
      id: l.id,
      geometry: { type: 'Point', coordinates: [l.lon, l.lat] },
      properties: {
        id: l.id, url: l.url, title: l.title, caption: l.caption,
        price: l.priceEur ?? null, area: l.areaM2 ?? null, ppm: l.pricePerM2 ?? null,
        district: l.macrozone ?? null, microzone: l.microzone ?? null, address: l.address ?? null,
        utilities: (l.utilities || []).map((u) => UTILITY_NAMES[u] || u).join(', '),
        agency: l.agencyName ?? null, photos: l.photoCount ?? 0,
        precise: marker === 'marker',
        precision: marker === 'marker' ? 'exact' : marker === 'only_area' ? 'area' : 'hidden',
        removed: Boolean(l.removedAt), removedAt: l.removedAt ?? null,
        firstSeen: l.firstSeen, lastSeen: l.lastSeen,
        prevPrice: prev?.priceEur ?? null, prevPriceAt: prev ? hist[hist.length - 1].seenAt : null,
        stack: stack.get(`${l.lat},${l.lon}`),
        zone,
      },
    };
  });

  return JSON.stringify({
    type: 'FeatureCollection',
    meta: {
      source: doc.source || 'nekretnine.hr',
      updated: doc.updated, lastCompleteScan: doc.lastCompleteScan ?? null,
      total: all.length, active: all.filter((l) => !l.removedAt).length,
      located: located.length, withoutCoordinates: all.length - located.length,
    },
    features,
  });
}

function listingsJson() {
  const { mtimeMs } = fs.statSync(LISTINGS_FILE);
  if (mtimeMs !== listingsCache.mtimeMs) listingsCache = { mtimeMs, body: buildListings() };
  return listingsCache.body;
}

// Per-code statistics for the legend
const stats = {};
for (const f of zones.features) {
  const c = f.properties.code;
  stats[c] ??= { count: 0, area_m2: 0 };
  stats[c].count += 1;
  stats[c].area_m2 += f.properties.area_m2 || 0;
}

// ---------------------------------------------------------------------------
// App
// ---------------------------------------------------------------------------
const app = express();
app.use(compression());

app.use(express.static(path.join(__dirname, 'public')));
app.use('/vendor/maplibre-gl', express.static(path.join(__dirname, 'node_modules/maplibre-gl/dist')));
app.use('/vendor/proj4', express.static(path.join(__dirname, 'node_modules/proj4/dist')));
app.use('/data', express.static(DATA_DIR, { maxAge: '1h' }));

// The Google key is a browser key: tiles are requested by the browser, so it is sent to the page.
// Protect it in Google Cloud Console with an HTTP-referrer restriction and limit it to the Map Tiles API.
app.get('/api/config', (_req, res) => res.json({
  googleMapsApiKey: GOOGLE_MAPS_API_KEY || null,
  googleTileBase: GOOGLE_TILE_BASE,
}));
app.get('/api/rules', (_req, res) => res.json(rules));
app.get('/api/stats', (_req, res) => res.json(stats));

// GET /api/listings → land for sale as GeoJSON points (empty collection if the file is missing)
app.get('/api/listings', (_req, res) => {
  if (!fs.existsSync(LISTINGS_FILE)) {
    return res.json({ type: 'FeatureCollection', meta: { missing: path.relative(__dirname, LISTINGS_FILE) }, features: [] });
  }
  try {
    res.type('application/json').send(listingsJson());
  } catch (err) {
    // e.g. DataFetch is writing the file right now
    res.status(503).json({ error: `Could not read ${path.basename(LISTINGS_FILE)}: ${err.message}` });
  }
});

// GET /api/zone?lon=15.977&lat=45.813  → zones at that point + their rules
app.get('/api/zone', (req, res) => {
  const lon = Number(req.query.lon), lat = Number(req.query.lat);
  if (!Number.isFinite(lon) || !Number.isFinite(lat)) {
    return res.status(400).json({ error: 'Provide numeric lon and lat, e.g. /api/zone?lon=15.977&lat=45.813' });
  }
  const [E, N] = proj4('EPSG:4326', 'EPSG:3765', [lon, lat]);
  const hits = zonesAt(lon, lat).map((f) => ({
    id: f.id,
    ...f.properties,
    rules: rulesFor(f.properties.code),
  }));
  res.json({ lon, lat, htrs96: { E: Math.round(E * 10) / 10, N: Math.round(N * 10) / 10 }, zones: hits });
});

app.listen(PORT, () => {
  console.log(`Zagreb GUP map running at http://localhost:${PORT}`);
  console.log(`Loaded ${zones.features.length} zone polygons, ${Object.keys(rules.zones).length} zone rule sets`);
  console.log(fs.existsSync(LISTINGS_FILE)
    ? `Land for sale: ${path.relative(__dirname, LISTINGS_FILE)}`
    : `No listings file at ${path.relative(__dirname, LISTINGS_FILE)} – the "Land for sale" layer stays empty`);
  console.log(GOOGLE_MAPS_API_KEY
    ? 'Google Maps background enabled (Map Tiles API)'
    : 'No GOOGLE_MAPS_API_KEY in .env – Google backgrounds disabled, OpenStreetMap is used instead');
});
