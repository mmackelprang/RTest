using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Exceptions;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Factories;
using Radio.Infrastructure.Audio.Sources.Primary;
using RTLSDRCore;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using RTLSDRCore.Sweep;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// AUD-99: a tune the hardware refuses reaches callers as <see cref="TunerRefusedException"/>, not
/// as the <see cref="ArgumentOutOfRangeException"/> an out-of-range frequency gets; the live-sweep
/// HF-mode refusal is the receiver's; and <c>RTLSDR:HfDirectSampling</c> is read as documented.
/// </summary>
public class SDRRadioAudioSourceHfTests
{
  private readonly Mock<ISdrDevice> _device = new();

  public SDRRadioAudioSourceHfTests()
  {
    _device.Setup(d => d.DeviceInfo).Returns(new RTLSDRCore.Models.DeviceInfo
    {
      Name = "Mock RTL-SDR Device",
      Product = "Generic",
      Type = DeviceType.Mock,
      IsAvailable = true,
      MinFrequencyHz = 24_000_000,
      MaxFrequencyHz = 1_766_000_000,
    });
    _device.Setup(d => d.IsOpen).Returns(true);
  }

  private static SDRRadioAudioSource CreateSource(RadioReceiver receiver)
  {
    Mock<IOptionsMonitor<RadioOptions>> options = new();
    options.Setup(o => o.CurrentValue).Returns(new RadioOptions());
    return new SDRRadioAudioSource(NullLogger<SDRRadioAudioSource>.Instance, receiver, options.Object);
  }

  [Fact]
  public async Task SetFrequency_RefusedByTheDevice_ThrowsTunerRefused_NotArgumentOutOfRange()
  {
    _device.Setup(d => d.SetFrequency(It.IsAny<long>())).Returns(false);
    await using SDRRadioAudioSource source = CreateSource(new RadioReceiver(_device.Object));

    TunerRefusedException ex = await Assert.ThrowsAsync<TunerRefusedException>(() =>
      source.SetFrequencyAsync(new Frequency(99_500_000)));

    Assert.Contains("refused", ex.Message);
  }

  [Fact]
  public async Task SetFrequency_OutsideEveryBandAndTheDevice_StillThrowsArgumentOutOfRange()
  {
    _device.Setup(d => d.SetFrequency(It.IsAny<long>())).Returns(true);
    await using SDRRadioAudioSource source = CreateSource(new RadioReceiver(_device.Object));

    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
      source.SetFrequencyAsync(new Frequency(2_000_000_000)));

