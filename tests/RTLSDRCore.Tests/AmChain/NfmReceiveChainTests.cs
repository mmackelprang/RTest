using RTLSDRCore.DSP;
using RTLSDRCore.Enums;
using Xunit.Abstractions;

namespace RTLSDRCore.Tests.AmChain;

/// <summary>
/// AUD-107: the narrowband FM chain (Weather, VHF) through the whole <see cref="RadioReceiver"/>:
/// the channel filter and decimation before the discriminator. Synthetic IQ built for the frequency
/// the fake device is tuned to (<see cref="SyntheticRf"/>), one block at a time, each block's audio
/// event awaited (<see cref="ReceiverHarness"/>): counted, not timed.
/// </summary>
public class NfmReceiveChainTests
{
  private const long WxHz = 162_475_000;    // NOAA WX4, mid-band
  private const long VhfHz = 146_520_000;   // the 2 m national simplex calling frequency

  /// <summary>Blocks fed before measuring: 12 × 16384 IQ samples ≈ 0.82 s at 240 kS/s.</summary>
  private const int Blocks = 12;

  /// <summary>
  /// The measured tail: 12000 samples = 250 ms at 48 kHz, a whole number of cycles of every tone
  /// measured here (1000, 1200, 1600, 2000, 3000 and 3200 Hz).
  /// </summary>
  private const int Window = 12_000;

  private readonly ITestOutputHelper _output;

  public NfmReceiveChainTests(ITestOutputHelper output)
  {
    _output = output;
  }

  private static SyntheticRf NfmStation(long rfHz, float amplitude, double toneHz = 1_000, double deviationHz = 3_000)
  {
    SyntheticRf rf = new();
    rf.Transmitters.Add(new Transmitter(rfHz, amplitude, ToneHz: toneHz, FmDeviationHz: deviationHz));
    return rf;
  }

  /// <summary>
  /// Everything in <paramref name="tail"/> that is not the <paramref name="toneHz"/> sine (noise,
  /// an interferer's tone, beats, distortion), in dB relative to that sine's power.
  /// </summary>
  private static double ResidualDb(float[] tail, double toneHz)
  {
    double tone = AudioMeasure.ToneAmplitude(tail, toneHz);
    double rms = AudioMeasure.Rms(tail);
    double tonePower = tone * tone / 2;
    return 10.0 * Math.Log10(Math.Max(rms * rms - tonePower, 1e-24) / tonePower);
  }

  private static float[] RunAndTakeTail(SyntheticRf rf, BandType band, long frequencyHz)
  {
    using ReceiverHarness h = new(rf, band, frequencyHz);
    h.Run(Blocks);
    return AudioMeasure.Tail(h.Audio, Window);
  }

  [Theory]
  [InlineData(BandType.Weather, WxHz)]
  [InlineData(BandType.VHF, VhfHz)]
  public void NfmTone_IsRecovered_AtTheDiscriminatorsScale(BandType band, long frequencyHz)
  {
    float[] tail = RunAndTakeTail(NfmStation(frequencyHz, 0.3f), band, frequencyHz);

    double tone = AudioMeasure.ToneAmplitude(tail, 1_000);
    double rms = AudioMeasure.Rms(tail);
    double second = AudioMeasure.ToneAmplitude(tail, 2_000);
    double third = AudioMeasure.ToneAmplitude(tail, 3_000);
    _output.WriteLine($"{band}: 1 kHz {tone:F4}, RMS {rms:F4}, H2 {AudioMeasure.Db(second / tone):F1} dB, H3 {AudioMeasure.Db(third / tone):F1} dB");

    // The discriminator reads 5 kHz deviation as 1.0, so 3 kHz reads 0.6: the level the generic
    // path produced at 240 kHz. Within 0.5 dB.
    Assert.InRange(AudioMeasure.Db(tone / 0.6), -0.5, 0.5);
    // The tone carries at least 99 % of the output power, and the harmonics are small.
    Assert.True(tone * tone / 2 >= 0.99 * rms * rms, $"1 kHz amplitude {tone:F4} is not dominant (RMS {rms:F4})");
    Assert.True(AudioMeasure.Db(second / tone) <= -40, $"2nd harmonic {AudioMeasure.Db(second / tone):F1} dB");
    Assert.True(AudioMeasure.Db(third / tone) <= -40, $"3rd harmonic {AudioMeasure.Db(third / tone):F1} dB");
  }

