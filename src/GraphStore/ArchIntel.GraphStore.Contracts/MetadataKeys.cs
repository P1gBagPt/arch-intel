namespace ArchIntel.GraphStore.Contracts;

/// <summary>
/// Well-known keys the Scanner writes into <see cref="NodeDto.Metadata"/> / <see cref="EdgeDto.Metadata"/>.
/// Centralized here (rather than each heuristic inventing its own key) so the Scanner and any
/// reader-side consumer agree on spelling.
/// </summary>
public static class MetadataKeys
{
    /// <summary>One of <see cref="ResolutionConfidenceValues"/>. Carried on heuristic-derived edges.</summary>
    public const string ResolutionConfidence = "resolutionConfidence";

    public const string HttpMethod = "httpMethod";
    public const string RouteTemplate = "routeTemplate";
    public const string DiLifetime = "diLifetime";
    public const string ConfigSection = "configSection";
    public const string CyclomaticComplexity = "cyclomaticComplexity";
    public const string ViaConstructor = "viaConstructor";
    public const string NamingConventionMatch = "namingConventionMatch";
    public const string DetectionConfidence = "detectionConfidence";
}

/// <summary>String values for <see cref="MetadataKeys.ResolutionConfidence"/>.</summary>
public static class ResolutionConfidenceValues
{
    public const string Resolved = "Resolved";
    public const string Heuristic = "Heuristic";
    public const string Unresolved = "Unresolved";
}
