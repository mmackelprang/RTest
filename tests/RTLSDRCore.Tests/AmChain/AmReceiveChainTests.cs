using RTLSDRCore.DSP;
using RTLSDRCore.Enums;
using RTLSDRCore.Models;
using RTLSDRCore.Sweep;
using RTLSDRCore.Tests.Sweep;

namespace RTLSDRCore.Tests.AmChain;

/// <summary>
/// AUD-101: the AM-modulation chain through the whole <see cref="RadioReceiver"/>: software offset
/// tuning, the channel filter and decimation, the applied AGC, the audio low-pass and the in-channel
/// level. Every test feeds synthetic IQ built for the frequency the receiver actually tuned the fake
/// device to (<see cref="SyntheticRf"/>), one block at a time, and waits for each block's audio
/// event (<see cref="ReceiverHarness"/>): counted, not timed.
/// </summary>
public class AmReceiveChainTests
{
  private const long AmHz = 1_000_000;
  private const long SwHz = 6_000_000;
  private const long AirHz = 120_000_000;
  private const long FmHz = 99_500_000;
  private const long Offset = AmReceiveChain.TuningOffsetHz;

  /// <summary>Blocks fed before measuring: 12 × 16384 IQ samples ≈ 0.82 s at 240 kS/s.</summary>
  private const int Blocks = 12;

  /// <summary>
  /// The measured tail: 12000 samples = 250 ms at 48 kHz, a whole number of cycles of every tone
  /// measured here (60, 120, 1000, 3000 and 10000 Hz).
  /// </summary>
  private const int Window = 12_000;

  private static float[] RunAndTakeTail(SyntheticRf rf, BandType band, long frequencyHz, out ReceiverHarness harness)
  {
    harness = new ReceiverHarness(rf, band, frequencyHz);
    harness.Run(Blocks);
    return AudioMeasure.Tail(harness.Audio, Window);
  }

  private static SyntheticRf AmStation(long rfHz, float amplitude, double toneHz = 1_000, double depth = 0.5)
  {
    SyntheticRf rf = new();
    rf.Transmitters.Add(new Transmitter(rfHz, amplitude, ToneHz: toneHz, AmDepth: depth));
    return rf;
  }

  // (a) ------------------------------------------------------------------------------------------

  [Theory]
  [InlineData(BandType.AM, AmHz)]
  [InlineData(BandType.Shortwave, SwHz)]
  public void AmTone_IsRecovered_AtALevelIndependentOfCarrierStrength(BandType band, long frequencyHz)
  {
    // Two carriers 20 dB apart, both inside the AGC's range (the 50 % tone needs a gain of 1/A,
    // under the maximum of 30 for A >= 0.033).
    double[] toneDb = new double[2];
    float[] amplitudes = { 0.04f, 0.4f };
    for (int k = 0; k < amplitudes.Length; k++)
    {
      float[] tail = RunAndTakeTail(AmStation(frequencyHz, amplitudes[k]), band, frequencyHz, out ReceiverHarness h);
      using (h)
      {
        double tone = AudioMeasure.ToneAmplitude(tail, 1_000);
        double rms = AudioMeasure.Rms(tail);
        // Dominant: the 1 kHz sine carries at least 90 % of the output power.
        Assert.True(tone * tone / 2 >= 0.9 * rms * rms,
          $"A={amplitudes[k]}: 1 kHz amplitude {tone:F4} is not dominant (RMS {rms:F4})");
        toneDb[k] = AudioMeasure.Db(tone);
      }
    }

    // AGC: 20 dB of carrier strength moves the output by less than 1 dB.
    Assert.True(Math.Abs(toneDb[1] - toneDb[0]) < 1.0,
      $"1 kHz level {toneDb[0]:F2} dBFS at A=0.04 and {toneDb[1]:F2} dBFS at A=0.4");
    // And it is held near the AGC target (0.5 peak, -6 dBFS).
    Assert.InRange(toneDb[0], -9.0, -4.0);
  }

