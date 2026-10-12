using RTLSDRCore.Bands;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using RTLSDRCore.Models;
using RTLSDRCore.Sweep;
using RTLSDRCore.Tests.Sweep;

namespace RTLSDRCore.Tests;

/// <summary>
/// AUD-99: which dongles get HF direct sampling, which bands use it, and the mock and sweep
/// tuner's share of the contract.
/// </summary>
public class HfDirectSamplingTests
{
  [Theory]
  [InlineData("NESDR SMArt v5", DirectSamplingMode.Q)]
  [InlineData("  nesdr smart V5  ", DirectSamplingMode.Q)]
  [InlineData("RTL2838UHIDIR", DirectSamplingMode.Off)]
  [InlineData("NESDR SMArt", DirectSamplingMode.Off)]
  [InlineData("", DirectSamplingMode.Off)]
  [InlineData(null, DirectSamplingMode.Off)]
  public void Auto_IsQOnlyForAKnownGoodProduct(string? product, DirectSamplingMode expected)
  {
    Assert.Equal(expected, HfDirectSampling.Resolve(HfDirectSamplingSetting.Auto, product));
  }

  [Theory]
  [InlineData(HfDirectSamplingSetting.Q, "RTL2838UHIDIR", DirectSamplingMode.Q)]
  [InlineData(HfDirectSamplingSetting.I, "RTL2838UHIDIR", DirectSamplingMode.I)]
  [InlineData(HfDirectSamplingSetting.I, "NESDR SMArt v5", DirectSamplingMode.I)]
  [InlineData(HfDirectSamplingSetting.Off, "NESDR SMArt v5", DirectSamplingMode.Off)]
  public void ExplicitSettings_IgnoreTheProduct(HfDirectSamplingSetting setting, string product, DirectSamplingMode expected)
  {
    Assert.Equal(expected, HfDirectSampling.Resolve(setting, product));
  }

  [Theory]
  [InlineData("Auto", HfDirectSamplingSetting.Auto)]
  [InlineData("q", HfDirectSamplingSetting.Q)]
  [InlineData(" I ", HfDirectSamplingSetting.I)]
  [InlineData("OFF", HfDirectSamplingSetting.Off)]
  [InlineData(null, HfDirectSamplingSetting.Auto)]
  [InlineData("", HfDirectSamplingSetting.Auto)]
  public void TryParseSetting_AcceptsTheFourNames(string? value, HfDirectSamplingSetting expected)
  {
    Assert.True(HfDirectSampling.TryParseSetting(value, out HfDirectSamplingSetting setting));
    Assert.Equal(expected, setting);
  }

  [Theory]
  [InlineData("2")]
  [InlineData("Q-branch")]
  [InlineData("On")]
  public void TryParseSetting_RejectsAnythingElse_AndReportsAuto(string value)
  {
    Assert.False(HfDirectSampling.TryParseSetting(value, out HfDirectSamplingSetting setting));
    Assert.Equal(HfDirectSamplingSetting.Auto, setting);
  }

  [Theory]
  [InlineData(5, "R820T")]
  [InlineData(6, "R828D")]
  [InlineData(1, "E4000")]
  [InlineData(0, "Unknown")]
  [InlineData(99, "Unknown")]
  public void TunerName_MapsLibrtlsdrsEnum(int value, string expected)
  {
    Assert.Equal(expected, HfDirectSampling.TunerName(value));
  }

  [Theory]
  [InlineData(BandType.AM, true)]
  [InlineData(BandType.Shortwave, true)]
  [InlineData(BandType.FM, false)]
  [InlineData(BandType.Aircraft, false)]
  [InlineData(BandType.Weather, false)]
  [InlineData(BandType.VHF, false)]
  [InlineData(BandType.Custom, false)]
  public void HfBands_AreAmAndShortwaveOnly(BandType band, bool expected)
  {
    Assert.Equal(expected, HfBands.Requires(band));
  }

  [Fact]
  public void HfBands_ReadsAPlansBandCode()
  {
    Assert.False(HfBands.Requires(BandSweepPlans.For(BandType.FM, null)!));
    Assert.False(HfBands.Requires(BandSweepPlans.For(BandType.Weather, null)!));
    Assert.True(HfBands.Requires(AmPlan()));
  }

