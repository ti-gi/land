# Data contract

The files that connect the parts of the repository. Producers and consumers must change together: when a field is
added, renamed or changes meaning, update this document, the producer, the consumer and the tests in one commit.

```
tools/gup-extract ──► data/gup/zones.geojson ─┐
                  └─► data/gup/rules.json ────┼─► src/web (server.js → /api/zone, /data/zones.geojson)
src/DataFetch ──────► data/listings/listings-nekretnine.json ── copied by hand ──► src/web/data/listings-nekretnine.json ─► src/web (server.js → /api/listings)
              └─────► data/listings/listings-njuskalo.json     (not used by the map yet)
```

| File | In git | Producer | Consumer |
|---|---|---|---|
| `data/gup/zones.geojson` | yes | `tools/gup-extract/extract_zones.py` | `src/web` |
| `data/gup/rules.json` | yes | `tools/gup-extract/build_rules.py` | `src/web` |
| `data/listings/listings-nekretnine.json` | **no** | DataFetch, source `nekretnine` | the owner (copies it to `src/web/data/`) |
| `src/web/data/listings-nekretnine.json` | **no** | copied by hand | `src/web` |
| `data/listings/listings-njuskalo.json` | **no** | DataFetch, source `njuskalo` | – |
| `data/datafetch/snapshots/`, `data/datafetch/browser-profile/` | **no** | DataFetch | people (debugging) |

All JSON is UTF-8. Dates are ISO 8601 with offset (`2026-10-29T14:46:12.9+01:00`). Money is EUR, areas m².
Missing values are **omitted**, not `null` (DataFetch writes with "ignore nulls") – consumers must treat an absent
field as unknown.

---

## `data/gup/zones.geojson`

GeoJSON FeatureCollection, **WGS84** (lon, lat), 6 decimals (~0.1 m). One feature per zone polygon
(Polygon or MultiPolygon), 4 457 for the 2025 plan.

| Field | Type | Meaning |
|---|---|---|
| `id` (feature and property) | int | 1…n, stable only within one extraction (MapLibre feature-state uses it) |
| `properties.code` | string | GUP zone code: `S`, `M`, `M0`–`M2`, `TR`, `D`, `D1`–`D8`, `I`, `K1`, `K2`, `T`, `G`, `N`, `R1`, `R2`, `Z`, `Z1`–`Z4`, `ZIS`, `IS`, `V1`–`V3`; unresolved: `?group` |
| `properties.name_hr` | string | Zone name (Croatian) |
| `properties.group` | string | Colour group on the map: `stambena`, `mjesovita`, `javna_drustvena`, `gospodarska`, `zelene_sport`, `vode` |
| `properties.code_source` | string | `label` (read from the sheet), `label_multi` (several labels, most frequent), `inferred_neighbour`, `group_default`, `unresolved` |
| `properties.area_m2` | int | Area in HTRS96/TM |

Top level: `name` = `gup_2025_namjena`, `metadata.source`, `metadata.method`.
The full-precision HTRS96/TM (EPSG:3765) version is produced in `tools/gup-extract/out/` (not committed).

## `data/gup/rules.json`

```jsonc
{
  "meta": { "plan": "...", "version": "Pročišćeni tekst ...", "note": "..." },
  "zones": {
    "M1": {
      "code": "M1", "name_hr": "...", "name_en": "...", "group": "mjesovita",
      "buildability": "gradive",            // key of buildability_classes
      "article": "10.b",                    // GUP article
      "primary": "...",                     // optional summary
      "permitted_uses": ["..."], "ancillary_uses": [{ "label_hr": "...", "condition": "..." }],
      "separate_plot_uses": { "any_size": ["..."], "up_to_1_ha": ["..."] },
      "limits": ["..."], "prohibited": ["..."], "prohibited_note": "...",
      "notes": ["..."], "subtypes": { "D1": "..." }   // all optional except code…article
    }
  },
  "everywhere": { "article": "...", "allowed_everywhere": ["..."], "interim_use_note": "...", "advertising_note": "..." },
  "building_types": { "individualna": {...}, "niska": {...}, "visoka": {...}, "floor_height_limits_m": {...} },
  "buildability_classes": { "gradive": { "name_hr": "...", "zones": ["S", ...] }, ... }
}
```

Zone codes `D1`–`D8` have no entry of their own: consumers fall back to `D` (see `rulesFor` in `src/web/server.js`).
Extra entries `GROBLJE`, `KPRP`, `KPRD` exist in the text but not as map codes.

---

## `listings-nekretnine.json` (`data/listings/`, copy in `src/web/data/`)

