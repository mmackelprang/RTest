using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
using IConfigurationManager = Radio.Configuration.Abstractions.IConfigurationManager;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// UI-28: Shuffle and Repeat on the file player survive a configuration reload.
/// </summary>
/// <remarks>
/// <para>
/// The owner saw both buttons switch themselves off "whenever the progress bar updates". The buttons were
/// telling the truth: the server's values had reverted. <see cref="FilePlayerAudioSource"/> kept them only in
/// <c>IOptionsMonitor&lt;FilePlayerPreferences&gt;.CurrentValue</c>, and every
/// <c>IConfigurationManager.SetValueAsync</c> in the API (the Web queue panel's <c>queue.state</c> save among
/// them) reloads the SQLite configuration provider, after which the monitor rebuilds <c>CurrentValue</c> from
/// configuration that never held them.
/// </para>
/// <para>
/// This fixture reproduces that chain with the real options machinery: a configuration provider whose
/// <c>Load</c> reads a dictionary standing in for the SQLite table (and flattens JSON arrays as
/// <c>SqliteConfigurationProvider</c> does), and a mocked store whose writes land in the same dictionary.
/// <see cref="StoreBackedProvider.Reload"/> is what <c>ConfigStoreChangeNotifier.NotifyReload</c> does to the
/// real provider. No timers are involved, so nothing here waits on a clock.
/// </para>
/// </remarks>
public class FilePlayerPlaybackModePersistenceTests : IDisposable
{
  private readonly StoreBackedProvider _provider = new();
  private readonly ServiceProvider _services;
  private readonly IOptionsMonitor<FilePlayerPreferences> _preferences;
  private readonly Mock<IConfigurationManager> _configManager = new();
  private readonly Mock<IConfigurationStore> _store = new();
  private readonly string _testDir;

