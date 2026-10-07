namespace Radio.Infrastructure.Audio.Visualization;

/// <summary>
/// Groups linear FFT bins into log-spaced display bands, scaled in dB, for the spectrum and ring
/// visualizers.
///
/// <para>
/// <b>Why this exists.</b> The analyzer's bins are evenly spaced (sample rate / FFT size, ~23 Hz at
/// 48 kHz / 2048), and the UI drew the first 64 (spectrum) or 128 (ring) of them — so the spectrum
/// showed roughly 0–1.5 kHz and the ring 0–3 kHz, never the treble. It also shipped all 1024 bins
/// plus 1024 frequencies on every frame, ~20–25 KB of JSON at 20 fps, through two hops (API to
/// Radio.Web, Radio.Web to the browser), to draw 64 of them.
/// </para>
///
/// <para>
/// <b>Bands</b> are spaced evenly in log frequency between <see cref="DefaultMinHz"/> and
/// <see cref="DefaultMaxHz"/>, which is how pitch is heard. A band that spans at least one whole bin
/// takes the loudest bin inside it. A band containing no whole bin — at 48 kHz / 2048 with 128 bands, those
/// below roughly 490 Hz — is interpolated between the two bins either side of its centre, so the bass reads as a slope
/// rather than repeated steps.
/// </para>
///
/// <para>
/// <b>dB scaling</b>: the analyzer normalizes each frame so its loudest bin is 1.0, linearly. On a
/// log axis a linear scale leaves the upper bands near zero (music energy falls with frequency), so
/// each band is mapped from 20·log10 onto 0..1 over <see cref="DefaultRangeDb"/> dB below the frame's
/// peak. Anything quieter than that floor — including silence — draws as 0.
/// </para>
/// </summary>
public static class LogSpectrumBands
{
  /// <summary>
  /// Number of display bands sent to the browser. 128 so the ring visualizer keeps its 128 spokes;
  /// the spectrum bars merge neighbouring pairs into 64 at draw time (visualizer.js).
  /// </summary>
  public const int DefaultBandCount = 128;

  /// <summary>Lower edge of the lowest band, in Hz.</summary>
  public const float DefaultMinHz = 40f;

  /// <summary>Upper edge of the highest band, in Hz (clamped to the Nyquist frequency).</summary>
  public const float DefaultMaxHz = 16000f;

  /// <summary>Dynamic range mapped onto 0..1, in dB below the frame's peak.</summary>
  public const float DefaultRangeDb = 60f;

  /// <summary>
  /// Computes log-spaced, dB-scaled band magnitudes and each band's centre frequency.
  /// </summary>
  /// <param name="binMagnitudes">Linear bin magnitudes (0..1), bin <c>i</c> at <c>i × resolution</c> Hz.</param>
  /// <param name="frequencyResolution">Hz per bin.</param>
  /// <param name="bandCount">Number of bands to produce.</param>
  /// <param name="minHz">Lower edge of the lowest band.</param>
  /// <param name="maxHz">Upper edge of the highest band; clamped to the highest bin's frequency.</param>
  /// <param name="rangeDb">Dynamic range mapped onto 0..1.</param>
  /// <returns>
  /// Band magnitudes (0..1) and band centre frequencies (Hz), each of length <paramref name="bandCount"/>,
  /// or two empty arrays when there are no bins to band.
  /// </returns>
  public static (float[] Magnitudes, float[] Frequencies) Compute(
    float[] binMagnitudes,
    float frequencyResolution,
    int bandCount = DefaultBandCount,
    float minHz = DefaultMinHz,
    float maxHz = DefaultMaxHz,
    float rangeDb = DefaultRangeDb)
  {
    ArgumentNullException.ThrowIfNull(binMagnitudes);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bandCount);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minHz);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rangeDb);

    int binCount = binMagnitudes.Length;
    if (binCount < 2 || frequencyResolution <= 0f)
    {
      return ([], []);
    }

    // The top bin is the highest frequency there is data for; a band above it would be empty.
    float topHz = (binCount - 1) * frequencyResolution;
    float hi = Math.Min(maxHz, topHz);
    float lo = Math.Min(minHz, hi / 2f);
    float ratio = MathF.Pow(hi / lo, 1f / bandCount);

    float[] magnitudes = new float[bandCount];
    float[] frequencies = new float[bandCount];

    float bandLoHz = lo;
    for (int b = 0; b < bandCount; b++)
    {
      float bandHiHz = bandLoHz * ratio;
      float centreHz = MathF.Sqrt(bandLoHz * bandHiHz);
      frequencies[b] = centreHz;

      int firstBin = (int)MathF.Ceiling(bandLoHz / frequencyResolution);
      int lastBin = Math.Min((int)MathF.Floor(bandHiHz / frequencyResolution), binCount - 1);

      float linear;
      if (firstBin <= lastBin)
      {
        linear = 0f;
        for (int i = firstBin; i <= lastBin; i++)
        {
          linear = Math.Max(linear, binMagnitudes[i]);
        }
      }
      else
      {
        // Narrower than a bin: interpolate at the band's centre between its neighbouring bins.
        float position = centreHz / frequencyResolution;
        int below = Math.Min((int)position, binCount - 1);
        int above = Math.Min(below + 1, binCount - 1);
        float t = position - below;
        linear = binMagnitudes[below] + (binMagnitudes[above] - binMagnitudes[below]) * t;
      }

      magnitudes[b] = ToDisplayScale(linear, rangeDb);
      bandLoHz = bandHiHz;
    }

    return (magnitudes, frequencies);
  }

  private static float ToDisplayScale(float linear, float rangeDb)
  {
    if (linear <= 0f)
    {
      return 0f;
    }

    float db = 20f * MathF.Log10(linear);
    return Math.Clamp((db + rangeDb) / rangeDb, 0f, 1f);
  }
}
