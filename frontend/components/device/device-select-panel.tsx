"use client";

import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { DeviceDto } from "@/lib/types";

interface DeviceSelectPanelProps {
  devices: DeviceDto[];
  selectedUdid: string | null;
  onSelectUdid: (udid: string) => void;
  onRefresh: () => void;
  refreshing: boolean;
}

export function DeviceSelectPanel({ devices, selectedUdid, onSelectUdid, onRefresh, refreshing }: DeviceSelectPanelProps) {
  return (
    <div className="space-y-2">
      <Label htmlFor="device-select">Device Name:</Label>
      <div className="flex gap-2">
        <Select value={selectedUdid ?? undefined} onValueChange={(value) => value && onSelectUdid(value)}>
          <SelectTrigger id="device-select" className="w-full">
            <SelectValue placeholder={devices.length ? "Select a device" : "No devices found"} />
          </SelectTrigger>
          <SelectContent>
            {devices.map((device) => (
              <SelectItem key={device.udid} value={device.udid}>
                {device.displayName}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Button type="button" onClick={onRefresh} disabled={refreshing} className="shrink-0">
          Refresh
        </Button>
      </div>
    </div>
  );
}
