"use client";

import { useState } from "react";
import { useLiveStatusStore } from "@/stores/live-status";

const STATE_LABEL: Record<string, string> = {
  connecting: "Connecting…",
  connected: "Live",
  reconnecting: "Reconnecting…",
  disconnected: "Offline",
};

const STATE_DOT: Record<string, string> = {
  connecting: "bg-coupling-moderate",
  connected: "bg-coupling-stable",
  reconnecting: "bg-coupling-moderate",
  disconnected: "bg-coupling-high",
};

// Job push notifications only (see components/layout/SignalRProvider.tsx) — this is not a
// general "live graph updates" indicator, since the backend has no producer for that event.
export function LiveStatusIndicator() {
  const connectionState = useLiveStatusStore((s) => s.connectionState);
  const recentEvents = useLiveStatusStore((s) => s.recentEvents);
  const [open, setOpen] = useState(false);

  return (
    <div className="relative">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="flex items-center gap-1.5 rounded-md px-2 py-1 text-xs text-muted-foreground hover:text-foreground"
      >
        <span className={`h-2 w-2 rounded-full ${STATE_DOT[connectionState]}`} />
        {STATE_LABEL[connectionState]}
        {recentEvents.length > 0 && (
          <span className="rounded-full bg-surface-border px-1.5 text-[10px]">{recentEvents.length}</span>
        )}
      </button>
      {open && (
        <div className="absolute right-0 top-full z-10 mt-1 w-72 rounded-md border border-surface-border bg-background p-2 shadow-lg">
          <p className="px-1 pb-1 text-xs font-medium text-muted-foreground">Recent job events</p>
          {recentEvents.length === 0 ? (
            <p className="px-1 py-2 text-xs text-muted-foreground">No job events yet this session.</p>
          ) : (
            <ul className="max-h-64 space-y-1 overflow-auto">
              {recentEvents.map((event) => (
                <li
                  key={event.id}
                  className={`rounded-md px-1.5 py-1 text-xs ${event.kind === "job:failed" ? "text-coupling-high" : ""}`}
                >
                  <span className="block">{event.message}</span>
                  <span className="text-[10px] text-muted-foreground">
                    {new Date(event.timestamp).toLocaleTimeString()}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
