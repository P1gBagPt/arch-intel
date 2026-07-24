using System.Data;

namespace ArchIntel.GraphStore.Core;

/// <summary>
/// Backend-agnostic connection factory so callers never reference Microsoft.Data.Sqlite/Npgsql directly.
/// </summary>
public interface IDbConnectionFactory
{
    Task<IDbConnection> OpenConnectionAsync(CancellationToken ct = default);
}
