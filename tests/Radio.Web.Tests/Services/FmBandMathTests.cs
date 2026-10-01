using FluentAssertions;
using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Tests.Services;

/// <summary>
/// The BAND view's tap-to-tune rule and display normalisation (AUD-76 PR 2): a tap snaps to the
/// strongest peak within ±0.4 MHz, else to the nearest US FM channel.
/// </summary>
public class FmBandMathTests
{
  private const float Noise = -60f;

  /// <summary>A full 101-channel map at the noise floor, with the given channels raised.</summary>
  private static List<BandMapChannelDto> Map(params (double Mhz, float Level)[] raised)
  {
    var channels = new List<BandMapChannelDto>();
    for (long hz = FmBandMath.FirstChannelHz; hz <= FmBandMath.LastChannelHz; hz += FmBandMath.ChannelSpacingHz)
    {
      channels.Add(new BandMapChannelDto(hz, Noise));
    }

    foreach ((double mhz, float level) in raised)
    {
      long hz = (long)Math.Round(mhz * 1_000_000);
      int i = channels.FindIndex(c => c.FrequencyHz == hz);
      i.Should().BeGreaterThanOrEqualTo(0, $"{mhz} MHz is a US FM channel");
      channels[i] = new BandMapChannelDto(hz, level);
    }

    return channels;
  }

  private static double Hz(double mhz) => mhz * 1_000_000;

  [Fact]
  public void Tap_SnapsToAPeakWithinTheWindow()
  {
    // Tap 0.3 MHz below a strong station: the peak wins over the nearest channel (99.3).
    var map = Map((99.5, -30f));
    FmBandMath.ResolveTapTarget(Hz(99.2), map).Should().Be(99_500_000);
  }

  [Fact]
  public void Tap_TwoPeaksInTheWindow_TheStrongerWins()
  {
    // 99.1 and 99.9 are both within 0.4 MHz of 99.5; 99.9 is stronger even though both are
    // equally far from the tap.
    var map = Map((99.1, -40f), (99.9, -30f));
    FmBandMath.ResolveTapTarget(Hz(99.5), map).Should().Be(99_900_000);
  }

  [Fact]
  public void Tap_PeakJustOutsideTheWindow_IsIgnored()
  {
    // 99.9 is 0.5 MHz from the tap: outside ±0.4, so the nearest channel wins.
    var map = Map((99.9, -30f));
    FmBandMath.ResolveTapTarget(Hz(99.4), map).Should().Be(99_500_000);
  }

  [Fact]
  public void Tap_NoPeak_SnapsToTheNearestOddTenthChannel()
  {
    var map = Map();
    FmBandMath.ResolveTapTarget(Hz(99.44), map).Should().Be(99_500_000);
    FmBandMath.ResolveTapTarget(Hz(99.36), map).Should().Be(99_300_000);
  }

  [Fact]
  public void Tap_ARaisedChannelBelowTheProminenceThreshold_IsNotAPeak()
  {
    // 3 dB over the median is a local maximum but not a peak (threshold: median + 6 dB).
    var map = Map((99.7, Noise + 3f));
    FmBandMath.ResolveTapTarget(Hz(99.4), map).Should().Be(99_500_000);
  }

  [Fact]
  public void Tap_AnAliasShadowNextToAStation_LosesToTheStation()
  {
    // AUD-76 PR 1, M3: a strong station casts a weaker shadow 200 kHz away. Tapping on the shadow
    // must still land on the station, because the shadow is lower than its neighbour.
    var map = Map((101.1, -25f), (101.3, -38f));
    FmBandMath.ResolveTapTarget(Hz(101.35), map).Should().Be(101_100_000);
  }

  [Fact]
  public void Tap_WithOnlyTheShadowInTheWindow_SnapsToTheNearestChannel_NotTheShadow()
  {
    // The case the test above cannot tell apart from "stronger wins": here the station (101.1) is
    // 0.52 MHz from the tap, outside the window, and only its shadow (101.3, 22 dB above the floor) is
    // inside. Without the neighbour rule the shadow would count as a peak and win the tap.
    var map = Map((101.1, -25f), (101.3, -38f));
    FmBandMath.ResolveTapTarget(Hz(101.62), map).Should().Be(101_700_000);
  }

  [Theory]
  [InlineData(0.0, 87_900_000)]
  [InlineData(-5.0, 87_900_000)]
  [InlineData(1.0, 107_900_000)]
  [InlineData(9.0, 107_900_000)]
  public void Tap_AtTheEdges_ClampsToTheFirstAndLastChannels(double fraction, long expected)
  {
    FmBandMath.ResolveTapTarget(FmBandMath.FractionToHz(fraction), Map()).Should().Be(expected);
  }

  [Fact]
  public void Tap_WithNoMap_SnapsToTheNearestChannel()
  {
    FmBandMath.ResolveTapTarget(Hz(95.06), null).Should().Be(95_100_000);
    FmBandMath.ResolveTapTarget(Hz(95.06), []).Should().Be(95_100_000);
  }

  [Fact]
  public void Tap_APeakAtTheBandEdge_CountsItsMissingNeighbourAsLower()
  {
    var map = Map((87.9, -30f));
    FmBandMath.ResolveTapTarget(Hz(88.2), map).Should().Be(87_900_000);
  }

