import Link from "next/link";

export default function Home() {
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-6 p-16 text-center">
      <h1 className="text-3xl font-semibold tracking-tight">Architecture Intelligence Platform</h1>
      <p className="max-w-md text-muted-foreground">
        Browse, search, and reason about your architecture graph before writing code.
      </p>
      <Link
        href="/explorer"
        className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-white hover:opacity-90"
      >
        Open Repository Explorer
      </Link>
    </div>
  );
}
