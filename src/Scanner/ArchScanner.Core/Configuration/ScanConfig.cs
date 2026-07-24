namespace ArchScanner.Core.Configuration;

public sealed class ScanConfig
{
    public required string Solution { get; init; }
    public IReadOnlyList<string> ScanOrder { get; init; } = [];
    public IReadOnlyList<string> Ignore { get; init; } = ["bin", "obj"];
    public IReadOnlyList<string> Languages { get; init; } = ["csharp"];
    public ScanRules Rules { get; init; } = new();
}
