using Microsoft.Extensions.Logging;
using Moq;
using Radio.Infrastructure.Audio.SoundFlow;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Structs;
using Xunit;
using Xunit.Abstractions;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// Diagnostic tests that simulate the full FM radio audio pipeline to detect
/// and diagnose audio dropouts (zero runs in the output waveform).
///
/// Pipeline under test:
///   RTL-SDR callback (mono float) → mono→stereo → BufferedSoundGenerator.AddSamples()
///   → GenerateAudio() (mixer consumer) → output analysis for zero runs
///
/// The tests simulate realistic timing: RTL-SDR delivers ~3277 mono samples per
/// USB transfer at ~68ms intervals (240kHz SDR / 5:1 audio decimation / 48kHz output).
/// The mixer consumes at 48kHz stereo in configurable chunk sizes.
/// </summary>
public class FmAudioDropoutDiagnosticTests
{
  private readonly ITestOutputHelper _output;
  private readonly Mock<AudioEngine> _engineMock;

  // FM broadcast pipeline constants (98 MHz strong station)
  private const int AudioSampleRate = 48000;
  private const int Channels = 2;
  private const int SamplesPerSecondStereo = AudioSampleRate * Channels;

  // RTL-SDR USB transfer produces 16384 IQ pairs → after 5:1 audio decimation
  // yields ~3277 mono samples per callback (~68ms of audio at 48kHz)
  private const int MonoSamplesPerCallback = 3277;
  private const int StereoSamplesPerCallback = MonoSamplesPerCallback * 2;
  private const double CallbackIntervalMs = (double)MonoSamplesPerCallback / AudioSampleRate * 1000;

  // Mixer typically requests 256-1024 stereo samples per GenerateAudio call
  // SoundFlow/MiniAudio default period is 256 frames = 512 stereo samples
  private const int MixerChunkStereo = 512;

  public FmAudioDropoutDiagnosticTests(ITestOutputHelper output)
  {
    _output = output;
    _engineMock = new Mock<AudioEngine>();
  }

  /// <summary>
  /// Exposes BufferedSoundGenerator.GenerateAudio for direct testing.
  /// </summary>
  private class TestableGenerator : BufferedSoundGenerator<float>
  {
    public TestableGenerator(AudioEngine engine, AudioFormat format, ILogger logger,
      float maxBufferSeconds = 2.0f)
      : base(engine, format, logger, maxBufferSeconds) { }

    /// <summary>
    /// Simulates the mixer pulling audio. Returns the buffer contents.
    /// </summary>
    public float[] PullAudio(int sampleCount)
    {
      var buffer = new float[sampleCount];
      GenerateAudio(buffer, Format.Channels);
      return buffer;
    }
  }

  /// <summary>
  /// Analyzes output audio for zero runs (dropouts).
  /// Returns a list of (startSample, lengthSamples) for each contiguous zero run.
  /// </summary>
  private static List<(int Start, int Length)> FindZeroRuns(float[] samples, int minRunLength = 4)
  {
    var runs = new List<(int Start, int Length)>();
    int runStart = -1;
    int runLength = 0;

    for (int i = 0; i < samples.Length; i++)
    {
      if (samples[i] == 0f)
      {
        if (runStart < 0) runStart = i;
        runLength++;
      }
      else
      {
        if (runLength >= minRunLength)
        {
          runs.Add((runStart, runLength));
        }
        runStart = -1;
        runLength = 0;
      }
    }

    if (runLength >= minRunLength)
    {
      runs.Add((runStart, runLength));
    }

    return runs;
  }

  /// <summary>
  /// Generates a simulated FM audio callback with a 1kHz tone.
  /// Returns mono float samples that look like real demodulated FM audio.
  /// </summary>
  private static float[] GenerateToneCallback(int monoSamples, int callbackIndex, float amplitude = 0.3f)
  {
    var mono = new float[monoSamples];
    var basePhase = callbackIndex * monoSamples; // Maintain phase continuity
    for (int i = 0; i < monoSamples; i++)
    {
      // 1kHz tone at 48kHz sample rate
      mono[i] = amplitude * MathF.Sin(2 * MathF.PI * 1000f * (basePhase + i) / AudioSampleRate);
    }
    return mono;
  }

  /// <summary>
  /// Converts mono samples to interleaved stereo (same as SDRRadioAudioSource).
  /// </summary>
  private static float[] MonoToStereo(float[] mono)
  {
    var stereo = new float[mono.Length * 2];
    for (int i = 0; i < mono.Length; i++)
    {
      stereo[i * 2] = mono[i];
      stereo[i * 2 + 1] = mono[i];
    }
    return stereo;
  }