  [Fact]
  public void FullDeviation_ThreeKilohertzTone_PassesTheChannelFilterWithLowDistortion()
  {
    // The widest signal the channel is meant for: 5 kHz deviation at 3 kHz audio (Carson ±8 kHz).
    float[] tail = RunAndTakeTail(NfmStation(WxHz, 0.3f, toneHz: 3_000, deviationHz: 5_000), BandType.Weather, WxHz);

    double tone = AudioMeasure.ToneAmplitude(tail, 3_000);
    double rms = AudioMeasure.Rms(tail);
    _output.WriteLine($"3 kHz at 5 kHz deviation: {tone:F4} (expect 1.0), RMS {rms:F4}");

    Assert.InRange(AudioMeasure.Db(tone / 1.0), -1.0, 1.0);
    Assert.True(tone * tone / 2 >= 0.95 * rms * rms, $"3 kHz amplitude {tone:F4} is not dominant (RMS {rms:F4})");
  }

  [Theory]
  [InlineData(25_000, 0.9f)]     // NOAA Weather's spacing, an adjacent station 10 dB stronger
  [InlineData(-25_000, 0.9f)]
  public void AdjacentStation_TwentyFiveKilohertzAway_TenDbStronger_IsRejected(int spacingHz, float adjacentAmplitude)
  {
    SyntheticRf rf = NfmStation(WxHz, 0.3f, toneHz: 1_000);
    rf.Transmitters.Add(new Transmitter(WxHz + spacingHz, adjacentAmplitude, ToneHz: 1_600, FmDeviationHz: 3_000));

    float[] tail = RunAndTakeTail(rf, BandType.Weather, WxHz);

    double tone = AudioMeasure.ToneAmplitude(tail, 1_000);
    double interferer = AudioMeasure.ToneAmplitude(tail, 1_600);
    double residual = ResidualDb(tail, 1_000);
    _output.WriteLine($"{spacingHz:+#;-#} Hz: 1 kHz {tone:F4}, 1.6 kHz {AudioMeasure.Db(interferer / tone):F1} dB, residual {residual:F1} dB");

    // Without the channel filter the stronger station captures the discriminator: its 1.6 kHz
    // tone dominates and the wanted one is lost. With it, the wanted tone is at full level and
    // everything else, the adjacent station's tone included, is at least 40 dB under it.
    Assert.InRange(AudioMeasure.Db(tone / 0.6), -0.5, 0.5);
    Assert.True(AudioMeasure.Db(interferer / tone) <= -40, $"1.6 kHz is {AudioMeasure.Db(interferer / tone):F1} dB relative to 1 kHz");
    Assert.True(residual <= -40, $"the residual is {residual:F1} dB relative to the 1 kHz tone");
  }

  [Fact]
  public void AdjacentChannel_TwelvePointFiveKilohertzAway_EqualStrength_IsPartlyRejected()
  {
    // VHF's 12.5 kHz raster with a 2.5 kHz-deviation neighbour (the deviation 12.5 kHz channels
    // use) at the same strength. Its occupied band (±5.5 kHz for 3 kHz audio) reaches 7 kHz from
    // the wanted carrier, inside the filter's transition band, so this is partial rejection.
    SyntheticRf rf = NfmStation(VhfHz, 0.3f, toneHz: 1_000, deviationHz: 2_500);
    rf.Transmitters.Add(new Transmitter(VhfHz + 12_500, 0.3f, ToneHz: 1_600, FmDeviationHz: 2_500));

    float[] tail = RunAndTakeTail(rf, BandType.VHF, VhfHz);

    double tone = AudioMeasure.ToneAmplitude(tail, 1_000);
    double interferer = AudioMeasure.ToneAmplitude(tail, 1_600);
    double residual = ResidualDb(tail, 1_000);
    _output.WriteLine($"12.5 kHz: 1 kHz {tone:F4}, 1.6 kHz {AudioMeasure.Db(interferer / tone):F1} dB, residual {residual:F1} dB");

    Assert.InRange(AudioMeasure.Db(tone / 0.5), -1.0, 1.0);
    Assert.True(AudioMeasure.Db(interferer / tone) <= -20, $"1.6 kHz is {AudioMeasure.Db(interferer / tone):F1} dB relative to 1 kHz");
    Assert.True(residual <= -15, $"the residual is {residual:F1} dB relative to the 1 kHz tone");
  }

  [Fact]
  public void WidebandNoise_OutsideTheChannel_DoesNotReachTheAudio()
  {
    // White noise across the ±120 kHz capture, 3.5 dB under the carrier in total: under the
    // discriminator's threshold without a filter. About 1/15 of it falls in the ~16 kHz the
    // channel filter passes, so the carrier is about 15 dB over the noise the discriminator sees.
    SyntheticRf rf = NfmStation(WxHz, 0.3f);
    rf.NoiseRms = 0.2f;

    float[] tail = RunAndTakeTail(rf, BandType.Weather, WxHz);

    double tone = AudioMeasure.ToneAmplitude(tail, 1_000);
    double residual = ResidualDb(tail, 1_000);
    _output.WriteLine($"noisy: 1 kHz {tone:F4}, residual {residual:F1} dB");

    Assert.InRange(AudioMeasure.Db(tone / 0.6), -1.0, 1.0);
    Assert.True(residual <= -10, $"the residual is {residual:F1} dB relative to the 1 kHz tone");
  }

