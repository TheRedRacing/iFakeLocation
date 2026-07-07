import type { RoutePointDto } from "@/lib/types";

function squaredDistance(a: RoutePointDto, b: RoutePointDto): number {
  const dLat = a.lat - b.lat;
  const dLng = a.lng - b.lng;
  return dLat * dLat + dLng * dLng;
}

/** Finds the point in `routePoints` closest to `target` (simple nearest-vertex snap). */
export function findNearestRoutePoint(routePoints: RoutePointDto[], target: RoutePointDto): RoutePointDto {
  let closest = routePoints[0];
  let closestDistance = Infinity;

  for (const point of routePoints) {
    const distance = squaredDistance(point, target);
    if (distance < closestDistance) {
      closestDistance = distance;
      closest = point;
    }
  }

  return closest;
}

/**
 * One draggable "insert a waypoint here" handle per leg between two consecutive named waypoints
 * (start -> via-points -> end), positioned at that leg's straight-line midpoint then snapped onto
 * the nearest point of the actual road-following polyline -- an approximation of the true
 * road-network midpoint that avoids an extra OSRM request per leg against the rate-limited public
 * demo server.
 */
export function computeSegmentHandles(
  waypoints: RoutePointDto[],
  routePoints: RoutePointDto[],
): RoutePointDto[] {
  if (waypoints.length < 2 || routePoints.length === 0) {
    return [];
  }

  const handles: RoutePointDto[] = [];
  for (let i = 0; i < waypoints.length - 1; i++) {
    const a = waypoints[i];
    const b = waypoints[i + 1];
    const straightLineMidpoint: RoutePointDto = { lat: (a.lat + b.lat) / 2, lng: (a.lng + b.lng) / 2 };
    handles.push(findNearestRoutePoint(routePoints, straightLineMidpoint));
  }

  return handles;
}
