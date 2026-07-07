"use client";

import { MapMarker, MarkerContent } from "@/components/ui/map";
import type { RoutePointDto } from "@/lib/types";

interface LocationMarkerProps {
  position: RoutePointDto;
  onMove: (position: RoutePointDto) => void;
}

/** The draggable pin representing the manually-selected "Set Fake Location" target. */
export function LocationMarker({ position, onMove }: LocationMarkerProps) {
  return (
    <MapMarker
      longitude={position.lng}
      latitude={position.lat}
      draggable
      onDragEnd={(lngLat) => onMove({ lat: lngLat.lat, lng: lngLat.lng })}
    >
      <MarkerContent>
        <div className="h-4 w-4 rounded-full border-2 border-white bg-red-500 shadow-lg" />
      </MarkerContent>
    </MapMarker>
  );
}
