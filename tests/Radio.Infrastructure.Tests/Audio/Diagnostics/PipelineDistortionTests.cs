using Microsoft.Extensions.Logging;
using Moq;
using Radio.AudioAnalysis;
using Radio.Infrastructure.Audio.SoundFlow;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Structs;
using Xunit;
using Xunit.Abstractions;

namespace Radio.Infrastructure.Tests.Audio.Diagnostics;

/// <summary>
/// CI-safe tests that send known audio through the BufferedSoundGenerator pipeline
/// and verify the output matches the input — no hardware required.
/// Uses the TestableGenerator pattern from FmAudioDropoutDiagnosticTests.
/// </summary>
public class PipelineDistortionTests
{
  private readonly ITestOutputHelper _output;
  private readonly Mock<AudioEngine> _engineMock;

  private const int SampleRate = 48000;
  private const int Channels = 2;
  private const int SamplesPerSecondStereo = SampleRate * Channels;

  public PipelineDistortionTests(ITestOutputHelper output)
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

    public float[] PullAudio(int sampleCount)
    {
      var buffer = new float[sampleCount];
      GenerateAudio(buffer, Format.Channels);
      return buffer;
    }
  }

  [Fact]
  public void SineTone_ThroughGenerator_NoDistortion()
  {
    // Generate a known stereo sine tone, push through BufferedSoundGenerator,
    // pull output, and compare input vs output.
    var format = new AudioFormat
    {
      SampleRate = SampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    // Generate 0.5s of stereo sine tone (200Hz L, 300Hz R)
    var reference = WavFileHelper.GenerateStereoSineWave(
      leftHz: 200, rightHz: 300, sampleRate: SampleRate,
      durationSamples: SampleRate / 2, amplitude: 0.8f);

    // Push into generator
    generator.AddSamples(reference);

    // Pull all samples out
    var output = generator.PullAudio(reference.Length);

    // Compare
    var report = WaveformComparison.Compare(reference, output);

    _output.WriteLine($"Report: {report}");
    Assert.True(report.IsClean, $"Expected clean output, got: {report}");
    Assert.True(report.CorrelationCoefficient > 0.999f,
      $"Correlation too low: {report.CorrelationCoefficient:F4}");
  }

  [Fact]
  public void SineTone_ThroughGeneratorAndLimiter_NoDistortion()
  {
    var format = new AudioFormat
    {
      SampleRate = SampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    // Generate tone at 0.8 amplitude — well within limiter threshold
    var reference = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, durationSamples: SampleRate / 2, amplitude: 0.8f);

    generator.AddSamples(reference);
    var rawOutput = generator.PullAudio(reference.Length);

    // Run through limiter modifier (processes sample-by-sample)
    var limiter = new LimiterModifier();
    var limitedOutput = new float[rawOutput.Length];
    for (int i = 0; i < rawOutput.Length; i++)
    {
      limitedOutput[i] = limiter.ProcessSample(rawOutput[i], i % Channels);
    }

    // Limiter at 0.8 amplitude should not trigger — output should match
    var report = WaveformComparison.Compare(reference, limitedOutput);

    _output.WriteLine($"Report: {report}");
    Assert.True(report.IsClean, $"Expected clean after limiter, got: {report}");
  }

  [Theory]
  [InlineData(48000, "1s")]
  [InlineData(480000, "10s")]
  public void SineTone_ExtendedDuration_NoDistortion(int durationSamples, string label)
  {
    var format = new AudioFormat
    {
      SampleRate = SampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object,
      maxBufferSeconds: 4.0f);

    var reference = WavFileHelper.GenerateStereoSineWave(
      leftHz: 200, rightHz: 300, durationSamples: durationSamples, amplitude: 0.8f);

    // Pre-fill the buffer above the drift-compensation threshold so the
    // interleaved push/pull below stays in the "healthy" buffer-fill region
    // and the clock-drift compensation path stays dormant. The test's purpose
    // is signal-integrity through the basic ring-buffer transport, not
    // compensation behaviour; compensation has its own dedicated tests in
    // BufferedSoundGeneratorTests.cs.
    //
    // Path C (docs/plans/2026-05-22-bt-drift-compensation-refinement.md)
    // removed the 2-second cooldown that previously kept compensation
    // dormant during this tight push/pull simulation; without the prefill,
    // the buffer drains to a level below the 15 % threshold on every cycle
    // and triggers continuous compensation events that legitimately distort
    // the test signal (rewind + crossfade by design).
    //
    // Threshold = 15 % of 4-second buffer = ~600 ms = ~57 600 samples; we
    // prefill 1.5 s = 144 000 samples to ride comfortably above it. The
    // prefilled silence comes out of the generator first; we capture and
    // discard those leading samples before comparing the reference against
    // the trailing samples that contain the actual signal.
    generator.PreFillSilence(1.5f);
    var prefillSamples = (int)(SampleRate * Channels * 1.5);

    // Interleave push/pull (simulating real producer/consumer behavior)
    // Push a chunk, then pull the same amount — prevents buffer overflow.
    // Pull `prefillSamples + reference.Length` total: the leading prefill
    // samples are silence (discarded), the trailing samples are the signal.
    var pushChunkSize = 6554; // ~2 RTL-SDR callbacks worth of stereo
    var pullChunkSize = 512;  // Mixer quantum
    var totalPullSamples = prefillSamples + reference.Length;
    var fullOutput = new float[totalPullSamples];
    var pushOffset = 0;
    var pullOffset = 0;

    while (pushOffset < reference.Length || pullOffset < fullOutput.Length)
    {
      // Push one chunk. Once reference is exhausted, push silence so the
      // buffer headroom stays above the drift-compensation threshold for
      // the pull-only tail of the loop. The trailing silence appears past
      // the trim window and does not affect the comparison output.
      if (pushOffset < reference.Length)
      {
        var len = Math.Min(pushChunkSize, reference.Length - pushOffset);
        generator.AddSamples(reference.AsSpan(pushOffset, len));
        pushOffset += len;
      }
      else if (pullOffset < fullOutput.Length)
      {
        generator.AddSamples(new float[pushChunkSize]);
      }

      // Pull equivalent amount in mixer-sized chunks
      var toPull = Math.Min(pushChunkSize, fullOutput.Length - pullOffset);
      while (toPull > 0 && pullOffset < fullOutput.Length)
      {
        var pullSize = Math.Min(pullChunkSize, toPull);
        pullSize = Math.Min(pullSize, fullOutput.Length - pullOffset);
        var chunk = generator.PullAudio(pullSize);
        Array.Copy(chunk, 0, fullOutput, pullOffset, pullSize);
        pullOffset += pullSize;
        toPull -= pullSize;
      }
    }

    // Trim the leading prefill silence; the signal lives in the trailing
    // `reference.Length` samples.
    var output = new float[reference.Length];
    Array.Copy(fullOutput, prefillSamples, output, 0, reference.Length);

    var report = WaveformComparison.Compare(reference, output);

    _output.WriteLine($"[{label}] Report: {report}");
    Assert.True(report.IsClean, $"[{label}] Expected clean, got: {report}");
    Assert.True(report.CorrelationCoefficient > 0.999f);
  }

  [Fact]
  public void KnownDistortion_RepeatedSamples_Detected()
  {
    var format = new AudioFormat
    {
      SampleRate = SampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    var reference = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, durationSamples: SampleRate / 2, amplitude: 0.8f);

    // Inject distortion: repeat a non-zero sample value 50 times
    var distorted = (float[])reference.Clone();
    var repeatedVal = 0.42f;
    for (int i = 10000; i < 10050; i++)
    {
      distorted[i] = repeatedVal;
    }

    generator.AddSamples(distorted);
    var output = generator.PullAudio(distorted.Length);

    var report = WaveformComparison.Compare(reference, output);

    _output.WriteLine($"Report: {report}");
    Assert.False(report.IsClean);
    Assert.Contains(report.Events, e => e.Type == DistortionType.RepeatedSamples);
  }

  [Fact]
  public void KnownDistortion_DroppedSamples_Detected()
  {
    var format = new AudioFormat
    {
      SampleRate = SampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    var reference = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, durationSamples: SampleRate / 2, amplitude: 0.8f);

    // Create a shortened version (dropped samples)
    var dropStart = 10000;
    var dropLength = 200;
    var shortened = new float[reference.Length - dropLength];
    Array.Copy(reference, 0, shortened, 0, dropStart);
    Array.Copy(reference, dropStart + dropLength, shortened, dropStart,
      reference.Length - dropStart - dropLength);

    generator.AddSamples(shortened);
    var output = generator.PullAudio(shortened.Length);

    // Compare shortened output against full reference
    // The correlation should be lower and RMS error higher due to misalignment
    var report = WaveformComparison.Compare(reference, output);

    _output.WriteLine($"Report: {report}");
    Assert.True(report.RmsError > 0.001f, "Dropped samples should produce measurable error");
  }

  [Fact]
  public void KnownDistortion_SilenceInsertion_Detected()
  {
    var format = new AudioFormat
    {
      SampleRate = SampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    var reference = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, durationSamples: SampleRate / 2, amplitude: 0.8f);

    // Inject silence gap
    var distorted = (float[])reference.Clone();
    for (int i = 20000; i < 20100; i++)
    {
      distorted[i] = 0f;
    }

    generator.AddSamples(distorted);
    var output = generator.PullAudio(distorted.Length);

    var report = WaveformComparison.Compare(reference, output);

    _output.WriteLine($"Report: {report}");
    Assert.False(report.IsClean);
    Assert.Contains(report.Events, e => e.Type == DistortionType.SilenceInsertion);
  }

  [Fact]
  public void DiagnosticInputCapture_CapturesAddedSamples()
  {
    var format = new AudioFormat
    {
      SampleRate = SampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    // Wire up diagnostic capture
    var captured = new List<float>();
    generator.DiagnosticInputCapture = (ReadOnlySpan<float> span) =>
    {
      var arr = new float[span.Length];
      span.CopyTo(arr);
      lock (captured) { captured.AddRange(arr); }
    };

    var input = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, durationSamples: 4800, amplitude: 0.8f);
    generator.AddSamples(input);

    // Captured should match input
    Assert.Equal(input.Length, captured.Count);
    for (int i = 0; i < input.Length; i++)
    {
      Assert.Equal(input[i], captured[i]);
    }
  }

  [Fact]
  public void DiagnosticOutputCapture_CapturesOutputSamples()
  {
    var format = new AudioFormat
    {
      SampleRate = SampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    var captured = new List<float>();
    generator.DiagnosticOutputCapture = (ReadOnlySpan<float> span) =>
    {
      var arr = new float[span.Length];
      span.CopyTo(arr);
      lock (captured) { captured.AddRange(arr); }
    };

    var input = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, durationSamples: 4800, amplitude: 0.8f);
    generator.AddSamples(input);
    generator.PullAudio(input.Length);

    // Output capture should have samples
    Assert.True(captured.Count > 0, "Output capture should receive samples");
    Assert.Equal(input.Length, captured.Count);
  }

  [Fact]
  public void NullDiagnosticHooks_ZeroCost()
  {
    var format = new AudioFormat
    {
      SampleRate = SampleRate, Channels = Channels, Format = SampleFormat.F32
    };
    var logger = new Mock<ILogger>();
    var generator = new TestableGenerator(_engineMock.Object, format, logger.Object);

    // Hooks are null by default — this should not throw or allocate
    Assert.Null(generator.DiagnosticInputCapture);
    Assert.Null(generator.DiagnosticOutputCapture);

    var input = WavFileHelper.GenerateStereoSineWave(durationSamples: 4800);
    generator.AddSamples(input);
    var output = generator.PullAudio(input.Length);

    Assert.Equal(input.Length, output.Length);
  }
}
