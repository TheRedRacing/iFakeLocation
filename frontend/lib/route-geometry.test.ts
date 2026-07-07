import { describe, expect, it } from "vitest";
import { computeSegmentHandles, findNearestRoutePoint } from "./route-geometry";

describe("findNearestRoutePoint", () => {
  it("returns the closest point", () => {
    const points = [{ lat: 0, lng: 0 }, { lat: 5, lng: 5 }, { lat: 10, lng: 10 }];
    expect(findNearestRoutePoint(points, { lat: 4, lng: 4 })).toEqual({ lat: 5, lng: 5 });
  });

  it("returns the exact match when present", () => {
    const points = [{ lat: 1, lng: 2 }, { lat: 3, lng: 4 }];
    expect(findNearestRoutePoint(points, { lat: 3, lng: 4 })).toEqual({ lat: 3, lng: 4 });
  });
});

describe("computeSegmentHandles", () => {
  it("returns one handle per leg between consecutive waypoints", () => {
    const waypoints = [{ lat: 0, lng: 0 }, { lat: 10, lng: 10 }, { lat: 20, lng: 0 }];
    const routePoints = [
      { lat: 0, lng: 0 }, { lat: 5, lng: 5 }, { lat: 10, lng: 10 },
      { lat: 15, lng: 5 }, { lat: 20, lng: 0 },
    ];

    const handles = computeSegmentHandles(waypoints, routePoints);

    expect(handles).toHaveLength(2);
    expect(handles[0]).toEqual({ lat: 5, lng: 5 });
    expect(handles[1]).toEqual({ lat: 15, lng: 5 });
  });

  it("returns an empty array with fewer than two waypoints", () => {
    expect(computeSegmentHandles([{ lat: 0, lng: 0 }], [{ lat: 0, lng: 0 }])).toEqual([]);
  });

  it("returns an empty array with no route points", () => {
    expect(computeSegmentHandles([{ lat: 0, lng: 0 }, { lat: 1, lng: 1 }], [])).toEqual([]);
  });
});
