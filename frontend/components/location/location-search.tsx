"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { geocode } from "@/lib/nominatim-client";
import type { RoutePointDto } from "@/lib/types";

interface LocationSearchProps {
  onResult: (point: RoutePointDto) => void;
  onError: (message: string) => void;
}

export function LocationSearch({ onResult, onError }: LocationSearchProps) {
  const [query, setQuery] = useState("");
  const [searching, setSearching] = useState(false);

  const search = async () => {
    if (!query.trim() || searching) return;

    setSearching(true);
    try {
      const results = await geocode(query);
      if (results.length > 0) {
        onResult({ lat: results[0].lat, lng: results[0].lng });
      } else {
        onError(`No results found for "${query}".`);
      }
    } catch {
      onError("Location search failed. Please try again.");
    } finally {
      setSearching(false);
    }
  };

  return (
    <form
      className="flex items-end gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        void search();
      }}
    >
      <div className="flex-1 space-y-2">
        <Label htmlFor="location-search-query">Location Search:</Label>
        <Input
          id="location-search-query"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Search for a place or address"
        />
      </div>
      <Button type="submit" disabled={searching}>
        Search
      </Button>
    </form>
  );
}
