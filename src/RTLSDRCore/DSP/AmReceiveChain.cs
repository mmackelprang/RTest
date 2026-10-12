using RTLSDRCore.Enums;
using RTLSDRCore.Models;

namespace RTLSDRCore.DSP;

/// <summary>
/// The receive chain for AM modulation (AUD-101): the AM and Shortwave bands, and Aircraft, which is
/// AM too. IQ at the SDR rate → offset shift, channel filter and decimation to 48 kHz
/// (<see cref="AmChannelizer"/>) → envelope detector (<see cref="AmDemodulator"/>) → audio
/// low-pass → AGC (<see cref="AgcProcessor"/>). It also measures the in-channel level that the
/// receiver reports as its signal strength on the HF bands (AM and Shortwave); on Aircraft the
/// receiver keeps reporting the wideband RMS (see <c>RadioReceiver.SignalStrength</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Offset.</b> The device is tuned <see cref="TuningOffsetHz"/> (60 kHz) below the wanted
/// frequency (<see cref="DeviceFrequencyFor"/>), so the device's DC offset and its low-frequency
/// spurs land 60 kHz away from the carrier instead of on it. librtlsdr refuses its own offset
/// tuning in direct-sampling mode, so the shift is done here. 60 kHz is well clear of DC; with
/// the widest channel this chain takes (AM, ±5 kHz, filter stop band from 7 kHz) the shifted
/// channel spans 53–67 kHz of the ±120 kHz capture at 240 kS/s, more than 50 kHz inside its edge.
/// (The RTL2832's own decimation filter response across that span has not been measured.) 60 kHz
/// is also exactly a quarter of 240 kS/s, so the mixer is a 4-entry table (1, −j, −1, +j) with no
/// rounding and no drift.
/// </para>
/// <para>
/// <b>Why a chain of its own</b> rather than an AM case in <c>RadioReceiver.CalculateRates</c>:
/// <c>CalculateRates</c> also sets the NFM path, which is out of scope here and must not change;
/// the generic <see cref="Decimator"/> evaluates its FIR for every input sample and has no mixer,
/// whereas <see cref="AmChannelizer"/> mixes, filters and decimates in one pass and evaluates the
/// FIR only for the one sample in five that is kept.
/// </para>
/// </remarks>
internal sealed class AmReceiveChain
{
  /// <summary>How far below the wanted frequency the device is tuned on AM-modulated bands, in Hz.</summary>
  public const int TuningOffsetHz = 60_000;

  /// <summary>The demodulator and audio rate, in Hz: the receiver's 48 kHz output, so no audio decimator follows.</summary>
  public const int DemodSampleRate = 48_000;

  /// <summary>The widest channel this chain takes, in Hz. Wider AM-modulated bands keep the generic path.</summary>
  public const int MaxChannelBandwidthHz = 20_000;

  /// <summary>Transition band of the channel filter, as a fraction of the channel bandwidth.</summary>
  public const float ChannelTransitionFraction = 0.4f;

  /// <summary>Transition band of the audio low-pass, in Hz.</summary>
  public const float AudioTransitionHz = 1_500f;

  /// <summary>AGC target: the output envelope (peak) the AGC holds, in full-scale units.</summary>
  public const float AgcTarget = 0.5f;

  /// <summary>AGC attack time constant, in seconds.</summary>
  public const double AgcAttackSeconds = 0.005;

  /// <summary>AGC decay time constant, in seconds.</summary>
  public const double AgcDecaySeconds = 1.0;

  /// <summary>The AGC's maximum gain (≈ +29.5 dB).</summary>
  public const float AgcMaxGain = 30f;

  /// <summary>The in-channel level that reads as signal strength 0, in dB relative to full scale.</summary>
  public const double StrengthFloorDbfs = -60.0;

  /// <summary>The span of in-channel level that signal strength 0 → 1 covers, in dB.</summary>
  public const double StrengthSpanDb = 60.0;

  private IqSample[] _channel;
  private int _channelCount;

