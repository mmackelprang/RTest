using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Outputs;

/// <summary>
/// AUD-54 (5): <see cref="DirectCastStreamingService"/> must release its tapped-output reader
/// and cancellation source on stop even when the streaming loop has already ended on its own.
/// </summary>
/// <remarks>
/// The loop is ended by a reader whose read throws, and the test waits on the loop's own task
/// (<c>StreamingTaskForTests</c>) — no sleeps. The 10 s waits are hang guards only.
/// </remarks>
public class DirectCastStreamingServiceTests
{
  private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(10);

  [Fact]
  public async Task Stop_AfterTheLoopDiedOnItsOwn_StillReleasesTheReader()
  {
    var reader = new ThrowingReader();
    var service = BuildService(reader);

    service.Start();
    await service.StreamingTaskForTests!.WaitAsync(HangGuard);

    // Precondition: the loop is gone and nothing has released the reader yet.
    Assert.False(service.IsStreaming);
    Assert.False(reader.Disposed);

    await service.StopAsync();

    Assert.True(reader.Disposed);
  }

  [Fact]
  public async Task Dispose_AfterTheLoopDiedOnItsOwn_ReleasesTheReaderOnce()
  {
    var reader = new ThrowingReader();
    var service = BuildService(reader);

    service.Start();
    await service.StreamingTaskForTests!.WaitAsync(HangGuard);

    await service.StopAsync();
    await service.DisposeAsync();
    await service.StopAsync();

    Assert.Equal(1, reader.DisposeCount);
  }

  private static DirectCastStreamingService BuildService(Stream reader)
  {
    var engine = new Mock<IAudioEngine>();
    engine.Setup(e => e.CreateStreamReader(It.IsAny<string>(), It.IsAny<double?>())).Returns(reader);

    // The channel has no client, so the config send fails and is logged; the read then throws
    // and the loop's catch-all ends it — the "died on its own" path.
    var service = new DirectCastStreamingService(
      NullLogger.Instance,
      engine.Object,
      new DirectCastAudioChannel("urn:x-cast:test.audio", NullLogger.Instance),
      new GoogleCastOutputOptions());
    service.SetTransportId("transport-1");
    return service;
  }

  /// <summary>A reader whose first read throws; records disposal.</summary>
  private sealed class ThrowingReader : Stream
  {
    private int _disposeCount;

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public bool Disposed => DisposeCount > 0;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) =>
      throw new InvalidOperationException("reader broke");

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
      Task.FromException<int>(new InvalidOperationException("reader broke"));

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
      Interlocked.Increment(ref _disposeCount);
      base.Dispose(disposing);
    }
  }
}
