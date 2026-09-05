using System.Net;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;
using Xunit;

namespace Radio.Web.Tests.Components;

/// <summary>
/// VoicemailPlayer after ADR-029 PR 6 — a remote control for something happening in the room, not a
/// media player.
/// </summary>
/// <remarks>
/// ⚠ Every state below is driven by a BROADCAST, through AudioStateStore.OnHubEventPlaybackChanged,
/// after the component has been given a playback id by tapping play. That is the real path: the 202
/// answers before any audio exists, so the component holds only an id and renders whatever the hub
/// says about it.
///
/// ⚠ NOTHING HERE PRODUCES SOUND, and no assertion in this file may be cited as evidence that
/// voicemail is audible, ducks the radio, follows mute or reaches a Cast device. Those are the four
/// checks Feature A exists to satisfy and they are reachable only by a person in the room
/// (plan §2.2, §3 U1–U4).
/// </remarks>
public class VoicemailPlayerTests : TestContext
{
  private const string PlaybackId = "evp-1";

  private AudioStateStore _store = default!;

  private VoicemailItemDto Vm(int duration = 42, string? transcript = "hi",
    DateTime? received = null) =>
    new("vm1", "t1", "+15551234567", "Jane",
      received ?? DateTime.UtcNow, duration, false, transcript,
      "/api/gvbridge/voicemail/vm1/audio");

  private void Register()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;  // Radzen/JS-interop components
    Services.AddRadzenComponents();
    Services.AddHermeticTestRig();

