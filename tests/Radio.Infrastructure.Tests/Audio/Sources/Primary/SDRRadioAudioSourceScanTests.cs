using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.Sources.Primary;
using RTLSDRCore;
using RTLSDRCore.Enums;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// AUD-100: <see cref="SDRRadioAudioSource.StartScanAsync"/> hops between a fresh band map's stations,
/// seeks live otherwise, and records a live seek's stops on the map. The receiver runs on the mock
/// device with <see cref="RadioReceiver.ScanPauseMs"/> 0, so every scan ends by going all the way
/// round; each test waits for the receiver's own Scanning → Running transition, never for a time.
/// </summary>
public sealed class SDRRadioAudioSourceScanTests : IAsyncDisposable
{
  private static readonly TimeSpan FailSafe = TimeSpan.FromSeconds(30);
  private static readonly long[] Stations = { 88_100_000, 92_300_000, 100_100_000 };

  private readonly RadioReceiver _receiver = RadioReceiver.CreateWithMockDevice();
  private readonly Mock<IScanStationMap> _map = new();
  private readonly RadioOptions _radioOptions = new();
  private readonly List<long> _tunes = new();
  private readonly TaskCompletionSource _scanEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private SDRRadioAudioSource? _source;

  public SDRRadioAudioSourceScanTests()
  {
    _receiver.ScanPauseMs = 0;
    Assert.True(_receiver.Startup());
    _receiver.SetBand(BandType.FM, 92_300_000);
    _receiver.FrequencyChanged += (_, e) =>
    {
      lock (_tunes)
      {
        _tunes.Add(e.NewFrequency);
      }
    };
    _receiver.StateChanged += (_, e) =>
    {
      if (e.OldState == ReceiverState.Scanning && e.NewState == ReceiverState.Running)
      {
        _scanEnded.TrySetResult();
      }
    };
  }

  public async ValueTask DisposeAsync()
  {
    if (_source != null)
    {
      await _source.DisposeAsync();
    }
    _receiver.Shutdown();
    _receiver.Dispose();
  }

  private SDRRadioAudioSource CreateSource(IScanStationMap? map)
  {
    Mock<IOptionsMonitor<RadioOptions>> options = new();
    options.Setup(o => o.CurrentValue).Returns(_radioOptions);
    _source = new SDRRadioAudioSource(
      NullLogger<SDRRadioAudioSource>.Instance, _receiver, options.Object, scanStationMap: map);
    return _source;
  }

  private void MapReturns(ScanStationList? list, string? whyNot = null)
  {
    _map.Setup(m => m.GetScanStations(It.IsAny<string>(), It.IsAny<long>(), out whyNot)).Returns(list);
  }

  [Theory]
  [InlineData(ScanDirection.Up, new long[] { 100_100_000, 88_100_000 })]
  [InlineData(ScanDirection.Down, new long[] { 88_100_000, 100_100_000 })]
  public async Task FreshMap_ScanHopsBetweenItsStations_WithNoLiveSeek(ScanDirection direction, long[] expected)
  {
    MapReturns(new ScanStationList(Stations, 100_000, DateTimeOffset.UnixEpoch));
    SDRRadioAudioSource source = CreateSource(_map.Object);

    await source.StartScanAsync(direction);
    await _scanEnded.Task.WaitAsync(FailSafe);

    // Only the mapped stations were tuned: a live seek would have stepped through 87.5–108 MHz.
    Assert.Equal(expected, Snapshot());
    _map.Verify(m => m.GetScanStations("FM", 92_300_000, out It.Ref<string?>.IsAny), Times.Once);
    _map.Verify(m => m.RecordSeekStation(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<float>()), Times.Never);
  }

  [Fact]
  public async Task NoUsableMap_ScanSeeksLive_AndRecordsEveryStopOnTheBandMap()
  {
    MapReturns(null, BandMapService.ScanReasonStale);
    _radioOptions.ScanStopThreshold = 0;   // every step is a stop
    SDRRadioAudioSource source = CreateSource(_map.Object);
    await source.SetFrequencyStepAsync(new Frequency(5_000_000));

    await source.StartScanAsync(ScanDirection.Up);
    await _scanEnded.Task.WaitAsync(FailSafe);

    long[] tunes = Snapshot();
    Assert.True(tunes.Length > 2, $"expected a live seek's steps, got {tunes.Length}");
    // Each stop recorded on the FM map, at the frequency the seek stopped on.
    foreach (long hz in tunes)
    {
      _map.Verify(m => m.RecordSeekStation("FM", hz, It.IsAny<float>()), Times.Once);
    }
  }

  [Fact]
  public async Task NoBandMaps_ScanSeeksLive()
  {
    _radioOptions.ScanStopThreshold = 0;
    SDRRadioAudioSource source = CreateSource(null);
    await source.SetFrequencyStepAsync(new Frequency(5_000_000));

    await source.StartScanAsync(ScanDirection.Up);
    await _scanEnded.Task.WaitAsync(FailSafe);

    Assert.True(Snapshot().Length > 2);
  }

  [Fact]
  public async Task AMapThatThrows_ScanSeeksLive()
  {
    _map.Setup(m => m.GetScanStations(It.IsAny<string>(), It.IsAny<long>(), out It.Ref<string?>.IsAny))
      .Throws(new InvalidOperationException("boom"));
    _radioOptions.ScanStopThreshold = 100;
    SDRRadioAudioSource source = CreateSource(_map.Object);
    await source.SetFrequencyStepAsync(new Frequency(5_000_000));

    await source.StartScanAsync(ScanDirection.Up);
    await _scanEnded.Task.WaitAsync(FailSafe);

    Assert.True(Snapshot().Length > 2, "a live seek steps through the band");
  }

  private long[] Snapshot()
  {
    lock (_tunes)
    {
      return _tunes.ToArray();
    }
  }
}
