import type { RoutePointDto } from "@/lib/types";

// Public OSRM demo server, called directly from the browser (CORS-enabled, no API key). Chosen
// over other free routing options because it's the same ecosystem as the Nominatim geocoder
// already in use, and needs no signup. Usage is capped at ~1 request/second and is intended for
// "reasonable, non-commercial" use per FOSSGIS's demo-server policy -- for heavier use, point
// OSRM_BASE_URL at a self-hosted instance (this is a build-time constant, not a runtime env var,
// since a static export has no server to read environment variables at request time).
const OSRM_BASE_URL = "https://router.project-osrm.org";

export type RouteProfile = "driving" | "foot" | "bike";

/**
 * Approximates which OSRM profile (road-network geometry) best matches a simulated travel
 * speed, so the drawn route follows footpaths/cycleways/roads appropriate to the chosen pace
 * without needing a separate profile selector in the UI.
 */
export function resolveOsrmProfile(speedKmh: number): RouteProfile {
  if (speedKmh <= 8) return "foot";
  if (speedKmh <= 25) return "bike";
  return "driving";
}

export interface RouteResult {
  /** Road-following polyline, in the same order as the input waypoints. */
  points: RoutePointDto[];
  distanceMeters: number;
  durationSeconds: number;
}

interface OsrmRouteResponse {
  code: string;
  message?: string;
  routes: Array<{
    geometry: { type: "LineString"; coordinates: [number, number][] };
    distance: number;
    duration: number;
  }>;
}

/**
 * Computes a road-following route through an ordered list of waypoints
 * (start, then any via-points in the order they should be visited, then end).
 */
export async function route(
  waypoints: RoutePointDto[],
  profile: RouteProfile = "driving",
  signal?: AbortSignal,
): Promise<RouteResult> {
  if (waypoints.length < 2) {
    throw new Error("At least a start and end point are required to compute a route.");
  }

  const coordinates = waypoints.map((p) => `${p.lng},${p.lat}`).join(";");
  const url = new URL(`${OSRM_BASE_URL}/route/v1/${profile}/${coordinates}`);
  url.searchParams.set("overview", "full");
  url.searchParams.set("geometries", "geojson");

  const response = await fetch(url, { signal });
  const body = (await response.json()) as OsrmRouteResponse;

  if (!response.ok || body.code !== "Ok" || body.routes.length === 0) {
    throw new Error(body.message ?? `OSRM routing failed (code: ${body.code ?? response.status}).`);
  }

  const bestRoute = body.routes[0];
  return {
    points: bestRoute.geometry.coordinates.map(([lng, lat]) => ({ lat, lng })),
    distanceMeters: bestRoute.distance,
    durationSeconds: bestRoute.duration,
  };
}
