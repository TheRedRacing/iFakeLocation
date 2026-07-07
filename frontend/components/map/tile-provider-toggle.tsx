"use client";

import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";

interface TileProviderToggleProps {
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
}

export function TileProviderToggle({ checked, onCheckedChange }: TileProviderToggleProps) {
  return (
    <div className="flex items-center gap-2">
      <Checkbox
        id="map-provider-alt"
        checked={checked}
        onCheckedChange={(value) => onCheckedChange(value === true)}
      />
      <Label htmlFor="map-provider-alt" className="font-normal">
        Use alternative map tile server
      </Label>
    </div>
  );
}
