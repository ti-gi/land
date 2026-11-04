// Zagreb GUP 2025 – MapLibre front end (vanilla JS, ES module)
// MapLibre GL JS v6 is published as an ES module; the server exposes it under /vendor.
import * as maplibregl from '/vendor/maplibre-gl/maplibre-gl.mjs';
import { FAMILIES, ZONES, zoneInfo, colorExpression } from './zone-styles.js';
import { BASEMAPS, BasemapManager, BasemapSwitcher, GoogleAttribution } from './basemaps.js';
import { GoogleSearch, describeSearchError } from './google-search.js';

const ZAGREB = { center: [15.98, 45.81], zoom: 11.2 };
const BASEMAP_PREF = 'gup-basemap';

// Land for sale: all pins in one colour (the zone colours underneath carry the meaning)
const PIN_COLOR = '#1f2328';
const LISTING_LAYERS = ['listings-points', 'listings-selected'];

const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const fmtNum = (v, d = 0) => (v == null ? '–' : Number(v).toLocaleString('hr-HR', { maximumFractionDigits: d }));
const fmtDate = (s) => (s ? new Date(s).toLocaleDateString('hr-HR') : '–');
const fmtArea = (m2) => (m2 >= 10000 ? `${(m2 / 10000).toLocaleString('hr-HR', { maximumFractionDigits: 2 })} ha` : `${Math.round(m2).toLocaleString('hr-HR')} m²`);

let map, stats = {}, rules = null, codes = [];
let selectedIds = [], hoverId = null, marker = null;
let basemaps, switcher, googleAttr, activeGoogle = null, basemapId = null;
let searchService;

// Land for sale state
let listings = null;                       // FeatureCollection from /api/listings
const listingById = new Map();
const listingView = { on: true, approx: true, removed: false };
let selectedListingIds = [];

// ---------------------------------------------------------------------------
// Start-up
// ---------------------------------------------------------------------------
init();

async function init() {
  let config;
  [stats, rules, config, listings] = await Promise.all([
    fetch('/api/stats').then((r) => r.json()),
    fetch('/api/rules').then((r) => r.json()),
    fetch('/api/config').then((r) => r.json()),
    fetch('/api/listings').then((r) => (r.ok ? r.json() : null)).catch(() => null),
  ]);
  codes = Object.keys(stats);
  prepareListings();
  buildListingsLegend();
  buildLegend();

  basemaps = new BasemapManager({ apiKey: config.googleMapsApiKey, tileBase: config.googleTileBase });
  searchService = new GoogleSearch(config.googleMapsApiKey);
  let wanted = null;
  try { wanted = localStorage.getItem(BASEMAP_PREF); } catch { /* storage unavailable */ }
  const valid = (id) => BASEMAPS.some((b) => b.id === id && (!b.google || basemaps.hasGoogle));
  const first = valid(wanted) ? wanted : (basemaps.hasGoogle ? 'google-roadmap' : 'none');

  const base = await resolveBasemap(first);
  map = new maplibregl.Map({ container: 'map', style: withZones(base), ...ZAGREB, attributionControl: { compact: true } });

  switcher = new BasemapSwitcher(basemaps, basemapId, switchBasemap);
  googleAttr = new GoogleAttribution();
  map.addControl(switcher, 'top-right');
  map.addControl(new ZonesLegendControl(), 'top-right');            // below the background switcher
  map.addControl(googleAttr, 'bottom-right');
  map.addControl(new maplibregl.NavigationControl({ visualizePitch: false }), 'bottom-right');   // above the attribution
  map.addControl(new maplibregl.ScaleControl({ unit: 'metric' }), 'bottom-left');

  map.on('load', () => { wireMapEvents(); updateGoogleAttribution(); });
  map.on('moveend', debounce(updateGoogleAttribution, 400));
  map.on('error', onMapError);
  wireUi();
}

