using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Services;
using RTLSDRCore.Bands;
using RTLSDRCore.Enums;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// Covers the per-band dial memory (ENC-5 Task 4).
///
/// <para>
/// The behaviour under test is a three-rung ladder — what was remembered, then the configured
/// default for the band, then the band's bottom edge — and the unit it stores in. Both matter:
/// today a band switch clamps the outgoing band's frequency into the incoming band's range, which
/// lands FM → AM at the top of the AM dial every time.
/// </para>
/// </summary>
public class RadioBandMemoryServiceTests
{
  /// <summary>
  /// A configuration store that keeps entries in a dictionary and matches sections the way the
  /// real stores do — on the prefix plus a ':' separator.
  /// </summary>
  private sealed class InMemoryStore : IConfigurationStore
  {
    public Dictionary<string, string> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int SaveCalls { get; private set; }

    public string StoreId => "config";
    public ConfigurationStoreType StoreType => ConfigurationStoreType.Json;

    public Task<ConfigurationEntry?> GetEntryAsync(
      string key, ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default) =>
      Task.FromResult(Entries.TryGetValue(key, out var v)
        ? new ConfigurationEntry { Key = key, Value = v }
        : null);

    public Task<IReadOnlyList<ConfigurationEntry>> GetAllEntriesAsync(
      ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default) =>
      Task.FromResult<IReadOnlyList<ConfigurationEntry>>(
        Entries.Select(kvp => new ConfigurationEntry { Key = kvp.Key, Value = kvp.Value }).ToList());

    public Task<IReadOnlyList<ConfigurationEntry>> GetEntriesBySectionAsync(
      string sectionPrefix, ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default)
    {
      var prefix = sectionPrefix.EndsWith(':') ? sectionPrefix : sectionPrefix + ":";
      return Task.FromResult<IReadOnlyList<ConfigurationEntry>>(
        Entries
          .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
          .Select(kvp => new ConfigurationEntry { Key = kvp.Key, Value = kvp.Value })
          .ToList());
    }

    public Task SetEntryAsync(string key, string value, CancellationToken ct = default)
    {
      Entries[key] = value;
      return Task.CompletedTask;
    }

    public Task SetEntriesAsync(IEnumerable<ConfigurationEntry> entries, CancellationToken ct = default)
    {
      foreach (var entry in entries)
      {
        Entries[entry.Key] = entry.Value;
      }

      return Task.CompletedTask;
    }

    public Task<bool> DeleteEntryAsync(string key, CancellationToken ct = default) =>
      Task.FromResult(Entries.Remove(key));

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
      Task.FromResult(Entries.ContainsKey(key));

    public Task<bool> SaveAsync(CancellationToken ct = default)
    {
      SaveCalls++;
      return Task.FromResult(true);
    }

    public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;
  }

  private readonly InMemoryStore _store = new();
  private readonly RadioOptions _options = new();

  private RadioBandMemoryService CreateService()
  {
    var manager = new Mock<IConfigurationManager>();
    manager.Setup(m => m.CurrentStoreType).Returns(ConfigurationStoreType.Json);
    manager.Setup(m => m.GetStoreAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(_store);

    var optionsMonitor = new Mock<IOptionsMonitor<RadioOptions>>();
    optionsMonitor.Setup(o => o.CurrentValue).Returns(_options);

    return new RadioBandMemoryService(
      NullLogger<RadioBandMemoryService>.Instance,
      manager.Object,
      optionsMonitor.Object);
  }

  [Fact]
  public async Task Get_ReturnsRememberedValue_WhenPresent()
  {
    _store.Entries["RadioBandMemory:LastFrequencyHzByBand:FM"] = "98500000";
    var service = CreateService();

    var frequency = await service.GetAsync(RadioBand.FM);

    Assert.Equal(98_500_000, frequency!.Value.Hertz);
  }

  [Fact]
  public async Task Get_FallsBackToConfiguredDefault_ForFm()
  {
    _options.DefaultFMFrequencyMHz = 104.3;
    var service = CreateService();

    var frequency = await service.GetAsync(RadioBand.FM);

    Assert.Equal(104_300_000, frequency!.Value.Hertz);
  }

  [Fact]
  public async Task Get_FallsBackToConfiguredDefault_ForAm()
  {
    _options.DefaultAMFrequencyKHz = 810.0;
    var service = CreateService();

    var frequency = await service.GetAsync(RadioBand.AM);

    Assert.Equal(810_000, frequency!.Value.Hertz);
  }

  [Fact]
  public async Task Get_FallsBackToBandBottomEdge_ForShortwave()
  {
    var service = CreateService();

    var frequency = await service.GetAsync(RadioBand.SW);

    // The bottom edge is a real tunable frequency and where a mechanical dial rests, not a
    // placeholder - so it is read from BandPresets rather than restated here.
    Assert.Equal(BandPresets.GetBand(BandType.Shortwave).MinFrequencyHz, frequency!.Value.Hertz);
  }

  [Fact]
  public async Task Get_ReturnsNull_WhenNoMemoryAndNoDefault()
  {
    var service = CreateService();

    // A value outside the declared enum: no configured default and no BandPresets mapping, which
    // is the only combination that produces null.
    var frequency = await service.GetAsync((RadioBand)99);

    Assert.Null(frequency);
  }

  [Fact]
  public async Task Set_ThenGet_RoundTripsInHertz()
  {
    // The unit guard. RadioPreferences.LastFrequency's doc comment says "MHz (for FM) or kHz (for
    // AM)" while the code writing it stores hertz; this test is what stops that repeating here.
    var service = CreateService();

    await service.SetAsync(RadioBand.AM, Frequency.FromKilohertz(1130));
    var frequency = await service.GetAsync(RadioBand.AM);

    Assert.Equal(1_130_000, frequency!.Value.Hertz);
    Assert.Equal("1130000", _store.Entries["RadioBandMemory:LastFrequencyHzByBand:AM"]);
    Assert.True(_store.SaveCalls > 0);
  }

  [Fact]
  public async Task Set_IgnoresNonPositiveFrequency()
  {
    var service = CreateService();

    await service.SetAsync(RadioBand.FM, new Frequency(0));

    Assert.Empty(_store.Entries);
  }
}
