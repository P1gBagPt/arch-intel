import { ThemeToggle } from "./ThemeToggle";

export function TopBar() {
  return (
    <header className="flex h-14 shrink-0 items-center justify-between border-b border-surface-border px-6">
      <span className="text-sm font-semibold">Architecture Intelligence</span>
      <ThemeToggle />
    </header>
  );
}
