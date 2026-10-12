using System.Numerics;
using RTLSDRCore.Models;

namespace RTLSDRCore.DSP;

/// <summary>
/// Software offset tuning and channel selection, in one pass over the IQ: each input sample is
/// shifted down by <see cref="OffsetHz"/> with a numerically controlled oscillator (a complex
/// mixer), low-pass filtered to the channel with a Hamming-windowed sinc FIR, and decimated by
/// <see cref="Decimation"/>. The FIR is evaluated only for the samples that are kept. The mean
/// power of the filtered channel is reported per call.
/// </summary>
/// <remarks>
/// <para>
/// Two users: the AM chain (<see cref="AmReceiveChain"/>, AUD-101), with a 60 kHz offset, and the
/// narrowband FM chain (<see cref="NfmReceiveChain"/>, AUD-107), with no offset (an
/// <see cref="OffsetHz"/> of 0 makes the oscillator a one-entry table of 1, i.e. no shift). Named
/// <c>AmChannelizer</c> until AUD-107.
/// </para>
/// <para>
/// With an offset, the receiver tunes the device to <c>f − OffsetHz</c>, so the wanted carrier arrives at
/// <c>+OffsetHz</c> in the capture and the device's DC offset (and its low-frequency spurs) at
/// 0 Hz. The mixer moves the carrier to 0 Hz and the DC offset to <c>−OffsetHz</c>, where the
/// channel filter removes it.
/// </para>
/// <para>
/// The oscillator is a table of exactly one period of <c>exp(−j·2π·OffsetHz·n/fs)</c>, which has
/// <c>fs / gcd(fs, OffsetHz)</c> entries (4 for 60 kHz at 240 kS/s). It cannot drift: every
/// period is the same table.
/// </para>
/// </remarks>
public sealed class IqChannelizer
{
  /// <summary>The longest oscillator table accepted (entries).</summary>
  public const int MaxOscillatorPeriod = 1 << 16;

  private readonly float[] _coefficients;
  private readonly float[] _historyI;
  private readonly float[] _historyQ;
  private readonly float[] _oscCos;
  private readonly float[] _oscSin;
  private int _writeIndex;
  private int _oscIndex;
  private int _decimationCounter;