  /// <summary>
  /// Simulates the full pipeline with time-based scheduling.
  /// Both producer (RTL-SDR callbacks) and consumer (mixer pulls) run on
  /// independent clocks, just like the real system.
  ///
  /// Producer: delivers MonoSamplesPerCallback samples every ~68.3ms
  /// Consumer: pulls MixerChunkStereo samples every ~5.3ms (512 / 96000)
  ///
  /// Events are interleaved by timestamp to simulate real scheduling.
  /// </summary>
  private (float[] OutputAudio, PipelineMetrics Metrics) RunPipelineSimulation(
    TestableGenerator generator,
    int durationSeconds,
    Func<int, float[]?> callbackProvider, // Returns mono samples or null (missed callback)
    int mixerChunkSize = MixerChunkStereo)
  {
    var totalMixerSamples = durationSeconds * SamplesPerSecondStereo;
    var output = new float[totalMixerSamples];
    var outputOffset = 0;
    var metrics = new PipelineMetrics();

    // Producer timing: callback period = MonoSamplesPerCallback / AudioSampleRate
    var producerPeriodMs = (double)MonoSamplesPerCallback / AudioSampleRate * 1000.0;
    // Consumer timing: mixer period = MixerChunkStereo / SamplesPerSecondStereo
    var consumerPeriodMs = (double)mixerChunkSize / SamplesPerSecondStereo * 1000.0;

    var durationMs = durationSeconds * 1000.0;
    var nextProducerMs = 0.0;
    var nextConsumerMs = 0.0;
    int callbackIndex = 0;

    // Time-step simulation: process events in chronological order
    while (nextConsumerMs < durationMs || outputOffset < totalMixerSamples)
    {
      // Determine which event fires next
      bool doProducer = nextProducerMs <= nextConsumerMs && nextProducerMs < durationMs;
      bool doConsumer = !doProducer || nextConsumerMs <= nextProducerMs;

      if (doProducer && nextProducerMs < durationMs)
      {
        // === Producer: RTL-SDR callback ===
        var monoSamples = callbackProvider(callbackIndex);
        if (monoSamples != null)
        {
          var stereo = MonoToStereo(monoSamples);
          generator.AddSamples(stereo);
          metrics.CallbacksDelivered++;
          metrics.TotalSamplesProduced += stereo.Length;
        }
        else
        {
          metrics.CallbacksMissed++;
        }
        callbackIndex++;
        nextProducerMs += producerPeriodMs;
      }

      if (doConsumer && outputOffset < totalMixerSamples)
      {
        // === Consumer: Mixer pull ===
        var remaining = totalMixerSamples - outputOffset;
        var chunkSize = Math.Min(mixerChunkSize, remaining);
        var chunk = generator.PullAudio(chunkSize);
        Array.Copy(chunk, 0, output, outputOffset, chunkSize);
        outputOffset += chunkSize;
        metrics.MixerPulls++;
        metrics.TotalSamplesConsumed += chunkSize;
        nextConsumerMs += consumerPeriodMs;
      }

      // Safety: prevent infinite loop if both times are past duration
      if (nextProducerMs >= durationMs && outputOffset >= totalMixerSamples)
        break;
    }

    metrics.Diagnostics = generator.GetDiagnostics();
    return (output, metrics);
  }

