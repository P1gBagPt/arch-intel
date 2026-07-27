namespace ArchIntel.Api.Auth;

/// <summary>Per-repository roles (05-rest-api.md Section 6.3). Ordered by increasing privilege so
/// a numeric comparison (`actual >= required`) expresses "at least this role".</summary>
public enum RepoRole
{
    Viewer = 0,
    Maintainer = 1,
    Owner = 2,
}
