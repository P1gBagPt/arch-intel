import type { ApiEnvelope } from "@/types/api";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5219";

// Mirrors src/Api/ArchIntel.Api/Problems/ProblemTypes.cs (RFC 7807 ProblemDetails).
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  constructor(
    public status: number,
    public problem: ProblemDetails | null,
  ) {
    super(problem?.detail ?? problem?.title ?? `API request failed with status ${status}`);
    this.name = "ApiError";
  }
}

export interface RequestOptions {
  method?: "GET" | "POST";
  query?: Record<string, string | number | boolean | string[] | undefined>;
  body?: unknown;
  signal?: AbortSignal;
}

function buildQueryString(query: RequestOptions["query"]): string {
  if (!query) return "";
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined) continue;
    params.set(key, Array.isArray(value) ? value.join(",") : String(value));
  }
  const qs = params.toString();
  return qs ? `?${qs}` : "";
}

// DevBearerAuthenticationHandler trusts `Authorization: Bearer <userId>` verbatim (no real auth
// exists yet, and RepoAuthorizationHandler no-ops to "allowed" while Authentication:Enabled is
// false server-side) — sending a stable dev identity now costs nothing and means Phase 4's real
// token swap only touches this one function.
function authHeaders(): Record<string, string> {
  const devUserId = process.env.NEXT_PUBLIC_DEV_USER_ID;
  return devUserId ? { Authorization: `Bearer ${devUserId}` } : {};
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", query, body, signal } = options;
  const url = `${API_URL}${path}${buildQueryString(query)}`;

  const res = await fetch(url, {
    method,
    headers: {
      Accept: "application/json",
      ...(body ? { "Content-Type": "application/json" } : {}),
      ...authHeaders(),
    },
    body: body ? JSON.stringify(body) : undefined,
    signal,
  });

  if (!res.ok) {
    const problem = await res.json().catch(() => null);
    throw new ApiError(res.status, problem);
  }

  if (res.status === 204) {
    return undefined as T;
  }

  return res.json() as Promise<T>;
}

// Every dashboard-relevant endpoint is repo-scoped under /api/v1/repos/{repoId} (Program.cs) —
// this factory bakes that prefix in so callers never repeat it.
export function createApiClient(repoId: string) {
  const base = `/api/v1/repos/${encodeURIComponent(repoId)}`;

  return {
    get: <T>(path: string, query?: RequestOptions["query"], signal?: AbortSignal) =>
      request<ApiEnvelope<T>>(`${base}${path}`, { method: "GET", query, signal }),

    post: <T>(path: string, body?: unknown, signal?: AbortSignal) =>
      request<ApiEnvelope<T>>(`${base}${path}`, { method: "POST", body, signal }),
  };
}

export type ApiClient = ReturnType<typeof createApiClient>;