  /// <summary>
  /// Creates a channelizer.
  /// </summary>
  /// <param name="inputSampleRate">The IQ sample rate, in Hz.</param>
  /// <param name="offsetHz">
  /// The frequency, relative to the device centre, that is moved to 0 Hz (the receiver's tuning
  /// offset). 0 for no shift.
  /// </param>
  /// <param name="cutoffHz">The filter's cutoff (its −6 dB point), in Hz: the channel's half-bandwidth.</param>
  /// <param name="transitionHz">The width of the filter's transition band, in Hz; sets the tap count.</param>
  /// <param name="decimation">The decimation factor (1 keeps every sample).</param>
  public IqChannelizer(int inputSampleRate, int offsetHz, float cutoffHz, float transitionHz, int decimation)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputSampleRate);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decimation);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cutoffHz);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(transitionHz);
    if (Math.Abs(offsetHz) >= inputSampleRate / 2)
    {
      throw new ArgumentOutOfRangeException(nameof(offsetHz), offsetHz, "The offset must be inside the capture (under half the sample rate).");
    }

    InputSampleRate = inputSampleRate;
    OffsetHz = offsetHz;
    CutoffHz = cutoffHz;
    Decimation = decimation;

    _coefficients = DesignLowPass(inputSampleRate, cutoffHz, TapsFor(inputSampleRate, transitionHz));
    _historyI = new float[_coefficients.Length * 2];
    _historyQ = new float[_coefficients.Length * 2];

    int period = inputSampleRate / Gcd(inputSampleRate, Math.Abs(offsetHz));
    if (period > MaxOscillatorPeriod)
    {
      throw new ArgumentOutOfRangeException(nameof(offsetHz), offsetHz,
        $"The oscillator period ({period} samples) is too long; choose an offset with a larger common divisor with the sample rate.");
    }

    _oscCos = new float[period];
    _oscSin = new float[period];
    for (int n = 0; n < period; n++)
    {
      double phase = -2.0 * Math.PI * offsetHz * (double)n / inputSampleRate;
      _oscCos[n] = (float)Math.Cos(phase);
      _oscSin[n] = (float)Math.Sin(phase);
    }
  }

  /// <summary>Gets the input sample rate, in Hz.</summary>
  public int InputSampleRate { get; }

  /// <summary>Gets the output sample rate, in Hz.</summary>
  public int OutputSampleRate => InputSampleRate / Decimation;

  /// <summary>Gets the frequency moved to 0 Hz, in Hz, relative to the device centre.</summary>
  public int OffsetHz { get; }

  /// <summary>Gets the filter's cutoff (−6 dB), in Hz.</summary>
  public float CutoffHz { get; }

  /// <summary>Gets the decimation factor.</summary>
  public int Decimation { get; }

  /// <summary>Gets the number of filter taps.</summary>
  public int Taps => _coefficients.Length;

  /// <summary>Gets the length of the oscillator table, in samples.</summary>
  public int OscillatorPeriod => _oscCos.Length;

  /// <summary>
  /// Gets the mean power (mean of |y|², normalised IQ units) of the samples the last
  /// <see cref="Process"/> call produced; 0 when it produced none.
  /// </summary>
  public double LastMeanPower { get; private set; }

  /// <summary>
  /// The tap count of a Hamming-windowed FIR with a transition band of
  /// <paramref name="transitionHz"/> at <paramref name="sampleRate"/>: 3.3·fs/Δf, rounded up to
  /// an odd number.
  /// </summary>
  /// <param name="sampleRate">Sample rate, in Hz.</param>
  /// <param name="transitionHz">Transition width, in Hz.</param>
  public static int TapsFor(int sampleRate, float transitionHz) =>
    (int)Math.Ceiling(3.3 * sampleRate / transitionHz) | 1;

  /// <summary>The most samples <see cref="Process"/> can produce from <paramref name="inputLength"/> input samples.</summary>
  /// <param name="inputLength">The number of input samples.</param>
  public int MaxOutputFor(int inputLength) => inputLength / Decimation + 1;

  /// <summary>
  /// Shifts, filters and decimates <paramref name="input"/>, writing the kept samples to
  /// <paramref name="output"/>. Filter, oscillator and decimation state carry over between calls.
  /// </summary>
  /// <param name="input">IQ at <see cref="InputSampleRate"/>.</param>
  /// <param name="output">Receives the channel at <see cref="OutputSampleRate"/>.</param>
  /// <returns>The number of samples written.</returns>
  /// <exception cref="ArgumentException">
  /// <paramref name="output"/> is shorter than <see cref="MaxOutputFor"/> of the input length. Every
  /// input sample is always consumed, so that a short output buffer cannot drop input samples and
  /// shift the decimation phase.
  /// </exception>
  public int Process(ReadOnlySpan<IqSample> input, Span<IqSample> output)
  {
    if (output.Length < MaxOutputFor(input.Length))
    {
      throw new ArgumentException(
        $"The output holds {output.Length} samples; {input.Length} input samples can produce {MaxOutputFor(input.Length)}.",
        nameof(output));
    }

    int taps = _coefficients.Length;
    int period = _oscCos.Length;
    int written = 0;
    double powerSum = 0;

    for (int i = 0; i < input.Length; i++)
    {
      // Mix: (I + jQ)·(c + js), with c + js = exp(−j·2π·offset·n/fs).
      float c = _oscCos[_oscIndex];
      float s = _oscSin[_oscIndex];
      float inI = input[i].I;
      float inQ = input[i].Q;
      float mixedI = inI * c - inQ * s;
      float mixedQ = inI * s + inQ * c;
      if (++_oscIndex == period)
      {
        _oscIndex = 0;
      }

      // The history is stored twice, so the newest `taps` samples are always contiguous at
      // [_writeIndex + 1, _writeIndex + taps], oldest first.
      _historyI[_writeIndex] = mixedI;
      _historyI[_writeIndex + taps] = mixedI;
      _historyQ[_writeIndex] = mixedQ;
      _historyQ[_writeIndex + taps] = mixedQ;
      int windowStart = _writeIndex + 1;
      if (++_writeIndex == taps)
      {
        _writeIndex = 0;
      }

      if (++_decimationCounter < Decimation)
      {
        continue;
      }
      _decimationCounter = 0;

      float outI = Dot(_historyI.AsSpan(windowStart, taps), _coefficients);
      float outQ = Dot(_historyQ.AsSpan(windowStart, taps), _coefficients);
      output[written++] = new IqSample(outI, outQ);
      powerSum += (double)outI * outI + (double)outQ * outQ;
    }

    LastMeanPower = written > 0 ? powerSum / written : 0;
    return written;
  }

  /// <summary>Clears the filter history and restarts the oscillator and the decimation phase.</summary>
  public void Reset()
  {
    Array.Clear(_historyI);
    Array.Clear(_historyQ);
    _writeIndex = 0;
    _oscIndex = 0;
    _decimationCounter = 0;
    LastMeanPower = 0;
  }

  private static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
  {
    int i = 0;
    float sum = 0;
    if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
    {
      Vector<float> acc = Vector<float>.Zero;
      int last = a.Length - Vector<float>.Count;
      for (; i <= last; i += Vector<float>.Count)
      {
        acc += new Vector<float>(a.Slice(i)) * new Vector<float>(b.Slice(i));
      }
      sum = Vector.Sum(acc);
    }

    for (; i < a.Length; i++)
    {
      sum += a[i] * b[i];
    }

    return sum;
  }

  /// <summary>Hamming-windowed sinc low-pass, normalised to unity gain at DC.</summary>
  private static float[] DesignLowPass(int sampleRate, float cutoffHz, int taps)
  {
    double fc = cutoffHz / sampleRate;
    int centre = taps / 2;
    double[] h = new double[taps];
    double sum = 0;
    for (int i = 0; i < taps; i++)
    {
      int n = i - centre;
      double sinc = n == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * n) / (Math.PI * n);
      double window = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (taps - 1));
      h[i] = sinc * window;
      sum += h[i];
    }

    float[] coefficients = new float[taps];
    for (int i = 0; i < taps; i++)
    {
      coefficients[i] = (float)(h[i] / sum);
    }
    return coefficients;
  }

  private static int Gcd(int a, int b)
  {
    while (b != 0)
    {
      (a, b) = (b, a % b);
    }
    return a;
  }
}
