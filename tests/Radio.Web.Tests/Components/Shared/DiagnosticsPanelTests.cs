using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for <see cref="DiagnosticsPanel"/> — Settings → Diagnostics (UI-2 / UI-4, D11).
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>The request counts are the point of this fixture.</b> The Metrics page this panel replaced
/// sent 41 requests per 10 s refresh (one snapshot call plus up to 40 per-metric history calls), and
/// that load is implicated in the audio distortion under investigation. These tests pin the bound:
/// one <c>/api/metrics/window</c> per poll, a <c>/api/metrics/history</c> only for a selected tile,
/// and nothing at all between polls or after disposal.
/// </para>
/// <para>
/// Polls are driven by a <see cref="FakeTimeProvider"/> and counted, never timed (CLAUDE.md § Test
/// Timing). Every API client talks to one <see cref="RoutedApiHandler"/>, which records every request.
/// </para>
/// </remarks>
public class DiagnosticsPanelTests : TestContext
{
  private readonly RoutedApiHandler _api = new();
  private readonly FakeTimeProvider _clock = new();

  private HttpClient NewClient() =>
    new(_api, disposeHandler: false) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };

  public DiagnosticsPanelTests()
  {
    Services.AddHermeticTestRig();
    JSInterop.Mode = JSRuntimeMode.Loose;

    Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    Services.AddRadzenComponents();

    Services.AddSingleton(_ => new MetricsApiService(NewClient(), NullLogger<MetricsApiService>.Instance));
    Services.AddSingleton(_ => new ConfigurationApiService(NewClient(), NullLogger<ConfigurationApiService>.Instance));

    // 220 keys on the real box, 148 of them per-route API counters. Four is enough to exercise every
    // branch: a counter, a gauge, a per-route counter, and a fingerprint metric.
    _api.Get("/api/metrics/window", new List<MetricWindowSummaryDto>
    {
      Summary("audio.buffer.underruns", "Counter", sum: 7, latest: 999),
      Summary("system.cpu_usage_percent", "Gauge", sum: 500, latest: 42),
      Summary("api.requests.api.Audio.volume", "Counter", sum: 3, latest: 3),
      Summary("fingerprint.identification_successes", "Counter", sum: 11, latest: 1),
    });
    _api.Get("/api/metrics/descriptors", new List<MetricDescriptorDto>());
    _api.Get("/api/metrics/history", new List<MetricHistoryDto>
    {
      new(DateTime.UtcNow.AddMinutes(-2), 1, 1, 1, 1, 1, null),
      new(DateTime.UtcNow.AddMinutes(-1), 2, 1, 2, 2, 2, null),
    });
  }

  private static MetricWindowSummaryDto Summary(string key, string type, double sum, double latest) =>
    new(key, type, sum, 10, 0, sum, latest, DateTimeOffset.UtcNow, 5);

  private IRenderedComponent<DiagnosticsPanel> RenderPanel()
  {
    var cut = RenderComponent<DiagnosticsPanel>(p => p.Add(x => x.Clock, _clock));
    // Load has finished once the window has been read and the tiles are drawn.
    cut.WaitForAssertion(() => cut.FindAll(".metric-tile").Should().NotBeEmpty());
    return cut;
  }

  private int CountRequests(string path) => _api.Requests.Count(r => r.Path == path);

  private int CountMetricsRequests() => _api.Requests.Count(r => r.Path.StartsWith("/api/metrics", StringComparison.Ordinal));

  [Fact]
  public void Load_IsBounded_NoPerMetricHistoryFanOut()
  {
    RenderPanel();

    // Three reads on open, whatever the number of metrics: preferences, descriptors, the window.
    _api.Requests.Should().HaveCount(3);
    CountRequests("/api/metrics/window").Should().Be(1);
    CountRequests("/api/metrics/descriptors").Should().Be(1);
    CountRequests("/api/metrics/history").Should().Be(0, "no tile is selected, so no history is read");
    CountRequests("/api/metrics/snapshots").Should().Be(0);
    CountRequests("/api/metrics/keys").Should().Be(0);
  }

  [Fact]
  public void Poll_IssuesExactlyOneWindowRequest_AtThePollInterval()
  {
    RenderPanel();
    int before = CountMetricsRequests();

    // One second short of the interval: the timer has not fired. (A bounded negative — a callback
    // that fired inside Advance would only be missed here if it were still in flight, which can only
    // weaken this check, never falsely fail it. The next step is the positive proof.)
    _clock.Advance(DiagnosticsPanel.PollInterval - TimeSpan.FromSeconds(1));
    CountMetricsRequests().Should().Be(before);

    _clock.Advance(TimeSpan.FromSeconds(1));
    WaitForState(() => CountMetricsRequests() == before + 1);

    CountRequests("/api/metrics/window").Should().Be(2);
    CountRequests("/api/metrics/history").Should().Be(0);

    _clock.Advance(DiagnosticsPanel.PollInterval);
    WaitForState(() => CountMetricsRequests() == before + 2);
    CountRequests("/api/metrics/window").Should().Be(3);
    CountRequests("/api/metrics/history").Should().Be(0);
  }

  [Fact]
  public void SelectedTile_AddsExactlyOneHistoryRequestPerPoll()
  {
    var cut = RenderPanel();

    cut.Find(".diagnostics-tile").Click();
    cut.WaitForAssertion(() => CountRequests("/api/metrics/history").Should().Be(1));

    int windowBefore = CountRequests("/api/metrics/window");
    _clock.Advance(DiagnosticsPanel.PollInterval);
    cut.WaitForAssertion(() => CountRequests("/api/metrics/history").Should().Be(2));
    CountRequests("/api/metrics/window").Should().Be(windowBefore + 1);
  }

  [Fact]
  public void Disposed_PanelNeverPollsAgain()
  {
    RenderPanel();
    int before = _api.Requests.Count;

    DisposeComponents();
    _clock.Advance(DiagnosticsPanel.PollInterval * 4);

    // The timer is disposed synchronously in DisposeAsync, so a FakeTimeProvider advance cannot fire it.
    _api.Requests.Count.Should().Be(before);
  }

  [Fact]
  public void CounterTile_ShowsWindowTotal_GaugeTile_ShowsLatestBucket()
  {
    var cut = RenderPanel();

    string Value(string key) =>
      cut.Find($".metric-tile[data-metric-key='{key}'] .metric-tile-value").TextContent.Trim();

    Value("audio.buffer.underruns").Should().Contain("7").And.NotContain("999");
    Value("system.cpu_usage_percent").Should().Contain("42").And.NotContain("500");
  }

  [Fact]
  public void PerRouteApiCounters_AreFoldedBehindAToggle()
  {
    var cut = RenderPanel();

    cut.FindAll(".metric-tile[data-metric-key='api.requests.api.Audio.volume']").Should().BeEmpty();
    cut.FindAll(".metric-tile[data-metric-key='fingerprint.identification_successes']").Should().ContainSingle(
      "the fingerprint group is what the DevTray card sends people here for");

    cut.Find("[data-testid='toggle-per-route']").Click();

    cut.FindAll(".metric-tile[data-metric-key='api.requests.api.Audio.volume']").Should().ContainSingle();
  }

  private static void WaitForState(Func<bool> condition)
  {
    // Rendezvous on the recorded request, not on elapsed time: the timer callback dispatches the
    // poll onto the renderer, so the request lands a moment after Advance returns.
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (!condition())
    {
      if (DateTime.UtcNow > deadline)
      {
        throw new TimeoutException("The expected poll never arrived.");
      }
      Thread.Sleep(5);
    }
  }
}
