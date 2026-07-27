"use client";

import { cn } from "@/lib/cn";

interface SearchInputProps {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  className?: string;
}

export function SearchInput({ value, onChange, placeholder, className }: SearchInputProps) {
  return (
    <input
      type="text"
      value={value}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder ?? "Search…"}
      className={cn(
        "w-full rounded-md border border-surface-border bg-background px-3 py-1.5 text-sm outline-none focus:border-accent",
        className,
      )}
    />
  );
}
