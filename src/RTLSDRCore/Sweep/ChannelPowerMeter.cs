using RTLSDRCore.Models;

namespace RTLSDRCore.Sweep;

/// <summary>
/// Measures the in-channel signal level of a block of IQ samples with a
/// Hann-windowed FFT. Used by the band sweep (<see cref="BandSweeper"/>) to
/// give each channel a single comparable number: one window per tune on FM,
/// several offset windows from one capture on the narrowband bands.
/// </summary>
/// <remarks>
/// <para>
/// The block is cut into consecutive, non-overlapping frames of
/// <see cref="FftSize"/> samples; a trailing partial frame is ignored. For
/// every frame the power of each FFT bin whose frequency <c>f</c> satisfies
/// <c>dcExcludeHz &lt; |f| &lt;= halfBandwidthHz</c> is summed, and the result
/// is the mean of those bin powers over all frames, in dB.
/// </para>
/// <para>
/// Bins at and near DC are excluded because the RTL-SDR produces a DC spike
/// at the tuned centre frequency whatever the antenna is receiving.
/// </para>
/// <para>
/// Bin power is normalised by the squared sum of the window coefficients, so a
/// full-scale complex tone that falls exactly on one bin reads close to 0 dB in
/// that bin. The returned figure is a mean over many bins, so it is a relative
/// level for comparing channels measured the same way, not a calibrated
/// absolute power.
/// </para>
/// </remarks>
public static class ChannelPowerMeter
{
  /// <summary>FFT length used per frame. Must be a power of two.</summary>
  public const int FftSize = 2048;

  /// <summary>
  /// Level returned when the in-band power is zero, when no full frame is
  /// available, or when no bin falls inside the measured band.
  /// </summary>
  public const float FloorDb = -150f;

  private static readonly float[] Window = CreateHannWindow(FftSize);
  private static readonly double WindowPowerNorm = ComputeWindowPowerNorm(Window);
  private static readonly int[] BitReverse = CreateBitReverseTable(FftSize);
  private static readonly float[] CosTable = CreateTwiddles(FftSize, cosine: true);
  private static readonly float[] SinTable = CreateTwiddles(FftSize, cosine: false);

  /// <summary>
  /// Returns the mean in-band FFT bin power of <paramref name="samples"/> in dB.
  /// </summary>
  /// <param name="samples">IQ samples captured at <paramref name="sampleRate"/>.</param>
  /// <param name="sampleRate">Sample rate in Hz.</param>
  /// <param name="halfBandwidthHz">Upper edge of the measured band, as an offset from the tuned centre.</param>
  /// <param name="dcExcludeHz">Bins with <c>|f| &lt;= dcExcludeHz</c> are not measured.</param>
  /// <returns>
  /// The level in dB, or <see cref="FloorDb"/> when there is no full frame,
  /// no in-band bin, or zero in-band power.
  /// </returns>
  public static float MeasureDbfs(
    ReadOnlySpan<IqSample> samples,
    int sampleRate,
    int halfBandwidthHz = 80_000,
    int dcExcludeHz = 8_000)
  {
    if (sampleRate <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
    }

    int frames = samples.Length / FftSize;
    if (frames == 0)
    {
      return FloorDb;
    }

    double binWidthHz = (double)sampleRate / FftSize;
    float[] re = new float[FftSize];
    float[] im = new float[FftSize];

    double powerSum = 0.0;
    long binCount = 0;

    for (int frame = 0; frame < frames; frame++)
    {
      ReadOnlySpan<IqSample> block = samples.Slice(frame * FftSize, FftSize);

      // Load windowed samples in bit-reversed order, ready for the
      // in-place iterative butterfly passes.
      for (int n = 0; n < FftSize; n++)
      {
        int target = BitReverse[n];
        float w = Window[n];
        re[target] = block[n].I * w;
        im[target] = block[n].Q * w;
      }

      Transform(re, im);

      for (int k = 0; k < FftSize; k++)
      {
        // Bins 0..N/2-1 are non-negative frequencies; N/2..N-1 are negative.
        int signedBin = k < FftSize / 2 ? k : k - FftSize;
        double absFrequency = Math.Abs(signedBin * binWidthHz);
        if (absFrequency <= dcExcludeHz || absFrequency > halfBandwidthHz)
        {
          continue;
        }

        powerSum += ((double)re[k] * re[k] + (double)im[k] * im[k]) / WindowPowerNorm;
        binCount++;
      }
    }

    if (binCount == 0 || powerSum <= 0.0)
    {
      return FloorDb;
    }

    double db = 10.0 * Math.Log10(powerSum / binCount);
    return db < FloorDb ? FloorDb : (float)db;
  }

