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
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
using IConfigurationManager = Radio.Configuration.Abstractions.IConfigurationManager;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// AUD-98 on the production configuration stack — a real <see cref="SqliteConfigurationStore"/> read back
/// through the real bridge, as <see cref="FilePlayerPlaybackModeSqliteTests"/> sets it up. Checks what the
/// in-memory <see cref="FilePlayerQueueRestoreTests"/> cannot: that the lists survive being stored as JSON and
/// flattened by the bridge, that a stale case-variant row cannot add tracks to a restored list, that the
/// box's old-shape rows still load, and that <c>CurrentValue</c> carries the new list before any reload.
/// </summary>
public sealed class FilePlayerQueueRestoreSqliteTests : IAsyncDisposable
{
  private readonly string _dir = Path.Combine(Path.GetTempPath(), $"FilePlayerRestoreSqlite_{Guid.NewGuid():N}");
  private readonly ConfigStoreChangeNotifier _notifier = new();
  private readonly SqliteConfigurationStore _store;
  private readonly ServiceProvider _services;
  private readonly Mock<IConfigurationManager> _configManager = new();

  public FilePlayerQueueRestoreSqliteTests()
  {
    Directory.CreateDirectory(_dir);
    string dbPath = Path.Combine(_dir, "configuration.db");
    _store = new SqliteConfigurationStore(
      "sqlite", $"Data Source={dbPath}", Mock.Of<ISecretsProvider>(), NullLogger<SqliteConfigurationStore>.Instance);
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

  private IOptionsMonitor<FilePlayerPreferences> Preferences =>
    _services.GetRequiredService<IOptionsMonitor<FilePlayerPreferences>>();

  private FilePlayerAudioSource CreateSource() =>
    new(
      NullLogger<FilePlayerAudioSource>.Instance,
      Mock.Of<IOptionsMonitor<FilePlayerOptions>>(o => o.CurrentValue == new FilePlayerOptions()),
      Preferences,
      _dir,
      configurationManager: _configManager.Object);

  private List<string> CreateTracks(string prefix, int count)
  {
    var paths = new List<string>();
    for (int i = 1; i <= count; i++)
    {
      string path = Path.Combine(_dir, $"{prefix}{i:D2}.mp3");
      File.WriteAllText(path, "not audio");
      paths.Add(path);
    }
    return paths;
  }

  /// <summary>Plays into a list, waits for the store write, reloads configuration, restores a new source.</summary>
  private async Task<FilePlayerAudioSource> PlayThenRestartAsync(List<string> tracks, int advances)
  {
    await using (FilePlayerAudioSource before = CreateSource())
    {
      await before.LoadPlaylistAsync(tracks);
      for (int i = 0; i < advances; i++)
      {
        await before.NextAsync();
      }
      await before.WhenQueueStateSavedAsync();
    }

    _notifier.NotifyReload();
    FilePlayerAudioSource restored = CreateSource();
    await restored.InitializeAsync();
    return restored;
  }

  [Fact]
  public async Task Restart_OnTheRealStack_BringsBackTheWholeList()
  {
    List<string> tracks = CreateTracks("t", 5);

    await using FilePlayerAudioSource restored = await PlayThenRestartAsync(tracks, advances: 2);
    IReadOnlyList<QueueItem> full = await restored.GetFullPlaylistAsync();

    Assert.Equal(tracks, full.Select(i => i.Id));
    Assert.Equal(tracks[2], restored.CurrentFile);
    Assert.Equal(2, full.Count(i => i.State == QueueItemState.Played));
  }

  [Fact]
  public async Task AStaleLowercaseRowWithALongerList_CannotAddTracksToTheRestoredOne()
  {
    // The System Config page writes this section lowercased (see PersistPlaybackModeAsync). A lowercase
    // queueitems row holding a longer, older list would, if left alone, contribute its entries 3..7 to the
    // bound list: the bridge's keys ignore case, and a JSON array flattens to :0, :1, ...
    List<string> old = CreateTracks("old", 8);
    await _store.SetEntryAsync("fileplayerpreferences:queueitems", System.Text.Json.JsonSerializer.Serialize(old));
    _notifier.NotifyReload();

    List<string> tracks = CreateTracks("t", 3);
    await using FilePlayerAudioSource restored = await PlayThenRestartAsync(tracks, advances: 1);

    Assert.Equal(tracks, (await restored.GetFullPlaylistAsync()).Select(i => i.Id));
    Assert.Equal(
      System.Text.Json.JsonSerializer.Serialize(tracks),
      (await _store.GetEntryAsync("fileplayerpreferences:queueitems"))?.Value);
  }

  [Fact]
  public async Task TheBoxsOldShapeRows_StillLoad()
  {
    // As measured on the box 2026-10-02: QueueItems (current + upcoming), CurrentQueueIndex 0, no
    // OriginalOrder, Shuffle and Repeat in both cases.
    List<string> tracks = CreateTracks("t", 4);
    await _store.SetEntryAsync("FilePlayerPreferences:CurrentQueueIndex", "0");
    await _store.SetEntryAsync("FilePlayerPreferences:QueueItems", System.Text.Json.JsonSerializer.Serialize(tracks));
    await _store.SetEntryAsync("FilePlayerPreferences:Repeat", "All");
    await _store.SetEntryAsync("FilePlayerPreferences:Shuffle", "True");
    await _store.SetEntryAsync("fileplayerpreferences:repeat", "All");
    await _store.SetEntryAsync("fileplayerpreferences:shuffle", "True");
    _notifier.NotifyReload();

    await using FilePlayerAudioSource source = CreateSource();
    await source.InitializeAsync();

    Assert.Equal(AudioSourceState.Ready, source.State);
    Assert.Equal(tracks[0], source.CurrentFile);
    Assert.Equal(tracks, (await source.GetFullPlaylistAsync()).Select(i => i.Id));
  }

  [Fact]
  public async Task CurrentValue_CarriesTheNewList_BeforeAnyReload()
  {
    // PreferencesPersistenceService writes CurrentValue back every 30 s. If CurrentValue still held the list
    // from the last reload, that save would put it back over the one just written.
    List<string> tracks = CreateTracks("t", 3);
    await using FilePlayerAudioSource source = CreateSource();
    await source.LoadPlaylistAsync(tracks);
    await source.NextAsync();
    await source.WhenQueueStateSavedAsync();

    FilePlayerPreferences current = Preferences.CurrentValue;
    Assert.Equal(tracks, current.QueueItems);
    Assert.Equal(1, current.CurrentQueueIndex);
    Assert.Equal(tracks, current.OriginalOrder);
  }
}
