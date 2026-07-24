namespace ArchScanner.Core;

/// <summary>Excludes generated code from scanning by default (Section 3.1, Risk #3).</summary>
public static class SourceFileFilter
{
    public static bool IsGenerated(string filePath)
        => filePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
        || filePath.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
        || filePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || filePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
}
