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
  private readonly float[] _flushBuffer;
  private readonly int _bufferSize;
  private int _bufferIndex;
  private readonly object _lock = new();
  private volatile bool _flushInProgress;

  // Pre-allocated work item to avoid closure allocation on every flush
  private readonly FlushWorkItem _flushWorkItem;

  /// <summary>
  /// Pre-allocated IThreadPoolWorkItem to avoid closure allocation (~94/sec)
  /// when queueing flush work to the ThreadPool.
  /// </summary>
  private sealed class FlushWorkItem : IThreadPoolWorkItem
  {
    private readonly BufferedTapModifier _owner;
    public FlushWorkItem(BufferedTapModifier owner) => _owner = owner;
    public void Execute()
    {
      try
      {
        _owner.ProcessFlushBuffer(_owner._flushBuffer);
      }
      catch (Exception ex)
      {
        _owner.OnFlushError(ex);
      }
      finally
      {
        _owner._flushInProgress = false;
      }
    }
  }

  protected BufferedTapModifier(int bufferSize)
  {
    _bufferSize = bufferSize;
    _sampleBuffer = new float[bufferSize];
    _flushBuffer = new float[bufferSize];
    _bufferIndex = 0;
    _flushWorkItem = new FlushWorkItem(this);
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
    // Only lock briefly for the flush copy to _flushBuffer.
    var index = Interlocked.Increment(ref _bufferIndex) - 1;
    if (index < _bufferSize)
    {
      _sampleBuffer[index] = sample;
      OnSampleBuffered();
    }

    if (index == _bufferSize - 1)
    {
      if (!_flushInProgress)
      {
        lock (_lock)
        {
          Array.Copy(_sampleBuffer, _flushBuffer, _bufferSize);
        }
        _flushInProgress = true;
        ThreadPool.UnsafeQueueUserWorkItem(_flushWorkItem, preferLocal: false);
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
