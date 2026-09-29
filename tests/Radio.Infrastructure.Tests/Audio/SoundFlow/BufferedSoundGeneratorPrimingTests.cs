using Microsoft.Extensions.Logging;
using Moq;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Metrics;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Structs;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// AUD-15: jitter-buffer priming. Playback is held — silence out, nothing consumed — until the target of
/// REAL audio is buffered, and re-held after a genuine run-dry. Off by default.
/// </summary>
public class BufferedSoundGeneratorPrimingTests
{
  // 0.001 s × 48 kHz × 2 ch = 96 interleaved samples.
  private const float PrimeSeconds = 0.001f;
  private const int PrimeSamples = 96;

  private static readonly AudioFormat TestFormat = new() { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 };

  private sealed class TestGenerator : BufferedSoundGenerator<float>
  {
    public TestGenerator(IMetricsCollector? metrics = null)
      : base(new Mock<AudioEngine>().Object, TestFormat, new Mock<ILogger>().Object, metricsCollector: metrics) { }

    public float[] Pull(int samples)
    {
      var buffer = new float[samples];
      GenerateAudio(buffer, TestFormat.Channels);
      return buffer;
    }
  }

  private static float[] Ones(int n) => Enumerable.Repeat(1f, n).ToArray();

  [Fact]
  public void WhilePriming_OutputsSilence_AndConsumesNothing()
  {
    var generator = new TestGenerator();
    generator.ConfigurePriming(PrimeSeconds);
    generator.AddSamples(Ones(PrimeSamples - 2));

    var output = generator.Pull(8);

    Assert.All(output, s => Assert.Equal(0f, s));
    Assert.Null(generator.LevelWhilePlaying);
    Assert.Equal(PrimeSamples - 2, generator.BufferedSamplesForTest());
  }

  [Fact]
  public void OnceTheTargetIsBuffered_PlaysRealAudio()
  {
    var generator = new TestGenerator();
    generator.ConfigurePriming(PrimeSeconds);
    generator.AddSamples(Ones(PrimeSamples));

    var output = generator.Pull(8);

    Assert.All(output, s => Assert.Equal(1f, s));
    Assert.Equal(PrimeSamples - 8, generator.LevelWhilePlaying);
    Assert.Equal(1, generator.PrimeCount);
  }

  [Fact]
  public void AfterARunDry_RearmsAndHoldsUntilTheTargetAgain()
  {
    var generator = new TestGenerator();
    generator.ConfigurePriming(PrimeSeconds);
    generator.AddSamples(Ones(PrimeSamples));
    generator.Pull(PrimeSamples);       // plays the buffer out exactly
    generator.Pull(8);                  // runs dry: re-arms

    generator.AddSamples(Ones(PrimeSamples / 2));
    var held = generator.Pull(8);

    Assert.All(held, s => Assert.Equal(0f, s));
    Assert.Null(generator.LevelWhilePlaying);

    generator.AddSamples(Ones(PrimeSamples / 2));
    var playing = generator.Pull(8);

    Assert.All(playing, s => Assert.Equal(1f, s));
    Assert.Equal(2, generator.PrimeCount);
  }

  [Fact]
  public void APrimingHold_IsNotCountedAsAnUnderrun()
  {
    var metrics = new Mock<IMetricsCollector>();
    var generator = new TestGenerator(metrics.Object);
    generator.ConfigurePriming(PrimeSeconds);
    generator.AddSamples(Ones(4));      // received > 0, still below target

    generator.Pull(8);
    generator.Pull(8);

    metrics.Verify(
      m => m.Increment("audio.buffer.underrun_total", It.IsAny<long>(), It.IsAny<IDictionary<string, string>>()),
      Times.Never);
  }

  [Fact]
  public void TheRunDryItself_IsStillCountedAsAnUnderrun()
  {
    var metrics = new Mock<IMetricsCollector>();
    var generator = new TestGenerator(metrics.Object);
    generator.ConfigurePriming(PrimeSeconds);
    generator.AddSamples(Ones(PrimeSamples));

    generator.Pull(PrimeSamples + 8);   // plays 96, then 8 short: a real underrun

    metrics.Verify(
      m => m.Increment("audio.buffer.underrun_total", 1, It.IsAny<IDictionary<string, string>>()),
      Times.Once);
  }

  [Fact]
  public void RearmPriming_DiscardsBufferedAudio_AndHolds()
  {
    var generator = new TestGenerator();
    generator.ConfigurePriming(PrimeSeconds);
    generator.AddSamples(Ones(PrimeSamples * 2));
    generator.Pull(8);

    generator.RearmPriming();

    Assert.All(generator.Pull(8), s => Assert.Equal(0f, s));
    Assert.Equal(0, generator.BufferedSamplesForTest());
  }

  [Fact]
  public void WithoutConfigurePriming_BehaviourIsUnchanged()
  {
    var generator = new TestGenerator();
    generator.AddSamples(Ones(4));

    var output = generator.Pull(8);

    Assert.Equal(new[] { 1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f }, output);
    Assert.False(generator.IsPrimingEnabled);
    Assert.Equal(0, generator.LevelWhilePlaying);
  }
}

internal static class BufferedSoundGeneratorTestExtensions
{
  /// <summary>The buffered count regardless of priming state (LevelWhilePlaying hides it while priming).</summary>
  public static int BufferedSamplesForTest(this BufferedSoundGenerator<float> generator)
  {
    var field = typeof(BufferedSoundGenerator<float>).GetField(
      "_count", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    return (int)field.GetValue(generator)!;
  }
}
