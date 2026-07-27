namespace ArchIntel.Api.Contracts;

/// <summary>Shared lightweight node reference (05-rest-api.md Section 3.3's `NodeRef`), used
/// anywhere a response needs to point at another graph node without embedding its full detail.
/// `Relation` is populated where the reference carries edge context (e.g. impact/dependency
/// listings), null where it's just identity (e.g. an implements/tests listing).</summary>
public sealed record NodeRefDto(string Id, string Kind, string Name, string? Relation = null);
