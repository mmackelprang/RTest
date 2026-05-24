namespace Radio.Web.Tests.Configuration;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Radio.Configuration.Bridge;
using Radio.Web.Models;

/// <summary>
/// Pins the SQLite-bridge → IOptionsMonitor seam for Radio.Web's
/// <see cref="DisplayOptions"/> binding. Regression guard for the bug where
/// Radio.Web.csproj was missing the Radio.Configuration project reference
/// and Radio.Web/Program.cs was missing the <c>AddSqliteConfigStore(...)</c>
/// call — so user-saved values for <c>Display:TimeFormat</c> /
/// <c>Display:ShowSeconds</c> had no effect on the topbar clock, sleep clock,
/// or queue ends-prediction (only the hardcoded <see cref="DisplayOptions"/>
/// defaults were ever read).
///
/// If this test ever starts failing because the bridge wiring isn't on the
/// configuration builder, that's the same regression. Fix by re-checking
/// Radio.Web/Program.cs against Radio.API/Program.cs's bridge wiring block.
/// </summary>
public class SqliteConfigBridgeIntegrationTests : IDisposable
{
  private readonly string _testDirectory;
  private readonly string _dbPath;
  private readonly string _connectionString;
  private const string TableName = "Config_sqlite";

  public SqliteConfigBridgeIntegrationTests()
  {
    _testDirectory = Path.Combine(Path.GetTempPath(), $"WebSqliteBridgeTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_testDirectory);
    _dbPath = Path.Combine(_testDirectory, "configuration.db");
    _connectionString = $"Data Source={_dbPath}";
  }

  public void Dispose()
  {
    try
    {
      // Force SQLite to release file handles before deleting the temp dir on
      // Windows (otherwise the file is still locked by the provider's last
      // connection and Directory.Delete throws IOException).
      SqliteConnection.ClearAllPools();
      if (Directory.Exists(_testDirectory))
      {
        Directory.Delete(_testDirectory, recursive: true);
      }
    }
    catch { /* cleanup best-effort */ }
  }

  /// <summary>
  /// End-to-end: write a <c>display:timeFormat=12h</c> row to a temp SQLite
  /// config DB, build an <see cref="IConfiguration"/> through the bridge,
  /// resolve <see cref="IOptionsMonitor{DisplayOptions}"/>, and assert the
  /// monitor sees <c>"12h"</c> instead of the <see cref="DisplayOptions"/>
  /// default of <c>"24h"</c>.
  ///
  /// This is the EXACT path the kiosk takes: System Config page writes a
  /// <c>Display:TimeFormat</c> row → Radio.Web reloads its IConfiguration on
  /// next process restart or reload trigger → MainLayout/Sleep/QueueHistory
  /// read <c>IOptionsMonitor&lt;DisplayOptions&gt;.CurrentValue.TimeFormat</c>.
  /// </summary>
  [Fact]
  public void DisplayTimeFormat_SqliteValue_FlowsThroughBridge_ToIOptionsMonitor()
  {
    // Arrange — pre-populate the SQLite store with a 12h time format,
    // mirroring what the System Config page writes via ConfigurationManager.
    // Note: lowercase "display:timeFormat" — the bridge does case-insensitive
    // lookups, so it must still bind to DisplayOptions.TimeFormat. This
    // mirrors the real-world key the UI saves (camelCase property name).
    CreateTableAndInsert(("display:timeFormat", "12h"));

    var notifier = new ConfigStoreChangeNotifier();
    var configBuilder = new ConfigurationBuilder();

    // Seed the appsettings-equivalent default (matches what Radio.Web ships
    // in appsettings.json — TimeFormat defaults to 24h before user override).
    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Display:TimeFormat"] = "24h",
      ["Display:ShowSeconds"] = "false",
    });

    // Add the SQLite bridge AFTER the in-memory defaults so it overrides them,
    // matching production order in Radio.Web/Program.cs (bridge comes after
    // appsettings.json in the configuration chain).
    configBuilder.AddSqliteConfigStore(_dbPath, "sqlite", notifier);

    var configuration = configBuilder.Build();

    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(configuration);
    services.Configure<DisplayOptions>(configuration.GetSection(DisplayOptions.SectionName));
    services.AddSingleton(notifier);
    var sp = services.BuildServiceProvider();

    // Act
    var monitor = sp.GetRequiredService<IOptionsMonitor<DisplayOptions>>();

    // Assert — SQLite value overrides the 24h default
    Assert.Equal("12h", monitor.CurrentValue.TimeFormat);
  }

  /// <summary>
  /// Belt-and-braces: NotifyReload triggers re-read so a runtime change to
  /// the SQLite row is picked up by IOptionsMonitor.CurrentValue without
  /// rebuilding the service provider. Documents the in-process hot-reload
  /// contract for Radio.Web (used by the API process; Web sees changes on
  /// next page load because the notifier doesn't cross process boundaries).
  /// </summary>
  [Fact]
  public void DisplayTimeFormat_Reload_PicksUpSqliteChange()
  {
    // Arrange — start with 24h in SQLite (matches default)
    CreateTableAndInsert(("display:timeFormat", "24h"));

    var notifier = new ConfigStoreChangeNotifier();
    var configBuilder = new ConfigurationBuilder();
    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Display:TimeFormat"] = "24h",
    });
    configBuilder.AddSqliteConfigStore(_dbPath, "sqlite", notifier);

    var configuration = configBuilder.Build();
    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(configuration);
    services.Configure<DisplayOptions>(configuration.GetSection(DisplayOptions.SectionName));
    services.AddSingleton(notifier);
    var sp = services.BuildServiceProvider();

    var monitor = sp.GetRequiredService<IOptionsMonitor<DisplayOptions>>();
    Assert.Equal("24h", monitor.CurrentValue.TimeFormat);

    // Act — UI flips the value and pings the notifier
    UpdateValue("display:timeFormat", "12h");
    notifier.NotifyReload();

    // Assert — monitor reflects the new value
    Assert.Equal("12h", monitor.CurrentValue.TimeFormat);
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