// Load a basemap; if it fails, fall back to the plain background
async function resolveBasemap(id) {
  const order = [id, 'none'].filter((v, i, a) => a.indexOf(v) === i);
  for (const candidate of order) {
    try {
      const base = await basemaps.getStyle(candidate);
      if (candidate !== id) showNotice(`${BASEMAPS.find((b) => b.id === id).title} could not be loaded – using ${BASEMAPS.find((b) => b.id === candidate).title}.`);
      basemapId = candidate;
      activeGoogle = base.google;
      return base;
    } catch (err) {
      console.warn(`Basemap ${candidate} failed:`, err.message);
      if (candidate === id && BASEMAPS.find((b) => b.id === id)?.google) showNotice(`Google Maps: ${err.message}`);
    }
  }
  throw new Error('No basemap available');
}

async function switchBasemap(id) {
  if (id === basemapId) return;
  const base = await resolveBasemap(id);
  try { localStorage.setItem(BASEMAP_PREF, basemapId); } catch { /* ignore */ }
  switcher.setActive(basemapId);
  map.setStyle(withZones(base), { diff: false });
  map.once('idle', () => {
    setSelectedListings(selectedListingIds);
    selectedIds.forEach((fid) => map.setFeatureState({ source: 'zones', id: fid }, { selected: true }));
    updateGoogleAttribution();
  });
}

// Combine a basemap style with the zone source and layers
function withZones(base) {
  const style = structuredClone(base.style);
  style.sources.zones = {
    type: 'geojson', data: '/data/zones.geojson',
    attribution: 'Zones: GUP grada Zagreba, izmjene i dopune 2025.',
  };
  const layout = { visibility: zonesOn ? 'visible' : 'none' };
  const zoneLayers = [
    { id: 'zones-fill', type: 'fill', source: 'zones', layout,
      paint: { 'fill-color': colorExpression(codes), 'fill-opacity': Number($('opacity').value) } },
    { id: 'zones-line', type: 'line', source: 'zones', layout,
      paint: { 'line-color': 'rgba(40,40,40,0.45)', 'line-width': ['interpolate', ['linear'], ['zoom'], 11, 0.1, 15, 0.8] } },
  ];
  const highlight = {
    id: 'zones-highlight', type: 'line', source: 'zones', layout,
    paint: {
      'line-color': ['case', ['boolean', ['feature-state', 'selected'], false], '#d0021b', '#1d5fbf'],
      'line-width': ['case', ['boolean', ['feature-state', 'selected'], false], 3, ['boolean', ['feature-state', 'hover'], false], 2, 0],
    },
  };
  // On vector basemaps keep street names above the zones; raster basemaps have labels baked in
  const at = base.labelsOnTop ? style.layers.findIndex((l) => l.type === 'symbol') : -1;
  if (at >= 0) style.layers.splice(at, 0, ...zoneLayers);
  else style.layers.push(...zoneLayers);
  style.layers.push(highlight);
  if (listings) addListingLayers(style);
  return style;
}

// ---------------------------------------------------------------------------
// Land for sale (listings collected by DataFetch from nekretnine.hr)
// ---------------------------------------------------------------------------
function prepareListings() {
  if (!listings?.features) { listings = null; return; }
  for (const f of listings.features) listingById.set(f.id, f);
  // Exact pins are drawn above approximate ones
  listings.features.sort((a, b) => Number(a.properties.precise) - Number(b.properties.precise));
}

function shownListings() {
  const v = listingView;
  return {
    type: 'FeatureCollection',
    features: (listings?.features || []).filter((f) => {
      const p = f.properties;
      return (v.approx || p.precise) && (v.removed || !p.removed);
    }),
  };
}

