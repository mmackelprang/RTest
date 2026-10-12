using Radio.Core.Models;
using Xunit;

namespace Radio.Core.Tests.Models;

/// <summary>AUD-100: <see cref="BandMapStations.Peaks"/>, the rule Scan and the BAND view's tap share.</summary>
public class BandMapStationsTests
{
  private static BandMapChannel[] Fm(params (long Hz, float Db)[] stations)
  {
    List<BandMapChannel> channels = new();
    for (long hz = 87_900_000; hz <= 107_900_000; hz += 200_000)
    {
      (long Hz, float Db) station = stations.FirstOrDefault(s => s.Hz == hz);
      channels.Add(new BandMapChannel(hz, station.Hz == hz ? station.Db : -60f));
    }

    return channels.ToArray();
  }

  [Fact]
  public void Peaks_AreTheChannelsWellAboveTheMedian_Ascending()
  {
    Assert.Equal(
      new long[] { 88_100_000, 100_100_000 },
      BandMapStations.Peaks(Fm((100_100_000, -30f), (88_100_000, -40f))));
  }

  [Fact]
  public void Peaks_DropAStationsAdjacentChannelShadow()
  {
    // 92.5 stands 20 dB above the floor, but only as 92.3's shadow: lower than its neighbour.
    Assert.Equal(new long[] { 92_300_000 }, BandMapStations.Peaks(Fm((92_300_000, -30f), (92_500_000, -40f))));
  }

  [Fact]
  public void Peaks_NeedTheProminence_NotJustALocalMaximum()
  {
    // 5.9 dB above the median: a local maximum, not a station. 6 dB: a station.
    Assert.Empty(BandMapStations.Peaks(Fm((95_100_000, -54.1f))));
    Assert.Equal(new long[] { 95_100_000 }, BandMapStations.Peaks(Fm((95_100_000, -54f))));
  }

  [Fact]
  public void Peaks_AtTheBandEdge_CountTheMissingNeighbourAsLower()
  {
    Assert.Equal(new long[] { 87_900_000, 107_900_000 }, BandMapStations.Peaks(Fm((87_900_000, -30f), (107_900_000, -30f))));
  }

  [Fact]
  public void Peaks_OfNoChannels_AreNone()
  {
    Assert.Empty(BandMapStations.Peaks(Array.Empty<BandMapChannel>()));
  }

  [Fact]
  public void PeakProminence_IsTheValueTheBandViewsTapUses()
  {
    Assert.Equal(6.0, BandMapStations.PeakProminenceDb);
  }

  // ── AUD-103: known device spurs ──────────────────────────────────────────

  /// <summary>The 119 AM channels, 530–1710 kHz, at a -50 dB floor with the levels given.</summary>
  private static BandMapChannel[] Am(params (long Hz, float Db)[] levels)
  {
    List<BandMapChannel> channels = new();
    for (long hz = 530_000; hz <= 1_710_000; hz += 10_000)
    {
      (long Hz, float Db) level = levels.FirstOrDefault(s => s.Hz == hz);
      channels.Add(new BandMapChannel(hz, level.Hz == hz ? level.Db : -50f));
    }

    return channels.ToArray();
  }

  [Fact]
  public void Peaks_OnAm_NeverIncludeThe960KhzCrystalSpur()
  {
    // The box's night pattern: the spur 16 dB over the floor, nothing else.
    Assert.Empty(BandMapStations.Peaks(Am((960_000, -34f))));
  }

  [Fact]
  public void Peaks_ARealStationBesideTheSpur_IsNotHiddenByIt()
  {
    // 970 kHz (the 968 kHz carrier) is lower than the spur next to it; the spur is left out first, so
    // 970's neighbours are 950 and 980. 1030 kHz (skywave, near 28.8 MHz / 28) is a station.
    Assert.Equal(
      new long[] { 970_000, 1_030_000 },
      BandMapStations.Peaks(Am((960_000, -34f), (970_000, -43f), (1_030_000, -42f))));
  }

  [Theory]
  [InlineData(960_000L, true)]
  [InlineData(958_000L, true)]
  [InlineData(962_000L, true)]
  [InlineData(957_999L, false)]
  [InlineData(970_000L, false)]
  [InlineData(1_030_000L, false)]   // real skywave: never excluded
  [InlineData(1_028_571L, false)]   // 28.8 MHz / 28 is not in the table: only measured spurs are
  [InlineData(9_600_000L, false)]   // 28.8 MHz / 3, in 31 m, read at the floor
  [InlineData(96_000_000L, false)]
  public void IsKnownSpur_IsTheMeasuredTable_WithinItsTolerance(long hz, bool expected)
  {
    Assert.Equal(expected, BandMapStations.IsKnownSpur(hz));
  }

  [Fact]
  public void KnownSpurs_IsJustThe960KhzEntry_WithItsSource()
  {
    KnownDeviceSpur spur = Assert.Single(BandMapStations.KnownSpurs);
    Assert.Equal(960_000, spur.FrequencyHz);
    Assert.Equal(28_800_000 / 30, spur.FrequencyHz);
    Assert.Contains("28.8 MHz", spur.Source);
  }

  [Fact]
  public void Peaks_OfOnlySpurs_AreNone()
  {
    Assert.Empty(BandMapStations.Peaks(new[] { new BandMapChannel(960_000, -30f) }));
  }
}
