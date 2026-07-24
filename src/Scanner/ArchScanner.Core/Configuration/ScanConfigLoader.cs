using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ArchScanner.Core.Configuration;

public static class ScanConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static ScanConfig LoadFromFile(string path)
    {
        var yaml = File.ReadAllText(path);
        return LoadFromYaml(yaml);
    }

    public static ScanConfig LoadFromYaml(string yaml)
    {
        // YamlDotNet's default object deserializer needs a concrete, settable collection type
        // (List<T>) — it can't instantiate the read-only collection interfaces the public
        // ScanConfig contract exposes. Deserialize into this mutable shape, then map across.
        var raw = Deserializer.Deserialize<YamlScanConfig>(yaml)
            ?? throw new InvalidOperationException("Scan config YAML deserialized to null.");

        if (string.IsNullOrWhiteSpace(raw.Solution))
        {
            throw new InvalidOperationException("Scan config must specify 'solution'.");
        }

        return new ScanConfig
        {
            Solution = raw.Solution,
            ScanOrder = raw.ScanOrder ?? [],
            Ignore = raw.Ignore ?? ["bin", "obj"],
            Languages = raw.Languages ?? ["csharp"],
            Rules = raw.Rules ?? new ScanRules(),
        };
    }

    private sealed class YamlScanConfig
    {
        public string? Solution { get; set; }
        public List<string>? ScanOrder { get; set; }
        public List<string>? Ignore { get; set; }
        public List<string>? Languages { get; set; }
        public ScanRules? Rules { get; set; }
    }
}
