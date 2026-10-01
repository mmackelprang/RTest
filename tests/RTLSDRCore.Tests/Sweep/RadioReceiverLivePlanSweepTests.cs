using RTLSDRCore.Enums;
using RTLSDRCore.Models;
using RTLSDRCore.Sweep;

namespace RTLSDRCore.Tests.Sweep;

/// <summary>
/// AUD-91: the plan-based live sweep over a running <see cref="RadioReceiver"/>, with the same
/// harness as <see cref="RadioReceiverLiveSweepTests"/>: every IQ block is raised by the test
/// from the receiver's <c>SweepAwaitingSamples</c> seam.
/// </summary>
public class RadioReceiverLivePlanSweepTests
{
  private const long StartHz = 101_100_000;
  private const long WeatherStationHz = 162_475_000;
  private const float SweepGain = 28f;

  private static RadioReceiver StartReceiver(FakeSdrDevice device)
  {
    RadioReceiver receiver = new(device);
    receiver.SetBand(BandType.FM, StartHz);
    Assert.True(receiver.Startup());
    return receiver;
  }

  /// <summary>A tone at the weather station's offset from whatever the device is tuned to.</summary>
  private static IqSample[] WeatherBlock(long tunedHz, int count)
  {
    IqSample[] noise = SweepTestSignals.Noise(count, 0.01f, seed: (int)(tunedHz / 1_000));
    long offset = WeatherStationHz - tunedHz;
    return Math.Abs(offset) < SweepTestSignals.SampleRate / 2
      ? SweepTestSignals.Add(SweepTestSignals.Tone(count, offset, 0.5f), noise)
      : noise;
  }

  [Fact]
  public void GroupedPlan_MeasuresEveryChannel_ThenReturnsToTheStationAndRestoresGain()
  {
    FakeSdrDevice device = new() { BlockFactory = WeatherBlock };
    using RadioReceiver receiver = StartReceiver(device);
    int frequencyChanged = 0;
    receiver.FrequencyChanged += (_, _) => Interlocked.Increment(ref frequencyChanged);
    receiver.SweepAwaitingSamples = device.RaiseBlock;
    BandSweepPlan plan = BandSweepPlans.For(BandType.Weather, null)!;
    int hopsBefore = device.SetFrequencyCalls.Count;
    int callsBefore = device.Calls.Count;
    ModulationType modulationBefore = receiver.CurrentModulation;
    Assert.False(receiver.IsMuted);

    IReadOnlyList<ChannelLevel> levels = receiver.SweepPlan(plan, SweepGain, null, CancellationToken.None);

    // Sweeping the WB plan measures WB channels; it does not move the receiver to WB.
    Assert.Equal(BandType.FM, receiver.CurrentBand.Type);
    Assert.Equal(modulationBefore, receiver.CurrentModulation);

    Assert.Equal(plan.Channels, levels.Select(l => l.FrequencyHz));
    Assert.Equal(WeatherStationHz, levels.MaxBy(l => l.LevelDbfs)!.FrequencyHz);
    Assert.Equal(new long[] { 162_487_500, StartHz }, device.SetFrequencyCalls.Skip(hopsBefore));
    Assert.Equal(StartHz, receiver.CurrentFrequency);
    Assert.Equal(0, frequencyChanged);
    string[] gainCalls = device.Calls.Skip(callsBefore).Where(c => c.StartsWith("SetGain", StringComparison.Ordinal)).ToArray();
    Assert.Equal(new[] { "SetGainMode:False", "SetGain:28", "SetGainMode:True" }, gainCalls);
    Assert.False(receiver.IsSweeping);
    Assert.False(receiver.IsMuted);
  }

  [Fact]
  public void GroupedPlan_UserTuneMidSweep_CancelsAndDeviceEndsOnTheNewFrequency()
  {
    const long userHz = 95_100_000;
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartReceiver(device);
    int waits = 0;
    receiver.SweepAwaitingSamples = () =>
    {
      waits++;
      if (waits == 5)
      {
        Task.Run(() => receiver.SetFrequency(userHz)).GetAwaiter().GetResult();
      }
      device.RaiseBlock();
    };
    BandSweepPlan plan = BandSweepPlans.For(BandType.Aircraft, null)!;

    Assert.ThrowsAny<OperationCanceledException>(() =>
      receiver.SweepPlan(plan, SweepGain, null, CancellationToken.None));

    Assert.Equal(userHz, receiver.CurrentFrequency);
    List<long> tunes = device.SetFrequencyCalls.ToList();
    int userTune = tunes.IndexOf(userHz);
    Assert.True(userTune >= 0);
    Assert.All(tunes.Skip(userTune), hz => Assert.Equal(userHz, hz));
    Assert.False(receiver.IsSweeping);
  }

  [Fact]
  public void Cancelled_ByTheCallersToken_RetunesToTheStation()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartReceiver(device);
    using CancellationTokenSource cts = new();
    int waits = 0;
    receiver.SweepAwaitingSamples = () =>
    {
      if (++waits == 4)
      {
        cts.Cancel();
      }
      device.RaiseBlock();
    };
    BandSweepPlan plan = BandSweepPlans.For(BandType.VHF, 146_520_000)!;

    Assert.ThrowsAny<OperationCanceledException>(() => receiver.SweepPlan(plan, SweepGain, null, cts.Token));

    Assert.Equal(StartHz, device.SetFrequencyCalls[^1]);
    Assert.Equal(StartHz, receiver.CurrentFrequency);
    Assert.False(receiver.IsSweeping);
  }

  [Fact]
  public void PlanDesignedForAHigherRate_ThrowsBeforeTouchingTheDevice()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartReceiver(device);
    BandSweepPlan plan = new(
      "WB", new[] { new SweepTune(162_487_500, new long[] { 162_400_000, 162_425_000 }) },
      25_000, 162_387_500, 162_437_500, 4_000, 1_000, sampleRate: 2_400_000);
    int before = device.Calls.Count;

    Assert.Throws<InvalidOperationException>(() => receiver.SweepPlan(plan, SweepGain, null, CancellationToken.None));

    Assert.Equal(before, device.Calls.Count);
    Assert.False(receiver.IsSweeping);
  }

  [Fact]
  public void FmPlan_HopsLikeTheChannelListSweep()
  {
    long[] channels = { 99_100_000, 99_300_000, FakeSdrDevice.StationHz };
    BandSweepPlan plan = new(
      "FM", channels.Select(hz => new SweepTune(hz, new[] { hz })).ToArray(),
      200_000, 87_500_000, 108_000_000, BandSweepPlans.FmHalfWindowHz, BandSweepPlans.FmDcExcludeHz, 240_000);

    FakeSdrDevice byPlanDevice = new();
    using RadioReceiver byPlan = StartReceiver(byPlanDevice);
    byPlan.SweepAwaitingSamples = byPlanDevice.RaiseBlock;
    int planBefore = byPlanDevice.Calls.Count;
    IReadOnlyList<ChannelLevel> planLevels = byPlan.SweepPlan(plan, SweepGain, null, CancellationToken.None);

    FakeSdrDevice byListDevice = new();
    using RadioReceiver byList = StartReceiver(byListDevice);
    byList.SweepAwaitingSamples = byListDevice.RaiseBlock;
    int listBefore = byListDevice.Calls.Count;
    IReadOnlyList<ChannelLevel> listLevels = byList.SweepChannels(channels, SweepGain, null, CancellationToken.None);

    Assert.Equal(byListDevice.Calls.Skip(listBefore), byPlanDevice.Calls.Skip(planBefore));
    Assert.Equal(listLevels, planLevels);
  }
}
