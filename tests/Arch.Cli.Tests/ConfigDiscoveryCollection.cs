namespace Arch.Cli.Tests;

/// <summary>
/// ConfigDiscoveryTests mutates the process-global ARCH_CONFIG environment variable. xUnit runs
/// different test classes in parallel by default, so any other class calling
/// ConfigDiscovery.Load(null, ...) — DoctorCommandTests, GraphCommandTests — must share this
/// collection to guarantee it never runs concurrently with that mutation.
/// </summary>
[CollectionDefinition("ConfigDiscovery")]
public sealed class ConfigDiscoveryCollection;
