namespace ArchIntel.Api.Contracts;

/// <summary>`GET /snapshots` (05-rest-api.md Section 4.11). Real historical snapshots need Graph
/// Store schema/writer work that doesn't exist yet (`scan_runs` has no aggregate counts, and a
/// full `arch scan` hard-deletes prior rows rather than retaining them — confirmed against
/// 02-graph-store.md's actual implementation, not just its design). This surfaces the one data
/// point that IS real: the current/latest scan, computed live, rather than fabricating a
/// multi-day timeline.</summary>
public sealed record SnapshotDto(string SnapshotId, DateTimeOffset TakenAtUtc, int ClassCount, int ProjectCount, int InterfaceCount);
