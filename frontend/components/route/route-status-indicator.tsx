"use client";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Progress } from "@/components/ui/progress";
import type { RouteStatusResponse } from "@/lib/types";

interface RouteStatusIndicatorProps {
  status: RouteStatusResponse;
  onPause: () => void;
  onResume: () => void;
  onStop: () => void;
}

export function RouteStatusIndicator({ status, onPause, onResume, onStop }: RouteStatusIndicatorProps) {
  return (
    <div className="space-y-3 rounded-md border p-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-medium">Route Simulation</h3>
        <Badge variant={status.state === "running" ? "default" : "secondary"}>{status.state}</Badge>
      </div>

      <Progress value={status.progressPercent} />
      <p className="text-muted-foreground text-xs">
        {Math.round(status.elapsedSeconds)}s / {Math.round(status.totalSeconds)}s ({status.progressPercent.toFixed(1)}%)
      </p>

      <div className="flex gap-2">
        {status.state === "running" && (
          <Button type="button" variant="secondary" onClick={onPause}>
            Pause
          </Button>
        )}
        {status.state === "paused" && (
          <Button type="button" variant="secondary" onClick={onResume}>
            Resume
          </Button>
        )}
        <Button type="button" variant="destructive" onClick={onStop}>
          Stop
        </Button>
      </div>
    </div>
  );
}