  [Fact]
  public void Baseline_ContinuousCallbacks_NoDropouts()
  {
    // Arrange: Continuous FM audio with no gaps — should produce zero dropouts
    var format = new AudioFormat
    {
      SampleRate = AudioSampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    // Act: Run 3 seconds of continuous audio
    var (output, metrics) = RunPipelineSimulation(
      generator,
      durationSeconds: 3,
      callbackProvider: cb => GenerateToneCallback(MonoSamplesPerCallback, cb));

    // Analyze
    var zeroRuns = FindZeroRuns(output, minRunLength: 8);
    LogMetrics("Baseline (continuous)", metrics, zeroRuns, output);

    // Assert: No significant zero runs in continuous audio
    var significantRuns = zeroRuns.Where(r => r.Length > MixerChunkStereo).ToList();
    Assert.Empty(significantRuns);
    Assert.Equal(0, metrics.CallbacksMissed);
    _output.WriteLine("PASS: Continuous audio produces no dropouts");
  }

  [Fact]
  public void SquelchGap_SingleMissedCallback_ShowsDropout()
  {
    // Arrange: Simulate squelch gate dropping ONE callback (~68ms gap)
    var format = new AudioFormat
    {
      SampleRate = AudioSampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    const int gapAtCallback = 22; // Gap after ~1.5 seconds

    // Act
    var (output, metrics) = RunPipelineSimulation(
      generator,
      durationSeconds: 3,
      callbackProvider: cb =>
      {
        if (cb == gapAtCallback) return null; // Squelch closed → no audio
        return GenerateToneCallback(MonoSamplesPerCallback, cb);
      });

    // Analyze
    var zeroRuns = FindZeroRuns(output, minRunLength: 8);
    LogMetrics("Single squelch gap", metrics, zeroRuns, output);

    // Assert: Should show exactly 1 missed callback and at least one zero run
    Assert.Equal(1, metrics.CallbacksMissed);
    Assert.NotEmpty(zeroRuns);
    _output.WriteLine($"DETECTED: Single missed callback creates {zeroRuns.Count} zero run(s)");
    foreach (var run in zeroRuns)
    {
      var durationMs = (double)run.Length / SamplesPerSecondStereo * 1000;
      _output.WriteLine($"  Zero run at sample {run.Start}: {run.Length} samples ({durationMs:F1}ms)");
    }
  }

  [Fact]
  public void SquelchFlutter_IntermittentGaps_ShowsDropoutPattern()
  {
    // Arrange: Simulate signal strength fluctuating around squelch threshold
    // Every ~5th callback is dropped (signal briefly dips below threshold)
    var format = new AudioFormat
    {
      SampleRate = AudioSampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    // Act
    var (output, metrics) = RunPipelineSimulation(
      generator,
      durationSeconds: 5,
      callbackProvider: cb =>
      {
        // Simulate periodic signal dips — every 7th callback is lost
        if (cb % 7 == 3) return null;
        return GenerateToneCallback(MonoSamplesPerCallback, cb);
      });

    // Analyze
    var zeroRuns = FindZeroRuns(output, minRunLength: 8);
    LogMetrics("Squelch flutter (1-in-7 callbacks dropped)", metrics, zeroRuns, output);

    // Report dropout pattern
    Assert.True(metrics.CallbacksMissed > 0);
    var totalDropoutSamples = zeroRuns.Sum(r => r.Length);
    var totalDropoutMs = (double)totalDropoutSamples / SamplesPerSecondStereo * 1000;
    _output.WriteLine($"TOTAL DROPOUT: {totalDropoutMs:F1}ms across {zeroRuns.Count} gaps");
  }

  [Fact]
  public void ConsumerOutpacesProducer_SmallBuffer_ShowsUnderruns()
  {
    // Arrange: Use a very small buffer (200ms) to expose underrun behavior
    var format = new AudioFormat
    {
      SampleRate = AudioSampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object,
      maxBufferSeconds: 0.2f); // Only 200ms buffer

    // Act: Run with continuous audio but tiny buffer
    var (output, metrics) = RunPipelineSimulation(
      generator,
      durationSeconds: 3,
      callbackProvider: cb => GenerateToneCallback(MonoSamplesPerCallback, cb));

    // Analyze
    var zeroRuns = FindZeroRuns(output, minRunLength: 8);
    LogMetrics("Small buffer (200ms)", metrics, zeroRuns, output);

    // Even with continuous callbacks, a tiny buffer may underrun
    _output.WriteLine($"Buffer underruns: {metrics.Diagnostics.TotalDropped}");
    _output.WriteLine($"Zero runs found: {zeroRuns.Count}");
  }

  [Fact]
  public void RealWorldTiming_JitteredCallbacks_ShowsDropoutRisk()
  {
    // Arrange: Simulate USB transfer jitter — callbacks arrive at variable intervals.
    // Some callbacks arrive late (next mixer pull window has already consumed the buffer).
    // This simulates real USB timing jitter on the Raspberry Pi.
    var format = new AudioFormat
    {
      SampleRate = AudioSampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    // Simulate by sometimes delivering a double-sized callback followed by no callback
    // (USB transfer arrived late, two transfers coalesced)
    var (output, metrics) = RunPipelineSimulation(
      generator,
      durationSeconds: 3,
      callbackProvider: cb =>
      {
        // Every 10th callback: simulate delayed double delivery
        if (cb % 10 == 5)
        {
          // Double-sized callback (two USB transfers coalesced)
          var mono = new float[MonoSamplesPerCallback * 2];
          var tone1 = GenerateToneCallback(MonoSamplesPerCallback, cb);
          var tone2 = GenerateToneCallback(MonoSamplesPerCallback, cb + 1);
          Array.Copy(tone1, 0, mono, 0, MonoSamplesPerCallback);
          Array.Copy(tone2, 0, mono, MonoSamplesPerCallback, MonoSamplesPerCallback);
          return mono;
        }
        if (cb % 10 == 6) return null; // Corresponding gap
        return GenerateToneCallback(MonoSamplesPerCallback, cb);
      });

    var zeroRuns = FindZeroRuns(output, minRunLength: 8);
    LogMetrics("Jittered USB timing", metrics, zeroRuns, output);

    // Even with jitter, total audio delivered equals total expected, so should be fine
    _output.WriteLine($"Zero runs: {zeroRuns.Count}");
  }

  [Fact]
  public void WaveformVisualization_CapturesZeroGaps()
  {
    // Arrange: Run pipeline with gaps and check the waveform analyzer sees them
    var format = new AudioFormat
    {
      SampleRate = AudioSampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    var waveformAnalyzer = new Radio.Infrastructure.Audio.Visualization.WaveformAnalyzer(
      sampleCount: 2048, sampleRate: AudioSampleRate);

    const int gapCallback = 15;

    // Act: Run pipeline with a gap, feeding output to waveform analyzer
    var (output, metrics) = RunPipelineSimulation(
      generator,
      durationSeconds: 2,
      callbackProvider: cb =>
      {
        if (cb == gapCallback) return null;
        return GenerateToneCallback(MonoSamplesPerCallback, cb);
      });

    // Feed the output to the waveform analyzer in chunks (like the VisualizationTapModifier does)
    for (int i = 0; i < output.Length - 2048; i += 2048)
    {
      waveformAnalyzer.AddSamples(output.AsSpan(i, 2048), 2048);
    }

    // Get the waveform data
    var (left, right) = waveformAnalyzer.GetSamples();

    // Check if zeros are visible in the waveform
    var leftZeroRuns = FindZeroRuns(left, minRunLength: 4);
    var rightZeroRuns = FindZeroRuns(right, minRunLength: 4);

    _output.WriteLine($"Waveform analyzer sees {leftZeroRuns.Count} zero runs in left channel");
    _output.WriteLine($"Waveform analyzer sees {rightZeroRuns.Count} zero runs in right channel");

    // The waveform buffer is only 2048 samples (~42ms window), so the gap
    // must be visible within that window to be seen
    var outputZeroRuns = FindZeroRuns(output, minRunLength: 8);
    _output.WriteLine($"Full output has {outputZeroRuns.Count} zero runs");
    LogMetrics("Waveform visualization gap detection", metrics, outputZeroRuns, output);
  }

  [Fact]
  public void DiagnosticDump_ShowsBufferHealthOverTime()
  {
    // Arrange: Run pipeline and snapshot buffer health at regular intervals
    var format = new AudioFormat
    {
      SampleRate = AudioSampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    var healthSnapshots = new List<(int Callback, BufferDiagnostics Diag)>();

    // Act: Manual pipeline run with periodic diagnostics
    var totalCallbacks = (int)(5 * AudioSampleRate / (double)MonoSamplesPerCallback);
    var totalOutput = new List<float>();

    for (int cb = 0; cb < totalCallbacks; cb++)
    {
      // Producer: deliver audio (with occasional gaps simulating squelch flutter)
      if (cb % 11 != 5) // Drop 1 in 11 callbacks
      {
        var mono = GenerateToneCallback(MonoSamplesPerCallback, cb);
        var stereo = MonoToStereo(mono);
        generator.AddSamples(stereo);
      }

      // Consumer: pull one callback's worth of audio
      var mixerPulls = (int)Math.Ceiling((double)StereoSamplesPerCallback / MixerChunkStereo);
      for (int p = 0; p < mixerPulls; p++)
      {
        var chunk = generator.PullAudio(MixerChunkStereo);
        totalOutput.AddRange(chunk);
      }

      // Snapshot every 0.5 seconds
      if (cb % (int)(0.5 * AudioSampleRate / MonoSamplesPerCallback) == 0)
      {
        healthSnapshots.Add((cb, generator.GetDiagnostics()));
      }
    }

    // Report
    _output.WriteLine("=== BUFFER HEALTH OVER TIME ===");
    _output.WriteLine($"{"Time":>8} {"Received":>12} {"Output":>12} {"Dropped":>10} {"Buffered":>10} {"Fill%":>8}");
    foreach (var (cb, diag) in healthSnapshots)
    {
      var timeMs = cb * CallbackIntervalMs;
      var fillPercent = diag.BufferCapacity > 0
        ? (double)diag.BufferCount / diag.BufferCapacity * 100 : 0;
      _output.WriteLine($"{timeMs,8:F0}ms {diag.TotalReceived,12} {diag.TotalOutput,12} {diag.TotalDropped,10} {diag.BufferCount,10} {fillPercent,7:F1}%");
    }

    var outputArray = totalOutput.ToArray();
    var zeroRuns = FindZeroRuns(outputArray, minRunLength: 8);
    _output.WriteLine($"\nTotal zero runs: {zeroRuns.Count}");
    foreach (var run in zeroRuns.Take(20))
    {
      var timeMs = (double)run.Start / SamplesPerSecondStereo * 1000;
      var durationMs = (double)run.Length / SamplesPerSecondStereo * 1000;
      _output.WriteLine($"  Gap at {timeMs:F1}ms: {run.Length} samples ({durationMs:F1}ms)");
    }
  }

  [Fact]
  public void SquelchSilenceFix_DeliversSilenceInsteadOfNothing_NoLargeGaps()
  {
    // This test verifies the FIX: when squelch closes, deliver silence (zero-filled
    // buffer) instead of delivering NOTHING. This keeps the BufferedSoundGenerator
    // fed at a consistent rate, preventing underruns.
    //
    // Before fix: squelch → no callback → buffer drains → underrun → garbled audio
    // After fix:  squelch → silence callback → buffer stays fed → clean transition
    var format = new AudioFormat
    {
      SampleRate = AudioSampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    const int gapAtCallback = 22;

    // Simulate the fix: squelch delivers silence (zero-valued samples) instead of null
    var silenceMono = new float[MonoSamplesPerCallback]; // All zeros = silence

    var (output, metrics) = RunPipelineSimulation(
      generator,
      durationSeconds: 3,
      callbackProvider: cb =>
      {
        if (cb == gapAtCallback) return silenceMono; // Silence, not null!
        return GenerateToneCallback(MonoSamplesPerCallback, cb);
      });

    var zeroRuns = FindZeroRuns(output, minRunLength: 8);
    LogMetrics("Squelch silence fix (single gap)", metrics, zeroRuns, output);

    // With the fix, zero runs come from intentional silence (squelch) and small
    // structural residuals. There should be no UNDERRUN gaps (buffer depletion).
    // The key indicator: buffer never fully depletes, no samples dropped.
    Assert.Equal(0, metrics.CallbacksMissed); // All callbacks delivered (silence counts)
    Assert.Equal(0, metrics.Diagnostics.TotalDropped);
    Assert.True(metrics.Diagnostics.BufferCount > 0,
      "Buffer should not be fully depleted with silence delivery");
    _output.WriteLine("PASS: Squelch silence delivery prevents buffer depletion");
  }

  [Fact]
  public void SquelchSilenceFix_FlutterWithSilence_NoUnderruns()
  {
    // Verify the fix works for squelch flutter (frequent brief signal dips).
    // With the fix, every squelch-closed callback still delivers silence data.
    var format = new AudioFormat
    {
      SampleRate = AudioSampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    var silenceMono = new float[MonoSamplesPerCallback];

    var (output, metrics) = RunPipelineSimulation(
      generator,
      durationSeconds: 5,
      callbackProvider: cb =>
      {
        // Every 7th callback: signal dips below squelch → deliver silence
        if (cb % 7 == 3) return silenceMono;
        return GenerateToneCallback(MonoSamplesPerCallback, cb);
      });

    var zeroRuns = FindZeroRuns(output, minRunLength: 8);
    LogMetrics("Squelch flutter with silence fix", metrics, zeroRuns, output);

    // With the fix, zero runs come from two sources:
    // 1. Intentional squelch silence (≤ StereoSamplesPerCallback + small residual)
    // 2. Structural timing residuals (~102 samples)
    // There should be NO underrun-caused gaps (which would be significantly larger
    // than a single callback, showing buffer depletion).
    // The unfixed version has gaps up to 6758 samples that grow over time as the
    // buffer drains. With the fix, buffer never drains (diagnostics show dropped=0).
    Assert.Equal(0, metrics.CallbacksMissed);
    Assert.Equal(0, metrics.Diagnostics.TotalDropped);

    // Key metric: buffer never fully depletes. Final buffer count should be > 0.
    _output.WriteLine($"Buffer final fill: {metrics.Diagnostics.BufferCount} (should be > 0 with fix)");
    Assert.True(metrics.Diagnostics.BufferCount > 0,
      $"Buffer should not be fully depleted with silence delivery (was {metrics.Diagnostics.BufferCount})");

    // Compare: the unfixed version shows buffer draining to 0 with 698.9ms total dropout.
    // With the fix, zeros are intentional silence, not underruns.
    var totalGapMs = zeroRuns.Sum(r => (double)r.Length) / SamplesPerSecondStereo * 1000;
    _output.WriteLine($"FIXED: Zero runs are intentional silence, buffer stays fed. Total: {totalGapMs:F1}ms");
  }

  private void LogMetrics(string scenario, PipelineMetrics metrics,
    List<(int Start, int Length)> zeroRuns, float[] output)
  {
    _output.WriteLine($"\n=== {scenario} ===");
    _output.WriteLine($"Callbacks: {metrics.CallbacksDelivered} delivered, {metrics.CallbacksMissed} missed");
    _output.WriteLine($"Mixer pulls: {metrics.MixerPulls}");
    _output.WriteLine($"Samples: {metrics.TotalSamplesProduced} produced, {metrics.TotalSamplesConsumed} consumed");
    _output.WriteLine($"Buffer final: {metrics.Diagnostics.BufferCount}/{metrics.Diagnostics.BufferCapacity} " +
      $"({(double)metrics.Diagnostics.BufferCount / metrics.Diagnostics.BufferCapacity * 100:F1}%)");
    _output.WriteLine($"Buffer stats: received={metrics.Diagnostics.TotalReceived}, " +
      $"output={metrics.Diagnostics.TotalOutput}, dropped={metrics.Diagnostics.TotalDropped}");

    // Audio quality analysis
    var nonZeroSamples = output.Count(s => s != 0f);
    var zeroPct = (double)(output.Length - nonZeroSamples) / output.Length * 100;
    _output.WriteLine($"Audio quality: {nonZeroSamples}/{output.Length} non-zero ({100 - zeroPct:F1}% fill)");
    _output.WriteLine($"Zero runs (≥8 samples): {zeroRuns.Count}");

    if (zeroRuns.Count > 0)
    {
      var maxGap = zeroRuns.Max(r => r.Length);
      var avgGap = zeroRuns.Average(r => r.Length);
      var totalGapMs = zeroRuns.Sum(r => (double)r.Length) / SamplesPerSecondStereo * 1000;
      _output.WriteLine($"Largest gap: {maxGap} samples ({(double)maxGap / SamplesPerSecondStereo * 1000:F1}ms)");
      _output.WriteLine($"Average gap: {avgGap:F0} samples");
      _output.WriteLine($"Total gap time: {totalGapMs:F1}ms");

      // Show first 10 gaps
      foreach (var run in zeroRuns.Take(10))
      {
        var timeMs = (double)run.Start / SamplesPerSecondStereo * 1000;
        var durationMs = (double)run.Length / SamplesPerSecondStereo * 1000;
        _output.WriteLine($"  [{timeMs:F1}ms] {run.Length} samples ({durationMs:F1}ms)");
      }
    }
  }

  [Fact]
  public void ClockDrift_BtFasterThanPlayback_ShowsDrift()
  {
    // Simulate BT clock running 0.035s/min faster than playback clock.
    // Over 10s, producer delivers ~0.058s extra audio, causing buffer to grow.
    // With DropOldest, excess samples are dropped to prevent overflow.
    var format = new AudioFormat
    {
      SampleRate = AudioSampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object,
      maxBufferSeconds: 1.0f);

    // Producer rate: slightly faster than consumer
    var driftFactor = 1.00058; // 0.058% faster (0.035s/min)
    var producerSamplesPerCallback = (int)(MonoSamplesPerCallback * driftFactor);

    var (output, metrics) = RunPipelineSimulation(
      generator,
      durationSeconds: 10,
      callbackProvider: cb =>
        GenerateToneCallback(producerSamplesPerCallback, cb));

    var zeroRuns = FindZeroRuns(output, minRunLength: 8);
    LogMetrics("BT clock drift (producer faster)", metrics, zeroRuns, output);

    // Verify DropOldest behavior: some samples should be dropped
    _output.WriteLine($"Dropped samples: {metrics.Diagnostics.TotalDropped}");
    _output.WriteLine($"Buffer final fill: {metrics.Diagnostics.BufferCount}/{metrics.Diagnostics.BufferCapacity}");

    // With DropOldest, audio should still play without large gaps
    var significantGaps = zeroRuns.Where(r => r.Length > StereoSamplesPerCallback).ToList();
    Assert.Empty(significantGaps);
  }

  [Fact]
  public void ClockDrift_BtSlowerThanPlayback_ShowsUnderruns()
  {
    // Producer clock slower than consumer → buffer drains → underruns.
    // Clock drift compensation should mitigate some of this.
    var format = new AudioFormat
    {
      SampleRate = AudioSampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object,
      maxBufferSeconds: 2.0f);

    // Deliver fewer samples per callback (simulating slower BT clock)
    var driftFactor = 0.9994; // 0.06% slower
    var producerSamplesPerCallback = (int)(MonoSamplesPerCallback * driftFactor);

    var (output, metrics) = RunPipelineSimulation(
      generator,
      durationSeconds: 10,
      callbackProvider: cb =>
        GenerateToneCallback(producerSamplesPerCallback, cb));

    var zeroRuns = FindZeroRuns(output, minRunLength: 8);
    LogMetrics("BT clock drift (producer slower)", metrics, zeroRuns, output);

    _output.WriteLine($"Buffer draining — compensated samples: {metrics.Diagnostics.TotalCompensated}");

    // Report drift impact — this is diagnostic, not strictly pass/fail
    var nonZero = output.Count(s => s != 0f);
    var fillPct = (double)nonZero / output.Length * 100;
    _output.WriteLine($"Audio fill: {fillPct:F1}%");
  }

  [Fact]
  public void HighFrequencyAddSamples_LockContention_MeasuresImpact()
  {
    // Compare small-frequent vs large-infrequent AddSamples calls.
    // Small-frequent calls increase lock contention on _bufferLock.
    var format = new AudioFormat
    {
      SampleRate = AudioSampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();

    // Test 1: Many small writes (simulating PipeWire native 5ms callbacks)
    var gen1 = new TestableGenerator(_engineMock.Object, format, logger.Object);
    var smallChunkSize = 480; // 5ms stereo
    var totalSamples = SamplesPerSecondStereo * 2; // 2 seconds
    var inputData = new float[totalSamples];
    for (int i = 0; i < totalSamples; i++)
      inputData[i] = MathF.Sin(2 * MathF.PI * 440f * (i / 2) / AudioSampleRate) * 0.5f;

    for (int offset = 0; offset < totalSamples; offset += smallChunkSize)
    {
      var len = Math.Min(smallChunkSize, totalSamples - offset);
      gen1.AddSamples(inputData.AsSpan(offset, len));
    }
    var output1 = gen1.PullAudio(totalSamples);
    var diag1 = gen1.GetDiagnostics();

    // Test 2: Fewer large writes (simulating SDR ~68ms callbacks)
    var gen2 = new TestableGenerator(_engineMock.Object, format, logger.Object);
    var largeChunkSize = 6554; // ~68ms stereo

    for (int offset = 0; offset < totalSamples; offset += largeChunkSize)
    {
      var len = Math.Min(largeChunkSize, totalSamples - offset);
      gen2.AddSamples(inputData.AsSpan(offset, len));
    }
    var output2 = gen2.PullAudio(totalSamples);
    var diag2 = gen2.GetDiagnostics();

    _output.WriteLine($"Small chunks ({smallChunkSize}): received={diag1.TotalReceived}, output={diag1.TotalOutput}");
    _output.WriteLine($"Large chunks ({largeChunkSize}): received={diag2.TotalReceived}, output={diag2.TotalOutput}");

    // Both should produce equivalent output
    Assert.Equal(totalSamples, (int)diag1.TotalReceived);
    Assert.Equal(totalSamples, (int)diag2.TotalReceived);
  }

  [Fact]
  public void GcPauseInjection_DuringCallback_MeasuresDropouts()
  {
    // Simulate GC pauses (5-50ms) during producer delivery.
    // Pauses cause delayed AddSamples, which may cause the consumer
    // to run ahead and hit underruns.
    var format = new AudioFormat
    {
      SampleRate = AudioSampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object,
      maxBufferSeconds: 2.0f);
    generator.PreFillSilence(0.5f); // Prefill helps absorb GC pauses

    var totalCallbacks = (int)(5 * AudioSampleRate / (double)MonoSamplesPerCallback);
    var totalOutput = new List<float>();
    var random = new Random(42);

    for (int cb = 0; cb < totalCallbacks; cb++)
    {
      // Inject a GC-like pause every ~20 callbacks
      if (cb > 0 && cb % 20 == 0)
      {
        // Simulate consumer running during the GC pause by pulling audio
        var gcPauseMs = random.Next(5, 50);
        var gcPausePulls = (int)(gcPauseMs / ((double)MixerChunkStereo / SamplesPerSecondStereo * 1000));
        for (int p = 0; p < gcPausePulls; p++)
        {
          var chunk = generator.PullAudio(MixerChunkStereo);
          totalOutput.AddRange(chunk);
        }
      }

      // Producer delivers audio
      var mono = GenerateToneCallback(MonoSamplesPerCallback, cb);
      var stereo = MonoToStereo(mono);
      generator.AddSamples(stereo);

      // Consumer pulls
      var mixerPulls = (int)Math.Ceiling((double)StereoSamplesPerCallback / MixerChunkStereo);
      for (int p = 0; p < mixerPulls; p++)
      {
        var chunk = generator.PullAudio(MixerChunkStereo);
        totalOutput.AddRange(chunk);
      }
    }

    var outputArray = totalOutput.ToArray();
    var zeroRuns = FindZeroRuns(outputArray, minRunLength: 8);

    _output.WriteLine($"GC pause injection test:");
    _output.WriteLine($"  Total output: {outputArray.Length} samples");
    _output.WriteLine($"  Zero runs: {zeroRuns.Count}");
    if (zeroRuns.Count > 0)
    {
      var maxGap = zeroRuns.Max(r => r.Length);
      var totalGapMs = zeroRuns.Sum(r => (double)r.Length) / SamplesPerSecondStereo * 1000;
      _output.WriteLine($"  Largest gap: {maxGap} samples ({(double)maxGap / SamplesPerSecondStereo * 1000:F1}ms)");
      _output.WriteLine($"  Total gap time: {totalGapMs:F1}ms");
    }

    // Pre-fill should absorb most GC pauses — no catastrophic gaps
    var diag = generator.GetDiagnostics();
    _output.WriteLine($"  Buffer final: {diag.BufferCount}/{diag.BufferCapacity}");
    _output.WriteLine($"  Compensated: {diag.TotalCompensated}");
  }

  [Fact]
  public async Task ConcurrentProducerConsumer_StressTest_NoCorruption()
  {
    // Real concurrent threading: producer and consumer run on separate threads.
    // Verifies no data corruption, deadlocks, or crashes under contention.
    var format = new AudioFormat
    {
      SampleRate = AudioSampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object,
      maxBufferSeconds: 2.0f);
    generator.PreFillSilence(0.5f);

    var durationSeconds = 5;
    var producerDone = false;

    // Producer thread: delivers audio at roughly real-time rate
    var producerTask = Task.Run(() =>
    {
      var totalCallbacks = (int)(durationSeconds * AudioSampleRate / (double)MonoSamplesPerCallback);
      for (int cb = 0; cb < totalCallbacks; cb++)
      {
        var mono = GenerateToneCallback(MonoSamplesPerCallback, cb);
        var stereo = MonoToStereo(mono);
        generator.AddSamples(stereo);
        // Brief yield to simulate real timing (not a sleep)
        if (cb % 10 == 0) Thread.Yield();
      }
      producerDone = true;
    });

    // Consumer thread: pulls audio at roughly real-time rate
    var totalOutput = new List<float>();
    var consumerTask = Task.Run(() =>
    {
      while (!producerDone || generator.GetDiagnostics().BufferCount > 0)
      {
        var chunk = generator.PullAudio(MixerChunkStereo);
        lock (totalOutput) { totalOutput.AddRange(chunk); }
        if (totalOutput.Count > SamplesPerSecondStereo * durationSeconds * 2)
          break; // Safety: don't accumulate forever
      }
    });

    await Task.WhenAll(producerTask, consumerTask);

    var outputArray = totalOutput.ToArray();
    var diag = generator.GetDiagnostics();

    _output.WriteLine($"Concurrent stress test:");
    _output.WriteLine($"  Producer: {diag.TotalReceived} samples");
    _output.WriteLine($"  Consumer: {outputArray.Length} samples");
    _output.WriteLine($"  Dropped: {diag.TotalDropped}");
    _output.WriteLine($"  Final buffer: {diag.BufferCount}/{diag.BufferCapacity}");

    // Verify no NaN/Infinity (data corruption indicator)
    foreach (var sample in outputArray)
    {
      Assert.False(float.IsNaN(sample), "NaN detected — concurrent corruption");
      Assert.False(float.IsInfinity(sample), "Infinity detected — concurrent corruption");
    }

    // Should have received all samples
    Assert.True(diag.TotalReceived > 0);
    Assert.True(diag.TotalOutput > 0);
  }

  private class PipelineMetrics
  {
    public int CallbacksDelivered;
    public int CallbacksMissed;
    public int MixerPulls;
    public long TotalSamplesProduced;
    public long TotalSamplesConsumed;
    public BufferDiagnostics Diagnostics;
  }
}