function addListingLayers(style) {
  style.sources.listings = { type: 'geojson', data: shownListings(), attribution: 'Listings: nekretnine.hr' };
  const stacked = ['>', ['get', 'stack'], 1];
  const radius = (extra) => ['interpolate', ['linear'], ['zoom'],
    10, ['+', ['case', stacked, 4.5, 3.5], extra],
    14, ['+', ['case', stacked, 7.5, 6], extra],
    17, ['+', ['case', stacked, 10, 8.5], extra]];
  const visibility = listingView.on ? 'visible' : 'none';
  style.layers.push(
    { id: 'listings-points', type: 'circle', source: 'listings', layout: { visibility },
      paint: {
        'circle-radius': radius(0),
        // exact pin: filled; approximate (area only / hidden address): ring; several ads on one pin: bigger, thicker border
        'circle-color': ['case', ['get', 'precise'], PIN_COLOR, 'rgba(255,255,255,0.9)'],
        'circle-stroke-color': ['case', ['get', 'precise'], '#ffffff', PIN_COLOR],
        'circle-stroke-width': ['case', ['get', 'precise'], ['case', stacked, 2.5, 1.5], 2.5],
        'circle-opacity': ['case', ['get', 'removed'], 0.45, 1],
        'circle-stroke-opacity': ['case', ['get', 'removed'], 0.45, 1],
      } },
    { id: 'listings-selected', type: 'circle', source: 'listings', layout: { visibility },
      filter: ['in', ['id'], ['literal', selectedListingIds]],
      paint: { 'circle-radius': radius(4), 'circle-color': 'rgba(0,0,0,0)', 'circle-stroke-color': '#d0021b', 'circle-stroke-width': 3 } },
  );
}

function refreshListings() {
  const src = map?.getSource('listings');
  if (src) src.setData(shownListings());
  const shown = shownListings().features.length;
  $('listings-count').textContent = listingView.on ? `${shown.toLocaleString('hr-HR')} shown` : 'hidden';
}

function setSelectedListings(ids) {
  selectedListingIds = ids;
  if (map?.getLayer('listings-selected')) map.setFilter('listings-selected', ['in', ['id'], ['literal', ids]]);
}

function buildListingsLegend() {
  const box = $('listings-box');
  if (!listings) return;
  box.hidden = false;
  $('legend-panel').hidden = false;
  const meta = listings.meta || {};
  if (meta.missing) {
    $('listings-body').innerHTML = `<p class="legend-foot">No listings yet – copy DataFetch's <b>listings-nekretnine.json</b> to <code>${esc(meta.missing)}</code> and reload.</p>`;
    $('listings-on').disabled = true;
    return;
  }
  const active = listings.features.filter((f) => !f.properties.removed);
  const removed = listings.features.length - active.length;
  $('listings-removed-row').hidden = removed === 0;
  const parts = [`${meta.source || 'nekretnine.hr'} · building land`,
    meta.lastCompleteScan ? `last complete scan ${fmtDate(meta.lastCompleteScan)}` : null,
    meta.withoutCoordinates ? `${meta.withoutCoordinates} ads have no coordinates and are not shown` : null];
  $('listings-foot').textContent = parts.filter(Boolean).join(' · ') + '. Click a pin for details.';

  box.addEventListener('change', (e) => {
    const t = e.target;
    if (t.id === 'listings-on') {
      listingView.on = t.checked;
      box.classList.toggle('off', !t.checked);
      LISTING_LAYERS.forEach((id) => map?.getLayer(id) && map.setLayoutProperty(id, 'visibility', t.checked ? 'visible' : 'none'));
    } else if (t.id === 'listings-approx') listingView.approx = t.checked;
    else if (t.id === 'listings-removed') listingView.removed = t.checked;
    refreshListings();
  });
  refreshListings();
}

function listingsAt(point) {
  if (!listingView.on || !map.getLayer('listings-points')) return [];
  const r = 6;
  // Exactly under the cursor first (same pin the hover text describes), then a few pixels around it
  let hits = map.queryRenderedFeatures(point, { layers: ['listings-points'] });
  if (!hits.length) hits = map.queryRenderedFeatures([[point.x - r, point.y - r], [point.x + r, point.y + r]], { layers: ['listings-points'] });
  // The top-most pin under the cursor, plus every other ad that shares exactly that pin
  const features = hits.map((h) => listingById.get(h.id)).filter(Boolean);
  if (!features.length) return [];
  const [lon, lat] = features[0].geometry.coordinates;
  const shown = new Set(shownListings().features);
  return listings.features.filter((f) => shown.has(f) && f.geometry.coordinates[0] === lon && f.geometry.coordinates[1] === lat)
    .sort((a, b) => (b.properties.precise - a.properties.precise) || ((a.properties.ppm ?? 1e9) - (b.properties.ppm ?? 1e9)));
}

