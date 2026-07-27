"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/cn";

const NAV_ITEMS = [
  { href: "/explorer", label: "Repository Explorer" },
  { href: "/graph", label: "Dependency Graph" },
  { href: "/services", label: "Service Explorer" },
  { href: "/impact", label: "Impact Analysis" },
  { href: "/coupling", label: "Coupling Heatmap" },
  { href: "/timeline", label: "Architecture Timeline" },
];

export function Sidebar() {
  const pathname = usePathname();

  return (
    <nav className="w-56 shrink-0 border-r border-surface-border bg-surface p-4">
      <ul className="space-y-1">
        {NAV_ITEMS.map((item) => {
          const active = pathname?.startsWith(item.href);
          return (
            <li key={item.href}>
              <Link
                href={item.href}
                className={cn(
                  "block rounded-md px-3 py-2 text-sm font-medium transition-colors",
                  active
                    ? "bg-accent/10 text-accent"
                    : "text-foreground/80 hover:bg-surface-border/50 hover:text-foreground",
                )}
              >
                {item.label}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
