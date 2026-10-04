using RTLSDRCore.Enums;
using Xunit;

namespace RTLSDRCore.Tests;

/// <summary>
/// AUD-100: the station-list scan — the pure hop rule, and <see cref="RadioReceiver.ScanStations"/>
/// and <see cref="RadioReceiver.SeekStationFound"/> on a mock-device receiver. No test here waits on a
/// clock: <see cref="RadioReceiver.ScanPauseMs"/> is 0, and every scan ends by going all the way round,
/// so what is asserted is the order of tunes, not how long they took.
/// </summary>
public class StationListScanTests
{
  private static readonly long[] Stations = { 88_100_000, 92_300_000, 100_100_000 };
  private const long Gap = 100_000;

  [Theory]
  [InlineData(92_300_000, true, 100_100_000)]   // up: the next one above
  [InlineData(92_300_000, false, 88_100_000)]   // down: the next one below
  [InlineData(100_100_000, true, 88_100_000)]   // up from the top wraps to the bottom
  [InlineData(88_100_000, false, 100_100_000)]  // down from the bottom wraps to the top
  [InlineData(95_000_000, true, 100_100_000)]   // between stations
  [InlineData(95_000_000, false, 92_300_000)]
  [InlineData(92_290_000, true, 100_100_000)]   // 10 kHz below 92.3 is on 92.3: not a 10 kHz hop onto it
  [InlineData(92_310_000, false, 88_100_000)]
  public void Next_IsTheNearestStationBeyondTheCurrentOne_Wrapping(long currentHz, bool ascending, long expected)
  {
    Assert.Equal(expected, StationListScan.Next(Stations, currentHz, ascending, Gap));
  }

  [Fact]
  public void Next_IsNull_WhenTheOnlyStationIsTheCurrentOne()
  {
    Assert.Null(StationListScan.Next(new long[] { 92_300_000 }, 92_300_000, true, Gap));
    Assert.Null(StationListScan.Next(new long[] { 92_300_000 }, 92_350_000, false, Gap));
    Assert.Null(StationListScan.Next(Array.Empty<long>(), 92_300_000, true, Gap));
  }

  [Fact]
  public void Next_WithNoGap_SkipsOnlyAnExactMatch()
  {
    Assert.Equal(92_300_000, StationListScan.Next(Stations, 92_290_000, true, 0));
    Assert.Equal(100_100_000, StationListScan.Next(Stations, 92_300_000, true, 0));
  }

  [Theory]
  [InlineData(92_300_000, 88_100_000, 92_300_000, true, true)]     // reaches the start
  [InlineData(92_290_000, 88_100_000, 92_300_000, true, true)]     // reaches the station the scan started on
  [InlineData(92_290_000, 100_100_000, 92_300_000, false, true)]   // down, from above: 92.3 is not past 92.29, but is its station
  [InlineData(95_000_000, 88_100_000, 100_100_000, true, true)]    // passes the start after wrapping
  [InlineData(95_000_000, 95_000_000, 100_100_000, true, false)]   // first hop
  [InlineData(95_000_000, 100_100_000, 88_100_000, true, false)]   // the wrap itself
  [InlineData(95_000_000, 100_100_000, 92_300_000, false, true)]   // down: passes the start after wrapping
  [InlineData(95_000_000, 92_300_000, 88_100_000, false, false)]
  public void CompletesCircle_WhenTheHopReachesOrPassesTheStart(long startHz, long currentHz, long nextHz, bool ascending, bool expected)
  {
    Assert.Equal(expected, StationListScan.CompletesCircle(startHz, currentHz, nextHz, ascending, Gap));
  }