async function selectListings(features) {
  const [lon, lat] = features[0].geometry.coordinates;
  setSelectedListings(features.map((f) => f.id));
  marker?.remove();
  const data = await fetch(`/api/zone?lon=${lon}&lat=${lat}`).then((r) => r.json());
  selectedIds.forEach((id) => map.setFeatureState({ source: 'zones', id }, { selected: false }));
  selectedIds = data.zones.map((z) => z.id);
  selectedIds.forEach((id) => map.setFeatureState({ source: 'zones', id }, { selected: true }));

  const n = features.length;
  const head = `<div class="section-label" style="margin-top:0;padding-top:0;border:0">${n === 1 ? '1 listing' : `${n} listings`} at this spot</div>`
    + features.map(listingCard).join('');
  const approx = !features[0].properties.precise;
  const note = approx
    ? `<div class="warn">This pin is only the centre of the area (${esc(features[0].properties.microzone || features[0].properties.district || 'neighbourhood')}), not the plot. The zone below is for that point – the plot itself may be in a different zone.</div>`
    : '';
  renderDetails(data, { title: 'Land for sale', before: head + `<div class="section-label">GUP zone at this pin</div>` + note });
}

function listingCard(f) {
  const p = f.properties;
  const precision = { exact: 'exact pin', area: 'approximate – area only', hidden: 'approximate – address hidden by advertiser' }[p.precision];
  const changed = p.prevPrice != null && p.prevPrice !== p.price
    ? `<div class="warn">Price changed: was ${fmtNum(p.prevPrice)} € (until ${fmtDate(p.prevPriceAt)})</div>` : '';
  const row = (k, v) => (v ? `<dt>${esc(k)}</dt><dd>${esc(v)}</dd>` : '');
  return `<div class="listing-card${p.removed ? ' removed' : ''}">
    <h3>${esc(p.caption || p.title)}</h3>
    ${p.caption ? `<p class="cap">${esc(p.title)}</p>` : ''}
    <div class="listing-figures">
      <span><b>${p.price != null ? `${fmtNum(p.price)} €` : 'price on request'}</b></span>
      <span>${p.area != null ? `${fmtNum(p.area)} m²` : 'area –'}</span>
      ${p.ppm != null ? `<span>${fmtNum(p.ppm)} €/m²</span>` : ''}
    </div>
    ${changed}
    <dl>
      ${row('Location', [p.address, p.microzone, p.district].filter(Boolean).join(', '))}
      ${row('Pin', precision)}
      ${row('Utilities', p.utilities || 'none listed')}
      ${row('Agency', p.agency)}
      ${row('First seen', fmtDate(p.firstSeen))}
      ${p.removed ? row('No longer listed', fmtDate(p.removedAt)) : ''}
    </dl>
    <a href="${esc(p.url)}" target="_blank" rel="noopener">Open ad on nekretnine.hr ↗</a>
  </div>`;
}

async function updateGoogleAttribution() {
  if (!activeGoogle) { googleAttr.hide(); return; }
  const mapType = activeGoogle;
  googleAttr.show('', mapType !== 'roadmap');
  try {
    const b = map.getBounds();
    const v = await basemaps.viewport(mapType, map.getZoom(), { north: b.getNorth(), south: b.getSouth(), east: b.getEast(), west: b.getWest() });
    if (activeGoogle === mapType) googleAttr.show(v.copyright, mapType !== 'roadmap');
  } catch (err) {
    console.warn('Google attribution failed:', err.message);
  }
}

// An expired or revoked Google session shows up as failing tiles: renew it once
let renewing = false;
async function onMapError(e) {
  if (e.sourceId !== 'google' || !activeGoogle || renewing) return;
  const status = e.error?.status;
  if (![400, 401, 403].includes(status)) return;
  renewing = true;
  basemaps.forgetSession(activeGoogle);
  const id = basemapId;
  basemapId = null;
  await switchBasemap(id).catch(() => {});
  setTimeout(() => { renewing = false; }, 30000);
}

