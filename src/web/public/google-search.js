// Address search with Google, limited to the City of Zagreb:
//  - suggestions while typing: Places API (New) autocomplete, grouped into a billing session that ends when a
//    suggestion is picked (the pick fetches only the place's location);
//  - Enter without picking a suggestion: Geocoding.
// Both run through the Maps JavaScript API with the same browser key as the map tiles, so the key's project needs
// "Maps JavaScript API", "Places API (New)" and "Geocoding API" enabled (and allowed in the key's API restrictions).

const ZAGREB = { north: 45.97, south: 45.70, east: 16.25, west: 15.75 };
const inZagreb = (lat, lon) => lat <= ZAGREB.north && lat >= ZAGREB.south && lon <= ZAGREB.east && lon >= ZAGREB.west;

let loading = null;

/** Loads the Maps JavaScript API once (only its loader; libraries come with importLibrary). */
function loadMapsJs(key) {
  if (window.google?.maps?.importLibrary) return Promise.resolve();
  loading ??= new Promise((resolve, reject) => {
    window.__gmapsSearchReady = () => resolve();
    const s = document.createElement('script');
    s.src = 'https://maps.googleapis.com/maps/api/js?' + new URLSearchParams({
      key, v: 'weekly', loading: 'async', language: 'hr', region: 'HR', callback: '__gmapsSearchReady',
    });
    s.async = true;
    s.onerror = () => { loading = null; reject(new Error('Google Maps could not be loaded (no internet connection?)')); };
    document.head.appendChild(s);
  });
  return loading;
}

/** Turns Google's error into a sentence the person can act on. */
export function describeSearchError(err) {
  const msg = String(err?.message || err);
  if (/ApiNotActivated|not activated|REQUEST_DENIED|not authorized|ApiTargetBlocked|PERMISSION_DENIED/i.test(msg)) {
    return 'Google search is not enabled for this key – enable "Places API (New)" and "Geocoding API" in Google Cloud Console.';
  }
  if (/RefererNotAllowed|referer/i.test(msg)) return 'This page\'s address is not allowed by the key\'s website restrictions.';
  return 'Search is unavailable right now.';
}

export class GoogleSearch {
  constructor(apiKey) {
    this.key = apiKey || null;
    this.token = null;
  }

  get available() { return Boolean(this.key); }

  async #lib(name) {
    await loadMapsJs(this.key);
    return google.maps.importLibrary(name);
  }

  /** Suggestions for what has been typed so far. */
  async suggest(input) {
    const { AutocompleteSuggestion, AutocompleteSessionToken } = await this.#lib('places');
    this.token ??= new AutocompleteSessionToken();
    const { suggestions } = await AutocompleteSuggestion.fetchAutocompleteSuggestions({
      input, sessionToken: this.token, includedRegionCodes: ['hr'], locationRestriction: ZAGREB, language: 'hr', region: 'hr',
    });
    return suggestions.filter((s) => s.placePrediction).map((s) => {
      const p = s.placePrediction;
      return { main: p.mainText?.toString() || p.text.toString(), secondary: p.secondaryText?.toString() || '', prediction: p };
    });
  }

  /** Location of a picked suggestion. Ends the billing session. */
  async locate(suggestion) {
    const place = suggestion.prediction.toPlace();
    await place.fetchFields({ fields: ['location', 'formattedAddress'] });
    this.token = null;
    return { lat: place.location.lat(), lon: place.location.lng(), label: place.formattedAddress || suggestion.main };
  }

  /** Direct address lookup (Enter without a picked suggestion). Only results inside Zagreb. */
  async geocode(address) {
    const { Geocoder } = await this.#lib('geocoding');
    try {
      const { results } = await new Geocoder().geocode({
        address, bounds: ZAGREB, componentRestrictions: { country: 'HR' }, language: 'hr',
      });
      return results
        .map((r) => ({ label: r.formatted_address, lat: r.geometry.location.lat(), lon: r.geometry.location.lng() }))
        .filter((r) => inZagreb(r.lat, r.lon));
    } catch (err) {
      if (/ZERO_RESULTS/.test(String(err?.code || err?.message || err))) return [];
      throw err;
    }
  }
}
