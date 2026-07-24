using ArchScanner.Core.Configuration;

namespace ArchScanner.Core.Tests;

public class ScanConfigLoaderTests
{
    private const string SampleYaml = """
        solution: PatternVision.sln

        scanOrder:
          - Common
          - Domain
          - Application
          - Infrastructure
          - API
          - Tests

        ignore:
          - bin
          - obj
          - node_modules

        languages:
          - csharp

        rules:
          followInheritance: true
          followDI: false
          followMediatR: true
          followProjectReferences: false
        """;

    [Fact]
    public void LoadFromYaml_ParsesAllFields()
    {
        var config = ScanConfigLoader.LoadFromYaml(SampleYaml);

        Assert.Equal("PatternVision.sln", config.Solution);
        Assert.Equal(["Common", "Domain", "Application", "Infrastructure", "API", "Tests"], config.ScanOrder);
        Assert.Equal(["bin", "obj", "node_modules"], config.Ignore);
        Assert.Equal(["csharp"], config.Languages);
    }

    [Fact]
    public void LoadFromYaml_MapsFollowDI_ToFollowDiProperty()
    {
        // "followDI" (capital DI) in the documented config shape must bind to FollowDi, despite
        // camelCase convention alone producing "followDi" (lowercase i) for that property name.
        var config = ScanConfigLoader.LoadFromYaml(SampleYaml);

        Assert.False(config.Rules.FollowDi);
        Assert.True(config.Rules.FollowInheritance);
        Assert.True(config.Rules.FollowMediatR);
        Assert.False(config.Rules.FollowProjectReferences);
    }

    [Fact]
    public void LoadFromYaml_AppliesDefaults_WhenOptionalFieldsOmitted()
    {
        var config = ScanConfigLoader.LoadFromYaml("solution: Minimal.sln");

        Assert.Equal("Minimal.sln", config.Solution);
        Assert.Empty(config.ScanOrder);
        Assert.Equal(["bin", "obj"], config.Ignore);
        Assert.Equal(["csharp"], config.Languages);
        Assert.True(config.Rules.FollowDi);
        Assert.Equal("sqlite", config.Storage.Provider);
        Assert.Equal(".arch/graph.db", config.Storage.ConnectionString);
    }

    [Fact]
    public void LoadFromYaml_ParsesStorageSection_WhenPresent()
    {
        var config = ScanConfigLoader.LoadFromYaml("""
            solution: PatternVision.sln
            storage:
              provider: postgres
              connectionString: Host=localhost;Database=arch
            """);

        Assert.Equal("postgres", config.Storage.Provider);
        Assert.Equal("Host=localhost;Database=arch", config.Storage.ConnectionString);
    }

    [Fact]
    public void LoadFromYaml_Throws_WhenSolutionMissing()
    {
        Assert.Throws<InvalidOperationException>(() => ScanConfigLoader.LoadFromYaml("scanOrder: [Domain]"));
    }
}
