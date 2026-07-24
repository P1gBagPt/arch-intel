using ArchScanner.Core.Configuration;

namespace Arch.Cli.Configuration;

public static class GraphStorePaths
{
    /// <summary>Resolves ScanConfig.Storage.ConnectionString (Section 5.2's `storage:` block) to an
    /// absolute path, relative to the directory the arch.yml itself lives in.</summary>
    public static string ResolveDbPath(ScanConfig config, string configDir)
        => Path.GetFullPath(config.Storage.ConnectionString, configDir);
}
