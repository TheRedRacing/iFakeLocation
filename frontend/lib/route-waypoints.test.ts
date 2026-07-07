import { describe, expect, it } from "vitest";
import { buildWaypointSequence, insertViaPointAtSegment, removeViaPointAt } from "./route-waypoints";

const start = { lat: 1, lng: 1 };
const end = { lat: 9, lng: 9 };
const viaA = { lat: 2, lng: 2 };
const viaB = { lat: 3, lng: 3 };

describe("buildWaypointSequence", () => {
  it("orders start, via-points, end", () => {
    expect(buildWaypointSequence(start, [viaA, viaB], end)).toEqual([start, viaA, viaB, end]);
  });

  it("omits start/end when null", () => {
    expect(buildWaypointSequence(null, [viaA], null)).toEqual([viaA]);
  });

  it("returns just start+end with no via-points", () => {
    expect(buildWaypointSequence(start, [], end)).toEqual([start, end]);
  });
});

describe("insertViaPointAtSegment", () => {
  it("inserts at the beginning (segment 0, no existing via-points)", () => {
    expect(insertViaPointAtSegment([], 0, viaA)).toEqual([viaA]);
  });

  it("inserts between two existing via-points", () => {
    const result = insertViaPointAtSegment([viaA, end], 1, viaB);
    expect(result).toEqual([viaA, viaB, end]);
  });

  it("clamps a negative index to 0", () => {
    expect(insertViaPointAtSegment([viaA], -5, viaB)).toEqual([viaB, viaA]);
  });

  it("clamps an out-of-range index to the end", () => {
    expect(insertViaPointAtSegment([viaA], 99, viaB)).toEqual([viaA, viaB]);
  });
});

describe("removeViaPointAt", () => {
  it("removes only the targeted index", () => {
    expect(removeViaPointAt([viaA, viaB], 0)).toEqual([viaB]);
  });

  it("is a no-op for an out-of-range index", () => {
    expect(removeViaPointAt([viaA], 5)).toEqual([viaA]);
  });
});
