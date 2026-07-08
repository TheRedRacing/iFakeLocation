"use client";

import { Dialog, DialogContent } from "@/components/ui/dialog";
import { Progress } from "@/components/ui/progress";

interface PreparingDeviceDialogProps {
  preparing: boolean;
}

/**
 * Shown while the backend is readying a device (developer-mode toggle check + image mount).
 * Non-dismissible, matching the original app's download-progress modal -- but indeterminate
 * rather than a percentage: pymobiledevice3's mount step resolves/downloads/mounts the image as
 * one call with no byte-level progress to report (see ARCHITECTURE.md).
 */
export function DownloadProgressDialog({ preparing }: PreparingDeviceDialogProps) {
  return (
    <Dialog open={preparing} disablePointerDismissal modal>
      <DialogContent showCloseButton={false}>
        <p className="text-sm">Preparing device… this can take a little while the first time.</p>
        <Progress value={null} />
      </DialogContent>
    </Dialog>
  );
}
