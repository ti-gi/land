// Zone codes: colour, Croatian name, English gloss, family (legend group)
export const FAMILIES = [
  { id: 'stambena', label: 'Stambena' },
  { id: 'mjesovita', label: 'Mješovita' },
  { id: 'javna', label: 'Javna i društvena' },
  { id: 'gospodarska', label: 'Gospodarska i posebna' },
  { id: 'sport', label: 'Sport i rekreacija' },
  { id: 'zelene', label: 'Zelene površine' },
  { id: 'vode', label: 'Vode i infrastruktura' },
  { id: 'ostalo', label: 'Neodređeno' },
];

export const ZONES = {
  S:   { color: '#f6e05e', fam: 'stambena', hr: 'Stambena namjena', en: 'Residential' },

  M:   { color: '#f0a050', fam: 'mjesovita', hr: 'Mješovita namjena', en: 'Mixed use' },
  M0:  { color: '#d9772b', fam: 'mjesovita', hr: 'Mješovita namjena gradskog središta', en: 'Mixed – city centre' },
  M1:  { color: '#f6be85', fam: 'mjesovita', hr: 'Mješovita – pretežito stambena', en: 'Mixed – mainly residential' },
  M2:  { color: '#e5873f', fam: 'mjesovita', hr: 'Mješovita – pretežito poslovna', en: 'Mixed – mainly business' },
  TR:  { color: '#b5652a', fam: 'mjesovita', hr: 'Tržnica', en: 'Market' },

  D:   { color: '#e06666', fam: 'javna', hr: 'Javna i društvena namjena', en: 'Public & social (all)' },
  D1:  { color: '#c94545', fam: 'javna', hr: 'Javna i društvena – upravna', en: 'Administrative' },
  D2:  { color: '#ea8a90', fam: 'javna', hr: 'Javna i društvena – socijalna', en: 'Social care' },
  D3:  { color: '#b83226', fam: 'javna', hr: 'Javna i društvena – zdravstvena', en: 'Health' },
  D4:  { color: '#f2a29a', fam: 'javna', hr: 'Javna i društvena – predškolska', en: 'Pre-school' },
  D5:  { color: '#ec7063', fam: 'javna', hr: 'Javna i društvena – školska', en: 'School' },
  D6:  { color: '#9c3328', fam: 'javna', hr: 'Javna i društvena – visoko učilište i znanost', en: 'University & science' },
  D7:  { color: '#f5bdb7', fam: 'javna', hr: 'Javna i društvena – kulturna', en: 'Culture' },
  D8:  { color: '#cd6155', fam: 'javna', hr: 'Javna i društvena – vjerska', en: 'Religious' },

  G:   { color: '#a569bd', fam: 'gospodarska', hr: 'Gospodarska namjena', en: 'Economic (all)' },
  I:   { color: '#7d3c98', fam: 'gospodarska', hr: 'Gospodarska – proizvodna', en: 'Industrial' },
  K1:  { color: '#b04db0', fam: 'gospodarska', hr: 'Gospodarska – poslovna', en: 'Business' },
  K2:  { color: '#c77dc7', fam: 'gospodarska', hr: 'Gospodarska – trgovački kompleksi', en: 'Retail complexes' },
  T:   { color: '#dda0dd', fam: 'gospodarska', hr: 'Gospodarska – ugostiteljsko-turistička', en: 'Hospitality & tourism' },
  N:   { color: '#5d4037', fam: 'gospodarska', hr: 'Posebna namjena', en: 'Special purpose (defence etc.)' },

  R:   { color: '#48c9b0', fam: 'sport', hr: 'Sportsko-rekreacijska namjena', en: 'Sport & recreation' },
  R1:  { color: '#2fb59b', fam: 'sport', hr: 'Sport s gradnjom', en: 'Sport with buildings' },
  R2:  { color: '#9be0d0', fam: 'sport', hr: 'Sport bez gradnje', en: 'Sport without buildings' },

  Z:   { color: '#b5e3c4', fam: 'zelene', hr: 'Zaštitne zelene površine', en: 'Protective green' },
  Z1:  { color: '#58d68d', fam: 'zelene', hr: 'Javni park', en: 'Public park' },
  Z2:  { color: '#1e8449', fam: 'zelene', hr: 'Gradske park-šume', en: 'City park-forest' },
  Z3:  { color: '#7dcea0', fam: 'zelene', hr: 'Tematski park', en: 'Theme park' },
  Z4:  { color: '#a3d9a5', fam: 'zelene', hr: 'Javne gradske površine – tematske zone', en: 'Thematic public areas' },
  ZIS: { color: '#c9e8b8', fam: 'zelene', hr: 'Površine zelenog i infrastrukturnog sustava', en: 'Green & infrastructure corridor' },

  V:   { color: '#5dade2', fam: 'vode', hr: 'Vode i vodna dobra', en: 'Water' },
  V1:  { color: '#2e86c1', fam: 'vode', hr: 'Površine pod vodom', en: 'Permanent water' },
  V2:  { color: '#6fb8e8', fam: 'vode', hr: 'Površine povremeno pod vodom', en: 'Periodically flooded' },
  V3:  { color: '#93c9ee', fam: 'vode', hr: 'Vodotok sa zaštitnim koridorom', en: 'Stream with corridor' },
  IS:  { color: '#8c969c', fam: 'vode', hr: 'Površine infrastrukturnih sustava', en: 'Infrastructure' },
};

export const UNKNOWN_COLOR = '#c8c8c8';

export function zoneInfo(code) {
  if (ZONES[code]) return ZONES[code];
  return { color: UNKNOWN_COLOR, fam: 'ostalo', hr: 'Neodređena oznaka (provjeriti na karti)', en: 'Unresolved code' };
}

// MapLibre "match" expression: code → colour
export function colorExpression(codes) {
  const expr = ['match', ['get', 'code']];
  for (const c of codes) expr.push(c, zoneInfo(c).color);
  expr.push(UNKNOWN_COLOR);
  return expr;
}
