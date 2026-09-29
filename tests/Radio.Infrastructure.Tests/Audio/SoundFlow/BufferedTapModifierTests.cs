using Radio.Infrastructure.Audio.SoundFlow;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// AUD-79: a full batch is never discarded just because the previous flush has not run yet. It used to
/// be: one flush buffer and an in-progress flag, so the ThreadPool had 21 ms to run each flush or the
/// batch was dropped — 3.3% of the Cast tap's audio on the appliance while Bluetooth played.
/// </summary>
/// <remarks>
/// The consumer is parked on a gate so every batch is produced while the first flush is still "in
/// progress"; deliveries are counted, not timed (CLAUDE.md, Test Timing).
/// </remarks>
public class BufferedTapModifierTests
{
  private const int BatchSize = 8;

  private sealed class GatedTap : BufferedTapModifier
  {
    public readonly ManualResetEventSlim Gate = new(false);
    public readonly List<float[]> Delivered = new();
    public readonly SemaphoreSlim DeliveredSignal = new(0);

    public GatedTap() : base(BatchSize) { }

    protected override void ProcessFlushBuffer(float[] buffer)
    {
      Gate.Wait(TimeSpan.FromSeconds(10));
      lock (Delivered)
      {
        Delivered.Add((float[])buffer.Clone());
      }
      DeliveredSignal.Release();
    }
  }

  private static void FeedBatch(GatedTap tap, float value)
  {
    for (var i = 0; i < BatchSize; i++)
    {
      tap.ProcessSample(value, i % 2);
    }
  }

  private static void AwaitDeliveries(GatedTap tap, int count)
  {
    for (var i = 0; i < count; i++)
    {
      Assert.True(tap.DeliveredSignal.Wait(TimeSpan.FromSeconds(10)), $"only {i} of {count} batches delivered");
    }
  }

  [Fact]
  public void BatchesProducedWhileAFlushIsStalled_AreAllDelivered_InOrder()
  {
    var tap = new GatedTap();

    for (var b = 1; b <= 5; b++)
    {
      FeedBatch(tap, b);
    }
    tap.Gate.Set();
    AwaitDeliveries(tap, 5);

    Assert.Equal(new[] { 1f, 2f, 3f, 4f, 5f }, tap.Delivered.Select(d => d[0]));
    Assert.All(tap.Delivered, d => Assert.All(d, v => Assert.Equal(d[0], v)));
    Assert.Equal(0, tap.DroppedBatches);
  }

  [Fact]
  public void OnlyWhenEveryPooledBufferIsInFlight_IsABatchDropped_AndCounted()
  {
    var tap = new GatedTap();

    // 16 pooled buffers: one is taken by the stalled flush, 15 wait in the queue, the rest have none.
    for (var b = 1; b <= 20; b++)
    {
      FeedBatch(tap, b);
    }
    tap.Gate.Set();
    AwaitDeliveries(tap, 16);

    Assert.Equal(Enumerable.Range(1, 16).Select(i => (float)i), tap.Delivered.Select(d => d[0]));
    Assert.Equal(4, tap.DroppedBatches);
  }
}
