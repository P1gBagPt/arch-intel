using ArchScanner.Core.Configuration;

namespace ArchIntel.Api.Configuration;

/// <summary>
/// Resolves the Graph Store SQLite path the same way `arch mcp start` does (Arch.Cli.Configuration
/// .ConfigDiscovery): ARCH_CONFIG env var, then arch.yml / .arch/arch.yml walking up from cwd. Kept
/// as its own small copy rather than referencing Arch.Cli, since that project is the CLI executable
/// (System.CommandLine, Spectre.Console) and this API host has no business depending on it.
/// </summary>
public static class ConfigDiscovery
{
    public static (string? DbPath, string? Error) TryResolveDbPath(string cwd)
    {
        var configPath = FindConfigPath(cwd);
        if (configPath is null)
        {
            return (null, "No arch.yml found. Run 'arch init' and 'arch scan' first.");
        }

        var config = ScanConfigLoader.LoadFromFile(configPath);
        var configDir = Path.GetDirectoryName(configPath)!;
        return (Path.GetFullPath(config.Storage.ConnectionString, configDir), null);
    }

    private static string? FindConfigPath(string cwd)
    {
        var envPath = Environment.GetEnvironmentVariable("ARCH_CONFIG");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return File.Exists(envPath) ? Path.GetFullPath(envPath) : null;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(cwd));
        while (directory is not null)
        {
            var direct = Path.Combine(directory.FullName, "arch.yml");
            if (File.Exists(direct))
            {
                return direct;
            }

            var dotArch = Path.Combine(directory.FullName, ".arch", "arch.yml");
            if (File.Exists(dotArch))
            {
                return dotArch;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
