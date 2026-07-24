using ArchIntel.GraphStore.Sqlite;
using Microsoft.Data.Sqlite;

namespace Arch.Cli.Tests.Fixtures;

/// <summary>Spins up a fresh, migrated, temp-file SQLite database per test instance — mirrors
/// ArchIntel.GraphStore.Contracts.Tests.SqliteFixture so CLI tests exercise the real backend
/// rather than hand-rolled fakes.</summary>
public sealed class SqliteFixture : IAsyncLifetime
{
    public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"archcli-test-{Guid.NewGuid():N}.db");

    public SqliteConnectionFactory ConnectionFactory { get; private set; } = null!;
    public SqliteGraphWriter Writer { get; private set; } = null!;
    public SqliteGraphReader Reader { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        ConnectionFactory = new SqliteConnectionFactory($"Data Source={DbPath}");
        await new MigrationRunner(ConnectionFactory).ApplyAsync();
        Writer = new SqliteGraphWriter(ConnectionFactory);
        Reader = new SqliteGraphReader(ConnectionFactory);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        foreach (var path in new[] { DbPath, DbPath + "-wal", DbPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }
}
