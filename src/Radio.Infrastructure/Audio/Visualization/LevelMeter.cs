namespace Radio.Infrastructure.Audio.Visualization;

/// <summary>
/// Audio level meter providing peak and RMS measurements.
/// Supports stereo audio with independent channel metering.
/// </summary>
internal sealed class LevelMeter
{
  private readonly float _peakDecayRate;
  private readonly float _rmsSmoothing;
  private readonly int _peakHoldTimeMs;

  private float _leftPeak;
  private float _rightPeak;
  private float _leftPeakHeld;
  private float _rightPeakHeld;
  private float _leftRms;
  private float _rightRms;
  private DateTime _leftPeakHoldExpiry;
  private DateTime _rightPeakHoldExpiry;
  private readonly object _lock = new();

  private const float MinDbValue = -96f; // Minimum dB value for silence
  private const float ClippingThreshold = 0.999f;

  /// <summary>
  /// Initializes a new instance of the <see cref="LevelMeter"/> class.
  /// </summary>
  /// <param name="sampleRate">The sample rate in Hz.</param>
  /// <param name="peakDecayRate">Peak decay rate per second (0.0 to 1.0).</param>
  /// <param name="rmsSmoothing">RMS smoothing factor (0.0 to 1.0).</param>
  /// <param name="peakHoldTimeMs">Peak hold time in milliseconds.</param>
  public LevelMeter(int sampleRate, float peakDecayRate = 0.95f, float rmsSmoothing = 0.3f, int peakHoldTimeMs = 1000)
  {
    if (sampleRate <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive");
    }

    _peakDecayRate = Math.Clamp(peakDecayRate, 0f, 1f);
    _rmsSmoothing = Math.Clamp(rmsSmoothing, 0f, 1f);
    _peakHoldTimeMs = Math.Max(0, peakHoldTimeMs);

    _leftPeakHoldExpiry = DateTime.MinValue;
    _rightPeakHoldExpiry = DateTime.MinValue;
  }

  /// <summary>
  /// Processes stereo audio samples (interleaved left/right).
  /// </summary>
  /// <param name="samples">Interleaved stereo samples.</param>
  public void ProcessSamples(float[] samples)
  {
    ProcessSamplesCore(samples, samples.Length);
  }

  /// <summary>
  /// Processes stereo audio samples (interleaved left/right).
  /// </summary>
  /// <param name="samples">Interleaved stereo samples span.</param>
  /// <param name="count">Number of samples to process.</param>
  public void ProcessSamples(Span<float> samples, int count)
  {
    ProcessSamplesCore(samples, count);
  }

  private void ProcessSamplesCore(ReadOnlySpan<float> samples, int count)
  {
    lock (_lock)
    {
      var now = DateTime.UtcNow;
      float newLeftPeak = 0f;
      float newRightPeak = 0f;
      float leftSumSq = 0f;
      float rightSumSq = 0f;
      var samplePairs = 0;

      // Process interleaved stereo samples
      for (var i = 0; i < count - 1; i += 2)
      {
        var left = Math.Abs(samples[i]);
        var right = Math.Abs(samples[i + 1]);

        newLeftPeak = Math.Max(newLeftPeak, left);
        newRightPeak = Math.Max(newRightPeak, right);

        leftSumSq += samples[i] * samples[i];
        rightSumSq += samples[i + 1] * samples[i + 1];
        samplePairs++;
      }

      // Handle odd sample count (treat last sample as mono)
      if (count % 2 != 0)
      {
        var mono = Math.Abs(samples[count - 1]);
        newLeftPeak = Math.Max(newLeftPeak, mono);
        newRightPeak = Math.Max(newRightPeak, mono);
        leftSumSq += samples[count - 1] * samples[count - 1];
        rightSumSq += samples[count - 1] * samples[count - 1];
        samplePairs++;
      }

      if (samplePairs == 0) return;

      // Update peak with hold
      UpdatePeakWithHold(newLeftPeak, ref _leftPeak, ref _leftPeakHeld, ref _leftPeakHoldExpiry, now);
      UpdatePeakWithHold(newRightPeak, ref _rightPeak, ref _rightPeakHeld, ref _rightPeakHoldExpiry, now);

      // Calculate RMS with exponential smoothing
      var newLeftRms = MathF.Sqrt(leftSumSq / samplePairs);
      var newRightRms = MathF.Sqrt(rightSumSq / samplePairs);

      _leftRms = _leftRms * _rmsSmoothing + newLeftRms * (1f - _rmsSmoothing);
      _rightRms = _rightRms * _rmsSmoothing + newRightRms * (1f - _rmsSmoothing);
    }
  }