  // (b) ------------------------------------------------------------------------------------------

  [Fact]
  public void StrongerCarrier_TenKilohertzAway_IsRejected_ByAtLeast40Db()
  {
    SyntheticRf rf = AmStation(AmHz, 0.1f);
    // 6 dB stronger than the wanted carrier, modulated at 3 kHz.
    rf.Transmitters.Add(new Transmitter(AmHz + 10_000, 0.2f, ToneHz: 3_000, AmDepth: 0.5));

    float[] tail = RunAndTakeTail(rf, BandType.AM, AmHz, out ReceiverHarness h);
    using (h)
    {
      double tone = AudioMeasure.ToneAmplitude(tail, 1_000);
      double interferer = AudioMeasure.ToneAmplitude(tail, 3_000);
      double whistle = AudioMeasure.ToneAmplitude(tail, 10_000);

      Assert.True(AudioMeasure.Db(interferer / tone) <= -40,
        $"3 kHz is {AudioMeasure.Db(interferer / tone):F1} dB relative to the 1 kHz tone");
      Assert.True(AudioMeasure.Db(whistle / tone) <= -40,
        $"10 kHz is {AudioMeasure.Db(whistle / tone):F1} dB relative to the 1 kHz tone");
    }
  }

  // (c) ------------------------------------------------------------------------------------------

  [Fact]
  public void DcOffsetAtTheDeviceCentre_GivesNoHum_Above40DbUnderTheTone()
  {
    SyntheticRf rf = AmStation(AmHz, 0.1f);
    // A DC offset at the device centre twice the carrier, rippling +-50 % at 60 Hz.
    rf.DcLevel = 0.2f;
    rf.DcRippleDepth = 0.5f;
    rf.DcRippleHz = 60;

    float[] tail = RunAndTakeTail(rf, BandType.AM, AmHz, out ReceiverHarness h);
    using (h)
    {
      double tone = AudioMeasure.ToneAmplitude(tail, 1_000);
      foreach (double humHz in new[] { 60.0, 120.0 })
      {
        double hum = AudioMeasure.ToneAmplitude(tail, humHz);
        Assert.True(AudioMeasure.Db(hum / tone) <= -40,
          $"{humHz} Hz is {AudioMeasure.Db(hum / tone):F1} dB relative to the 1 kHz tone");
      }
    }
  }

  // (d) ------------------------------------------------------------------------------------------

  private static double LevelDbfs(float strength) =>
    strength * AmReceiveChain.StrengthSpanDb + AmReceiveChain.StrengthFloorDbfs;

  private static float StrengthAfter(SyntheticRf rf, BandType band, long frequencyHz)
  {
    using ReceiverHarness h = new(rf, band, frequencyHz);
    h.Run(3);
    return h.Receiver.SignalStrength;
  }

  [Fact]
  public void InChannelLevel_OfATunedCarrier_IsAtLeast20DbAboveAnEmptyChannelBesideAStrongOne()
  {
    SyntheticRf tuned = new();
    tuned.Transmitters.Add(new Transmitter(AmHz, 0.05f));
    float tunedStrength = StrengthAfter(tuned, BandType.AM, AmHz);

    // Nothing on AmHz; a carrier ten times stronger on the next channel.
    SyntheticRf empty = new();
    empty.Transmitters.Add(new Transmitter(AmHz + 10_000, 0.5f));
    float emptyStrength = StrengthAfter(empty, BandType.AM, AmHz);

    double tunedDb = LevelDbfs(tunedStrength);
    double emptyDb = LevelDbfs(emptyStrength);
    Assert.True(tunedDb - emptyDb >= 20, $"tuned {tunedDb:F1} dBFS, empty channel {emptyDb:F1} dBFS");
    // An unmodulated carrier of amplitude A reads 20·log10(A): -26.0 dBFS.
    Assert.InRange(tunedDb, -26.5, -25.5);
  }

