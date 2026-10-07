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
/// takes the loudest bin inside it. At 48 kHz / 2048 with 128 bands, bands below ~490 Hz are narrower
/// than a bin; those containing no bin (up to ~390 Hz) are interpolated between the two bins either side
/// of their centre, so the bass reads as a slope rather than repeated steps. Bin 0 (DC) is never used,
/// so a DC offset on a capture source cannot light up the lowest bands at small FFT sizes.
/// </para>
///
/// <para>
/// <b>Scale</b>: absolute dBFS (the analyzer's amplitudes, full-scale sine = 1.0), tilted, mapped from
/// <see cref="DefaultFloorDbfs"/>..<see cref="DefaultCeilingDbfs"/> onto 0..1, then raised to
/// <see cref="DefaultCurve"/>. All four are configurable (VisualizerOptions.Spectrum*); these are defaults.
/// <list type="bullet">
///   <item><b>Absolute, not relative to the frame.</b> Scaling each frame to its own loudest band drew
///   radio static — flat, broadband — as a full-height wall. Absolute levels draw it as a steady, low
///   ramp instead (white noise plus the tilt rises toward the treble).</item>
///   <item><b>A 40 dB window and a 1.5 curve, not 70 dB linear-in-dB.</b> A 70 dB window (-80..-10)
///   put most music at half height, where a 6 dB swing moved a bar ~8% and 62% of bars sat in one
///   colour. Measured on the box 2026-10-06 on the same audio, -65..-25 with a 1.5 curve gave bar
///   spread 0.24 (the old per-frame-peak display: 0.27), frame-to-frame motion 0.18 (old: 0.12), and
///   a ~38/38/24 % cyan/amber/red colour split.</item>
///   <item><b>Tilt, +<see cref="DefaultTiltDbPerOctave"/> dB/octave about <see cref="DefaultTiltPivotHz"/>.</b>
///   Music's energy falls with frequency — measured on the box 2026-10-06, ~26 dB from 80 Hz to 10 kHz,
///   about 3.7 dB/octave — so an untilted display is all bass. The tilt is the usual analyzer
///   compensation for that; it brought the same music's band averages to within ~7 dB of each other.</item>
/// </list>
/// Silence and anything below the floor draw as 0.
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

  /// <summary>Tilted level, in dBFS, that draws as 0 (and anything below it).</summary>
  public const float DefaultFloorDbfs = -65f;

  /// <summary>Tilted level, in dBFS, that draws as 1 (and anything above it).</summary>
  public const float DefaultCeilingDbfs = -25f;

  /// <summary>Exponent applied to the 0..1 height after the dB window; above 1 adds contrast.</summary>
  public const float DefaultCurve = 1.5f;

  /// <summary>Gain added per octave above <see cref="DefaultTiltPivotHz"/> (removed per octave below).</summary>
  public const float DefaultTiltDbPerOctave = 3f;

  /// <summary>Frequency the tilt pivots about: a band centred here is drawn at its true level.</summary>
  public const float DefaultTiltPivotHz = 1000f;

  /// <summary>
  /// Computes log-spaced, tilted, dBFS-scaled band values and each band's centre frequency.
  /// </summary>
  /// <param name="binMagnitudes">Bin amplitudes (full-scale sine = 1.0), bin <c>i</c> at <c>i × resolution</c> Hz.</param>
  /// <param name="frequencyResolution">Hz per bin.</param>
  /// <param name="bandCount">Number of bands to produce.</param>
  /// <param name="minHz">Lower edge of the lowest band.</param>
  /// <param name="maxHz">Upper edge of the highest band; clamped to the highest bin's frequency.</param>
  /// <param name="floorDbfs">Tilted level that draws as 0.</param>
  /// <param name="ceilingDbfs">Tilted level that draws as 1; must be above <paramref name="floorDbfs"/>.</param>
  /// <param name="tiltDbPerOctave">dB added per octave above <paramref name="tiltPivotHz"/>; 0 disables the tilt.</param>
  /// <param name="tiltPivotHz">Frequency the tilt pivots about.</param>
  /// <param name="curve">Exponent applied to the 0..1 height after the dB window; must be positive. 1 is plain dB.</param>
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
    float floorDbfs = DefaultFloorDbfs,
    float ceilingDbfs = DefaultCeilingDbfs,
    float tiltDbPerOctave = DefaultTiltDbPerOctave,
    float tiltPivotHz = DefaultTiltPivotHz,
    float curve = DefaultCurve)
  {
    ArgumentNullException.ThrowIfNull(binMagnitudes);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bandCount);
    // NaN passes ThrowIfNegativeOrZero and every comparison, so it is checked separately.
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minHz);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHz);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tiltPivotHz);
    ThrowIfNaN(minHz, nameof(minHz));
    ThrowIfNaN(maxHz, nameof(maxHz));
    ThrowIfNaN(floorDbfs, nameof(floorDbfs));
    ThrowIfNaN(ceilingDbfs, nameof(ceilingDbfs));
    ThrowIfNaN(tiltDbPerOctave, nameof(tiltDbPerOctave));
    ThrowIfNaN(tiltPivotHz, nameof(tiltPivotHz));
    ThrowIfNaN(curve, nameof(curve));
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(curve);
    if (ceilingDbfs <= floorDbfs)
    {
      throw new ArgumentOutOfRangeException(nameof(ceilingDbfs), ceilingDbfs, "Must be above floorDbfs.");
    }

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

      // Bin 0 is DC, not audio: never used.
      int firstBin = Math.Max(1, (int)MathF.Ceiling(bandLoHz / frequencyResolution));
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
        // Clamped to bin 1 so a band below the first audio bin takes that bin rather than DC.
        float position = Math.Max(1f, centreHz / frequencyResolution);
        int below = Math.Min((int)position, binCount - 1);
        int above = Math.Min(below + 1, binCount - 1);
        float t = position - below;
        linear = binMagnitudes[below] + (binMagnitudes[above] - binMagnitudes[below]) * t;
      }

      float tiltDb = tiltDbPerOctave * MathF.Log2(centreHz / tiltPivotHz);
      magnitudes[b] = ToDisplayScale(linear, tiltDb, floorDbfs, ceilingDbfs, curve);
      bandLoHz = bandHiHz;
    }

    return (magnitudes, frequencies);
  }

  private static void ThrowIfNaN(float value, string paramName)
  {
    if (float.IsNaN(value))
    {
      throw new ArgumentOutOfRangeException(paramName, value, "Must be a number.");
    }
  }

  private static float ToDisplayScale(float amplitude, float tiltDb, float floorDbfs, float ceilingDbfs, float curve)
  {
    if (amplitude <= 0f)
    {
      return 0f;
    }

    float db = 20f * MathF.Log10(amplitude) + tiltDb;
    float linear = Math.Clamp((db - floorDbfs) / (ceilingDbfs - floorDbfs), 0f, 1f);
    return curve == 1f ? linear : MathF.Pow(linear, curve);
  }
}
