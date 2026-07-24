using System.Data;
using ArchIntel.GraphStore.Contracts.Enums;
using Dapper;

namespace ArchIntel.GraphStore.Sqlite.DapperTypeHandlers;

public sealed class NodeTypeHandler : SqlMapper.TypeHandler<NodeType>
{
    public override void SetValue(IDbDataParameter parameter, NodeType value)
        => parameter.Value = value.ToString();

    public override NodeType Parse(object value)
        => Enum.Parse<NodeType>((string)value);
}
