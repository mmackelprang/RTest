using RTLSDRCore.Enums;
using RTLSDRCore.Models;
using RTLSDRCore.Sweep;

namespace RTLSDRCore.Tests.Sweep;

/// <summary>
/// Live-path sweep over a running <see cref="RadioReceiver"/>. The fake device
/// has no streaming thread: every IQ block is raised by the test from the
/// receiver's <c>SweepAwaitingSamples</c> seam, i.e. exactly when the sweep
/// is waiting for one. Timeouts below only bound a failing test.
/// </summary>
public class RadioReceiverLiveSweepTests
{
  private const long StartHz = 101_100_000;
  private const float SweepGain = 28f;
  private static readonly TimeSpan FailSafe = TimeSpan.FromSeconds(10);

  private static readonly long[] Channels =
  {
    99_100_000, 99_300_000, FakeSdrDevice.StationHz, 99_700_000, 99_900_000,
  };

  private static RadioReceiver StartReceiver(FakeSdrDevice device)
  {
    RadioReceiver receiver = new(device);
    receiver.SetBand(BandType.FM, StartHz);
    Assert.True(receiver.Startup());
    return receiver;
  }

  [Fact]
  public void Sweep_MeasuresEveryChannel_PeakAtStation()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartReceiver(device);
    receiver.SweepAwaitingSamples = device.RaiseBlock;

    IReadOnlyList<ChannelLevel> levels = receiver.SweepChannels(Channels, SweepGain, null, CancellationToken.None);

