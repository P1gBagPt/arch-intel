namespace ArchScanner.Core.Configuration;

public sealed class ScanConfig
{
    public required string Solution { get; init; }
    public IReadOnlyList<string> ScanOrder { get; init; } = [];
    public IReadOnlyList<string> Ignore { get; init; } = ["bin", "obj"];
    public IReadOnlyList<string> Languages { get; init; } = ["csharp"];
    public ScanRules Rules { get; init; } = new();
    public StorageConfig Storage { get; init; } = new();
}

/// <summary>Where/how the scanned graph is persisted. Additive (Section 5.2 of 03-cli.md) — absent in
/// existing arch.yml files defaults to a local SQLite database under .arch/.</summary>
public sealed class StorageConfig
{
    public string Provider { get; init; } = "sqlite";
    public string ConnectionString { get; init; } = ".arch/graph.db";
}
