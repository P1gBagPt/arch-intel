"use client";

import { useTheme } from "next-themes";
import { useEffect, useState } from "react";

export function ThemeToggle() {
  const { resolvedTheme, setTheme } = useTheme();
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    // Mount-check avoids a hydration mismatch: the server can't know the resolved
    // theme, so the toggle renders a neutral placeholder until the client settles.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setMounted(true);
  }, []);

  if (!mounted) {
    return <div className="h-8 w-8" />;
  }

  const isDark = resolvedTheme === "dark";

  return (
    <button
      type="button"
      onClick={() => setTheme(isDark ? "light" : "dark")}
      className="rounded-md border border-surface-border px-3 py-1.5 text-sm text-foreground/80 hover:text-foreground"
      aria-label="Toggle theme"
    >
      {isDark ? "Light" : "Dark"}
    </button>
  );
}
