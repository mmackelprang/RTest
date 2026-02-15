using System.Collections.Concurrent;
using Radio.Core.Interfaces;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// Stream that captures mixed audio output for HTTP streaming and fingerprinting.
/// Uses a ring buffer for real-time audio capture with support for multiple
/// independent readers via <see cref="CreateReader"/>.
/// </summary>
internal sealed class TappedOutputStream : Stream
{
  private readonly byte[] _buffer;
  private readonly int _bufferSize;
  private readonly int _sampleRate;
  private readonly int _channels;
  private readonly int _bytesPerSample;
  private readonly IMetricsCollector? _metricsCollector;
  private int _writePosition;
  private readonly object _lock = new();
  private bool _disposed;

  // Diagnostic counters
  private long _totalBytesWritten;
  private long _totalWriteCalls;
  private DateTime _lastWriteTime;

  // Metrics tracking
  private DateTime _lastMetricsTime = DateTime.MinValue;
  private long _lastMetricsBytesWritten;

  // Per-reader tracking: each reader has its own read position
  private readonly ConcurrentDictionary<string, int> _readerPositions = new();

  /// <summary>
  /// Initializes a new instance of the <see cref="TappedOutputStream"/> class.
  /// </summary>
  /// <param name="sampleRate">The sample rate in Hz.</param>
  /// <param name="channels">The number of audio channels.</param>
  /// <param name="bufferSizeSeconds">The buffer size in seconds.</param>
  /// <param name="metricsCollector">Optional metrics collector for pipeline metrics.</param>
  public TappedOutputStream(int sampleRate = 48000, int channels = 2, int bufferSizeSeconds = 5,
    IMetricsCollector? metricsCollector = null)
  {
    _sampleRate = sampleRate;
    _channels = channels;
    _bytesPerSample = 2; // 16-bit PCM
    _metricsCollector = metricsCollector;

    // Calculate buffer size: sampleRate * channels * bytesPerSample * seconds
    _bufferSize = sampleRate * channels * _bytesPerSample * bufferSizeSeconds;
    _buffer = new byte[_bufferSize];
  }

  /// <summary>
  /// Gets the sample rate of the output stream.
  /// </summary>
  public int SampleRate => _sampleRate;

  /// <summary>
  /// Gets the number of audio channels.
  /// </summary>
  public int Channels => _channels;

  /// <summary>
  /// Gets the number of bytes available to read (legacy single-reader path).
  /// Returns 0 since reads should go through <see cref="CreateReader"/>.
  /// </summary>
  public int Available
  {
    get
    {
      lock (_lock)
      {
        return 0;
      }
    }
  }

  /// <summary>
  /// Gets the number of bytes available to read for a specific reader.
  /// </summary>
  /// <param name="readerId">The reader identifier.</param>
  /// <returns>Number of bytes available, or 0 if reader not found.</returns>
  public int GetAvailableForReader(string readerId)
  {
    lock (_lock)
    {
      if (!_readerPositions.TryGetValue(readerId, out var readPos))
        return 0;
      return (_writePosition - readPos + _bufferSize) % _bufferSize;
    }
  }

  /// <summary>
  /// Creates an independent reader stream with its own read cursor.
  /// Each reader receives all data written after creation, independent of other readers.
  /// </summary>
  /// <param name="readerId">A unique identifier for this reader.</param>
  /// <param name="lagBytes">Number of bytes to start behind the write position,
  /// giving the reader immediate access to recently written audio data.
  /// Clamped to the lesser of total bytes written and buffer capacity.
  /// Use 0 (default) for real-time only (no historical data).</param>
  /// <returns>A <see cref="TappedOutputStreamReader"/> with an independent read position.</returns>
  public TappedOutputStreamReader CreateReader(string readerId, int lagBytes = 0)
  {
    lock (_lock)
    {
      if (lagBytes > 0)
      {
        // Clamp lag to what's actually available in the ring buffer
        var maxLag = (int)Math.Min(_totalBytesWritten, _bufferSize - 1);
        var actualLag = Math.Min(lagBytes, maxLag);
        _readerPositions[readerId] = (_writePosition - actualLag + _bufferSize) % _bufferSize;
      }
      else
      {
        // Start reading from current write position (only new data)
        _readerPositions[readerId] = _writePosition;
      }
    }
    return new TappedOutputStreamReader(this, readerId);
  }

  /// <summary>
  /// Removes a reader's tracked position. Called when a reader is disposed.
  /// </summary>
  /// <param name="readerId">The reader identifier to remove.</param>
  internal void RemoveReader(string readerId)
  {
    _readerPositions.TryRemove(readerId, out _);
  }