  /// <summary>
  /// Returns one level per channel, in dB, from a single set of FFT frames of
  /// <paramref name="samples"/> (AUD-91: several narrowband channels per capture).
  /// </summary>
  /// <remarks>
  /// <para>
  /// Channel <c>c</c> is the mean power of the bins whose frequency <c>f</c> satisfies
  /// <c>|f − offsetsHz[c]| &lt;= halfWindowHz</c> and <c>|f| &gt; dcExcludeHz</c>, over all
  /// frames. Bins span <c>−sampleRate/2</c> to just under <c>+sampleRate/2</c>, so a window that
  /// reaches past either edge is measured over the bins inside it only.
  /// </para>
  /// <para>
  /// Bins are visited in the same order, and their powers summed with the same arithmetic, as
  /// <see cref="MeasureDbfs"/>. For a single offset of 0 the result is therefore identical to
  /// <c>MeasureDbfs(samples, sampleRate, halfWindowHz, dcExcludeHz)</c>.
  /// </para>
  /// </remarks>
  /// <param name="samples">IQ samples captured at <paramref name="sampleRate"/>.</param>
  /// <param name="sampleRate">Sample rate in Hz.</param>
  /// <param name="offsetsHz">Each channel's centre as an offset from the tuned centre, in Hz.</param>
  /// <param name="halfWindowHz">Half-width of each channel's measured window, in Hz.</param>
  /// <param name="dcExcludeHz">Bins with <c>|f| &lt;= dcExcludeHz</c> are not measured for any channel.</param>
  /// <returns>
  /// One level per entry of <paramref name="offsetsHz"/>, each <see cref="FloorDb"/> when there is
  /// no full frame, no bin in that channel's window, or zero power in it.
  /// </returns>
  public static float[] MeasureChannelsDbfs(
    ReadOnlySpan<IqSample> samples,
    int sampleRate,
    IReadOnlyList<long> offsetsHz,
    int halfWindowHz,
    int dcExcludeHz)
  {
    if (sampleRate <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
    }
    ArgumentNullException.ThrowIfNull(offsetsHz);

    int channelCount = offsetsHz.Count;
    float[] levels = new float[channelCount];
    Array.Fill(levels, FloorDb);

    int frames = samples.Length / FftSize;
    if (frames == 0 || channelCount == 0)
    {
      return levels;
    }

    double binWidthHz = (double)sampleRate / FftSize;
    float[] re = new float[FftSize];
    float[] im = new float[FftSize];
    double[] powerSums = new double[channelCount];
    long[] binCounts = new long[channelCount];

    for (int frame = 0; frame < frames; frame++)
    {
      ReadOnlySpan<IqSample> block = samples.Slice(frame * FftSize, FftSize);

      for (int n = 0; n < FftSize; n++)
      {
        int target = BitReverse[n];
        float w = Window[n];
        re[target] = block[n].I * w;
        im[target] = block[n].Q * w;
      }

      Transform(re, im);

      for (int k = 0; k < FftSize; k++)
      {
        int signedBin = k < FftSize / 2 ? k : k - FftSize;
        double frequency = signedBin * binWidthHz;
        if (Math.Abs(frequency) <= dcExcludeHz)
        {
          continue;
        }

        double power = ((double)re[k] * re[k] + (double)im[k] * im[k]) / WindowPowerNorm;
        for (int c = 0; c < channelCount; c++)
        {
          if (Math.Abs(frequency - offsetsHz[c]) > halfWindowHz)
          {
            continue;
          }
          powerSums[c] += power;
          binCounts[c]++;
        }
      }
    }

    for (int c = 0; c < channelCount; c++)
    {
      if (binCounts[c] == 0 || powerSums[c] <= 0.0)
      {
        continue;
      }
      double db = 10.0 * Math.Log10(powerSums[c] / binCounts[c]);
      levels[c] = db < FloorDb ? FloorDb : (float)db;
    }

    return levels;
  }

  /// <summary>
  /// In-place iterative radix-2 decimation-in-time FFT. Inputs must already
  /// be in bit-reversed order.
  /// </summary>
  private static void Transform(float[] re, float[] im)
  {
    for (int size = 2; size <= FftSize; size <<= 1)
    {
      int half = size >> 1;
      int twiddleStep = FftSize / size;
      for (int start = 0; start < FftSize; start += size)
      {
        for (int j = 0; j < half; j++)
        {
          int t = j * twiddleStep;
          float wr = CosTable[t];
          float wi = SinTable[t];
          int a = start + j;
          int b = a + half;

          float xr = re[b] * wr - im[b] * wi;
          float xi = re[b] * wi + im[b] * wr;

          re[b] = re[a] - xr;
          im[b] = im[a] - xi;
          re[a] += xr;
          im[a] += xi;
        }
      }
    }
  }

  private static float[] CreateHannWindow(int size)
  {
    float[] window = new float[size];
    for (int n = 0; n < size; n++)
    {
      window[n] = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * n / size));
    }
    return window;
  }

  private static double ComputeWindowPowerNorm(float[] window)
  {
    double sum = 0.0;
    foreach (float w in window)
    {
      sum += w;
    }
    return sum * sum;
  }

  private static int[] CreateBitReverseTable(int size)
  {
    int bits = (int)Math.Round(Math.Log2(size));
    int[] table = new int[size];
    for (int i = 0; i < size; i++)
    {
      int reversed = 0;
      for (int b = 0; b < bits; b++)
      {
        if ((i & (1 << b)) != 0)
        {
          reversed |= 1 << (bits - 1 - b);
        }
      }
      table[i] = reversed;
    }
    return table;
  }

  private static float[] CreateTwiddles(int size, bool cosine)
  {
    // Forward transform: W = exp(-2*pi*i*k/N) => cos(-x), sin(-x).
    float[] table = new float[size / 2];
    for (int k = 0; k < size / 2; k++)
    {
      double angle = -2.0 * Math.PI * k / size;
      table[k] = (float)(cosine ? Math.Cos(angle) : Math.Sin(angle));
    }
    return table;
  }
}
