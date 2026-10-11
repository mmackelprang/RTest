using RTLSDRCore.Enums;
using RTLSDRCore.Sweep;
using RTLSDRCore.Tests.Sweep;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace RTLSDRCore.Tests;

/// <summary>
/// AUD-99: the receiver puts the device in HF direct sampling for AM and Shortwave, and in tuner
/// mode for every other band, with streaming stopped and before the centre frequency is set; and
/// it reports a refused switch, sample rate or tune instead of swallowing it. Every assertion reads
/// the fake device's recorded call order; nothing is timed.
/// </summary>
public class RadioReceiverHfModeTests
{
  private const long FmHz = 101_100_000;
  private const long AmHz = 1_000_000;
  private const long SwHz = 6_000_000;

  private static RadioReceiver StartOn(FakeSdrDevice device, BandType band, long frequencyHz, ILogger? logger = null)
  {
    RadioReceiver receiver = new(device, logger);
    Assert.True(receiver.SetBand(band, frequencyHz));   // device closed: recorded for the start
    Assert.True(receiver.Startup());
    return receiver;
  }

  /// <summary>The calls made after <paramref name="from"/> calls had been recorded.</summary>
  private static List<string> CallsSince(FakeSdrDevice device, int from) => device.Calls.Skip(from).ToList();

  private static void AssertBefore(List<string> calls, string first, string second)
  {
    int a = calls.IndexOf(first);
    int b = calls.IndexOf(second);
    Assert.True(a >= 0, $"'{first}' was not called; calls: {string.Join(", ", calls)}");
    Assert.True(b >= 0, $"'{second}' was not called; calls: {string.Join(", ", calls)}");
    Assert.True(a < b, $"'{first}' came after '{second}'; calls: {string.Join(", ", calls)}");
  }

  [Fact]
  public void Startup_OnAm_SwitchesHfOn_BeforeTheSampleRateAndTheFrequency()
  {
    FakeSdrDevice device = new();

    using RadioReceiver receiver = StartOn(device, BandType.AM, AmHz);

    List<string> calls = device.Calls.ToList();
    AssertBefore(calls, "Open", "SetHfMode:True");
    AssertBefore(calls, "SetHfMode:True", "SetSampleRate:240000");
    AssertBefore(calls, "SetHfMode:True", $"SetFrequency:{AmHz}");
    AssertBefore(calls, $"SetFrequency:{AmHz}", "StartStreaming");
    Assert.True(receiver.IsHfMode);
  }

  [Fact]
  public void Startup_OnFm_MakesNoHfSwitch()
  {
    FakeSdrDevice device = new();

    using RadioReceiver receiver = StartOn(device, BandType.FM, FmHz);

    Assert.DoesNotContain(device.Calls, c => c.StartsWith("SetHfMode", StringComparison.Ordinal));
    Assert.False(receiver.IsHfMode);
  }

