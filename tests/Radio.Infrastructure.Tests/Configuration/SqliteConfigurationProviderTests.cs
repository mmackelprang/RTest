namespace Radio.Infrastructure.Tests.Configuration;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.Configuration.Bridge;

/// <summary>
/// Tests for the SQLite configuration bridge provider that connects
/// the SQLite config store to .NET's IConfiguration/IOptionsMonitor pipeline.
/// </summary>
public class SqliteConfigurationProviderTests : IDisposable
{
  private readonly string _testDirectory;
  private readonly string _dbPath;
  private readonly string _connectionString;
  private const string TableName = "Config_sqlite";

  public SqliteConfigurationProviderTests()
  {
    _testDirectory = Path.Combine(Path.GetTempPath(), $"SqliteBridgeTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_testDirectory);
    _dbPath = Path.Combine(_testDirectory, "test.db");
    _connectionString = $"Data Source={_dbPath}";
  }

  public void Dispose()
  {
    try
    {
      if (Directory.Exists(_testDirectory))
      {
        Directory.Delete(_testDirectory, recursive: true);
      }
    }
    catch { /* cleanup best-effort */ }
  }

  [Fact]
  public void Load_ReadsValuesFromSqliteTable()
  {
    // Arrange
    CreateTableAndInsert(("audio:volume", "75"), ("audio:muted", "false"));

    var provider = new SqliteConfigurationProvider(_connectionString, TableName);

    // Act
    provider.Load();

    // Assert
    Assert.True(provider.TryGet("audio:volume", out var volume));
    Assert.Equal("75", volume);
    Assert.True(provider.TryGet("audio:muted", out var muted));
    Assert.Equal("false", muted);
  }

  [Fact]
  public void Load_FlattensJsonObject_IntoHierarchicalKeys()
  {
    // Arrange — store a JSON object value
    CreateTableAndInsert(("devices:Radio", """{"USBPort":"AB13X","Enabled":true}"""));

    var provider = new SqliteConfigurationProvider(_connectionString, TableName);

    // Act
    provider.Load();

    // Assert
    Assert.True(provider.TryGet("devices:Radio:USBPort", out var usbPort));
    Assert.Equal("AB13X", usbPort);
    Assert.True(provider.TryGet("devices:Radio:Enabled", out var enabled));
    Assert.Equal("True", enabled);
  }

  [Fact]
  public void Load_FlattensJsonArray_IntoIndexedKeys()
  {
    // Arrange — store a JSON array value
    CreateTableAndInsert(("presets", """["FM 101.1","AM 780","FM 93.5"]"""));

    var provider = new SqliteConfigurationProvider(_connectionString, TableName);

    // Act
    provider.Load();

    // Assert
    Assert.True(provider.TryGet("presets:0", out var v0));
    Assert.Equal("FM 101.1", v0);
    Assert.True(provider.TryGet("presets:1", out var v1));
    Assert.Equal("AM 780", v1);
    Assert.True(provider.TryGet("presets:2", out var v2));
    Assert.Equal("FM 93.5", v2);
  }

  [Fact]
  public void Load_CaseInsensitiveKeyLookup()
  {
    // Arrange
    CreateTableAndInsert(("Fingerprinting:UseShazamForAllSources", "true"));

    var provider = new SqliteConfigurationProvider(_connectionString, TableName);

    // Act
    provider.Load();

    // Assert — lookup with different casing should succeed
    Assert.True(provider.TryGet("fingerprinting:useshazamforallsources", out var value));
    Assert.Equal("true", value);
    Assert.True(provider.TryGet("FINGERPRINTING:USESHAZAMFORALLSOURCES", out var value2));
    Assert.Equal("true", value2);
  }

  [Fact]
  public void Load_MissingDatabase_EmptyData_NoException()
  {
    // Arrange — point at a non-existent database
    var missingDbPath = Path.Combine(_testDirectory, "nonexistent.db");
    var connectionString = $"Data Source={missingDbPath}";
    var provider = new SqliteConfigurationProvider(connectionString, TableName);

    // Act — should not throw
    provider.Load();

    // Assert — no values loaded
    Assert.False(provider.TryGet("any:key", out _));
  }

  [Fact]
  public void Load_MissingTable_EmptyData_NoException()
  {
    // Arrange — create DB but don't create the Config_sqlite table
    using (var conn = new SqliteConnection(_connectionString))
    {
      conn.Open();
      using var cmd = conn.CreateCommand();
      cmd.CommandText = "CREATE TABLE SomeOtherTable (Id INTEGER PRIMARY KEY)";
      cmd.ExecuteNonQuery();
    }

    var provider = new SqliteConfigurationProvider(_connectionString, TableName);

    // Act — should not throw
    provider.Load();

    // Assert — no values loaded
    Assert.False(provider.TryGet("any:key", out _));
  }

  [Fact]
  public void Reload_PicksUpNewValues()
  {
    // Arrange — initial load with one value
    CreateTableAndInsert(("audio:volume", "50"));
    var provider = new SqliteConfigurationProvider(_connectionString, TableName);
    provider.Load();

    Assert.True(provider.TryGet("audio:volume", out var initial));
    Assert.Equal("50", initial);

    // Act — update value in DB and reload
    UpdateValue("audio:volume", "80");
    provider.Reload();

    // Assert — new value visible
    Assert.True(provider.TryGet("audio:volume", out var updated));
    Assert.Equal("80", updated);
  }

  [Fact]
  public void Integration_WriteToSqlite_NotifyReload_IOptionsMonitorReflectsChange()
  {
    // Arrange — set up config with appsettings default + SQLite bridge
    CreateTableAndInsert(("Fingerprinting:UseShazamForAllSources", "false"));

    var notifier = new ConfigStoreChangeNotifier();

    var configBuilder = new ConfigurationBuilder();

    // Add a base JSON config with a default value
    var baseConfig = new Dictionary<string, string?>
    {
      ["Fingerprinting:Enabled"] = "true",
      ["Fingerprinting:UseShazamForAllSources"] = "false"
    };
    configBuilder.AddInMemoryCollection(baseConfig);

    // Add SQLite bridge (overrides base)
    configBuilder.AddSqliteConfigStore(_dbPath, "sqlite", notifier);

    var configuration = configBuilder.Build();

    // Wire up IOptionsMonitor via DI
    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(configuration);
    services.Configure<FingerprintingOptions>(configuration.GetSection("Fingerprinting"));
    services.AddSingleton(notifier);
    var sp = services.BuildServiceProvider();

    var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<FingerprintingOptions>>();

    // Initial state: UseShazamForAllSources = false
    Assert.False(optionsMonitor.CurrentValue.UseShazamForAllSources);

    // Act — simulate UI writing to SQLite and triggering reload
    UpdateValue("Fingerprinting:UseShazamForAllSources", "true");
    notifier.NotifyReload();

    // Assert — IOptionsMonitor now reflects the change
    Assert.True(optionsMonitor.CurrentValue.UseShazamForAllSources);
  }

  [Fact]
  public void Load_NonJsonStringValue_StoredAsIs()
  {
    // Arrange — plain string value that starts with neither { nor [
    CreateTableAndInsert(("source:name", "Bluetooth A2DP"));

    var provider = new SqliteConfigurationProvider(_connectionString, TableName);

    // Act
    provider.Load();

    // Assert
    Assert.True(provider.TryGet("source:name", out var value));
    Assert.Equal("Bluetooth A2DP", value);
  }

  [Fact]
  public void Load_NestedJsonObject_FlattensRecursively()
  {
    // Arrange — nested JSON
    var json = """{"SongRec":{"Enabled":true,"TimeoutSeconds":15},"Enabled":false}""";
    CreateTableAndInsert(("Fingerprinting", json));

    var provider = new SqliteConfigurationProvider(_connectionString, TableName);

    // Act
    provider.Load();

    // Assert
    Assert.True(provider.TryGet("Fingerprinting:SongRec:Enabled", out var songRecEnabled));
    Assert.Equal("True", songRecEnabled);
    Assert.True(provider.TryGet("Fingerprinting:SongRec:TimeoutSeconds", out var timeout));
    Assert.Equal("15", timeout);
    Assert.True(provider.TryGet("Fingerprinting:Enabled", out var fpEnabled));
    Assert.Equal("False", fpEnabled);
  }

  #region Helpers

  private void CreateTableAndInsert(params (string Key, string Value)[] entries)
  {
    using var conn = new SqliteConnection(_connectionString);
    conn.Open();

    using var createCmd = conn.CreateCommand();
    createCmd.CommandText = $@"
      CREATE TABLE IF NOT EXISTS {TableName} (
        Key TEXT PRIMARY KEY,
        Value TEXT NOT NULL,
        Description TEXT,
        LastModified TEXT NOT NULL
      )";
    createCmd.ExecuteNonQuery();

    foreach (var (key, value) in entries)
    {
      using var insertCmd = conn.CreateCommand();
      insertCmd.CommandText = $@"
        INSERT OR REPLACE INTO {TableName} (Key, Value, LastModified)
        VALUES (@Key, @Value, @LastModified)";
      insertCmd.Parameters.AddWithValue("@Key", key);
      insertCmd.Parameters.AddWithValue("@Value", value);
      insertCmd.Parameters.AddWithValue("@LastModified", DateTimeOffset.UtcNow.ToString("O"));
      insertCmd.ExecuteNonQuery();
    }
  }

  private void UpdateValue(string key, string value)
  {
    using var conn = new SqliteConnection(_connectionString);
    conn.Open();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = $@"
      UPDATE {TableName} SET Value = @Value, LastModified = @LastModified
      WHERE Key = @Key";
    cmd.Parameters.AddWithValue("@Key", key);
    cmd.Parameters.AddWithValue("@Value", value);
    cmd.Parameters.AddWithValue("@LastModified", DateTimeOffset.UtcNow.ToString("O"));
    cmd.ExecuteNonQuery();
  }

  #endregion
}
