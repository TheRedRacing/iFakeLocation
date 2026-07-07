"use client";

import { Dialog, DialogContent } from "@/components/ui/dialog";
import { Progress } from "@/components/ui/progress";
import type { DownloadProgressResponse } from "@/lib/types";

interface DownloadProgressDialogProps {
  progress: DownloadProgressResponse | null;
}

/** Non-dismissible while a developer-image download is in progress -- matches the original app. */
export function DownloadProgressDialog({ progress }: DownloadProgressDialogProps) {
  const open = progress !== null && !progress.done;

  return (
    <Dialog open={open} disablePointerDismissal modal>
      <DialogContent showCloseButton={false}>
        <p className="text-sm">
          Downloading: {progress?.fileName ?? "…"} ({(progress?.progressPercent ?? 0).toFixed(2)}%)
        </p>
        <Progress value={progress?.progressPercent ?? 0} />
      </DialogContent>
    </Dialog>
  );
}