  [Theory]
  [InlineData(BandType.AM, AmHz)]
  [InlineData(BandType.Shortwave, SwHz)]
  public void SetBand_ToAnHfBand_StopsStreaming_SwitchesHfOn_ThenTunes_ThenResumes(BandType band, long frequencyHz)
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.FM, FmHz);
    int before = device.Calls.Count;

    Assert.True(receiver.SetBand(band, frequencyHz));

    List<string> calls = CallsSince(device, before);
    AssertBefore(calls, "StopStreaming", "SetHfMode:True");
    AssertBefore(calls, "SetHfMode:True", "SetSampleRate:240000");
    AssertBefore(calls, "SetHfMode:True", $"SetFrequency:{frequencyHz}");
    AssertBefore(calls, $"SetFrequency:{frequencyHz}", "StartStreaming");
    Assert.True(receiver.IsHfMode);
    Assert.Equal(band, receiver.CurrentBand.Type);
  }

  [Theory]
  [InlineData(BandType.FM, FmHz)]
  [InlineData(BandType.Aircraft, 120_000_000)]
  [InlineData(BandType.Weather, 162_400_000)]
  [InlineData(BandType.VHF, 146_520_000)]
  public void SetBand_FromAm_ToAnyOtherBand_SwitchesHfOff_BeforeTheFrequency(BandType band, long frequencyHz)
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.AM, AmHz);
    int before = device.Calls.Count;

    Assert.True(receiver.SetBand(band, frequencyHz));

    List<string> calls = CallsSince(device, before);
    AssertBefore(calls, "StopStreaming", "SetHfMode:False");
    AssertBefore(calls, "SetHfMode:False", $"SetFrequency:{frequencyHz}");
    Assert.False(receiver.IsHfMode);
  }

  [Theory]
  [InlineData(BandType.Aircraft, 120_000_000)]
  [InlineData(BandType.Weather, 162_400_000)]
  [InlineData(BandType.VHF, 146_520_000)]
  public void SetBand_BetweenTunerBands_NeverSwitchesHfOn(BandType band, long frequencyHz)
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.FM, FmHz);

    Assert.True(receiver.SetBand(band, frequencyHz));

    Assert.DoesNotContain("SetHfMode:True", device.Calls);
    Assert.False(receiver.IsHfMode);
  }

  [Fact]
  public void FmToAmToFm_RoundTrip_LeavesTheDeviceInTheRightModeAtEachStep()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.FM, FmHz);
    Assert.False(receiver.IsHfMode);

    Assert.True(receiver.SetBand(BandType.AM, AmHz));
    Assert.True(receiver.IsHfMode);
    Assert.Equal(AmHz, device.GetFrequency());

    Assert.True(receiver.SetBand(BandType.FM, FmHz));
    Assert.False(receiver.IsHfMode);
    Assert.Equal(FmHz, device.GetFrequency());

    string[] switches = device.Calls.Where(c => c.StartsWith("SetHfMode", StringComparison.Ordinal)).ToArray();
    Assert.Equal(new[] { "SetHfMode:True", "SetHfMode:False" }, switches);
  }

  [Fact]
  public void SetFrequency_IntoTheShortwaveBand_SwitchesHfOnBeforeTuning()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.FM, FmHz);
    int before = device.Calls.Count;

    Assert.Equal(TuneResult.Success, receiver.TrySetFrequency(SwHz));

    List<string> calls = CallsSince(device, before);
    AssertBefore(calls, "SetHfMode:True", $"SetFrequency:{SwHz}");
    Assert.Equal(BandType.Shortwave, receiver.CurrentBand.Type);
    Assert.True(receiver.IsHfMode);
  }

  [Fact]
  public void TuneWithinAm_StaysInHfMode_WithNoFurtherSwitch()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.AM, AmHz);
    int before = device.Calls.Count;

    Assert.True(receiver.TuneFrequencyUp(10_000));
    Assert.True(receiver.SetFrequencyInBand(1_500_000));

    Assert.DoesNotContain(CallsSince(device, before), c => c.StartsWith("SetHfMode", StringComparison.Ordinal));
    Assert.True(receiver.IsHfMode);
  }

  [Fact]
  public void RefusedTune_IsReported_NotSwallowed()
  {
    FakeSdrDevice device = new() { SetFrequencyResult = hz => hz != 99_500_000 };
    using RadioReceiver receiver = StartOn(device, BandType.FM, FmHz);

    Assert.False(receiver.SetFrequencyInBand(99_500_000));
    Assert.Equal(TuneResult.Refused, receiver.TrySetFrequency(99_500_000));
    Assert.False(receiver.SetFrequency(99_500_000));
    Assert.False(receiver.SetBand(BandType.FM, 99_500_000));

    // The receiver keeps running, and a tune the device accepts still works.
    Assert.True(receiver.IsRunning);
    Assert.Equal(TuneResult.Success, receiver.TrySetFrequency(98_100_000));
  }

  [Fact]
  public void FrequencyOutsideEveryBandAndTheDevice_IsOutOfRange_NotRefused()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.FM, FmHz);
    int before = device.Calls.Count;

    Assert.Equal(TuneResult.OutOfRange, receiver.TrySetFrequency(2_000_000_000));
    // Below the AM band: a custom band would need the tuner, whose floor is 24 MHz.
    Assert.Equal(TuneResult.OutOfRange, receiver.TrySetFrequency(300_000));

    Assert.Empty(CallsSince(device, before));
  }

  [Fact]
  public void RefusedHfSwitch_FailsTheBandChange_AndLeavesTheReceiverOnItsBandAndStreaming()
  {
    FakeSdrDevice device = new() { HfSwitchResult = false };
    using RadioReceiver receiver = StartOn(device, BandType.FM, FmHz);
    int before = device.Calls.Count;

    Assert.False(receiver.SetBand(BandType.AM, AmHz));

    Assert.Equal(BandType.FM, receiver.CurrentBand.Type);
    Assert.Equal(FmHz, receiver.CurrentFrequency);
    Assert.False(receiver.IsHfMode);
    List<string> calls = CallsSince(device, before);
    Assert.DoesNotContain($"SetFrequency:{AmHz}", calls);
    Assert.Equal("StartStreaming", calls.Last());
    Assert.True(receiver.IsRunning);
  }

  [Fact]
  public void DeviceWithoutHf_RefusesAm_WithoutTuningBelowTheTunerFloor()
  {
    FakeSdrDevice device = new() { SupportsHfDirectSampling = false };
    using RadioReceiver receiver = StartOn(device, BandType.FM, FmHz);

    Assert.False(receiver.SetBand(BandType.AM, AmHz));
    Assert.Equal(TuneResult.Refused, receiver.TrySetFrequency(AmHz));

    Assert.DoesNotContain(device.SetFrequencyCalls, hz => hz < 24_000_000);
    Assert.Equal(BandType.FM, receiver.CurrentBand.Type);
  }

  [Fact]
  public void Startup_FailsAndClosesTheDevice_WhenTheTuneIsRefused()
  {
    FakeSdrDevice device = new() { SetFrequencyResult = _ => false };
    using RadioReceiver receiver = new(device);

    Assert.False(receiver.Startup());

    Assert.False(device.IsOpen);
    Assert.Equal(ReceiverState.Error, receiver.GetRadioState().State);
    Assert.DoesNotContain("StartStreaming", device.Calls);
  }

  [Fact]
  public void Startup_FailsAndClosesTheDevice_WhenTheSampleRateIsRefused()
  {
    FakeSdrDevice device = new() { SampleRateResult = false };
    using RadioReceiver receiver = new(device);

    Assert.False(receiver.Startup());

    Assert.False(device.IsOpen);
    Assert.DoesNotContain(device.Calls, c => c.StartsWith("SetFrequency", StringComparison.Ordinal));
  }

  [Fact]
  public void Startup_OnAm_FailsAndClosesTheDevice_WhenHfIsRefused()
  {
    FakeSdrDevice device = new() { SupportsHfDirectSampling = false };
    using RadioReceiver receiver = new(device);
    Assert.True(receiver.SetBand(BandType.AM, AmHz));

    Assert.False(receiver.Startup());

    Assert.False(device.IsOpen);
    Assert.Empty(device.SetFrequencyCalls);
  }

  [Fact]
  public void LiveSweep_OfATunerBand_WhileOnAm_IsRefused_WithoutTouchingTheDevice()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.AM, AmHz);
    BandSweepPlan fm = BandSweepPlans.For(BandType.FM, null)!;
    int before = device.Calls.Count;

    Assert.NotNull(receiver.LiveSweepModeConflict(fm));
    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
      receiver.SweepPlan(fm, 28f, null, CancellationToken.None));
    Assert.Throws<InvalidOperationException>(() =>
      receiver.SweepChannels(new long[] { 98_100_000 }, 28f, null, CancellationToken.None));

    Assert.Contains("HF direct sampling", ex.Message);
    Assert.Empty(CallsSince(device, before));
    Assert.True(receiver.IsHfMode);
    Assert.False(receiver.IsSweeping);
  }

  [Fact]
  public void LiveSweep_OfAnHfBand_WhileOnFm_IsRefused()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.FM, FmHz);
    BandSweepPlan am = HfDirectSamplingTests.AmPlan();
    int before = device.Calls.Count;

    Assert.NotNull(receiver.LiveSweepModeConflict(am));
    Assert.Throws<InvalidOperationException>(() => receiver.SweepPlan(am, 28f, null, CancellationToken.None));

    Assert.Empty(CallsSince(device, before));
    Assert.False(receiver.IsHfMode);
  }

  [Fact]
  public void LiveSweep_InTheRadiosOwnMode_RunsWithNoModeSwitch()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.AM, AmHz);
    receiver.SweepAwaitingSamples = device.RaiseBlock;
    BandSweepPlan am = HfDirectSamplingTests.AmPlan();
    int before = device.Calls.Count;

    Assert.Null(receiver.LiveSweepModeConflict(am));
    IReadOnlyList<ChannelLevel> levels = receiver.SweepPlan(am, 28f, null, CancellationToken.None);

    Assert.Equal(am.Channels, levels.Select(l => l.FrequencyHz));
    Assert.DoesNotContain(CallsSince(device, before), c => c.StartsWith("SetHfMode", StringComparison.Ordinal));
    Assert.True(receiver.IsHfMode);
    Assert.Equal(AmHz, device.GetFrequency());   // back on the station
  }

  [Fact]
  public void HfMode_TunesLogNoWarningOrError()
  {
    Collector sink = new();
    ILogger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.FM, FmHz, logger);

    Assert.True(receiver.SetBand(BandType.AM, AmHz));
    for (int i = 0; i < 5; i++)
    {
      Assert.True(receiver.TuneFrequencyUp(10_000));
    }
    Assert.True(receiver.SetBand(BandType.Shortwave, SwHz));
    receiver.AutoGainEnabled = false;
    receiver.Gain = 20f;

    Assert.DoesNotContain(sink.Events, e => e.Level >= LogEventLevel.Warning);
  }

  private sealed class Collector : ILogEventSink
  {
    private readonly List<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Events
    {
      get
      {
        lock (_events)
        {
          return _events.ToArray();
        }
      }
    }

    public void Emit(LogEvent logEvent)
    {
      lock (_events)
      {
        _events.Add(logEvent);
      }
    }
  }
}
