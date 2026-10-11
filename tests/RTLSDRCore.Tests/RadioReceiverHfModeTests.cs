using RTLSDRCore.Bands;
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
    // The real tune is the last one; the first is the pre-tune made while HF is still on.
    Assert.True(calls.IndexOf("SetHfMode:False") < calls.LastIndexOf($"SetFrequency:{frequencyHz}"),
      $"no tune after HF off; calls: {string.Join(", ", calls)}");
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
    Assert.Equal(TuneResult.Unsupported, receiver.TrySetBand(BandType.AM, AmHz));
    Assert.Equal(TuneResult.Unsupported, receiver.TrySetFrequency(AmHz));

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
    FakeSdrDevice device = new() { HfSwitchResult = false };
    using RadioReceiver receiver = new(device);
    Assert.True(receiver.SetBand(BandType.AM, AmHz));

    Assert.False(receiver.Startup());

    Assert.False(device.IsOpen);
    Assert.Empty(device.SetFrequencyCalls);
  }

  // ---- Pre-merge fixes ----

  /// <summary>H1/M1/L3: the calls of a switch from AM (manual gain 20 dB when <paramref name="autoGain"/> is false), in order.</summary>
  private static List<string> SwitchFromAmWithGain(BandType band, long frequencyHz, bool autoGain, out RadioReceiver receiver, out FakeSdrDevice device)
  {
    device = new FakeSdrDevice();
    receiver = StartOn(device, BandType.AM, AmHz);
    receiver.AutoGainEnabled = autoGain;
    receiver.Gain = 20f;
    int before = device.Calls.Count;

    Assert.Equal(TuneResult.Success, band == BandType.Custom
      ? receiver.TrySetFrequency(frequencyHz)
      : receiver.TrySetBand(band, frequencyHz));

    return CallsSince(device, before);
  }

  [Fact]
  public void SetBand_FromAmToFm_ReappliesManualGain_AfterHfOff()
  {
    List<string> calls = SwitchFromAmWithGain(BandType.FM, FmHz, autoGain: false, out RadioReceiver receiver, out _);
    using (receiver)
    {
      AssertBefore(calls, "SetHfMode:False", "SetGainMode:False");
      AssertBefore(calls, "SetHfMode:False", "SetGain:20");
      AssertBefore(calls, "SetGain:20", "StartStreaming");
    }
  }

  [Fact]
  public void SetBand_FromAmToFm_ReappliesAutoGain_AfterHfOff()
  {
    List<string> calls = SwitchFromAmWithGain(BandType.FM, FmHz, autoGain: true, out RadioReceiver receiver, out _);
    using (receiver)
    {
      AssertBefore(calls, "SetHfMode:False", "SetGainMode:True");
      Assert.DoesNotContain(calls, c => c.StartsWith("SetGain:", StringComparison.Ordinal));
    }
  }

  [Fact]
  public void SetBand_FromAmToFm_PreTunesTheNewFrequency_WhileHfIsStillOn()
  {
    List<string> calls = SwitchFromAmWithGain(BandType.FM, FmHz, autoGain: true, out RadioReceiver receiver, out FakeSdrDevice device);
    using (receiver)
    {
      // librtlsdr retunes to its last frequency when HF goes off: that must be the new one.
      int preTune = calls.IndexOf($"SetFrequency:{FmHz}");
      int hfOff = calls.IndexOf("SetHfMode:False");
      int tune = calls.LastIndexOf($"SetFrequency:{FmHz}");
      Assert.True(preTune >= 0 && preTune < hfOff && hfOff < tune, string.Join(", ", calls));
      Assert.DoesNotContain(calls, c => c.StartsWith("SetFrequency:", StringComparison.Ordinal) && c != $"SetFrequency:{FmHz}");
      Assert.Equal(FmHz, device.GetFrequency());
    }
  }

  [Fact]
  public void SetBand_FromAmToFm_RefusedPreTune_DoesNotFailTheSwitch()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.AM, AmHz);
    int fmTunes = 0;
    // Refuse only the first FM tune: the pre-tune made while HF is still on.
    device.SetFrequencyResult = hz => hz != FmHz || fmTunes++ > 0;

    Assert.True(receiver.SetBand(BandType.FM, FmHz));

    Assert.Equal(2, fmTunes);
    Assert.False(receiver.IsHfMode);
    Assert.Equal(FmHz, device.GetFrequency());
  }

  [Fact]
  public void CustomBand_FromAm_HfOff_ThenGain_ThenRate_ThenTune_ThenResume()
  {
    const long CustomHz = 433_920_000;
    Assert.Null(BandPresets.FindBandForFrequency(CustomHz));

    List<string> calls = SwitchFromAmWithGain(BandType.Custom, CustomHz, autoGain: false, out RadioReceiver receiver, out _);
    using (receiver)
    {
      Assert.Equal(
        new[]
        {
          "StopStreaming",
          $"SetFrequency:{CustomHz}",   // pre-tune, HF still on (M1)
          "SetHfMode:False",
          "SetGainMode:False",          // H1
          "SetGain:20",
          "SetSampleRate:240000",       // H1: the tuner's init reset the IF filter
          $"SetFrequency:{CustomHz}",
          "StartStreaming",             // L3: after the tune
        },
        calls);
      Assert.False(receiver.IsHfMode);
      Assert.Equal(BandType.Custom, receiver.CurrentBand.Type);
    }
  }

  [Fact]
  public void CustomBand_FromAm_RefusedHfOff_IsRefused_AndResumesStreaming()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.AM, AmHz);
    device.HfSwitchResult = false;
    int before = device.Calls.Count;

    Assert.Equal(TuneResult.Refused, receiver.TrySetFrequency(433_920_000));

    Assert.Equal("StartStreaming", CallsSince(device, before).Last());
    Assert.Equal(BandType.AM, receiver.CurrentBand.Type);
    Assert.True(receiver.IsRunning);
    // The pre-tune moved the device to the custom frequency; after the refusal it is back where the
    // receiver says it is.
    Assert.True(device.IsHfMode);
    Assert.Equal(AmHz, device.GetFrequency());
  }

  [Fact]
  public void SetBand_FromAmToFm_RefusedHfOff_PutsTheDeviceBackOnTheAmFrequency()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.AM, AmHz);
    device.HfSwitchResult = false;
    int before = device.Calls.Count;

    Assert.Equal(TuneResult.Refused, receiver.TrySetBand(BandType.FM, FmHz));

    List<string> calls = CallsSince(device, before);
    AssertBefore(calls, $"SetFrequency:{FmHz}", "SetHfMode:False");
    AssertBefore(calls, "SetHfMode:False", $"SetFrequency:{AmHz}");
    Assert.Equal(BandType.AM, receiver.CurrentBand.Type);
    Assert.True(device.IsHfMode);
    Assert.Equal(AmHz, device.GetFrequency());
    Assert.True(receiver.IsRunning);
  }

  [Fact]
  public void Startup_RetriesARefusedSampleRateOnce()
  {
    FakeSdrDevice device = new() { RefuseNextSampleRates = 1 };
    using RadioReceiver receiver = new(device);

    Assert.True(receiver.Startup());

    Assert.Equal(2, device.Calls.Count(c => c == "SetSampleRate:240000"));
    Assert.True(receiver.IsRunning);
  }

  [Fact]
  public void Startup_RetriesARefusedFirstTuneOnce()
  {
    int tunes = 0;
    FakeSdrDevice device = new() { SetFrequencyResult = _ => tunes++ > 0 };
    using RadioReceiver receiver = new(device);

    Assert.True(receiver.Startup());

    Assert.Equal(2, device.SetFrequencyCalls.Count);
    Assert.True(receiver.IsRunning);
  }

  [Fact]
  public void Startup_TwoRefusedSampleRates_FailTheStart()
  {
    FakeSdrDevice device = new() { RefuseNextSampleRates = 2 };
    using RadioReceiver receiver = new(device);

    Assert.False(receiver.Startup());

    Assert.Equal(2, device.Calls.Count(c => c.StartsWith("SetSampleRate", StringComparison.Ordinal)));
    Assert.False(device.IsOpen);
  }

  [Theory]
  [InlineData(BandType.AM, AmHz)]
  [InlineData(BandType.Shortwave, SwHz)]
  public void Startup_OnAnHfBand_WithoutHf_FallsBackToFm_WithOneWarning(BandType band, long frequencyHz)
  {
    Collector sink = new();
    ILogger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
    FakeSdrDevice device = new() { SupportsHfDirectSampling = false };
    using RadioReceiver receiver = new(device, logger);
    Assert.True(receiver.SetBand(band, frequencyHz));   // device closed: recorded, as a persisted band is

    Assert.True(receiver.Startup());

    Assert.Equal(BandType.FM, receiver.CurrentBand.Type);
    Assert.True(BandPresets.GetBand(BandType.FM).ContainsFrequency(receiver.CurrentFrequency));
    Assert.DoesNotContain("SetHfMode:True", device.Calls);
    Assert.DoesNotContain(device.SetFrequencyCalls, hz => hz < 24_000_000);
    Assert.Single(sink.Events, e => e.Level >= LogEventLevel.Warning);
    Assert.True(receiver.IsRunning);
  }

  /// <summary>S1: the audio of <paramref name="blocks"/> weak noise blocks on <paramref name="band"/>, one event per block.</summary>
  private static List<float[]> AudioFromWeakBlocks(BandType band, long frequencyHz, int blocks, out float signalStrength)
  {
    FakeSdrDevice device = new()
    {
      // Uniform noise of +-0.03 per component: an RMS magnitude of about 0.024, under the 0.1 squelch default.
      BlockFactory = (_, count) => SweepTestSignals.Noise(count, 0.03f),
    };
    using RadioReceiver receiver = StartOn(device, band, frequencyHz);

    using SemaphoreSlim audioEvents = new(0);
    List<float[]> audio = new();
    receiver.AudioDataAvailable += (_, e) =>
    {
      lock (audio)
      {
        audio.Add(e.Samples.AsSpan(0, e.SampleCount).ToArray());
      }
      audioEvents.Release();
    };

    for (int i = 0; i < blocks; i++)
    {
      device.RaiseBlock();
      // Synchronised on the DSP thread's audio event for this block, not on elapsed time; the
      // timeout is only a fail-safe against a hang.
      Assert.True(audioEvents.Wait(TimeSpan.FromSeconds(10)), "DSP thread produced no audio event");
    }

    signalStrength = receiver.SignalStrength;
    lock (audio)
    {
      return audio.ToList();
    }
  }

  [Theory]
  [InlineData(BandType.AM, AmHz)]
  [InlineData(BandType.Shortwave, SwHz)]
  public void HfBand_WeakSignal_IsNotSquelched_ToSilence(BandType band, long frequencyHz)
  {
    List<float[]> audio = AudioFromWeakBlocks(band, frequencyHz, blocks: 2, out float strength);

    Assert.True(strength < 0.1f, $"signal strength {strength} is not under the squelch threshold");
    Assert.Equal(2, audio.Count);
    Assert.All(audio, block => Assert.Contains(block, s => s != 0f));
  }

  [Fact]
  public void Fm_WeakSignal_IsStillSquelched_ToSilence()
  {
    List<float[]> audio = AudioFromWeakBlocks(BandType.FM, FmHz, blocks: 2, out float strength);

    Assert.True(strength < 0.1f, $"signal strength {strength} is not under the squelch threshold");
    Assert.Equal(2, audio.Count);
    Assert.All(audio, block => Assert.All(block, s => Assert.Equal(0f, s)));
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
