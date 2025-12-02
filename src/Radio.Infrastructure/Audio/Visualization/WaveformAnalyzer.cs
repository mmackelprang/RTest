namespace Radio.Infrastructure.Audio.Visualization;

/// <summary>
/// Waveform analyzer that buffers time-domain samples for visualization.
/// Maintains a circular buffer of recent audio samples for display.
/// </summary>
internal sealed class WaveformAnalyzer
{
  private readonly int _sampleCount;
  private readonly int _sampleRate;
  private readonly float[] _leftBuffer;
  private readonly float[] _rightBuffer;
  private int _writePosition;
  private readonly object _lock = new();

  /// <summary>
  /// Gets the number of samples per channel in the buffer.
  /// </summary>
  public int SampleCount => _sampleCount;

  /// <summary>
  /// Gets the sample rate.
  /// </summary>
  public int SampleRate => _sampleRate;

  /// <summary>
  /// Gets the duration of audio in the buffer.
  /// </summary>
  public TimeSpan Duration => TimeSpan.FromSeconds((double)_sampleCount / _sampleRate);

  /// <summary>
  /// Initializes a new instance of the <see cref="WaveformAnalyzer"/> class.
  /// </summary>
  /// <param name="sampleCount">Number of samples per channel to buffer.</param>
  /// <param name="sampleRate">The sample rate in Hz.</param>
  public WaveformAnalyzer(int sampleCount, int sampleRate)
  {
    if (sampleCount <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(sampleCount), "Sample count must be positive");
    }

    if (sampleRate <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive");
    }

    _sampleCount = sampleCount;
    _sampleRate = sampleRate;
    _leftBuffer = new float[sampleCount];
    _rightBuffer = new float[sampleCount];
  }

  /// <summary>
  /// Adds interleaved stereo samples to the buffer.
  /// </summary>
  /// <param name="samples">Interleaved stereo samples (left, right, left, right, ...).</param>
  public void AddSamples(float[] samples)
  {
    AddSamplesCore(samples, samples.Length);
  }

  /// <summary>
  /// Adds interleaved stereo samples to the buffer.
  /// </summary>
  /// <param name="samples">Interleaved stereo samples span.</param>
  /// <param name="count">Number of samples to add.</param>
  public void AddSamples(Span<float> samples, int count)
  {
    AddSamplesCore(samples, count);
  }

  private void AddSamplesCore(ReadOnlySpan<float> samples, int count)
  {
    lock (_lock)
    {
      // Process interleaved stereo samples
      for (var i = 0; i < count - 1; i += 2)
      {
        _leftBuffer[_writePosition] = samples[i];
        _rightBuffer[_writePosition] = samples[i + 1];
        _writePosition = (_writePosition + 1) % _sampleCount;
      }

      // Handle odd sample count (treat last sample as mono for both channels)
      if (count % 2 != 0)
      {
        _leftBuffer[_writePosition] = samples[count - 1];
        _rightBuffer[_writePosition] = samples[count - 1];
        _writePosition = (_writePosition + 1) % _sampleCount;
      }
    }
  }

  /// <summary>
  /// Gets the left channel samples in chronological order.
  /// </summary>
  /// <returns>Array of left channel samples.</returns>
  public float[] GetLeftSamples()
  {
    lock (_lock)
    {
      return GetOrderedSamples(_leftBuffer);
    }
  }

  /// <summary>
  /// Gets the right channel samples in chronological order.
  /// </summary>
  /// <returns>Array of right channel samples.</returns>
  public float[] GetRightSamples()
  {
    lock (_lock)
    {
      return GetOrderedSamples(_rightBuffer);
    }
  }

  /// <summary>
  /// Gets both channel samples as a tuple.
  /// </summary>
  /// <returns>Tuple of left and right channel sample arrays.</returns>
  public (float[] Left, float[] Right) GetSamples()
  {
    lock (_lock)
    {
      return (GetOrderedSamples(_leftBuffer), GetOrderedSamples(_rightBuffer));
    }
  }

  /// <summary>
  /// Gets downsampled waveform data suitable for display.
  /// </summary>
  /// <param name="targetSampleCount">The target number of samples in the output.</param>
  /// <returns>Tuple of downsampled left and right channel arrays.</returns>
  public (float[] Left, float[] Right) GetDownsampledSamples(int targetSampleCount)
  {
    if (targetSampleCount <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(targetSampleCount), "Target sample count must be positive");
    }

    lock (_lock)
    {
      var leftOrdered = GetOrderedSamples(_leftBuffer);
      var rightOrdered = GetOrderedSamples(_rightBuffer);

      if (targetSampleCount >= _sampleCount)
      {
        return (leftOrdered, rightOrdered);
      }

      var leftDownsampled = Downsample(leftOrdered, targetSampleCount);
      var rightDownsampled = Downsample(rightOrdered, targetSampleCount);

      return (leftDownsampled, rightDownsampled);
    }
  }

  private float[] GetOrderedSamples(float[] buffer)
  {
    var result = new float[_sampleCount];

    // Copy from write position to end (oldest samples)
    var firstPartLength = _sampleCount - _writePosition;
    Array.Copy(buffer, _writePosition, result, 0, firstPartLength);

    // Copy from beginning to write position (newest samples)
    Array.Copy(buffer, 0, result, firstPartLength, _writePosition);

    return result;
  }

  private static float[] Downsample(float[] samples, int targetCount)
  {
    var result = new float[targetCount];
    var ratio = (float)samples.Length / targetCount;

    for (var i = 0; i < targetCount; i++)
    {
      var startIndex = (int)(i * ratio);
      var endIndex = (int)((i + 1) * ratio);

      // Find min and max in the range for peak visualization
      var min = float.MaxValue;
      var max = float.MinValue;

      for (var j = startIndex; j < endIndex && j < samples.Length; j++)
      {
        min = Math.Min(min, samples[j]);
        max = Math.Max(max, samples[j]);
      }

      // Use the value with larger absolute magnitude
      result[i] = Math.Abs(max) > Math.Abs(min) ? max : min;
    }

    return result;
  }

  /// <summary>
  /// Resets the waveform buffer to silence.
  /// </summary>
  public void Reset()
  {
    lock (_lock)
    {
      Array.Clear(_leftBuffer);
      Array.Clear(_rightBuffer);
      _writePosition = 0;
    }
  }
}
