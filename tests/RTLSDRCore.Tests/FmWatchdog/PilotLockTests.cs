using RTLSDRCore.DSP;

namespace RTLSDRCore.Tests.FmWatchdog;

/// <summary>
/// AUD-106 review: <c>RadioReceiver.IsPilotLocked</c>, the per-block evidence that records a frequency
/// as locked, against the stereo detector it is stricter than. A synthetic composite at 240 kHz is fed
/// block by block (16384 samples, as the receiver does) and the verdict is checked at every block end,
/// over many seeds, not at one instant.
/// </summary>
public class PilotLockTests
{
  private const int Rate = 240_000;
  private const int Block = 16_384;
  private const int Blocks = 44;   // the default 3 s window

  /// <summary>Feeds <see cref="Blocks"/> blocks and returns the verdict and readings after each.</summary>
  private static List<(bool Locked, float InPhase, float Strength, float Pll)> Run(Func<long, float> composite)
  {
    StereoFmDecoder decoder = new(Rate);
    float[] input = new float[Block];
    float[] output = new float[Block * 2];
    List<(bool, float, float, float)> verdicts = new();
    long n = 0;
    for (int b = 0; b < Blocks; b++)
    {
      for (int i = 0; i < Block; i++)
      {
        input[i] = composite(n++);
      }
      decoder.Decode(input, output);
      verdicts.Add((RadioReceiver.IsPilotLocked(decoder), decoder.PilotInPhase, decoder.PilotStrength, decoder.PllFrequency));
    }
    return verdicts;
  }

  private static Func<long, float> Noise(double rms, int seed)
  {
    Random random = new(seed);
    return _ =>
    {
      double u1 = 1.0 - random.NextDouble();
      double u2 = random.NextDouble();
      return (float)(rms * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    };
  }

  [Theory]
  [InlineData(0.08f)]   // 8 % pilot
  [InlineData(0.1f)]    // 10 % pilot
  public void ABroadcastPilot_IsLockedAtEveryBlockEnd_AfterTheFirst(float pilot)
  {
    var verdicts = Run(n => pilot * MathF.Sin(2 * MathF.PI * 19_000 * n / Rate) + 0.5f * MathF.Sin(2 * MathF.PI * 1_000 * n / Rate));

    Assert.All(verdicts.Skip(1), v => Assert.True(v.Locked, $"in phase {v.InPhase:F4}, strength {v.Strength:F4}, PLL {v.Pll:F1} Hz"));
  }

  [Theory]
  [InlineData(0.5)]
  [InlineData(0.92)]   // an empty channel: phase steps uniform over ±π read ±1.6 on the WFM scale, RMS 0.92
  [InlineData(2.0)]
  [InlineData(4.0)]
  public void DiscriminatorNoise_IsNeverLocked_AtAnyBlockEnd_OverTwentySeeds(double rms)
  {
    int locked = 0;
    float worst = 0;
    for (int seed = 1; seed <= 20; seed++)
    {
      var verdicts = Run(Noise(rms, seed));
      locked += verdicts.Count(v => v.Locked);
      worst = Math.Max(worst, verdicts.Max(v => v.InPhase));
      // The PLL's integrator barely moves on noise: it stays near 19 kHz, so its frequency cannot
      // tell noise from a pilot (the reason IsPilotLocked does not use it).
      Assert.All(verdicts, v => Assert.InRange(v.Pll, 18_990f, 19_010f));
    }

    Assert.True(locked == 0, $"{locked} of {20 * Blocks} block ends read locked at noise RMS {rms} (largest in-phase {worst:F4})");
  }

  [Fact]
  public void AMonoProgramme_IsNeverLocked()
  {
    var verdicts = Run(n => 0.5f * MathF.Sin(2 * MathF.PI * 1_000 * n / Rate));

    Assert.All(verdicts, v => Assert.False(v.Locked));
  }
}
