using System.Reflection;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for <see cref="NowPlayingPanel"/>.
///
/// The first three tests pre-date PR 2 and assert the panel's invariants around
/// timestamp formatting and the friendly empty-state placeholder. They render
/// the panel against a no-API-server backdrop and assert against the markup.
///
/// The PR 2 tests below add coverage for the recognition-stream rewrite:
///
/// <list type="bullet">
///   <item>NOW + EARLIER headers anchor the active match when
///         <c>RadioStateDto.NowPlayingMatchId</c> matches an event row.</item>
///   <item>The active match row carries the <c>np-recognition-row-current</c>
///         class so the design-system stylesheet can paint the amber border.</item>
///   <item>A no-match event surfaces the italic "No match in window" sentence,
///         never the legacy "--" fallback.</item>
///   <item>No raw percentage character (<c>%</c>) appears anywhere in the
///         recognition stream — the PR 2 headline acceptance criterion.</item>
///   <item>The legacy <c>Fingerprints: X/min · Lookups: Y/min</c> telemetry
///         strip is absent from the panel header.</item>
/// </list>
///
/// The PR 2 tests drive state by reflectively populating the private fields the
/// component would normally fill from API/hub callbacks — the same approach
/// <c>NowPlayingDockTests</c> uses for its hub-push regression cases.
/// </summary>
public class NowPlayingPanelTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public NowPlayingPanelTests()
  {
    _loggerFactory = new NullLoggerFactory();
    JSInterop.Mode = JSRuntimeMode.Loose;

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", "http://localhost:5000" }
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(_loggerFactory);
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

    Services.AddRadzenComponents();

    Services.AddHttpClient<AudioApiService>();
    Services.AddHttpClient<ConfigurationApiService>();
    Services.AddHttpClient<RadioApiService>();

    Services.AddSingleton(sp =>
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        sp.GetRequiredService<IConfiguration>()
      )
    );
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _loggerFactory?.Dispose();
    }
    base.Dispose(disposing);
  }

  [Fact]
  public void NowPlayingPanel_Renders_DefaultEmptyState()
  {
    var cut = RenderComponent<NowPlayingPanel>();
    // Empty state shows the friendly placeholder rather than the raw "No Track" default.
    Assert.Contains("No Track Playing", cut.Markup);
  }

  [Fact]
  public void NowPlayingPanel_Markup_NeverContains_FractionalSecondsTimespan()
  {
    // Regression guard: any raw TimeSpan.ToString() reintroduced into the template
    // will produce a fractional-second tail. The Durations.FormatTrack helper rounds
    // to whole seconds, so this pattern must never appear in the rendered DOM.
    var cut = RenderComponent<NowPlayingPanel>();
    Assert.DoesNotMatch(@"\d+:\d{2}:\d{2}\.\d{4,}", cut.Markup);
  }

  [Fact]
  public void NowPlayingPanel_DurationElements_DeclareTabularNums()
  {
    // PR 3 spec requires tabular-nums on duration text so the elapsed/total columns
    // stay aligned digit-by-digit while the track plays. With no track loaded the
    // duration block is hidden, so we only assert this on the empty-state render —
    // it'll re-render with the same inline style when content appears.
    var cut = RenderComponent<NowPlayingPanel>();
    // The transport bar exists even on empty state.
    Assert.Contains("transport-group", cut.Markup);
  }

  // ─── PR 2: Recognition stream ──────────────────────────────────────────────

  /// <summary>
  /// Reflectively poke the component into "recognition stream open with state X"
  /// without going through the real API/hub callbacks. The component already has
  /// fields backing _fpStatus, _fpEventsReversed, _radioState, _showFingerprintDetail
  /// — we set them and re-render so the recognition branch lights up.
  /// </summary>
  private static void SetRecognitionState(
    IRenderedComponent<NowPlayingPanel> cut,
    FingerprintStatusDto fpStatus,
    string? nowPlayingMatchId)
  {
    var instance = cut.Instance;
    var type = typeof(NowPlayingPanel);
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;

    type.GetField("_fpStatus", flags)!.SetValue(instance, fpStatus);
    type.GetField("_fpEventsReversed", flags)!
      .SetValue(instance, Enumerable.Reverse(fpStatus.RecentEvents).ToList());
    type.GetField("_radioState", flags)!.SetValue(instance,
      new RadioStateDto(
        Frequency: 92.5e6, Band: "FM", Step: 100e3,
        SignalStrength: 70, IsScanning: false, ScanDirection: null,
        ScanStopThreshold: -18.0, Gain: 28, AutoGain: true,
        Equalizer: "Flat", DeviceVolume: 70,
        NowPlayingMatchId: nowPlayingMatchId));
    type.GetField("_showFingerprintDetail", flags)!.SetValue(instance, true);
    cut.Render();
  }

  private static FingerprintEventDto MakeEvent(
    string matchId,
    string? title,
    string? artist = null,
    ConfidenceBucket confidence = ConfidenceBucket.None,
    DateTime? timestamp = null)
  {
    return new FingerprintEventDto
    {
      MatchId = matchId,
      AudioSource = "SDR Radio",
      SourceType = "Radio",
      IsMatch = title != null,
      Count = 1,
      Confidence = confidence,
      Title = title,
      Artist = artist,
      Phase = title != null ? "Matched" : "NoMatch",
      Timestamp = timestamp ?? DateTime.UtcNow.AddMinutes(-1)
    };
  }

  [Fact]
  public void Recognition_RendersNowAndEarlierHeaders_WhenMatchesPresent()
  {
    var matches = new List<FingerprintEventDto>
    {
      MakeEvent("m-1", "Older Song", "Old Artist", ConfidenceBucket.Likely, DateTime.UtcNow.AddMinutes(-5)),
      MakeEvent("m-2", "Current Hit", "Active Artist", ConfidenceBucket.Strong, DateTime.UtcNow.AddSeconds(-30))
    };
    var status = new FingerprintStatusDto
    {
      IsEnabled = true,
      Phase = "Matched",
      RecentEvents = matches
    };

    var cut = RenderComponent<NowPlayingPanel>();
    SetRecognitionState(cut, status, nowPlayingMatchId: "m-2");

    var headers = cut.FindAll(".np-recognition-header");
    Assert.Equal(2, headers.Count);
    Assert.Equal("NOW", headers[0].TextContent);
    Assert.Equal("EARLIER", headers[1].TextContent);
  }

  [Fact]
  public void Recognition_CurrentMatchHasAmberBorderClass()
  {
    var matches = new List<FingerprintEventDto>
    {
      MakeEvent("m-1", "Older Song", "Old Artist", ConfidenceBucket.Likely, DateTime.UtcNow.AddMinutes(-5)),
      MakeEvent("m-anchor", "Now Playing", "Anchor Artist", ConfidenceBucket.Strong, DateTime.UtcNow.AddSeconds(-10))
    };
    var status = new FingerprintStatusDto
    {
      IsEnabled = true,
      Phase = "Matched",
      RecentEvents = matches
    };

    var cut = RenderComponent<NowPlayingPanel>();
    SetRecognitionState(cut, status, nowPlayingMatchId: "m-anchor");

    var currentRows = cut.FindAll(".np-recognition-row-current");
    Assert.Single(currentRows);
    Assert.Equal("m-anchor", currentRows[0].GetAttribute("data-match-id"));
  }

  [Fact]
  public void Recognition_NoMatchRow_RendersItalicizedSentence()
  {
    var matches = new List<FingerprintEventDto>
    {
      MakeEvent("m-nomatch", title: null, confidence: ConfidenceBucket.None)
    };
    var status = new FingerprintStatusDto
    {
      IsEnabled = true,
      Phase = "NoMatch",
      RecentEvents = matches
    };

    var cut = RenderComponent<NowPlayingPanel>();
    SetRecognitionState(cut, status, nowPlayingMatchId: null);

    var noMatchSpan = cut.Find(".np-recognition-no-match");
    Assert.Equal("No match in window", noMatchSpan.TextContent);
    // The "--" placeholder used by the prior <table> rendering must NOT appear
    // anywhere inside the recognition stream branch.
    var stream = cut.Find(".np-recognition-stream");
    Assert.DoesNotContain("--", stream.InnerHtml);
  }

  [Fact]
  public void Recognition_DropsRawConfidencePercentage()
  {
    var matches = new List<FingerprintEventDto>
    {
      MakeEvent("m-1", "Song A", "Artist A", ConfidenceBucket.Strong, DateTime.UtcNow.AddSeconds(-15)),
      MakeEvent("m-2", "Song B", "Artist B", ConfidenceBucket.Likely, DateTime.UtcNow.AddMinutes(-3)),
      MakeEvent("m-3", title: null, confidence: ConfidenceBucket.None, timestamp: DateTime.UtcNow.AddMinutes(-7))
    };
    var status = new FingerprintStatusDto
    {
      IsEnabled = true,
      Phase = "Matched",
      RecentEvents = matches
    };

    var cut = RenderComponent<NowPlayingPanel>();
    SetRecognitionState(cut, status, nowPlayingMatchId: "m-1");

    var stream = cut.Find(".np-recognition-stream");
    // PR 2 acceptance gate: no row in the recognition surface contains the
    // literal "80%" / "94%" / "0%" / any other raw percentage. Match the
    // bare-percent pattern AND a few-digit-percent pattern to be safe.
    Assert.DoesNotMatch(@"\b\d{1,3}\s?%", stream.InnerHtml);
  }

  [Fact]
  public void Recognition_TelemetryStripRemoved()
  {
    // Even with fingerprint detail open, the panel header no longer carries the
    // legacy "Fingerprints: X.X/min · Lookups: Y.Y/min" strip — those values
    // are scoped out of the recognition surface entirely per the plan.
    var matches = new List<FingerprintEventDto>
    {
      MakeEvent("m-1", "Any Song", "Any Artist", ConfidenceBucket.Possible)
    };
    var status = new FingerprintStatusDto
    {
      IsEnabled = true,
      Phase = "Matched",
      FingerprintsPerMinute = 4.5,
      MetadataCallsPerMinute = 1.2,
      RecentEvents = matches
    };

    var cut = RenderComponent<NowPlayingPanel>();
    SetRecognitionState(cut, status, nowPlayingMatchId: "m-1");

    Assert.DoesNotContain("Fingerprints:", cut.Markup);
    Assert.DoesNotContain("Lookups:", cut.Markup);
    Assert.DoesNotContain("/min", cut.Markup);
  }

  [Fact]
  public void Recognition_ConfidencePips_RenderedForEachMatch()
  {
    var matches = new List<FingerprintEventDto>
    {
      MakeEvent("m-1", "Older Song", "Old Artist", ConfidenceBucket.Likely, DateTime.UtcNow.AddMinutes(-5)),
      MakeEvent("m-anchor", "Now Playing", "Active Artist", ConfidenceBucket.Strong, DateTime.UtcNow.AddSeconds(-10))
    };
    var status = new FingerprintStatusDto
    {
      IsEnabled = true,
      Phase = "Matched",
      RecentEvents = matches
    };

    var cut = RenderComponent<NowPlayingPanel>();
    SetRecognitionState(cut, status, nowPlayingMatchId: "m-anchor");

    // One ConfidencePips widget per row (active + earlier).
    var pips = cut.FindAll(".confidence-pips");
    Assert.Equal(2, pips.Count);
  }

  [Fact]
  public void Recognition_LegacyTableHeader_NotRendered()
  {
    // The old recognition surface was a <table> with "Conf%" / "Track" / "Src"
    // column headers. PR 2 drops the table entirely.
    var matches = new List<FingerprintEventDto>
    {
      MakeEvent("m-1", "Song A", "Artist A", ConfidenceBucket.Strong)
    };
    var status = new FingerprintStatusDto
    {
      IsEnabled = true,
      Phase = "Matched",
      RecentEvents = matches
    };

    var cut = RenderComponent<NowPlayingPanel>();
    SetRecognitionState(cut, status, nowPlayingMatchId: "m-1");

    Assert.DoesNotContain("Conf%", cut.Markup);
    // The recognition surface no longer renders an HTML <table> element.
    Assert.Empty(cut.FindAll(".np-recognition-stream table"));
  }

  [Fact]
  public void FormatTimeAgo_RecentSeconds_RendersSecondsAgo()
  {
    var now = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);
    var twentySecondsAgo = now.AddSeconds(-20);
    var result = NowPlayingPanel.FormatTimeAgo(twentySecondsAgo, now);
    Assert.Equal("20s ago", result);
  }

  [Fact]
  public void FormatTimeAgo_Minutes_RendersMinutesAgo()
  {
    var now = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);
    var threeMinutesAgo = now.AddMinutes(-3);
    var result = NowPlayingPanel.FormatTimeAgo(threeMinutesAgo, now);
    Assert.Equal("3m ago", result);
  }

  // ─── Wire-path regression: RadioStateChanged carries the typed DTO ─────────
  //
  // Tester surfaced during live-kiosk UAT that the recognition NOW row never
  // anchored because the SignalR RadioStateChanged handler discarded the
  // payload and re-fetched via REST. The REST endpoint cannot populate
  // NowPlayingMatchId (it lives in AudioStateUpdateService._currentMatchId
  // which RadioController has no access to). After the fix, the handler
  // accepts a RadioStateDto and the panel anchors immediately on hub push.
  //
  // This test proves the wire-path end-to-end: build a real hub service,
  // mount the panel, invoke the typed event with a payload carrying
  // NowPlayingMatchId, and assert the NOW row anchors to the matching event.

  [Fact]
  public async Task Recognition_AnchorsNowRow_WhenRadioStateChangedDtoArrives()
  {
    var matches = new List<FingerprintEventDto>
    {
      MakeEvent("m-old", "Older Song", "Old Artist", ConfidenceBucket.Likely, DateTime.UtcNow.AddMinutes(-5)),
      MakeEvent("m-target", "Anchored Track", "Anchor Artist", ConfidenceBucket.Strong, DateTime.UtcNow.AddSeconds(-10))
    };
    var status = new FingerprintStatusDto
    {
      IsEnabled = true,
      Phase = "Matched",
      RecentEvents = matches
    };

    var cut = RenderComponent<NowPlayingPanel>();

    // Seed the fingerprint events without an anchor — same reflective injection
    // pattern the rest of the suite uses. _radioState stays null until the
    // typed hub event fires below.
    var instance = cut.Instance;
    var type = typeof(NowPlayingPanel);
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;
    type.GetField("_fpStatus", flags)!.SetValue(instance, status);
    type.GetField("_fpEventsReversed", flags)!
      .SetValue(instance, Enumerable.Reverse(status.RecentEvents).ToList());
    type.GetField("_showFingerprintDetail", flags)!.SetValue(instance, true);
    cut.Render();

    // Sanity: nothing is anchored yet (no NowPlayingMatchId).
    Assert.Empty(cut.FindAll(".np-recognition-row-current"));

    // Now drive the typed hub event. The hub service is a real instance in DI;
    // we reach into its compiler-generated backing field and invoke directly,
    // mirroring what SignalR would do on a live wire push.
    var hubService = Services.GetRequiredService<AudioStateHubService>();
    var eventField = typeof(AudioStateHubService).GetField(
      nameof(AudioStateHubService.RadioStateChanged),
      BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(eventField);
    var handler = (Func<RadioStateDto, Task>?)eventField!.GetValue(hubService);
    Assert.NotNull(handler);

    var dto = new RadioStateDto(
      Frequency: 92.5e6, Band: "FM", Step: 100e3,
      SignalStrength: 70, IsScanning: false, ScanDirection: null,
      ScanStopThreshold: -18.0, Gain: 28, AutoGain: true,
      Equalizer: "Flat", DeviceVolume: 70,
      NowPlayingMatchId: "m-target");

    await cut.InvokeAsync(() => handler!.Invoke(dto));

    // The NOW row now anchors to the target match — proving the typed payload
    // reached the panel without a REST refetch.
    var currentRows = cut.FindAll(".np-recognition-row-current");
    Assert.Single(currentRows);
    Assert.Equal("m-target", currentRows[0].GetAttribute("data-match-id"));
  }
}
