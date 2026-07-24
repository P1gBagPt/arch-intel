using ArchScanner.Core.Configuration;

namespace Arch.Cli.Configuration;

/// <summary>Thrown when no arch.yml could be located anywhere in the discovery chain (Section 5.1).</summary>
public sealed class ConfigNotFoundException() : Exception("No arch.yml found. Run 'arch init' to create one.");

public sealed record ResolvedConfig(ScanConfig Config, string Path);

/// <summary>
/// Implements the config discovery precedence from 03-cli.md Section 5.1: explicit flag, then
/// ARCH_CONFIG env var, then ./arch.yml / ./.arch/arch.yml walking upward through parent
/// directories (like .editorconfig) until found or the file-system root is reached.
/// </summary>
public static class ConfigDiscovery
{
    public static string ResolvePath(string? explicitPath, string cwd)
    {
        if (explicitPath is not null)
        {
            return File.Exists(explicitPath)
                ? Path.GetFullPath(explicitPath)
                : throw new FileNotFoundException($"Config file not found: {explicitPath}", explicitPath);
        }

        var envPath = Environment.GetEnvironmentVariable("ARCH_CONFIG");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return File.Exists(envPath)
                ? Path.GetFullPath(envPath)
                : throw new FileNotFoundException($"ARCH_CONFIG points to a missing file: {envPath}", envPath);
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

        throw new ConfigNotFoundException();
    }

    public static ResolvedConfig Load(string? explicitPath, string cwd)
    {
        var path = ResolvePath(explicitPath, cwd);
        return new ResolvedConfig(ScanConfigLoader.LoadFromFile(path), path);
    }
}
