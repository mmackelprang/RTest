using Microsoft.Data.Sqlite;
using Radio.Core.Interfaces.Bluetooth;
using Radio.Core.Models;
using Radio.Core.Utilities;

namespace Radio.Infrastructure.Bluetooth;

public class PbapContactRepository : IPbapContactRepository
{
  private readonly string _connectionString;
  private readonly SqliteConnection? _sharedConnection; // for testing with in-memory DB

  public PbapContactRepository(string connectionString)
  {
    _connectionString = connectionString;
  }

  /// <summary>Test-only constructor for in-memory SQLite.</summary>
  internal PbapContactRepository(SqliteConnection sharedConnection)
  {
    _sharedConnection = sharedConnection;
    _connectionString = sharedConnection.ConnectionString;
  }

  private SqliteConnection GetConnection()
  {
    if (_sharedConnection != null) return _sharedConnection;
    var conn = new SqliteConnection(_connectionString);
    conn.Open();
    return conn;
  }

  private void ReturnConnection(SqliteConnection conn)
  {
    // Don't dispose shared test connections
    if (conn != _sharedConnection) conn.Dispose();
  }

  public async Task InitializeAsync()
  {
    var conn = GetConnection();
    try
    {
      using var cmd = conn.CreateCommand();
      cmd.CommandText = """
          CREATE TABLE IF NOT EXISTS PbapContacts (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              DeviceAddress TEXT NOT NULL,
              DisplayName TEXT NOT NULL,
              PhoneNumber TEXT NOT NULL,
              LastSynced DATETIME NOT NULL,
              UNIQUE(DeviceAddress, PhoneNumber)
          );
          CREATE INDEX IF NOT EXISTS IX_PbapContacts_DeviceAddress ON PbapContacts(DeviceAddress);
          CREATE INDEX IF NOT EXISTS IX_PbapContacts_PhoneNumber ON PbapContacts(PhoneNumber);
          """;
      await cmd.ExecuteNonQueryAsync();
    }
    finally
    {
      ReturnConnection(conn);
    }
  }

  public async Task UpsertContactsAsync(string deviceAddress, List<PbapContact> contacts, CancellationToken ct = default)
  {
    var conn = GetConnection();
    try
    {
      using var transaction = conn.BeginTransaction();

      // Delete existing contacts for this device
      using (var delCmd = conn.CreateCommand())
      {
        delCmd.CommandText = "DELETE FROM PbapContacts WHERE DeviceAddress = @addr";
        delCmd.Parameters.AddWithValue("@addr", deviceAddress);
        await delCmd.ExecuteNonQueryAsync(ct);
      }

      // Insert new contacts (one row per phone number)
      var now = DateTime.UtcNow;
      foreach (var contact in contacts)
      {
        foreach (var number in contact.PhoneNumbers)
        {
          using var insCmd = conn.CreateCommand();
          insCmd.CommandText = """
              INSERT OR REPLACE INTO PbapContacts (DeviceAddress, DisplayName, PhoneNumber, LastSynced)
              VALUES (@addr, @name, @phone, @synced)
              """;
          insCmd.Parameters.AddWithValue("@addr", deviceAddress);
          insCmd.Parameters.AddWithValue("@name", contact.DisplayName);
          insCmd.Parameters.AddWithValue("@phone", number);
          insCmd.Parameters.AddWithValue("@synced", now.ToString("o"));
          await insCmd.ExecuteNonQueryAsync(ct);
        }
      }

      transaction.Commit();
    }
    finally
    {
      ReturnConnection(conn);
    }
  }

  public async Task<PbapContact?> FindByPhoneNumberAsync(string deviceAddress, string normalizedNumber, CancellationToken ct = default)
  {
    var conn = GetConnection();
    try
    {
      // Try exact match first
      using (var cmd = conn.CreateCommand())
      {
        cmd.CommandText = "SELECT DisplayName FROM PbapContacts WHERE DeviceAddress = @addr AND PhoneNumber = @phone LIMIT 1";
        cmd.Parameters.AddWithValue("@addr", deviceAddress);
        cmd.Parameters.AddWithValue("@phone", normalizedNumber);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is string name)
          return new PbapContact { DisplayName = name, PhoneNumbers = new() { normalizedNumber } };
      }

      // Try last-7 suffix match
      var last7 = PhoneNumberNormalizer.GetLast7(normalizedNumber);
      if (last7.Length == 7)
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DisplayName, PhoneNumber FROM PbapContacts
            WHERE DeviceAddress = @addr AND PhoneNumber LIKE @suffix
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@addr", deviceAddress);
        cmd.Parameters.AddWithValue("@suffix", "%" + last7);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
          return new PbapContact
          {
            DisplayName = reader.GetString(0),
            PhoneNumbers = new() { reader.GetString(1) }
          };
        }
      }

      return null;
    }
    finally
    {
      ReturnConnection(conn);
    }
  }

  public async Task<List<PbapContact>> GetContactsAsync(string deviceAddress, CancellationToken ct = default)
  {
    var conn = GetConnection();
    try
    {
      var contactMap = new Dictionary<string, PbapContact>();

      using var cmd = conn.CreateCommand();
      cmd.CommandText = "SELECT DisplayName, PhoneNumber FROM PbapContacts WHERE DeviceAddress = @addr ORDER BY DisplayName";
      cmd.Parameters.AddWithValue("@addr", deviceAddress);

      using var reader = await cmd.ExecuteReaderAsync(ct);
      while (await reader.ReadAsync(ct))
      {
        var name = reader.GetString(0);
        var phone = reader.GetString(1);

        if (!contactMap.TryGetValue(name, out var contact))
        {
          contact = new PbapContact { DisplayName = name, PhoneNumbers = new() };
          contactMap[name] = contact;
        }
        contact.PhoneNumbers.Add(phone);
      }

      return contactMap.Values.ToList();
    }
    finally
    {
      ReturnConnection(conn);
    }
  }

  public async Task<List<(string DeviceAddress, int ContactCount, DateTime? LastSynced)>> GetSyncSummaryAsync(string? deviceAddress = null, CancellationToken ct = default)
  {
    var conn = GetConnection();
    try
    {
      using var cmd = conn.CreateCommand();
      var sql = "SELECT DeviceAddress, COUNT(*), MAX(LastSynced) FROM PbapContacts";
      if (deviceAddress != null)
      {
        sql += " WHERE DeviceAddress = @addr";
        cmd.Parameters.AddWithValue("@addr", deviceAddress);
      }
      sql += " GROUP BY DeviceAddress";
      cmd.CommandText = sql;

      var results = new List<(string, int, DateTime?)>();
      using var reader = await cmd.ExecuteReaderAsync(ct);
      while (await reader.ReadAsync(ct))
      {
        var addr = reader.GetString(0);
        var count = reader.GetInt32(1);
        DateTime? synced = reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2));
        results.Add((addr, count, synced));
      }

      return results;
    }
    finally
    {
      ReturnConnection(conn);
    }
  }

  public async Task DeleteContactsAsync(string deviceAddress, CancellationToken ct = default)
  {
    var conn = GetConnection();
    try
    {
      using var cmd = conn.CreateCommand();
      cmd.CommandText = "DELETE FROM PbapContacts WHERE DeviceAddress = @addr";
      cmd.Parameters.AddWithValue("@addr", deviceAddress);
      await cmd.ExecuteNonQueryAsync(ct);
    }
    finally
    {
      ReturnConnection(conn);
    }
  }
}