  /// <summary>
  /// Reads data for a specific reader, advancing only that reader's position.
  /// When no data is available, returns PCM silence to keep HTTP streams alive
  /// during source pause (prevents Cast device timeout/disconnect).
  /// </summary>
  internal int ReadForReader(string readerId, byte[] buffer, int offset, int count)
  {
    if (_disposed)
      throw new ObjectDisposedException(nameof(TappedOutputStream));

    lock (_lock)
    {
      if (!_readerPositions.TryGetValue(readerId, out var readPos))
        return 0;

      var available = (_writePosition - readPos + _bufferSize) % _bufferSize;

      if (available == 0)
      {
        // No new audio data — return silence (zeroed PCM) to keep HTTP streams
        // alive during source pause. The reader position is NOT advanced so
        // real audio data will be read immediately when the source resumes.
        // Limit to ~21ms of audio per call (1024 stereo 16-bit samples = 4096 bytes)
        // to approximate real-time rate.
        var silenceBytes = Math.Min(count, 4096);
        Array.Clear(buffer, offset, silenceBytes);
        return silenceBytes;
      }

      var toRead = Math.Min(count, available);

      for (var i = 0; i < toRead; i++)
      {
        buffer[offset + i] = _buffer[readPos];
        readPos = (readPos + 1) % _bufferSize;
      }

      _readerPositions[readerId] = readPos;
      return toRead;
    }
  }

  /// <summary>
  /// Writes audio samples from the engine to the ring buffer.
  /// Converts float samples (-1.0 to 1.0) to 16-bit PCM.
  /// </summary>
  /// <param name="samples">The float samples to write.</param>
  public void WriteFromEngine(float[] samples)
  {
    if (_disposed) return;

    lock (_lock)
    {
      _totalWriteCalls++;
      _totalBytesWritten += samples.Length * _bytesPerSample;
      _lastWriteTime = DateTime.UtcNow;

      foreach (var sample in samples)
      {
        // Clamp and convert float to 16-bit PCM
        var clampedSample = Math.Clamp(sample, -1f, 1f);
        var pcm = (short)(clampedSample * short.MaxValue);

        // Write low byte then high byte (little-endian)
        _buffer[_writePosition] = (byte)(pcm & 0xFF);
        _writePosition = (_writePosition + 1) % _bufferSize;

        _buffer[_writePosition] = (byte)((pcm >> 8) & 0xFF);
        _writePosition = (_writePosition + 1) % _bufferSize;
      }
    }

    ReportMetrics();
  }

  /// <summary>
  /// Writes audio samples from the engine to the ring buffer.
  /// Converts float samples (-1.0 to 1.0) to 16-bit PCM.
  /// </summary>
  /// <param name="samples">The samples span to write.</param>
  /// <param name="count">The number of samples to write.</param>
  public void WriteFromEngine(Span<float> samples, int count)
  {
    if (_disposed) return;

    lock (_lock)
    {
      _totalWriteCalls++;
      _totalBytesWritten += count * _bytesPerSample;
      _lastWriteTime = DateTime.UtcNow;

      for (var i = 0; i < count; i++)
      {
        // Clamp and convert float to 16-bit PCM
        var clampedSample = Math.Clamp(samples[i], -1f, 1f);
        var pcm = (short)(clampedSample * short.MaxValue);

        // Write low byte then high byte (little-endian)
        _buffer[_writePosition] = (byte)(pcm & 0xFF);
        _writePosition = (_writePosition + 1) % _bufferSize;

        _buffer[_writePosition] = (byte)((pcm >> 8) & 0xFF);
        _writePosition = (_writePosition + 1) % _bufferSize;
      }
    }

    ReportMetrics();
  }

  private void ReportMetrics()
  {
    if (_metricsCollector == null) return;

    var now = DateTime.UtcNow;
    if ((now - _lastMetricsTime).TotalSeconds < 10) return;

    var elapsed = _lastMetricsTime == DateTime.MinValue ? 10.0 : (now - _lastMetricsTime).TotalSeconds;
    var bytesWrittenSinceLast = _totalBytesWritten - _lastMetricsBytesWritten;
    var writeRate = bytesWrittenSinceLast / elapsed;

    _metricsCollector.Gauge("audio.stream.ring_buffer_write_rate", writeRate);
    _metricsCollector.Gauge("audio.stream.active_readers", _readerPositions.Count);

    _lastMetricsTime = now;
    _lastMetricsBytesWritten = _totalBytesWritten;
  }

  /// <summary>
  /// Gets diagnostic information about the output tap.
  /// </summary>
  public OutputTapDiagnostics GetDiagnostics()
  {
    lock (_lock)
    {
      return new OutputTapDiagnostics
      {
        TotalBytesWritten = _totalBytesWritten,
        TotalWriteCalls = _totalWriteCalls,
        LastWriteTime = _lastWriteTime,
        ActiveReaderCount = _readerPositions.Count,
        BufferSize = _bufferSize,
        WritePosition = _writePosition
      };
    }
  }

  /// <summary>
  /// Clears all data from the ring buffer and resets all reader positions.
  /// </summary>
  public void Clear()
  {
    lock (_lock)
    {
      _writePosition = 0;
      Array.Clear(_buffer, 0, _bufferSize);

      // Reset all reader positions to start
      foreach (var key in _readerPositions.Keys)
      {
        _readerPositions[key] = 0;
      }
    }
  }

