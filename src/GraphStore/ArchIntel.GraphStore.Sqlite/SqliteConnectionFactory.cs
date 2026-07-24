using System.Data;
using ArchIntel.GraphStore.Core;
using Microsoft.Data.Sqlite;

namespace ArchIntel.GraphStore.Sqlite;

/// <summary>
/// Opens WAL-mode SQLite connections. WAL is required from Phase 1 so a scan (writer) doesn't block
/// concurrent readers (CLI / future API), per the single-writer contention risk called out in the plan.
/// </summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private bool _initialized;

    public SqliteConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IDbConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        if (!_initialized)
        {
            using var pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;";
            await pragmaCmd.ExecuteNonQueryAsync(ct);
            _initialized = true;
        }

        return connection;
    }
}
