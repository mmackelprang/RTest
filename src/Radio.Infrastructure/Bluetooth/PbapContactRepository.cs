using System.Globalization;
using Microsoft.Data.Sqlite;
using Radio.Core.Interfaces.Bluetooth;
using Radio.Core.Models;
using Radio.Core.Utilities;

namespace Radio.Infrastructure.Bluetooth;

public class PbapContactRepository : IPbapContactRepository
{
  private readonly string _connectionString;
  private readonly SqliteConnection? _sharedConnection; // for testing with in-memory DB
  private readonly TimeProvider _time;

  public PbapContactRepository(string connectionString)
  {
    _connectionString = connectionString;
    _time = TimeProvider.System;
  }

  /// <summary>Test-only constructor for in-memory SQLite.</summary>
  /// <param name="sharedConnection">The open in-memory connection every call reuses.</param>
  /// <param name="timeProvider">Stamps <c>LastSynced</c> on upsert; lets a test order syncs deterministically.</param>
  internal PbapContactRepository(SqliteConnection sharedConnection, TimeProvider? timeProvider = null)
  {
    _sharedConnection = sharedConnection;
    _connectionString = sharedConnection.ConnectionString;
    _time = timeProvider ?? TimeProvider.System;
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

      try
      {
        // Delete existing contacts for this device
        using (var delCmd = conn.CreateCommand())
        {
          delCmd.Transaction = transaction;
          delCmd.CommandText = "DELETE FROM PbapContacts WHERE DeviceAddress = @addr";
          delCmd.Parameters.AddWithValue("@addr", deviceAddress);
          await delCmd.ExecuteNonQueryAsync(ct);
        }

        // Insert new contacts (one row per phone number)
        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var contact in contacts)
        {
          foreach (var number in contact.PhoneNumbers)
          {
            using var insCmd = conn.CreateCommand();
            insCmd.Transaction = transaction;
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
      catch
      {
        transaction.Rollback();
        throw;
      }
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

  /// <inheritdoc />
  /// <remarks>
  /// One query reads every candidate on every device (an exact match, or a stored number ending in the same
  /// seven digits) with its device's sync time; the ranking is done here rather than in SQL so the order is
  /// written once, in one place. <c>LastSynced</c> per device is <c>MAX(LastSynced)</c>: an upsert replaces a
  /// device's whole phone book with one timestamp, so every row of a device carries its last sync.
  /// </remarks>
  public async Task<PbapContactMatch?> FindByPhoneNumberAnyDeviceAsync(
    string normalizedNumber, string? preferredDeviceAddress = null, CancellationToken ct = default)
  {
    if (string.IsNullOrEmpty(normalizedNumber))
    {
      return null;
    }

    var last7 = PhoneNumberNormalizer.GetLast7(normalizedNumber);
    bool useSuffix = last7.Length == 7;

    var conn = GetConnection();
    try
    {
      using var cmd = conn.CreateCommand();
      cmd.CommandText = """
          SELECT c.Id, c.DeviceAddress, c.DisplayName, c.PhoneNumber, d.LastSynced
          FROM PbapContacts c
          JOIN (SELECT DeviceAddress, MAX(LastSynced) AS LastSynced FROM PbapContacts GROUP BY DeviceAddress) d
            ON d.DeviceAddress = c.DeviceAddress
          WHERE c.PhoneNumber = @phone OR (@useSuffix = 1 AND c.PhoneNumber LIKE @suffix)
          """;
      cmd.Parameters.AddWithValue("@phone", normalizedNumber);
      cmd.Parameters.AddWithValue("@useSuffix", useSuffix ? 1 : 0);
      cmd.Parameters.AddWithValue("@suffix", "%" + last7);

      var candidates = new List<(long Id, PbapContactMatch Match, DateTime? Synced)>();
      using var reader = await cmd.ExecuteReaderAsync(ct);
      while (await reader.ReadAsync(ct))
      {
        var phone = reader.GetString(3);
        DateTime? synced = reader.IsDBNull(4)
          ? null
          : DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        candidates.Add((reader.GetInt64(0),
          new PbapContactMatch(reader.GetString(1), reader.GetString(2), phone, phone == normalizedNumber),
          synced));
      }

      return candidates
        .OrderByDescending(c => c.Match.IsExactMatch)
        .ThenByDescending(c => preferredDeviceAddress is not null
          && string.Equals(c.Match.DeviceAddress, preferredDeviceAddress, StringComparison.OrdinalIgnoreCase))
        .ThenByDescending(c => c.Synced ?? DateTime.MinValue)
        .ThenBy(c => c.Match.DeviceAddress, StringComparer.Ordinal)
        .ThenBy(c => c.Id)
        .Select(c => c.Match)
        .FirstOrDefault();
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
        DateTime? synced = reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
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