function wireMapEvents() {
  map.on('mousemove', 'zones-fill', (e) => {
    const f = e.features[0];
    map.getCanvas().style.cursor = 'pointer';
    if (hoverId !== f.id) {
      if (hoverId !== null) map.setFeatureState({ source: 'zones', id: hoverId }, { hover: false });
      hoverId = f.id;
      map.setFeatureState({ source: 'zones', id: hoverId }, { hover: true });
    }
    const z = zoneInfo(f.properties.code);
    $('status').textContent = `${f.properties.code} – ${z.hr} (${z.en}) · ${fmtArea(f.properties.area_m2)}`;
  });
  map.on('mouseleave', 'zones-fill', () => {
    map.getCanvas().style.cursor = '';
    if (hoverId !== null) map.setFeatureState({ source: 'zones', id: hoverId }, { hover: false });
    hoverId = null;
  });
  map.on('mousemove', 'listings-points', (e) => {
    const f = listingById.get(e.features[0].id);
    if (!f) return;
    const p = f.properties;
    map.getCanvas().style.cursor = 'pointer';
    $('status').textContent = [p.area != null ? `${fmtNum(p.area)} m²` : null, p.price != null ? `${fmtNum(p.price)} €` : 'price on request',
      p.ppm != null ? `${fmtNum(p.ppm)} €/m²` : null, p.microzone || p.district, p.precise ? null : 'approximate location',
      p.stack > 1 ? `${p.stack} ads on this pin` : null].filter(Boolean).join(' · ');
  });
  map.on('click', (e) => {
    const hits = listingsAt(e.point);
    if (hits.length) selectListings(hits);
    else selectPoint(e.lngLat.lng, e.lngLat.lat);
  });
}

function debounce(fn, ms) {
  let t;
  return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); };
}

// ---------------------------------------------------------------------------
// Zones legend (read-only) – a panel on the map below the background switcher
// ---------------------------------------------------------------------------
const ZONES_COLLAPSED_PREF = 'gup-zones-collapsed';
const ZONE_LAYERS = ['zones-fill', 'zones-line', 'zones-highlight'];
let zonesOn = false;                         // "Show zones" starts off

class ZonesLegendControl {
  onAdd() {
    const el = $('zones-panel');
    el.classList.add('maplibregl-ctrl');
    // keep map gestures (drag, wheel zoom) out of the panel so its list can scroll
    ['wheel', 'mousedown', 'touchstart', 'dblclick', 'click'].forEach((ev) => el.addEventListener(ev, (e) => e.stopPropagation()));
    return el;
  }
  onRemove() {}
}

function buildLegend() {
  const list = $('legend-list');
  list.innerHTML = '';
  for (const fam of FAMILIES) {
    const famCodes = codes
      .filter((c) => zoneInfo(c).fam === fam.id)
      .sort((a, b) => a.localeCompare(b, 'hr', { numeric: true }));
    if (!famCodes.length) continue;

    const box = document.createElement('div');
    box.className = 'fam';
    const famArea = famCodes.reduce((s, c) => s + stats[c].area_m2, 0);
    box.innerHTML = `<div class="fam-head">${esc(fam.label)}
      <span class="zone-area" style="margin-left:auto">${(famArea / 1e6).toFixed(1)} km²</span></div>`;

    for (const c of famCodes) {
      const z = zoneInfo(c);
      const row = document.createElement('div');
      row.className = 'zone-row';
      row.title = `${z.hr} – ${z.en}\n${stats[c].count} polygons`;
      row.innerHTML = `<span class="swatch" style="background:${z.color}"></span>
        <span class="zone-name"><b>${esc(c)}</b>${esc(ZONES[c] ? z.hr : 'neodređeno')}</span>
        <span class="zone-area">${(stats[c].area_m2 / 1e6).toFixed(2)}</span>`;
      box.appendChild(row);
    }
    list.appendChild(box);
  }

  const total = Object.values(stats).reduce((s, v) => s + v.area_m2, 0);
  const n = Object.values(stats).reduce((s, v) => s + v.count, 0);
  $('legend-foot').textContent = `${n.toLocaleString('hr-HR')} polygons · ${(total / 1e6).toFixed(1)} km² (areas in km²). Streets are not zoned in the plan.`;

  // Collapsed or open: remembered per browser; on small screens it starts collapsed
  let collapsed = window.matchMedia('(max-width: 820px)').matches;
  try { const v = localStorage.getItem(ZONES_COLLAPSED_PREF); if (v !== null) collapsed = v === '1'; } catch { /* storage unavailable */ }
  setZonesCollapsed(collapsed);
}

