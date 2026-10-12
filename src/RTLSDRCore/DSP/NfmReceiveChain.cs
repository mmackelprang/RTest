using RTLSDRCore.Enums;
using RTLSDRCore.Models;

namespace RTLSDRCore.DSP;

/// <summary>
/// The receive chain for narrowband FM (AUD-107): the Weather and VHF bands. IQ at the SDR rate →
/// channel filter and decimation to 48 kHz (<see cref="IqChannelizer"/>, no offset) → FM
/// discriminator (<see cref="NfmDemodulator"/>) at 48 kHz, mono, no audio decimator.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> Before AUD-107 the generic path ran NFM at SDR rate = demod rate = 240 kHz
/// (<c>RadioReceiver.CalculateRates</c>), so no IQ filter was built and the discriminator heard the
/// whole ±120 kHz capture: an adjacent station and the wideband noise reached it, and a stronger
/// adjacent station captured it outright. The channel is now filtered before the discriminator,
/// with the channelizer <c>AUD-101</c> built for AM.
/// </para>
/// <para>
/// <b>The filter.</b> Cut-off (−6 dB) at <see cref="ChannelCutoffFraction"/> of the band's channel
/// bandwidth and a transition band of <see cref="ChannelTransitionFraction"/> of it: for the
/// 12.5 kHz Weather and VHF channels, flat to 6 kHz, −6 dB at 8 kHz and in the stop band from
/// 10 kHz. That passes a 5 kHz-deviation signal's occupied band (Carson: ±(5 + 3) kHz for 3 kHz
/// audio) with little loss, and the whole of a station 25 kHz away (NOAA Weather's spacing; from
/// 17 kHz) falls in the stop band. A station 12.5 kHz away with the same deviation overlaps the
/// channel and is only partly rejected; the tests state both numbers.
/// </para>
/// <para>
/// <b>No offset.</b> NFM is tuned zero-IF as before, so the device frequency mapping, the band
/// sweep and seek are unchanged. The bands are tuner-mode (not direct sampling), whose DC offset is
/// small; it lands on the carrier, as it did before.
/// </para>
/// <para>
/// <b>The level is not taken from here.</b> The receiver keeps the wideband RMS as the signal
/// strength on these bands, so the squelch and the seek stop are on the same scale as before
/// (<c>AUD-101</c> found in-channel levels read hot on tuner-mode bands; see
/// <c>RadioReceiver.SignalStrength</c>). <see cref="IqChannelizer.LastMeanPower"/> is available but
/// unused.
/// </para>
/// <para>
/// <b>The audio level is unchanged.</b> The discriminator's gain is <c>fs / (2π · 5 kHz)</c>, so a
/// 5 kHz deviation reads 1.0 at any sample rate; moving it from 240 kHz to 48 kHz keeps the scale.
/// </para>
/// </remarks>
internal sealed class NfmReceiveChain
{
  /// <summary>The demodulator and audio rate, in Hz: the receiver's 48 kHz output.</summary>
  public const int DemodSampleRate = 48_000;

  /// <summary>The widest channel this chain takes, in Hz. Wider FM keeps the generic path.</summary>
  public const int MaxChannelBandwidthHz = 25_000;

  /// <summary>The channel filter's −6 dB cut-off, as a fraction of the channel bandwidth (8 kHz for 12.5 kHz).</summary>
  public const float ChannelCutoffFraction = 0.64f;

  /// <summary>The channel filter's transition band, as a fraction of the channel bandwidth (4 kHz for 12.5 kHz).</summary>
  public const float ChannelTransitionFraction = 0.32f;

  private IqSample[] _channel;

  /// <summary>Creates the chain for a channel of <paramref name="channelBandwidthHz"/> at <paramref name="sdrSampleRate"/>.</summary>
  /// <param name="sdrSampleRate">The IQ rate; a multiple of <see cref="DemodSampleRate"/>.</param>
  /// <param name="channelBandwidthHz">The band's channel bandwidth (<c>RadioBand.DefaultBandwidthHz</c>).</param>
  /// <param name="maxBlockSamples">The largest IQ block expected per call; the buffers grow for a larger one.</param>
  public NfmReceiveChain(int sdrSampleRate, int channelBandwidthHz, int maxBlockSamples)
  {
    if (sdrSampleRate % DemodSampleRate != 0)
    {
      throw new ArgumentException($"The SDR rate {sdrSampleRate} Hz is not a multiple of {DemodSampleRate} Hz", nameof(sdrSampleRate));
    }

    ChannelBandwidthHz = channelBandwidthHz;
    Channelizer = new IqChannelizer(
      sdrSampleRate,
      offsetHz: 0,
      cutoffHz: channelBandwidthHz * ChannelCutoffFraction,
      transitionHz: channelBandwidthHz * ChannelTransitionFraction,
      decimation: sdrSampleRate / DemodSampleRate);

    Demodulator = new NfmDemodulator { SampleRate = DemodSampleRate, Bandwidth = channelBandwidthHz };

    int maxChannel = Channelizer.MaxOutputFor(maxBlockSamples);
    _channel = new IqSample[maxChannel];
    Audio = new float[maxChannel];
  }

  /// <summary>Gets the channel bandwidth this chain was built for, in Hz.</summary>
  public int ChannelBandwidthHz { get; }

  /// <summary>Gets the channel filter and decimation stage.</summary>
  public IqChannelizer Channelizer { get; }

  /// <summary>Gets the FM discriminator.</summary>
  public NfmDemodulator Demodulator { get; }

  /// <summary>Gets the audio buffer <see cref="Process"/> writes to (reused between calls; replaced by a larger one if a block needs it).</summary>
  public float[] Audio { get; private set; }

  /// <summary>Whether this chain receives <paramref name="modulation"/> at <paramref name="channelBandwidthHz"/>.</summary>
  /// <param name="modulation">The receiver's modulation.</param>
  /// <param name="channelBandwidthHz">The band's channel bandwidth, in Hz.</param>
  public static bool Applies(ModulationType modulation, int channelBandwidthHz) =>
    modulation == ModulationType.NFM && channelBandwidthHz > 0 && channelBandwidthHz <= MaxChannelBandwidthHz;

  /// <summary>Filters, decimates and demodulates one IQ block into <see cref="Audio"/>.</summary>
  /// <param name="iq">IQ at the SDR rate.</param>
  /// <returns>The number of audio samples in <see cref="Audio"/>.</returns>
  public int Process(ReadOnlySpan<IqSample> iq)
  {
    int needed = Channelizer.MaxOutputFor(iq.Length);
    if (_channel.Length < needed)
    {
      _channel = new IqSample[needed];
      Audio = new float[needed];
    }

    int channelCount = Channelizer.Process(iq, _channel);
    return Demodulator.Demodulate(_channel.AsSpan(0, channelCount), Audio);
  }
}
