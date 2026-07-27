import { create } from "zustand";

export type ConnectionState = "connecting" | "connected" | "reconnecting" | "disconnected";

export interface LiveEvent {
  id: string;
  timestamp: string;
  kind: "job:completed" | "job:failed";
  jobId: string;
  message: string;
}

const MAX_EVENTS = 20;

interface LiveStatusStore {
  connectionState: ConnectionState;
  recentEvents: LiveEvent[];
  setConnectionState: (state: ConnectionState) => void;
  pushEvent: (event: LiveEvent) => void;
}

// Scoped to what the backend actually pushes today (see types/signalr-events.ts) — job
// completion/failure only, not a general "live graph" event log.
export const useLiveStatusStore = create<LiveStatusStore>((set) => ({
  connectionState: "connecting",
  recentEvents: [],
  setConnectionState: (state) => set({ connectionState: state }),
  pushEvent: (event) =>
    set((s) => ({ recentEvents: [event, ...s.recentEvents].slice(0, MAX_EVENTS) })),
}));
