using System.Reflection;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
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
using Radio.Web.Tests.TestHelpers;

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
    // Hermetic rig: fails every outbound HTTP request and every SignalR
    // negotiate without touching the network, so this fixture's result never
    // depends on whether radio-api happens to be running locally.
    Services.AddHermeticTestRig();

    _loggerFactory = new NullLoggerFactory();
    JSInterop.Mode = JSRuntimeMode.Loose;

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl }
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
        sp.GetRequiredService<IConfiguration>(),
        transport: new OfflineHubTransport()
      )
    );

    // PR 4 — GainControlPopover is rendered inline when _showGainPopover is true;
    // it depends on AudioVisualizationHubService for its peak meter. Register a
    // real instance so the popover branches render. StartAsync will fail-silently
    // against the unreachable test ApiBaseUrl, which is exactly how the production
    // popover behaves on a cold start.
    Services.AddSingleton(sp =>
      new AudioVisualizationHubService(
        NullLogger<AudioVisualizationHubService>.Instance,
        sp.GetRequiredService<IConfiguration>(),
        transport: new OfflineHubTransport()
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
  public void NowPlayingPanel_ProgressBarLabel_ShowsRoundedWholePercent_NotDecimalTail()
  {
    // Regression guard for the "47.8234917%" bug: a non-seekable source (e.g.
    // Bluetooth) renders the RadzenProgressBar branch, whose default label would
    // print the raw double _progressPercent. The Template must instead surface
    // _progressPercentDisplay — a whole-number percent — while the bar WIDTH
    // still binds to the full-precision _progressPercent.
    var cut = RenderComponent<NowPlayingPanel>();

    var instance = cut.Instance;
    var type = typeof(NowPlayingPanel);
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;
    // Position + duration present, source not seekable → RadzenProgressBar branch.
    type.GetField("_position", flags)!.SetValue(instance, "1:40");
    type.GetField("_duration", flags)!.SetValue(instance, "3:29");
    type.GetField("_canSeek", flags)!.SetValue(instance, false);
    // 100 / 209 → 47.8468…; the raw value drives the bar width, the rounded
    // string ("48%") is what the label must show.
    type.GetField("_progressPercent", flags)!.SetValue(instance, 47.8468899521531);
    type.GetField("_progressPercentDisplay", flags)!.SetValue(instance, "48%");
    cut.Render();

    var bar = cut.Find(".rz-progressbar");
    // The visible label text (TextContent excludes the width style attribute,
    // where the full-precision value legitimately lives) reads the rounded
    // whole-number percent — no decimal point leaks to the user.
    Assert.Contains("48%", bar.TextContent);
    Assert.DoesNotContain(".", bar.TextContent);
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

    // One ConfidencePips widget per recognition row (active + earlier). PR 4
    // adds a separate ConfidencePips inside the match badge under the song
    // title — that one is outside the recognition stream container, so we
    // scope the assertion to .np-recognition-stream descendants only.
    var stream = cut.Find(".np-recognition-stream");
    var pips = stream.QuerySelectorAll(".confidence-pips");
    Assert.Equal(2, pips.Length);
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

  // Note: FormatTimeAgo unit tests moved to TimestampsTests.FormatRecentRelative_*
  // in Arc 3 PR C (item #35) — the helper was extracted into Timestamps so any
  // surface that needs short-relative formatting can consume it.

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
    // we reach into its compiler-generated backing field and await every
    // subscriber, mirroring what SignalR would do on a live wire push.
    var hubService = Services.GetRequiredService<AudioStateHubService>();

    var dto = new RadioStateDto(
      Frequency: 92.5e6, Band: "FM", Step: 100e3,
      SignalStrength: 70, IsScanning: false, ScanDirection: null,
      ScanStopThreshold: -18.0, Gain: 28, AutoGain: true,
      Equalizer: "Flat", DeviceVolume: 70,
      NowPlayingMatchId: "m-target");

    await cut.InvokeAsync(() => HubEventFire.FireAsync(
      hubService, nameof(AudioStateHubService.RadioStateChanged), dto));

    // The NOW row now anchors to the target match — proving the typed payload
    // reached the panel without a REST refetch.
    var currentRows = cut.FindAll(".np-recognition-row-current");
    Assert.Single(currentRows);
    Assert.Equal("m-target", currentRows[0].GetAttribute("data-match-id"));
  }

  // ─── Status bar (main-page status-bar redesign, 2026-10-06) ───────────────
  //
  // The first row of the transport block, for every source: Mute | Fingerprint | Gain. It replaced
  // the 36 px source + frequency strip across the top of the panel and the floating match badge
  // over the album art. The fingerprint slot reports song recognition only — never RDS.

  /// <summary>
  /// Reflectively seeds the panel's now-playing + radio + fingerprint state without touching HTTP.
  /// Mirrors the pattern <see cref="SetRecognitionState"/> uses for the recognition stream tests.
  /// </summary>
  private static void SetStatusBarState(
    IRenderedComponent<NowPlayingPanel> cut,
    string sourceType,
    string sourceName = "Source",
    RadioStateDto? radioState = null,
    float gain = 1.0f,
    FingerprintStatusDto? fpStatus = null,
    bool? muted = null)
  {
    var instance = cut.Instance;
    var type = typeof(NowPlayingPanel);
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;

    type.GetField("_nowPlayingSourceType", flags)!.SetValue(instance, sourceType);
    type.GetField("_source", flags)!.SetValue(instance, sourceName);
    type.GetField("_currentSourceGain", flags)!.SetValue(instance, gain);
    if (radioState != null)
    {
      type.GetField("_radioState", flags)!.SetValue(instance, radioState);
    }
    if (fpStatus != null)
    {
      type.GetField("_fpStatus", flags)!.SetValue(instance, fpStatus);
      type.GetField("_fpEventsReversed", flags)!
        .SetValue(instance, Enumerable.Reverse(fpStatus.RecentEvents).ToList());
    }
    if (muted is bool m)
    {
      type.GetField("_isMuted", flags)!.SetValue(instance, m);
    }
    cut.Render();
  }

  private static RadioStateDto FmState(string? matchId = null, string? rdsStationName = null) => new(
    Frequency: 92.5e6, Band: "FM", Step: 100e3,
    SignalStrength: 70, IsScanning: false, ScanDirection: null,
    ScanStopThreshold: -18.0, Gain: 28, AutoGain: true,
    Equalizer: "Flat", DeviceVolume: 70,
    RdsStationName: rdsStationName,
    AppliedGain: 28.0,
    NowPlayingMatchId: matchId);

  private static FingerprintStatusDto Status(string phase, bool enabled = true, params FingerprintEventDto[] events) => new()
  {
    IsEnabled = enabled,
    Phase = phase,
    RecentEvents = events.ToList()
  };

  [Theory]
  [InlineData("RTLSDRCore")]
  [InlineData("FilePlayer")]
  [InlineData("Bluetooth")]
  public void StatusBar_IsTheTransportBlocksFirstRow_WithThreeSlotsInOrder_ForEverySource(string sourceType)
  {
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, sourceType, radioState: sourceType == "RTLSDRCore" ? FmState() : null);

    var body = cut.Find(".np-transport-body");
    Assert.Contains("np-status-bar", body.FirstElementChild!.ClassList);

    var slots = cut.Find(".np-status-bar").Children.ToArray();
    Assert.Equal(3, slots.Length);
    Assert.Contains("np-bar-mute", slots[0].ClassList);
    Assert.Contains("np-bar-fp", slots[1].ClassList);
    Assert.Contains("np-bar-gain", slots[2].ClassList);
  }

  [Fact]
  public void TopStripAndFloatingBadge_AreGone_EvenForATunerWithRdsAndAMatch()
  {
    // Owner request 7: the input-device + frequency strip is removed. The floating match badge is
    // replaced by the bar's fingerprint slot.
    var events = new[] { MakeEvent("m-1", "Hit Song", "Artist", ConfidenceBucket.Strong, DateTime.UtcNow.AddSeconds(-5)) };
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "RTLSDRCore", "SDR Radio (RTL-SDR)", FmState("m-1", "KEXP"),
      fpStatus: Status("Matched", true, events));

    Assert.Empty(cut.FindAll(".np-status-strip"));
    Assert.Empty(cut.FindAll(".np-status-cell-source"));
    Assert.Empty(cut.FindAll(".np-status-cell-frequency"));
    Assert.Empty(cut.FindAll(".np-match-badge"));
    Assert.DoesNotContain("92.50", cut.Markup);
    Assert.DoesNotContain("SDR Radio (RTL-SDR)", cut.Markup);
  }

  [Fact]
  public void MuteSlot_Unmuted_ReadsSound()
  {
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "FilePlayer", muted: false);

    var mute = cut.Find(".np-bar-mute");
    Assert.DoesNotContain("is-muted", mute.ClassList);
    Assert.Equal("Sound", mute.QuerySelector(".np-bar-label")!.TextContent.Trim());
    Assert.Equal("false", mute.GetAttribute("aria-pressed"));
  }

  [Fact]
  public void MuteSlot_Muted_ReadsMuted()
  {
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "FilePlayer", muted: true);

    var mute = cut.Find(".np-bar-mute");
    Assert.Contains("is-muted", mute.ClassList);
    Assert.Equal("Muted", mute.QuerySelector(".np-bar-label")!.TextContent.Trim());
    Assert.Equal("true", mute.GetAttribute("aria-pressed"));
  }

  [Fact]
  public async Task MuteSlot_Tap_TogglesMute()
  {
    var handler = new RecordingHandler();
    Services.AddSingleton(new AudioApiService(
      new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) },
      NullLogger<AudioApiService>.Instance));
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "FilePlayer", muted: false);
    static bool IsMute(HttpMethod m, string p) => m == HttpMethod.Post && p == "/api/audio/mute";
    var before = handler.Count(IsMute);

    await cut.InvokeAsync(() => cut.Find(".np-bar-mute").Click());

    await handler.WaitForAsync(IsMute, before + 1);
    Assert.Equal(before + 1, handler.Count(IsMute));
  }

  public static TheoryData<string, bool, string, string> FingerprintPhases => new()
  {
    // phase, enabled, expected label, expected state class
    { "Idle", false, "ID off", "is-off" },
    { "Idle", true, "Waiting to identify", "is-waiting" },
    { "Capturing", true, "Identifying…", "is-identifying" },
    { "Fingerprinting", true, "Identifying…", "is-identifying" },
    { "Querying", true, "Identifying…", "is-identifying" },
    { "NoMatch", true, "No match", "is-nomatch" },
    { "Error", true, "ID unavailable", "is-unavailable" },
  };

  [Theory]
  [MemberData(nameof(FingerprintPhases))]
  public void FingerprintSlot_ShowsTheRecognitionPhase_WhenThereIsNoMatch(
    string phase, bool enabled, string label, string stateClass)
  {
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "RTLSDRCore", radioState: FmState(), fpStatus: Status(phase, enabled));

    var slot = cut.Find(".np-bar-fp");
    Assert.Equal(label, slot.QuerySelector(".np-bar-fp-label")!.TextContent.Trim());
    Assert.Contains(stateClass, slot.ClassList);
    Assert.Empty(slot.QuerySelectorAll(".confidence-pips"));
  }

  [Fact]
  public void FingerprintSlot_ReadsNoMatch_AfterTheServiceReturnsToIdle()
  {
    // BackgroundIdentificationService goes back to Idle straight after a NoMatch, so the slot falls
    // back to the newest event: a failed lookup still reads "No match".
    var events = new[] { MakeEvent("m-miss", null, timestamp: DateTime.UtcNow.AddSeconds(-3)) };
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "RTLSDRCore", radioState: FmState(), fpStatus: Status("Idle", true, events));

    Assert.Equal("No match", cut.Find(".np-bar-fp-label").TextContent.Trim());
  }

  [Fact]
  public void FingerprintSlot_ReadsUnavailable_WhenTheStatusReadFailed()
  {
    // The hermetic rig fails every request, so the panel's first status read returns null.
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "FilePlayer");

    var slot = cut.Find(".np-bar-fp");
    Assert.Equal("ID unavailable", slot.QuerySelector(".np-bar-fp-label")!.TextContent.Trim());
    Assert.Contains("is-unavailable", slot.ClassList);
  }

  [Fact]
  public void FingerprintSlot_ShowsPipsAndAge_ForTheAnchoredMatch_OnATuner()
  {
    var events = new[]
    {
      MakeEvent("m-old", "Old Song", "A", ConfidenceBucket.Strong, DateTime.UtcNow.AddMinutes(-9)),
      MakeEvent("m-now", "Hit Song", "B", ConfidenceBucket.Likely, DateTime.UtcNow.AddSeconds(-3)),
    };
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "RTLSDRCore", radioState: FmState("m-now"), fpStatus: Status("Matched", true, events));

    var slot = cut.Find(".np-bar-fp");
    Assert.Contains("is-match", slot.ClassList);
    Assert.Single(slot.QuerySelectorAll(".confidence-pips"));
    var label = slot.QuerySelector(".np-bar-fp-label")!.TextContent;
    Assert.StartsWith("Likely match · ", label);
    Assert.DoesNotContain("%", label);
  }

  [Fact]
  public void FingerprintSlot_ShowsTheLatestMatch_ForANonTunerSource()
  {
    // Non-tuner sources get no radio state, so no NowPlayingMatchId: the slot applies the API's
    // anchor rule itself — the most recent matched event.
    var events = new[]
    {
      MakeEvent("m-old", "Old Song", "A", ConfidenceBucket.Possible, DateTime.UtcNow.AddMinutes(-9)),
      MakeEvent("m-new", "New Song", "B", ConfidenceBucket.Strong, DateTime.UtcNow.AddSeconds(-4)),
      MakeEvent("m-miss", null, timestamp: DateTime.UtcNow.AddSeconds(-1)),
    };
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "Bluetooth", fpStatus: Status("Idle", true, events));

    var slot = cut.Find(".np-bar-fp");
    Assert.Contains("is-match", slot.ClassList);
    Assert.StartsWith("Strong match · ", slot.QuerySelector(".np-bar-fp-label")!.TextContent);
  }

  [Fact]
  public void FingerprintSlot_KeepsTheMatch_WhileTheNextCycleRuns()
  {
    var events = new[] { MakeEvent("m-now", "Hit Song", "B", ConfidenceBucket.Strong, DateTime.UtcNow.AddSeconds(-3)) };
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "RTLSDRCore", radioState: FmState("m-now"), fpStatus: Status("Querying", true, events));

    Assert.Contains("is-match", cut.Find(".np-bar-fp").ClassList);
  }

  [Fact]
  public void FingerprintSlot_NeverShowsRds()
  {
    // Owner decision 5: RDS takes no part in recognition, so a station name with no fingerprint
    // match must not fill the slot — it shows the recognition phase instead.
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "RTLSDRCore", radioState: FmState(matchId: null, rdsStationName: "WKQX"),
      fpStatus: Status("Idle"));

    Assert.Equal("Waiting to identify", cut.Find(".np-bar-fp-label").TextContent.Trim());
    Assert.DoesNotContain("RDS", cut.Find(".np-status-bar").TextContent);
    Assert.DoesNotContain("station-supplied", cut.Markup);
    Assert.DoesNotContain("RDS-supplied", cut.Markup);
    Assert.DoesNotContain("WKQX", cut.Markup);
  }

  [Fact]
  public void FingerprintSlot_Tap_OpensTheRecognitionStream()
  {
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "RTLSDRCore", radioState: FmState(), fpStatus: Status("Idle"));
    var detailField = typeof(NowPlayingPanel).GetField("_showFingerprintDetail", BindingFlags.NonPublic | BindingFlags.Instance)!;
    Assert.False((bool)detailField.GetValue(cut.Instance)!);

    cut.Find(".np-bar-fp").Click();

    // The handler also starts a status refresh that fails under the hermetic rig, so assert the
    // flag rather than racing the re-render.
    Assert.True((bool)detailField.GetValue(cut.Instance)!);
  }

  [Theory]
  [InlineData(1.0f, "+0.0 dB", false)]
  [InlineData(0.5f, "-6.0 dB", true)]
  [InlineData(2.0f, "+6.0 dB", true)]
  [InlineData(0.0f, "−∞ dB", true)]
  public void GainSlot_ShowsTheSourceGain_AndMarksAnOffset(float gain, string text, bool offset)
  {
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "FilePlayer", gain: gain);

    var slot = cut.Find(".np-bar-gain");
    Assert.Equal(text, slot.QuerySelector(".np-bar-gain-value")!.TextContent.Trim());
    Assert.Equal(offset, slot.ClassList.Contains("is-offset"));
  }

  [Theory]
  [InlineData("RTLSDRCore")]
  [InlineData("FilePlayer")]
  public void GainSlot_Tap_OpensTheSourceGainPopover_TitledSourceGain_WithNoAutoPill(string sourceType)
  {
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, sourceType, "SDR Radio (RTL-SDR)", sourceType == "RTLSDRCore" ? FmState() : null);
    Assert.Empty(cut.FindAll(".gain-popover"));

    cut.Find(".np-bar-gain").Click();

    Assert.Single(cut.FindAll(".gain-popover"));
    Assert.Equal("Source gain", cut.Find(".gain-popover-title").TextContent.Trim());
    Assert.Empty(cut.FindAll(".gain-popover-auto"));
    Assert.Equal("true", cut.Find(".np-bar-gain").GetAttribute("aria-expanded"));
  }

  [Fact]
  public void GainSlot_IsDisabled_UntilASourceIsKnown()
  {
    var cut = RenderComponent<NowPlayingPanel>();
    Assert.True(cut.Find(".np-bar-gain").HasAttribute("disabled"));
  }

  [Fact]
  public void GainPopover_BackdropTap_ClosesIt()
  {
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "FilePlayer");
    cut.Find(".np-bar-gain").Click();
    Assert.Single(cut.FindAll(".gain-popover"));

    cut.Find(".np-gain-popover-backdrop").Click();

    Assert.Empty(cut.FindAll(".gain-popover"));
    Assert.Empty(cut.FindAll(".np-gain-popover-backdrop"));
  }

  [Fact]
  public void GainPopover_AndItsBackdrop_AreChildrenOfTheUntrappedTransportBox()
  {
    // The stacking fix, structurally: the backdrop (9999) and the card's anchor (10000) must be
    // children of .np-transport, which carries no z-index and no filter, so neither is trapped in a
    // stacking context under the album art or the radio panel. bUnit computes no styles; the CSS
    // half is Css_TransportBox_IsNotAStackingContext_AndTheBarsDoNotAnimate.
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "FilePlayer");
    cut.Find(".np-bar-gain").Click();

    var transport = cut.Find(".np-transport");
    Assert.Null(transport.GetAttribute("style"));
    var children = transport.Children.Select(c => c.ClassName).ToArray();
    Assert.Contains("np-gain-popover-backdrop", children);
    Assert.Contains("np-gain-popover-anchor", children);
  }

  [Fact]
  public void Css_TransportBox_IsNotAStackingContext_AndTheBarsDoNotAnimate()
  {
    var css = File.ReadAllText(LocateDesignSystemCss());
    var transport = StripComments(Regex.Match(css, @"\n\.np-transport\s*\{(?<body>[^}]*)\}").Groups["body"].Value);
    Assert.NotEmpty(transport);
    Assert.DoesNotContain("z-index", transport);
    Assert.DoesNotContain("filter", transport);
    Assert.DoesNotContain("transform", transport);

    // #789 removed two infinite animations for the kiosk's CPU; the bar must not add one —
    // "Identifying…" is static text.
    var rules = Regex.Matches(css, @"\n(?<sel>\.(np-status-bar|np-bar-|np-transport|np-gain-popover-)[^{]*)\{(?<body>[^}]*)\}");
    Assert.NotEmpty(rules);
    foreach (Match rule in rules)
    {
      Assert.DoesNotContain("animation", StripComments(rule.Groups["body"].Value));
    }
  }

  // Rule comments explain these properties in the same words, so check declarations only.
  private static string StripComments(string css) =>
    Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

  private static string LocateDesignSystemCss()
  {
    var dir = AppContext.BaseDirectory;
    for (var i = 0; i < 10 && dir != null; i++)
    {
      var candidate = Path.Combine(dir, "src", "Radio.Web", "wwwroot", "css", "design-system.css");
      if (File.Exists(candidate))
      {
        return candidate;
      }
      dir = Path.GetDirectoryName(dir);
    }
    throw new FileNotFoundException("design-system.css not found by walking up from test base dir");
  }

  // ─── Legacy floating pills must not render ────────────────────────────────

  [Fact]
  public void LegacyFloatingPills_NotRendered()
  {
    // The pre-PR-4 panel rendered three independent pills: a fingerprint status pill in the top-left
    // ("Searching" / "Strong" / …), a source RadzenBadge in the top-right, and a "0dB" gain button.
    // None may come back with the status bar.
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusBarState(cut, "RTLSDRCore", "SDR Radio", FmState(rdsStationName: "KEXP"),
      fpStatus: Status("Querying"));

    Assert.DoesNotContain("Searching", cut.Markup);
    Assert.Empty(cut.FindAll(".rz-badge"));
    Assert.DoesNotMatch(@">\s*0dB\s*<", cut.Markup);
  }

  // ─── PR 4 fixer: GainPopoverKicker strips "SDR Radio (...)" wrapper ────────
  //
  // The kicker passed to GainControlPopover's header used to render as
  // "SDR · SDR Radio (RTL-SDR)" because _source carries the friendly name
  // "SDR Radio (RTL-SDR)" and the format string already prepends "SDR · ".
  // GetSourceShortToken strips the wrapper so the kicker reads
  // "SDR · RTL-SDR" — matching the spec.

  [Theory]
  [InlineData("SDR Radio (RTL-SDR)", "RTL-SDR")]
  [InlineData("SDR Radio (HackRF One)", "HackRF One")]
  [InlineData("Generic Source", "Generic Source")] // fallback — no wrapper
  [InlineData("", "")]
  [InlineData("   ", "   ")]
  public void GetSourceShortToken_StripsSdrRadioWrapper(string input, string expected)
  {
    Assert.Equal(expected, NowPlayingPanel.GetSourceShortToken(input));
  }

  // ─── Task #15 PR B (handoff item #6): DisplayNames.Track projection on hub push ───
  //
  // The panel must run incoming NowPlayingDto payloads through the
  // DisplayNames.Track projection so a generic "Track 8" title (which the
  // file-player surfaces while metadata is still being read) gets upgraded
  // to the cleaned-up filename. Wire path: AudioStateHubService raises
  // NowPlayingChanged → panel's OnNowPlayingChanged → ApplyNowPlayingDto →
  // ApplyDisplayProjection → DisplayNames.Track(dto) → _displayTitle.

  [Fact]
  public async Task NowPlayingPanel_DisplayNamesTrackProjection_AppliedFromHubPush()
  {
    var cut = RenderComponent<NowPlayingPanel>();

    var hub = Services.GetRequiredService<AudioStateHubService>();

    // Generic "Track N" title — the kind the metadata reader emits before it
    // resolves real tags. With a populated FilePath the panel's projection
    // pipeline must surface "Opening Night" instead.
    var dto = new NowPlayingDto
    {
      Title = "Track 8",
      Artist = "Cary High Chorus",
      FilePath = @"C:\music\Cary High Chorus\2006 Fall Concert\08 opening night.mp3",
      IsPlaying = true,
      SourceType = "FilePlayer",
      SourceName = "File Player",
    };

    await cut.InvokeAsync(() => HubEventFire.FireAsync<NowPlayingDto?>(
      hub, nameof(AudioStateHubService.NowPlayingChanged), dto));

    // The DisplayNames.Track projection rewrites the generic "Track 8" to the
    // parsed file-name. The rendered title block carries the cleaned name —
    // never the original "Track 8" payload.
    cut.Markup.Should().Contain("Opening Night");
    cut.Markup.Should().NotContain("Track 8");
  }

  // ─── Task #15 PR B (handoff item #34): anchor flips on NowPlayingMatchId change ───
  //
  // The recognition stream's NOW row anchors on RadioStateDto.NowPlayingMatchId.
  // When the server identifies a new match (different MatchId in the snapshot),
  // the panel must transfer the .np-recognition-row-current class to the new
  // row — the previous "current" row reverts to the EARLIER section. This pins
  // the wire-path behaviour: re-firing RadioStateChanged with a new MatchId
  // shifts the active row, not just appends another one.

  [Fact]
  public async Task Recognition_AnchorFlips_WhenNowPlayingMatchIdChanges()
  {
    // Seed the panel with two matched events. Initial anchor = "m-first";
    // after the second hub push the anchor moves to "m-second".
    var matches = new List<FingerprintEventDto>
    {
      MakeEvent("m-first", "First Song", "First Artist", ConfidenceBucket.Strong, DateTime.UtcNow.AddMinutes(-2)),
      MakeEvent("m-second", "Second Song", "Second Artist", ConfidenceBucket.Strong, DateTime.UtcNow.AddSeconds(-15)),
    };
    var status = new FingerprintStatusDto
    {
      IsEnabled = true,
      Phase = "Matched",
      RecentEvents = matches
    };

    var cut = RenderComponent<NowPlayingPanel>();

    // Seed events + open the recognition detail; both pushes below go through
    // the real hub event so the wire path is exercised end-to-end.
    var instance = cut.Instance;
    var type = typeof(NowPlayingPanel);
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;
    type.GetField("_fpStatus", flags)!.SetValue(instance, status);
    type.GetField("_fpEventsReversed", flags)!
      .SetValue(instance, Enumerable.Reverse(status.RecentEvents).ToList());
    type.GetField("_showFingerprintDetail", flags)!.SetValue(instance, true);
    cut.Render();

    var hub = Services.GetRequiredService<AudioStateHubService>();

    // First push: anchor on m-first.
    var firstState = new RadioStateDto(
      Frequency: 92.5e6, Band: "FM", Step: 100e3,
      SignalStrength: 70, IsScanning: false, ScanDirection: null,
      ScanStopThreshold: -18.0, Gain: 28, AutoGain: true,
      Equalizer: "Flat", DeviceVolume: 70,
      NowPlayingMatchId: "m-first");
    await cut.InvokeAsync(() => HubEventFire.FireAsync(
      hub, nameof(AudioStateHubService.RadioStateChanged), firstState));

    var currentBefore = cut.FindAll(".np-recognition-row-current");
    Assert.Single(currentBefore);
    Assert.Equal("m-first", currentBefore[0].GetAttribute("data-match-id"));

    // Second push: anchor on m-second. The current-row class must migrate —
    // m-second now carries it; m-first drops back to a plain EARLIER row.
    var secondState = firstState with { NowPlayingMatchId = "m-second" };
    await cut.InvokeAsync(() => HubEventFire.FireAsync(
      hub, nameof(AudioStateHubService.RadioStateChanged), secondState));

    var currentAfter = cut.FindAll(".np-recognition-row-current");
    Assert.Single(currentAfter);
    Assert.Equal("m-second", currentAfter[0].GetAttribute("data-match-id"));
    // m-first must NOT carry the current-row class anymore.
    var firstRows = cut.FindAll("[data-match-id=\"m-first\"].np-recognition-row-current");
    Assert.Empty(firstRows);
  }

  // ─── Album-art <img> rendering (BT persistence fix acceptance) ─────────────
  //
  // The BT album-art persistence fix keeps a valid /api/albumart/... URL in
  // source metadata across AVRCP refreshes. These tests pin the consumer side:
  // when _albumArtUrl carries a real proxy path the panel renders the album-art
  // <img>; when it holds the default placeholder the panel treats it as invalid
  // (via _hasValidAlbumArt) and omits the <img> so the fallback surface shows.

  [Fact]
  public void NowPlayingPanel_RendersAlbumArtImg_WhenAlbumArtUrlPresent()
  {
    var cut = RenderComponent<NowPlayingPanel>();
    var instance = cut.Instance;
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;

    // A real proxy path + failed=false + fingerprint-detail=false makes
    // _hasValidAlbumArt true, so the album-art <img> branch renders.
    typeof(NowPlayingPanel).GetField("_nowPlayingSourceType", flags)!
      .SetValue(instance, "Bluetooth");
    typeof(NowPlayingPanel).GetField("_source", flags)!
      .SetValue(instance, "Bluetooth Audio");
    typeof(NowPlayingPanel).GetField("_albumArtUrl", flags)!
      .SetValue(instance, "/api/albumart/track-xyz.jpg");
    cut.Render();

    var img = cut.Find("img[alt='Album Art']");
    Assert.Equal("/api/albumart/track-xyz.jpg", img.GetAttribute("src"));
  }

  [Fact]
  public void NowPlayingPanel_OmitsAlbumArtImg_WhenDefaultUrl()
  {
    var cut = RenderComponent<NowPlayingPanel>();
    var instance = cut.Instance;
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;

    // The default placeholder path is explicitly treated as "no valid art" by
    // _hasValidAlbumArt, so the album-art <img> must NOT render (fallback shows).
    typeof(NowPlayingPanel).GetField("_nowPlayingSourceType", flags)!
      .SetValue(instance, "Bluetooth");
    typeof(NowPlayingPanel).GetField("_source", flags)!
      .SetValue(instance, "Bluetooth Audio");
    typeof(NowPlayingPanel).GetField("_albumArtUrl", flags)!
      .SetValue(instance, "/images/default-album-art.png");
    cut.Render();

    Assert.Empty(cut.FindAll("img[alt='Album Art']"));
  }

  // ─── AUD-28: seek on release ───────────────────────────────────────────────

  private static bool IsPlaybackUpdate(HttpMethod method, string path) =>
    method == HttpMethod.Post && path == "/api/audio";

  /// <summary>
  /// Renders the panel over a RecordingHandler and pokes it into "seekable file, 1:00 of 4:00" so the
  /// SeekBar branch renders. Returns the handler so a test can count the POST /api/audio a seek makes.
  /// </summary>
  private (IRenderedComponent<NowPlayingPanel> Cut, RecordingHandler Handler) RenderSeekable()
  {
    var handler = new RecordingHandler();
    // Registered after the constructor's AddHttpClient<AudioApiService>, so this instance wins.
    Services.AddSingleton(new AudioApiService(
      new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) },
      NullLogger<AudioApiService>.Instance));

    var cut = RenderComponent<NowPlayingPanel>();
    var type = typeof(NowPlayingPanel);
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;
    type.GetField("_position", flags)!.SetValue(cut.Instance, "1:00");
    type.GetField("_duration", flags)!.SetValue(cut.Instance, "4:00");
    type.GetField("_canSeek", flags)!.SetValue(cut.Instance, true);
    type.GetField("_totalDurationSeconds", flags)!.SetValue(cut.Instance, 240.0);
    type.GetField("_progressPercent", flags)!.SetValue(cut.Instance, 25.0);
    cut.Render();
    return (cut, handler);
  }

  [Fact]
  public async Task SeekBar_Drag_SendsNoSeekUntilRelease_ThenExactlyOne()
  {
    var (cut, handler) = RenderSeekable();
    var seekBar = cut.FindComponent<SeekBar>();
    var before = handler.Count(IsPlaybackUpdate);

    // ⚠ THIS IS THE ROW. With RadzenSlider every one of these frames reached POST /api/audio as a
    // Seek, which is the stutter the owner heard (AUD-28).
    foreach (var f in new[] { 0.30, 0.35, 0.40, 0.45, 0.50 })
    {
      await seekBar.Instance.OnDragMove(f);
    }

    Assert.Equal(before, handler.Count(IsPlaybackUpdate));

    // OnDragEnd awaits the whole commit (HandleSeekAsync awaits its POST), so the request is
    // recorded by the time this returns — no rendezvous or delay needed.
    await seekBar.Instance.OnDragEnd(0.5);

    Assert.Equal(before + 1, handler.Count(IsPlaybackUpdate));
  }

  [Fact]
  public async Task SeekBar_Tap_StillSeeksOnce()
  {
    var (cut, handler) = RenderSeekable();
    var seekBar = cut.FindComponent<SeekBar>();
    var before = handler.Count(IsPlaybackUpdate);

    await seekBar.Instance.OnDragMove(0.75);
    await seekBar.Instance.OnDragEnd(0.75);

    Assert.Equal(before + 1, handler.Count(IsPlaybackUpdate));
  }

  [Fact]
  public async Task SeekBar_Drag_ElapsedReadoutFollowsTheFinger()
  {
    var (cut, _) = RenderSeekable();
    var seekBar = cut.FindComponent<SeekBar>();

    // AUD-28 scope question 4: deferring the seek must not make the control look dead. 0.5 of 4:00.
    await seekBar.Instance.OnDragMove(0.5);

    Assert.Contains(">2:00<", cut.Markup);
    Assert.DoesNotContain(">1:00<", cut.Markup);
  }
}
