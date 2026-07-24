using ArchIntel.GraphStore.Sqlite.DapperTypeHandlers;
using Dapper;

namespace ArchIntel.GraphStore.Sqlite;

internal static class DapperBootstrap
{
    private static int _registered;

    /// <summary>Idempotent — SqlMapper.AddTypeHandler is a static, process-wide registration.</summary>
    public static void EnsureTypeHandlersRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        SqlMapper.AddTypeHandler(new NodeTypeHandler());
        SqlMapper.AddTypeHandler(new RelationshipTypeHandler());
        SqlMapper.AddTypeHandler(new MetadataJsonHandler());
    }
}