  private void UpdatePeakWithHold(float newPeak, ref float currentPeak, ref float peakHeld,
    ref DateTime holdExpiry, DateTime now)
  {
    if (newPeak >= peakHeld)
    {
      // New peak is higher, reset hold
      peakHeld = newPeak;
      holdExpiry = now.AddMilliseconds(_peakHoldTimeMs);
      currentPeak = newPeak;
    }
    else if (now < holdExpiry)
    {
      // Still in hold period, keep held value
      currentPeak = peakHeld;
    }
    else
    {
      // Decay the peak
      currentPeak *= _peakDecayRate;
      peakHeld = currentPeak;
    }
  }

  /// <summary>
  /// Gets the current left channel peak level (0.0 to 1.0).
  /// </summary>
  public float LeftPeak
  {
    get
    {
      lock (_lock) return _leftPeak;
    }
  }

  /// <summary>
  /// Gets the current right channel peak level (0.0 to 1.0).
  /// </summary>
  public float RightPeak
  {
    get
    {
      lock (_lock) return _rightPeak;
    }
  }

  /// <summary>
  /// Gets the current left channel RMS level (0.0 to 1.0).
  /// </summary>
  public float LeftRms
  {
    get
    {
      lock (_lock) return _leftRms;
    }
  }

  /// <summary>
  /// Gets the current right channel RMS level (0.0 to 1.0).
  /// </summary>
  public float RightRms
  {
    get
    {
      lock (_lock) return _rightRms;
    }
  }

  /// <summary>
  /// Gets whether the audio is clipping (peak at or near 1.0).
  /// </summary>
  public bool IsClipping
  {
    get
    {
      lock (_lock)
      {
        return _leftPeak >= ClippingThreshold || _rightPeak >= ClippingThreshold;
      }
    }
  }

  /// <summary>
  /// Gets the mono/combined peak level (0.0 to 1.0).
  /// </summary>
  public float MonoPeak
  {
    get
    {
      lock (_lock) return Math.Max(_leftPeak, _rightPeak);
    }
  }

  /// <summary>
  /// Gets the mono/combined RMS level (0.0 to 1.0).
  /// </summary>
  public float MonoRms
  {
    get
    {
      lock (_lock) return (_leftRms + _rightRms) / 2f;
    }
  }

  /// <summary>
  /// Converts a linear level (0.0 to 1.0) to decibels (dBFS).
  /// </summary>
  /// <param name="linear">The linear level value.</param>
  /// <returns>The level in dBFS.</returns>
  public static float LinearToDb(float linear)
  {
    if (linear <= 0f)
    {
      return MinDbValue;
    }

    var db = 20f * MathF.Log10(linear);
    return Math.Max(db, MinDbValue);
  }

  /// <summary>
  /// Gets the left channel peak in decibels (dBFS).
  /// </summary>
  public float LeftPeakDb => LinearToDb(LeftPeak);

  /// <summary>
  /// Gets the right channel peak in decibels (dBFS).
  /// </summary>
  public float RightPeakDb => LinearToDb(RightPeak);

  /// <summary>
  /// Gets the left channel RMS in decibels (dBFS).
  /// </summary>
  public float LeftRmsDb => LinearToDb(LeftRms);

  /// <summary>
  /// Gets the right channel RMS in decibels (dBFS).
  /// </summary>
  public float RightRmsDb => LinearToDb(RightRms);

  /// <summary>
  /// Resets all level measurements.
  /// </summary>
  public void Reset()
  {
    lock (_lock)
    {
      _leftPeak = 0f;
      _rightPeak = 0f;
      _leftPeakHeld = 0f;
      _rightPeakHeld = 0f;
      _leftRms = 0f;
      _rightRms = 0f;
      _leftPeakHoldExpiry = DateTime.MinValue;
      _rightPeakHoldExpiry = DateTime.MinValue;
    }
  }
}