  /// <summary>Creates the chain for a channel of <paramref name="channelBandwidthHz"/> at <paramref name="sdrSampleRate"/>.</summary>
  /// <param name="sdrSampleRate">The IQ rate; a multiple of <see cref="DemodSampleRate"/>.</param>
  /// <param name="channelBandwidthHz">The band's channel bandwidth (<c>RadioBand.DefaultBandwidthHz</c>).</param>
  /// <param name="maxBlockSamples">The largest IQ block expected per call; the buffers grow for a larger one.</param>
  public AmReceiveChain(int sdrSampleRate, int channelBandwidthHz, int maxBlockSamples)
  {
    if (sdrSampleRate % DemodSampleRate != 0)
    {
      throw new ArgumentException($"The SDR rate {sdrSampleRate} Hz is not a multiple of {DemodSampleRate} Hz", nameof(sdrSampleRate));
    }

    ChannelBandwidthHz = channelBandwidthHz;
    float halfBandwidth = channelBandwidthHz / 2f;

    Channelizer = new AmChannelizer(
      sdrSampleRate,
      TuningOffsetHz,
      cutoffHz: halfBandwidth,
      transitionHz: channelBandwidthHz * ChannelTransitionFraction,
      decimation: sdrSampleRate / DemodSampleRate);

    Demodulator = new AmDemodulator { SampleRate = DemodSampleRate, Bandwidth = channelBandwidthHz };

    Agc = new AgcProcessor
    {
      TargetLevel = AgcTarget,
      AttackRate = AgcProcessor.CoefficientFor(AgcAttackSeconds, DemodSampleRate),
      DecayRate = AgcProcessor.CoefficientFor(AgcDecaySeconds, DemodSampleRate),
      MaxGain = AgcMaxGain,
    };

    // The audio cut-off is the channel's half-bandwidth: about 5 kHz on AM, 3 kHz on Shortwave,
    // 4.2 kHz on Aircraft. Shortwave broadcasters sit 5 kHz apart, so its adjacent-carrier whistle
    // is at 5 kHz: inside a 5 kHz low-pass's transition band, but in the stop band of a 3 kHz one.
    AudioFilter = new LowPassFilter(DemodSampleRate, halfBandwidth,
      AmChannelizer.TapsFor(DemodSampleRate, AudioTransitionHz));

    int maxChannel = Channelizer.MaxOutputFor(maxBlockSamples);
    _channel = new IqSample[maxChannel];
    Audio = new float[maxChannel];
  }

  /// <summary>Gets the channel bandwidth this chain was built for, in Hz.</summary>
  public int ChannelBandwidthHz { get; }

  /// <summary>Gets the offset tuning, channel filter and decimation stage.</summary>
  public AmChannelizer Channelizer { get; }

  /// <summary>Gets the envelope detector.</summary>
  public AmDemodulator Demodulator { get; }

  /// <summary>Gets the AGC.</summary>
  public AgcProcessor Agc { get; }

  /// <summary>Gets the audio low-pass.</summary>
  public LowPassFilter AudioFilter { get; }

  /// <summary>Gets the audio buffer <see cref="ProduceAudio"/> writes to (reused between calls; replaced by a larger one if a block needs it).</summary>
  public float[] Audio { get; private set; }

  /// <summary>
  /// The tune generation (<c>RadioReceiver</c>'s count of tunes) of the last block passed to
  /// <see cref="BeginBlock"/>. Read and written only on the DSP thread.
  /// </summary>
  public int SeenTuneGeneration { get; private set; }

  /// <summary>
  /// Whether <see cref="ProduceAudio"/> re-seeds the audio state (DC blocker, audio low-pass, AGC)
  /// before its next block. True for a new chain.
  /// </summary>
  public bool ReseedPending { get; private set; } = true;

  // Set on the first block after a tune generation change (a block that may still hold the old
  // frequency); the next block re-seeds. See BeginBlock.
  private bool _awaitingPostTuneBlock;

  /// <summary>
  /// The signal strength of the last channelized block: the in-channel level L =
  /// 10·log10(mean |y|²) dBFS mapped linearly so that −60 dBFS reads 0 and 0 dBFS reads 1
  /// (<c>(L + 60) / 60</c>), never below 0. One step of 0.01 is 0.6 dB.
  /// </summary>
  public float Strength => LevelToStrength(Channelizer.LastMeanPower);

  /// <summary>Whether this chain receives <paramref name="modulation"/> at <paramref name="channelBandwidthHz"/>.</summary>
  /// <param name="modulation">The receiver's modulation.</param>
  /// <param name="channelBandwidthHz">The band's channel bandwidth, in Hz.</param>
  public static bool Applies(ModulationType modulation, int channelBandwidthHz) =>
    modulation == ModulationType.AM && channelBandwidthHz > 0 && channelBandwidthHz <= MaxChannelBandwidthHz;

  /// <summary>
  /// The one mapping from the frequency the receiver reports to the frequency the device is tuned
  /// to: <c>f − TuningOffsetHz</c> when the receiver demodulates with this chain, <c>f</c> otherwise.
  /// </summary>
  /// <param name="frequencyHz">The frequency the receiver reports, in Hz.</param>
  /// <param name="offsetTuned">Whether this chain is (or is about to be) the one demodulating.</param>
  public static long DeviceFrequencyFor(long frequencyHz, bool offsetTuned) =>
    offsetTuned ? frequencyHz - TuningOffsetHz : frequencyHz;

