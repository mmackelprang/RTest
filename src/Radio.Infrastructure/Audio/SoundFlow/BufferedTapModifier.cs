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

  protected BufferedTapModifier(int bufferSize)
  {
    _bufferSize = bufferSize;
    _sampleBuffer = new float[bufferSize];
    _flushBuffer = new float[bufferSize];
    _bufferIndex = 0;
  }

  /// <summary>
  /// Gets the configured buffer size.
  /// </summary>
  protected int BufferSize => _bufferSize;

  /// <inheritdoc/>
  public override float ProcessSample(float sample, int channel)
  {
    // Hot path: called 96,000 times/second for stereo 48kHz.
    // Only buffer samples on the audio thread — heavy processing
    // is offloaded to ThreadPool via ProcessFlushBuffer.
    bool shouldFlush = false;
    lock (_lock)
    {
      OnSampleBuffered();

      if (_bufferIndex < _bufferSize)
      {
        _sampleBuffer[_bufferIndex++] = sample;
      }

      if (_bufferIndex >= _bufferSize)
      {
        if (!_flushInProgress)
        {
          Array.Copy(_sampleBuffer, _flushBuffer, _bufferSize);
          shouldFlush = true;
        }
        _bufferIndex = 0;
      }
    }

    if (shouldFlush)
    {
      _flushInProgress = true;
      ThreadPool.QueueUserWorkItem(_ =>
      {
        try
        {
          ProcessFlushBuffer(_flushBuffer);
        }
        catch (Exception ex)
        {
          OnFlushError(ex);
        }
        finally
        {
          _flushInProgress = false;
        }
      });
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
      if (_bufferIndex > 0)
      {
        try
        {
          var remainingSamples = new float[_bufferIndex];
          Array.Copy(_sampleBuffer, remainingSamples, _bufferIndex);
          FlushRemaining(remainingSamples);
        }
        catch
        {
          // Ignore flush errors
        }

        _bufferIndex = 0;
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
      _bufferIndex = 0;
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
