using Arch.Cli.Configuration;

namespace Arch.Cli.Tests;

/// <summary>Covers the discovery precedence from 03-cli.md Section 5.1: explicit flag, ARCH_CONFIG
/// env var, ./arch.yml, ./.arch/arch.yml, then walking upward through parent directories.</summary>
public sealed class ConfigDiscoveryTests : IDisposable
{
    private readonly DirectoryInfo _tempRoot = Directory.CreateTempSubdirectory("archcli-config-");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ARCH_CONFIG", null);
        _tempRoot.Delete(recursive: true);
    }

    [Fact]
    public void ResolvePath_PrefersExplicitPath_OverEverythingElse()
    {
        var explicitPath = WriteConfig(_tempRoot.FullName, "explicit.yml", "solution: Explicit.sln");
        WriteConfig(_tempRoot.FullName, "arch.yml", "solution: Cwd.sln");

        var resolved = ConfigDiscovery.ResolvePath(explicitPath, _tempRoot.FullName);

        Assert.Equal(Path.GetFullPath(explicitPath), resolved);
    }

    [Fact]
    public void ResolvePath_FallsBackToArchConfigEnvVar()
    {
        var envPath = WriteConfig(_tempRoot.FullName, "env.yml", "solution: Env.sln");
        Environment.SetEnvironmentVariable("ARCH_CONFIG", envPath);

        var resolved = ConfigDiscovery.ResolvePath(null, _tempRoot.FullName);

        Assert.Equal(Path.GetFullPath(envPath), resolved);
    }

    [Fact]
    public void ResolvePath_FindsArchYml_InCwd()
    {
        WriteConfig(_tempRoot.FullName, "arch.yml", "solution: Cwd.sln");

        var resolved = ConfigDiscovery.ResolvePath(null, _tempRoot.FullName);

        Assert.Equal(Path.Combine(_tempRoot.FullName, "arch.yml"), resolved);
    }

    [Fact]
    public void ResolvePath_FindsDotArchArchYml_WhenNoTopLevelConfig()
    {
        var dotArchDir = Path.Combine(_tempRoot.FullName, ".arch");
        Directory.CreateDirectory(dotArchDir);
        WriteConfig(dotArchDir, "arch.yml", "solution: DotArch.sln");

        var resolved = ConfigDiscovery.ResolvePath(null, _tempRoot.FullName);

        Assert.Equal(Path.Combine(dotArchDir, "arch.yml"), resolved);
    }

    [Fact]
    public void ResolvePath_WalksUpParentDirectories()
    {
        WriteConfig(_tempRoot.FullName, "arch.yml", "solution: Parent.sln");
        var nested = Path.Combine(_tempRoot.FullName, "nested", "deeper");
        Directory.CreateDirectory(nested);

        var resolved = ConfigDiscovery.ResolvePath(null, nested);

        Assert.Equal(Path.Combine(_tempRoot.FullName, "arch.yml"), resolved);
    }

    [Fact]
    public void ResolvePath_Throws_WhenNothingFound()
    {
        Assert.Throws<ConfigNotFoundException>(() => ConfigDiscovery.ResolvePath(null, _tempRoot.FullName));
    }

    [Fact]
    public void ResolvePath_Throws_WhenExplicitPathMissing()
    {
        var missing = Path.Combine(_tempRoot.FullName, "missing.yml");

        Assert.Throws<FileNotFoundException>(() => ConfigDiscovery.ResolvePath(missing, _tempRoot.FullName));
    }

    private static string WriteConfig(string directory, string fileName, string contents)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, contents);
        return path;
    }
}
