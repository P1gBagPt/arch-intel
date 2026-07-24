using System.Data;
using System.Text.Json;
using Dapper;

namespace ArchIntel.GraphStore.Sqlite.DapperTypeHandlers;

/// <summary>(De)serializes the metadata_json column into the DTOs' Metadata dictionary.</summary>
public sealed class MetadataJsonHandler : SqlMapper.TypeHandler<IReadOnlyDictionary<string, string>>
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    public override void SetValue(IDbDataParameter parameter, IReadOnlyDictionary<string, string>? value)
        => parameter.Value = JsonSerializer.Serialize(value ?? Empty);

    public override IReadOnlyDictionary<string, string> Parse(object value)
        => JsonSerializer.Deserialize<Dictionary<string, string>>((string)value) ?? new Dictionary<string, string>();
}
