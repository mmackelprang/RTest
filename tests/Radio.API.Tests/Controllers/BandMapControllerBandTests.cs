using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Radio.API.Controllers;
using Radio.API.Models;
using Radio.Core.Configuration;
using Radio.Core.Models;
using Radio.Infrastructure.Audio.Services;
using RTLSDRCore.Hardware;
using RTLSDRCore.Sweep;

namespace Radio.API.Tests.Controllers;

/// <summary>AUD-91: <c>api/radio/bandmap</c> per band, over a real <see cref="BandMapService"/>.</summary>
public sealed class BandMapControllerBandTests : IDisposable
{
  private readonly string _root = Path.Combine(Path.GetTempPath(), "aud91-api-" + Guid.NewGuid().ToString("N"));
  private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
  private readonly SdrDeviceGate _gate = new();
  private readonly BandMapOptions _options = new();
  private BandMapService? _service;

  public void Dispose()
  {
    _service?.Dispose();
    if (Directory.Exists(_root))
    {
      Directory.Delete(_root, recursive: true);
    }
  }

  private BandMapTuning? Tuning { get; set; } = new("FM", 101_100_000);

  private BandMapController CreateController(Func<ISdrDevice?>? deviceFactory = null)
  {
    Mock<IOptionsMonitor<BandMapOptions>> monitor = new();
    monitor.Setup(m => m.CurrentValue).Returns(_options);
    _service = new BandMapService(
      NullLogger<BandMapService>.Instance,
      monitor.Object,
      new BandMapStore(_root, NullLogger.Instance),
      _gate,
      () => null,
      sleepService: null,
      deviceFactory: deviceFactory ?? (() => null),
      timeProvider: _time,
      currentTuning: () => Tuning);
    return new BandMapController(_service);
  }

  private static BandMapResponseDto Body(ActionResult<BandMapResponseDto> result) =>
    Assert.IsType<BandMapResponseDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

  private void Store(BandMap map) => new BandMapStore(_root, NullLogger.Instance).Save(map);

  [Fact]
  public void Get_WithNoBand_FollowsTheRadiosBand()
  {
    Tuning = new BandMapTuning("WB", 162_400_000);
    BandMapController controller = CreateController();

    BandMapResponseDto dto = Body(controller.Get());

    Assert.Equal("WB", dto.Band);
    Assert.True(dto.Mappable);
  }

  [Fact]
  public void Get_Fm_ReportsTheAud76Axis()
  {
    BandMapController controller = CreateController();

    BandMapResponseDto dto = Body(controller.Get("fm"));

    Assert.Equal("FM", dto.Band);
    Assert.True(dto.Mappable);
    Assert.Null(dto.UnavailableReason);
    Assert.Equal(87_500_000, dto.DisplayMinHz);
    Assert.Equal(108_000_000, dto.DisplayMaxHz);
    Assert.Equal(87_900_000, dto.FirstChannelHz);
    Assert.Equal(107_900_000, dto.LastChannelHz);
    Assert.Equal(200_000, dto.ChannelSpacingHz);
    Assert.Empty(dto.Channels);
  }

  [Fact]
  public void Get_Wb_ReportsTheNoaaAxis()
  {
    BandMapController controller = CreateController();

    BandMapResponseDto dto = Body(controller.Get("WB"));

    Assert.Equal(162_387_500, dto.DisplayMinHz);
    Assert.Equal(162_562_500, dto.DisplayMaxHz);
    Assert.Equal(162_400_000, dto.FirstChannelHz);
    Assert.Equal(162_550_000, dto.LastChannelHz);
    Assert.Equal(25_000, dto.ChannelSpacingHz);
  }

  [Fact]
  public void Get_Vhf_WithNoMap_UsesThePlanAroundTheRadiosVhfFrequency()
  {
    Tuning = new BandMapTuning("VHF", 146_520_000);
    BandMapController controller = CreateController();

    BandMapResponseDto dto = Body(controller.Get("VHF"));

    Assert.Equal(145_525_000, dto.DisplayMinHz);
    Assert.Equal(147_525_000, dto.DisplayMaxHz);
    Assert.Equal(145_525_000, dto.FirstChannelHz);
    Assert.Equal(147_525_000, dto.LastChannelHz);
    Assert.Equal(12_500, dto.ChannelSpacingHz);
  }

  [Fact]
  public void Get_Vhf_WithAStoredMap_UsesTheStoredWindow_NotTheRadiosFrequency()
  {
    Store(new BandMap
    {
      Band = "VHF",
      RangeMinHz = 154_000_000,
      RangeMaxHz = 156_000_000,
      ChannelSpacingHz = 12_500,
      ScannedAtUtc = _time.GetUtcNow(),
      Channels = new[] { new BandMapChannel(155_000_000, -40f) },
    });
    Tuning = new BandMapTuning("VHF", 146_520_000);
    BandMapController controller = CreateController();

    BandMapResponseDto dto = Body(controller.Get("VHF"));

    Assert.Equal(154_000_000, dto.DisplayMinHz);
    Assert.Equal(156_000_000, dto.DisplayMaxHz);
    Assert.Equal(154_000_000, dto.FirstChannelHz);
    Assert.Equal(156_000_000, dto.LastChannelHz);
    Assert.Single(dto.Channels);
  }

