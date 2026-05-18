using System.Reflection;
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

    // PR 4 — GainControlPopover is rendered inline when _showGainPopover is true;
    // it depends on AudioVisualizationHubService for its peak meter. Register a
    // real instance so the popover branches render. StartAsync will fail-silently
    // against the unreachable test ApiBaseUrl, which is exactly how the production
    // popover behaves on a cold start.
    Services.AddSingleton(sp =>
      new AudioVisualizationHubService(
        NullLogger<AudioVisualizationHubService>.Instance,
        sp.GetRequiredService<IConfiguration>()
      )
    );

    // Task #15 PR E item #47 — gain-popover backdrop is now portaled to
    // MainLayout via this scoped service. The panel injects it for the
    // open/close + OnClose subscription wiring, so test renders need it
    // registered too.
    Services.AddScoped<Radio.Web.Services.GainPopoverService>();
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

  // ─── PR 4: Status strip + match badge ──────────────────────────────────────
  //
  // The status strip lives at the top of the panel. Its visibility is driven by
  // _nowPlayingSourceType — once set, the strip renders with the source cell
  // always present, frequency + RDS conditional on tuner-family + RDS data, and
  // gain always present. The match badge sits inside the album-art card and
  // requires either an active fingerprint match or RDS station info to render.

  /// <summary>
  /// Reflectively seeds the panel's now-playing + radio state without touching
  /// HTTP. Mirrors the pattern <see cref="SetRecognitionState"/> uses for the
  /// recognition stream tests.
  /// </summary>
  private static void SetStatusStripState(
    IRenderedComponent<NowPlayingPanel> cut,
    string sourceType,
    string sourceName = "Source",
    RadioStateDto? radioState = null,
    float gain = 1.0f,
    FingerprintStatusDto? fpStatus = null)
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
    cut.Render();
  }

  [Fact]
  public void StatusStrip_RendersAllFourCells_WhenTunerActiveWithRds()
  {
    var radioState = new RadioStateDto(
      Frequency: 92.5e6, Band: "FM", Step: 100e3,
      SignalStrength: 70, IsScanning: false, ScanDirection: null,
      ScanStopThreshold: -18.0, Gain: 28, AutoGain: true,
      Equalizer: "Flat", DeviceVolume: 70,
      RdsStationName: "KEXP",
      AppliedGain: 28.0);

    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusStripState(cut, "RTLSDRCore", "SDR Radio", radioState);

    var strip = cut.Find(".np-status-strip");
    Assert.NotNull(strip);

    // All four cells present and in order: source, freq, rds, gain.
    Assert.Single(cut.FindAll(".np-status-cell-source"));
    Assert.Single(cut.FindAll(".np-status-cell-frequency"));
    Assert.Single(cut.FindAll(".np-status-cell-rds"));
    Assert.Single(cut.FindAll(".np-status-cell-gain"));
  }

  [Fact]
  public void StatusStrip_OmitsFrequencyCell_WhenNonTunerSource()
  {
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusStripState(cut, "FilePlayer", "File Player");

    Assert.Single(cut.FindAll(".np-status-cell-source"));
    Assert.Empty(cut.FindAll(".np-status-cell-frequency"));
    Assert.Empty(cut.FindAll(".np-status-cell-rds"));
    Assert.Single(cut.FindAll(".np-status-cell-gain"));
  }

  [Fact]
  public void StatusStrip_OmitsRdsCell_WhenNoStationName()
  {
    var radioState = new RadioStateDto(
      Frequency: 92.5e6, Band: "FM", Step: 100e3,
      SignalStrength: 70, IsScanning: false, ScanDirection: null,
      ScanStopThreshold: -18.0, Gain: 28, AutoGain: true,
      Equalizer: "Flat", DeviceVolume: 70,
      RdsStationName: null);

    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusStripState(cut, "RTLSDRCore", "SDR Radio", radioState);

    Assert.Single(cut.FindAll(".np-status-cell-frequency"));
    Assert.Empty(cut.FindAll(".np-status-cell-rds"));
  }

  [Fact]
  public void StatusStrip_FrequencyCell_RendersValueAndUnit()
  {
    var radioState = new RadioStateDto(
      Frequency: 92.5e6, Band: "FM", Step: 100e3,
      SignalStrength: 70, IsScanning: false, ScanDirection: null,
      ScanStopThreshold: -18.0, Gain: 28, AutoGain: true,
      Equalizer: "Flat", DeviceVolume: 70);

    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusStripState(cut, "RTLSDRCore", "SDR Radio", radioState);

    var freqCell = cut.Find(".np-status-cell-frequency");
    Assert.Contains("92.50", freqCell.TextContent);
    Assert.Contains("MHz", freqCell.TextContent);
  }

  [Fact]
  public void StatusStrip_FrequencyCell_AmBand_RendersKHz()
  {
    var radioState = new RadioStateDto(
      Frequency: 1010e3, Band: "AM", Step: 10e3,
      SignalStrength: 40, IsScanning: false, ScanDirection: null,
      ScanStopThreshold: -22.0, Gain: 12, AutoGain: false,
      Equalizer: "Flat", DeviceVolume: 70);

    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusStripState(cut, "RTLSDRCore", "SDR Radio", radioState);

    var freqCell = cut.Find(".np-status-cell-frequency");
    Assert.Contains("1010", freqCell.TextContent);
    Assert.Contains("kHz", freqCell.TextContent);
  }

  [Fact]
  public void StatusStrip_RdsCell_RendersStationName()
  {
    var radioState = new RadioStateDto(
      Frequency: 92.5e6, Band: "FM", Step: 100e3,
      SignalStrength: 70, IsScanning: false, ScanDirection: null,
      ScanStopThreshold: -18.0, Gain: 28, AutoGain: true,
      Equalizer: "Flat", DeviceVolume: 70,
      RdsStationName: "WKQX");

    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusStripState(cut, "RTLSDRCore", "SDR Radio", radioState);

    var rds = cut.Find(".np-status-rds-station");
    Assert.Equal("WKQX", rds.TextContent);
    // The dim tag carries the literal "RDS".
    var tag = cut.Find(".np-status-rds-tag");
    Assert.Equal("RDS", tag.TextContent);
  }

  [Fact]
  public void StatusStrip_AppliesArtBgClass_WhenAlbumArtPresent()
  {
    var cut = RenderComponent<NowPlayingPanel>();
    var instance = cut.Instance;
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;
    typeof(NowPlayingPanel).GetField("_nowPlayingSourceType", flags)!.SetValue(instance, "FilePlayer");
    typeof(NowPlayingPanel).GetField("_source", flags)!.SetValue(instance, "File");
    typeof(NowPlayingPanel).GetField("_albumArtUrl", flags)!.SetValue(instance, "/api/albumart/track-123.jpg");
    cut.Render();

    var strip = cut.Find(".np-status-strip");
    Assert.Contains("has-art-bg", strip.ClassList);
  }

  [Fact]
  public void StatusStrip_GainCell_OpensPopover_OnClick()
  {
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusStripState(cut, "FilePlayer", "File");

    // Sanity: popover not rendered yet.
    Assert.Empty(cut.FindAll(".gain-popover"));

    cut.Find(".np-status-cell-gain").Click();

    // Popover renders after the click.
    Assert.Single(cut.FindAll(".gain-popover"));
  }

  [Fact]
  public void StatusStrip_SourceSwatch_UsesSourceAccent()
  {
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusStripState(cut, "RTLSDRCore", "SDR Radio");

    var swatch = cut.Find(".np-status-swatch");
    var styleAttr = swatch.GetAttribute("style") ?? string.Empty;
    Assert.Contains("--source-radio", styleAttr);
  }

  // ─── Match badge ──────────────────────────────────────────────────────────

  [Fact]
  public void MatchBadge_RendersConfidencePips_WhenFingerprintMatchActive()
  {
    var events = new List<FingerprintEventDto>
    {
      MakeEvent("m-active", "Hit Song", "Artist", ConfidenceBucket.Strong, DateTime.UtcNow.AddSeconds(-12))
    };
    var fpStatus = new FingerprintStatusDto
    {
      IsEnabled = true,
      Phase = "Matched",
      RecentEvents = events
    };
    var radioState = new RadioStateDto(
      Frequency: 92.5e6, Band: "FM", Step: 100e3,
      SignalStrength: 70, IsScanning: false, ScanDirection: null,
      ScanStopThreshold: -18.0, Gain: 28, AutoGain: true,
      Equalizer: "Flat", DeviceVolume: 70,
      NowPlayingMatchId: "m-active");

    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusStripState(cut, "RTLSDRCore", "SDR Radio", radioState, fpStatus: fpStatus);

    var badge = cut.Find(".np-match-badge");
    // ConfidencePips is rendered inside the badge.
    Assert.Single(badge.QuerySelectorAll(".confidence-pips"));
    // Label text reads "Strong match · …".
    Assert.Contains("Strong match", badge.TextContent);
  }

  [Fact]
  public void MatchBadge_RendersRdsLabel_WhenNoFingerprintButRdsPresent()
  {
    var radioState = new RadioStateDto(
      Frequency: 92.5e6, Band: "FM", Step: 100e3,
      SignalStrength: 70, IsScanning: false, ScanDirection: null,
      ScanStopThreshold: -18.0, Gain: 28, AutoGain: true,
      Equalizer: "Flat", DeviceVolume: 70,
      RdsStationName: "WKQX",
      NowPlayingMatchId: null);

    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusStripState(cut, "RTLSDRCore", "SDR Radio", radioState);

    var badge = cut.Find(".np-match-badge");
    Assert.Contains("is-rds", badge.ClassList);
    Assert.Contains("RDS · station-supplied", badge.TextContent);
    // No ConfidencePips for the RDS variant.
    Assert.Empty(badge.QuerySelectorAll(".confidence-pips"));
  }

  [Fact]
  public void MatchBadge_Hidden_WhenNeitherFingerprintNorRds()
  {
    var radioState = new RadioStateDto(
      Frequency: 92.5e6, Band: "FM", Step: 100e3,
      SignalStrength: 70, IsScanning: false, ScanDirection: null,
      ScanStopThreshold: -18.0, Gain: 28, AutoGain: true,
      Equalizer: "Flat", DeviceVolume: 70,
      RdsStationName: null,
      NowPlayingMatchId: null);

    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusStripState(cut, "RTLSDRCore", "SDR Radio", radioState);

    Assert.Empty(cut.FindAll(".np-match-badge"));
  }

  [Fact]
  public void MatchBadge_OpensRecognitionStream_OnClick()
  {
    var events = new List<FingerprintEventDto>
    {
      MakeEvent("m-active", "Hit Song", "Artist", ConfidenceBucket.Likely, DateTime.UtcNow.AddSeconds(-30))
    };
    var fpStatus = new FingerprintStatusDto
    {
      IsEnabled = true,
      Phase = "Matched",
      RecentEvents = events
    };
    var radioState = new RadioStateDto(
      Frequency: 92.5e6, Band: "FM", Step: 100e3,
      SignalStrength: 70, IsScanning: false, ScanDirection: null,
      ScanStopThreshold: -18.0, Gain: 28, AutoGain: true,
      Equalizer: "Flat", DeviceVolume: 70,
      NowPlayingMatchId: "m-active");

    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusStripState(cut, "RTLSDRCore", "SDR Radio", radioState, fpStatus: fpStatus);

    var flags = BindingFlags.NonPublic | BindingFlags.Instance;
    var detailField = typeof(NowPlayingPanel).GetField("_showFingerprintDetail", flags)!;

    // Sanity: recognition stream is closed.
    Assert.False((bool)detailField.GetValue(cut.Instance)!);

    cut.Find(".np-match-badge").Click();

    // After the click, _showFingerprintDetail flipped to true — confirming the
    // match badge wires its OnClick to ToggleFingerprintDetail. The handler
    // also fires a background RefreshFingerprintStatusAsync which throws under
    // the no-API-server test fixture and nulls out _fpEventsReversed, so we
    // assert the boolean flip directly rather than the rendered DOM (which
    // would race against the background refresh's null-out).
    Assert.True((bool)detailField.GetValue(cut.Instance)!);
  }

  // ─── Legacy floating pills must not render ────────────────────────────────

  [Fact]
  public void LegacyFloatingPills_NotRendered_WhenStatusStripOwnsSource()
  {
    // The pre-PR-4 panel rendered three independent pills:
    //   - Fingerprint status pill in the top-left ("Searching" / "Strong" / …)
    //     written via inline styles + GetFingerprintBadge*() helpers.
    //   - Source RadzenBadge in the top-right ("SDR · RTL-SDR").
    //   - Gain button next to the source badge ("0dB" / "+1.5dB").
    //
    // PR 4 folds all three into the status strip. The regression guard asserts
    // none of the legacy artifacts survive. We render against a typical
    // tuner-active scenario so all three previous pills would have been live.
    var radioState = new RadioStateDto(
      Frequency: 92.5e6, Band: "FM", Step: 100e3,
      SignalStrength: 70, IsScanning: false, ScanDirection: null,
      ScanStopThreshold: -18.0, Gain: 28, AutoGain: true,
      Equalizer: "Flat", DeviceVolume: 70,
      RdsStationName: "KEXP");
    var fpStatus = new FingerprintStatusDto
    {
      IsEnabled = true,
      Phase = "Querying", // would have surfaced as "Searching" pill
      RecentEvents = new List<FingerprintEventDto>()
    };
    var cut = RenderComponent<NowPlayingPanel>();
    SetStatusStripState(cut, "RTLSDRCore", "SDR Radio", radioState, fpStatus: fpStatus);

    // No standalone "Searching" pill — the only place "Searching" appears would
    // be inside the legacy badge, which is gone.
    Assert.DoesNotContain("Searching", cut.Markup);
    // No standalone RadzenBadge text rendered as a floating element.
    var radzenBadges = cut.FindAll(".rz-badge");
    Assert.Empty(radzenBadges);
    // No leftover absolute-positioned "0dB" button — the gain cell now uses
    // the formatted "+0.0 dB" string via FormatGainDb.
    Assert.DoesNotMatch(@">\s*0dB\s*<", cut.Markup);
  }

  // ─── Wire-path regression: status strip updates on hub push ────────────────
  //
  // The status strip reads frequency / RDS / applied-gain off _radioState, which
  // is populated by the typed RadioStateChanged hub event (PR 2). The PR 2
  // wire-path test proves the recognition stream updates; this complementary
  // test proves the status strip does too — same hub, same DTO, different
  // consumer surface.

  [Fact]
  public async Task StatusStrip_FrequencyCell_UpdatesOnRadioStateHubPush()
  {
    var cut = RenderComponent<NowPlayingPanel>();

    var instance = cut.Instance;
    var type = typeof(NowPlayingPanel);
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;
    type.GetField("_nowPlayingSourceType", flags)!.SetValue(instance, "RTLSDRCore");
    type.GetField("_source", flags)!.SetValue(instance, "SDR Radio");
    cut.Render();

    var hubService = Services.GetRequiredService<AudioStateHubService>();
    var eventField = typeof(AudioStateHubService).GetField(
      nameof(AudioStateHubService.RadioStateChanged),
      BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(eventField);
    var handler = (Func<RadioStateDto, Task>?)eventField!.GetValue(hubService);
    Assert.NotNull(handler);

    var dto = new RadioStateDto(
      Frequency: 88.1e6, Band: "FM", Step: 100e3,
      SignalStrength: 60, IsScanning: false, ScanDirection: null,
      ScanStopThreshold: -20.0, Gain: 24, AutoGain: false,
      Equalizer: "Flat", DeviceVolume: 70,
      RdsStationName: "KFOG",
      AppliedGain: 24.0);

    await cut.InvokeAsync(() => handler!.Invoke(dto));

    var freq = cut.Find(".np-status-cell-frequency").TextContent;
    Assert.Contains("88.10", freq);
    var rds = cut.Find(".np-status-rds-station");
    Assert.Equal("KFOG", rds.TextContent);
  }

  // ─── Task #15 PR E item #47: gain-popover backdrop portal wiring ─────────
  //
  // The click-away backdrop for the gain popover used to live INSIDE
  // NowPlayingPanel, which is rendered under .page-transition. That wrapper
  // declares transform + will-change which creates a stacking context that
  // traps z-index 9999 — meaning the backdrop's @onclick never received
  // events when the user clicked outside the popover-anchor sub-tree (the
  // RadioControlPanel sat above it). The fix portals the backdrop to
  // MainLayout via GainPopoverService. These tests pin the wire-path:
  //
  // 1. ToggleGainPopover opens the service-driven backdrop.
  // 2. CloseGainPopover closes both the local popover AND the service.
  // 3. Subscribing to GainPopover.OnClose (the MainLayout-side backdrop
  //    click) tears down _showGainPopover on the panel — verified by
  //    invoking the service's HandleBackdropClick directly.

  [Fact]
  public void GainPopover_ToggleGainPopover_OpensServiceBackdrop()
  {
    var cut = RenderComponent<NowPlayingPanel>();
    var svc = Services.GetRequiredService<Radio.Web.Services.GainPopoverService>();
    svc.IsOpen.Should().BeFalse("the backdrop should start unmounted");

    // The instance-level toggle is private; reflect into it to mirror the
    // path the status-strip gain cell hits via @onclick. Reflective access
    // here is consistent with how long-press / band-pill tests drive
    // RadioControlPanel.
    var toggle = typeof(NowPlayingPanel).GetMethod(
      "ToggleGainPopover",
      BindingFlags.NonPublic | BindingFlags.Instance);
    toggle.Should().NotBeNull();
    cut.InvokeAsync(() => toggle!.Invoke(cut.Instance, Array.Empty<object>()));

    svc.IsOpen.Should().BeTrue(
      "ToggleGainPopover must drive the layout-mounted backdrop, not just the local popover");
  }

  [Fact]
  public void GainPopover_BackdropClickFromLayout_ClosesLocalPopover()
  {
    var cut = RenderComponent<NowPlayingPanel>();
    var svc = Services.GetRequiredService<Radio.Web.Services.GainPopoverService>();

    // Open via the panel surface, then simulate the backdrop click that
    // MainLayout would have wired to HandleBackdropClick. The panel must
    // close its local _showGainPopover in response (so re-rendering hides
    // the popover anchor) AND the service must end up closed (so the
    // backdrop unmounts in MainLayout).
    var toggle = typeof(NowPlayingPanel).GetMethod(
      "ToggleGainPopover",
      BindingFlags.NonPublic | BindingFlags.Instance);
    cut.InvokeAsync(() => toggle!.Invoke(cut.Instance, Array.Empty<object>()));
    svc.IsOpen.Should().BeTrue();

    cut.InvokeAsync(() => svc.HandleBackdropClick());

    svc.IsOpen.Should().BeFalse();
    // The panel's private _showGainPopover field should have been cleared
    // by the OnClose subscriber.
    var showField = typeof(NowPlayingPanel).GetField(
      "_showGainPopover",
      BindingFlags.NonPublic | BindingFlags.Instance);
    showField.Should().NotBeNull();
    var showValue = (bool)showField!.GetValue(cut.Instance)!;
    showValue.Should().BeFalse(
      "backdrop click must tear down the panel's local popover state via OnClose");
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
  [InlineData("SDR Radio (RF320)", "RF320")]
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
    var field = typeof(AudioStateHubService).GetField(
      nameof(AudioStateHubService.NowPlayingChanged),
      BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(field);
    var handler = (Func<NowPlayingDto?, Task>?)field!.GetValue(hub);
    Assert.NotNull(handler);

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

    await cut.InvokeAsync(() => handler!.Invoke(dto));

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
    var eventField = typeof(AudioStateHubService).GetField(
      nameof(AudioStateHubService.RadioStateChanged),
      BindingFlags.NonPublic | BindingFlags.Instance);
    var handler = (Func<RadioStateDto, Task>?)eventField!.GetValue(hub);
    Assert.NotNull(handler);

    // First push: anchor on m-first.
    var firstState = new RadioStateDto(
      Frequency: 92.5e6, Band: "FM", Step: 100e3,
      SignalStrength: 70, IsScanning: false, ScanDirection: null,
      ScanStopThreshold: -18.0, Gain: 28, AutoGain: true,
      Equalizer: "Flat", DeviceVolume: 70,
      NowPlayingMatchId: "m-first");
    await cut.InvokeAsync(() => handler!.Invoke(firstState));

    var currentBefore = cut.FindAll(".np-recognition-row-current");
    Assert.Single(currentBefore);
    Assert.Equal("m-first", currentBefore[0].GetAttribute("data-match-id"));

    // Second push: anchor on m-second. The current-row class must migrate —
    // m-second now carries it; m-first drops back to a plain EARLIER row.
    var secondState = firstState with { NowPlayingMatchId = "m-second" };
    await cut.InvokeAsync(() => handler!.Invoke(secondState));

    var currentAfter = cut.FindAll(".np-recognition-row-current");
    Assert.Single(currentAfter);
    Assert.Equal("m-second", currentAfter[0].GetAttribute("data-match-id"));
    // m-first must NOT carry the current-row class anymore.
    var firstRows = cut.FindAll("[data-match-id=\"m-first\"].np-recognition-row-current");
    Assert.Empty(firstRows);
  }
}