    // The store the component reads through ConsolePlaybackState, built over OfflineHubTransport and
    // never started — the same shape as AudioStateStoreEventPlaybackTests. Held in a field so a test
    // can drive a broadcast.
    _store = new AudioStateStore(
      NullLogger<AudioStateStore>.Instance,
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        new ConfigurationBuilder().Build(),
        transport: new OfflineHubTransport()));
    Services.AddSingleton(_store);
    Services.AddSingleton(sp => new ConsolePlaybackState(
      sp.GetRequiredService<AudioStateStore>(), NullLogger<ConsolePlaybackState>.Instance));

    Services.AddSingleton(new EventPlaybackApiService(
      new HttpClient(new EventsApiHandler()) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) },
      NullLogger<EventPlaybackApiService>.Instance));
    Services.AddSingleton(new AudioApiService(
      new HttpClient(new MockHttpHandler("{}")) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) },
      NullLogger<AudioApiService>.Instance));
  }

  private static EventPlaybackSnapshotDto Snapshot(
    string state,
    string id = PlaybackId,
    TimeSpan? duration = null,
    string? failureReason = null,
    int broadcastSecondsAgo = 0) =>
    new(id, "RemoteMedia", "Voicemail from Jane", state,
      duration ?? TimeSpan.FromSeconds(42), TimeSpan.Zero,
      DateTimeOffset.UtcNow.AddSeconds(-broadcastSecondsAgo), failureReason);

  private IRenderedComponent<VoicemailPlayer> RenderPlayer(VoicemailItemDto? item = null)
  {
    Register();
    return RenderComponent<VoicemailPlayer>(p => p.Add(x => x.Item, item ?? Vm()));
  }

  /// <summary>Taps play, which is what gives the component the playback id every state is gated on.</summary>
  private static void TapPlay(IRenderedComponent<VoicemailPlayer> cut) =>
    cut.Find("button.transport-btn-primary").Click();

  private Task BroadcastAsync(IRenderedComponent<VoicemailPlayer> cut, EventPlaybackSnapshotDto snapshot) =>
    cut.InvokeAsync(() => _store.OnHubEventPlaybackChanged(snapshot));

  // ── the row ──────────────────────────────────────────────────────────────────────────────────

  [Fact]
  public void Renders_NoAudioElement_BecauseTheConsolePlaysItNow()
  {
    var cut = RenderPlayer();

    // ⚠ THIS IS THE ROW. An <audio> element here is a second audio path that bypasses mute, master
    // volume, balance, ducking and Cast routing (owner decision D17). This test REPLACES
    // Renders_AbsoluteAudioSrc, which asserted the opposite and was correct until this PR.
    Assert.Empty(cut.FindAll("audio"));
  }

  [Fact]
  public void Idle_DisablesSkipAndStop_AndOffersPlay()
  {
    var cut = RenderPlayer();

    Assert.True(cut.Find("button[aria-label='Back 15 seconds']").HasAttribute("disabled"));
    Assert.True(cut.Find("button[aria-label='Forward 15 seconds']").HasAttribute("disabled"));
    Assert.True(cut.Find("button[aria-label='Stop playing']").HasAttribute("disabled"));
    Assert.Contains("play_arrow", cut.Markup);
  }

  [Fact]
  public async Task Preparing_ShowsTheSpinnerAndTheFetchingNote()
  {
    var cut = RenderPlayer();
    TapPlay(cut);

    await BroadcastAsync(cut, Snapshot("Preparing"));

    Assert.Single(cut.FindAll(".spinner"));
    Assert.Contains("Fetching recording", cut.Markup);
    Assert.DoesNotContain("play_arrow", cut.Markup);
  }

  [Fact]
  public async Task Waiting_SaysWhy_AndDoesNotRunTheProgressBar()
  {
    var cut = RenderPlayer();
    TapPlay(cut);

    // ⚠ The anchor is 20 SECONDS OLD on purpose. With a fresh anchor the interpolated width would
    // round to 0% either way and the test could not tell a guarded ElapsedSeconds from an unguarded
    // one. At 20s of a 42s recording an unguarded one reads ~47.62%.
    await BroadcastAsync(cut, Snapshot("Waiting", broadcastSecondsAgo: 20));

    Assert.Contains("Waiting for the announcement to finish", cut.Markup);
    Assert.Contains("width:0%", cut.Markup);
  }

  [Fact]
  public async Task Waiting_StillOffersStop()
  {
    var cut = RenderPlayer();
    TapPlay(cut);

    // PHN-1f §0.6 item 1: Waiting is LIVE and STOPPABLE. A queued playback the user cannot cancel is
    // the complaint D28's wait-then-play queue was accepted on condition of avoiding.
    await BroadcastAsync(cut, Snapshot("Waiting"));

    Assert.False(cut.Find("button[aria-label='Stop playing']").HasAttribute("disabled"));
  }

  [Fact]
  public void UnknownDuration_RendersIndeterminate_AndNeverZeroZero()
  {
    var cut = RenderPlayer(Vm(duration: 0));

    // ⚠ DurationSeconds == 0 means UNKNOWN in the GV contract, not "zero seconds". The scrubber
    // downgrades its ROLE rather than leaving an unchangeable slider, which would be an
    // accessibility lie in exactly that state.
    Assert.Single(cut.FindAll(".vm-scrubber-indeterminate"));
    Assert.Contains("progressbar", cut.Markup);
    Assert.Contains("Unknown length", cut.Markup);

    // ⚠ The TOTAL is what must never be 0:00, and the assertion says so rather than banning the
    // string outright: the readout is "elapsed / total", and an elapsed of 0:00 at position zero is
    // correct. The plan asked for a bare DoesNotContain("0:00"); that is unsatisfiable here and was
    // borrowed from VoicemailRowTests, which renders a duration ALONE. Falsifying mutation: drop the
    // DurationKnown conditional from TotalDisplay so it formats the zero — "/ 0:00" appears, reds.
    Assert.Contains("/ --:--", cut.Markup);
    Assert.DoesNotContain("/ 0:00", cut.Markup);
  }

  [Fact]
  public async Task ASnapshotForADifferentPlaybackDoesNotDriveThisRow()
  {
    var cut = RenderPlayer();
    TapPlay(cut);

    // Handoff §A4b makes every play button on /phone one single-selection group over GLOBAL state,
    // so the id gate is the only thing stopping every open row from rendering somebody else's
    // playback — including one started from a different row or a different browser.
    await BroadcastAsync(cut, Snapshot("Playing", id: "evp-SOMEBODY-ELSE"));

    Assert.Contains("play_arrow", cut.Markup);
    Assert.True(cut.Find("button[aria-label='Stop playing']").HasAttribute("disabled"));
  }

  [Fact]
  public async Task MediaUnauthorized_DoesNotSayItWillClearUp()
  {
    var cut = RenderPlayer();
    TapPlay(cut);

    await BroadcastAsync(cut, Snapshot("Failed", failureReason: "MediaUnauthorized"));

    Assert.Contains("allowed to fetch recordings", cut.Markup);
    // ⚠ It never clears up: it means GvMedia:AuthKey and RotaryPhone's InterServiceAuthKey have
    // diverged, which is two hand edits on two files the deploy does not re-seed. The blackout's
    // copy would send the owner into a retry loop against a configuration fault.
    Assert.DoesNotContain("clears up", cut.Markup);
  }

  [Fact]
  public async Task MediaNotFound_SaysItUsuallyClearsUp()
  {
    var cut = RenderPlayer();
    TapPlay(cut);

    // ⚠ MediaNotFound does NOT mean the recording is gone. Six transient conditions reach
    // RotaryPhone's 404, the commonest being the Google Voice auth blackout — dead ~9 minutes in
    // every 20 — so "this usually clears up in a minute" is an accurate promise, not a soothing one.
    await BroadcastAsync(cut, Snapshot("Failed", failureReason: "MediaNotFound"));

    Assert.Contains("This usually clears up in a minute.", cut.Markup);
  }

  /// <summary>
  /// Primes the store's mute/volume the way production does: one hub broadcast.
  /// </summary>
  /// <remarks>
  /// ⚠ NOT AudioStateStore.UpdatePlaybackStateAsync, which is what this test used until PHN-2's
  /// review. That method is the ONLY writer of AudioStateStore.PlaybackState and it has no production
  /// caller at all, so the component read a property that is null forever on the box and the pill
  /// could never appear — a green test over an affordance that did not exist. OnHubVolumeChanged is
  /// internal for exactly this seam, the same way OnHubEventPlaybackChanged is.
  /// </remarks>
  private Task PrimeVolumeAsync(IRenderedComponent<VoicemailPlayer> cut, float volume, bool isMuted) =>
    cut.InvokeAsync(() => _store.OnHubVolumeChanged(new VolumeDto(volume, isMuted)));

  [Fact]
  public async Task MutedAtPlayTime_ShowsTheAmberPill()
  {
    var cut = RenderPlayer();
    await PrimeVolumeAsync(cut, volume: 0.75f, isMuted: true);

    TapPlay(cut);

    Assert.Single(cut.FindAll(".phone-pill.amber"));
    Assert.Contains("The console is muted.", cut.Markup);

    // ⚠ HONEST LIMIT. This pins that the pill appears when the store reports muted at play time,
    // through the same handler a real broadcast runs. It does NOT pin the ORDERING. The plan named
    // "read mute after the start call" as the falsifying mutation, and that mutation leaves this
    // GREEN: nothing in this harness changes the mute state between the read and the start, so both
    // orderings observe the same value. Pinning the ordering needs a fake that mutates mute from
    // inside the start call, which is a harness this row does not have. Said here rather than
    // implied, because an assertion that cannot fail is how five consecutive cycles in this arc
    // shipped a test that passed against a broken implementation.
    //
    // ⚠ And it does NOT pin the SEEDING, because there is none: nothing writes the store's Volume or
    // IsMuted except this broadcast (see AudioStateStore.OnHubVolumeChanged's remarks). A console
    // muted before radio-web started is a case no unit test can distinguish from this one.
  }

  [Fact]
  public async Task VolumeZeroAtPlayTime_ShowsTheAmberPill()
  {
    // The handoff's condition is "muted OR volume 0". A console at zero is inaudible whether or not
    // anything set the mute flag, and this arm is a separate disjunct in the component.
    var cut = RenderPlayer();
    await PrimeVolumeAsync(cut, volume: 0f, isMuted: false);

    TapPlay(cut);

    Assert.Single(cut.FindAll(".phone-pill.amber"));
  }

  [Fact]
  public async Task AudibleAtPlayTime_ShowsNoPill()
  {
    // ⭐ THE ABSENCE HALF, and the reason it is here: without it, mutating the component's
    // @if (_mutedAtPlay) to @if (true) left every muted-pill assertion GREEN. A test that only ever
    // asserts a thing is PRESENT cannot fail on an implementation that always shows it.
    var cut = RenderPlayer();
    await PrimeVolumeAsync(cut, volume: 0.75f, isMuted: false);

    TapPlay(cut);

    Assert.Empty(cut.FindAll(".phone-pill.amber"));
    Assert.DoesNotContain("The console is muted.", cut.Markup);
  }

  // ── transcript, unchanged by this row ────────────────────────────────────────────────────────

  [Fact]
  public void TranscriptPresent_RendersBody()
  {
    var cut = RenderPlayer(Vm(transcript: "Hello there"));
    Assert.Contains("Hello there", cut.Markup);
  }

  [Fact]
  public void TranscriptPending_WhenNullAndRecent()
  {
    var cut = RenderPlayer(Vm(transcript: null, received: DateTime.UtcNow));
    Assert.Contains("Transcript pending", cut.Markup);
  }

  [Fact]
  public void TranscriptAbsent_WhenNullAndOld()
  {
    var cut = RenderPlayer(Vm(transcript: null, received: DateTime.UtcNow.AddHours(-2)));
    Assert.Contains("No transcript available", cut.Markup);
  }

  [Fact]
  public void Duration_RendersEmDash_WhenZero()
  {
    var cut = RenderPlayer(Vm(duration: 0));
    // total shows --:-- not 0:00 when duration unknown
    Assert.Contains("--:--", cut.Markup);
  }

  /// <summary>
  /// Answers /api/audio/events* with a snapshot carrying the id every test above broadcasts against.
  /// </summary>
  /// <remarks>
  /// ⚠ 202 with State "Preparing", because that is what the API answers: the accept happens BEFORE
  /// any audio exists. A handler returning "Playing" here would let a test pass against a component
  /// that rendered Playing straight off the start call, which is the failure handoff §Cross-5 exists
  /// to prevent.
  /// </remarks>
  private sealed class EventsApiHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.AbsolutePath ?? string.Empty;
      if (!path.StartsWith("/api/audio/events", StringComparison.Ordinal))
      {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
      }

      const string body = """
        {"id":"evp-1","kind":"RemoteMedia","label":"Voicemail from Jane","state":"Preparing",
         "duration":"00:00:42","positionAtBroadcast":"00:00:00",
         "broadcastAtUtc":"2026-09-05T00:00:00+00:00","failureReason":null}
        """;

      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
      {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
      });
    }
  }
}