  public FilePlayerPlaybackModePersistenceTests()
  {
    IConfiguration configuration = new ConfigurationBuilder().Add(new StoreBackedSource(_provider)).Build();
    var services = new ServiceCollection();
    services.AddOptions();
    services.Configure<FilePlayerPreferences>(configuration.GetSection(FilePlayerPreferences.SectionName));
    _services = services.BuildServiceProvider();
    _preferences = _services.GetRequiredService<IOptionsMonitor<FilePlayerPreferences>>();

    _configManager.SetupGet(m => m.CurrentStoreType).Returns(ConfigurationStoreType.Sqlite);
    _configManager.Setup(m => m.GetStoreAsync("sqlite", It.IsAny<CancellationToken>())).ReturnsAsync(_store.Object);
    _store
      .Setup(s => s.SetEntriesAsync(It.IsAny<IEnumerable<ConfigurationEntry>>(), It.IsAny<CancellationToken>()))
      .Callback<IEnumerable<ConfigurationEntry>, CancellationToken>((entries, _) => _provider.Write(entries))
      .Returns(Task.CompletedTask);
    _store.Setup(s => s.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

    _testDir = Path.Combine(Path.GetTempPath(), $"FilePlayerModeTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_testDir);
  }

  public void Dispose()
  {
    _services.Dispose();
    if (Directory.Exists(_testDir))
    {
      Directory.Delete(_testDir, recursive: true);
    }
  }

  private FilePlayerAudioSource CreateSource(IConfigurationManager? configurationManager) =>
    new(
      NullLogger<FilePlayerAudioSource>.Instance,
      Mock.Of<IOptionsMonitor<FilePlayerOptions>>(o => o.CurrentValue == new FilePlayerOptions()),
      _preferences,
      _testDir,
      configurationManager: configurationManager);

  [Fact]
  public void Harness_AReloadRebuildsCurrentValue_SoAnUnpersistedMutationIsLost()
  {
    // ⚠ Prove the instrument before trusting its silence. If the reload did NOT rebuild CurrentValue, the
    // two tests below would pass whether or not anything was persisted. This is the pre-fix mechanism in
    // isolation: mutate the cached object, reload, and the mutation is gone.
    _preferences.CurrentValue.Shuffle = true;
    _preferences.CurrentValue.Repeat = RepeatMode.All;

    _provider.Reload();

    Assert.False(_preferences.CurrentValue.Shuffle);
    Assert.Equal(RepeatMode.Off, _preferences.CurrentValue.Repeat);
  }

  [Fact]
  public async Task Shuffle_SurvivesAConfigurationReload()
  {
    await using var source = CreateSource(_configManager.Object);

    await source.SetShuffleAsync(true);
    Assert.True(source.IsShuffleEnabled);

    _provider.Reload();

    Assert.True(source.IsShuffleEnabled, "a reload rebuilds CurrentValue from the store, which now holds it");
  }

  [Fact]
  public async Task Repeat_SurvivesAConfigurationReload()
  {
    await using var source = CreateSource(_configManager.Object);

    await source.SetRepeatModeAsync(RepeatMode.All);
    _provider.Reload();
    Assert.Equal(RepeatMode.All, source.RepeatMode);

    await source.SetRepeatModeAsync(RepeatMode.One);
    _provider.Reload();
    Assert.Equal(RepeatMode.One, source.RepeatMode);
  }

  [Fact]
  public async Task TurningShuffleAndRepeatOff_AlsoSurvivesAReload()
  {
    // The store already holds "on" from an earlier session; turning them off must persist "off", or the
    // next reload would switch them back on.
    _provider.Write(new[]
    {
      new ConfigurationEntry { Key = "FilePlayerPreferences:Shuffle", Value = "True" },
      new ConfigurationEntry { Key = "FilePlayerPreferences:Repeat", Value = "All" },
    });
    _provider.Reload();
    await using var source = CreateSource(_configManager.Object);
    Assert.True(source.IsShuffleEnabled);

    await source.SetShuffleAsync(false);
    await source.SetRepeatModeAsync(RepeatMode.Off);
    _provider.Reload();

    Assert.False(source.IsShuffleEnabled);
    Assert.Equal(RepeatMode.Off, source.RepeatMode);
  }

  [Fact]
  public async Task StoreFailure_StillTogglesInMemory_AndDoesNotThrow()
  {
    _store
      .Setup(s => s.SetEntriesAsync(It.IsAny<IEnumerable<ConfigurationEntry>>(), It.IsAny<CancellationToken>()))
      .ThrowsAsync(new IOException("disk full"));
    await using var source = CreateSource(_configManager.Object);

    await source.SetShuffleAsync(true);
    await source.SetRepeatModeAsync(RepeatMode.All);

    Assert.True(source.IsShuffleEnabled);
    Assert.Equal(RepeatMode.All, source.RepeatMode);
  }

  [Fact]
  public async Task NoConfigurationManager_StillTogglesInMemory()
  {
    await using var source = CreateSource(configurationManager: null);

    await source.SetShuffleAsync(true);
    await source.SetRepeatModeAsync(RepeatMode.One);

    Assert.True(source.IsShuffleEnabled);
    Assert.Equal(RepeatMode.One, source.RepeatMode);
  }

  /// <summary>Stands in for the SQLite table <c>SqliteConfigurationProvider</c> reads.</summary>
  private sealed class StoreBackedProvider : ConfigurationProvider
  {
    private readonly object _gate = new();
    private readonly Dictionary<string, string?> _table = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A store write. Like <c>IConfigurationStore.SetEntriesAsync</c>, it does not reload.</summary>
    public void Write(IEnumerable<ConfigurationEntry> entries)
    {
      lock (_gate)
      {
        foreach (ConfigurationEntry entry in entries)
        {
          _table[entry.Key] = entry.Value;
        }
      }
    }

    public override void Load()
    {
      var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
      lock (_gate)
      {
        foreach ((string key, string? value) in _table)
        {
          // SqliteConfigurationProvider flattens JSON arrays into indexed keys; the queue save writes one.
          if (value != null && value.TrimStart().StartsWith('['))
          {
            int i = 0;
            foreach (JsonElement item in JsonDocument.Parse(value).RootElement.EnumerateArray())
            {
              data[$"{key}:{i++}"] = item.ToString();
            }
          }
          else
          {
            data[key] = value;
          }
        }
      }

      Data = data;
    }

    /// <summary>What <c>ConfigStoreChangeNotifier.NotifyReload</c> does to the real provider.</summary>
    public void Reload()
    {
      Load();
      OnReload();
    }
  }

  private sealed class StoreBackedSource(StoreBackedProvider provider) : IConfigurationSource
  {
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
  }
}
