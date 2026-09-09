using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// The hub client's null-PASS-THROUGH contract — the half of ADR-033 that `UI-12` did not gate
/// (queue row `UI-14`).
/// </summary>
/// <remarks>
/// ⚠⚠ THESE DRIVE THE On&lt;T&gt; HANDLER BODY, NOT THE EVENT, AND THAT IS THE ENTIRE POINT.
/// <c>HubEventFire</c> reflects the compiler-generated backing field and awaits subscribers directly
/// (HubEventFire.cs:54-61), which is DOWNSTREAM of the fan-out — a null test written that way passes
/// whether the null is passed through, dropped, or replaced. The two tests in this repository that
/// fire a null on a hub event (NowPlayingDockTests.Dock_NullNowPlayingDto_ClearsState and
/// SleepTests.Sleep_NullNowPlayingDto_ClearsTrackBlock) both do exactly that; they are correct tests
/// of those COMPONENTS and prove nothing about this service.
///
/// ⭐ THE MISTAKE THESE EXIST TO CATCH IS GREEN WITHOUT THEM. Keeping AcceptPayload and ALSO adding a
/// "defensive" `if (arg is null) return;` to AudioStateHubService.NotifyAsync&lt;T&gt; passed the
/// entire Radio.Web.Tests assembly before this file existed, while silently dropping every
/// NowPlayingChanged(null). Measured on the tree, as `UI-14` Task 0. The naive form of the same
/// refactor — deleting AcceptPayload — fails four tests in AudioStateHubServiceNullPayloadTests, so
/// the pre-existing gate looked real and was not.
///
/// ⚠ NO ASSERTION HERE USES A WALL CLOCK (`CLAUDE.md` § Test Timing). Every seam is awaited to
/// completion before anything is asserted, so each observation is a fact about control flow.
///
/// 📌 What these do NOT prove: that a null can ever arrive. It cannot — each of the three events has
/// exactly one server-side sender and none can produce one (`UI-14` §0.4). These pin the BOUNDARY's
/// behaviour if one ever does, which is the same standing ADR-033 gives the rejection tests.
/// </remarks>
public class AudioStateHubServicePassThroughTests
{
  private static AudioStateHubService NewHub(List<(LogLevel Level, string Message)> sink) =>
    new(
      new CapturingLogger<AudioStateHubService>(sink),
      new ConfigurationBuilder().Build(),
      transport: new OfflineHubTransport());

  private static EventPlaybackSnapshotDto Snapshot() =>
    new(
      Id: "evt-1",
      Kind: "Voicemail",
      Label: "Voicemail",
      State: "Playing",
      Duration: TimeSpan.FromSeconds(30),
      PositionAtBroadcast: TimeSpan.Zero,
      BroadcastAtUtc: DateTimeOffset.UnixEpoch,
      FailureReason: null);

  // --- T1/T2/T3 — THE HEADLINE: a null MUST reach every subscriber, AS a null -------------------

  /// <summary>
  /// ⭐ THE DISCRIMINATING TEST. Add `if (arg is null) return;` to NotifyAsync&lt;T&gt; — with or
  /// without keeping AcceptPayload — and this goes RED. Before `UI-14` no test in the repository
  /// could.
  /// </summary>
  /// <remarks>
  /// ⚠ TWO subscribers, and the `seen` slots are SEEDED WITH NON-NULL SENTINELS. Both are deliberate.
  /// Two subscribers means a regression to a direct `Event.Invoke(dto)` — which runs every handler but
  /// returns only the LAST one's Task (`UI-7`) — is still observed here. The sentinels make
  /// Assert.Null non-vacuous: without them a handler that never ran and a handler that ran with null
  /// leave the same value behind, and the assertion would pass against a dropped payload.
  /// </remarks>
  [Fact]
  public async Task NullNowPlayingPayloadReachesEverySubscriberAsNull()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var order = new List<int>();
    var seen = new NowPlayingDto?[] { new(), new() };

    hub.NowPlayingChanged += d => { order.Add(1); seen[0] = d; return Task.CompletedTask; };
    hub.NowPlayingChanged += d => { order.Add(2); seen[1] = d; return Task.CompletedTask; };

    await hub.OnNowPlayingMessageAsync(null);

