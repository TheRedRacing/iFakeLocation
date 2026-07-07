"use client";

import { MapMarker, MapRoute, MarkerContent } from "@/components/ui/map";
import { computeSegmentHandles } from "@/lib/route-geometry";
import { buildWaypointSequence } from "@/lib/route-waypoints";
import type { RoutePointDto } from "@/lib/types";

interface RouteLayerProps {
  startPoint: RoutePointDto | null;
  endPoint: RoutePointDto | null;
  viaPoints: RoutePointDto[];
  /** The full road-following polyline returned by OSRM for the current waypoint sequence. */
  routePoints: RoutePointDto[];
  /** Live simulated position while a route simulation is running/paused. */
  currentPosition?: RoutePointDto | null;
  onInsertViaPoint: (segmentIndex: number, point: RoutePointDto) => void;
}

export function RouteLayer({ startPoint, endPoint, viaPoints, routePoints, currentPosition, onInsertViaPoint }: RouteLayerProps) {
  const waypoints = buildWaypointSequence(startPoint, viaPoints, endPoint);
  const segmentHandles = computeSegmentHandles(waypoints, routePoints);

  return (
    <>
      {routePoints.length >= 2 && (
        <MapRoute coordinates={routePoints.map((p) => [p.lng, p.lat])} color="#2563eb" width={4} interactive={false} />
      )}

      {startPoint && (
        <MapMarker longitude={startPoint.lng} latitude={startPoint.lat}>
          <MarkerContent>
            <div className="h-4 w-4 rounded-full border-2 border-white bg-emerald-500 shadow-lg" />
          </MarkerContent>
        </MapMarker>
      )}

      {endPoint && (
        <MapMarker longitude={endPoint.lng} latitude={endPoint.lat}>
          <MarkerContent>
            <div className="h-4 w-4 rounded-full border-2 border-white bg-rose-500 shadow-lg" />
          </MarkerContent>
        </MapMarker>
      )}

      {viaPoints.map((point, index) => (
        <MapMarker key={`via-${index}-${point.lat}-${point.lng}`} longitude={point.lng} latitude={point.lat}>
          <MarkerContent>
            <div className="h-3 w-3 rounded-full border-2 border-white bg-amber-500 shadow" />
          </MarkerContent>
        </MapMarker>
      ))}

      {/* Draggable "insert a waypoint here" handles -- one per leg, dropped position becomes a
          new via-point in the correct sequence order (Google Maps/Strava-style route editing). */}
      {segmentHandles.map((handle, segmentIndex) => (
        <MapMarker
          key={`handle-${segmentIndex}-${handle.lat}-${handle.lng}`}
          longitude={handle.lng}
          latitude={handle.lat}
          draggable
          onDragEnd={(lngLat) => onInsertViaPoint(segmentIndex, { lat: lngLat.lat, lng: lngLat.lng })}
        >
          <MarkerContent>
            <div className="h-2.5 w-2.5 rounded-full border border-white bg-blue-300 opacity-80 shadow" />
          </MarkerContent>
        </MapMarker>
      ))}

      {currentPosition && (
        <MapMarker longitude={currentPosition.lng} latitude={currentPosition.lat}>
          <MarkerContent>
            <div className="h-5 w-5 animate-pulse rounded-full border-2 border-white bg-blue-600 shadow-lg" />
          </MarkerContent>
        </MapMarker>
      )}
    </>
  );
}