  /// <summary>Maps a mean in-channel power to a signal strength; see <see cref="Strength"/>.</summary>
  /// <param name="meanPower">Mean |y|², normalised IQ units.</param>
  public static float LevelToStrength(double meanPower)
  {
    if (meanPower <= 0)
    {
      return 0f;
    }

    double levelDbfs = 10.0 * Math.Log10(meanPower);
    return (float)Math.Max(0.0, (levelDbfs - StrengthFloorDbfs) / StrengthSpanDb);
  }

  /// <summary>Shifts, filters and decimates one IQ block into the chain's channel buffer.</summary>
  /// <param name="iq">IQ at the SDR rate.</param>
  public void Channelize(ReadOnlySpan<IqSample> iq)
  {
    int needed = Channelizer.MaxOutputFor(iq.Length);
    if (_channel.Length < needed)
    {
      _channel = new IqSample[needed];
      Audio = new float[needed];
    }
    _channelCount = Channelizer.Process(iq, _channel);
  }

  /// <summary>
  /// Called once per IQ block, before <see cref="Channelize"/>, with the tune generation the
  /// receiver stamped on the block when the USB thread raised it (not when the DSP thread took it
  /// from the queue). Decides when the audio state is re-seeded after a tune (AUD-101 review M1).
  /// </summary>
  /// <remarks>
  /// The first block stamped with a new generation was raised after the tune, but its samples may
  /// have been read from the device before the tune took effect (the read was in flight), so it can
  /// still hold the old station; it is processed with the old station's state. Blocks stamped with
  /// the old generation, however many were queued, never count. The next block was read only after
  /// that one had been raised, i.e. after the tune, so the audio
  /// state is re-seeded on it (<see cref="ReseedPending"/>). The same reasoning as the live band
  /// sweep's <c>LiveSweepTuner._dropNextBlock</c> in <c>RadioReceiver</c>.
  /// </remarks>
  /// <param name="tuneGeneration">The receiver's count of tunes, stamped when the block was raised.</param>
  public void BeginBlock(int tuneGeneration)
  {
    if (tuneGeneration != SeenTuneGeneration)
    {
      SeenTuneGeneration = tuneGeneration;
      _awaitingPostTuneBlock = true;
      return;
    }

    if (_awaitingPostTuneBlock)
    {
      _awaitingPostTuneBlock = false;
      ReseedPending = true;
    }
  }

  /// <summary>
  /// Records that the receiver delivered silence for this block instead of calling
  /// <see cref="ProduceAudio"/> (squelch closed, muted, or a band sweep's silencing). The audio
  /// state did not follow the signal meanwhile, so the next <see cref="ProduceAudio"/> re-seeds it.
  /// </summary>
  public void MarkSilenced() => ReseedPending = true;

  /// <summary>
  /// Demodulates the channel from the last <see cref="Channelize"/> call, applies the audio
  /// low-pass and then the AGC, and leaves the result in <see cref="Audio"/>.
  /// </summary>
  /// <remarks>
  /// When <see cref="ReseedPending"/> is set, the DC blocker, the audio low-pass and the AGC are
  /// reset first, and the first <see cref="SettleSamples"/> channel samples of the block are output
  /// as silence and kept out of the seeds: they come from the channel filter while its history
  /// still holds the previous block, which after a tune is the old station (or, for a new chain,
  /// zeros). The DC blocker is then seeded from the mean envelope and the AGC from the RMS of the
  /// rest of the block.
  /// </remarks>
  /// <returns>The number of audio samples in <see cref="Audio"/>.</returns>
  public int ProduceAudio()
  {
    int skip = 0;
    if (ReseedPending)
    {
      ReseedPending = false;
      Demodulator.Reset();
      AudioFilter.Reset();
      Agc.Reset();
      skip = Math.Min(SettleSamples, _channelCount);
      Array.Clear(Audio, 0, skip);
    }

    int count = skip + Demodulator.Demodulate(_channel.AsSpan(skip, _channelCount - skip), Audio.AsSpan(skip));
    Span<float> audio = Audio.AsSpan(skip, count - skip);
    for (int i = 0; i < audio.Length; i++)
    {
      audio[i] = AudioFilter.Process(audio[i]);
    }
    // After the low-pass, so the AGC levels what is heard, not the out-of-band products.
    Agc.Process(audio, audio);
    return count;
  }

  /// <summary>
  /// The number of channel samples (at <see cref="DemodSampleRate"/>) the channel filter takes to
  /// flush its history: its tap count divided by the decimation, rounded up (40 on AM, 67 on
  /// Shortwave, 48 on Aircraft; 0.8–1.4 ms).
  /// </summary>
  public int SettleSamples => (Channelizer.Taps + Channelizer.Decimation - 1) / Channelizer.Decimation;
}
