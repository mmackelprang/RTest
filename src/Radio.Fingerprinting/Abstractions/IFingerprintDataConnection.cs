using Microsoft.Data.Sqlite;

namespace Radio.Fingerprinting.Abstractions;

/// <summary>
/// Provides a SQLite connection for fingerprinting data access.
/// Implemented by the host application's database context.
/// </summary>
public interface IFingerprintDataConnection
{
  /// <summary>
  /// Gets an initialized SQLite connection for fingerprinting data operations.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>An open SQLite connection.</returns>
  Task<SqliteConnection> GetConnectionAsync(CancellationToken ct = default);
}
