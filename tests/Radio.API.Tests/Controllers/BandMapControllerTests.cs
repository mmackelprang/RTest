using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Radio.API.Controllers;
using Radio.API.Models;
using Radio.API.Tests.TestSupport;
using Radio.Core.Configuration;
using Radio.Core.Models;
using Radio.Infrastructure.Audio.Services;
using RTLSDRCore.Hardware;
using RTLSDRCore.Sweep;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// AUD-76: <c>api/radio/bandmap</c>. Unit tests drive a real <see cref="BandMapService"/> over
/// fakes; one integration test checks the route resolves in the real host.
/// </summary>
public sealed class BandMapControllerTests : IDisposable, IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly CustomWebApplicationFactory<Program> _factory;
  private readonly string _root = Path.Combine(Path.GetTempPath(), "aud76-api-" + Guid.NewGuid().ToString("N"));
  private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
  private readonly SdrDeviceGate _gate = new();
  private readonly FakeLiveSweeper _live = new();
  private readonly BandMapOptions _options = new();
  private BandMapService? _service;

  public BandMapControllerTests(CustomWebApplicationFactory<Program> factory)
  {
    _factory = factory;
  }

  public void Dispose()
  {
    _service?.Dispose();
    if (Directory.Exists(_root))
    {
      Directory.Delete(_root, recursive: true);
    }
  }

  private BandMapController CreateController(Func<ISdrDevice?>? deviceFactory = null)
  {
    Mock<IOptionsMonitor<BandMapOptions>> monitor = new();
    monitor.Setup(m => m.CurrentValue).Returns(_options);
    _service = new BandMapService(
      NullLogger<BandMapService>.Instance,
      monitor.Object,
      new BandMapStore(_root, NullLogger.Instance),
      _gate,
      () => _live,
      sleepService: null,
      deviceFactory: deviceFactory ?? (() => null),
      timeProvider: _time);
    return new BandMapController(_service);
  }

  /// <summary>
  /// Waits on the service's own state: a started sweep reports IsSweeping until its result
  /// (map, outcome) has been recorded. The bound only limits a broken test.
  /// </summary>
  private void WaitForSweepToFinish()
  {
    Assert.True(SpinWait.SpinUntil(() => !_service!.GetStatus().IsSweeping, TimeSpan.FromSeconds(30)),
      "sweep did not finish");
  }

  [Fact]
  public void Get_BeforeAnyScan_ReturnsEmptyChannelsAndNullTime()
  {
    BandMapController controller = CreateController();

    ActionResult<BandMapResponseDto> result = controller.Get();

    OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
    BandMapResponseDto dto = Assert.IsType<BandMapResponseDto>(ok.Value);
    Assert.Equal("FM", dto.Band);
    Assert.Null(dto.ScannedAtUtc);
    Assert.Null(dto.AgeSeconds);
    Assert.Empty(dto.Channels);
    Assert.False(dto.Sweep.IsSweeping);
    Assert.Null(dto.Sweep.Last);
  }

  [Fact]
  public async Task Scan_WhileRadioHolds_StartsLiveSweep_Returns202_ThenGetReturnsMapWithAge()
  {
    await _gate.ClaimForRadioAsync();
    _live.CanSweepLive = true;
    BandMapController controller = CreateController();

    ActionResult<BandSweepStatusDto> scan = controller.Scan();

    ObjectResult accepted = Assert.IsAssignableFrom<ObjectResult>(scan.Result);
    Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
    Assert.IsType<BandSweepStatusDto>(accepted.Value);
    WaitForSweepToFinish();

    _time.Advance(TimeSpan.FromSeconds(90));
    BandMapResponseDto dto = Assert.IsType<BandMapResponseDto>(
      Assert.IsType<OkObjectResult>(controller.Get().Result).Value);
    Assert.Equal(FmChannelPlan.Channels.Count, dto.Channels.Count);
    Assert.Equal(_time.GetUtcNow().AddSeconds(-90), dto.ScannedAtUtc);
    Assert.Equal(90, dto.AgeSeconds);
    Assert.Equal("completed", dto.Sweep.Last!.Result);
    Assert.Equal("live", dto.Sweep.Last.Path);
  }

  [Fact]
  public async Task Scan_WhileASweepIsRunning_Returns202WithoutStartingAnother()
  {
    await _gate.ClaimForRadioAsync();
    _live.CanSweepLive = true;
    _live.Block = new TaskCompletionSource();
    BandMapController controller = CreateController();
    controller.Scan();
    await _live.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

    ActionResult<BandSweepStatusDto> second = controller.Scan();

    ObjectResult accepted = Assert.IsAssignableFrom<ObjectResult>(second.Result);
    Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
    Assert.True(Assert.IsType<BandSweepStatusDto>(accepted.Value).IsSweeping);
    Assert.Equal(1, _live.Calls);

    _live.Block.SetResult();
    WaitForSweepToFinish();
  }

  [Fact]
  public void Scan_WhenNoSdrDevice_Returns503()
  {
    BandMapController controller = CreateController(deviceFactory: () => null);

    ObjectResult result = Assert.IsAssignableFrom<ObjectResult>(controller.Scan().Result);

    Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
  }

  [Fact]
  public void Scan_WhenDisabled_Returns503()
  {
    _options.Enabled = false;
    BandMapController controller = CreateController();

    ObjectResult result = Assert.IsAssignableFrom<ObjectResult>(controller.Scan().Result);

    Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
  }

  [Fact]
  public async Task Scan_WhenRadioHoldsAndCannotSweepLive_Returns409()
  {
    await _gate.ClaimForRadioAsync();
    _live.CanSweepLive = false;
    BandMapController controller = CreateController();

    ObjectResult result = Assert.IsAssignableFrom<ObjectResult>(controller.Scan().Result);

    Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
  }

  [Fact]
  public async Task Get_InRealHost_Returns200WithCamelCaseShape()
  {
    HttpClient client = _factory.CreateClient();

    HttpResponseMessage response = await client.GetAsync("/api/radio/bandmap");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Assert.Equal("FM", json.RootElement.GetProperty("band").GetString());
    Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("channels").ValueKind);
    Assert.True(json.RootElement.TryGetProperty("scannedAtUtc", out _));
    Assert.True(json.RootElement.GetProperty("sweep").TryGetProperty("isSweeping", out _));
  }

  private sealed class FakeLiveSweeper : ILiveBandSweeper
  {
    private int _calls;

    public bool CanSweepLive { get; set; }

    public int Calls => Volatile.Read(ref _calls);

    public TaskCompletionSource? Block { get; set; }

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<IReadOnlyList<ChannelLevel>> SweepLiveAsync(
      IReadOnlyList<long> channels, float gainDb, int samplesPerMeasurement,
      IProgress<BandSweepProgress>? progress, CancellationToken cancellationToken)
    {
      Interlocked.Increment(ref _calls);
      Entered.TrySetResult();
      if (Block != null)
      {
        await Block.Task.WaitAsync(cancellationToken);
      }
      return channels.Select(hz => new ChannelLevel(hz, -50f)).ToArray();
    }
  }
}
