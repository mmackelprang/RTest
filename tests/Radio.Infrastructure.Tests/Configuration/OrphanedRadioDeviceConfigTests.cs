using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Configuration.Bridge;
using Radio.Configuration.Models;
using Radio.Core.Configuration;
using Radio.Infrastructure.Configuration;
using Radio.Infrastructure.DependencyInjection;

namespace Radio.Infrastructure.Tests.Configuration;

/// <summary>
/// AUD-16 removed the RF320 USB radio and with it <c>DeviceOptions.Radio</c>. The deployed box still
/// carries that configuration, and none of it is migrated away:
///
/// <list type="bullet">
///   <item>SQLite config store rows <c>devices:radio|{"usbPort":"/dev/ttyUSB0"}</c> and
///         <c>devices:Radio|{"usbPort":""}</c>, next to the live <c>devices:vinyl</c> /
///         <c>devices:Vinyl</c> rows, both <c>{"usbPort":"USB Microphone"}</c>;</item>
///   <item><c>Devices:Radio:USBPort = "AB13X"</c> in the box's own <c>appsettings.Production.json</c>,
///         which the deploy never overwrites.</item>
/// </list>
///
/// These tests use those literal shapes (measured read-only on the box 2026-09-30) and pin that the
/// orphans are inert: nothing throws, and Vinyl still resolves to the value the box actually uses.
/// </summary>
public class OrphanedRadioDeviceConfigTests : IDisposable
{
  private const string OrphanRadioLower = """{"usbPort":"/dev/ttyUSB0"}""";
  private const string OrphanRadioUpper = """{"usbPort":""}""";
  private const string LiveVinyl = """{"usbPort":"USB Microphone"}""";

  private readonly string _testDirectory;
  private readonly string _dbPath;

  public OrphanedRadioDeviceConfigTests()
  {
    _testDirectory = Path.Combine(Path.GetTempPath(), $"OrphanRadioConfig_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_testDirectory);
    _dbPath = Path.Combine(_testDirectory, "configuration.db");
  }

  public void Dispose()
  {
    SqliteConnection.ClearAllPools();
    try
    {
      if (Directory.Exists(_testDirectory))
      {
        Directory.Delete(_testDirectory, recursive: true);
      }
    }
    catch { /* cleanup best-effort */ }
  }

  /// <summary>
  /// (a) Options binding. Builds configuration in the same layer order as the API — shipped
  /// appsettings.json, the box's appsettings.Production.json, then the real SQLite bridge over a real
  /// table holding the box's rows — and binds <see cref="DeviceOptions"/> through the production
  /// registration (<c>AudioServiceExtensions.AddSoundFlowAudio(services, configuration)</c>), not a
  /// copy of it.
  /// </summary>
  [Fact]
  public void DeviceOptionsBinding_WithTheBoxsOrphanedRadioConfig_DoesNotThrow_AndBindsVinyl()
  {
    CreateStoreRows(
      ("devices:radio", OrphanRadioLower),
      ("devices:Radio", OrphanRadioUpper),
      ("devices:vinyl", LiveVinyl),
      ("devices:Vinyl", LiveVinyl));

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        // src/Radio.API/appsettings.json after AUD-16
        ["Devices:Vinyl:USBPort"] = "",
      })
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        // /opt/radio-console/api/appsettings.Production.json on the box
        ["Devices:Radio:USBPort"] = "AB13X",
        ["Devices:Vinyl:USBPort"] = "",
      })
      .AddSqliteConfigStore(_dbPath, "sqlite")
      .Build();

    // The orphans really are present in the bound section — otherwise this test proves nothing.
    Assert.NotEmpty(configuration.GetSection("Devices:Radio").GetChildren());

    var services = new ServiceCollection();
    services.AddSoundFlowAudio(configuration);
    using var provider = services.BuildServiceProvider();

    var options = provider.GetRequiredService<IOptionsMonitor<DeviceOptions>>().CurrentValue;

    Assert.Equal("USB Microphone", options.Vinyl.USBPort);
  }

  /// <summary>
  /// (a) The resolver, fed the box's four <c>devices:*</c> rows exactly. It must return the live
  /// Vinyl value and must never read the orphaned radio rows — they have the same JSON shape as a
  /// Vinyl row, so reading one by mistake would silently hand Vinyl the radio's port.
  /// </summary>
  [Fact]
  public async Task Resolver_WithTheBoxsOrphanedRadioRows_ResolvesVinyl_AndNeverReadsTheRadioRows()
  {
    var configManager = new Mock<Radio.Configuration.Abstractions.IConfigurationManager>();
    configManager.Setup(x => x.CurrentStoreType).Returns(ConfigurationStoreType.Sqlite);
    var rows = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["devices:radio"] = OrphanRadioLower,
      ["devices:Radio"] = OrphanRadioUpper,
      ["devices:vinyl"] = LiveVinyl,
      ["devices:Vinyl"] = LiveVinyl,
    };
    configManager
      .Setup(x => x.GetValueAsync<string>("sqlite", It.IsAny<string>(), It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string _, string key, ConfigurationReadMode _, CancellationToken _) =>
        rows.TryGetValue(key, out var value) ? value : null);

    var fallback = new Mock<IOptionsMonitor<DeviceOptions>>();
    fallback.Setup(x => x.CurrentValue).Returns(new DeviceOptions());

    var resolver = new DeviceOptionsResolver(
      NullLogger<DeviceOptionsResolver>.Instance, fallback.Object, configManager.Object);

    var options = await resolver.GetDeviceOptionsAsync();
    var vinylPort = await resolver.GetVinylUSBPortAsync();

    Assert.Equal("USB Microphone", options.Vinyl.USBPort);
    Assert.Equal("USB Microphone", vinylPort);
    configManager.Verify(
      x => x.GetValueAsync<string>(It.IsAny<string>(), "devices:radio", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()),
      Times.Never);
    configManager.Verify(
      x => x.GetValueAsync<string>(It.IsAny<string>(), "devices:Radio", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()),
      Times.Never);
  }

  /// <summary>
  /// Creates the SQLite config store table the bridge reads (same schema as
  /// <c>SqliteConfigurationStore</c>) and inserts the given rows.
  /// </summary>
  private void CreateStoreRows(params (string Key, string Value)[] entries)
  {
    using var conn = new SqliteConnection($"Data Source={_dbPath}");
    conn.Open();

    using (var create = conn.CreateCommand())
    {
      create.CommandText = """
        CREATE TABLE IF NOT EXISTS Config_sqlite (
          Key TEXT PRIMARY KEY,
          Value TEXT NOT NULL,
          Description TEXT,
          LastModified TEXT NOT NULL
        )
        """;
      create.ExecuteNonQuery();
    }

    foreach (var (key, value) in entries)
    {
      using var insert = conn.CreateCommand();
      insert.CommandText = "INSERT INTO Config_sqlite (Key, Value, LastModified) VALUES (@Key, @Value, @LastModified)";
      insert.Parameters.AddWithValue("@Key", key);
      insert.Parameters.AddWithValue("@Value", value);
      insert.Parameters.AddWithValue("@LastModified", DateTimeOffset.UtcNow.ToString("O"));
      insert.ExecuteNonQuery();
    }
  }
}
