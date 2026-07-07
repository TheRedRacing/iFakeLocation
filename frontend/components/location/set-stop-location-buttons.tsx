"use client";

import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";

interface SetStopLocationButtonsProps {
  canSet: boolean;
  canStop: boolean;
  /** Returns true on success (shows a transient confirmation), false if the caller already surfaced an error. */
  onSetLocation: () => Promise<boolean>;
  onStopLocation: () => Promise<boolean>;
}

type FeedbackKind = "set" | "stop" | null;

export function SetStopLocationButtons({ canSet, canStop, onSetLocation, onStopLocation }: SetStopLocationButtonsProps) {
  const [busy, setBusy] = useState<FeedbackKind>(null);
  const [feedback, setFeedback] = useState<FeedbackKind>(null);
  const feedbackTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    return () => {
      if (feedbackTimeoutRef.current) clearTimeout(feedbackTimeoutRef.current);
    };
  }, []);

  const showFeedback = (kind: FeedbackKind) => {
    setFeedback(kind);
    if (feedbackTimeoutRef.current) clearTimeout(feedbackTimeoutRef.current);
    feedbackTimeoutRef.current = setTimeout(() => setFeedback(null), 7000);
  };

  const handleSet = async () => {
    setBusy("set");
    try {
      if (await onSetLocation()) showFeedback("set");
    } finally {
      setBusy(null);
    }
  };

  const handleStop = async () => {
    setBusy("stop");
    try {
      if (await onStopLocation()) showFeedback("stop");
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="space-y-2">
      <div className="grid grid-cols-2 gap-2">
        <Button type="button" className="w-full" disabled={!canSet || busy !== null} onClick={() => void handleSet()}>
          Set Fake Location
        </Button>
        <Button
          type="button"
          variant="secondary"
          className="w-full"
          disabled={!canStop || busy !== null}
          onClick={() => void handleStop()}
        >
          Stop Fake Location
        </Button>
      </div>
      {feedback === "set" && (
        <p className="text-muted-foreground text-sm">Location has been successfully set. Confirm using Maps or other apps.</p>
      )}
      {feedback === "stop" && (
        <p className="text-muted-foreground text-sm">
          Fake location has been stopped. If your location is still stuck, try turning Location Services off and back on.
        </p>
      )}
    </div>
  );
}
