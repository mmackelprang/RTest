using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.Sources.Primary;
using RTLSDRCore;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// The last station is what a restart with no phone comes back to, so it has to be the last one
/// played. PreferencesPersistenceService saves it every 30 s and only while radio is active, so a
/// station tuned in the seconds before a takeover used to be lost.
/// </summary>
public class SDRRadioAudioSourceLastStationTests
{
  private readonly Mock<ISdrDevice> _device = new();
  private readonly RadioReceiver _receiver;
  private readonly Mock<IConfigurationStore> _store = new();
  private readonly Dictionary<string, string?> _saved = new();
  private readonly SDRRadioAudioSource _source;

  public SDRRadioAudioSourceLastStationTests()
  {
    _device.Setup(d => d.DeviceInfo).Returns(new RTLSDRCore.Models.DeviceInfo
    {
      Name = "Mock RTL-SDR Device",
      Type = DeviceType.Mock,
      IsAvailable = true,
      MinFrequencyHz = 24_000_000,
      MaxFrequencyHz = 1_766_000_000,
    });
    _device.Setup(d => d.IsOpen).Returns(true);
    _device.Setup(d => d.Open()).Returns(true);
    _device.Setup(d => d.SetFrequency(It.IsAny<long>())).Returns(true);
    _device.Setup(d => d.StartStreaming()).Returns(true);
    _receiver = new RadioReceiver(_device.Object);
    _receiver.SetBand(BandType.FM, 101_100_000);

    _store.Setup(s => s.SetEntriesAsync(It.IsAny<IEnumerable<ConfigurationEntry>>(), It.IsAny<CancellationToken>()))
      .Callback((IEnumerable<ConfigurationEntry> entries, CancellationToken _) =>
      {
        foreach (ConfigurationEntry entry in entries)
        {
          _saved[entry.Key] = entry.Value;
        }
      })
      .Returns(Task.CompletedTask);
    _store.Setup(s => s.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

    Mock<IConfigurationManager> configuration = new();
    configuration.Setup(c => c.CurrentStoreType).Returns(ConfigurationStoreType.Sqlite);
    configuration.Setup(c => c.GetStoreAsync("sqlite", It.IsAny<CancellationToken>())).ReturnsAsync(_store.Object);

    Mock<IOptionsMonitor<RadioOptions>> options = new();
    options.Setup(o => o.CurrentValue).Returns(new RadioOptions());
    _source = new SDRRadioAudioSource(
      NullLogger<SDRRadioAudioSource>.Instance, _receiver, options.Object,
      configurationManager: configuration.Object);
  }

  [Fact]
  public async Task Stop_SavesTheStationThatWasPlaying()
  {
    await _source.PlayAsync();
    await _source.TuneInBandAsync(Radio.Core.Models.Audio.RadioBand.FM, new Radio.Core.Models.Audio.Frequency(98_500_000));

    await _source.StopAsync();

    Assert.Equal("98500000", _saved["Radio:LastFrequency"]);
    Assert.Equal("FM", _saved["Radio:LastBand"]);
  }
}
