using System.Data;
using ArchIntel.GraphStore.Contracts.Enums;
using Dapper;

namespace ArchIntel.GraphStore.Sqlite.DapperTypeHandlers;

public sealed class RelationshipTypeHandler : SqlMapper.TypeHandler<RelationshipType>
{
    public override void SetValue(IDbDataParameter parameter, RelationshipType value)
        => parameter.Value = value.ToString();

    public override RelationshipType Parse(object value)
        => Enum.Parse<RelationshipType>((string)value);
}
