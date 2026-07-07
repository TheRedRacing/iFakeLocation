"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { geocode } from "@/lib/nominatim-client";
import type { RoutePointDto } from "@/lib/types";

const SPEED_PRESETS = {
  walk: 5,
  bike: 15,
  car: 50,
} as const;

type SpeedPresetKey = keyof typeof SPEED_PRESETS | "custom";

interface RoutePlannerPanelProps {
  onGeocodeStart: (point: RoutePointDto) => void;
  onGeocodeEnd: (point: RoutePointDto) => void;
  onError: (message: string) => void;
  speedKmh: number;
  onSpeedChange: (speedKmh: number) => void;
  loop: boolean;
  onLoopChange: (loop: boolean) => void;
  canPlanRoute: boolean;
  onPlanRoute: () => void;
  planningRoute: boolean;
  canStartSimulation: boolean;
  onStartSimulation: () => void;
}

async function geocodeSingle(query: string): Promise<RoutePointDto | null> {
  const results = await geocode(query);
  return results.length > 0 ? { lat: results[0].lat, lng: results[0].lng } : null;
}

export function RoutePlannerPanel({
  onGeocodeStart,
  onGeocodeEnd,
  onError,
  speedKmh,
  onSpeedChange,
  loop,
  onLoopChange,
  canPlanRoute,
  onPlanRoute,
  planningRoute,
  canStartSimulation,
  onStartSimulation,
}: RoutePlannerPanelProps) {
  const [startAddress, setStartAddress] = useState("");
  const [endAddress, setEndAddress] = useState("");
  const [preset, setPreset] = useState<SpeedPresetKey>("walk");
  const [geocoding, setGeocoding] = useState<"start" | "end" | null>(null);

  const handleGeocode = async (kind: "start" | "end") => {
    const address = kind === "start" ? startAddress : endAddress;
    if (!address.trim()) return;

    setGeocoding(kind);
    try {
      const point = await geocodeSingle(address);
      if (!point) {
        onError(`No results found for "${address}".`);
        return;
      }
      (kind === "start" ? onGeocodeStart : onGeocodeEnd)(point);
    } catch {
      onError("Address lookup failed. Please try again.");
    } finally {
      setGeocoding(null);
    }
  };

  const handlePresetChange = (value: SpeedPresetKey) => {
    setPreset(value);
    if (value !== "custom") {
      onSpeedChange(SPEED_PRESETS[value]);
    }
  };

  return (
    <div className="space-y-4 rounded-md border p-4">
      <h3 className="text-sm font-medium">Route Planning</h3>

      <div className="grid gap-3 sm:grid-cols-2">
        <div className="space-y-2">
          <Label htmlFor="route-start">Start address</Label>
          <div className="flex gap-2">
            <Input
              id="route-start"
              value={startAddress}
              onChange={(e) => setStartAddress(e.target.value)}
              placeholder="Starting address"
            />
            <Button type="button" variant="secondary" disabled={geocoding !== null} onClick={() => void handleGeocode("start")}>
              Set
            </Button>
          </div>
        </div>

        <div className="space-y-2">
          <Label htmlFor="route-end">End address</Label>
          <div className="flex gap-2">
            <Input
              id="route-end"
              value={endAddress}
              onChange={(e) => setEndAddress(e.target.value)}
              placeholder="Destination address"
            />
            <Button type="button" variant="secondary" disabled={geocoding !== null} onClick={() => void handleGeocode("end")}>
              Set
            </Button>
          </div>
        </div>
      </div>

      <div className="flex flex-wrap items-end gap-4">
        <div className="space-y-2">
          <Label>Speed</Label>
          <Select value={preset} onValueChange={(value) => handlePresetChange(value as SpeedPresetKey)}>
            <SelectTrigger className="w-40">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="walk">Walking (5 km/h)</SelectItem>
              <SelectItem value="bike">Bicycle (15 km/h)</SelectItem>
              <SelectItem value="car">Car (50 km/h)</SelectItem>
              <SelectItem value="custom">Custom</SelectItem>
            </SelectContent>
          </Select>
        </div>

        {preset === "custom" && (
          <div className="space-y-2">
            <Label htmlFor="route-speed-custom">Speed (km/h)</Label>
            <Input
              id="route-speed-custom"
              type="number"
              min={1}
              value={speedKmh}
              onChange={(e) => onSpeedChange(Number(e.target.value))}
              className="w-28"
            />
          </div>
        )}

        <div className="flex items-center gap-2">
          <Checkbox id="route-loop" checked={loop} onCheckedChange={(value) => onLoopChange(value === true)} />
          <Label htmlFor="route-loop" className="font-normal">
            Loop back to start
          </Label>
        </div>
      </div>

      <div className="flex gap-2">
        <Button type="button" variant="secondary" disabled={!canPlanRoute || planningRoute} onClick={onPlanRoute}>
          {planningRoute ? "Calculating route…" : "Calculate Route"}
        </Button>
        <Button type="button" disabled={!canStartSimulation} onClick={onStartSimulation}>
          Follow Route
        </Button>
      </div>
    </div>
  );
}
