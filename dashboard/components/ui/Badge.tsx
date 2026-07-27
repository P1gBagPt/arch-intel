import { cn } from "@/lib/cn";

export function Badge({ children, className }: { children: React.ReactNode; className?: string }) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full border border-surface-border bg-surface px-2 py-0.5 text-xs font-medium text-muted-foreground",
        className,
      )}
    >
      {children}
    </span>
  );
}