  [Theory]
  [InlineData(true, new long[] { 100_100_000, 88_100_000 })]
  [InlineData(false, new long[] { 88_100_000, 100_100_000 })]
  public void ScanStations_HopsStationToStation_AndStopsBeforeReturningToItsStart(bool ascending, long[] expectedTunes)
  {
    using RadioReceiver receiver = StartedReceiver(92_300_000);
    List<long> tunes = new();
    int seekEvents = 0;
    List<ReceiverState> states = new();
    receiver.FrequencyChanged += (_, e) => tunes.Add(e.NewFrequency);
    receiver.SeekStationFound += (_, _) => seekEvents++;
    receiver.StateChanged += (_, e) => states.Add(e.NewState);

    receiver.ScanStations(Stations, ascending, Gap);

    // Straight from station to station: no frequency in between is ever tuned.
    Assert.Equal(expectedTunes, tunes);
    Assert.Equal(expectedTunes[^1], receiver.CurrentFrequency);
    Assert.False(receiver.IsScanning);
    Assert.Equal(new[] { ReceiverState.Scanning, ReceiverState.Running }, states);
    // Only a live seek's stops are observations; the list's stations are already known.
    Assert.Equal(0, seekEvents);
    receiver.Shutdown();
  }

  [Fact]
  public void ScanStations_StartedJustOffAStation_DoesNotReturnToIt()
  {
    // 10 kHz below 92.3 is listening to 92.3: going down and round must stop before 92.3, not land on it.
    using RadioReceiver receiver = StartedReceiver(92_290_000);
    List<long> tunes = new();
    receiver.FrequencyChanged += (_, e) => tunes.Add(e.NewFrequency);

    receiver.ScanStations(Stations, ascending: false, Gap);

    Assert.Equal(new long[] { 88_100_000, 100_100_000 }, tunes);
    receiver.Shutdown();
  }

  [Fact]
  public void ScanStations_IgnoresStationsOutsideTheCurrentBand()
  {
    using RadioReceiver receiver = StartedReceiver(92_300_000);
    List<long> tunes = new();
    receiver.FrequencyChanged += (_, e) => tunes.Add(e.NewFrequency);

    receiver.ScanStations(new long[] { 162_400_000, 100_100_000, 92_300_000 }, ascending: true, Gap);

    Assert.Equal(new long[] { 100_100_000 }, tunes);
    receiver.Shutdown();
  }

  [Fact]
  public void ScanStations_WhenNotStarted_Throws()
  {
    using RadioReceiver receiver = RadioReceiver.CreateWithMockDevice();

    Assert.Throws<InvalidOperationException>(() => receiver.ScanStations(Stations, true, Gap));
  }

  [Fact]
  public void LiveSeek_RaisesSeekStationFound_ForEveryStop_AtTheFrequencyItStoppedOn()
  {
    using RadioReceiver receiver = StartedReceiver(98_000_000);
    List<long> tunes = new();
    List<SeekStationFoundEventArgs> found = new();
    receiver.FrequencyChanged += (_, e) => tunes.Add(e.NewFrequency);
    receiver.SeekStationFound += (_, e) => found.Add(e);

    // Threshold 0: every step is a stop, so the events are determined by the steps, not by what the
    // mock device's samples happen to measure. 5 MHz steps keep the round trip to a few dwells.
    receiver.ScanFrequencyUp(stepHz: 5_000_000, signalThreshold: 0f, dwellTimeMs: 0);

    Assert.NotEmpty(found);
    Assert.Equal(tunes, found.Select(e => e.FrequencyHz));
    Assert.All(found, e => Assert.True(e.Strength >= 0f));
    receiver.Shutdown();
  }

  [Fact]
  public void LiveSeek_AHandlerThatThrows_DoesNotEndTheScan()
  {
    using RadioReceiver receiver = StartedReceiver(98_000_000);
    int calls = 0;
    receiver.SeekStationFound += (_, _) =>
    {
      calls++;
      throw new IOException("disk full");
    };

    receiver.ScanFrequencyUp(stepHz: 5_000_000, signalThreshold: 0f, dwellTimeMs: 0);

    // More than one stop: the first throw did not end the scan.
    Assert.True(calls > 1, $"expected several stops, got {calls}");
    Assert.False(receiver.IsScanning);
    receiver.Shutdown();
  }

  private static RadioReceiver StartedReceiver(long frequencyHz)
  {
    RadioReceiver receiver = RadioReceiver.CreateWithMockDevice();
    receiver.ScanPauseMs = 0;
    Assert.True(receiver.Startup());
    receiver.SetBand(BandType.FM, frequencyHz);
    return receiver;
  }
}