  /// <inheritdoc/>
  public override int Read(byte[] buffer, int offset, int count)
  {
    // Legacy single-reader path returns 0 — callers should use CreateReader()
    return 0;
  }

  /// <inheritdoc/>
  public override bool CanRead => true;

  /// <inheritdoc/>
  public override bool CanSeek => false;

  /// <inheritdoc/>
  public override bool CanWrite => false;

  /// <inheritdoc/>
  public override long Length => throw new NotSupportedException();

  /// <inheritdoc/>
  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  /// <inheritdoc/>
  public override void Flush()
  {
    // No-op for ring buffer
  }

  /// <inheritdoc/>
  public override long Seek(long offset, SeekOrigin origin) =>
    throw new NotSupportedException();

  /// <inheritdoc/>
  public override void SetLength(long value) =>
    throw new NotSupportedException();

  /// <inheritdoc/>
  public override void Write(byte[] buffer, int offset, int count) =>
    throw new NotSupportedException(
      "Direct writes are not supported. Use WriteFromEngine() to write audio samples to this stream.");

  /// <inheritdoc/>
  protected override void Dispose(bool disposing)
  {
    _disposed = true;
    _readerPositions.Clear();
    base.Dispose(disposing);
  }
}

/// <summary>
/// An independent reader stream for <see cref="TappedOutputStream"/>.
/// Each reader has its own read cursor so multiple consumers can read
/// the same audio data without interfering with each other.
/// </summary>
internal sealed class TappedOutputStreamReader : Stream
{
  private readonly TappedOutputStream _parent;
  private readonly string _readerId;
  private bool _disposed;

  /// <summary>
  /// Initializes a new instance of the <see cref="TappedOutputStreamReader"/> class.
  /// </summary>
  /// <param name="parent">The parent TappedOutputStream.</param>
  /// <param name="readerId">The unique reader identifier.</param>
  internal TappedOutputStreamReader(TappedOutputStream parent, string readerId)
  {
    _parent = parent;
    _readerId = readerId;
  }

  /// <summary>
  /// Gets the unique identifier for this reader.
  /// </summary>
  public string ReaderId => _readerId;

  /// <inheritdoc/>
  public override int Read(byte[] buffer, int offset, int count)
  {
    if (_disposed)
      throw new ObjectDisposedException(nameof(TappedOutputStreamReader));
    return _parent.ReadForReader(_readerId, buffer, offset, count);
  }

  /// <inheritdoc/>
  /// <remarks>
  /// Paces silence reads to approximate real-time rate. Without this,
  /// callers spin a tight loop when no audio data is available because
  /// <see cref="TappedOutputStream.ReadForReader"/> returns non-zero
  /// silence bytes instead of 0.
  /// </remarks>
  public override async Task<int> ReadAsync(
    byte[] buffer, int offset, int count, CancellationToken cancellationToken)
  {
    if (_disposed)
      throw new ObjectDisposedException(nameof(TappedOutputStreamReader));

    var available = _parent.GetAvailableForReader(_readerId);
    var bytesRead = _parent.ReadForReader(_readerId, buffer, offset, count);

    if (available == 0 && bytesRead > 0)
    {
      // Silence was emitted — pace to approximate real-time to prevent
      // tight-loop spinning that causes high CPU and bandwidth waste.
      // At 48kHz stereo 16-bit: 192,000 bytes/sec → 192 bytes/ms
      var bytesPerMs = _parent.SampleRate * _parent.Channels * 2 / 1000;
      var delayMs = bytesPerMs > 0 ? bytesRead / bytesPerMs : 20;
      await Task.Delay(Math.Max(1, delayMs), cancellationToken);
    }

    return bytesRead;
  }

  /// <inheritdoc/>
  public override bool CanRead => true;

  /// <inheritdoc/>
  public override bool CanSeek => false;

  /// <inheritdoc/>
  public override bool CanWrite => false;

  /// <inheritdoc/>
  public override long Length => throw new NotSupportedException();

  /// <inheritdoc/>
  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  /// <inheritdoc/>
  public override void Flush() { }

  /// <inheritdoc/>
  public override long Seek(long offset, SeekOrigin origin) =>
    throw new NotSupportedException();

  /// <inheritdoc/>
  public override void SetLength(long value) =>
    throw new NotSupportedException();

  /// <inheritdoc/>
  public override void Write(byte[] buffer, int offset, int count) =>
    throw new NotSupportedException();

  /// <inheritdoc/>
  protected override void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      _disposed = true;
      _parent.RemoveReader(_readerId);
    }
    base.Dispose(disposing);
  }
}

/// <summary>
/// Diagnostic snapshot of the output tap state.
/// </summary>
public struct OutputTapDiagnostics
{
  public long TotalBytesWritten { get; set; }
  public long TotalWriteCalls { get; set; }
  public DateTime LastWriteTime { get; set; }
  public int ActiveReaderCount { get; set; }
  public int BufferSize { get; set; }
  public int WritePosition { get; set; }
}
