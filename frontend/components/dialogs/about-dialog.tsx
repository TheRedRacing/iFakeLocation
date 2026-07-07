"use client";

import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";

interface AboutDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  version: string | null;
}

export function AboutDialog({ open, onOpenChange, version }: AboutDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>About iFakeLocation</DialogTitle>
        </DialogHeader>
        <p className="text-sm">
          Author: master131 (original), rewritten with .NET 10 + Next.js
          <br />
          Version: {version ?? "…"}
          <br />
          <a
            href="https://github.com/master131/iFakeLocation/"
            target="_blank"
            rel="noreferrer"
            className="underline"
          >
            View on Github
          </a>
        </p>
        <DialogFooter>
          <Button variant="secondary" onClick={() => onOpenChange(false)}>
            Close
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
