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
}
