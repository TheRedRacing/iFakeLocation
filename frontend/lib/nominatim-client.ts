// OSM Nominatim geocoding, called directly from the browser -- same provider the original app
// used (via leaflet-geosearch) for its location search box. Subject to Nominatim's usage policy
// (max ~1 request/second, requires an identifying User-Agent or Referer -- the browser supplies
// Referer automatically). For heavier use, self-hosting Nominatim is the documented escape hatch.
const NOMINATIM_BASE_URL = "https://nominatim.openstreetmap.org";

export interface GeocodeResult {
  lat: number;
  lng: number;
  displayName: string;
}

interface NominatimSearchResult {
  lat: string;
  lon: string;
  display_name: string;
  /** OSM's own relevance score (0-1). Nominatim's default result order does NOT reliably sort by
   * this -- e.g. searching "Switzerland" returns "Switzerland County, Indiana" (importance 0.53)
   * ahead of the country "Suisse" (importance 0.89) if the response happens to be locale-shifted.
   * Always re-sort by importance ourselves rather than trusting result order. */
  importance?: number;
}

export async function geocode(query: string, signal?: AbortSignal): Promise<GeocodeResult[]> {
  if (!query.trim()) {
    return [];
  }

  const url = new URL(`${NOMINATIM_BASE_URL}/search`);
  url.searchParams.set("q", query);
  url.searchParams.set("format", "json");
  url.searchParams.set("limit", "5");

  const response = await fetch(url, { signal });
  if (!response.ok) {
    throw new Error(`Nominatim search failed with status ${response.status}`);
  }

  const results = (await response.json()) as NominatimSearchResult[];
  return results
    .slice()
    .sort((a, b) => (b.importance ?? 0) - (a.importance ?? 0))
    .map((r) => ({
      lat: parseFloat(r.lat),
      lng: parseFloat(r.lon),
      displayName: r.display_name,
    }));
}