function setZonesCollapsed(collapsed) {
  $('zones-panel').classList.toggle('collapsed', collapsed);
  document.querySelector('.layout').classList.toggle('zones-expanded', !collapsed);
  $('zones-collapse').setAttribute('aria-expanded', String(!collapsed));
  $('zones-collapse').title = collapsed ? 'Show the zones legend' : 'Hide the zones legend';
}

function setZonesOn(on) {
  zonesOn = on;
  $('zones-panel').classList.toggle('off', !on);
  ZONE_LAYERS.forEach((id) => map?.getLayer(id) && map.setLayoutProperty(id, 'visibility', on ? 'visible' : 'none'));
}

// ---------------------------------------------------------------------------
// Point lookup & details panel
// ---------------------------------------------------------------------------
async function selectPoint(lon, lat) {
  const data = await fetch(`/api/zone?lon=${lon}&lat=${lat}`).then((r) => r.json());
  setSelectedListings([]);

  selectedIds.forEach((id) => map.setFeatureState({ source: 'zones', id }, { selected: false }));
  selectedIds = data.zones.map((z) => z.id);
  selectedIds.forEach((id) => map.setFeatureState({ source: 'zones', id }, { selected: true }));

  if (!marker) marker = new maplibregl.Marker({ color: '#d0021b', scale: 0.8 });
  marker.setLngLat([lon, lat]).addTo(map);

  renderDetails(data);
}

function renderDetails(data, { title = 'Zone details', before = '' } = {}) {
  const body = $('details-body');
  $('details-title').textContent = title;
  const coords = `<div class="coords">WGS84 ${data.lat.toFixed(5)}, ${data.lon.toFixed(5)} · HTRS96/TM E ${data.htrs96.E.toLocaleString('hr-HR')} N ${data.htrs96.N.toLocaleString('hr-HR')}</div>`;

  if (!data.zones.length) {
    body.innerHTML = `${before}${coords}<p class="empty-msg">No zone here. Streets and areas outside the GUP boundary are not zoned in this plan sheet.</p>`;
  } else {
    body.innerHTML = before + coords + data.zones.map(zoneCard).join('') + everywhereBlock() +
      `<p class="disclaimer">Extracted from GUP grada Zagreba, izmjene i dopune 2025 (map sheet 1 and the consolidated text). Not legally binding – check the official plan and any detailed plan (UPU) for the location.</p>`;
  }
  $('details-panel').hidden = false;
  document.querySelector('.layout').classList.add('details-open');
}

function zoneCard(z) {
  const info = zoneInfo(z.code);
  const r = z.rules;
  const cls = rules && (Object.values(rules.buildability_classes).find((b) => b.zones.includes(z.code) || (r && b.zones.includes(r.code)))
    || rules.buildability_classes[r?.buildability]);
  const src = {
    label: 'Code read from map label',
    label_multi: 'Several labels in this polygon – most frequent used',
    inferred_neighbour: null,
    group_default: null,
    unresolved: null,
  }[z.code_source];

  let html = `<div class="zone-card">
    <div class="zone-title">
      <span class="chip" style="background:${info.color}">${esc(z.code)}</span>
      <div><h3>${esc(info.hr)}</h3><div class="en">${esc(info.en)}</div></div>
    </div>
    <div class="meta">
      <span class="tag">${fmtArea(z.area_m2)}</span>
      ${cls ? `<span class="tag">${esc(cls.name_hr)}</span>` : ''}
      ${r?.article ? `<span class="tag">GUP čl. ${esc(r.article)}</span>` : ''}
      ${src ? `<span class="tag">${esc(src)}</span>` : ''}
    </div>`;

  if (z.code_source === 'inferred_neighbour' || z.code_source === 'group_default') {
    html += `<div class="warn">No label in this polygon on the map sheet – code taken from the neighbouring zone. Check the official sheet.</div>`;
  } else if (z.code_source === 'unresolved' || z.code.startsWith('?')) {
    html += `<div class="warn">Zone code could not be determined from the map sheet. Only the colour group is known.</div>`;
  }

  if (!r) return html + `<p class="empty-msg">No rule text extracted for this code yet.</p></div>`;

  if (/^D\d$/.test(z.code) && r.subtypes?.[z.code]) html += `<p><b>Subtype:</b> ${esc(r.subtypes[z.code])}</p>`;
  if (r.primary) html += `<p>${esc(r.primary)}</p>`;
  html += list('Permitted uses', r.permitted_uses);
  if (r.ancillary_uses) html += list('Ancillary uses', r.ancillary_uses.map((a) => a.label_hr + (a.condition ? ` (${a.condition})` : '')));
  if (r.separate_plot_uses) {
    html += list('On a separate plot', r.separate_plot_uses.any_size);
    html += list('On a separate plot up to 1 ha', r.separate_plot_uses.up_to_1_ha);
  }
  html += list('Limits', r.limits);
  html += list('Not allowed', r.prohibited, 'no');
  if (r.prohibited_note) html += `<p class="empty-msg">${esc(r.prohibited_note)}</p>`;
  html += list('Notes & exceptions', r.notes);
  return html + '</div>';
}

