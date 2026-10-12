using RTLSDRCore.DSP;

namespace RTLSDRCore.Tests.FmWatchdog;

/// <summary>
/// AUD-106 review M4: <c>RadioReceiver.IsPilotLocked</c>, the evidence that records a frequency as
/// locked, against the stereo detector it is stricter than. Fed directly with a synthetic composite at
/// 240 kHz, block by block.
/// </summary>
public class PilotLockTests
{
  private const int Rate = 240_000;

  private static StereoFmDecoder Decode(Func<int, float> composite, int samples = 3 * 16_384)
  {
    StereoFmDecoder decoder = new(Rate);
    float[] input = new float[samples];
    for (int n = 0; n < samples; n++)
    {
      input[n] = composite(n);
    }
    decoder.Decode(input, new float[samples * 2]);
    return decoder;
  }

  [Fact]
  public void ABroadcastPilot_IsLocked()
  {
    // 10 % of 75 kHz: 0.1 on the discriminator's scale, plus 1 kHz programme.
    StereoFmDecoder d = Decode(n => 0.1f * MathF.Sin(2 * MathF.PI * 19_000 * n / Rate) + 0.5f * MathF.Sin(2 * MathF.PI * 1_000 * n / Rate));

    Assert.True(d.StereoDetected);
    Assert.True(RadioReceiver.IsPilotLocked(d), $"pilot {d.PilotStrength:F4}, in phase {d.PilotInPhase:F4}, PLL {d.PllFrequency:F1} Hz");
  }

  [Theory]
  [InlineData(0.5, 4242)]
  [InlineData(0.92, 4242)]   // an empty channel: phase steps uniform over ±π read ±1.6 on the WFM scale, RMS 0.92
  [InlineData(2.0, 4242)]
  [InlineData(2.0, 7)]
  [InlineData(4.0, 99)]
  public void DiscriminatorNoise_IsNotLocked(double rms, int seed)
  {
    // Gaussian noise on the discriminator's scale.
    Random random = new(seed);
    StereoFmDecoder d = Decode(_ =>
    {
      double u1 = 1.0 - random.NextDouble();
      double u2 = random.NextDouble();
      return (float)(rms * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    });

    Assert.False(RadioReceiver.IsPilotLocked(d), $"pilot {d.PilotStrength:F4}, in phase {d.PilotInPhase:F4}, PLL {d.PllFrequency:F1} Hz");
  }

  [Fact]
  public void AMonoProgramme_IsNotLocked()
  {
    StereoFmDecoder d = Decode(n => 0.5f * MathF.Sin(2 * MathF.PI * 1_000 * n / Rate));

    Assert.False(d.StereoDetected);
    Assert.False(RadioReceiver.IsPilotLocked(d));
  }
}
