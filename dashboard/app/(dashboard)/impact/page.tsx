"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { SearchInput } from "@/components/ui/SearchInput";

export default function ImpactPickerPage() {
  const [nodeId, setNodeId] = useState("");
  const router = useRouter();

  return (
    <div className="mx-auto max-w-xl space-y-4">
      <h1 className="text-xl font-semibold">Impact Analysis</h1>
      <p className="text-sm text-muted-foreground">
        Enter a class, interface, or entity node id to see what depends on it. Node ids are visible
        in the Dependency Graph&apos;s node detail drawer.
      </p>
      <form
        className="flex gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          if (nodeId.trim()) router.push(`/impact/${encodeURIComponent(nodeId.trim())}`);
        }}
      >
        <SearchInput value={nodeId} onChange={setNodeId} placeholder="Node id…" />
        <button
          type="submit"
          className="shrink-0 rounded-md bg-accent px-4 py-1.5 text-sm font-medium text-white hover:opacity-90"
        >
          Analyze
        </button>
      </form>
    </div>
  );
}