```jsonc
{
  "note": "...", "source": "nekretnine.hr",
  "updated": "<date>",                 // last save
  "lastCompleteScan": "<date>",        // last run that read every page (removals are only decided then)
  "runs": [ { "startedAt", "finishedAt", "complete": true, "pagesRead": 45, "apiTotal": 1107,
              "matching": 1000, "new": 3, "priceChanged": 1, "removed": 2, "reappeared": 0,
              "otherTypes": 107, "stopReason": "reached the end of the search" } ],
  "listings": [ { ...listing... } ]
}
```

Listing (one per nekretnine.hr ad; building land, typology 261, province Grad Zagreb):

| Field | Type | Meaning |
|---|---|---|
| `id` | long | nekretnine.hr ad id – the key |
| `uuid`, `url`, `title`, `caption` | string | `url` = `https://www.nekretnine.hr/oglasi/{id}/` |
| `typologyId`, `typology` | int, string | 261 = Građevinsko zemljište |
| `priceEur`, `priceText` | decimal, string | absent when the advertiser hides the price |
| `areaM2`, `surfaceText`, `pricePerM2` | decimal, string, decimal | |
| `address`, `city`, `province`, `macrozone`, `microzone` | string | macrozone ≈ city district (Črnomerec, Sesvete…) |
| `lat`, `lon` | double | WGS84; **check precision in `raw` before using it for a plot** (below) |
| `utilities` | string[] | `struja`, `voda`, `kanalizacija`, `plin`, `telefon`; unknown labels kept as the site writes them (Italian) |
| `description` | string | usually absent in the list API; phones/e-mails replaced by `[phone]` / `[e-mail]` |
| `photoCount`, `visibility`, `isNew` | | |
| `advertiserType`, `agencyId`, `agencyName`, `agencyUrl` | | the agency (a business). **No agent names or phone numbers.** |
| `firstSeen`, `lastSeen` | date | first/last run that saw the ad (the API has no publication date) |
| `removedAt` | date | set by a complete scan that no longer found the ad; removed again if it comes back |
| `priceHistory` | `[{ seenAt, priceEur }]` | one entry per distinct price |
| `raw` | object | the API record (`realEstate`, `seo`, …) with `supervisor`, `phones`, `agentPhone`, `email` removed and descriptions masked |

**Location precision** – `raw.realEstate.properties[0].location.marker`:
- `marker` – the advertiser placed a pin (≈ 80 %);
- `only_area` – the coordinates are the centre of the microzone, not the plot;
- `no_map` – address hidden; coordinates (if any) are approximate.
Many ads share identical coordinates (one agency pin for several plots). The web map shows only `marker` as exact.

## `data/listings/listings-njuskalo.json`

```jsonc
{ "note", "updated", "runs": [ { "startedAt", "finishedAt", "searchUrl", "pagesRead", "newAds", "knownAdsSeen",
                                 "unreadable", "skipped", "stopReason" } ],
  "listings": [ ... ], "resumePage": 3, "skippedIds": ["..."] }
```

Listing: `id` (Njuškalo ad number, string), `url`, `title`, `priceEur`, `areaM2`, `pricePerM2`, `landType`,
`locationText` ("Grad Zagreb, Sesvete, Kraljevec"), `lat`, `lon`, `locationSource` (`map_pin` | `none`),
`locationApproximate`, `cadastralParcel`, `cadastralMunicipality` (k.č. / k.o. if written in the ad),
`publishedText`, `details` (every labelled field as shown, e.g. `"Površina": "650,00 m²"`), `description` (masked),
`firstSeen`, `lastSeen`, `priceHistory`. No seller fields.

---

## Web API (`src/web/server.js`)

| Endpoint | Returns |
|---|---|
| `GET /api/zone?lon=&lat=` | `{ lon, lat, htrs96: {E, N}, zones: [ {id, code, name_hr, group, code_source, area_m2, rules} ] }` |
| `GET /api/stats` | `{ code: { count, area_m2 } }` |
| `GET /api/rules` | `rules.json` |
| `GET /api/listings` | GeoJSON points from `listings-nekretnine.json` (see below); empty collection with `meta.missing` when the file is absent; 503 while it can't be read |
| `GET /api/config` | `{ googleMapsApiKey, googleTileBase }` – the browser key, protect it by referrer |
| `GET /data/zones.geojson` | the zones file |

`/api/listings` feature: `id` = ad id; properties `id, url, title, caption, price, area, ppm, district, microzone,
address, utilities` (comma-joined), `agency, photos, precise` (bool), `precision` (`exact` | `area` | `hidden`),
`removed, removedAt, firstSeen, lastSeen, prevPrice, prevPriceAt, stack` (ads sharing these coordinates),
`zone` (GUP code at the point or null). `meta`: `source, updated, lastCompleteScan, total, active, located,
withoutCoordinates`. Ads without coordinates are left out.
