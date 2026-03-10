namespace Radio.Infrastructure.Audio.Diagnostics;

/// <summary>
/// Thread-safe bounded sample accumulator for diagnostic audio capture.
/// Pre-allocates a float[] for the specified duration and accepts samples
/// via AddSamples until stopped or full.
/// </summary>
public class CaptureSession
{
  private readonly float[] _buffer;
  private readonly object _lock = new();
  private int _writePos;
  private volatile bool _isCapturing;
  private readonly string _stageName;
  private readonly int _sampleRate;
  private readonly int _channels;

  /// <summary>
  /// Creates a new capture session with pre-allocated buffer.
  /// </summary>
  /// <param name="stageName">Human-readable name of the capture stage.</param>
  /// <param name="maxDurationSeconds">Maximum capture duration in seconds.</param>
  /// <param name="sampleRate">Audio sample rate.</param>
  /// <param name="channels">Number of audio channels.</param>
  public CaptureSession(string stageName, float maxDurationSeconds,
    int sampleRate = 48000, int channels = 2)
  {
    _stageName = stageName;
    _sampleRate = sampleRate;
    _channels = channels;
    var maxSamples = (int)(maxDurationSeconds * sampleRate * channels);
    _buffer = new float[maxSamples];
  }

  /// <summary>Name of this capture stage.</summary>
  public string StageName => _stageName;

  /// <summary>Number of samples currently captured.</summary>
  public int CapturedSamples
  {
    get { lock (_lock) { return _writePos; } }
  }

  /// <summary>Whether the session is currently accepting samples.</summary>
  public bool IsCapturing => _isCapturing;

  /// <summary>
  /// Starts accepting samples.
  /// </summary>
  public void Start()
  {
    lock (_lock)
    {
      _writePos = 0;
      _isCapturing = true;
    }
  }

  /// <summary>
  /// Stops accepting samples.
  /// </summary>
  public void Stop()
  {
    _isCapturing = false;
  }

  /// <summary>
  /// Adds samples to the capture buffer. Thread-safe.
  /// Samples are dropped if the buffer is full or the session is stopped.
  /// </summary>
  /// <param name="samples">Audio samples to capture.</param>
  public void AddSamples(ReadOnlySpan<float> samples)
  {
    if (!_isCapturing)
    {
      return;
    }

    lock (_lock)
    {
      if (!_isCapturing)
      {
        return;
      }

      var remaining = _buffer.Length - _writePos;
      var toCopy = Math.Min(samples.Length, remaining);

      if (toCopy <= 0)
      {
        _isCapturing = false; // Buffer full — auto-stop
        return;
      }

      samples.Slice(0, toCopy).CopyTo(_buffer.AsSpan(_writePos, toCopy));
      _writePos += toCopy;

      if (_writePos >= _buffer.Length)
      {
        _isCapturing = false; // Buffer full — auto-stop
      }
    }
  }

  /// <summary>
  /// Returns a copy of the captured samples.
  /// </summary>
  public float[] GetSamples()
  {
    lock (_lock)
    {
      var result = new float[_writePos];
      Array.Copy(_buffer, result, _writePos);
      return result;
    }
  }

  /// <summary>
  /// Writes captured samples to a WAV file.
  /// </summary>
  /// <param name="filePath">Output file path.</param>
  public void WriteToFile(string filePath)
  {
    float[] samples;
    lock (_lock)
    {
      samples = new float[_writePos];
      Array.Copy(_buffer, samples, _writePos);
    }

    var dir = Path.GetDirectoryName(filePath);
    if (!string.IsNullOrEmpty(dir))
    {
      Directory.CreateDirectory(dir);
    }

    // Inline WAV write to avoid dependency on Radio.AudioAnalysis
    using var fs = new FileStream(filePath, FileMode.Create);
    using var writer = new BinaryWriter(fs);

    const int bitsPerSample = 16;
    var bytesPerSample = bitsPerSample / 8;
    var dataSize = samples.Length * bytesPerSample;

    // RIFF header
    writer.Write("RIFF"u8);
    writer.Write(36 + dataSize);
    writer.Write("WAVE"u8);

    // Format chunk
    writer.Write("fmt "u8);
    writer.Write(16);
    writer.Write((short)1); // PCM
    writer.Write((short)_channels);
    writer.Write(_sampleRate);
    writer.Write(_sampleRate * _channels * bytesPerSample);
    writer.Write((short)(_channels * bytesPerSample));
    writer.Write((short)bitsPerSample);

    // Data chunk
    writer.Write("data"u8);
    writer.Write(dataSize);

    foreach (var sample in samples)
    {
      var clamped = Math.Clamp(sample, -1f, 1f);
      writer.Write((short)(clamped * short.MaxValue));
    }
  }
}
