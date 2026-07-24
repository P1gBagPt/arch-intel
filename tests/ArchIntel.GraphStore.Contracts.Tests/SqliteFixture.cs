using ArchIntel.GraphStore.Sqlite;
using Microsoft.Data.Sqlite;

namespace ArchIntel.GraphStore.Contracts.Tests;

/// <summary>Spins up a fresh, migrated, temp-file SQLite database per test instance.</summary>
public sealed class SqliteFixture : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"archintel-test-{Guid.NewGuid():N}.db");

    public SqliteConnectionFactory ConnectionFactory { get; private set; } = null!;
    public SqliteGraphWriter Writer { get; private set; } = null!;
    public SqliteGraphReader Reader { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        ConnectionFactory = new SqliteConnectionFactory($"Data Source={_dbPath}");
        await new MigrationRunner(ConnectionFactory).ApplyAsync();
        Writer = new SqliteGraphWriter(ConnectionFactory);
        Reader = new SqliteGraphReader(ConnectionFactory);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        var walPath = _dbPath + "-wal";
        var shmPath = _dbPath + "-shm";
        if (File.Exists(walPath)) File.Delete(walPath);
        if (File.Exists(shmPath)) File.Delete(shmPath);

        return Task.CompletedTask;
    }
}