  [Fact]
  public void MockDevice_ReportsHf_WithA100kHzFloor_AndTheFactoryCopyAgrees()
  {
    using MockSdrDevice device = new();
    DeviceInfo factoryCopy = SdrDeviceFactory.EnumerateDevices().Single(d => d.Type == DeviceType.Mock);

    Assert.True(device.SupportsHfDirectSampling);
    Assert.Equal(DirectSamplingMode.Q, device.DeviceInfo.HfDirectSampling);
    Assert.Equal(HfDirectSampling.HfMinFrequencyHz, device.DeviceInfo.MinFrequencyHz);
    Assert.Equal(device.DeviceInfo.MinFrequencyHz, factoryCopy.MinFrequencyHz);
    Assert.Equal(device.DeviceInfo.HfDirectSampling, factoryCopy.HfDirectSampling);
  }

  [Fact]
  public void MockDevice_FrequencyFloor_FollowsItsHfMode()
  {
    using MockSdrDevice device = new();
    device.Open();

    Assert.False(device.SetFrequency(1_000_000));   // tuner mode: 24 MHz floor
    Assert.True(device.SetHfMode(true));
    Assert.True(device.IsHfMode);
    Assert.True(device.SetFrequency(1_000_000));
    Assert.True(device.SetHfMode(true));             // idempotent
    Assert.True(device.SetHfMode(false));
    Assert.False(device.SetFrequency(1_000_000));
  }

  [Fact]
  public void MockDevice_WithoutHf_RefusesOn_AndAcceptsOff()
  {
    DeviceInfo info = MockSdrDevice.CreateDefaultDeviceInfo();
    info.HfDirectSampling = DirectSamplingMode.Off;
    info.MinFrequencyHz = RtlSdrDevice.TunerMinFrequencyHz;
    using MockSdrDevice device = new(info);
    device.Open();

    Assert.False(device.SupportsHfDirectSampling);
    Assert.False(device.SetHfMode(true));
    Assert.False(device.IsHfMode);
    Assert.True(device.SetHfMode(false));
  }

  [Fact]
  public void MockDevice_IsNeverInHfModeWhileClosed()
  {
    using MockSdrDevice device = new();

    Assert.False(device.SetHfMode(true));
    device.Open();
    Assert.True(device.SetHfMode(true));
    device.Close();

    Assert.False(device.IsHfMode);
  }

  [Fact]
  public void DeviceSweepTuner_HfPlan_SwitchesHfOnBeforeTheSampleRateAndEveryTune()
  {
    FakeSdrDevice device = new();
    using DeviceSweepTuner tuner = new(device, 28f, hfMode: true);

    tuner.Open();
    tuner.Tune(1_000_000);

    List<string> calls = device.Calls.ToList();
    int hf = calls.IndexOf("SetHfMode:True");
    Assert.True(hf >= 0, "SetHfMode(true) was not called");
    Assert.True(hf < calls.IndexOf($"SetSampleRate:{DeviceSweepTuner.SweepSampleRate}"));
    Assert.True(hf < calls.IndexOf("SetFrequency:1000000"));
    Assert.True(device.IsHfMode);
  }

  [Fact]
  public void DeviceSweepTuner_TunerPlan_LeavesAFreshDeviceInTunerMode_WithNoSwitch()
  {
    FakeSdrDevice device = new();
    using DeviceSweepTuner tuner = new(device, 28f, hfMode: false);

    tuner.Open();
    tuner.Tune(98_100_000);

    Assert.DoesNotContain(device.Calls, c => c.StartsWith("SetHfMode", StringComparison.Ordinal));
    Assert.False(device.IsHfMode);
  }

  [Fact]
  public void DeviceSweepTuner_HfRefused_FailsOpen_BeforeAnythingIsTuned()
  {
    FakeSdrDevice device = new() { SupportsHfDirectSampling = false };
    using DeviceSweepTuner tuner = new(device, 28f, hfMode: true);

    Assert.Throws<InvalidOperationException>(tuner.Open);
    Assert.Empty(device.SetFrequencyCalls);
  }

  /// <summary>A four-channel AM plan, built by hand: AUD-103 owns the real AM/SW plans.</summary>
  internal static BandSweepPlan AmPlan() =>
    BandSweepPlans.CreateNarrowband("AM", 530_000, 4, 10_000, 525_000, 565_000);
}
