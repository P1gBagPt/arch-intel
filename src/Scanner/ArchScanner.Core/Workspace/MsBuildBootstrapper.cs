using Microsoft.Build.Locator;

namespace ArchScanner.Core.Workspace;

/// <summary>
/// Wraps MSBuildLocator.RegisterDefaults(), which must run exactly once per process and strictly
/// before any Microsoft.CodeAnalysis.MSBuild type is touched (Section 3.1) — a common source of
/// runtime failures if skipped or ordered wrong.
/// </summary>
public static class MsBuildBootstrapper
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }
}
