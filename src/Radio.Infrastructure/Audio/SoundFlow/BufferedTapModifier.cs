using SoundFlow.Abstracts;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// Abstract base class for passthrough audio modifiers that buffer samples
/// and periodically flush them to a consumer on the ThreadPool.
/// Subclasses implement <see cref="ProcessFlushBuffer"/> and <see cref="FlushRemaining"/>
/// to define what happens with the buffered samples.
/// </summary>
public abstract class BufferedTapModifier : SoundModifier
{
  private readonly float[] _sampleBuffer;
  private readonly int _bufferSize;
  private int _bufferIndex;
  private readonly object _lock = new();

  // AUD-79: full batches are handed to the ThreadPool through a queue of pooled buffers, drained in
  // order by one work item at a time. This used to be a single flush buffer plus an in-progress flag,
  // and a batch that filled while the previous flush had not yet RUN was discarded outright — so the
  // pool had 21 ms (2,048 samples at 48 kHz stereo) to schedule every flush or that audio was lost.
  // Measured on the appliance 2026-09-29 with Bluetooth playing: the tap received 0.967x real time,
  // i.e. one batch in thirty dropped. Now a batch is dropped only when every pooled buffer is still in
  // flight (a stall of PoolSize x 21 ms), and the drop is counted.
  private const int PoolSize = 16;
  private readonly System.Collections.Concurrent.ConcurrentQueue<float[]> _free = new();
  private readonly System.Collections.Concurrent.ConcurrentQueue<float[]> _pending = new();
  private int _drainScheduled;
  private long _droppedBatches;

  // Pre-allocated work item to avoid closure allocation on every flush
  private readonly FlushWorkItem _flushWorkItem;

  /// <summary>
  /// Pre-allocated IThreadPoolWorkItem that drains the pending queue in order. Only one runs at a time
  /// (the <c>_drainScheduled</c> flag), so <see cref="ProcessFlushBuffer"/> calls never overlap.
  /// </summary>
  private sealed class FlushWorkItem : IThreadPoolWorkItem
  {
    private readonly BufferedTapModifier _owner;
    public FlushWorkItem(BufferedTapModifier owner) => _owner = owner;
    public void Execute() => _owner.DrainPending();
  }

  protected BufferedTapModifier(int bufferSize)
  {
    _bufferSize = bufferSize;
    _sampleBuffer = new float[bufferSize];
    _bufferIndex = 0;
    _flushWorkItem = new FlushWorkItem(this);
    for (var i = 0; i < PoolSize; i++)
    {
      _free.Enqueue(new float[bufferSize]);
    }
  }

  /// <summary>
  /// Batches discarded because every pooled flush buffer was still waiting to be processed (AUD-79).
  /// Should stay at zero; a rising count means the consumer is stalling for hundreds of milliseconds.
  /// </summary>
  public long DroppedBatches => Interlocked.Read(ref _droppedBatches);

  private void DrainPending()
  {
    while (true)
    {
      while (_pending.TryDequeue(out var batch))
      {
        try
        {
          ProcessFlushBuffer(batch);
        }
        catch (Exception ex)
        {
          OnFlushError(ex);
        }
        finally
        {
          _free.Enqueue(batch);
        }
      }

      Volatile.Write(ref _drainScheduled, 0);

      // A batch enqueued after the inner loop emptied but before the flag cleared would otherwise wait
      // for the next one to arrive. Re-claim the drain if so.
      if (_pending.IsEmpty || Interlocked.CompareExchange(ref _drainScheduled, 1, 0) != 0)
      {
        return;
      }
    }
  }

  /// <summary>
  /// Gets the configured buffer size.
  /// </summary>
  protected int BufferSize => _bufferSize;

  /// <inheritdoc/>
  public override float ProcessSample(float sample, int channel)
  {
    // Hot path: called 96,000 times/second for stereo 48kHz.
    // Lock-free sample buffering via atomic index increment.
    // Only lock briefly for the copy into a pooled flush batch.
    var index = Interlocked.Increment(ref _bufferIndex) - 1;
    if (index < _bufferSize)
    {
      _sampleBuffer[index] = sample;
      OnSampleBuffered();
    }

    if (index == _bufferSize - 1)
    {
      if (_free.TryDequeue(out var batch))
      {
        lock (_lock)
        {
          Array.Copy(_sampleBuffer, batch, _bufferSize);
        }
        _pending.Enqueue(batch);
        if (Interlocked.CompareExchange(ref _drainScheduled, 1, 0) == 0)
        {
          ThreadPool.UnsafeQueueUserWorkItem(_flushWorkItem, preferLocal: false);
        }
      }
      else
      {
        Interlocked.Increment(ref _droppedBatches);
      }
      Volatile.Write(ref _bufferIndex, 0);
    }

    // Pass through unchanged — this is a tap, not an effect
    return sample;
  }

  /// <summary>
  /// Flushes any remaining samples in the buffer.
  /// </summary>
  public void Flush()
  {
    lock (_lock)
    {
      var currentIndex = Volatile.Read(ref _bufferIndex);
      if (currentIndex > 0)
      {
        try
        {
          var remainingSamples = new float[currentIndex];
          Array.Copy(_sampleBuffer, remainingSamples, currentIndex);
          FlushRemaining(remainingSamples);
        }
        catch
        {
          // Ignore flush errors
        }

        Volatile.Write(ref _bufferIndex, 0);
      }
    }
  }

  /// <summary>
  /// Resets the sample buffer.
  /// </summary>
  public void Reset()
  {
    lock (_lock)
    {
      Volatile.Write(ref _bufferIndex, 0);
      Array.Clear(_sampleBuffer, 0, _bufferSize);
    }
  }

  /// <summary>
  /// Called on the ThreadPool when the buffer is full. Subclasses implement
  /// the actual work (writing to output tap, sending to visualizer, etc.).
  /// </summary>
  protected abstract void ProcessFlushBuffer(float[] buffer);

  /// <summary>
  /// Called during <see cref="Flush"/> with the remaining partial buffer.
  /// Defaults to calling <see cref="ProcessFlushBuffer"/>.
  /// </summary>
  protected virtual void FlushRemaining(float[] remainingSamples)
  {
    ProcessFlushBuffer(remainingSamples);
  }

  /// <summary>
  /// Called on every sample inside the lock. Override for per-sample bookkeeping.
  /// Default is a no-op.
  /// </summary>
  protected virtual void OnSampleBuffered() { }

  /// <summary>
  /// Called when <see cref="ProcessFlushBuffer"/> throws. Override for error tracking.
  /// </summary>
  protected virtual void OnFlushError(Exception ex) { }
}
