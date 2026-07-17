using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.DependencyInjection;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Fingerprinting;

/// <summary>
/// Tests for <see cref="SoundFlowAudioTap"/> buffer reuse. The tap previously
/// allocated ~7 MB on the Large Object Heap every fingerprint cycle (~15 s):
/// a byte scratch buffer rented from the shared ArrayPool (which no-ops above
/// ~1 MB, so it allocated+discarded) plus a fresh float[] for the samples. That
/// periodic churn triggered a GC pause which starved the capture-fill thread and
/// caused Cast-output audio to garble (source ring buffer draining to 0/384000).
///
/// These tests pin the reuse behaviour: across successive steady-stream captures
/// the tap must return the SAME float[] instance (no re-allocation), while still
/// producing exactly the captured samples.
/// </summary>
public class SoundFlowAudioTapTests
{
  private readonly Mock<ILogger<SoundFlowAudioTap>> _logger = new();
  private readonly Mock<IAudioEngine> _engine = new();
  private readonly Mock<IAudioManager> _manager = new();

  // 0.05 s at 48 kHz stereo 16-bit = 4800 samples = 9600 bytes.
  private const double CaptureSeconds = 0.05;
  private const int SampleRate = 48000;
  private const int Channels = 2;
  private const int ExpectedSamples = (int)(CaptureSeconds * SampleRate * Channels); // 4800
  private const int ExpectedBytes = ExpectedSamples * 2; // 9600

  // Distinctive non-zero PCM value (8000) so the capture passes the RMS silence
  // check (~-12 dBFS) and every chunk is treated as real audio.
  private const short PcmValue = 8000;

  private static byte[] MakePcm(short value, int byteCount)
  {
    var bytes = new byte[byteCount];
    for (int i = 0; i + 1 < byteCount; i += 2)
    {
      bytes[i] = (byte)(value & 0xFF);
      bytes[i + 1] = (byte)((value >> 8) & 0xFF);
    }
    return bytes;
  }

  private SoundFlowAudioTap CreateActiveTap(Func<byte[]> pcmFactory)
  {
    _engine.SetupGet(e => e.State).Returns(AudioEngineState.Running);
    // A fresh stream per capture — the tap wraps it in `using`.
    _engine
      .Setup(e => e.CreateStreamReader(It.IsAny<string>(), It.IsAny<double?>()))
      .Returns(() => new MemoryStream(pcmFactory()));

    var source = new Mock<IAudioSource>();
    source.SetupGet(s => s.State).Returns(AudioSourceState.Playing);
    source.SetupGet(s => s.Type).Returns(AudioSourceType.Radio);
    source.SetupGet(s => s.Name).Returns("TestSource");
    _manager.SetupGet(m => m.ActiveSource).Returns(source.Object);

    return new SoundFlowAudioTap(_logger.Object, _engine.Object, _manager.Object);
  }

  [Fact]
  public async Task CaptureAsync_ReusesSampleBuffer_AcrossSteadyStreamCaptures()
  {
    // Arrange: a steady, non-silent stream so both captures fill to the same length.
    var tap = CreateActiveTap(() => MakePcm(PcmValue, ExpectedBytes));

    // Act
    var first = await tap.CaptureAsync(TimeSpan.FromSeconds(CaptureSeconds));
    var second = await tap.CaptureAsync(TimeSpan.FromSeconds(CaptureSeconds));

    // Assert: both captures succeeded with the exact expected sample count...
    Assert.NotNull(first);
    Assert.NotNull(second);
    Assert.Equal(ExpectedSamples, first!.Samples.Length);
    Assert.Equal(ExpectedSamples, second!.Samples.Length);

    // ...and the second capture reused the SAME float[] (no LOH re-allocation).
    Assert.Same(first.Samples, second.Samples);
  }

  [Fact]
  public async Task CaptureAsync_ReusedBuffer_HoldsCurrentCaptureData_NotStale()
  {
    // First capture: value A. Second capture: value B (same length so buffer reuses).
    const short valueA = 8000;
    const short valueB = -8000;
    short current = valueA;
    // ReSharper disable once AccessToModifiedClosure — deliberate: switch the stream
    // contents between captures to prove the reused buffer is overwritten.
    var tap = CreateActiveTap(() => MakePcm(current, ExpectedBytes));

    var first = await tap.CaptureAsync(TimeSpan.FromSeconds(CaptureSeconds));
    current = valueB;
    var second = await tap.CaptureAsync(TimeSpan.FromSeconds(CaptureSeconds));

    Assert.NotNull(first);
    Assert.NotNull(second);

    // Same backing array (reuse), but the contents reflect the SECOND capture —
    // proving every element is freshly written and no stale data from the first
    // capture leaks through.
    Assert.Same(first!.Samples, second!.Samples);
    var expectedB = valueB / (float)short.MaxValue;
    Assert.All(second.Samples, s => Assert.Equal(expectedB, s, 0.0001f));
  }

  [Fact]
  public async Task CaptureAsync_AllocatesExactLength_WhenCaptureSizeChanges()
  {
    // A shorter (silence-truncated) capture must not reuse an oversized buffer:
    // Samples.Length is load-bearing (SongRec writes exactly that many samples).
    var longPcm = MakePcm(PcmValue, ExpectedBytes);
    var shortPcm = MakePcm(PcmValue, ExpectedBytes / 2);
    var streams = new Queue<byte[]>(new[] { longPcm, shortPcm });
    var tap = CreateActiveTap(() => streams.Dequeue());

    var first = await tap.CaptureAsync(TimeSpan.FromSeconds(CaptureSeconds));
    var second = await tap.CaptureAsync(TimeSpan.FromSeconds(CaptureSeconds));

    Assert.NotNull(first);
    Assert.NotNull(second);
    Assert.Equal(ExpectedSamples, first!.Samples.Length);
    // Second capture read fewer bytes → exact, smaller length (not the reused 4800).
    Assert.Equal(ExpectedSamples / 2, second!.Samples.Length);
    Assert.NotSame(first.Samples, second.Samples);
  }

  [Fact]
  public void AddFingerprinting_RegistersAudioTap_AsSingleton()
  {
    // Regression guard: the buffer reuse above only reduces LOH churn if the SAME
    // tap instance survives across identification cycles. BackgroundIdentificationService
    // resolves IAudioSampleProvider from a fresh DI scope every cycle, so a scoped or
    // transient registration would hand back a new tap (empty buffers) each time,
    // silently defeating the fix. Pin the production registration lifetime to Singleton.
    var services = new ServiceCollection();
    var config = new ConfigurationBuilder().Build();

    services.AddFingerprinting(config);

    var descriptor = services.Single(d => d.ServiceType == typeof(IAudioSampleProvider));
    Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    Assert.Equal(typeof(SoundFlowAudioTap), descriptor.ImplementationType);
  }
}
