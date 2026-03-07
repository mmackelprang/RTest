namespace Radio.AudioAnalysis;

/// <summary>
/// FFT-based frequency analysis for measuring Total Harmonic Distortion (THD)
/// of known-frequency test tones.
/// </summary>
public static class FrequencyAnalysis
{
  /// <summary>
  /// Measures Total Harmonic Distortion for a known single-frequency tone.
  /// Uses a simple DFT at the fundamental and harmonics (faster than full FFT
  /// when only a few bins are needed).
  /// </summary>
  /// <param name="samples">Interleaved audio samples.</param>
  /// <param name="sampleRate">Sample rate in Hz.</param>
  /// <param name="channels">Number of channels (samples are deinterleaved internally).</param>
  /// <param name="expectedFrequencyHz">Expected fundamental frequency.</param>
  /// <param name="channel">Which channel to analyze (0 = left, 1 = right).</param>
  /// <param name="maxHarmonic">Maximum harmonic number to measure (default: 8).</param>
  /// <returns>THD as a percentage (0.0 = no distortion).</returns>
  public static float MeasureTotalHarmonicDistortion(
    float[] samples, int sampleRate, int channels, int expectedFrequencyHz,
    int channel = 0, int maxHarmonic = 8)
  {
    // Extract the target channel
    var channelSamples = ExtractChannel(samples, channel, channels);
    if (channelSamples.Length < sampleRate / expectedFrequencyHz)
    {
      return 0f;
    }

    // Measure power at fundamental and harmonics using Goertzel algorithm
    var fundamentalPower = GoertzelPower(channelSamples, sampleRate, expectedFrequencyHz);

    if (fundamentalPower < 1e-12)
    {
      return 0f; // No signal at fundamental
    }

    double harmonicPowerSum = 0;
    for (int h = 2; h <= maxHarmonic; h++)
    {
      var harmonicFreq = expectedFrequencyHz * h;
      if (harmonicFreq >= sampleRate / 2)
      {
        break; // Above Nyquist
      }

      harmonicPowerSum += GoertzelPower(channelSamples, sampleRate, harmonicFreq);
    }

    // THD = sqrt(sum of harmonic powers) / fundamental power * 100%
    return (float)(Math.Sqrt(harmonicPowerSum / fundamentalPower) * 100.0);
  }

  /// <summary>
  /// Measures the power at a specific frequency using the Goertzel algorithm.
  /// More efficient than FFT when only a few frequency bins are needed.
  /// </summary>
  /// <param name="samples">Single-channel samples.</param>
  /// <param name="sampleRate">Sample rate in Hz.</param>
  /// <param name="targetFrequencyHz">Target frequency to measure.</param>
  /// <returns>Normalized power at the target frequency.</returns>
  public static double GoertzelPower(float[] samples, int sampleRate, int targetFrequencyHz)
  {
    var n = samples.Length;
    var k = (int)Math.Round((double)n * targetFrequencyHz / sampleRate);
    var omega = 2.0 * Math.PI * k / n;
    var coeff = 2.0 * Math.Cos(omega);

    double s0 = 0, s1 = 0, s2 = 0;

    for (int i = 0; i < n; i++)
    {
      s0 = samples[i] + coeff * s1 - s2;
      s2 = s1;
      s1 = s0;
    }

    // Power = (s1^2 + s2^2 - coeff * s1 * s2) / (N^2)
    var power = (s1 * s1 + s2 * s2 - coeff * s1 * s2) / ((double)n * n);
    return power;
  }

  /// <summary>
  /// Extracts a single channel from interleaved samples.
  /// </summary>
  private static float[] ExtractChannel(float[] samples, int channel, int channels)
  {
    var count = samples.Length / channels;
    var result = new float[count];
    for (int i = 0; i < count; i++)
    {
      result[i] = samples[i * channels + channel];
    }

    return result;
  }
}