  [Fact]
  public void Get_Aud76FmMapWithNoRecordedRange_UsesTheFmPlansAxis()
  {
    Store(new BandMap
    {
      Band = "FM",
      ScannedAtUtc = _time.GetUtcNow(),
      Channels = new[] { new BandMapChannel(92_300_000, -31f) },
    });
    BandMapController controller = CreateController();

    BandMapResponseDto dto = Body(controller.Get("FM"));

    Assert.Equal(87_500_000, dto.DisplayMinHz);
    Assert.Equal(108_000_000, dto.DisplayMaxHz);
    Assert.Equal(200_000, dto.ChannelSpacingHz);
    Assert.Single(dto.Channels);
  }

  [Fact]
  public void Get_Am_IsNotMappable_WithTheReasonAndThePresetAxis()
  {
    BandMapController controller = CreateController();

    BandMapResponseDto dto = Body(controller.Get("AM"));

    Assert.Equal("AM", dto.Band);
    Assert.False(dto.Mappable);
    Assert.Equal(BandSweepPlans.UnavailableReason(RTLSDRCore.Enums.BandType.AM), dto.UnavailableReason);
    Assert.Contains("below this tuner's 24 MHz lower limit", dto.UnavailableReason);
    Assert.Equal(530_000, dto.DisplayMinHz);
    Assert.Equal(1_710_000, dto.DisplayMaxHz);
    Assert.Equal(530_000, dto.FirstChannelHz);
    Assert.Equal(1_710_000, dto.LastChannelHz);
    Assert.Equal(10_000, dto.ChannelSpacingHz);
    Assert.Empty(dto.Channels);
  }

  [Fact]
  public void Get_InvalidBand_Returns400ListingTheValidBands()
  {
    BandMapController controller = CreateController();

    BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(controller.Get("LW").Result);

    string body = System.Text.Json.JsonSerializer.Serialize(bad.Value);
    Assert.Contains("AM, FM, SW, AIR, WB, VHF", body);
  }

  [Theory]
  [InlineData("AM")]
  [InlineData("sw")]
  public void Scan_UnmappableBand_Returns409WithTheReason(string band)
  {
    BandMapController controller = CreateController();

    ObjectResult result = Assert.IsAssignableFrom<ObjectResult>(controller.Scan(band).Result);

    Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
    string body = System.Text.Json.JsonSerializer.Serialize(result.Value);
    Assert.Contains("cannot be scanned", body);
    Assert.Null(_service!.GetStatus().Last);
  }

  [Fact]
  public void Scan_WithNoBand_OnAnUnmappableRadioBand_Returns409()
  {
    Tuning = new BandMapTuning("AM", 1_000_000);
    BandMapController controller = CreateController();

    ObjectResult result = Assert.IsAssignableFrom<ObjectResult>(controller.Scan().Result);

    Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
  }

  [Fact]
  public void Scan_InvalidBand_Returns400()
  {
    BandMapController controller = CreateController();

    Assert.IsType<BadRequestObjectResult>(controller.Scan("1").Result);
  }

  [Fact]
  public async Task Scan_ExplicitBand_StartsThatBandsSweep_AndStatusCarriesTheBand()
  {
    await _gate.ClaimForRadioAsync();
    TaskCompletionSource release = new();
    BlockingSweeper live = new(release.Task);
    Mock<IOptionsMonitor<BandMapOptions>> monitor = new();
    monitor.Setup(m => m.CurrentValue).Returns(_options);
    _service = new BandMapService(
      NullLogger<BandMapService>.Instance, monitor.Object, new BandMapStore(_root, NullLogger.Instance), _gate,
      () => live, timeProvider: _time, currentTuning: () => Tuning);
    BandMapController controller = new(_service);

    ObjectResult accepted = Assert.IsAssignableFrom<ObjectResult>(controller.Scan("AIR").Result);
    BandSweepStatusDto status = Assert.IsType<BandSweepStatusDto>(accepted.Value);
    release.SetResult();
    Assert.True(SpinWait.SpinUntil(() => !_service.GetStatus().IsSweeping, TimeSpan.FromSeconds(30)), "sweep did not finish");

    Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
    Assert.Equal("AIR", status.Band);
    BandMapResponseDto dto = Body(controller.Get("AIR"));
    Assert.Equal("AIR", dto.Sweep.Last!.Band);
    Assert.Equal(1161, dto.Channels.Count);
  }

  private sealed class BlockingSweeper : ILiveBandSweeper
  {
    private readonly Task _release;

    public BlockingSweeper(Task release)
    {
      _release = release;
    }

    public bool CanSweepLive => true;

    public Task<IReadOnlyList<ChannelLevel>> SweepLiveAsync(
      IReadOnlyList<long> channels, float gainDb, int samplesPerMeasurement,
      IProgress<BandSweepProgress>? progress, CancellationToken cancellationToken) =>
      throw new InvalidOperationException("The service must call the plan overload.");

    public async Task<IReadOnlyList<ChannelLevel>> SweepLiveAsync(
      BandSweepPlan plan, float gainDb, int samplesPerMeasurement,
      IProgress<BandSweepProgress>? progress, CancellationToken cancellationToken)
    {
      await _release.WaitAsync(cancellationToken);
      return plan.Channels.Select(hz => new ChannelLevel(hz, -50f)).ToArray();
    }
  }
}
