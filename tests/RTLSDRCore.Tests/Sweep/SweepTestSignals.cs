using RTLSDRCore.Models;

namespace RTLSDRCore.Tests.Sweep;

/// <summary>Deterministic synthetic IQ generators for the sweep tests.</summary>
internal static class SweepTestSignals
{
  public const int SampleRate = 240_000;

  /// <summary>A complex exponential at <paramref name="offsetHz"/> from centre.</summary>
  public static IqSample[] Tone(int count, double offsetHz, float amplitude, int sampleRate = SampleRate)
  {
    IqSample[] samples = new IqSample[count];
    for (int n = 0; n < count; n++)
    {
      double phase = 2.0 * Math.PI * offsetHz * n / sampleRate;
      samples[n] = new IqSample((float)(amplitude * Math.Cos(phase)), (float)(amplitude * Math.Sin(phase)));
    }
    return samples;
  }

  /// <summary>Uniform white noise with a fixed seed.</summary>
  public static IqSample[] Noise(int count, float amplitude, int seed = 1234)
  {
    Random random = new(seed);
    IqSample[] samples = new IqSample[count];
    for (int n = 0; n < count; n++)
    {
      float i = (float)((random.NextDouble() * 2 - 1) * amplitude);
      float q = (float)((random.NextDouble() * 2 - 1) * amplitude);
      samples[n] = new IqSample(i, q);
    }
    return samples;
  }

  /// <summary>Element-wise sum of two blocks of equal length.</summary>
  public static IqSample[] Add(IqSample[] a, IqSample[] b)
  {
    IqSample[] sum = new IqSample[a.Length];
    for (int n = 0; n < a.Length; n++)
    {
      sum[n] = a[n] + b[n];
    }
    return sum;
  }
}
