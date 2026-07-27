"use client";

import Link from "next/link";
import { useState } from "react";
import { Badge } from "@/components/ui/Badge";
import { SearchInput } from "@/components/ui/SearchInput";
import { useServices } from "@/hooks/useServices";

export default function ServicesPage() {
  const { data: services, isLoading, isError, error } = useServices();
  const [query, setQuery] = useState("");

  if (isLoading) return <p className="text-sm text-muted-foreground">Loading services…</p>;
  if (isError || !services) {
    return (
      <p className="text-sm text-red-500">
        Failed to load services: {error instanceof Error ? error.message : "unknown error"}
      </p>
    );
  }

  const q = query.trim().toLowerCase();
  const filtered = q ? services.filter((s) => s.name.toLowerCase().includes(q)) : services;

  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <h1 className="text-xl font-semibold">Service Explorer</h1>
      <SearchInput value={query} onChange={setQuery} placeholder="Search services…" />
      <ul className="divide-y divide-surface-border rounded-md border border-surface-border">
        {filtered.map((service) => (
          <li key={service.id}>
            <Link
              href={`/services/${encodeURIComponent(service.id)}`}
              className="flex items-center justify-between px-4 py-2 text-sm hover:bg-surface"
            >
              <span>{service.name}</span>
              <Badge>{service.kind}</Badge>
            </Link>
          </li>
        ))}
        {filtered.length === 0 && (
          <li className="px-4 py-8 text-center text-sm text-muted-foreground">No services match.</li>
        )}
      </ul>
    </div>
  );
}