    Assert.Equal(Channels, levels.Select(l => l.FrequencyHz));
    Assert.Equal(FakeSdrDevice.StationHz, levels.MaxBy(l => l.LevelDbfs)!.FrequencyHz);
    Assert.False(receiver.IsSweeping);
  }

  [Fact]
  public void Sweep_BlocksCapturedBeforeAHop_AreNeverMeasured()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartReceiver(device);
    int hopsSeen = device.SetFrequencyCalls.Count;
    long previousHz = StartHz;
    receiver.SweepAwaitingSamples = () =>
    {
      IReadOnlyList<long> tunes = device.SetFrequencyCalls;
      if (tunes.Count != hopsSeen)
      {
        // First wait after a hop: two blocks still carrying the previous
        // channel arrive now — one read before the retune but raised after
        // the tuner drained its queue, one whose read straddled the retune.
        hopsSeen = tunes.Count;
        device.RaiseBlockCapturedAt(previousHz);
        device.RaiseBlockCapturedAt(previousHz);
        previousHz = tunes[^1];
        return;
      }
      device.RaiseBlock();
    };

    IReadOnlyList<ChannelLevel> levels = receiver.SweepChannels(Channels, SweepGain, null, CancellationToken.None);

    Assert.Equal(Channels, levels.Select(l => l.FrequencyHz));
    float station = levels.Single(l => l.FrequencyHz == FakeSdrDevice.StationHz).LevelDbfs;
    // Had a stale block been measured, the station's level would show up on
    // the channel after it (and the station would read as noise).
    Assert.All(levels.Where(l => l.FrequencyHz != FakeSdrDevice.StationHz),
      l => Assert.True(l.LevelDbfs < station - 20f, $"{l.FrequencyHz} Hz measured {l.LevelDbfs} dB vs station {station} dB"));
  }

  [Theory]
  [InlineData(1024)]
  [InlineData(3000)]
  public void Sweep_InvalidSamplesPerMeasurement_ThrowsBeforeTouchingTheDevice(int samplesPerMeasurement)
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartReceiver(device);
    int before = device.Calls.Count;

    Assert.Throws<ArgumentOutOfRangeException>(() =>
      receiver.SweepChannels(Channels, SweepGain, null, CancellationToken.None, samplesPerMeasurement));

    Assert.Equal(before, device.Calls.Count);
    Assert.False(receiver.IsSweeping);
  }

  [Fact]
  public void Sweep_UserGainChangeMidSweep_CancelsAndDeviceEndsOnTheNewGain()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartReceiver(device);
    int waits = 0;
    receiver.SweepAwaitingSamples = () =>
    {
      waits++;
      if (waits == 3)
      {
        // The user changes gain from another thread while the sweep is mid-channel.
        Task.Run(() =>
        {
          receiver.AutoGainEnabled = false;
          receiver.Gain = 12f;
        }).GetAwaiter().GetResult();
      }
      device.RaiseBlock();
    };

    Assert.ThrowsAny<OperationCanceledException>(() =>
      receiver.SweepChannels(Channels, SweepGain, null, CancellationToken.None));

    string[] gainCalls = device.Calls.Where(c => c.StartsWith("SetGain", StringComparison.Ordinal)).ToArray();
    Assert.Equal(new[] { "SetGainMode:False", "SetGain:12" }, gainCalls[^2..]);
    Assert.False(receiver.IsSweeping);
  }

  [Fact]
  public void Sweep_NeverRaisesFrequencyChanged_OrMovesCurrentFrequency_AndReturnsToStation()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartReceiver(device);
    int frequencyChanged = 0;
    receiver.FrequencyChanged += (_, _) => Interlocked.Increment(ref frequencyChanged);
    List<long> seenCurrent = new();
    receiver.SweepAwaitingSamples = () =>
    {
      seenCurrent.Add(receiver.CurrentFrequency);
      device.RaiseBlock();
    };
    int hopsBefore = device.SetFrequencyCalls.Count;

    receiver.SweepChannels(Channels, SweepGain, null, CancellationToken.None);

    Assert.Equal(0, frequencyChanged);
    Assert.NotEmpty(seenCurrent);
    Assert.All(seenCurrent, hz => Assert.Equal(StartHz, hz));
    Assert.Equal(StartHz, receiver.CurrentFrequency);

    // Every channel was hopped to on the device, then the device went back.
    IReadOnlyList<long> sweepTunes = device.SetFrequencyCalls.Skip(hopsBefore).ToArray();
    Assert.Equal(Channels.Append(StartHz), sweepTunes);
  }

  [Fact]
  public void Sweep_SilencesAudio_WithoutTouchingIsMuted_ThenMutesTailBlocks()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartReceiver(device);
    device.BlockFactory = FakeSdrDevice.StrongBlock;

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

    float[] NextAudio()
    {
      Assert.True(audioEvents.Wait(FailSafe), "DSP thread produced no audio event");
      lock (audio)
      {
        return audio[^1];
      }
    }

    // Control: a strong block outside any sweep produces audible output.
    device.RaiseBlock();
    Assert.Contains(NextAudio(), s => s != 0f);

    List<bool> mutedDuringSweep = new();
    int silentBlocks = 0;
    receiver.SweepAwaitingSamples = () =>
    {
      mutedDuringSweep.Add(receiver.IsMuted);
      device.RaiseBlock();
      if (NextAudio().All(s => s == 0f))
      {
        silentBlocks++;
      }
    };

    receiver.SweepChannels(Channels, SweepGain, null, CancellationToken.None);

    Assert.NotEmpty(mutedDuringSweep);
    Assert.Equal(mutedDuringSweep.Count, silentBlocks);
    Assert.All(mutedDuringSweep, Assert.False);
    Assert.False(receiver.IsMuted);

    // The first blocks after the sweep are still silenced (the USB block in
    // flight across the final retune), then audio resumes.
    device.RaiseBlock();
    Assert.All(NextAudio(), s => Assert.Equal(0f, s));
    device.RaiseBlock();
    Assert.All(NextAudio(), s => Assert.Equal(0f, s));
    device.RaiseBlock();
    Assert.Contains(NextAudio(), s => s != 0f);
  }

  [Fact]
  public void Sweep_UserTuneMidSweep_CancelsAndDeviceEndsOnNewFrequency()
  {
    const long userHz = 95_100_000;
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartReceiver(device);
    List<long> changedTo = new();
    receiver.FrequencyChanged += (_, e) => changedTo.Add(e.NewFrequency);
    int waits = 0;
    receiver.SweepAwaitingSamples = () =>
    {
      waits++;
      if (waits == 3)
      {
        // The user tunes from another thread while the sweep is mid-channel.
        Task.Run(() => receiver.SetFrequency(userHz)).GetAwaiter().GetResult();
      }
      device.RaiseBlock();
    };

    Assert.ThrowsAny<OperationCanceledException>(() =>
      receiver.SweepChannels(Channels, SweepGain, null, CancellationToken.None));

    Assert.Equal(userHz, receiver.CurrentFrequency);
    Assert.Equal(new[] { userHz }, changedTo);
    IReadOnlyList<long> tunes = device.SetFrequencyCalls;
    int userTune = tunes.ToList().IndexOf(userHz);
    Assert.True(userTune >= 0);
    // No sweep hop after the user's tune: everything from there on is the user's frequency.
    Assert.All(tunes.Skip(userTune), hz => Assert.Equal(userHz, hz));
    Assert.False(receiver.IsSweeping);
  }

  [Fact]
  public void Sweep_UsesManualGain_ThenRestoresAutoGain()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartReceiver(device);
    receiver.SweepAwaitingSamples = device.RaiseBlock;
    int before = device.Calls.Count;

    receiver.SweepChannels(Channels, SweepGain, null, CancellationToken.None);

    string[] gainCalls = device.Calls.Skip(before).Where(c => c.StartsWith("SetGain", StringComparison.Ordinal)).ToArray();
    Assert.Equal(new[] { "SetGainMode:False", "SetGain:28", "SetGainMode:True" }, gainCalls);
  }

  [Fact]
  public void Sweep_UsesManualGain_ThenRestoresPriorManualGain()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartReceiver(device);
    receiver.AutoGainEnabled = false;
    receiver.Gain = 15f;
    receiver.SweepAwaitingSamples = device.RaiseBlock;
    int before = device.Calls.Count;

    receiver.SweepChannels(Channels, SweepGain, null, CancellationToken.None);

    string[] gainCalls = device.Calls.Skip(before).Where(c => c.StartsWith("SetGain", StringComparison.Ordinal)).ToArray();
    Assert.Equal(new[] { "SetGainMode:False", "SetGain:28", "SetGainMode:False", "SetGain:15" }, gainCalls);
  }

  [Fact]
  public void Sweep_WhenNotRunning_Throws()
  {
    using RadioReceiver receiver = new(new FakeSdrDevice());

    Assert.Throws<InvalidOperationException>(() =>
      receiver.SweepChannels(Channels, SweepGain, null, CancellationToken.None));
  }

  [Fact]
  public void Sweep_WhileAlreadySweeping_Throws()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartReceiver(device);
    Exception? nested = null;
    receiver.SweepAwaitingSamples = () =>
    {
      if (nested == null)
      {
        nested = Record.Exception(() => receiver.SweepChannels(Channels, SweepGain, null, CancellationToken.None));
      }
      device.RaiseBlock();
    };

    receiver.SweepChannels(Channels, SweepGain, null, CancellationToken.None);

    Assert.IsType<InvalidOperationException>(nested);
  }

  [Fact]
  public void Shutdown_MidSweep_CancelsSweepAndCompletes()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartReceiver(device);
    using ManualResetEventSlim cancelRequested = new();
    ReceiverState? stateAtCancel = null;
    receiver.SweepCancelRequested = () =>
    {
      stateAtCancel = receiver.GetRadioState().State;
      cancelRequested.Set();
    };
    Thread? shutdownThread = null;
    int waits = 0;
    receiver.SweepAwaitingSamples = () =>
    {
      waits++;
      if (waits == 2)
      {
        shutdownThread = new Thread(receiver.Shutdown);
        shutdownThread.Start();
        // Do not raise a block; return once Shutdown has cancelled us.
        Assert.True(cancelRequested.Wait(FailSafe), "Shutdown did not cancel the sweep");
        return;
      }
      device.RaiseBlock();
    };

    Assert.ThrowsAny<OperationCanceledException>(() =>
      receiver.SweepChannels(Channels, SweepGain, null, CancellationToken.None));

    Assert.NotNull(shutdownThread);
    Assert.True(shutdownThread!.Join(FailSafe), "Shutdown did not complete");
    Assert.False(receiver.IsRunning);
    Assert.False(receiver.IsSweeping);
    Assert.Equal("Close", device.Calls.Last(c => c is "Close" or "Open"));

    // Stopping was published before the sweep was cancelled, so no new
    // sweep could register between the cancel and the close.
    Assert.Equal(ReceiverState.Stopping, stateAtCancel);

    // The sweep's restore (station, then gain mode) ran before Shutdown closed the device.
    List<string> calls = device.Calls.ToList();
    int close = calls.LastIndexOf("Close");
    Assert.Equal(
      new[] { $"SetFrequency:{StartHz}", "SetGainMode:True", "StopStreaming", "Close" },
      calls.Skip(close - 3).Take(4));
  }

  [Fact]
  public void CancelSweep_WhenIdle_DoesNothing()
  {
    using RadioReceiver receiver = StartReceiver(new FakeSdrDevice());

    receiver.CancelSweep();

    Assert.False(receiver.IsSweeping);
  }
}
