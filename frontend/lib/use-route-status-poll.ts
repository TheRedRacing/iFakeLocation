"use client";

import { useEffect, useRef, useState } from "react";
import { apiClient, ApiError } from "@/lib/api-client";
import type { RouteStatusResponse } from "@/lib/types";

/**
 * Polls GET /api/devices/{udid}/route/status at a ~1s interval while `active`, mirroring the
 * cadence/shape of the existing download-progress poll. Chosen over SSE/WebSocket for
 * consistency with that existing pattern and because it needs zero extra infrastructure with a
 * statically-exported frontend -- see ARCHITECTURE.md.
 *
 * `onStatus` (e.g. to react to the simulation reaching "completed") is invoked from inside the
 * poll itself rather than left for the caller to derive via a separate effect watching `status`,
 * which the react-hooks/set-state-in-effect rule flags as cascading-render-prone.
 */
export function useRouteStatusPoll(
  udid: string | null,
  active: boolean,
  onStatus?: (status: RouteStatusResponse) => void,
  intervalMs = 1000,
) {
  const [status, setStatus] = useState<RouteStatusResponse | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const onStatusRef = useRef(onStatus);
  useEffect(() => {
    onStatusRef.current = onStatus;
  });

  useEffect(() => {
    if (!udid || !active) {
      return;
    }

    let cancelled = false;

    const poll = async () => {
      try {
        const result = await apiClient.getRouteStatus(udid);
        if (!cancelled) {
          setStatus(result);
          setError(null);
          onStatusRef.current?.(result);
        }
      } catch (err) {
        if (cancelled) return;

        // No active session (stopped, completed and cleared, or never started) isn't a real error.
        if (err instanceof ApiError && err.status === 404) {
          setStatus(null);
        } else {
          setError(err instanceof Error ? err : new Error(String(err)));
        }
      }
    };

    void poll();
    const id = setInterval(() => void poll(), intervalMs);

    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, [udid, active, intervalMs]);

  return { status, error };
}