    Assert.Equal([1, 2], order);
    Assert.All(seen, s => Assert.Null(s));
  }

  [Fact]
  public async Task NullVolumePayloadReachesEverySubscriberAsNull()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var order = new List<int>();
    var seen = new VolumeDto?[] { new(0.5f, false), new(0.5f, false) };

    hub.VolumeChanged += d => { order.Add(1); seen[0] = d; return Task.CompletedTask; };
    hub.VolumeChanged += d => { order.Add(2); seen[1] = d; return Task.CompletedTask; };

    await hub.OnVolumeMessageAsync(null);

    Assert.Equal([1, 2], order);
    Assert.All(seen, s => Assert.Null(s));
  }

  /// <summary>
  /// ⚠ UNEVIDENCED IN PRODUCTION and this test says so rather than implying otherwise. No producer
  /// sends EventPlaybackChanged(null) — AudioStateUpdateService.OnEventPlaybackChanged always builds
  /// an anonymous `new { … }`. What is pinned here is that the DECLARATION and the DISPATCH agree, so
  /// a future producer that does send one is not silently discarded at the boundary.
  /// </summary>
  [Fact]
  public async Task NullEventPlaybackPayloadReachesEverySubscriberAsNull()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var order = new List<int>();
    var seen = new EventPlaybackSnapshotDto?[] { Snapshot(), Snapshot() };

    hub.EventPlaybackChanged += d => { order.Add(1); seen[0] = d; return Task.CompletedTask; };
    hub.EventPlaybackChanged += d => { order.Add(2); seen[1] = d; return Task.CompletedTask; };

    await hub.OnEventPlaybackMessageAsync(null);

    Assert.Equal([1, 2], order);
    Assert.All(seen, s => Assert.Null(s));
  }

  // --- T4/T5/T6 — the regression half: the happy path must survive the extraction ---------------

  /// <summary>
  /// ⚠ Task 1 rewrites the ONLY production path that dispatches these three events, so the happy path
  /// needs its own assertion. T1-T3 cover the null; if the extraction broke dispatch for a NON-null
  /// payload as well, that is a total outage of the now-playing dock, the volume readout and the
  /// attended-playback state — and nothing in T1-T3 would say so, because they never send one. These
  /// three are the other side. Same argument as
  /// AudioStateHubServiceNullPayloadTests.NonNullRadioStatePayloadIsDispatchedUnchanged's.
  /// </summary>
  [Fact]
  public async Task NonNullNowPlayingPayloadIsDispatchedUnchanged()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var dto = new NowPlayingDto { Title = "Hey Jude", Artist = "The Beatles" };
    NowPlayingDto? seen = null;

    hub.NowPlayingChanged += d => { seen = d; return Task.CompletedTask; };

    await hub.OnNowPlayingMessageAsync(dto);

    Assert.Same(dto, seen);
  }

  [Fact]
  public async Task NonNullVolumePayloadIsDispatchedUnchanged()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var dto = new VolumeDto(0.42f, true);
    VolumeDto? seen = null;

    hub.VolumeChanged += d => { seen = d; return Task.CompletedTask; };

    await hub.OnVolumeMessageAsync(dto);

    Assert.Same(dto, seen);
  }

  [Fact]
  public async Task NonNullEventPlaybackPayloadIsDispatchedUnchanged()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var dto = Snapshot();
    EventPlaybackSnapshotDto? seen = null;

    hub.EventPlaybackChanged += d => { seen = d; return Task.CompletedTask; };

    await hub.OnEventPlaybackMessageAsync(dto);

    Assert.Same(dto, seen);
  }

  // --- T7 — a null on a nullable event is ORDINARY, and must stay quiet -------------------------

  /// <summary>
  /// ⭐ A null here is DATA, so it must not be reported as a fault. This is the mirror of
  /// AudioStateHubServiceNullPayloadTests' "it is not silent" assertion, and it is the one gate a
  /// dispatch assertion cannot provide: a seam could pass the null through correctly AND log a
  /// Warning, which looks harmless and is not.
  /// </summary>
  /// <remarks>
  /// ⚠ WHY THIS ASSERTS "NO WARNING AT ALL" WHERE UI-12'S EQUIVALENT WAS NARROWED TO THE REJECTION
  /// WORDING. That narrowing was right for a test that runs a real dispatch with subscribers attached,
  /// where an unrelated legitimate warning could appear later and fail the test for the wrong reason.
  /// Here no subscribers are attached and each seam's whole body is one LogDebug plus one NotifyAsync
  /// over an empty invocation list — there is no legitimate Warning these three lines can emit. If a
  /// future edit adds one, this test failing IS the intended conversation, not a false positive:
  /// src/Radio.Web/appsettings.json leaves the Console sink unrestricted, so under systemd every
  /// Warning from this process reaches `journalctl -u radio-web`, and NowPlayingChanged fires on every
  /// metadata change (`CLAUDE.md` § Deployment — log volume correlates with audible distortion).
  /// </remarks>
  [Fact]
  public async Task ANullOnANullableEventIsNotReportedAsAFault()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);

    await hub.OnNowPlayingMessageAsync(null);
    await hub.OnVolumeMessageAsync(null);
    await hub.OnEventPlaybackMessageAsync(null);

    Assert.DoesNotContain(sink, e => e.Level >= LogLevel.Information);
  }
}