  [Fact]
  public void InChannelLevel_ResolvesAOneDecibelStep()
  {
    float a = 0.01f;
    SyntheticRf lower = new();
    lower.Transmitters.Add(new Transmitter(AmHz, a));
    SyntheticRf higher = new();
    higher.Transmitters.Add(new Transmitter(AmHz, (float)(a * Math.Pow(10, 1.0 / 20))));

    double step = LevelDbfs(StrengthAfter(higher, BandType.AM, AmHz)) - LevelDbfs(StrengthAfter(lower, BandType.AM, AmHz));

    Assert.InRange(step, 0.9, 1.1);
  }

  // (e) ------------------------------------------------------------------------------------------

  [Fact]
  public void Wfm_Output_IsUnchanged()
  {
    // Golden values recorded on main c61bd0d (before AUD-101) with exactly this input. The FM path
    // is not touched by AUD-101; this pins it. Tolerances allow for last-bit differences between
    // platforms' math libraries (MathF.Atan2), nothing more.
    SyntheticRf rf = new();
    rf.Transmitters.Add(new Transmitter(FmHz, 0.5f, ToneHz: 1_000, FmDeviationHz: 50_000));
    using ReceiverHarness h = new(rf, BandType.FM, FmHz);
    h.Run(8);
    float[] audio = h.Audio;

    double sumSquares = 0;
    foreach (float v in audio)
    {
      sumSquares += (double)v * v;
    }

    Assert.Equal(52_424, audio.Length);
    Assert.Equal(9522.98320390176, sumSquares, 9522.98320390176 * 1e-4);
    Assert.Equal(-0.034194663, audio[100], 1e-4);
    Assert.Equal(-0.40148884, audio[1001], 1e-4);
    Assert.Equal(-0.45001084, audio[10000], 1e-4);
    Assert.Equal(-0.028814401, audio[30001], 1e-4);
    Assert.Equal(0.5127654, audio[50000], 1e-4);
    Assert.Equal(-0.24780518, audio[52423], 1e-4);
    Assert.All(rf.BlockCentres, c => Assert.Equal(FmHz, c));
  }

  // (f) ------------------------------------------------------------------------------------------

  private static RadioReceiver StartOn(FakeSdrDevice device, BandType band, long frequencyHz)
  {
    RadioReceiver receiver = new(device);
    Assert.True(receiver.SetBand(band, frequencyHz));
    Assert.True(receiver.Startup());
    return receiver;
  }

  private static List<long> TunesSince(FakeSdrDevice device, int from) =>
    device.Calls.Skip(from)
      .Where(c => c.StartsWith("SetFrequency:", StringComparison.Ordinal))
      .Select(c => long.Parse(c["SetFrequency:".Length..], System.Globalization.CultureInfo.InvariantCulture))
      .ToList();

