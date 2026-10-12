using RTLSDRCore.Bands;
using RTLSDRCore.Enums;
using RTLSDRCore.Sweep;

namespace RTLSDRCore.Tests.Sweep;

/// <summary>
/// AUD-103: the real AM and SW plans swept on both device paths. The idle path
/// (<see cref="DeviceSweepTuner"/>, opened with <see cref="HfBands.Requires(BandSweepPlan)"/> as
/// <c>BandMapService.RunIdle</c> opens it) switches HF direct sampling on before anything is tuned
/// for AM and SW and never for FM; the live path sweeps AM and SW only while the radio is in HF mode
/// and switches nothing. Every assertion reads the fake device's recorded calls; nothing is timed.
/// </summary>
public class HfBandSweepTests
{
  private const long FmHz = 101_100_000;
  private const long AmHz = 1_000_000;
  private const long SwHz = 9_600_000;

  private static RadioReceiver StartOn(FakeSdrDevice device, BandType band, long frequencyHz)
  {
    RadioReceiver receiver = new(device);
    Assert.True(receiver.SetBand(band, frequencyHz));
    Assert.True(receiver.Startup());
    return receiver;
  }

  private static List<string> CallsSince(FakeSdrDevice device, int from) => device.Calls.Skip(from).ToList();

  public static TheoryData<BandType, long?> HfPlans => new()
  {
    { BandType.AM, null },
    { BandType.Shortwave, SwHz },
  };

  [Theory]
  [MemberData(nameof(HfPlans))]
  public void IdleSweep_OfAnAmOrSwPlan_SwitchesHfOnBeforeTheSampleRateAndEveryTune(BandType band, long? currentHz)
  {
    BandSweepPlan plan = BandSweepPlans.For(band, currentHz)!;
    FakeSdrDevice device = new();

    IReadOnlyList<ChannelLevel> levels;
    using (DeviceSweepTuner tuner = new(device, 28f, HfBands.Requires(plan)))
    {
      tuner.Open();
      levels = BandSweeper.Sweep(tuner, plan, ChannelPowerMeter.FftSize, null, CancellationToken.None);
    }

    List<string> calls = device.Calls.ToList();
    int hf = calls.IndexOf("SetHfMode:True");
    Assert.True(hf >= 0, "SetHfMode(true) was not called");
    Assert.True(hf < calls.IndexOf($"SetSampleRate:{DeviceSweepTuner.SweepSampleRate}"));
    Assert.True(hf < calls.FindIndex(c => c.StartsWith("SetFrequency:", StringComparison.Ordinal)));
    // The idle path tunes each plan centre, unshifted (AUD-101's offset is the radio's, not the sweep's).
    Assert.Equal(plan.Tunes.Select(t => t.CentreHz), device.SetFrequencyCalls);
    Assert.Equal(plan.Channels, levels.Select(l => l.FrequencyHz));
    // Gain is set once, at open, not per tune (in HF mode the real device skips it at Debug; AUD-99).
    Assert.Single(calls, c => c.StartsWith("SetGain:", StringComparison.Ordinal));
  }

  [Fact]
  public void IdleSweep_OfTheFmPlan_NeverSwitchesHf()
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.FM, null)!;
    FakeSdrDevice device = new();

    using (DeviceSweepTuner tuner = new(device, 28f, HfBands.Requires(plan)))
    {
      tuner.Open();
      BandSweeper.Sweep(tuner, plan, ChannelPowerMeter.FftSize, null, CancellationToken.None);
    }

    Assert.False(HfBands.Requires(plan));
    Assert.DoesNotContain(device.Calls, c => c.StartsWith("SetHfMode", StringComparison.Ordinal));
    Assert.Equal(FmChannelPlan.Channels, device.SetFrequencyCalls);
  }

  [Theory]
  [InlineData(BandType.AM, AmHz, null)]
  [InlineData(BandType.Shortwave, SwHz, SwHz)]
  public void LiveSweep_OfTheRadiosOwnHfBand_HopsEveryTuneCentre_AndSwitchesNothing(BandType band, long radioHz, long? planHz)
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, band, radioHz);
    receiver.SweepAwaitingSamples = device.RaiseBlock;
    BandSweepPlan plan = BandSweepPlans.For(band, planHz)!;
    int before = device.Calls.Count;

    Assert.Null(receiver.LiveSweepModeConflict(plan));
    IReadOnlyList<ChannelLevel> levels = receiver.SweepPlan(plan, 28f, null, CancellationToken.None);

    List<string> calls = CallsSince(device, before);
    Assert.Equal(plan.Channels, levels.Select(l => l.FrequencyHz));
    Assert.DoesNotContain(calls, c => c.StartsWith("SetHfMode", StringComparison.Ordinal));
    Assert.True(receiver.IsHfMode);
    List<long> tunes = calls.Where(c => c.StartsWith("SetFrequency:", StringComparison.Ordinal))
      .Select(c => long.Parse(c["SetFrequency:".Length..], System.Globalization.CultureInfo.InvariantCulture))
      .ToList();
    // Every plan centre, in order, unshifted; then the retune to the radio's own frequency, which on
    // AM and SW is offset 60 kHz below it (AUD-101), written out rather than read back from the device.
    Assert.Equal(plan.Tunes.Select(t => t.CentreHz).Append(radioHz - 60_000), tunes);
    // The sweep's manual gain is set once before the hops, not per tune.
    Assert.Single(calls.TakeWhile(c => !c.StartsWith("SetFrequency:", StringComparison.Ordinal)),
      c => c.StartsWith("SetGain:", StringComparison.Ordinal));
    int firstHop = calls.FindIndex(c => c.StartsWith("SetFrequency:", StringComparison.Ordinal));
    int lastHop = calls.FindLastIndex(c => c == $"SetFrequency:{plan.Tunes[^1].CentreHz}");
    Assert.DoesNotContain(calls.Skip(firstHop).Take(lastHop - firstHop + 1),
      c => c.StartsWith("SetGain", StringComparison.Ordinal));
  }

  [Theory]
  [InlineData(BandType.AM, null)]
  [InlineData(BandType.Shortwave, SwHz)]
  public void LiveSweep_OfAnAmOrSwPlan_WhileTheRadioPlaysFm_IsRefused_WithoutTouchingTheDevice(BandType band, long? planHz)
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.FM, FmHz);
    BandSweepPlan plan = BandSweepPlans.For(band, planHz)!;
    int before = device.Calls.Count;

    Assert.NotNull(receiver.LiveSweepModeConflict(plan));
    Assert.Throws<InvalidOperationException>(() => receiver.SweepPlan(plan, 28f, null, CancellationToken.None));

    Assert.Empty(CallsSince(device, before));
    Assert.False(receiver.IsHfMode);
    Assert.False(receiver.IsSweeping);
  }

  [Fact]
  public void LiveSweep_OfTheFmPlan_WhileTheRadioPlaysSw_IsRefused()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.Shortwave, SwHz);
    BandSweepPlan fm = BandSweepPlans.For(BandType.FM, null)!;
    int before = device.Calls.Count;

    Assert.NotNull(receiver.LiveSweepModeConflict(fm));
    Assert.Throws<InvalidOperationException>(() => receiver.SweepPlan(fm, 28f, null, CancellationToken.None));

    Assert.Empty(CallsSince(device, before));
    Assert.True(receiver.IsHfMode);
  }
}