function list(title, items, cls = '') {
  if (!items?.length) return '';
  return `<h4>${esc(title)}</h4><ul>${items.map((i) => `<li class="${cls}">${esc(i)}</li>`).join('')}</ul>`;
}

function everywhereBlock() {
  const e = rules?.everywhere;
  if (!e) return '';
  return `<details class="everywhere"><summary>Allowed in every zone (GUP čl. ${esc(e.article)})</summary>
    ${list('', e.allowed_everywhere)}<p class="empty-msg">${esc(e.interim_use_note)}</p></details>`;
}

// ---------------------------------------------------------------------------
// Address search (Google, limited to Zagreb): suggestions while typing, address lookup on Enter
// ---------------------------------------------------------------------------
let suggestSeq = 0;
let suggestions = [];
let activeSuggestion = -1;

function showResults(html) {
  const ul = $('search-results');
  ul.innerHTML = html;
  ul.hidden = !html;
}

function renderSuggestions(list, emptyText) {
  suggestions = list;
  activeSuggestion = -1;
  if (!list.length) { showResults(emptyText ? `<li class="empty">${esc(emptyText)}</li>` : ''); return; }
  showResults(list.map((s, i) => `<li data-i="${i}" role="option"><b>${esc(s.main)}</b>`
    + `${s.secondary ? `<span class="secondary">${esc(s.secondary)}</span>` : ''}</li>`).join('')
    + '<li class="attrib">Search by Google</li>');
}

function highlightSuggestion(i) {
  activeSuggestion = i;
  $('search-results').querySelectorAll('li[data-i]').forEach((li) => li.classList.toggle('active', Number(li.dataset.i) === i));
}

let suggestTimer = null;
function suggestSoon(q) {
  clearTimeout(suggestTimer);
  suggestTimer = setTimeout(() => runSuggest(q), 250);
}
function cancelSuggest() {
  clearTimeout(suggestTimer);
  ++suggestSeq;                                  // answers still on their way are ignored
}

async function runSuggest(q) {
  const seq = ++suggestSeq;
  try {
    const list = await searchService.suggest(q);
    if (seq === suggestSeq) renderSuggestions(list, 'No suggestions in Zagreb – press Enter to search');
  } catch (err) {
    console.warn('Google suggestions failed:', err);
    if (seq === suggestSeq) showResults(`<li class="empty">${esc(describeSearchError(err))}</li>`);
  }
}

async function pickSuggestion(i) {
  const s = suggestions[i];
  if (!s) return;
  cancelSuggest();
  showResults('<li class="empty">Finding the place…</li>');
  try {
    const hit = await searchService.locate(s);
    goToSearchHit(hit);
  } catch (err) {
    console.warn('Google place lookup failed:', err);
    showResults(`<li class="empty">${esc(describeSearchError(err))}</li>`);
  }
}

