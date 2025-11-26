namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// Stream that captures mixed audio output for HTTP streaming.
/// Uses a ring buffer for real-time audio capture.
/// </summary>
internal sealed class TappedOutputStream : Stream
{
  private readonly byte[] _buffer;
  private readonly int _bufferSize;
  private readonly int _sampleRate;
  private readonly int _channels;
  private readonly int _bytesPerSample;
  private int _readPosition;
  private int _writePosition;
  private readonly object _lock = new();
  private bool _disposed;

  /// <summary>
  /// Initializes a new instance of the <see cref="TappedOutputStream"/> class.
  /// </summary>
  /// <param name="sampleRate">The sample rate in Hz.</param>
  /// <param name="channels">The number of audio channels.</param>
  /// <param name="bufferSizeSeconds">The buffer size in seconds.</param>
  public TappedOutputStream(int sampleRate = 48000, int channels = 2, int bufferSizeSeconds = 5)
  {
    _sampleRate = sampleRate;
    _channels = channels;
    _bytesPerSample = 2; // 16-bit PCM

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
  /// Gets the number of bytes available to read.
  /// </summary>
  public int Available
  {
    get
    {
      lock (_lock)
      {
        return (_writePosition - _readPosition + _bufferSize) % _bufferSize;
      }
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
  }

  /// <summary>
  /// Clears all data from the ring buffer.
  /// </summary>
  public void Clear()
  {
    lock (_lock)
    {
      _readPosition = 0;
      _writePosition = 0;
      Array.Clear(_buffer, 0, _bufferSize);
    }
  }

  /// <inheritdoc/>
  public override int Read(byte[] buffer, int offset, int count)
  {
    if (_disposed)
      throw new ObjectDisposedException(nameof(TappedOutputStream));

    lock (_lock)
    {
      var available = (_writePosition - _readPosition + _bufferSize) % _bufferSize;
      var toRead = Math.Min(count, available);

      for (var i = 0; i < toRead; i++)
      {
        buffer[offset + i] = _buffer[_readPosition];
        _readPosition = (_readPosition + 1) % _bufferSize;
      }

      return toRead;
    }
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
    throw new NotSupportedException("Use WriteFromEngine instead");

  /// <inheritdoc/>
  protected override void Dispose(bool disposing)
  {
    _disposed = true;
    base.Dispose(disposing);
  }
}
