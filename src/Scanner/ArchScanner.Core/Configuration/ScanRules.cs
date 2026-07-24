using YamlDotNet.Serialization;

namespace ArchScanner.Core.Configuration;

public sealed class ScanRules
{
    public bool FollowInheritance { get; init; } = true;

    [YamlMember(Alias = "followDI")]
    public bool FollowDi { get; init; } = true;

    public bool FollowMediatR { get; init; } = true;
    public bool FollowProjectReferences { get; init; } = true;
}
