// Background maps: Google Maps Platform Map Tiles API (roadmap / satellite / terrain), or none.
//
// Google 2D tiles need a session token: POST /v1/createSession?key=…  → { session, expiry }.
// Tiles are then requested as /v1/2dtiles/{z}/{x}/{y}?session=…&key=…
// Google's policies require showing the Google Maps attribution plus the `copyright`
// text from /tile/v1/viewport for the current view, and forbid caching tiles.
// Docs: https://developers.google.com/maps/documentation/tile/session_tokens

export const BASEMAPS = [
  { id: 'google-roadmap', label: 'Map', title: 'Google Maps – roadmap', google: 'roadmap' },
  { id: 'google-satellite', label: 'Satellite', title: 'Google Maps – satellite', google: 'satellite' },
  { id: 'google-terrain', label: 'Terrain', title: 'Google Maps – terrain', google: 'terrain' },
  { id: 'none', label: 'None', title: 'No background map' },
];

const PLAIN_STYLE = { version: 8, sources: {}, layers: [{ id: 'bg', type: 'background', paint: { 'background-color': '#f2efe9' } }] };
const ZAGREB_BOUNDS = { north: 45.97, south: 45.70, east: 16.25, west: 15.75 };

function store(key, value) { try { localStorage.setItem(key, JSON.stringify(value)); } catch { /* storage unavailable */ } }
function load(key) { try { return JSON.parse(localStorage.getItem(key)); } catch { return null; } }
function forget(key) { try { localStorage.removeItem(key); } catch { /* ignore */ } }

export class BasemapManager {
  constructor({ apiKey, tileBase }) {
    this.apiKey = apiKey || null;
    this.tileBase = (tileBase || 'https://tile.googleapis.com').replace(/\/$/, '');
    this.hiDpi = (window.devicePixelRatio || 1) > 1;
    this.maxZoomCache = {};
  }

  get hasGoogle() { return !!this.apiKey; }

  _sessionKey(mapType) { return `gmt-session:${mapType}:${this.hiDpi ? 2 : 1}:${this.apiKey.slice(-6)}`; }

  // Sessions are valid for two weeks, so one is reused from browser storage when possible
  async session(mapType, { fresh = false } = {}) {
    const key = this._sessionKey(mapType);
    const cached = fresh ? null : load(key);
    if (cached && Number(cached.expiry) * 1000 - Date.now() > 3600_000) return cached;

    const body = { mapType, language: 'hr-HR', region: 'HR' };
    if (mapType === 'terrain') body.layerTypes = ['layerRoadmap']; // required by Google for terrain
    if (this.hiDpi) Object.assign(body, { scale: 'scaleFactor2x', highDpi: true });

    const res = await fetch(`${this.tileBase}/v1/createSession?key=${encodeURIComponent(this.apiKey)}`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.session) {
      throw new Error(data?.error?.message || `Google Map Tiles API returned ${res.status}`);
    }
    store(key, data);
    return data;
  }

  forgetSession(mapType) { forget(this._sessionKey(mapType)); }

  // Copyright text + max zoom for a view (required attribution for Google tiles)
  async viewport(mapType, zoom, b) {
    const s = await this.session(mapType);
    const q = new URLSearchParams({
      session: s.session, key: this.apiKey, zoom: String(Math.max(0, Math.round(zoom))),
      north: b.north.toFixed(5), south: b.south.toFixed(5), east: b.east.toFixed(5), west: b.west.toFixed(5),
    });
    const res = await fetch(`${this.tileBase}/tile/v1/viewport?${q}`);
    if (!res.ok) throw new Error(`viewport ${res.status}`);
    return res.json();
  }

  async _maxZoom(mapType) {
    if (this.maxZoomCache[mapType]) return this.maxZoomCache[mapType];
    let mz = 21;
    try {
      const v = await this.viewport(mapType, 12, ZAGREB_BOUNDS);
      const c = { lat: 45.81, lng: 15.98 };
      const rects = (v.maxZoomRects || []).filter((r) => r.south <= c.lat && r.north >= c.lat && r.west <= c.lng && r.east >= c.lng);
      if (rects.length) mz = Math.max(...rects.map((r) => r.maxZoom));
    } catch { /* keep default */ }
    return (this.maxZoomCache[mapType] = mz);
  }

  // Returns { style, google: mapType|null, labelsOnTop }
  async getStyle(id) {
    const def = BASEMAPS.find((b) => b.id === id) || BASEMAPS[BASEMAPS.length - 1];

    if (def.google) {
      if (!this.hasGoogle) throw new Error('No Google Maps API key configured (see .env)');
      const s = await this.session(def.google);
      const maxzoom = await this._maxZoom(def.google);
      const url = `${this.tileBase}/v1/2dtiles/{z}/{x}/{y}?session=${encodeURIComponent(s.session)}&key=${encodeURIComponent(this.apiKey)}`;
      return {
        google: def.google,
        labelsOnTop: false,
        style: {
          version: 8,
          sources: { google: { type: 'raster', tiles: [url], tileSize: 256, maxzoom } },
          layers: [{ id: 'google', type: 'raster', source: 'google' }],
        },
      };
    }

    return { google: null, labelsOnTop: false, style: structuredClone(PLAIN_STYLE) };
  }
}

// Basemap switcher shown in the top-right corner of the map
export class BasemapSwitcher {
  constructor(manager, current, onChange) {
    this.manager = manager; this.current = current; this.onChange = onChange;
  }
  onAdd() {
    this.el = document.createElement('div');
    this.el.className = 'maplibregl-ctrl basemap-switcher';
    for (const b of BASEMAPS) {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.textContent = b.label;
      btn.dataset.id = b.id;
      const noKey = b.google && !this.manager.hasGoogle;
      btn.disabled = noKey;
      btn.title = noKey ? `${b.title} – add GOOGLE_MAPS_API_KEY to .env to enable` : b.title;
      btn.addEventListener('click', () => this.onChange(b.id));
      this.el.appendChild(btn);
    }
    this.setActive(this.current);
    return this.el;
  }
  setActive(id) {
    this.current = id;
    this.el?.querySelectorAll('button').forEach((b) => b.classList.toggle('active', b.dataset.id === id));
  }
  onRemove() { this.el.remove(); }
}

// Google attribution: "Google Maps" mark + the viewport copyright text
export class GoogleAttribution {
  onAdd() {
    this.el = document.createElement('div');
    this.el.className = 'maplibregl-ctrl google-attribution';
    this.el.hidden = true;
    this.el.innerHTML = '<span class="g-mark">Google Maps</span><span class="g-copy"></span>';
    return this.el;
  }
  show(text, dark) {
    this.el.hidden = false;
    this.el.classList.toggle('on-dark', !!dark);
    this.el.querySelector('.g-copy').textContent = text || '';
  }
  hide() { if (this.el) this.el.hidden = true; }
  onRemove() { this.el.remove(); }
}
