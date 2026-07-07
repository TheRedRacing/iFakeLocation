import type { RoutePointDto } from "@/lib/types";

/** Builds the full ordered waypoint list (start -> via-points in order -> end) for an OSRM request. */
export function buildWaypointSequence(
  start: RoutePointDto | null,
  viaPoints: RoutePointDto[],
  end: RoutePointDto | null,
): RoutePointDto[] {
  const sequence: RoutePointDto[] = [];
  if (start) sequence.push(start);
  sequence.push(...viaPoints);
  if (end) sequence.push(end);
  return sequence;
}

/**
 * Inserts a new via-point at the given segment index (0 = the segment right after `start`,
 * i.e. before the first existing via-point). Used when the user drags a point on the drawn
 * route to insert a waypoint at that segment, Google-Maps/Strava-route-editing style.
 */
export function insertViaPointAtSegment(
  viaPoints: RoutePointDto[],
  segmentIndex: number,
  point: RoutePointDto,
): RoutePointDto[] {
  const clampedIndex = Math.max(0, Math.min(segmentIndex, viaPoints.length));
  const next = [...viaPoints];
  next.splice(clampedIndex, 0, point);
  return next;
}

/** Removes the via-point at the given index (e.g. double-click to delete a waypoint handle). */
export function removeViaPointAt(viaPoints: RoutePointDto[], index: number): RoutePointDto[] {
  return viaPoints.filter((_, i) => i !== index);
}
