"use client";

import { useEffect, type ReactNode } from "react";
import type MapLibreGL from "maplibre-gl";
import { Map, useMap, type MapRef } from "@/components/ui/map";

// Matches the original app's "alternative map tile server" checkbox (a raster fallback in case
// the default provider is rate-limited/blocked). Kept as a plain raster MapLibre style so the
// same XYZ tile URL the original used still works.
const alternativeTileStyle: MapLibreGL.StyleSpecification = {
  version: 8,
  sources: {
    "osm-ch": {
      type: "raster",
      tiles: ["https://tile.osm.ch/switzerland/{z}/{x}/{y}.png"],
      tileSize: 256,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    },
  },
  layers: [{ id: "osm-ch", type: "raster", source: "osm-ch" }],
};

function DoubleClickListener({ onDoubleClick }: { onDoubleClick?: (point: { lat: number; lng: number }) => void }) {
  const { map } = useMap();

  useEffect(() => {
    if (!map || !onDoubleClick) return;

    const handler = (e: MapLibreGL.MapMouseEvent) => onDoubleClick({ lat: e.lngLat.lat, lng: e.lngLat.lng });
    map.on("dblclick", handler);
    return () => {
      map.off("dblclick", handler);
    };
  }, [map, onDoubleClick]);

  return null;
}

interface MapViewProps {
  children?: ReactNode;
  useAlternativeTiles: boolean;
  onDoubleClick?: (point: { lat: number; lng: number }) => void;
  mapRef?: React.Ref<MapRef>;
}

// Deliberately uncontrolled (no `viewport`/`onViewportChange`): this app only ever needs one-shot
// "fly the map to this point" actions (initial home-country center, search results), which are
// simpler and more reliable done imperatively via `mapRef` than through mapcn's controlled-
// viewport mode, which is intended for continuously syncing pan/zoom state.
export function MapView({ children, useAlternativeTiles, onDoubleClick, mapRef }: MapViewProps) {
  return (
    <Map
      ref={mapRef}
      className="h-[500px] max-h-[70vh] w-full rounded-md border"
      styles={
        useAlternativeTiles
          ? { light: alternativeTileStyle, dark: alternativeTileStyle }
          : undefined
      }
      doubleClickZoom={false}
    >
      <DoubleClickListener onDoubleClick={onDoubleClick} />
      {children}
    </Map>
  );
}