  [Fact]
  public void SignalStrength_OnNfm_IsStillTheWidebandRms()
  {
    // AUD-107 changes the audio only. The squelch and the seek stop keep the wideband RMS scale
    // (RadioReceiver.WidebandSignalStrength): a 0.3 carrier with an equal one 50 kHz away reads
    // their combined RMS, sqrt(0.3² + 0.3²) ≈ 0.42, not the in-channel 0.3.
    SyntheticRf rf = NfmStation(WxHz, 0.3f);
    rf.Transmitters.Add(new Transmitter(WxHz + 50_000, 0.3f));
    using ReceiverHarness h = new(rf, BandType.Weather, WxHz);
    h.Run(4);

    Assert.InRange(h.Receiver.SignalStrength, 0.40f, 0.44f);
    Assert.True(h.Receiver.GetRadioState().SquelchOpen);
  }

  [Fact]
  public void NoiseOnly_BelowTheSquelch_DeliversSilence_OfTheChainsBlockSize()
  {
    SyntheticRf rf = new() { NoiseRms = 0.02f };   // wideband RMS 0.02, under the 0.1 default squelch
    using ReceiverHarness h = new(rf, BandType.Weather, WxHz);
    h.Run(4);

    Assert.All(h.SilentEvents, Assert.True);
    // 16384 IQ samples at 240 kS/s → 3276 audio samples at 48 kHz, mono.
    Assert.All(h.Events, e => Assert.Equal(16_384 / 5, e.Length));
  }

  [Fact]
  public void TheDeviceIsTunedOnTheFrequency_NoOffset()
  {
    SyntheticRf rf = NfmStation(WxHz, 0.3f);
    using ReceiverHarness h = new(rf, BandType.Weather, WxHz);
    h.Run(2);

    Assert.All(rf.BlockCentres, c => Assert.Equal(WxHz, c));
  }

  [Fact]
  public void SetBand_FromAm_ToWeather_ReceivesWithTheNfmChain()
  {
    // A band change on a running receiver replaces the AM chain with the NFM one. ProcessSamples
    // prefers the AM chain, so one left behind would envelope-detect the FM station.
    SyntheticRf rf = NfmStation(WxHz, 0.3f);
    rf.Transmitters.Add(new Transmitter(1_000_000, 0.3f, ToneHz: 1_000, AmDepth: 0.5));
    using ReceiverHarness h = new(rf, BandType.AM, 1_000_000);
    h.Run(3);

    Assert.True(h.Receiver.SetBand(BandType.Weather, WxHz));
    h.Run(Blocks);

    float[] tail = AudioMeasure.Tail(h.Audio, Window);
    Assert.InRange(AudioMeasure.Db(AudioMeasure.ToneAmplitude(tail, 1_000) / 0.6), -0.5, 0.5);
    Assert.True(ResidualDb(tail, 1_000) <= -40, $"the residual is {ResidualDb(tail, 1_000):F1} dB");
  }

  [Fact]
  public void SetBand_FromWeather_ToFm_LeavesTheNfmChain_ForStereoWfm()
  {
    SyntheticRf rf = NfmStation(WxHz, 0.3f);
    rf.Transmitters.Add(new Transmitter(99_500_000, 0.5f, ToneHz: 1_000, FmDeviationHz: 50_000));
    using ReceiverHarness h = new(rf, BandType.Weather, WxHz);
    h.Run(2);
    int before = h.Events.Length;

    Assert.True(h.Receiver.SetBand(BandType.FM, 99_500_000));
    h.Run(3);

    // WFM delivers interleaved stereo (two samples per 48 kHz frame); the NFM chain delivers
    // 16384 / 5 = 3276 mono samples per block.
    Assert.All(h.Events.Skip(before + 1), e => Assert.True(e.Length > 6_000, $"{e.Length} samples: not stereo WFM"));
  }

  [Fact]
  public void Applies_OnlyToNarrowbandFm()
  {
    Assert.True(NfmReceiveChain.Applies(ModulationType.NFM, 12_500));
    Assert.False(NfmReceiveChain.Applies(ModulationType.NFM, 200_000));   // NFM on a broadcast-width band keeps the generic path
    Assert.False(NfmReceiveChain.Applies(ModulationType.WFM, 200_000));
    Assert.False(NfmReceiveChain.Applies(ModulationType.AM, 10_000));
  }
}