    _device.Verify(d => d.SetFrequency(It.IsAny<long>()), Times.Never);
  }

  [Fact]
  public async Task SetBand_ToAm_OnADeviceWithoutHf_ThrowsTunerCapability_AndStaysOnFm()
  {
    _device.Setup(d => d.SetFrequency(It.IsAny<long>())).Returns(true);
    _device.Setup(d => d.SupportsHfDirectSampling).Returns(false);
    RadioReceiver receiver = new(_device.Object);
    await using SDRRadioAudioSource source = CreateSource(receiver);

    // A capability gap (409 at the API), still a TunerRefusedException for callers that catch any refusal.
    TunerCapabilityException ex = await Assert.ThrowsAsync<TunerCapabilityException>(() => source.SetBandAsync(RadioBand.AM));
    Assert.IsAssignableFrom<TunerRefusedException>(ex);
    Assert.Contains("HF direct sampling", ex.Message);
    await Assert.ThrowsAsync<TunerCapabilityException>(() => source.SetFrequencyAsync(new Frequency(1_000_000)));
    await Assert.ThrowsAsync<TunerCapabilityException>(() => source.TuneInBandAsync(RadioBand.SW, new Frequency(6_000_000)));

    Assert.Equal(RadioBand.FM, source.CurrentBand);
    _device.Verify(d => d.SetHfMode(true), Times.Never);
    _device.Verify(d => d.SetFrequency(It.Is<long>(hz => hz < 24_000_000)), Times.Never);
  }

  [Fact]
  public async Task SetBand_ToAm_WhenTheHfSwitchIsRefused_ThrowsTunerRefused()
  {
    _device.Setup(d => d.SetFrequency(It.IsAny<long>())).Returns(true);
    _device.Setup(d => d.SupportsHfDirectSampling).Returns(true);
    _device.Setup(d => d.SetHfMode(true)).Returns(false);
    await using SDRRadioAudioSource source = CreateSource(new RadioReceiver(_device.Object));

    await Assert.ThrowsAsync<TunerRefusedException>(() => source.SetBandAsync(RadioBand.AM));

    _device.Verify(d => d.SetHfMode(true), Times.Once);
    Assert.Equal(RadioBand.FM, source.CurrentBand);
  }

  [Fact]
  public async Task LiveSweepRefusal_IsTheReceiversModeCheck()
  {
    // The mock device has HF (Q branch) by default.
    RadioReceiver receiver = new(new MockSdrDevice());
    Assert.True(receiver.SetBand(BandType.AM, 1_000_000));
    Assert.True(receiver.Startup());
    await using SDRRadioAudioSource source = CreateSource(receiver);
    BandSweepPlan fm = BandSweepPlans.For(BandType.FM, null)!;
    BandSweepPlan am = BandSweepPlans.CreateNarrowband("AM", 530_000, 4, 10_000, 525_000, 565_000);

    Assert.True(receiver.IsHfMode);
    Assert.NotNull(source.LiveSweepRefusal(fm));
    Assert.Null(source.LiveSweepRefusal(am));
  }

  [Fact]
  public async Task LiveSeek_OnAm_StepsPastThe960KhzSpur_AndStopsEverywhereElse()
  {
    // AUD-103 review M1: the radio source gives the receiver the band map's spur table, so a live seek
    // on AM never stops on 960 kHz however strong it reads. Threshold 0: every other step is a stop.
    RadioReceiver receiver = new(new MockSdrDevice());
    Assert.True(receiver.SetBand(BandType.AM, 940_000));
    Assert.True(receiver.Startup());
    receiver.ScanPauseMs = 0;
    await using SDRRadioAudioSource source = CreateSource(receiver);
    List<long> tunes = new();
    List<long> found = new();
    receiver.FrequencyChanged += (_, e) => tunes.Add(e.NewFrequency);
    receiver.SeekStationFound += (_, e) => found.Add(e.FrequencyHz);

    Task scan = Task.Run(() => receiver.ScanFrequencyUp(stepHz: 10_000, signalThreshold: 0f, dwellTimeMs: 0));
    // A fail-safe, not an assertion: the scan ends by going round.
    if (await Task.WhenAny(scan, Task.Delay(TimeSpan.FromSeconds(30))) != scan)
    {
      receiver.CancelScan();
      Assert.Fail("The scan did not end within the fail-safe.");
    }
    await scan;

    Assert.Contains(960_000, tunes);
    Assert.DoesNotContain(960_000, found);
    Assert.Contains(1_030_000, found);
    Assert.Equal(tunes.Where(hz => hz != 960_000), found);
    receiver.Shutdown();
  }

  [Theory]
  [InlineData(null, HfDirectSamplingSetting.Auto)]
  [InlineData("Auto", HfDirectSamplingSetting.Auto)]
  [InlineData("Q", HfDirectSamplingSetting.Q)]
  [InlineData("i", HfDirectSamplingSetting.I)]
  [InlineData("Off", HfDirectSamplingSetting.Off)]
  public void HfSetting_IsReadFromRtlsdrHfDirectSampling(string? value, HfDirectSamplingSetting expected)
  {
    Dictionary<string, string?> values = new();
    if (value != null)
    {
      values["RTLSDR:HfDirectSampling"] = value;
    }
    IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    Mock<ILogger> logger = new();

    Assert.Equal(expected, SdrHfConfiguration.Read(configuration, logger.Object));
    logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
      It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
  }

  [Fact]
  public void HfSetting_Invalid_IsAuto_WithOneWarning()
  {
    IConfiguration configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { ["RTLSDR:HfDirectSampling"] = "Yes please" })
      .Build();
    Mock<ILogger> logger = new();

    Assert.Equal(HfDirectSamplingSetting.Auto, SdrHfConfiguration.Read(configuration, logger.Object));
    logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
      It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
  }

  [Fact]
  public void IdleSweepDeviceFactory_OpensWithTheConfiguredSetting_ReadOnEveryCall()
  {
    // AUD-103 review L5: the idle band sweep's device gets RTLSDR:HfDirectSampling, not Auto, and a
    // changed value applies to the next open.
    IConfiguration configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { ["RTLSDR:HfDirectSampling"] = "Off" })
      .Build();
    List<HfDirectSamplingSetting> opened = new();
    Func<ISdrDevice?> factory = SdrHfConfiguration.IdleSweepDeviceFactory(
      () => configuration, NullLogger.Instance, setting => { opened.Add(setting); return _device.Object; });

    Assert.Same(_device.Object, factory());
    configuration["RTLSDR:HfDirectSampling"] = "Q";
    factory();

    Assert.Equal(new[] { HfDirectSamplingSetting.Off, HfDirectSamplingSetting.Q }, opened);
  }

  [Fact]
  public void ShippedAppsettings_DefaultsTheKeyToAuto()
  {
    string path = Path.Combine(FindRepoRoot(), "src", "Radio.API", "appsettings.json");
    using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

    Assert.Equal("Auto", json.RootElement.GetProperty("RTLSDR").GetProperty("HfDirectSampling").GetString());
  }

  private static string FindRepoRoot()
  {
    DirectoryInfo? dir = new(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RadioConsole.sln")))
    {
      dir = dir.Parent;
    }
    return dir?.FullName ?? throw new InvalidOperationException("RadioConsole.sln not found above the test binaries");
  }
}