async function geocodeSearch(q) {
  cancelSuggest();
  showResults('<li class="empty">Searching…</li>');
  try {
    const hits = await searchService.geocode(q);
    if (!hits.length) { showResults('<li class="empty">No results in Zagreb</li>'); return; }
    if (hits.length === 1) { goToSearchHit(hits[0]); return; }
    suggestions = hits.map((h) => ({ main: h.label, secondary: '', hit: h }));
    showResults(hits.map((h, i) => `<li data-i="${i}">${esc(h.label)}</li>`).join('') + '<li class="attrib">Search by Google</li>');
  } catch (err) {
    console.warn('Google geocoding failed:', err);
    showResults(`<li class="empty">${esc(describeSearchError(err))}</li>`);
  }
}

function goToSearchHit({ lat, lon, label }) {
  showResults('');
  suggestions = [];
  activeSuggestion = -1;
  $('search-input').value = label.replace(/, \d{5},? Zagreb.*$|, Hrvatska$/u, '');
  map.flyTo({ center: [lon, lat], zoom: Math.max(map.getZoom(), 16) });
  selectPoint(lon, lat);
}

function wireSearch() {
  const input = $('search-input');
  if (!searchService.available) {
    input.disabled = true;
    input.placeholder = 'Address search needs a Google Maps key (src/web/.env)';
    return;
  }
  input.addEventListener('input', () => {
    activeSuggestion = -1;                      // typing makes Enter search the text, not an old highlight
    const q = input.value.trim();
    if (q.length < 3) { cancelSuggest(); showResults(''); return; }
    suggestSoon(q);
  });
  input.addEventListener('keydown', (e) => {
    const n = $('search-results').querySelectorAll('li[data-i]').length;
    if (e.key === 'ArrowDown' && n) { e.preventDefault(); highlightSuggestion((activeSuggestion + 1) % n); }
    else if (e.key === 'ArrowUp' && n) { e.preventDefault(); highlightSuggestion((activeSuggestion - 1 + n) % n); }
    else if (e.key === 'Escape') showResults('');
  });
  $('search-results').addEventListener('click', (e) => {
    const li = e.target.closest('li[data-i]');
    if (!li) return;
    const s = suggestions[Number(li.dataset.i)];
    if (s?.hit) goToSearchHit(s.hit); else pickSuggestion(Number(li.dataset.i));
  });
  $('search-form').addEventListener('submit', (e) => {
    e.preventDefault();
    if (activeSuggestion >= 0) {
      const s = suggestions[activeSuggestion];
      if (s?.hit) goToSearchHit(s.hit); else pickSuggestion(activeSuggestion);
      return;
    }
    const q = input.value.trim();
    if (q.length > 2) geocodeSearch(q);
  });
}

// ---------------------------------------------------------------------------
// UI wiring
// ---------------------------------------------------------------------------
function wireUi() {
  $('opacity').addEventListener('input', (e) => {
    if (map.getLayer('zones-fill')) map.setPaintProperty('zones-fill', 'fill-opacity', Number(e.target.value));
  });
  $('zones-on').addEventListener('change', (e) => setZonesOn(e.target.checked));
  $('zones-collapse').addEventListener('click', () => {
    const collapsed = !$('zones-panel').classList.contains('collapsed');
    setZonesCollapsed(collapsed);
    try { localStorage.setItem(ZONES_COLLAPSED_PREF, collapsed ? '1' : '0'); } catch { /* storage unavailable */ }
  });
  $('close-details').addEventListener('click', () => {
    $('details-panel').hidden = true;
    document.querySelector('.layout').classList.remove('details-open');
    selectedIds.forEach((id) => map.setFeatureState({ source: 'zones', id }, { selected: false }));
    selectedIds = [];
    setSelectedListings([]);
    marker?.remove();
  });
  $('toggle-legend').addEventListener('click', () => $('legend-panel').classList.toggle('open'));

  wireSearch();
  document.addEventListener('click', (e) => {
    if (!$('search-form').contains(e.target)) $('search-results').hidden = true;
  });
}

function showNotice(text) {
  const n = document.createElement('div');
  n.className = 'notice';
  n.textContent = text;
  document.querySelector('.layout').appendChild(n);
  setTimeout(() => n.remove(), 8000);
}
