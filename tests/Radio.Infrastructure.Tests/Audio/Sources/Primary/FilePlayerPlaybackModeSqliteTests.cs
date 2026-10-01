using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Bridge;
using Radio.Configuration.Models;
using Radio.Configuration.Stores;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
using IConfigurationManager = Radio.Configuration.Abstractions.IConfigurationManager;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// UI-28 on the production configuration stack: a real <see cref="SqliteConfigurationStore"/> writing a real
/// SQLite file, read back by the real bridge (<c>AddSqliteConfigStore</c>, table <c>Config_sqlite</c>, as
/// <c>Radio.API/Program.cs</c> registers it) and its <see cref="ConfigStoreChangeNotifier"/>.
/// </summary>
/// <remarks>
/// <see cref="FilePlayerPlaybackModePersistenceTests"/> models the provider with a dictionary; this fixture
/// checks what that model cannot: that the store's table is the one the bridge reads, that the strings
/// written ("True", "All") bind back through the real provider, and that a reload triggered by an unrelated
/// write — the box's <c>queue.state</c> save — no longer turns Shuffle and Repeat off. Only the
/// <c>IConfigurationManager</c> that hands out the store is mocked.
/// </remarks>
public sealed class FilePlayerPlaybackModeSqliteTests : IAsyncDisposable
{
  private readonly string _dir = Path.Combine(Path.GetTempPath(), $"FilePlayerModeSqlite_{Guid.NewGuid():N}");
  private readonly ConfigStoreChangeNotifier _notifier = new();
  private readonly SqliteConfigurationStore _store;
  private readonly ServiceProvider _services;
  private readonly Mock<IConfigurationManager> _configManager = new();

  public FilePlayerPlaybackModeSqliteTests()
  {
    Directory.CreateDirectory(_dir);
    string dbPath = Path.Combine(_dir, "configuration.db");
    _store = new SqliteConfigurationStore(
      "sqlite", $"Data Source={dbPath}", Mock.Of<ISecretsProvider>(), NullLogger<SqliteConfigurationStore>.Instance);
    // Create the table before the bridge's first Load, as the box's long-lived database already has it.
    _store.SetEntryAsync("Unrelated:Seed", "1").GetAwaiter().GetResult();

    IConfiguration configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        // src/Radio.API/appsettings.json's FilePlayerPreferences defaults
        ["FilePlayerPreferences:Shuffle"] = "false",
        ["FilePlayerPreferences:Repeat"] = "Off",
      })
      .AddSqliteConfigStore(dbPath, "sqlite", _notifier)
      .Build();
    var services = new ServiceCollection();
    services.Configure<FilePlayerPreferences>(configuration.GetSection(FilePlayerPreferences.SectionName));
    _services = services.BuildServiceProvider();

    _configManager.SetupGet(m => m.CurrentStoreType).Returns(ConfigurationStoreType.Sqlite);
    _configManager.Setup(m => m.GetStoreAsync("sqlite", It.IsAny<CancellationToken>())).ReturnsAsync(_store);
  }

  public async ValueTask DisposeAsync()
  {
    await _services.DisposeAsync();
    await _store.DisposeAsync();
    SqliteConnection.ClearAllPools();
    try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
  }

  private FilePlayerAudioSource CreateSource() =>
    new(
      NullLogger<FilePlayerAudioSource>.Instance,
      Mock.Of<IOptionsMonitor<FilePlayerOptions>>(o => o.CurrentValue == new FilePlayerOptions()),
      _services.GetRequiredService<IOptionsMonitor<FilePlayerPreferences>>(),
      _dir,
      configurationManager: _configManager.Object);

  /// <summary>What <c>ConfigurationManager.SetValueAsync</c> does for the Web's <c>queue.state</c> save.</summary>
  private async Task UnrelatedConfigWriteAsync()
  {
    await _store.SetEntryAsync("queue.state:savedAt", DateTimeOffset.UtcNow.ToString("O"));
    _notifier.NotifyReload();
  }

  [Fact]
  public async Task ShuffleAndRepeat_SurviveAnUnrelatedConfigWrite_OnTheRealStack()
  {
    await using var source = CreateSource();

    await source.SetShuffleAsync(true);
    await source.SetRepeatModeAsync(RepeatMode.All);
    await UnrelatedConfigWriteAsync();

    Assert.True(source.IsShuffleEnabled);
    Assert.Equal(RepeatMode.All, source.RepeatMode);

    // And off again, so a stored "on" cannot mask a broken write.
    await source.SetShuffleAsync(false);
    await source.SetRepeatModeAsync(RepeatMode.One);
    await UnrelatedConfigWriteAsync();

    Assert.False(source.IsShuffleEnabled);
    Assert.Equal(RepeatMode.One, source.RepeatMode);
  }

  [Fact]
  public async Task TheStoredStrings_AreThePreferencesPersistenceServiceShape()
  {
    // PreferencesPersistenceService writes the same keys every 30 s (JsonElement.ToString of a bool is
    // "True"/"False"; JsonStringEnumConverter writes the enum name). Matching it means the two writers never
    // disagree about a value's spelling.
    await using var source = CreateSource();

    await source.SetShuffleAsync(true);
    await source.SetRepeatModeAsync(RepeatMode.All);

    Assert.Equal("True", (await _store.GetEntryAsync("FilePlayerPreferences:Shuffle"))?.Value);
    Assert.Equal("All", (await _store.GetEntryAsync("FilePlayerPreferences:Repeat"))?.Value);
  }
}
