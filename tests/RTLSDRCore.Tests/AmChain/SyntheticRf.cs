using RTLSDRCore.Models;

namespace RTLSDRCore.Tests.AmChain;

/// <summary>
/// One transmitter for <see cref="SyntheticRf"/>: a carrier at an absolute RF frequency, amplitude-
/// or frequency-modulated by one sine tone.
/// </summary>
/// <param name="RfHz">Carrier frequency, in Hz (absolute, not relative to the device centre).</param>
/// <param name="Amplitude">Carrier amplitude (complex magnitude, normalised IQ units).</param>
/// <param name="ToneHz">Modulating tone, in Hz; 0 for an unmodulated carrier.</param>
/// <param name="AmDepth">AM depth (0.5 = 50 %). Ignored when <paramref name="FmDeviationHz"/> is set.</param>
/// <param name="FmDeviationHz">FM peak deviation, in Hz; 0 for AM.</param>
/// <param name="PilotDeviationHz">
/// FM only: the deviation a 19 kHz stereo pilot adds, in Hz (broadcast FM uses about 10 % of 75 kHz,
/// 7.5 kHz); 0 for a mono station (AUD-106).
/// </param>
internal sealed record Transmitter(long RfHz, float Amplitude, double ToneHz = 0, double AmDepth = 0, double FmDeviationHz = 0,
  double PilotDeviationHz = 0);

/// <summary>
/// Synthesises the IQ a device tuned to a given centre would capture from a set of transmitters,
/// a DC component at the device centre and white Gaussian noise. Time runs on across blocks (one
/// sample counter for the whole run), so a carrier keeps its phase from block to block, and every
/// block is built for the frequency the device is actually tuned to: a transmitter at
/// <c>RfHz</c> appears at <c>RfHz - deviceHz</c> in the block.
/// </summary>
internal sealed class SyntheticRf
{
  public const int SampleRate = 240_000;

  private readonly Random _random;
  private long _n;

  public SyntheticRf(int seed = 4242)
  {
    _random = new Random(seed);
  }

  /// <summary>The transmitters on the air. Tests may replace one between blocks (the DSP thread does not read it).</summary>
  public List<Transmitter> Transmitters { get; } = new();

  /// <summary>Constant DC offset at the device centre (I component; the spike of a direct-conversion front end).</summary>
  public float DcLevel { get; set; }

  /// <summary>Depth of a sinusoidal ripple on the DC level (0.5 = the DC swings by +-50 %).</summary>
  public float DcRippleDepth { get; set; }

  /// <summary>Frequency of the DC ripple, in Hz (e.g. mains hum coupling into the DC offset).</summary>
  public double DcRippleHz { get; set; } = 60;

  /// <summary>RMS magnitude of the complex white Gaussian noise (E|n|^2 = NoiseRms^2).</summary>
  public float NoiseRms { get; set; }

  /// <summary>The device centre frequency of every block built so far, in order.</summary>
  public List<long> BlockCentres { get; } = new();

  /// <summary>Builds the next <paramref name="count"/> samples as captured at <paramref name="deviceHz"/>.</summary>
  public IqSample[] Block(long deviceHz, int count)
  {
    BlockCentres.Add(deviceHz);
    IqSample[] block = new IqSample[count];
    double noiseSigma = NoiseRms / Math.Sqrt(2.0);
    for (int k = 0; k < count; k++)
    {
      double t = (double)(_n + k) / SampleRate;
      double i = 0;
      double q = 0;

      foreach (Transmitter tx in Transmitters)
      {
        double offsetHz = tx.RfHz - deviceHz;
        double phase = 2.0 * Math.PI * offsetHz * t;
        double amplitude = tx.Amplitude;
        if (tx.FmDeviationHz > 0 && tx.ToneHz > 0)
        {
          phase -= tx.FmDeviationHz / tx.ToneHz * Math.Cos(2.0 * Math.PI * tx.ToneHz * t);
          if (tx.PilotDeviationHz > 0)
          {
            phase -= tx.PilotDeviationHz / 19_000.0 * Math.Cos(2.0 * Math.PI * 19_000.0 * t);
          }
        }
        else if (tx.ToneHz > 0)
        {
          amplitude *= 1.0 + tx.AmDepth * Math.Cos(2.0 * Math.PI * tx.ToneHz * t);
        }
        i += amplitude * Math.Cos(phase);
        q += amplitude * Math.Sin(phase);
      }

      if (DcLevel != 0)
      {
        i += DcLevel * (1.0 + DcRippleDepth * Math.Sin(2.0 * Math.PI * DcRippleHz * t));
      }

      if (noiseSigma > 0)
      {
        i += noiseSigma * Gaussian();
        q += noiseSigma * Gaussian();
      }

      block[k] = new IqSample((float)i, (float)q);
    }

    _n += count;
    return block;
  }

  private double Gaussian()
  {
    // Box-Muller; 1 - NextDouble() keeps the log argument in (0, 1].
    double u1 = 1.0 - _random.NextDouble();
    double u2 = _random.NextDouble();
    return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
  }
}