  [Theory]
  [InlineData(BandType.AM, AmHz)]
  [InlineData(BandType.Shortwave, SwHz)]
  [InlineData(BandType.Aircraft, AirHz)]
  public void Startup_OnAnAmModulatedBand_TunesTheDeviceBelowTheFrequency(BandType band, long frequencyHz)
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, band, frequencyHz);

    Assert.Equal(new[] { frequencyHz - Offset }, device.SetFrequencyCalls);
    Assert.Equal(frequencyHz, receiver.CurrentFrequency);
  }

  [Fact]
  public void Startup_RetryOfTheFirstTune_UsesTheOffsetToo()
  {
    int tunes = 0;
    FakeSdrDevice device = new() { SetFrequencyResult = _ => ++tunes > 1 };
    using RadioReceiver receiver = StartOn(device, BandType.AM, AmHz);

    Assert.Equal(new[] { AmHz - Offset, AmHz - Offset }, device.SetFrequencyCalls);
  }

  [Fact]
  public void InBandTunes_OnAm_UseTheOffset()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.AM, AmHz);
    int before = device.Calls.Count;

    Assert.True(receiver.SetFrequency(AmHz + 10_000));
    Assert.True(receiver.SetFrequencyInBand(AmHz + 20_000));
    Assert.True(receiver.TuneFrequencyUp(10_000));
    Assert.True(receiver.TuneFrequencyDown(10_000));

    Assert.Equal(
      new[] { AmHz + 10_000 - Offset, AmHz + 20_000 - Offset, AmHz + 30_000 - Offset, AmHz + 20_000 - Offset },
      TunesSince(device, before));
    Assert.Equal(AmHz + 20_000, receiver.CurrentFrequency);
  }

  [Theory]
  [InlineData(BandType.AM, AmHz)]
  [InlineData(BandType.Shortwave, SwHz)]
  [InlineData(BandType.Aircraft, AirHz)]
  public void SetBand_FromFm_ToAnAmModulatedBand_TunesBelowTheFrequency(BandType band, long frequencyHz)
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.FM, FmHz);
    int before = device.Calls.Count;

    Assert.True(receiver.SetBand(band, frequencyHz));

    Assert.Equal(new[] { frequencyHz - Offset }, TunesSince(device, before));
    Assert.Equal(frequencyHz, receiver.CurrentFrequency);
  }

  [Fact]
  public void SetBand_FromAm_ToFm_PreTunesAndTunesWithoutTheOffset()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.AM, AmHz);
    int before = device.Calls.Count;

    Assert.True(receiver.SetBand(BandType.FM, FmHz));

    // The M1 pre-tune (HF still on) and the tune, both at the FM frequency itself.
    Assert.Equal(new[] { FmHz, FmHz }, TunesSince(device, before));
  }

  [Fact]
  public void SetBand_FromAm_ToAircraft_PreTunesAndTunesWithTheOffset()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.AM, AmHz);
    int before = device.Calls.Count;

    Assert.True(receiver.SetBand(BandType.Aircraft, AirHz));

    Assert.Equal(new[] { AirHz - Offset, AirHz - Offset }, TunesSince(device, before));
  }

  [Fact]
  public void SetBand_FromAm_RefusedHfOff_PutsTheDeviceBackBelowTheAmFrequency()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.AM, AmHz);
    device.HfSwitchResult = false;
    int before = device.Calls.Count;

    Assert.False(receiver.SetBand(BandType.FM, FmHz));

    Assert.Equal(new[] { FmHz, AmHz - Offset }, TunesSince(device, before));
    Assert.Equal(AmHz - Offset, device.GetFrequency());
    Assert.Equal(AmHz, receiver.CurrentFrequency);
  }

  [Fact]
  public void LiveSweep_OnAm_HopsOnItsOwnCentres_ThenRetunesTheRadioWithTheOffset()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.AM, AmHz);
    receiver.SweepAwaitingSamples = device.RaiseBlock;
    BandSweepPlan plan = HfDirectSamplingTests.AmPlan();
    int before = device.Calls.Count;

    receiver.SweepPlan(plan, 28f, null, CancellationToken.None);

    List<long> tunes = TunesSince(device, before);
    Assert.True(tunes.Count >= 2);
    // The sweep's own hops: the plan's centres, not shifted by the radio's offset.
    Assert.Equal(plan.Tunes.Select(t => t.CentreHz), tunes.Take(tunes.Count - 1));
    // The cleanup retune of the radio: shifted.
    Assert.Equal(AmHz - Offset, tunes[^1]);
  }

  [Fact]
  public void SetModulation_IntoAndOutOfAm_RetunesTheDeviceCentre()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.AM, AmHz);
    int before = device.Calls.Count;

    Assert.True(receiver.SetModulation(ModulationType.NFM));
    Assert.True(receiver.SetModulation(ModulationType.AM));

    Assert.Equal(new[] { AmHz, AmHz - Offset }, TunesSince(device, before));
  }

  [Fact]
  public void Fm_IsNeverOffset()
  {
    FakeSdrDevice device = new();
    using RadioReceiver receiver = StartOn(device, BandType.FM, FmHz);

    Assert.True(receiver.SetFrequency(FmHz + 200_000));
    Assert.True(receiver.TuneFrequencyUp(100_000));
    Assert.True(receiver.SetBand(BandType.Weather, 162_400_000));
    Assert.True(receiver.SetBand(BandType.VHF, 146_520_000));

    Assert.Equal(new[] { FmHz, FmHz + 200_000, FmHz + 300_000, 162_400_000L, 146_520_000L }, device.SetFrequencyCalls);
  }

  // (g) ------------------------------------------------------------------------------------------

  [Theory]
  [InlineData(BandType.AM, AmHz, -47.8)]
  [InlineData(BandType.Shortwave, SwHz, -50.0)]
  public void NoiseOnly_OnAnHfBand_PlaysLowLevelNoise_NotSilence(BandType band, long frequencyHz, double expectedLevelDbfs)
  {
    // White noise at the box's measured direct-sampling level: about 0.02 RMS over the capture.
    SyntheticRf rf = new() { NoiseRms = 0.02f };
    using ReceiverHarness h = new(rf, band, frequencyHz);
    h.Run(Blocks);
    float[] audio = h.Audio;

    // One event per block, none of them silence, and every IQ sample decimated (16384 / 5 per block).
    Assert.Equal(Enumerable.Repeat(false, Blocks), h.SilentEvents);
    Assert.Equal(Blocks * 16_384 / 5, audio.Length);
    double rmsDb = AudioMeasure.Db(AudioMeasure.Rms(AudioMeasure.Tail(audio, Window)));
    // Not silence, and held down by the AGC's maximum gain (30) rather than raised to the target.
    Assert.InRange(rmsDb, -40.0, -15.0);
    // The in-channel noise level: -34 dBFS over 240 kHz, about 14-16 dB less in a 10 or 6 kHz channel.
    Assert.InRange(LevelDbfs(h.Receiver.SignalStrength), expectedLevelDbfs - 2, expectedLevelDbfs + 2);
    Assert.True(h.Receiver.GetRadioState().SquelchOpen);
  }

  // (h) ------------------------------------------------------------------------------------------

  [Fact]
  public void Aircraft_AmVoice_IsRecovered_WithItsSquelchOpen()
  {
    SyntheticRf rf = AmStation(AirHz, 0.3f);
    rf.NoiseRms = 0.05f;

    float[] tail = RunAndTakeTail(rf, BandType.Aircraft, AirHz, out ReceiverHarness h);
    using (h)
    {
      double tone = AudioMeasure.ToneAmplitude(tail, 1_000);
      double rms = AudioMeasure.Rms(tail);
      Assert.True(tone * tone / 2 >= 0.9 * rms * rms, $"1 kHz amplitude {tone:F4}, RMS {rms:F4}");
      Assert.True(h.Receiver.GetRadioState().SquelchOpen);
      Assert.All(rf.BlockCentres, c => Assert.Equal(AirHz - Offset, c));
    }
  }

  /// <summary>
  /// <c>main</c>'s signal strength formula before AUD-101, copied here so that a change to the
  /// receiver's own copy cannot move both sides of the comparison: the RMS magnitude of about 256
  /// raw IQ samples spread over the block, in float arithmetic.
  /// </summary>
  private static float MainWidebandStrength(IqSample[] samples)
  {
    float sumPower = 0f;
    int step = Math.Max(1, samples.Length / 256);
    int count = 0;
    for (int i = 0; i < samples.Length; i += step)
    {
      sumPower += samples[i].MagnitudeSquared;
      count++;
    }
    return MathF.Sqrt(sumPower / count);
  }

  /// <summary>Makes the harness's device remember the last block it built.</summary>
  private static Func<IqSample[]?> RecordLastBlock(ReceiverHarness h)
  {
    IqSample[]? last = null;
    h.Device.BlockFactory = (hz, n) => last = h.Rf.Block(hz, n);
    return () => last;
  }

  [Theory]
  // Noise under the 0.1 default: closed on the wideband scale. On the in-channel scale it would
  // read about -41 dBFS (strength 0.31) and open the squelch.
  [InlineData(0.05f, 0f, false)]
  // Noise over the 0.1 default: open.
  [InlineData(0.15f, 0f, true)]
  // A station: open.
  [InlineData(0.05f, 0.3f, true)]
  public void Aircraft_SignalStrengthAndSquelch_AreMainsWidebandFormula(float noiseRms, float carrier, bool open)
  {
    // AUD-101 review M2/M3: on the box an empty Aircraft channel read -18 to -26 dBFS in the
    // channel, so Aircraft keeps the wideband scale for its level, squelch and seek stop.
    SyntheticRf rf = carrier > 0 ? AmStation(AirHz, carrier) : new SyntheticRf();
    rf.NoiseRms = noiseRms;
    using ReceiverHarness h = new(rf, BandType.Aircraft, AirHz);
    Func<IqSample[]?> last = RecordLastBlock(h);
    h.Run(3);

    float expected = MainWidebandStrength(last()!);
    Assert.Equal(expected, h.Receiver.SignalStrength);
    Assert.Equal(expected >= h.Receiver.SquelchThreshold, h.Receiver.GetRadioState().SquelchOpen);
    Assert.Equal(open, h.Receiver.GetRadioState().SquelchOpen);
    Assert.Equal(!open, h.SilentEvents[^1]);
  }

  [Theory]
  [InlineData(BandType.AM, AmHz)]
  [InlineData(BandType.Shortwave, SwHz)]
  public void HfBands_ReportTheInChannelScale(BandType band, long frequencyHz)
  {
    // An unmodulated carrier of 0.05 (-26.0 dBFS) over the box's direct-sampling noise.
    SyntheticRf rf = new() { NoiseRms = 0.02f };
    rf.Transmitters.Add(new Transmitter(frequencyHz, 0.05f));
    using ReceiverHarness h = new(rf, band, frequencyHz);
    Func<IqSample[]?> last = RecordLastBlock(h);
    h.Run(3);

    Assert.InRange(LevelDbfs(h.Receiver.SignalStrength), -26.5, -25.5);
    // Not the wideband value (about 0.054 here).
    Assert.True(Math.Abs(h.Receiver.SignalStrength - MainWidebandStrength(last()!)) > 0.4f,
      $"strength {h.Receiver.SignalStrength}, wideband {MainWidebandStrength(last()!)}");
  }

  // (i) Re-seeding after a tune and after the squelch reopens (review M1) ----------------------

  /// <summary>Largest |mean| of an audio block allowed after a change: 2 % of full scale (the AGC target is 0.5 peak).</summary>
  private const double MaxBlockDc = 0.01;

  /// <summary>How far, in dB, each block's RMS may sit from the steady-state RMS after a change.</summary>
  private const double MaxLevelErrorDb = 1.0;

  /// <summary>
  /// Asserts that every block in <paramref name="blocks"/> has |mean| under
  /// <see cref="MaxBlockDc"/> and an RMS within <see cref="MaxLevelErrorDb"/> of the steady state
  /// (the RMS of the last four blocks).
  /// </summary>
  private static void AssertSettledFromTheFirstBlock(float[][] blocks)
  {
    double steady = AudioMeasure.Rms(blocks[^4..].SelectMany(b => b).ToArray());
    double[] means = blocks.Select(b => b.Average(v => (double)v)).ToArray();
    double[] errorsDb = blocks.Select(b => AudioMeasure.Db(AudioMeasure.Rms(b) / steady)).ToArray();
    string report = string.Join(", ", means.Zip(errorsDb, (m, e) => $"{m:F4}/{e:F2} dB"));

    Assert.True(means.All(m => Math.Abs(m) <= MaxBlockDc), $"block mean / level from steady state: {report}");
    Assert.True(errorsDb.All(e => Math.Abs(e) <= MaxLevelErrorDb), $"block mean / level from steady state: {report}");
  }

  [Fact]
  public void Retune_FromAStrongStation_ToAWeakOne_NeitherStepsTheDcNorDucksTheAgc()
  {
    // Two stations 30 kHz apart, 20 dB apart, both 1 kHz at 50 %. The weak one needs an AGC gain
    // of 20, inside the maximum of 30.
    SyntheticRf rf = AmStation(AmHz, 0.5f);
    rf.Transmitters.Add(new Transmitter(AmHz + 30_000, 0.05f, ToneHz: 1_000, AmDepth: 0.5));
    using ReceiverHarness h = new(rf, BandType.AM, AmHz);
    h.Run(Blocks);

    Assert.True(h.Receiver.SetFrequency(AmHz + 30_000));
    // The block in flight across the tune: captured at the old device centre, raised after it.
    h.RunCapturedAt(AmHz - Offset);
    int afterOld = h.Events.Length;
    h.Run(20);

    float[][] after = h.Events[afterOld..];
    Assert.Equal(20, after.Length);
    AssertSettledFromTheFirstBlock(after);
  }

  [Fact]
  public void Retune_WithOldBlocksQueued_StillReseedsOnlyOnTheNewStation()
  {
    // Re-review M1 of the fixes: the tune generation is stamped when a block is raised, not when
    // the DSP thread takes it from the queue. Old-station blocks waiting in the queue at the tune
    // must not count as "the first and second block after the tune"; if they did, the re-seed
    // would land on the strong old station and duck the weak new one.
    SyntheticRf rf = AmStation(AmHz, 0.5f);
    rf.Transmitters.Add(new Transmitter(AmHz + 30_000, 0.05f, ToneHz: 1_000, AmDepth: 0.5));
    using ReceiverHarness h = new(rf, BandType.AM, AmHz);
    h.Run(Blocks);

    // Hold the DSP thread inside the next block's audio event, then queue three more old-station
    // blocks behind it (the queue holds 8).
    h.DspGate.Reset();
    h.RaiseOnly();
    h.WaitForEvents(1);
    for (int i = 0; i < 3; i++)
    {
      h.RaiseOnly();
    }

    Assert.True(h.Receiver.SetFrequency(AmHz + 30_000));
    // The block in flight across the tune: captured at the old device centre, raised after it.
    h.RaiseOnly(capturedAtDeviceHz: AmHz - Offset);

    h.DspGate.Set();
    h.WaitForEvents(4);   // the three queued old blocks and the straddling one
    int afterOld = h.Events.Length;
    h.Run(20);

    float[][] after = h.Events[afterOld..];
    Assert.Equal(20, after.Length);
    AssertSettledFromTheFirstBlock(after);
  }

  [Fact]
  public void Aircraft_SquelchReopening_OnAWeakerStation_NeitherStepsTheDcNorDucksTheAgc()
  {
    // A strong station (wideband RMS about 0.32, open), then nothing (closed), then a weaker one
    // (about 0.13, open again).
    SyntheticRf rf = AmStation(AirHz, 0.3f);
    rf.NoiseRms = 0.001f;
    using ReceiverHarness h = new(rf, BandType.Aircraft, AirHz);
    h.Run(Blocks);
    Assert.True(h.Receiver.GetRadioState().SquelchOpen);

    rf.Transmitters.Clear();
    h.Run(3);
    Assert.False(h.Receiver.GetRadioState().SquelchOpen);

    rf.Transmitters.Add(new Transmitter(AirHz, 0.12f, ToneHz: 1_000, AmDepth: 0.5));
    int reopened = h.Events.Length;
    h.Run(20);

    Assert.True(h.Receiver.GetRadioState().SquelchOpen);
    AssertSettledFromTheFirstBlock(h.Events[reopened..]);
  }
}
