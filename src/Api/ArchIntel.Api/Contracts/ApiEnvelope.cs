namespace ArchIntel.Api.Contracts;

/// <summary>One consistent home for every collection-returning endpoint's response
/// (05-rest-api.md Section 3.3). `Page` is populated for cursor-paginated list endpoints
/// (Phase 2), omitted for singular/whole-graph responses.</summary>
public sealed record ApiEnvelope<T>(T Data, PageInfo? Page = null);

/// <summary>Cursor-pagination metadata (05-rest-api.md Section 3.5) — opaque `NextCursor` rather
/// than a page number, since the underlying list can change between requests.</summary>
public sealed record PageInfo(int Limit, int TotalCount, bool HasNextPage, string? NextCursor);
