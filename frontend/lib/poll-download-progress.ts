import { apiClient } from "@/lib/api-client";
import type { DownloadProgressResponse } from "@/lib/types";

/**
 * Polls GET /api/downloads/{iosVersion}/progress every 250ms (matching the original app's
 * polling cadence) until the download completes, then resolves. Rejects if the backend reports
 * an error. Not a React hook -- this models a one-shot "wait for this download, then continue"
 * flow triggered by a button click, not a continuously-rendered live view.
 */
export function pollDownloadProgress(
  iosVersion: string,
  onUpdate: (progress: DownloadProgressResponse) => void,
  signal?: AbortSignal,
): Promise<void> {
  const intervalMs = 250;

  return new Promise((resolve, reject) => {
    const tick = async () => {
      if (signal?.aborted) {
        reject(new DOMException("Aborted", "AbortError"));
        return;
      }

      try {
        const progress = await apiClient.getDownloadProgress(iosVersion);
        onUpdate(progress);

        if (progress.done) {
          resolve();
        } else {
          setTimeout(tick, intervalMs);
        }
      } catch (error) {
        reject(error);
      }
    };

    void tick();
  });
}
