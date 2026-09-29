using Microsoft.Extensions.Time.Testing;
using Radio.Infrastructure.Audio.SoundFlow;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// AUD-79: a reader that has caught up with a writer that is still writing waits for the next block.
/// It used to be handed 1,024 frames of zeros without its position advancing, so the silence was
/// spliced into the stream — Cast audio alternated 21 ms of music with 21 ms of silence. Keep-alive
/// silence is for a paused source only: the writer idle for <see cref="TappedOutputStream.KeepAliveAfterIdle"/>.
/// </summary>
public class TappedOutputStreamKeepAliveTests
{
  private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 20, 0, 0, TimeSpan.Zero));

  private static float[] Block(float value, int samples = 2048) => Enumerable.Repeat(value, samples).ToArray();

  [Fact]
  public async Task ACaughtUpReader_WithAnActiveWriter_WaitsForTheNextBlock_NotSilence()
  {
    using var tap = new TappedOutputStream(48000, 2, 5, null, _time);
    using var reader = tap.CreateReader("test");
    tap.WriteFromEngine(Block(0.5f));
    var buffer = new byte[4096];
    Assert.Equal(4096, await reader.ReadAsync(buffer, 0, buffer.Length));   // drains block A

    var pending = reader.ReadAsync(buffer, 0, buffer.Length);                // caught up, writer active
    await Task.Delay(50);
    Assert.False(pending.IsCompleted, "a caught-up reader must wait, not return spliced silence");

    tap.WriteFromEngine(Block(0.25f));
    var read = await pending.WaitAsync(TimeSpan.FromSeconds(10));

    Assert.Equal(4096, read);
    Assert.Contains(buffer, b => b != 0);
  }

  [Fact]
  public async Task OnceTheWriterHasBeenIdle_KeepAliveSilenceIsReturned()
  {
    using var tap = new TappedOutputStream(48000, 2, 5, null, _time);
    using var reader = tap.CreateReader("test");
    tap.WriteFromEngine(Block(0.5f));
    var buffer = new byte[4096];
    Assert.Equal(4096, await reader.ReadAsync(buffer, 0, buffer.Length));

    _time.Advance(TappedOutputStream.KeepAliveAfterIdle + TimeSpan.FromMilliseconds(1));
    var read = await reader.ReadAsync(buffer, 0, buffer.Length).WaitAsync(TimeSpan.FromSeconds(10));

    Assert.Equal(4096, read);
    Assert.All(buffer, b => Assert.Equal(0, b));
  }
}