  [Fact]
  public void NearestChannel_IsAlwaysAnOddTenth()
  {
    for (double mhz = 87.0; mhz <= 108.5; mhz += 0.037)
    {
      long hz = FmBandMath.NearestChannelHz(Hz(mhz));
      ((hz - FmBandMath.FirstChannelHz) % FmBandMath.ChannelSpacingHz).Should().Be(0);
      hz.Should().BeInRange(FmBandMath.FirstChannelHz, FmBandMath.LastChannelHz);
    }
  }

  [Fact]
  public void FractionAndHz_RoundTrip()
  {
    FmBandMath.FractionToHz(0).Should().Be(87_500_000);
    FmBandMath.FractionToHz(1).Should().Be(108_000_000);
    FmBandMath.HzToFraction(FmBandMath.FractionToHz(0.37)).Should().BeApproximately(0.37, 1e-12);
  }

  [Fact]
  public void Normalize_MapsTheLowPercentileToZeroAndTheMaxToOne()
  {
    var map = Map((99.5, -30f));
    double[] v = FmBandMath.NormalizeLevels(map);

    v.Should().HaveCount(map.Count);
    v.Min().Should().Be(0);
    v[map.FindIndex(c => c.FrequencyHz == 99_500_000)].Should().Be(1);
  }

  [Fact]
  public void Normalize_ANoiseOnlyMap_IsNotStretchedToFullHeight()
  {
    // 2 dB of ripple over a 10 dB minimum span: nothing rises above a fifth of the plot.
    var map = Map((95.1, Noise + 2f));
    FmBandMath.NormalizeLevels(map).Max().Should().BeApproximately(0.2, 1e-9);
  }

  [Fact]
  public void Normalize_Empty_IsEmpty()
  {
    FmBandMath.NormalizeLevels([]).Should().BeEmpty();
  }

  // ── UI-22: colour tiers ──────────────────────────────────────────────────

  private static BandSignalTier TierAt(List<BandMapChannelDto> map, double mhz) =>
    FmBandMath.SignalTiers(map)[map.FindIndex(c => c.FrequencyHz == (long)Math.Round(mhz * 1_000_000))];

  // The map's median is the noise floor, -60: a handful of raised channels does not move it. The dB
  // figures are written out rather than taken from the constants, so a change to a threshold fails here.
  [Theory]
  [InlineData(-54.1f, BandSignalTier.Noise)]   //  5.9 dB above
  [InlineData(-54.0f, BandSignalTier.Weak)]    //  6   — the peak rule's prominence belongs to Weak
  [InlineData(-48.1f, BandSignalTier.Weak)]    // 11.9
  [InlineData(-48.0f, BandSignalTier.Fair)]    // 12
  [InlineData(-40.1f, BandSignalTier.Fair)]    // 19.9
  [InlineData(-40.0f, BandSignalTier.Strong)]  // 20
  [InlineData(-10.0f, BandSignalTier.Strong)]  // 50
  public void Tiers_AtEachBoundary(float level, BandSignalTier expected)
  {
    TierAt(Map((99.5, level)), 99.5).Should().Be(expected);
  }

  [Fact]
  public void Tiers_TheNoiseFloorItself_IsNoise()
  {
    TierAt(Map((99.5, -30f)), 95.1).Should().Be(BandSignalTier.Noise);
  }

  [Fact]
  public void Tiers_AFlatDeadMap_IsAllNoise()
  {
    // Every channel at the median, whatever the absolute level: nothing stands above the noise.
    List<BandMapChannelDto> dead = Map().Select(c => c with { LevelDbfs = -25f }).ToList();
    FmBandMath.SignalTiers(dead).Should().OnlyContain(t => t == BandSignalTier.Noise).And.HaveCount(dead.Count);
  }

  [Fact]
  public void Tiers_FollowTheMedian_NotTheDisplayHeight()
  {
    // The busiest channel is drawn full height (here exactly MinDisplaySpanDb above the floor), but
    // 10 dB above the median is still weak; on a busier map the same height would be strong.
    var map = Map((99.5, Noise + 10f));
    FmBandMath.NormalizeLevels(map)[map.FindIndex(c => c.FrequencyHz == 99_500_000)].Should().Be(1);
    TierAt(map, 99.5).Should().Be(BandSignalTier.Weak);
  }

  [Fact]
  public void Tiers_AreInTheOrderGiven()
  {
    // Unsorted input: the tiers line up with the channels as passed, as NormalizeLevels' heights do.
    BandMapChannelDto[] channels =
    [
      new(99_500_000, -30f), new(88_100_000, -60f), new(101_100_000, -50f), new(95_100_000, -60f), new(90_100_000, -60f),
    ];

    FmBandMath.SignalTiers(channels).Should().Equal(
      BandSignalTier.Strong, BandSignalTier.Noise, BandSignalTier.Weak, BandSignalTier.Noise, BandSignalTier.Noise);
  }

  [Fact]
  public void Tiers_AChannelExactlyAtTheProminence_IsAPeakToTheTapAndWeakToTheColour()
  {
    // "A station" means the same thing on the colour scale as in tap-to-tune (spec §5.1).
    var map = Map((99.5, Noise + 6f));
    FmBandMath.ResolveTapTarget(Hz(99.2), map).Should().Be(99_500_000);
    TierAt(map, 99.5).Should().Be(BandSignalTier.Weak);
  }

  [Fact]
  public void Tiers_Empty_IsEmpty()
  {
    FmBandMath.SignalTiers([]).Should().BeEmpty();
  }
}
