using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.Sources.Events;
using Radio.Infrastructure.External;
using Radio.Infrastructure.Tests.External;

namespace Radio.Infrastructure.Tests.Audio.Events;

/// <summary>
/// EventPlaybackService — ADR-029 D1/D2/D3.
///
/// <para>
/// Built over REAL collaborators wherever that is cheap: a real GvMediaClient over a stub
/// HttpMessageHandler, a real GvMediaCache over a per-test temp directory, and a real
/// AudioFileEventSourceFactory. Only ITTSFactory and IDuckingService are faked, and both fakes are
/// hand-written — this project does not use a mocking framework for these.
/// </para>
///
/// <para>
/// ⚠ Every asynchronous assertion goes through <see cref="NextSnapshotWith"/> or
/// <see cref="WaitUntilAsync"/>, never a bare Task.Delay before an assertion. A Task.Delay inside a
/// fake collaborator that is SIMULATING WORK is a different thing and is used deliberately in two
/// places; each says so.
/// </para>
/// </summary>
public sealed class EventPlaybackServiceTests : IDisposable
{
  private const string RawMediaId = "vm-secret-identifier-9876";
  private const string PrivateUtterance = "Meet me at the bridge at nine, bring the envelope";

  private readonly string _root =
    Path.Combine(Path.GetTempPath(), "evp-tests-" + Guid.NewGuid().ToString("N"));

  private readonly string _cacheDir;
  private readonly string _fileRoot;
  private readonly List<HttpClient> _httpClients = [];
  private readonly FakeDuckingService _ducking = new();

  public EventPlaybackServiceTests()
  {
    _cacheDir = Path.Combine(_root, "gvmedia");
    _fileRoot = Path.Combine(_root, "music");
    Directory.CreateDirectory(_cacheDir);
    Directory.CreateDirectory(_fileRoot);
  }

  public void Dispose()
  {
    foreach (var client in _httpClients)
    {
      client.Dispose();
    }

    try
    {
      Directory.Delete(_root, recursive: true);
    }
    catch (Exception)
    {
      // Best effort — a leftover temp directory must never fail a test during teardown.
    }
  }

  // ── fixtures ────────────────────────────────────────────────────────────

  private static EventPlaybackRequest SpeechRequest(string text = PrivateUtterance) =>
    new() { Kind = EventPlaybackKind.Speech, Text = text, Label = "Message from Jane" };

  private static EventPlaybackRequest VoicemailRequest(int durationSeconds = 12) =>
    new()
    {
      Kind = EventPlaybackKind.RemoteMedia,
      MediaKind = RemoteMediaKind.GvVoicemail,
      MediaId = RawMediaId,
      DurationSeconds = durationSeconds,
      Label = "Voicemail from Jane"
    };

  private static TTSOptions DeployedTtsOptions() => new()
  {
    // What the box actually ships, so the C-25 pin asserts against real values.
    DefaultEngine = "Google",
    DefaultVoice = "en-US-Standard-A",
    DefaultSpeed = 1.0f,
    DefaultPitch = 1.0f
  };

  private EventPlaybackService CreateService(
    ITTSFactory? ttsFactory = null,
    GvMediaOptions? gvMedia = null,
    TTSOptions? tts = null,
    HttpMessageHandler? httpHandler = null,
    FakeDuckingService? ducking = null,
    CapturingLoggerProvider? logs = null,
    TimeProvider? timeProvider = null)
  {
    var gvOptions = gvMedia ?? new GvMediaOptions { Enabled = true, CacheDirectory = _cacheDir };
    var gvMonitor = new StaticOptionsMonitor<GvMediaOptions>(gvOptions);

    var cache = new GvMediaCache(
      logs?.CreateLogger<GvMediaCache>() ?? NullLogger<GvMediaCache>.Instance, gvMonitor);

    var http = new HttpClient(
      httpHandler ?? new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)))
    {
      // Mirrors what GvMediaServiceExtensions does at registration, so the Timeout case behaves
      // here the way it does in the container.
      Timeout = TimeSpan.FromSeconds(Math.Max(1, gvOptions.FetchTimeoutSeconds))
    };
    _httpClients.Add(http);

    var client = new GvMediaClient(
      logs?.CreateLogger<GvMediaClient>() ?? NullLogger<GvMediaClient>.Instance,
      gvMonitor, http, cache);

    var fileFactory = new AudioFileEventSourceFactory(
      logs?.CreateLogger<AudioFileEventSourceFactory>()
        ?? NullLogger<AudioFileEventSourceFactory>.Instance,
      logs?.CreateLogger<AudioFileEventSource>() ?? NullLogger<AudioFileEventSource>.Instance,
      new StaticOptionsMonitor<FilePlayerOptions>(
        new FilePlayerOptions { RootDirectory = _fileRoot }));

    return new EventPlaybackService(
      logs?.CreateLogger<EventPlaybackService>() ?? NullLogger<EventPlaybackService>.Instance,
      gvMonitor,
      new StaticOptionsMonitor<TTSOptions>(tts ?? DeployedTtsOptions()),
      ttsFactory ?? new FakeTtsFactory(),
      fileFactory,
      ducking ?? _ducking,
      client,
      timeProvider);
  }

  private static HttpResponseMessage Mp3Of(int bytes) =>
    new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[bytes]) };

  // ── helpers ─────────────────────────────────────────────────────────────

  /// <summary>
  /// Completes on the first snapshot in the given state. Subscribed BEFORE the action that causes
  /// it, so there is no window in which the transition can be missed.
  /// </summary>
  /// <remarks>
  /// ⚠ Every asynchronous assertion in this file goes through this or WaitUntilAsync, never through
  /// a fixed Task.Delay. TEST-4 is the row about a wall-clock test window racing a wall-clock loop,
  /// and TEST-1 is the row about not writing the next one.
  /// </remarks>
  private static Task<EventPlaybackSnapshot> NextSnapshotWith(
    EventPlaybackService service, EventPlaybackState state)
    => NextSnapshotMatching(service, s => s.State == state);

  /// <summary>
  /// The same rendezvous, on an arbitrary predicate — for the cases where the STATE alone does not
  /// identify the snapshot being waited for, because two playbacks are in play at once.
  /// </summary>
  private static Task<EventPlaybackSnapshot> NextSnapshotMatching(
    EventPlaybackService service, Func<EventPlaybackSnapshot, bool> predicate)
  {
    var tcs = new TaskCompletionSource<EventPlaybackSnapshot>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    service.PlaybackChanged += (_, s) =>
    {
      if (predicate(s))
      {
        tcs.TrySetResult(s);
      }
    };
    return tcs.Task;
  }

  private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
  {
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
      if (condition())
      {
        return;
      }
      await Task.Delay(10);   // a poll INSIDE a bounded wait, not a sleep before an assertion
    }
    Assert.Fail($"Condition was not met within {timeout}.");
  }

  // ── the four load-bearing facts ─────────────────────────────────────────

  [Fact]
  public async Task StartAsync_ReturnsPreparing_BeforeAnyAudioExists()
  {
    // ADR-029 §3.3 specifies 202, and IEventPlaybackService's own doc says the snapshot is
    // "normally Preparing, because both arms have an acquisition phase". Everything about the
    // cancellation model below follows from this being true.
    var tts = new FakeTtsFactory();
    var gate = new TaskCompletionSource();
    tts.OnCreate = async (_, _, ct) =>
    {
      await gate.Task.WaitAsync(ct);
      return (IEventAudioSource)new FakeEventSource();
    };
    using var service = CreateService(ttsFactory: tts);

    var snapshot = await service.StartAsync(SpeechRequest());

    Assert.Equal(EventPlaybackState.Preparing, snapshot.State);
    Assert.StartsWith("evp-", snapshot.Id, StringComparison.Ordinal);
    // ⚠ A DOCUMENTATION assertion, and it is labelled because it CANNOT FAIL as written: SnapshotOf
    // returns null for Duration unconditionally while the state is Preparing, so no change to the
    // acquisition path can make this red. It stays because the rule it records — nothing claims a
    // duration before audio exists — is what a future edit to SnapshotOf would break, and then it
    // would start doing real work.
    Assert.Null(snapshot.Duration);
    Assert.Equal(TimeSpan.Zero, snapshot.PositionAtBroadcast);
    Assert.Equal(snapshot.Id, service.Current?.Id);

    gate.SetResult();
  }

  [Fact]
  public async Task TheAcceptedPreparingSnapshotIsPublishedBeforePlaying()
  {
    // An ORDERING pin. Acquisition runs on a task that does NOT take the service's gate, so with
    // the fastest possible acquisition — an already-completed CreateAsync, which is also what a
    // cache hit looks like — it could in principle publish Playing before the accepting call
    // publishes Preparing. Both stores take the same lock, so the LATER writer would win: Current
    // would report Preparing for a playback already producing audio, and a subscriber would see
    // Playing followed by Preparing, a transition that never happened. Publishing the accepted
    // snapshot before starting the task makes the order structural rather than a matter of timing.
    //
    // ⚠ Honest about its strength: with the publish moved back after the Task.Run this test still
    // passed 25/25 on a Windows dev machine, because the window is only the few instructions
    // between queueing the work item and publishing. So read this as a REGRESSION GUARD on the
    // ordering, not as evidence the race was reachable here. The invariant it asserts —
    // "Preparing is never observed after Playing" — is the one that matters and is cheap to hold.
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory
    {
      OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source)
    };
    using var service = CreateService(ttsFactory: tts);

    var states = new List<EventPlaybackState>();
    service.PlaybackChanged += (_, s) =>
    {
      lock (states) { states.Add(s.State); }
    };
    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);

    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    lock (states)
    {
      Assert.Equal(
        [EventPlaybackState.Preparing, EventPlaybackState.Playing],
        states.Take(2).ToArray());
    }
    Assert.Equal(EventPlaybackState.Playing, service.Current!.State);
  }

  [Fact]
  public async Task AcquisitionSurvivesCancellationOfTheStartToken()
  {
    // ⚠ THE C-21 PIN. On the HTTP path the token passed to StartAsync is
    // HttpContext.RequestAborted, which is scoped to a request the acquisition deliberately
    // outlives — so linking the two would cancel every fetch the instant it was accepted, and the
    // RemoteMedia arm would fail 100% of the time in a way that looks like a network problem. This
    // fails if anyone links them.
    var tts = new FakeTtsFactory();
    var released = new TaskCompletionSource();
    var source = new FakeEventSource();
    tts.OnCreate = async (_, _, ct) =>
    {
      await released.Task.WaitAsync(ct);
      return (IEventAudioSource)source;
    };
    using var service = CreateService(ttsFactory: tts);
    using var caller = new CancellationTokenSource();

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest(), caller.Token);

    // The caller goes away the instant it has its 202 — exactly what a kiosk reload does.
    await caller.CancelAsync();
    released.SetResult();

    var final = await playing.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(accepted.Id, final.Id);
    Assert.Equal(EventPlaybackState.Playing, final.State);
    Assert.Equal(1, source.PlayCalls);
  }

  [Fact]
  public async Task ATerminalTransitionHappensExactlyOnce_EvenWhenTheSourceRaisesCompletionTwice()
  {
    // C-28. Both shipped sources raise EndOfContent from their monitor AND UserStopped from
    // StopCoreAsync, and AudioSourceBase.StopAsync does not short-circuit on Stopped — so teardown
    // after a natural end raises a second event. AnnouncementService is immune by accident
    // (TrySetResult discards it); a state machine is not.
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory
    {
      OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source)
    };
    using var service = CreateService(ttsFactory: tts);

    var terminals = new List<EventPlaybackSnapshot>();
    service.PlaybackChanged += (_, s) =>
    {
      if (s.State is EventPlaybackState.Completed or EventPlaybackState.Stopped
          or EventPlaybackState.Failed)
      {
        lock (terminals) { terminals.Add(s); }
      }
    };

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    // Rendezvous on the PUBLISHED terminal snapshot rather than on service.Current, and raise the
    // second completion only after it: the publish is the last statement of the handler's gated
    // body, so awaiting it means the first completion has been processed to the end — teardown
    // included — before the second is raised. Waiting on service.Current instead, as this test used
    // to, let both completions be in flight at once, so "exactly once" could pass by luck.
    var completed = NextSnapshotWith(service, EventPlaybackState.Completed);
    source.RaiseCompleted(PlaybackCompletionReason.EndOfContent);
    await completed.WaitAsync(TimeSpan.FromSeconds(5));

    source.RaiseCompleted(PlaybackCompletionReason.UserStopped);

    // ⚠ The second half is a BOUNDED NEGATIVE check and says so (CLAUDE.md § Test Timing). With the
    // ClaimTerminal guard working there is nothing to wait FOR — the correct behaviour is that
    // nothing happens — so this waits on two gated round-trips instead. Each StopAsync takes and
    // releases _gate, which is the same gate a stray second teardown would have to acquire, so a
    // stray one has a real opportunity to land before the assertions run. Starvation can only
    // WEAKEN this (less opportunity), never flip it into a false failure.
    Assert.False(await service.StopAsync("evp-nope"));
    Assert.False(await service.StopAsync("evp-nope"));

    lock (terminals)
    {
      Assert.Single(terminals);
      // The FIRST one wins. A guard that let the last write through would report Stopped for a
      // playback that ran to the end.
      Assert.Equal(EventPlaybackState.Completed, terminals[0].State);
    }

    // The second half of the same claim: without the guard the second completion runs its own
    // teardown, so these counts would be 2 rather than 1. Note that the FIRST teardown's own
    // source.StopAsync raises UserStopped inline, exactly as the real sources do — so the guard is
    // already being exercised once before the explicit raise above.
    Assert.Equal(1, source.StopCalls);
    Assert.Equal(1, source.DisposeCalls);
    Assert.Equal(EventPlaybackState.Completed, service.Current!.State);
  }

  [Theory]
  [InlineData(HttpStatusCode.NotFound, "MediaNotFound")]
  [InlineData(HttpStatusCode.Unauthorized, "MediaUnauthorized")]
  [InlineData(HttpStatusCode.Forbidden, "MediaUnauthorized")]
  [InlineData(HttpStatusCode.BadGateway, "MediaUpstream")]
  [InlineData(HttpStatusCode.InternalServerError, "MediaUpstream")]
  public async Task EveryGvMediaFailureReachesTheSnapshotUnderItsOwnName(
    HttpStatusCode status, string expectedReason)
  {
    // C-23. The 202 shape means these never become status codes, so FailureReason is the ONLY place
    // the distinction survives — and GV-6 / GV-8 are open rows for exactly the collapse this
    // prevents. Driven through the REAL GvMediaClient over a stub handler, so the real exception is
    // produced rather than a test constructing one.
    using var service = CreateService(
      gvMedia: new GvMediaOptions { Enabled = true, CacheDirectory = _cacheDir },
      httpHandler: new StubHandler(_ => new HttpResponseMessage(status)));

    var failed = NextSnapshotWith(service, EventPlaybackState.Failed);
    await service.StartAsync(VoicemailRequest());

    var final = await failed.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(expectedReason, final.FailureReason);

    // ⚠ Current RETAINS the failure rather than going null, and that is the point of the whole
    // shape: StartAsync answered before the fetch was made, so there is no response left to carry
    // a status code and GET /api/audio/events/current is where a caller reads what happened.
    Assert.Equal(EventPlaybackState.Failed, service.Current!.State);
    Assert.Equal(expectedReason, service.Current.FailureReason);
    Assert.Equal(final.Id, service.Current.Id);
  }

  [Fact]
  public async Task TheTransportAndTooLargeFailuresAlsoReachTheSnapshotUnderTheirOwnNames()
  {
    // The two the status-code theory above cannot reach: Transport needs the handler to THROW an
    // HttpRequestException, and TooLarge needs a declared body over MaxPlaybackSeconds x 32 000 B/s.
    using (var transport = CreateService(
      gvMedia: new GvMediaOptions { Enabled = true, CacheDirectory = _cacheDir },
      httpHandler: new StubHandler(_ => throw new HttpRequestException("no route to host"))))
    {
      var failed = NextSnapshotWith(transport, EventPlaybackState.Failed);
      await transport.StartAsync(VoicemailRequest());

      Assert.Equal(
        "MediaTransport", (await failed.WaitAsync(TimeSpan.FromSeconds(5))).FailureReason);
    }

    using var tooLarge = CreateService(
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, MaxPlaybackSeconds = 1
      },
      // 1 second x the client's assumed 32 000 B/s bound = 32 000 bytes. 40 000 declared is over.
      httpHandler: new StubHandler(_ => Mp3Of(40_000)));

    var over = NextSnapshotWith(tooLarge, EventPlaybackState.Failed);
    await tooLarge.StartAsync(VoicemailRequest());

    Assert.Equal("MediaTooLarge", (await over.WaitAsync(TimeSpan.FromSeconds(5))).FailureReason);
  }

  [Fact]
  public async Task ATimedOutFetchReachesTheSnapshotAsMediaTimeout()
  {
    // FetchTimeoutSeconds = 1 is mirrored onto HttpClient.Timeout by CreateService, exactly as
    // GvMediaServiceExtensions does at registration, so this is the real timeout path rather than a
    // constructed exception.
    //
    // ⚠ TIMING DECLARATION (CLAUDE.md § Test Timing). This test DOES depend on a production timer
    // firing inside a test-side wait — HttpClient's own 1 s timeout, inside the 15 s wait below. The
    // margin is 15x, and starvation pushes it toward FAILURE, which is the dangerous direction of
    // the two; state that rather than implying a determinism this does not have.
    //
    // Not converted to an injected TimeProvider, and the reason is that there is no seam here to
    // inject one into: the clock belongs to HttpClient, not to this seam or to GvMediaClient, so the
    // honest alternative is replacing HttpClient.Timeout with a self-imposed CancelAfter inside
    // GvMediaClient — a change to a shipped class, in a test-only fix pass. If this ever flakes on
    // CI, raise the wait, or take that route deliberately.
    var handler = new StubHandler(async (_, ct) =>
    {
      // A delay inside a fake collaborator SIMULATING an upstream that never answers. It is not a
      // synchroniser: the assertion below waits on a published snapshot, not on this.
      await Task.Delay(Timeout.Infinite, ct);
      return new HttpResponseMessage(HttpStatusCode.OK);
    });

    using var service = CreateService(
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, FetchTimeoutSeconds = 1
      },
      httpHandler: handler);

    var failed = NextSnapshotWith(service, EventPlaybackState.Failed);
    await service.StartAsync(VoicemailRequest());

    Assert.Equal("MediaTimeout", (await failed.WaitAsync(TimeSpan.FromSeconds(15))).FailureReason);
  }

  // ── the cancellation that DOES exist ────────────────────────────────────

  [Fact]
  public async Task StopAsync_CancelsAnAcquisitionStillInFlight()
  {
    var reached = new TaskCompletionSource();
    var observedCancellation = new TaskCompletionSource();
    var handler = new StubHandler(async (_, ct) =>
    {
      reached.TrySetResult();
      try
      {
        // Simulating an upstream that has not answered yet. The test synchronises on
        // observedCancellation, never on this delay.
        await Task.Delay(Timeout.Infinite, ct);
      }
      catch (OperationCanceledException)
      {
        observedCancellation.TrySetResult();
        throw;
      }
      return new HttpResponseMessage(HttpStatusCode.OK);
    });

    using var service = CreateService(
      gvMedia: new GvMediaOptions { Enabled = true, CacheDirectory = _cacheDir },
      httpHandler: handler);

    var stopped = NextSnapshotWith(service, EventPlaybackState.Stopped);
    var accepted = await service.StartAsync(VoicemailRequest());
    await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.True(await service.StopAsync(accepted.Id));

    await observedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var final = await stopped.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(accepted.Id, final.Id);
    Assert.Equal(EventPlaybackState.Stopped, service.Current!.State);
    // A second stop is refused — but NOT by ClaimTerminal, which is what this line used to claim.
    // The first StopAsync set _current to null, so this returns at the "playback is null" branch
    // several statements earlier and never reaches the flag. ClaimTerminal is what makes a stop
    // racing a COMPLETION once-only; that is a different guard on a different path, pinned by
    // ATerminalTransitionHappensExactlyOnce.
    Assert.False(await service.StopAsync(accepted.Id));
  }

  [Fact]
  public async Task Dispose_CancelsAnAcquisitionStillInFlight()
  {
    // The third cancellation that actually exists: the container disposes singletons at shutdown.
    var reached = new TaskCompletionSource();
    var observedCancellation = new TaskCompletionSource();
    var handler = new StubHandler(async (_, ct) =>
    {
      reached.TrySetResult();
      try
      {
        await Task.Delay(Timeout.Infinite, ct);
      }
      catch (OperationCanceledException)
      {
        observedCancellation.TrySetResult();
        throw;
      }
      return new HttpResponseMessage(HttpStatusCode.OK);
    });

    var service = CreateService(
      gvMedia: new GvMediaOptions { Enabled = true, CacheDirectory = _cacheDir },
      httpHandler: handler);

    await service.StartAsync(VoicemailRequest());
    await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));

    service.Dispose();

    await observedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartAsync(SpeechRequest()));
  }

  [Fact]
  public async Task StopDuringAcquisitionDisposesTheSourceItWasHolding()
  {
    // ⚠ A LEAK PIN. Every terminal caller claims the flag and then tears down, and teardown can
    // only release a source the playback already owns — which it does not, for the whole of
    // acquisition. So a stop landing mid-acquisition used to leave the source that acquisition then
    // produced with nobody to dispose it: on the RemoteMedia arm an AudioFileEventSource holding an
    // open FileStream over the cached recording, which on Windows also stops GvMediaCache's evictor
    // reclaiming that entry.
    var source = new FakeEventSource();
    var reached = new TaskCompletionSource();
    var release = new TaskCompletionSource();
    var tts = new FakeTtsFactory
    {
      OnCreate = async (_, _, _) =>
      {
        reached.TrySetResult();
        // Deliberately NOT awaited on the token: this models a synthesis that finished just as the
        // stop landed, which is the case where a source really does come into existence afterwards.
        await release.Task;
        return (IEventAudioSource)source;
      }
    };
    using var service = CreateService(ttsFactory: tts);

    var accepted = await service.StartAsync(SpeechRequest());
    await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.True(await service.StopAsync(accepted.Id));
    release.SetResult();

    await WaitUntilAsync(() => source.DisposeCalls == 1, TimeSpan.FromSeconds(5));

    // Never adopted, so never ducked and never played — there is nothing to stop, only to release.
    Assert.Equal(0, source.PlayCalls);
    Assert.Equal(0, source.StopCalls);
    Assert.Empty(_ducking.Started);
  }

  [Fact]
  public async Task DisposeDuringAcquisitionDisposesTheSourceItWasHolding()
  {
    // The same leak by the other door, and the one that needs its own test: Dispose CANCELS without
    // claiming the terminal flag — it is synchronous and teardown is not — so a check written
    // against the terminal flag alone would still leak here.
    var source = new FakeEventSource();
    var reached = new TaskCompletionSource();
    var release = new TaskCompletionSource();
    var tts = new FakeTtsFactory
    {
      OnCreate = async (_, _, _) =>
      {
        reached.TrySetResult();
        await release.Task;
        return (IEventAudioSource)source;
      }
    };
    var service = CreateService(ttsFactory: tts);

    await service.StartAsync(SpeechRequest());
    await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));

    service.Dispose();
    release.SetResult();

    await WaitUntilAsync(() => source.DisposeCalls == 1, TimeSpan.FromSeconds(5));
    Assert.Equal(0, source.PlayCalls);
  }

  [Fact]
  public async Task DisposeReleasesASourceThatIsAlreadyPlaying()
  {
    // ⚠ THE OTHER HALF OF THE LEAK, and the half that had no test. Both Dispose tests above cancel
    // MID-ACQUISITION, where TryAdopt's refusal is what releases the source. Once AcquireAndPlayAsync
    // has RETURNED, nothing else will ever run teardown for that playback — so before PHN-1c's
    // review Dispose cancelled a token nobody was waiting on and walked away: StopDuckingAsync and
    // DisposeAsync never ran, and on the RemoteMedia arm an AudioFileEventSource's FileStream over
    // the cached recording was left to the finalizer, which on Windows also blocks GvMediaCache from
    // evicting that entry.
    //
    // No WaitUntilAsync here, deliberately: Dispose blocks on the release (bounded), so by the time
    // it returns the three assertions below are already settled. If that ever stops being true these
    // fail immediately rather than flaking.
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory
    {
      OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source)
    };
    var service = CreateService(ttsFactory: tts);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    service.Dispose();

    Assert.Equal(1, source.StopCalls);
    Assert.Equal(1, source.DisposeCalls);
    Assert.Contains(source.Id, _ducking.Stopped);

    // Once-only: a second Dispose must not run the release again. _disposedFlag is claimed with
    // Interlocked.Exchange precisely so two disposers cannot both get here.
    service.Dispose();
    Assert.Equal(1, source.DisposeCalls);
  }

  // ── the single-slot rule ────────────────────────────────────────────────

  [Fact]
  public async Task ASecondStartReplacesTheFirst_AndTheFirstIsTornDown()
  {
    // ADR-029 D6 §8.1 — one set of speakers. NOT D5's priority rule, which is PR 4.
    var first = new FakeEventSource();
    var second = new FakeEventSource();
    var queue = new Queue<IEventAudioSource>([first, second]);
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult(queue.Dequeue()) };
    using var service = CreateService(ttsFactory: tts);

    var firstPlaying = NextSnapshotWith(service, EventPlaybackState.Playing);
    var one = await service.StartAsync(SpeechRequest());
    await firstPlaying.WaitAsync(TimeSpan.FromSeconds(5));

    var replaced = NextSnapshotWith(service, EventPlaybackState.Stopped);
    var two = await service.StartAsync(SpeechRequest());
    await WaitUntilAsync(() => second.PlayCalls == 1, TimeSpan.FromSeconds(5));

    Assert.NotEqual(one.Id, two.Id);
    Assert.Equal(one.Id, (await replaced.WaitAsync(TimeSpan.FromSeconds(5))).Id);
    Assert.Equal(1, first.StopCalls);
    Assert.Equal(1, first.DisposeCalls);
    Assert.Contains(first.Id, _ducking.Stopped);
    Assert.Equal(two.Id, service.Current?.Id);
  }

  [Fact]
  public async Task ALateCompletionFromAReplacedPlaybackDoesNotClearTheCurrentOne()
  {
    // A completion arriving for a playback that has already been replaced must not clear the slot
    // the replacement occupies. The guard is the ReferenceEquals(_current, playback) check on the
    // completion path.
    //
    // ⚠ THE ORDER OF THE NEXT TWO STATEMENTS IS THE ENTIRE TEST, and it was wrong until PHN-1c's
    // review. Written the other way round — replace first, then raise the completion — the guard is
    // STATICALLY UNREACHABLE: the replacing StartAsync has already claimed the first playback's
    // terminal flag, so OnSourceCompleted returns at its first line, several statements short of the
    // check this test claimed to pin. Raising the completion FIRST claims that flag for the handler
    // instead, so the replacing StartAsync finds a playback it cannot claim, skips teardown, and
    // simply installs the replacement — which is the only shape in which the guard runs at all.
    var first = new FakeEventSource();
    var second = new FakeEventSource();
    var queue = new Queue<IEventAudioSource>([first, second]);
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult(queue.Dequeue()) };
    using var service = CreateService(ttsFactory: tts);

    var firstPlaying = NextSnapshotWith(service, EventPlaybackState.Playing);
    var one = await service.StartAsync(SpeechRequest());
    await firstPlaying.WaitAsync(TimeSpan.FromSeconds(5));

    // Subscribed before the completion is raised, and matched on the FIRST playback's id: two
    // playbacks are in flight here, so the state alone does not identify the snapshot.
    // Completed, not Stopped: the completion below is EndOfContent, and OnSourceCompleted maps that
    // to Completed. A replacing StartAsync would have published Stopped — and the fact that it does
    // not is half of what this test is about.
    var oneEnded = NextSnapshotMatching(
      service, s => s.Id == one.Id && s.State == EventPlaybackState.Completed);

    // OnSourceCompleted claims the terminal flag SYNCHRONOUSLY inside this call and only then queues
    // its gated body, so the claim is ordered even though the body is not.
    first.RaiseCompleted(PlaybackCompletionReason.EndOfContent);

    var two = await service.StartAsync(SpeechRequest());
    await WaitUntilAsync(() => second.PlayCalls == 1, TimeSpan.FromSeconds(5));

    // A REAL rendezvous with the handler: this snapshot is published on the statement AFTER the
    // guard, so awaiting it means the guard has run. The previous version waited on
    // "first.StopCalls >= 1", which the replacing StartAsync had already satisfied before the
    // completion was even raised — so the negative assertions below got zero grace.
    await oneEnded.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(two.Id, service.Current?.Id);
    Assert.Equal(EventPlaybackState.Playing, service.Current?.State);

    // ⚠ Honest about the residual. Which of the two reaches _gate first — the handler's queued body
    // or the replacing StartAsync — is a genuine race, and nothing here can order it without a seam
    // the production type does not have. StartAsync almost always wins because it takes a free
    // semaphore synchronously on this thread while the handler's body has still to be dispatched to
    // the pool. When it does not, the handler clears a slot it legitimately owns and StartAsync
    // installs into an empty one: the assertions above still hold, but the guard was not the reason.
    // So starvation WEAKENS this test rather than failing it, which is the safe direction
    // (CLAUDE.md § Test Timing). What the reordering bought is that the guard is reachable at all.
  }

  [Fact]
  public async Task AnErrorCompletionReachesTheSnapshotAsPlaybackError()
  {
    // The one FailureReason that is NOT an acquisition failure: the audio existed and the PLAYER
    // failed. It is why OnSourceCompleted maps PlaybackCompletionReason.Error to Failed rather than
    // to Stopped, and why EventPlaybackState.Failed cannot be documented as "never produced sound" —
    // an Error completion can arrive after minutes of audio.
    //
    // ⚠ This whole arm was unexercised until PHN-1c's review: FakeEventSource.RaiseCompleted has
    // always taken an Exception?, and no test passed one. On the box "PlaybackError" is what an
    // operator sees when SoundFlow fails to start, which is a completely different diagnosis from
    // every "Media*" reason — so it is in design/INTEGRATIONS.md's table now, and here.
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory
    {
      OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source)
    };
    using var service = CreateService(ttsFactory: tts);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var failed = NextSnapshotWith(service, EventPlaybackState.Failed);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    source.RaiseCompleted(
      PlaybackCompletionReason.Error, new InvalidOperationException("the device went away"));

    var final = await failed.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(accepted.Id, final.Id);
    Assert.Equal("PlaybackError", final.FailureReason);
    Assert.Equal(EventPlaybackState.Failed, service.Current!.State);
    Assert.Equal("PlaybackError", service.Current.FailureReason);

    // Torn down like any other terminal transition — an Error completion is not a special case that
    // leaves the source running.
    Assert.Equal(1, source.DisposeCalls);
  }

  // ── ducking ─────────────────────────────────────────────────────────────

  [Fact]
  public async Task TheRequestPriorityReachesDucking_AndDefaultsToTheAttendedClass()
  {
    // ⚠ FakeDuckingService has recorded SetPriority since the fake was written and NOTHING ASSERTED
    // THE RECORDING, so a Priority that never reached ducking would have been invisible — the field
    // that decides whether a voicemail is audible over an announcement, unpinned. 6 is the
    // attended-playback class (ADR-029 §6.1): below the 8 this system gives an event that did not
    // state its importance, so anything that did not claim a rank still outranks a user listening
    // to a recording.
    var configured = new FakeDuckingService();
    using (var service = CreateService(
      ttsFactory: new FakeTtsFactory
      {
        OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(new FakeEventSource())
      },
      ducking: configured))
    {
      var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
      await service.StartAsync(SpeechRequest());
      await playing.WaitAsync(TimeSpan.FromSeconds(5));
    }

    Assert.Equal(6, Assert.Single(configured.Priorities).Priority);

    // And the request's own value wins when it names one — otherwise the default above would pass
    // against an implementation that hard-coded 6 and ignored the request entirely.
    var overridden = new FakeDuckingService();
    using (var service = CreateService(
      ttsFactory: new FakeTtsFactory
      {
        OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(new FakeEventSource())
      },
      ducking: overridden))
    {
      var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
      await service.StartAsync(SpeechRequest() with { Priority = 3 });
      await playing.WaitAsync(TimeSpan.FromSeconds(5));
    }

    Assert.Equal(3, Assert.Single(overridden.Priorities).Priority);
  }

  // ── the TTSParameters pin ───────────────────────────────────────────────

  [Fact]
  public async Task SpeechFillsAllFourTtsParametersFromConfiguration()
  {
    // ⚠ THE C-25 PIN, and it must assert all four. TTSFactory resolves each field as
    // "parameters?.X ?? opts.X", and for Speed and Pitch the ?? is lifted by the null-conditional on
    // the OBJECT — they are non-nullable floats with a 1.0f initializer, so a partially-filled
    // TTSParameters pins them to the TYPE's default rather than to configuration. Engine and Voice
    // became nullable in TTS-9 and their ?? does fire; the trap survives on exactly two of the four.
    // (This comment said "all four" until PHN-1c's review, matching a wrong comment in
    // AcquireSpeechAsync. design/FUTURE-WORK.md § "TTS seam" item 1 always had it right.)
    //
    // ⚠ The configured speed and pitch are deliberately NOT 1.0f. With the shipped defaults they
    // would be identical to the type's own initializers, and this test would pass against a
    // TTSParameters that omitted them entirely — proving nothing.
    var tts = new FakeTtsFactory();
    using var service = CreateService(
      ttsFactory: tts,
      tts: new TTSOptions
      {
        DefaultEngine = "Google",
        DefaultVoice = "en-US-Standard-A",
        DefaultSpeed = 1.15f,
        DefaultPitch = 0.85f
      });

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    var parameters = Assert.IsType<TTSParameters>(tts.LastParameters);
    Assert.Equal(TTSEngine.Google, parameters.Engine);
    Assert.Equal("en-US-Standard-A", parameters.Voice);
    Assert.Equal(1.15f, parameters.Speed);
    Assert.Equal(0.85f, parameters.Pitch);
  }

  [Fact]
  public async Task ARequestVoiceOverridesTheConfiguredVoice_AndLeavesTheEngineOnTheConfiguredOne()
  {
    // The exact shape ADR-029 §9.3 warns about: the moment a voice is attached, a null-parameters
    // call is no longer available, so every other field has to be supplied explicitly.
    var tts = new FakeTtsFactory();
    using var service = CreateService(ttsFactory: tts);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest() with { VoiceId = "en-US-Neural2-A" });
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    var parameters = Assert.IsType<TTSParameters>(tts.LastParameters);
    Assert.Equal("en-US-Neural2-A", parameters.Voice);
    Assert.Equal(TTSEngine.Google, parameters.Engine);
  }

  // ── engine resolution ───────────────────────────────────────────────────

  [Theory]
  [InlineData(null, "Google", TTSEngine.Google)]
  [InlineData(null, "azure", TTSEngine.Azure)]
  [InlineData("Azure", "Google", TTSEngine.Azure)]
  [InlineData("google", "Azure", TTSEngine.Google)]
  public void ResolveEngine_MirrorsTheRulesOfTTSFactoryParseEngine(
    string? requested, string configured, TTSEngine expected)
  {
    // Case-insensitive by name, request override wins, configured default otherwise.
    Assert.Equal(expected, EventPlaybackService.ResolveEngine(requested, configured));
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("not-an-engine")]
  [InlineData("ESpeak")]
  [InlineData("0")]
  public void ResolveEngine_ThrowsRatherThanSubstitutingAnEngine(string configured)
  {
    // ⚠ It THROWS. TTSFactory.ParseEngine has no fallback either, since TTS-9 removed the engine it
    // used to fall back to — and for a private message body, silently synthesising with a
    // different engine is the substitution ADR-029 §9.4 says must never happen.
    Assert.Throws<InvalidOperationException>(
      () => EventPlaybackService.ResolveEngine(null, configured));
  }

  [Fact]
  public async Task AnUnconfiguredDefaultEngineReachesAFailedSnapshot_NotAHangInPreparing()
  {
    // C-31 — synthesis is the only gate, so a misconfigured TTS:DefaultEngine has to surface as a
    // named failure rather than as an unhandled exception on a background task or a Preparing that
    // never clears.
    using var service = CreateService(tts: new TTSOptions { DefaultEngine = string.Empty });

    var failed = NextSnapshotWith(service, EventPlaybackState.Failed);
    await service.StartAsync(SpeechRequest());

    var final = await failed.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal("SpeechSynthesisFailed", final.FailureReason);
    Assert.Equal(EventPlaybackState.Failed, service.Current!.State);
  }

  [Fact]
  public async Task SynthesisIsBoundedByGenerationTimeoutSeconds()
  {
    // C-24. TTSOptions.GenerationTimeoutSeconds has never had a reader in src/Radio.Infrastructure;
    // this is its first. Without it a hung synthesis parks the seam in Preparing with no route
    // that clears it.
    //
    // ⚠ TIMING DECLARATION (CLAUDE.md § Test Timing). Production's own CancelAfter(1 s) inside
    // AcquireSpeechAsync has to fire inside the 15 s wait below — a wall clock racing a wall clock,
    // which that section names as the shape not to write. Margin is 15x, and starvation pushes it
    // toward FAILURE, the dangerous direction.
    //
    // Not converted to an injected TimeProvider because it is not the cheap change it looks like:
    // CancellationTokenSource.CreateLinkedTokenSource takes no TimeProvider, so doing it properly
    // means replacing the linked-source-plus-CancelAfter in AcquireSpeechAsync with a
    // TimeProvider.CreateTimer, adding a constructor parameter to EventPlaybackService and wiring it
    // through AddEventPlayback — production changes in a fix pass scoped to the review findings.
    // Declared here so the dependency is visible rather than implied, and filed as the reason.
    var tts = new FakeTtsFactory
    {
      OnCreate = async (_, _, ct) =>
      {
        // Simulating a synthesis that never returns. The assertion waits on the snapshot.
        await Task.Delay(Timeout.Infinite, ct);
        return (IEventAudioSource)new FakeEventSource();
      }
    };
    using var service = CreateService(
      ttsFactory: tts,
      tts: new TTSOptions
      {
        DefaultEngine = "Google",
        DefaultVoice = "en-US-Standard-A",
        GenerationTimeoutSeconds = 1
      });

    var failed = NextSnapshotWith(service, EventPlaybackState.Failed);
    await service.StartAsync(SpeechRequest());

    var final = await failed.WaitAsync(TimeSpan.FromSeconds(15));

    Assert.Equal("SpeechSynthesisFailed", final.FailureReason);
    Assert.Equal(EventPlaybackState.Failed, service.Current!.State);
  }

  // ── synchronous refusals ────────────────────────────────────────────────

  [Fact]
  public async Task StartAsync_ThrowsEventPlaybackRejected_ForAnInvalidRequest()
  {
    // The seam validates as well as the controller. Both call Validate; neither re-derives a rule.
    using var service = CreateService();

    var ex = await Assert.ThrowsAsync<EventPlaybackRejectedException>(
      () => service.StartAsync(SpeechRequest() with { Priority = 99 }));

    Assert.Equal(EventPlaybackRejection.PriorityOutOfRange, ex.Reason);
    Assert.Null(service.Current);
    // The message must carry the reason NAME and no field of the request.
    Assert.Contains("PriorityOutOfRange", ex.Message, StringComparison.Ordinal);
    Assert.DoesNotContain(PrivateUtterance, ex.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task StartAsync_ThrowsGvMediaUnavailableDisabled_WithoutMintingAPlayback()
  {
    // C-23: the one failure knowable without the network is answered synchronously. A refused
    // request must leave no state behind.
    using var service = CreateService(
      gvMedia: new GvMediaOptions { Enabled = false, CacheDirectory = _cacheDir });

    var ex = await Assert.ThrowsAsync<GvMediaUnavailableException>(
      () => service.StartAsync(VoicemailRequest()));

    Assert.Equal(GvMediaFailure.Disabled, ex.Reason);
    Assert.Null(service.Current);
  }

  // ── duration honesty ────────────────────────────────────────────────────

  [Fact]
  public async Task AVoicemailWithDurationZeroReportsANullSnapshotDuration()
  {
    // ADR-029 §4.1: 0 means UNKNOWN, and the UI must render an indeterminate bar rather than a
    // confident lie. The SOURCE still gets an estimate — its completion needs a number — so the
    // two differing is the whole point and would otherwise look like a bug.
    using var service = CreateService(
      httpHandler: new StubHandler(_ => Mp3Of(320_000)));   // 20s at the factory's 16 000 B/s

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(VoicemailRequest(durationSeconds: 0));

    var final = await playing.WaitAsync(TimeSpan.FromSeconds(10));

    Assert.Null(final.Duration);
    var source = Assert.Single(_ducking.StartedSources);
    Assert.Equal(TimeSpan.FromSeconds(20), source.Duration);
  }

  [Fact]
  public async Task AVoicemailWithAReportedDurationUsesItRatherThanTheSizeEstimate()
  {
    using var service = CreateService(
      httpHandler: new StubHandler(_ => Mp3Of(320_000)));   // would estimate to 20s

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(VoicemailRequest(durationSeconds: 47));

    var final = await playing.WaitAsync(TimeSpan.FromSeconds(10));

    Assert.Equal(TimeSpan.FromSeconds(47), final.Duration);
    var source = Assert.Single(_ducking.StartedSources);
    Assert.Equal(TimeSpan.FromSeconds(47), source.Duration);
  }

  [Fact]
  public async Task TTSEventSourceDeclaresItsOwnPositionOverride_AndNothingHereProvesItMoves()
  {
    // ⚠ UPDATED, NOT DELETED. PHN-1c pinned the inverse of the first assertion deliberately
    // (C-27): TTSEventSource did not override Position, so it inherited EventAudioSourceBase's
    // TimeSpan.Zero for the whole playback, and adding the override was meant to red exactly this
    // line. PHN-2 added it, so the line now names TTSEventSource. Deleting it instead would have
    // erased the record that the behaviour changed.
    //
    // This reflection assertion is the ONLY load-bearing one in this test: it pins the DECLARATION,
    // and removing the override reds it.
    var positionGetter = typeof(TTSEventSource).GetProperty(nameof(IEventAudioSource.Position))!
      .GetGetMethod()!;
    Assert.Equal(typeof(TTSEventSource), positionGetter.DeclaringType);

    // ⚠ THE TWO ASSERTIONS BELOW ARE NOT A CHECK ON TTSEventSource AT ALL, and the earlier wording
    // here — "the override's null-conditional falls through", "proves the override is declared AND
    // REACHABLE" — was simply false. CreateService() with no arguments builds a FakeTtsFactory, which
    // hands EventPlaybackService a FakeEventSource; TTSEventSource is NEVER CONSTRUCTED on this path.
    // The zero comes from FakeEventSource.Position, an auto-property that starts at TimeSpan.Zero.
    // Mutating TTSEventSource.Position to `=> TimeSpan.Zero` leaves this whole test green.
    //
    // They are kept as what they actually are: a pin that the SERVICE reports whatever its source
    // reports, unmodified, for a Speech playback. No speech source in this harness reports a moving
    // position, and none is being added to chase it — a fake that returned a rising number would pin
    // the fake. A speech position that MOVES is unverified in this repo and reachable only on the
    // appliance (plan §2.2 item 2).

    using var service = CreateService();

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());

    var final = await playing.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(TimeSpan.Zero, final.PositionAtBroadcast);
    Assert.Equal(TimeSpan.Zero, service.Current!.PositionAtBroadcast);
  }

  // ── transport refusals ──────────────────────────────────────────────────

  [Fact]
  public async Task SeekIsRefusedForANonSeekableSource_WithoutThrowing()
  {
    // EventAudioSourceBase.SeekAsync throws NotSupportedException when IsSeekable is false. "This
    // cannot scrub" is an ordinary answer, so the seam pre-checks and returns false instead.
    var source = new FakeEventSource { IsSeekable = false };
    var tts = new FakeTtsFactory
    {
      OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source)
    };
    using var service = CreateService(ttsFactory: tts);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.False(await service.SeekAsync(accepted.Id, TimeSpan.FromSeconds(20)));
    Assert.Null(source.SoughtTo);

    source.IsSeekable = true;
    Assert.True(await service.SeekAsync(accepted.Id, TimeSpan.FromSeconds(20)));
    Assert.Equal(TimeSpan.FromSeconds(20), source.SoughtTo);

    // And an id that names nothing is a refusal too, not a throw.
    Assert.False(await service.SeekAsync("evp-nope", TimeSpan.FromSeconds(1)));
  }

  [Fact]
  public async Task ASeekPastTheEndIsRefused_RatherThanEscapingAsA500()
  {
    // ⚠ A seek past the end used to be a bare 500. The controller range-checks only for negative,
    // NaN and infinite, so a finite position beyond the content reaches
    // AudioFileEventSource.SeekCoreAsync, which throws ArgumentOutOfRangeException — and Radio.API
    // registers neither UseExceptionHandler nor AddProblemDetails, so nothing turned that into a
    // status code a caller could read.
    //
    // It is most reachable exactly where the scrubber is least trustworthy: when the provider
    // reported duration 0 (unknown), the snapshot carries null and the source's duration is a
    // size-based ESTIMATE, so the UI's idea of "the end" and the source's do not agree. The seam
    // catches it and answers false, which the route turns into the same clean 409 "NotSeekable" a
    // non-seekable source already gets.
    var source = new FakeEventSource { Duration = TimeSpan.FromSeconds(30) };
    var tts = new FakeTtsFactory
    {
      OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source)
    };
    using var service = CreateService(ttsFactory: tts);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.False(await service.SeekAsync(accepted.Id, TimeSpan.FromSeconds(45)));
    Assert.Null(source.SoughtTo);

    // And the playback is untouched by the refusal — it is still the current one, still playing.
    Assert.Equal(EventPlaybackState.Playing, service.Current!.State);
    Assert.Equal(accepted.Id, service.Current.Id);

    // A position inside the content still works, so the guard is a bound rather than a blanket.
    Assert.True(await service.SeekAsync(accepted.Id, TimeSpan.FromSeconds(29)));
    Assert.Equal(TimeSpan.FromSeconds(29), source.SoughtTo);
  }

  [Fact]
  public async Task PauseAndResumeAreRefusedFromTheWrongState()
  {
    // EventAudioSourceBase already no-ops these with a warning; the seam must not report success
    // for a no-op — that is the untruth PR 1 refused when it made a non-seekable SeekAsync throw.
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory
    {
      OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source)
    };
    using var service = CreateService(ttsFactory: tts);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.False(await service.ResumeAsync(accepted.Id));      // not paused
    Assert.True(await service.PauseAsync(accepted.Id));
    Assert.Equal(EventPlaybackState.Paused, service.Current!.State);

    Assert.False(await service.PauseAsync(accepted.Id));       // already paused
    Assert.True(await service.ResumeAsync(accepted.Id));
    Assert.Equal(EventPlaybackState.Playing, service.Current!.State);

    Assert.False(await service.PauseAsync("evp-nope"));
    Assert.False(await service.ResumeAsync("evp-nope"));
  }

  // ── the two structural pins ─────────────────────────────────────────────

  [Fact]
  public void NoMixerSourceIsEverAdded()
  {
    // §0.6 — the single most copy-able mistake in the arc. SoundFlowMasterMixer.AddSource mutates a
    // bookkeeping list and does NOT route audio; SourcesController calls it and never removes,
    // which is where its per-play leak comes from.
    //
    // ⚠ Asserted structurally rather than by recording a mixer, and the difference is worth being
    // exact about: this service takes no mixer at all, so there is no call to record. What this pins
    // is exactly one thing — no constructor parameter and no field, instance or static, can hold an
    // IMasterMixer. A grep over the file is the textual half.
    //
    // ⚠ What it does NOT pin, because the claim would be FALSE BY DESIGN: that no SoundFlow playback
    // service is reachable from here. This comment used to say "an IMasterMixer or a SoundFlow
    // playback service", and only the first half was ever asserted. AudioFileEventSourceFactory —
    // which IS a constructor parameter — holds a SoundFlowPlaybackService and hands it to every
    // AudioFileEventSource it builds; that is precisely how the RemoteMedia arm makes sound. The
    // invariant is about AddSource, which mutates bookkeeping and routes no audio, not about
    // SoundFlow being absent from the graph.
    var constructor = Assert.Single(typeof(EventPlaybackService).GetConstructors());

    Assert.DoesNotContain(
      constructor.GetParameters(), p => typeof(IMasterMixer).IsAssignableFrom(p.ParameterType));

    // Static as well as instance: a static field holding a mixer would defeat the whole check, and
    // a `static readonly IMasterMixer` is exactly the kind of thing a hurried fix reaches for.
    var fields = typeof(EventPlaybackService).GetFields(
      System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
      | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

    Assert.DoesNotContain(fields, f => typeof(IMasterMixer).IsAssignableFrom(f.FieldType));
  }

  [Fact]
  public async Task NeitherTheTextNorTheRawMediaIdReachesAnyLogLineThisSeamWrites()
  {
    // The masking rule, extended to Text: for the Speech arm the payload is an SMS body, which is
    // private content by exactly the standard the media-id rule protects. The loggers captured here
    // are EventPlaybackService, GvMediaClient, GvMediaCache, AudioFileEventSourceFactory and
    // AudioFileEventSource, at every level, across a successful speech playback, a successful
    // voicemail playback and a failed one.
    //
    // ⚠ THE NAME SAYS "THIS SEAM WRITES" BECAUSE THE CHAIN IS DELIBERATELY INCOMPLETE. The Speech
    // arm runs on FakeTtsFactory, so the REAL TTSFactory and TTSEventSource are never in the chain
    // — and both of them DO log the utterance, on the path this PR ships:
    //
    //   TTSFactory.cs:99       LogInformation("Creating TTS audio for text: '{Text}' with engine
    //                          {Engine}", ...)    — the first 50 characters
    //   TTSEventSource.cs:92   LogInformation("TTS event source initialized: {Text}", _text)
    //                          — the WHOLE string
    //   TTSEventSource.cs:107  LogDebug("Playing TTS audio: {Text}", _text)
    //
    // Since LOG-11 an Information line no longer reaches the journal but DOES reach the file sink,
    // so on the appliance a private SMS body ends up at rest in /opt/radio-console/logs/. That is a
    // real residual and it is filed as design/FUTURE-WORK.md § "TTS seam" item 5; the fix belongs to
    // TTSFactory and TTSEventSource, two live shared paths this PR may not touch. What this test
    // pins is the rule the PHN-1c plan actually scoped: EventPlaybackService and
    // EventPlaybackController never log request.Text. Do NOT widen the name back without first
    // widening the chain to a real ITTSFactory.
    var logs = new CapturingLoggerProvider();

    using (var speech = CreateService(logs: logs))
    {
      var playing = NextSnapshotWith(speech, EventPlaybackState.Playing);
      await speech.StartAsync(SpeechRequest());
      await playing.WaitAsync(TimeSpan.FromSeconds(5));
    }

    using (var voicemail = CreateService(
      httpHandler: new StubHandler(_ => Mp3Of(320_000)), logs: logs))
    {
      var playing = NextSnapshotWith(voicemail, EventPlaybackState.Playing);
      await voicemail.StartAsync(VoicemailRequest());
      await playing.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // ⚠ Its OWN cache directory. The block above materialised this same recording, and
    // GvMediaCache would serve it straight back from disk — which is the cache working, and would
    // silently turn this third block into a fourth successful playback.
    using (var failing = CreateService(
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = Path.Combine(_root, "gvmedia-miss")
      },
      httpHandler: new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)),
      logs: logs))
    {
      var failed = NextSnapshotWith(failing, EventPlaybackState.Failed);
      await failing.StartAsync(VoicemailRequest());
      await failed.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ⚠ Without this the whole test passes vacuously against a service that logs nothing at all.
    Assert.NotEmpty(logs.Messages);

    foreach (var message in logs.Messages)
    {
      Assert.DoesNotContain(RawMediaId, message, StringComparison.Ordinal);
      Assert.DoesNotContain(PrivateUtterance, message, StringComparison.Ordinal);
    }

    // And the masked form IS present, so "no raw id" is not achieved by logging nothing about it.
    Assert.Contains(logs.Messages, m => m.Contains(GvMediaCache.MaskFor(RawMediaId), StringComparison.Ordinal));
  }

  // ── ADR-029 D5: priority is load-bearing (PHN-1d) ─────────────────────

  [Fact]
  public async Task ASourceStartingAtTheThresholdStopsAttendedPlayback()
  {
    // The row's whole point. With PhoneIntegration:Enabled false, the live instance of this is a
    // doorbell posted to /api/notifications/announce at its default priority 8 (ADR-029 §6.1).
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var stopped = NextSnapshotWith(service, EventPlaybackState.Stopped);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    ducking.RaiseStarted(new FakeEventSource(), 8);
    await service.PreemptionTail;

    var final = await stopped.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(accepted.Id, final.Id);
    // ⚠ NOT Assert.Null(service.Current). PHN-1c's review changed that contract: a terminal snapshot
    // is RETAINED so a client that re-attaches after the fact can still see how the playback ended.
    Assert.Equal(EventPlaybackState.Stopped, service.Current!.State);
    Assert.Equal(1, source.StopCalls);
    Assert.Contains(source.Id, ducking.Stopped);
  }

  [Fact]
  public async Task ASourceBelowTheThresholdDoesNotStopAttendedPlayback()
  {
    // ADR-029 §6.2 rule 3, pinned: sub-threshold events keep MIXING. Recorded so the next reader does
    // not mistake it for an oversight — a Home Assistant announcement at priority 5 talks over a
    // voicemail, and fixing that means a queue across every IAnnouncementService caller.
    //
    // ⚠ There is no snapshot to await here, so without a rendezvous the only options would be a sleep
    // or a poll, and starvation would make either pass for the wrong reason. What makes it safe is that
    // the whole decision happens SYNCHRONOUSLY inside RaiseStarted: by the time that call returns, the
    // handler has already declined.
    //
    // ⚠ The await below is therefore a no-op in this test as the code stands today — PreemptionTail is
    // Task.CompletedTask throughout, because a sub-threshold decision dispatches nothing. It is kept
    // deliberately as insurance: if the decision ever moves onto the dispatched task, this becomes a
    // real rendezvous and the test stays correct instead of becoming a race. Do not read it as the
    // thing currently providing determinism.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    ducking.RaiseStarted(new FakeEventSource(), 5);
    await service.PreemptionTail;

    Assert.Equal(accepted.Id, service.Current?.Id);
    Assert.Equal(EventPlaybackState.Playing, service.Current?.State);
    Assert.Equal(0, source.StopCalls);
  }

  [Fact]
  public async Task AnAttendedPlaybackAtPriorityEightDoesNotStopItself()
  {
    // EventPlaybackRequest.Priority accepts 1-10 and StartDuckingAsync now raises for the ATTENDED
    // source too, so without the identity check a caller posting Priority 8 would preempt itself the
    // instant it started ducking — reaching Playing and immediately reporting Stopped.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest() with { Priority = 8 });
    await playing.WaitAsync(TimeSpan.FromSeconds(5));
    await service.PreemptionTail;

    Assert.Equal(accepted.Id, service.Current?.Id);
    Assert.Equal(EventPlaybackState.Playing, service.Current?.State);
    Assert.Equal(0, source.StopCalls);
  }

  [Fact]
  public async Task AStoppingSourceNeverPreempts()
  {
    // The sharpest trap in this PR, and since PHN-1f exactly ONE thing stops it.
    // DuckingService.StopDuckingAsync deletes the source's priority entry before it raises, and
    // RaiseSetEmptied reproduces that: its args carry DuckingService.DefaultEventPriority (8), which
    // is AT the threshold. So the threshold test does not reject these args, and neither does the
    // identity check — the trigger is a foreign announcement, not our source. What rejects them is
    // rule 1, `e.Transition != DuckingSourceTransition.Started`: a source LEAVING is not a source
    // starting. Without it, the voicemail is stopped every time something else finishes talking.
    //
    // ⚠ TWO MECHANISMS THIS COMMENT USED TO NAME ARE GONE, and must not be looked for. There is no
    // "IsDucking filter" — OnDuckingStateChanged has not read that field since PHN-1f — and there is
    // no late priority resolution, because the priority travels on the args.
    //
    // MUTATION: delete `e.Transition != DuckingSourceTransition.Started` from the handler's guard and
    // this reds: the ending announcement reads as a priority-8 start, and StopCalls becomes 1.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    // A foreign announcement that started at priority 3 and has now ended: its entry is gone, so both
    // GetPriority and the Ended args answer 8 for it. Only rule 1 stops this being a preemption.
    var announcement = new FakeEventSource();
    ducking.RaiseStarted(announcement, 3);
    await service.PreemptionTail;
    ducking.RaiseSetEmptied(announcement);
    await service.PreemptionTail;

    Assert.Equal(DuckingService.DefaultEventPriority, ducking.GetPriority(announcement));
    Assert.Equal(accepted.Id, service.Current?.Id);
    Assert.Equal(EventPlaybackState.Playing, service.Current?.State);
    Assert.Equal(0, source.StopCalls);
  }

  [Fact]
  public async Task AStopAllRaiseWithNoTriggeringSourceIsIgnored()
  {
    // StopAllDuckingAsync raises Transition AllCleared with a NULL TriggeringSource, IsDucking false
    // and priority 0. It has no non-test callers today and this PR does not give it one; the shape is
    // pinned because the event's contract permits it, not because a caller does it.
    //
    // ⚠ THIS TEST CANNOT FAIL UNDER ANY SINGLE-GUARD MUTATION, and that is stated plainly because the
    // repo's worst recent defect was a test that could not fail. An earlier revision of this comment
    // named the wrong guards for it — it spoke of "!e.IsDucking", which OnDuckingStateChanged has not
    // read since PHN-1f. Traced against the handler as it now stands, THREE separate things turn these
    // args away and each is sufficient alone:
    //
    //   • `e.Transition != DuckingSourceTransition.Started` — AllCleared is not Started;
    //   • `e.TriggeringSource is not { } trigger`           — it is null;
    //   • `priority < threshold`                            — these args carry 0, and 0 < 8.
    //
    // Measured: removing the first two together still leaves this GREEN (the threshold test returns);
    // removing all three reds it, as a NullReferenceException on trigger.Id. The redundancy is
    // threefold, not the twofold this comment used to claim.
    //
    // So this is a SHAPE-DOCUMENTATION test, not a guard test: it records what StopAllDuckingAsync
    // actually emits and that the seam survives it. The guards are isolated elsewhere — rule 1 by
    // AStoppingSourceNeverPreempts, the null pattern by AStartRaiseWithNoTriggeringSourceIsIgnored
    // below, each of which reds under its own single mutation.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    ducking.RaiseStopAll();
    await service.PreemptionTail;

    Assert.Equal(accepted.Id, service.Current?.Id);
    Assert.Equal(0, source.StopCalls);
  }

  [Fact]
  public async Task AStartRaiseWithNoTriggeringSourceIsIgnored()
  {
    // The one shape that isolates the null half of the guard: a Started transition with a null
    // TriggeringSource, which rule 1 does not filter. Without the pattern match this is a
    // NullReferenceException on the raising thread — which, inside DuckingService, is the swallowed
    // announcement that POST /api/notifications/announce still reports as 200.
    //
    // ⚠ THE PRIORITY ON THESE ARGS IS DELIBERATELY DuckingService.DefaultEventPriority (8), NOT 0, and
    // that is the whole difference between this test having teeth and not. Task 6f moved the priority
    // onto the args, so with 0 the handler returns at `priority < threshold` BEFORE it ever
    // dereferences the trigger — the null pattern is never reached, and this test was green with the
    // pattern deleted. At 8 >= 8 the pattern is the only thing standing between these args and
    // trigger.Id.
    //
    // ⚠ No producer in this tree emits these args. DuckingService.StartDuckingAsync takes a non-null
    // source (ArgumentNullException.ThrowIfNull) and passes it straight through, so today the guard is
    // defensive against the event CONTRACT rather than against a caller. It is tested here because
    // DuckingStateChangedEventArgs.TriggeringSource is declared nullable, and a subscriber that
    // assumes otherwise is one producer away from a live fault. Driven through the args directly for
    // exactly that reason: the handler's contract is about the args, not about how they arose.
    //
    // MUTATION: replace the guard's `e.TriggeringSource is not { } trigger` with
    // `var trigger = e.TriggeringSource!;` and this reds with a NullReferenceException.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    // Raised the way DuckingService raises — unguarded — so an escaping NullReferenceException fails
    // this test rather than being absorbed by the fake.
    ducking.RaiseStartedWithNoSource();
    await service.PreemptionTail;

    Assert.Equal(accepted.Id, service.Current?.Id);
    Assert.Equal(EventPlaybackState.Playing, service.Current?.State);
    Assert.Equal(0, source.StopCalls);
  }

  [Fact]
  public async Task PreemptionIsDispatched_TheRaisingThreadIsNotHeldForTheTeardown()
  {
    // This handler runs on the thread inside DuckingService.StartDuckingAsync — on the live path
    // AnnouncementService's, mid-announcement — and StopAsync takes _gate, which this service is
    // already holding whenever the raise came out of ReleaseSourceAsync. A handler that awaited the
    // stop would deadlock a non-reentrant semaphore there, and everywhere else would block the
    // doorbell for the length of a voicemail teardown.
    //
    // Parking StopDuckingAsync makes the teardown observably incomplete: if RaiseStarted returns at all
    // while PreemptionTail is still pending, the stop was dispatched rather than awaited. (If it were
    // awaited, this test would hang rather than fail — which is the correct signal for a deadlock.)
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    ducking.StopGate = release;

    // ⚠ Bounded rather than called inline. RaiseStarted blocks until the handler returns, so a
    // regression that awaits the stop would park HERE forever — and xunit applies no default timeout,
    // so the observable result on the self-hosted runner would be a wedged job rather than a red test
    // with a name attached. This converts the deadlock into a failure. Starvation can only make this
    // wait longer, never make it pass wrongly.
    await Task.Run(() => ducking.RaiseStarted(new FakeEventSource(), 9))
      .WaitAsync(TimeSpan.FromSeconds(15));

    Assert.False(service.PreemptionTail.IsCompleted);
    Assert.Equal(accepted.Id, service.Current?.Id);

    release.SetResult();
    await service.PreemptionTail;

    Assert.Equal(EventPlaybackState.Stopped, service.Current!.State);
  }

  [Fact]
  public async Task PreemptingAPlaybackThatAlreadyEndedChangesNothing()
  {
    // Idempotence through Playback.ClaimTerminal, from the direction PR 4 introduces. A preemption
    // arriving just after a natural end must not overwrite Completed with Stopped, must not publish a
    // second terminal snapshot and must not, from PR 5, broadcast a transition that did not happen.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var terminals = new List<EventPlaybackSnapshot>();
    service.PlaybackChanged += (_, s) =>
    {
      if (s.State is EventPlaybackState.Completed or EventPlaybackState.Stopped
          or EventPlaybackState.Failed)
      {
        lock (terminals) { terminals.Add(s); }
      }
    };

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    source.RaiseCompleted(PlaybackCompletionReason.EndOfContent);
    await WaitUntilAsync(
      () => service.Current?.State == EventPlaybackState.Completed, TimeSpan.FromSeconds(5));

    ducking.RaiseStarted(new FakeEventSource(), 9);
    await service.PreemptionTail;

    lock (terminals)
    {
      Assert.Single(terminals);
      Assert.Equal(EventPlaybackState.Completed, terminals[0].State);
    }
  }

  [Fact]
  public async Task PreemptingAPreparingPlaybackCancelsAcquisitionAndDisposesWhatItAcquired()
  {
    // A preemption during Preparing must cancel the acquisition, and the source the acquisition then
    // produces must be DISPOSED — ReleaseSourceAsync can only release a source the playback already
    // adopted, so the acquisition tail is the only thing that can release this one. On the RemoteMedia
    // arm that is an open FileStream over a cached recording, which on Windows would also stop
    // GvMediaCache ever evicting the file. And it must never reach PlayAsync: audio started here would
    // have no playbackId, so nothing could ever stop it.
    var ducking = new FakeDuckingService();
    var acquired = new FakeEventSource();
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var tts = new FakeTtsFactory
    {
      OnCreate = async (_, _, _) =>
      {
        await release.Task;                 // deliberately NOT observing the token: the point is that
        return (IEventAudioSource)acquired; // the tail must cope with a source arriving after the stop.
      }
    };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var accepted = await service.StartAsync(SpeechRequest());
    Assert.Equal(EventPlaybackState.Preparing, accepted.State);

    ducking.RaiseStarted(new FakeEventSource(), 9);
    await service.PreemptionTail;

    release.SetResult();
    await WaitUntilAsync(() => acquired.DisposeCalls == 1, TimeSpan.FromSeconds(5));

    Assert.Equal(0, acquired.PlayCalls);
    Assert.Equal(EventPlaybackState.Stopped, service.Current!.State);
    Assert.Equal(accepted.Id, service.Current!.Id);
  }

  [Fact]
  public async Task APlaybackStartedUnderAHigherPrioritySourceWaitsAndThenPlays()
  {
    // ⚠ THIS WAS APlaybackStartedUnderAHigherPrioritySourceStillMixes_TODAY, and it is the one test in
    // this file that was written to be changed rather than kept. PHN-1d pinned today's mixing so that
    // D28's queue would arrive as an edited assertion. This is that edit.
    //
    // ⚠ WHAT CARRIES THE DECISION IS A PAIR, AND IT IS TAKEN AT THE WAITING CHECKPOINT — not at the
    // end: source.PlayCalls == 0 AND ducking.ActiveEventCount == 1 while the blocker is STILL IN THE
    // SET. One voice in the room, and it is the blocker's; the attended source is demonstrably absent
    // from the ducking set, which is what "the two voices no longer overlap" actually means here.
    //
    // ⚠ The final ActiveEventCount assertion is NOT the decision, and an earlier revision of this
    // comment said it was. The blocker is removed two statements before it, so 1 is the count in the
    // waits-world AND in the mixes-world (1 → 2 → 1) — it cannot fail. Its predecessor asserted
    // Equal(2, …) WHILE BOTH WERE LIVE, which is what made that one load-bearing. It is kept below as
    // an end-state check and claims nothing more than that.
    //
    // MUTATION (§2.1): delete the WaitForClearAirAsync call in AcquireAndPlayAsync and this reds on
    // the first rendezvous — the playback reaches Playing immediately, so no Waiting snapshot is ever
    // published and its 5 s bound fires.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    // A doorbell announcement is already sounding at priority 8.
    var blocker = new FakeEventSource();
    ducking.RaiseStarted(blocker, 8);

    // The blocker really is above the threshold, so this test is about the RULE and not about a
    // mis-configured fixture. Read BEFORE the stop below deletes the override.
    Assert.True(ducking.GetPriority(blocker) >= new GvMediaOptions().PreemptAtPriority);

    var waiting = NextSnapshotWith(service, EventPlaybackState.Waiting);
    var accepted = await service.StartAsync(SpeechRequest());
    var waited = await waiting.WaitAsync(TimeSpan.FromSeconds(5));

    // It is WAITING, and it has not made a sound.
    Assert.Equal(accepted.Id, waited.Id);
    Assert.Equal(EventPlaybackState.Waiting, waited.State);
    Assert.Equal(EventPlaybackState.Waiting, service.Current?.State);

    // ⭐ THE PAIR THAT CARRIES THE DECISION, taken while the blocker is still in the set: no audio,
    // and exactly ONE source ducking — the blocker's. The attended source has not joined it, because
    // the wait happens before TryAdopt and before StartDuckingAsync.
    Assert.Equal(0, source.PlayCalls);
    Assert.Equal(1, ducking.ActiveEventCount);

    // The doorbell finishes.
    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await ducking.StopDuckingAsync(blocker);
    var final = await playing.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(accepted.Id, final.Id);
    Assert.Equal(EventPlaybackState.Playing, final.State);
    Assert.Equal(1, source.PlayCalls);

    // End state: the attended source is the only thing ducking now. Kept because it is true and cheap,
    // NOT because it can fail — see the header. The blocker left two statements ago, so 1 is also what
    // a seam that had mixed would report by this point.
    Assert.Equal(1, ducking.ActiveEventCount);
  }

  [Fact]
  public async Task AWaitingPlaybackIsReportedByCurrent()
  {
    // §0.2: a waiting playback IS _current, in a new state — there is no pending slot. That is what
    // makes GET /api/audio/events/current carry it with no controller change at all (ADR-029 §8.1's
    // re-attach path), and what makes StopAsync and the replacement arm resolve it for free.
    //
    // MUTATION (§2.1, shared with the test above): delete the WaitForClearAirAsync call and no Waiting
    // snapshot is ever published, so the rendezvous times out.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    ducking.RaiseStarted(new FakeEventSource(), 8);

    var waiting = NextSnapshotWith(service, EventPlaybackState.Waiting);
    var accepted = await service.StartAsync(SpeechRequest());
    await waiting.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(accepted.Id, service.Current?.Id);
    Assert.Equal(EventPlaybackState.Waiting, service.Current?.State);
    Assert.Null(service.Current?.FailureReason);

    // C-67: a waiting SPEECH playback reports a null duration, because playback.Source is null until
    // TryAdopt and there is no other estimate. PHN-1e §0.6 item 2 already requires a client to render
    // that as indeterminate rather than zero. SnapshotOf is unchanged by this row.
    Assert.Null(service.Current?.Duration);
    Assert.Equal(TimeSpan.Zero, service.Current?.PositionAtBroadcast);
  }

  [Fact]
  public async Task StopAsyncResolvesAWaitingPlayback_AndDisposesWhatItAcquired()
  {
    // Two claims in one test because they are the same mechanism. StopAsync resolves _current by id
    // and the waiting playback IS _current, so the stop needs no new code — and TearDownAsync cancels
    // the token, which is what unblocks the parked waiter as an OperationCanceledException.
    //
    // ⚠ THE DISPOSE ASSERTION IS C-57 AND IT IS THE ONE THAT CAN FAIL ALONE. Nothing has been adopted,
    // so TearDownAsync and FailAsync both reach ClaimSourceForRelease, which answers null — the source
    // can only be released by AcquireAndPlayAsync's own guard around the wait.
    //
    // MUTATION (§2.1): delete that guard's DisposeOrphanAsync calls and DisposeCalls stays 0 while
    // every other assertion here still passes.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    ducking.RaiseStarted(new FakeEventSource(), 8);

    var waiting = NextSnapshotWith(service, EventPlaybackState.Waiting);
    var accepted = await service.StartAsync(SpeechRequest());
    await waiting.WaitAsync(TimeSpan.FromSeconds(5));

    var stopped = NextSnapshotWith(service, EventPlaybackState.Stopped);
    Assert.True(await service.StopAsync(accepted.Id));

    var final = await stopped.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(accepted.Id, final.Id);
    Assert.Equal(EventPlaybackState.Stopped, final.State);
    Assert.Equal(0, source.PlayCalls);

    // The acquisition task unwinds on its own thread, so the release is awaited rather than assumed.
    await WaitUntilAsync(() => source.DisposeCalls == 1, TimeSpan.FromSeconds(5));
  }

  [Fact]
  public async Task ASecondStartReplacesAWaitingPlayback()
  {
    // §0.2: replace semantics come free, because StartAsync's replacement arm tears down whatever is
    // in the slot without asking what state it is in. D28 is one deep.
    //
    // MUTATION (§2.1): make the replacement arm skip a playback whose Source is null — which is every
    // waiting playback — and the first one is never stopped, never disposed, and stays _current.
    var ducking = new FakeDuckingService();
    var first = new FakeEventSource();
    var second = new FakeEventSource();
    var queue = new Queue<IEventAudioSource>([first, second]);
    var tts = new FakeTtsFactory
    {
      OnCreate = (_, _, _) =>
      {
        lock (queue) { return Task.FromResult(queue.Dequeue()); }
      }
    };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    ducking.RaiseStarted(new FakeEventSource(), 8);

    var firstWaiting = NextSnapshotWith(service, EventPlaybackState.Waiting);
    var one = await service.StartAsync(SpeechRequest());
    await firstWaiting.WaitAsync(TimeSpan.FromSeconds(5));

    var replaced = NextSnapshotMatching(
      service, s => s.Id == one.Id && s.State == EventPlaybackState.Stopped);
    var two = await service.StartAsync(SpeechRequest());
    await replaced.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.NotEqual(one.Id, two.Id);
    Assert.Equal(two.Id, service.Current?.Id);
    Assert.Equal(0, first.PlayCalls);
    await WaitUntilAsync(() => first.DisposeCalls == 1, TimeSpan.FromSeconds(5));
  }

  [Fact]
  public async Task AWaitingPlaybackExpiresAsFailedWaitExpired()
  {
    // D28's staleness bound. GvMedia:MaxQueuedWaitSeconds has no "off": a 0 clamps to 1, because a 0
    // meaning "never wait" would resolve to mixing, which is the option D28 rejected.
    //
    // MUTATION (§2.1): drop the timeout argument from waiter.Task.WaitAsync and the wait never ends —
    // the 5 s bound below turns the hang into a red. Separately, delete the wait guard's
    // DisposeOrphanAsync calls and the DisposeCalls assertion reds alone (C-57).
    var time = new FakeTimeProvider();
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(
      ttsFactory: tts, ducking: ducking, timeProvider: time,
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, MaxQueuedWaitSeconds = 30
      });

    ducking.RaiseStarted(new FakeEventSource(), 8);

    // ⚠ The rendezvous is the WAITING SNAPSHOT, not a delay — but it is a PROXY FOR THE ARM RATHER
    // THAN THE ARM, and saying so is the point. WaitForClearAirAsync publishes Waiting STRICTLY
    // BEFORE it reaches waiter.Task.WaitAsync, so a single Advance taken on this rendezvous can land
    // before the timer exists, advance past nothing, and leave the test hanging on the Failed
    // snapshot — a race that reads as an unrelated timeout rather than as what it is.
    //
    // So the clock is advanced in a BOUNDED LOOP until the Failed task completes. FakeTimeProvider
    // fires every DUE timer synchronously inside Advance, so the first advance that lands after the
    // arm produces the expiry and the ones before it are no-ops. Still no elapsed-time assertion:
    // WaitUntilAsync polls inside a bound, exactly as the rest of this file does.
    var waiting = NextSnapshotWith(service, EventPlaybackState.Waiting);
    await service.StartAsync(SpeechRequest());
    await waiting.WaitAsync(TimeSpan.FromSeconds(5));

    var failed = NextSnapshotWith(service, EventPlaybackState.Failed);
    await WaitUntilAsync(
      () =>
      {
        time.Advance(TimeSpan.FromSeconds(30));
        return failed.IsCompleted;
      },
      TimeSpan.FromSeconds(5));

    var final = await failed;

    Assert.Equal(EventPlaybackState.Failed, final.State);
    Assert.Equal("WaitExpired", final.FailureReason);
    Assert.Equal(0, source.PlayCalls);
    // C-57: the acquired source was disposed rather than leaked.
    Assert.Equal(1, source.DisposeCalls);
  }

  [Fact]
  public async Task AHigherPrioritySourceEndingWhileALowerOneContinuesStillWakesTheQueue()
  {
    // ⭐ THE STARVATION CASE, and the whole reason DuckingStateChangedEventArgs gained a Transition
    // field. Before PHN-1f, StopDuckingAsync raised ONLY when the set emptied — so a priority-8 blocker
    // ending while a priority-3 announcement kept ducking produced NO RAISE AT ALL, this wake never
    // ran, and the playback expired as Failed/"WaitExpired" thirty seconds later: D28's rejected option
    // delivered late.
    //
    // MUTATION (§2.1): revert FakeDuckingService.StopDuckingAsync's raise to `if (remaining == 0)` —
    // the pre-PHN-1f rule — or revert DuckingService's, and this hangs on the Playing rendezvous.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var blocker = new FakeEventSource();
    var quiet = new FakeEventSource();
    ducking.RaiseStarted(blocker, 8);
    ducking.RaiseStarted(quiet, 3);

    var waiting = NextSnapshotWith(service, EventPlaybackState.Waiting);
    var accepted = await service.StartAsync(SpeechRequest());
    await waiting.WaitAsync(TimeSpan.FromSeconds(5));

    // The blocker leaves. The set does NOT empty — the priority-3 announcement is still ducking.
    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    ducking.RaiseEndedWithOthersRemaining(blocker);

    var final = await playing.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(accepted.Id, final.Id);
    Assert.Equal(1, source.PlayCalls);

    // And the set really was non-empty across the raise, so this is the starvation case and not the
    // emptying one wearing its name.
    Assert.Contains(quiet, ducking.GetActiveEventsByPriority());
  }

  [Fact]
  public async Task AWaitingPlaybackIsNotWokenByASubThresholdSourceEnding()
  {
    // The wake is a STATE re-evaluation, not an edge: it asks the same predicate that decided to wait
    // whether the air is clear NOW. A sub-threshold source leaving while the real blocker is still
    // sounding must therefore change nothing.
    //
    // ⚠ TIMING DECLARATION (CLAUDE.md § Test Timing). The negative half is a BOUNDED NEGATIVE — "no
    // Playing snapshot arrived within 500 ms" — so starvation can only make it pass more easily, never
    // flip a pass to a fail. That is the safe direction. The positive half that follows is what makes
    // the negative half mean something: the same playback DOES wake once the real blocker leaves, so
    // the 500 ms window was not merely too short for anything at all to happen.
    //
    // MUTATION (§2.1): make TryWakeWaitingPlayback wake unconditionally instead of re-evaluating the
    // predicate, and the ThrowsAsync below finds a completed task instead of a timeout.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var blocker = new FakeEventSource();
    var quiet = new FakeEventSource();
    ducking.RaiseStarted(blocker, 8);
    ducking.RaiseStarted(quiet, 3);

    var waiting = NextSnapshotWith(service, EventPlaybackState.Waiting);
    await service.StartAsync(SpeechRequest());
    await waiting.WaitAsync(TimeSpan.FromSeconds(5));

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    ducking.RaiseEndedWithOthersRemaining(quiet);

    await Assert.ThrowsAsync<TimeoutException>(
      () => playing.WaitAsync(TimeSpan.FromMilliseconds(500)));
    Assert.Equal(0, source.PlayCalls);
    Assert.Equal(EventPlaybackState.Waiting, service.Current?.State);

    // …and the same playback wakes the moment the source that actually blocks it leaves.
    var wokenPlaying = NextSnapshotWith(service, EventPlaybackState.Playing);
    ducking.RaiseEndedWithOthersRemaining(blocker);
    await wokenPlaying.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(1, source.PlayCalls);
  }

  [Fact]
  public async Task ASecondBlockerStartingDuringAWaitDoesNotStopTheWaitingPlayback()
  {
    // ⭐ ADR-029 §6.2 rule 2 stops an IN-FLIGHT attended playback, and a WAITING one is not in flight —
    // IEventPlaybackService.Current's own remark says so. Without the `victim.IsWaiting` clause in
    // OnDuckingStateChanged the guard falls straight through, because a waiting playback's Source is
    // null for the whole of the wait (_source is assigned only in TryAdopt, after the wait and after
    // _gate), so ReferenceEquals(null, trigger) is false and a real StopAsync is dispatched.
    //
    // The user-visible failure that closes: press play behind a doorbell, watch Waiting, and the very
    // next announcement destroys the queued playback — Waiting → Stopped, no sound, no reason given.
    // 8 is BOTH DuckingService.DefaultEventPriority and the shipped GvMedia:PreemptAtPriority, so
    // "the very next announcement" means every announcement that names no priority.
    //
    // ⚠ TIMING DECLARATION (CLAUDE.md § Test Timing). The middle assertion is a BOUNDED NEGATIVE —
    // "no Playing snapshot arrived within 500 ms" — so starvation can only make it pass more easily,
    // never flip a pass to a fail. The positive half after it is what stops that window being
    // meaningless: the same playback DOES sound once the second blocker leaves.
    //
    // MUTATION (§2.1): delete `victim.IsWaiting` from OnDuckingStateChanged's guard and this reds at
    // the first block, with the playback Stopped rather than Waiting.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var first = new FakeEventSource();
    ducking.RaiseStarted(first, 8);

    var waiting = NextSnapshotWith(service, EventPlaybackState.Waiting);
    var accepted = await service.StartAsync(SpeechRequest());
    await waiting.WaitAsync(TimeSpan.FromSeconds(5));

    // A SECOND source starts at the threshold while the first is still sounding. PreemptionTail is the
    // rendezvous: OnDuckingStateChanged decides on the raising thread and assigns the tail before
    // RaiseStarted returns, so awaiting it here covers the dispatch a regression would make.
    var second = new FakeEventSource();
    ducking.RaiseStarted(second, 8);
    await service.PreemptionTail;

    Assert.Equal(accepted.Id, service.Current?.Id);
    Assert.Equal(EventPlaybackState.Waiting, service.Current?.State);
    Assert.Equal(0, source.PlayCalls);
    Assert.Equal(0, source.StopCalls);

    // …and it is now waiting for BOTH. Ending only the first changes nothing, because the wake is a
    // state re-evaluation against the same predicate and the second blocker still fails it.
    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    ducking.RaiseEndedWithOthersRemaining(first);
    await Assert.ThrowsAsync<TimeoutException>(
      () => playing.WaitAsync(TimeSpan.FromMilliseconds(500)));
    Assert.Equal(EventPlaybackState.Waiting, service.Current?.State);
    Assert.Equal(0, source.PlayCalls);

    // Ending the second clears the air, and the playback the preemption would have destroyed sounds.
    ducking.RaiseEndedWithOthersRemaining(second);
    var final = await playing.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(accepted.Id, final.Id);
    Assert.Equal(1, source.PlayCalls);
  }

  [Fact]
  public async Task APlaybackAtPriorityEightDoesNotBlockItself()
  {
    // IsBlockedByAHigherPrioritySource deliberately writes NO exclusion for the attended source, and
    // this is what says the exclusion is unnecessary rather than merely absent: the predicate is only
    // ever evaluated before StartDuckingAsync, so our own source is not in the set when it is asked.
    // A guard for a state that cannot occur reads as evidence that it can.
    //
    // MUTATION (§2.1) — and it is the REVERSE of the obvious one: move WaitForClearAirAsync BELOW
    // StartDuckingAsync and this playback blocks on itself until WaitExpired, so the Playing
    // rendezvous times out. Adding a self-exclusion to the predicate leaves it green, which is the
    // point.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest() with { Priority = 8 });
    var final = await playing.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(accepted.Id, final.Id);
    Assert.Equal(1, source.PlayCalls);

    // It really did claim 8 — at or above the threshold — so the predicate WOULD have blocked on it
    // had it been asked after ducking started.
    Assert.Equal(8, ducking.GetPriority(source));
    Assert.True(8 >= new GvMediaOptions().PreemptAtPriority);
  }

  [Fact]
  public async Task AQuietRoomPublishesNoWaitingSnapshotAtAll()
  {
    // §0.6: the overwhelmingly common case must cost one walk of an empty list and ZERO extra messages
    // on the wire. Trap 5 is about churn on an N100, and a queue that broadcast a Waiting nobody
    // waited for would be churn with a straight face.
    //
    // MUTATION (§2.1): publish Waiting before the predicate in WaitForClearAirAsync and this reds.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var seen = new List<EventPlaybackState>();
    service.PlaybackChanged += (_, s) => { lock (seen) { seen.Add(s.State); } };

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    lock (seen)
    {
      Assert.Equal(new[] { EventPlaybackState.Preparing, EventPlaybackState.Playing }, seen);
    }
  }

  [Fact]
  public async Task StopAllDuckingWakesAWaitingPlayback()
  {
    // StopAllDuckingAsync raises with a NULL TriggeringSource and clears the whole set, so it is one of
    // the strongest reasons a wait should end — and the one shape a wake wired below the null check
    // would miss entirely.
    //
    // MUTATION (§2.1): move TryWakeWaitingPlayback() below the `e.TriggeringSource is not { } trigger`
    // test in OnDuckingStateChanged and this hangs on the Playing rendezvous.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    ducking.RaiseStarted(new FakeEventSource(), 8);

    var waiting = NextSnapshotWith(service, EventPlaybackState.Waiting);
    var accepted = await service.StartAsync(SpeechRequest());
    await waiting.WaitAsync(TimeSpan.FromSeconds(5));

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    ducking.RaiseStopAll();

    var final = await playing.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(accepted.Id, final.Id);
    Assert.Equal(1, source.PlayCalls);
  }

  [Fact]
  public async Task AWaitIsNotMissedWhenTheBlockerEndsWhileTheWaiterIsBeingArmed()
  {
    // ⚠ SAY WHAT THIS DOES AND DOES NOT PROVE. There is no rendezvous INSIDE WaitForClearAirAsync, so
    // this cannot deterministically place the blocker's end between BeginWait and the re-check. It is
    // a REPETITION test: N runs, so the interleaving is sampled rather than forced.
    //
    // ⚠ THE RENDEZVOUS IS AT ACQUISITION, and it is the only thing that makes the sampling worth
    // doing. FakeTtsFactory.OnCreate signals before it hands the source back, so the test resumes with
    // the acquisition switch about to return — a few statements from WaitForClearAirAsync's first
    // predicate check, rather than an unbounded distance away. WITHOUT it the test stopped the blocker
    // straight after StartAsync returned, and StartAsync returns as soon as it has QUEUED
    // Task.Run(AcquireAndPlayAsync) — so most iterations never reached even the first predicate check
    // and the loop sampled thread-pool scheduling latency instead of the window it names.
    //
    // ⛔ IT IS STILL A SAMPLER, NOT A PROOF, and the plan records it as a gap (§2.2 item 1). A run that
    // passes has not shown the window is closed — it has shown it was not hit. The re-check is
    // justified by C-66's argument, not by this test. Making it a proof means a test-only hook inside
    // the production wait, which is a bigger change to that path than the race is worth.
    //
    // ⚠ THE RED RATE, MEASURED RATHER THAN ASSUMED, and the measurement is why the rendezvous exists.
    // An earlier revision of this comment claimed deleting the re-check "reds MOST of the time". It
    // did not. Both shapes were run against that mutation on the dev box, five runs each:
    //
    //   • WITHOUT the acquisition rendezvous — 5 of 5 GREEN, whole test in ~22 ms. The stop landed
    //     before the first predicate check every time, so no wait was ever armed and the window the
    //     test is named for was never entered.
    //   • WITH it — 5 of 5 RED, each run parking on the 5 s bound. The wait IS armed, the wake is
    //     missed, and without the re-check the playback never proceeds.
    //
    // ⚠ Five of five is not "always", and this comment must not be read as promising determinism the
    // test does not have: the interleaving is still produced by the scheduler rather than forced.
    //
    // It is in the SAFE direction of CLAUDE.md § Test Timing: starvation can only make it pass more
    // often, never flip a pass to a fail.
    const int Runs = 30;

    for (var i = 0; i < Runs; i++)
    {
      var ducking = new FakeDuckingService();
      var source = new FakeEventSource();
      var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var tts = new FakeTtsFactory
      {
        OnCreate = (_, _, _) =>
        {
          // Signalled BEFORE the source is handed back, so the waiter below resumes while the
          // acquisition switch is still returning.
          acquired.TrySetResult();
          return Task.FromResult<IEventAudioSource>(source);
        }
      };
      using var service = CreateService(ttsFactory: tts, ducking: ducking);

      var blocker = new FakeEventSource();
      ducking.RaiseStarted(blocker, 8);

      var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
      await service.StartAsync(SpeechRequest());

      // Not a rendezvous on Waiting — the whole point is to race the arming rather than wait for it —
      // but a rendezvous on ACQUISITION, which is the statement before it.
      await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
      await ducking.StopDuckingAsync(blocker);

      // The bound is what turns a missed wake into a red rather than a 30 s hang.
      var final = await playing.WaitAsync(TimeSpan.FromSeconds(5));
      Assert.Equal(EventPlaybackState.Playing, final.State);
      Assert.Equal(1, source.PlayCalls);
    }
  }

  [Fact]
  public async Task TheWakeDoesNotStartAudioOnTheRaisingThread()
  {
    // BeginWait's TaskCreationOptions.RunContinuationsAsynchronously is what this pins. Without it,
    // TrySetResult runs the waiting playback's continuation INLINE on the thread that raised
    // DuckingStateChanged — and that continuation's next acts are a log write, _gate.WaitAsync,
    // ducking and PlayAsync, none of which the announcement's own thread should be made to run.
    //
    // ⚠ WHAT THIS PINS IS THREAD OWNERSHIP, NOT DEADLOCK-FREEDOM, and the reason given here used to be
    // wrong. See BeginWait's remark: a gate-holding raiser exists in this file today, and what makes
    // an inline continuation safe anyway is that SemaphoreSlim.WaitAsync SUSPENDS rather than blocks.
    //
    // MUTATION (§2.1): drop RunContinuationsAsynchronously from BeginWait and PlayAsync happens inline
    // on this thread, so PlayThreadId equals raisingThreadId.
    //
    // ⚠ TWO CAVEATS, because Assert.NotEqual is the kind of assertion that can pass for the wrong
    // reason.
    //
    // ① The mutation only reds while `_gate.WaitAsync` completes SYNCHRONOUSLY. It does here, because
    //    nothing else holds the gate at that instant. If something did, the inline continuation would
    //    suspend there and PlayAsync would resume on a pool thread — and the assertion would pass with
    //    the flag removed. The property is real; this instrument depends on the gate being free.
    //
    // ② The thread behaviour exercised is the FAKE's. FakeDuckingService raises synchronously on the
    //    calling thread for every removal. The real DuckingService does so only where there is no fade
    //    to await — a source leaving while OTHERS REMAIN, or a second source joining. The case driven
    //    here is the set EMPTYING, and on that path the real service raises after
    //    `await ApplyFadeAsync` (FadeSmooth over Audio:DuckingReleaseMs, 500 ms shipped), which hands
    //    the continuation to the pool, so on the box that raise is already off the caller's thread.
    //    The paths where the property actually bites are the non-emptying ones, and this test reaches
    //    their thread shape only through the fake.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var blocker = new FakeEventSource();
    ducking.RaiseStarted(blocker, 8);

    var waiting = NextSnapshotWith(service, EventPlaybackState.Waiting);
    await service.StartAsync(SpeechRequest());
    await waiting.WaitAsync(TimeSpan.FromSeconds(5));

    // The fake raises synchronously on whichever thread calls it, so this IS the raising thread.
    var raisingThreadId = Environment.CurrentManagedThreadId;

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await ducking.StopDuckingAsync(blocker);
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    // Asserted together: PlayThreadId is 0 until PlayAsync runs, so without the first line a test that
    // never started audio would pass the second.
    Assert.Equal(1, source.PlayCalls);
    Assert.NotEqual(raisingThreadId, source.PlayThreadId);
  }

  [Fact]
  public async Task AWaitingRemoteMediaSnapshotCarriesTheProvidersDuration()
  {
    // C-67: a waiting playback's Duration differs by ARM and both answers are honest. RemoteMedia
    // reports the provider's value because playback.ReportedDuration is assigned during acquisition,
    // which happens BEFORE the wait — so the chip can render a real bar while it waits. Speech reports
    // null, which AWaitingPlaybackIsReportedByCurrent asserts. SnapshotOf is unchanged by this row.
    var ducking = new FakeDuckingService();
    using var service = CreateService(
      ducking: ducking,
      httpHandler: new StubHandler(_ => Mp3Of(320_000)));   // would estimate to 20s

    ducking.RaiseStarted(new FakeEventSource(), 8);

    var waiting = NextSnapshotWith(service, EventPlaybackState.Waiting);
    await service.StartAsync(VoicemailRequest(durationSeconds: 47));
    var final = await waiting.WaitAsync(TimeSpan.FromSeconds(10));

    Assert.Equal(EventPlaybackState.Waiting, final.State);
    Assert.Equal(TimeSpan.FromSeconds(47), final.Duration);
    Assert.Equal(TimeSpan.Zero, final.PositionAtBroadcast);
  }

  [Fact]
  public async Task AStartRaiseForASourceThatHasAlreadyLeftTheSetIsIgnored()
  {
    // ⚠ Found in pre-merge review, and it falsified this PR's own reasoning. The handler's point (2)
    // originally said the priority entry "is present at this instant" because every caller sets it
    // immediately before StartDuckingAsync. That is true of the second-source path and FALSE of the
    // transition path: DuckingService raises the transition event after awaiting ApplyFadeAsync, so a
    // stop for that same source landing inside the ~100 ms attack fade deletes the override first, and
    // a synchronous GetPriority then answers the category default 8 for an announcement that had
    // explicitly claimed 3. Demonstrated against the real DuckingService, not theorised.
    //
    // The audible consequence, with the attended playback still in Preparing (so it is not itself in
    // the ducking set and the identity check cannot save it): a voicemail the user just pressed play on
    // is stopped before it ever sounds, because an unrelated announcement at priority 3 was cancelled.
    //
    // Not reachable in the shipped configuration — the only caller that can stop an announcement
    // concurrently with its own start is PhoneCallIntegrationService, and PhoneIntegration:Enabled is
    // false and has never been true. It becomes reachable the moment this arc turns that flag on.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var tts = new FakeTtsFactory
    {
      OnCreate = async (_, _, _) =>
      {
        await release.Task;
        return (IEventAudioSource)source;
      }
    };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    Assert.Equal(EventPlaybackState.Preparing, accepted.State);

    // An announcement explicitly claims 3 and starts ducking. Sub-threshold: it must not preempt.
    var announcement = new FakeEventSource();
    ducking.RaiseStarted(announcement, 3);
    await service.PreemptionTail;

    // ...and its transition raise arrives only after a concurrent stop deleted the override and
    // emptied the set.
    //
    // ⚠ WHAT SAVES THE PLAYBACK HERE CHANGED AT PHN-1f, and the change is the whole point of the args.
    // Before it, the subscriber resolved the priority for itself, read the category default 8 for an
    // announcement that had explicitly claimed 3, and only PHN-1d's ActiveEventCount == 0 guard stood
    // between here and a preemption — a guard whose own acknowledged residual was that a second
    // still-ducking source made the count non-zero and it fell silent. That guard is GONE. What stands
    // here now is that the args carry the priority the source CLAIMED, captured inside the lock that
    // added it and therefore before the fade that delayed this raise. So the rule that rejects it is
    // the ordinary sub-threshold one: 3 < GvMedia:PreemptAtPriority.
    ducking.RaiseStartedAfterItAlreadyLeft(announcement);
    await service.PreemptionTail;

    // The service's own map really has forgotten it — which is exactly why resolving the priority from
    // the service rather than from the args would answer 8 and preempt.
    Assert.Equal(DuckingService.DefaultEventPriority, ducking.GetPriority(announcement));
    Assert.Equal(EventPlaybackState.Preparing, service.Current?.State);

    // And it goes on to play, rather than having been stopped before it made a sound.
    release.SetResult();
    var final = await playing.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(accepted.Id, final.Id);
    Assert.Equal(1, source.PlayCalls);
    Assert.Equal(0, source.StopCalls);
  }

  [Fact]
  public async Task ThePreemptionPriorityComesFromTheArgs_NotFromTheDuckingService()
  {
    // ⚠ THIS WAS ThePriorityIsReadOnTheRaisingThread_NotOnTheDispatchedStop, and its premise is gone
    // rather than merely renamed. PHN-1d's answer to C-36 was "resolve the priority SYNCHRONOUSLY, on
    // the raising thread", and this test pinned WHERE the read happened because no outcome assertion
    // could separate the two orderings. PHN-1f removed the read entirely: the priority is captured by
    // DuckingService inside the lock that ADDS the source and travels on
    // DuckingStateChangedEventArgs.TriggeringSourcePriority, so the subscriber asks nothing.
    //
    // That is strictly stronger, and it is why the assertion inverts. Synchronous-on-the-raising-thread
    // still lost the race when the transition raise arrived AFTER the attack fade — PHN-1d could only
    // narrow that with an ActiveEventCount guard. Not reading at all cannot lose it.
    //
    // MUTATION: restore `priority = _duckingService.GetPriority(trigger)` in OnDuckingStateChanged and
    // PriorityReadsOnTheRaisingThread moves, so the first assertion reds.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    var readsElsewhereBefore = ducking.PriorityReadsElsewhere;
    var readsOnThreadBefore = ducking.PriorityReadsOnTheRaisingThread;

    ducking.RaiseStarted(new FakeEventSource(), 9);
    await service.PreemptionTail;

    // NEITHER counter moves. Not on the raising thread, because the args already carry it; and not
    // elsewhere, because nothing was dispatched to resolve it either.
    //
    // ⚠ TryWakeWaitingPlayback runs first on this raise and DOES call GetPriority — but only after its
    // "is anything waiting" guard, and nothing is waiting here. That guard is a trap-5 requirement in
    // its own right (this handler runs for every announcement on the box), and this test is the only
    // place in the suite that would notice it being removed: without it the wake walks the ducking set
    // on every raise and the raising-thread counter moves.
    Assert.Equal(readsOnThreadBefore, ducking.PriorityReadsOnTheRaisingThread);
    Assert.Equal(readsElsewhereBefore, ducking.PriorityReadsElsewhere);

    // …and the preemption still happened, so this is "it did not need to ask", not "it did not act".
    Assert.Equal(EventPlaybackState.Stopped, service.Current?.State);
    Assert.Equal(1, source.StopCalls);
  }

  [Fact]
  public async Task ATeardownCannotLandWhilePlayAsyncIsInFlight()
  {
    // ⚠ THIS IS THE TEST FOR THE GATE, and it exists because the first version of this PR shipped
    // without one. Removing the _gate serialisation from the acquisition tail left all 54 other tests
    // green — measured, not assumed — which meant the mitigation for the worst outcome in this PR was
    // the one change nothing could detect.
    //
    // The failure it guards: a preemption completing a whole teardown between the tail's IsTerminal
    // re-check and PlayAsync. AudioSourceBase.PlayAsync refuses only once _disposed is set, so in the
    // window between StopAsync and DisposeAsync the state reads Stopped and PlayCoreAsync still runs —
    // audio on a source the seam has already forgotten, with no playbackId, that no route, chip or
    // later preemption can address. It plays to the end over the announcement that preempted it.
    //
    // ORDERING is the assertion, not counting. Both implementations stop the playback and dispose the
    // source exactly once; what differs is whether the teardown was able to interleave with the start.
    var ducking = new FakeDuckingService();
    var playGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var source = new FakeEventSource { PlayGate = playGate };
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var accepted = await service.StartAsync(SpeechRequest());

    // The tail is now inside PlayAsync, holding _gate. PlayCalls is incremented before the park.
    await WaitUntilAsync(() => source.PlayCalls == 1, TimeSpan.FromSeconds(5));

    ducking.RaiseStarted(new FakeEventSource(), 9);
    var tail = service.PreemptionTail;

    // The gated implementation CANNOT complete this stop: StopAsync needs _gate, and the tail holds it
    // until after PlayAsync returns. That is deterministic — no amount of scheduling makes it proceed.
    // ⚠ The bound is what makes the BREAK deterministic rather than the behaviour: an ungated
    // implementation tears the source down inside this window. Starvation can only make a broken
    // implementation look correct here, never the reverse, which is the safe direction (CLAUDE.md
    // § Test Timing).
    await Assert.ThrowsAsync<TimeoutException>(
      () => tail.WaitAsync(TimeSpan.FromMilliseconds(500)));
    Assert.Equal(0, source.StopCalls);

    playGate.SetResult();
    await tail;

    lock (source.Calls)
    {
      Assert.Equal(new[] { "play", "stop", "dispose" }, source.Calls);
    }

    Assert.Equal(accepted.Id, service.Current!.Id);
    Assert.Equal(EventPlaybackState.Stopped, service.Current!.State);
  }

  [Fact]
  public void PreemptAtPriorityMustNotExceedTheEventCategoryDefault()
  {
    // The threshold has a CEILING as well as a floor, and only the floor is argued in ADR-029 §6.1.
    //
    // ⚠ THIS TEST PINS A PROXY, and saying so is the point. The tempting justification — "GetPriority
    // answers 8 for sources whose caller never called SetPriority, so a threshold of 9 exempts them" —
    // is NOT the live mechanism: all four StartDuckingAsync call sites in the tree call SetPriority on
    // the same source immediately before, so that fallback never answers a start raise. What actually
    // makes 9 a trap is NotificationsController.Announce's `request.Priority ?? 8`: every external
    // notification that names no priority arrives at exactly 8, so raising this key silently stops the
    // doorbell preempting while the dormant PhoneIntegration:RingPriority (9) still would.
    //
    // That live coupling lives in Radio.API and is NOT pinned anywhere — NotificationsControllerTests
    // only ever posts an explicit priority, so the `?? 8` default is untested. What this test can reach
    // is the compile-time pair ADR-029 §6.1 anchored the number on, and it is worth holding: 8 is the
    // category default, and the ADR chose the threshold to sit exactly there.
    //
    // Lowering it to 7 is the change ADR-029 §6.1 anticipates and this test permits.
    Assert.Equal(8, DuckingService.DefaultEventPriority);

    var shipped = new GvMediaOptions().PreemptAtPriority;
    Assert.True(
      shipped <= DuckingService.DefaultEventPriority,
      $"GvMedia:PreemptAtPriority defaults to {shipped}, above DuckingService.DefaultEventPriority "
      + $"({DuckingService.DefaultEventPriority}), which is the value ADR-029 6.1 anchored the "
      + "threshold on. Above it, the live preempting caller stops qualifying: an external notification "
      + "that names no priority arrives at exactly 8 via NotificationsController's 'Priority ?? 8', so "
      + "the doorbell silently stops preempting while the dormant ring at 9 still would - the feature "
      + "looks intact and has stopped happening. Lower the threshold, or move both together and say so.");
  }

  // ── the max-duration cap (ADR-029 D7 §7.1) ────────────────────────────────

  [Fact]
  public async Task TheDurationCapStopsAPlaybackThatOutlivesMaxPlaybackSeconds()
  {
    // ADR-029 D7 §7.1 — "This is THE guarantee. No client cooperation, no heartbeat, no timer loop,
    // no polling." Everything else in D7 is a latency improvement on this line.
    //
    // ⚠ Driven by FakeTimeProvider, never by Task.Delay. CLAUDE.md § Test Timing forbids racing a
    // wall clock against a wall clock, and TEST-4 is the row about the last time this repo did.
    // Advance() fires every DUE timer synchronously before it returns, and the rendezvous below is on
    // the Stopped SNAPSHOT rather than on elapsed time — so both halves are deterministic.
    var time = new FakeTimeProvider();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(
      ttsFactory: tts,
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, MaxPlaybackSeconds = 30
      },
      timeProvider: time);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    // Subscribed BEFORE the advance, so the transition cannot be missed.
    var stopped = NextSnapshotWith(service, EventPlaybackState.Stopped);
    time.Advance(TimeSpan.FromSeconds(30));

    var final = await stopped.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(accepted.Id, final.Id);
    Assert.Equal(1, source.StopCalls);
    Assert.Equal(EventPlaybackState.Stopped, service.Current!.State);
  }

  [Fact]
  public async Task TheDurationCapDoesNotFireBeforeItsTime()
  {
    // ⚠ A NEGATIVE assertion that is DETERMINISTIC rather than merely patient, and this test says so
    // about itself because CLAUDE.md § Test Timing asks a test to. FakeTimeProvider.Advance runs
    // every due timer synchronously before returning, so when it returns with none due there is
    // nothing in flight for the assertions to lose a race to. This is NOT "no event arrived within
    // 200 ms", and it is the reason the cap uses TimeProvider rather than a raw Timer.
    var time = new FakeTimeProvider();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(
      ttsFactory: tts,
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, MaxPlaybackSeconds = 30
      },
      timeProvider: time);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    time.Advance(TimeSpan.FromSeconds(29));

    Assert.Equal(EventPlaybackState.Playing, service.Current!.State);
    Assert.Equal(0, source.StopCalls);
  }

  [Fact]
  public async Task ANaturalEndDisarmsTheDurationCap()
  {
    // The disarm lives in ReleaseSourceAsync, the one funnel that stops and disposes a source. If it
    // were missing, a ten-second voicemail would leave a five-minute timer alive — and on this box a
    // timer per playback that never fires is the shape trap 5 of the arc breakdown exists to refuse.
    //
    // ⚠ THE ASSERTION IS ON THE LOG, AND THAT IS NOT A STYLISTIC CHOICE — it is the only thing here
    // that can actually fail. The obvious assertion, which plan PHN-1e Task 8 specifies and which an
    // earlier draft of this test used, is that source.StopCalls does not move. It does not move
    // EITHER WAY: measured by deleting playback.DisarmDurationCap() from ReleaseSourceAsync and
    // re-running, 62/62 still passed. The cap timer fires, dispatches StopAsync(id), and StopAsync
    // finds a playback whose terminal transition ClaimTerminal has already admitted — so it returns
    // false without ever reaching the source. A StopCalls assertion is therefore satisfied by
    // ClaimTerminal's idempotence, which is a DIFFERENT property that a different test already pins,
    // and it would have reported this disarm as covered while covering nothing.
    //
    // The cap callback's LogWarning is emitted unconditionally at the top of the callback, before the
    // dispatch and before any idempotence can absorb it, so it is the one observable that exists if
    // and only if the timer was still armed. FakeTimeProvider.Advance runs due callbacks
    // synchronously on the calling thread, so the message is in the sink by the time it returns.
    var time = new FakeTimeProvider();
    var logs = new CapturingLoggerProvider();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(
      ttsFactory: tts,
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, MaxPlaybackSeconds = 30
      },
      logs: logs,
      timeProvider: time);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    var completed = NextSnapshotWith(service, EventPlaybackState.Completed);
    source.RaiseCompleted(PlaybackCompletionReason.EndOfContent);
    await completed.WaitAsync(TimeSpan.FromSeconds(5));

    var stopsAtEnd = source.StopCalls;
    time.Advance(TimeSpan.FromMinutes(10));

    Assert.DoesNotContain(
      logs.Messages,
      m => m.Contains("reached GvMedia:MaxPlaybackSeconds", StringComparison.Ordinal));

    // Kept, but understood for what they are: true whether or not the disarm happened, and here to
    // show the natural end itself still behaved. Neither can fail on the disarm's account.
    Assert.Equal(stopsAtEnd, source.StopCalls);
    Assert.Equal(EventPlaybackState.Completed, service.Current!.State);
  }

  [Fact]
  public async Task AReplacedPlaybackDoesNotTakeItsReplacementDownWhenItsCapExpires()
  {
    // Two independent reasons this holds, and the test exists because only one of them is obvious.
    // (1) The replaced playback's teardown runs ReleaseSourceAsync, which disarms its cap.
    // (2) Even if it did not, the callback addresses StopAsync BY THE OLD ID, which no longer matches
    //     _current, so it is a no-op.
    // Belt and braces — but a refactor that made the cap address "whatever is current" would turn a
    // stale timer into a stop of an unrelated playback, and this is the test that catches it.
    var time = new FakeTimeProvider();
    var first = new FakeEventSource();
    var second = new FakeEventSource();
    var queue = new Queue<IEventAudioSource>([first, second]);
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult(queue.Dequeue()) };
    using var service = CreateService(
      ttsFactory: tts,
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, MaxPlaybackSeconds = 30
      },
      timeProvider: time);

    var firstPlaying = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await firstPlaying.WaitAsync(TimeSpan.FromSeconds(5));

    // Ten seconds into the first playback, a second one replaces it — so the first's cap would fire
    // twenty seconds from now, while the second's has thirty seconds to run from here.
    time.Advance(TimeSpan.FromSeconds(10));
    var replaced = NextSnapshotWith(service, EventPlaybackState.Stopped);

    // ⚠ The rendezvous is the Playing SNAPSHOT, not second.PlayCalls, and the difference is a real
    // race rather than a style preference. AcquireAndPlayAsync runs source.PlayAsync (which
    // increments PlayCalls), then ArmDurationCap, then PublishNonTerminal(Playing) — so a wait on
    // PlayCalls can return while the state is still Preparing, and the assertion below would read it.
    // Waiting on the snapshot is what the four sibling cap tests do.
    var secondPlaying = NextSnapshotWith(service, EventPlaybackState.Playing);
    var two = await service.StartAsync(SpeechRequest());
    await replaced.WaitAsync(TimeSpan.FromSeconds(5));
    await secondPlaying.WaitAsync(TimeSpan.FromSeconds(5));

    time.Advance(TimeSpan.FromSeconds(21));

    Assert.Equal(two.Id, service.Current!.Id);
    Assert.Equal(EventPlaybackState.Playing, service.Current!.State);
    Assert.Equal(0, second.StopCalls);
  }

  [Fact]
  public async Task AnAbsurdMaxPlaybackSecondsIsClampedRatherThanKillingEveryPlayback()
  {
    // ⚠ A CRASH FIX, and the crash was silent in the worst way. TimeProvider.CreateTimer rejects a
    // due time above ~49.7 days, so an absurd MaxPlaybackSeconds threw ArgumentOutOfRangeException
    // out of ArmDurationCap — which runs AFTER PlayAsync returned — landing in AcquireAndPlayAsync's
    // general catch and failing EVERY attended playback immediately after it started, under a
    // generic failure reason. One config value, feature dead, no diagnosis.
    //
    // ⚠ FakeTimeProvider does NOT enforce that bound, so this test cannot reproduce the throw. What
    // it pins is the observable consequence of the clamp: the playback reaches Playing and stays
    // there rather than failing, and the cap does not fire at a clamped-to-something-small value.
    // Said plainly rather than implied, because a test that cannot see the original fault should not
    // be read as covering it.
    var time = new FakeTimeProvider();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(
      ttsFactory: tts,
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, MaxPlaybackSeconds = int.MaxValue
      },
      timeProvider: time);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    // A day later - the clamp - it stops. Not immediately, and not never.
    Assert.Equal(EventPlaybackState.Playing, service.Current!.State);

    var stopped = NextSnapshotWith(service, EventPlaybackState.Stopped);
    time.Advance(TimeSpan.FromHours(24));

    await stopped.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(1, source.StopCalls);
  }

  [Fact]
  public async Task AThrowingLogSinkInTheCapCallbackDoesNotEscape()
  {
    // ⚠ THE CALLBACK BOUNDARY IS A PROCESS BOUNDARY. This lambda IS the TimerCallback:
    // System.Threading.Timer does not wrap it, so an unhandled exception there runs on a thread-pool
    // thread and TERMINATES THE PROCESS. The guard used to start inside the dispatched Task.Run,
    // leaving the LogWarning and the Task.Run scheduling outside it - and ILogger.Log aggregates and
    // RETHROWS provider exceptions, so a failing Serilog file sink (or a log written after
    // CloseAndFlush during a shutdown racing an armed cap) escaped straight into the runtime.
    //
    // ⚠ FakeTimeProvider is what makes this testable at all: Advance runs due callbacks
    // SYNCHRONOUSLY on the calling thread, so an unguarded throw surfaces here as a failed
    // assertion instead of taking the test host down with it. Delete the outer try in
    // ArmDurationCap's callback and this test throws.
    var time = new FakeTimeProvider();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    var logs = new ThrowingLoggerProvider();
    using var service = CreateService(
      ttsFactory: tts,
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, MaxPlaybackSeconds = 30
      },
      logs: logs,
      timeProvider: time);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    logs.ThrowOnLog = true;

    // The assertion IS that this does not throw.
    time.Advance(TimeSpan.FromSeconds(30));
  }

  [Fact]
  public async Task AZeroMaxPlaybackSecondsClampsToOneSecondRatherThanMeaningNoCap()
  {
    // ⚠ There is deliberately no off switch (ADR-029 §7.1 calls this THE guarantee), and this pins
    // which direction a nonsense value resolves in. The alternative reading — 0 means "never cap" —
    // is the PreemptAtPriority trap in another key: a number that silently deletes a safety property
    // while leaving it looking configured (plan PHN-1d C-43).
    var time = new FakeTimeProvider();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(
      ttsFactory: tts,
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, MaxPlaybackSeconds = 0
      },
      timeProvider: time);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    var stopped = NextSnapshotWith(service, EventPlaybackState.Stopped);
    time.Advance(TimeSpan.FromSeconds(1));

    await stopped.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(1, source.StopCalls);
  }
}

/// <summary>
/// A <see cref="CapturingLoggerProvider"/> that can be told to start throwing, standing in for a
/// wedged log sink. <see cref="ILogger"/> aggregates and rethrows provider exceptions, so this is the
/// shape a failing Serilog sink presents to its caller.
/// </summary>
internal sealed class ThrowingLoggerProvider : CapturingLoggerProvider
{
  public bool ThrowOnLog { get; set; }

  public new ILogger<T> CreateLogger<T>() => new ThrowingLogger<T>(this, base.CreateLogger<T>());

  private sealed class ThrowingLogger<T>(ThrowingLoggerProvider owner, ILogger<T> inner) : ILogger<T>
  {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter)
    {
      if (owner.ThrowOnLog)
      {
        throw new InvalidOperationException("the log sink is wedged");
      }

      inner.Log(logLevel, eventId, state, exception, formatter);
    }
  }
}

/// <summary>An ITTSFactory that hands back a source the test controls, or throws on demand.</summary>
internal sealed class FakeTtsFactory : ITTSFactory
{
  public Func<string, TTSParameters?, CancellationToken, Task<IEventAudioSource>>? OnCreate { get; set; }

  public TTSParameters? LastParameters { get; private set; }

  public IReadOnlyList<TTSEngineInfo> AvailableEngines => Array.Empty<TTSEngineInfo>();

  public Task<IEventAudioSource> CreateAsync(
    string text, TTSParameters? parameters = null, CancellationToken cancellationToken = default)
  {
    LastParameters = parameters;
    return OnCreate is null
      ? Task.FromResult<IEventAudioSource>(new FakeEventSource())
      : OnCreate(text, parameters, cancellationToken);
  }

  public Task<IReadOnlyList<TTSVoiceInfo>> GetVoicesAsync(
    TTSEngine engine, CancellationToken cancellationToken = default)
    => Task.FromResult<IReadOnlyList<TTSVoiceInfo>>(Array.Empty<TTSVoiceInfo>());

  public Task<int> RefreshVoicesAsync(TTSEngine engine, CancellationToken cancellationToken = default)
    => Task.FromResult(0);

  public Task SetVoiceFavoriteAsync(
    TTSEngine engine, string voiceId, CancellationToken cancellationToken = default)
    => Task.CompletedTask;

  public Task RemoveVoiceFavoriteAsync(
    TTSEngine engine, string voiceId, CancellationToken cancellationToken = default)
    => Task.CompletedTask;
}

/// <summary>
/// A minimal IEventAudioSource the test drives directly.
/// </summary>
/// <remarks>
/// ⚠ RaiseCompleted is deliberately callable more than once, because that is what the real sources
/// do: both raise EndOfContent from their monitor AND UserStopped from StopCoreAsync, and
/// AudioSourceBase.StopAsync does not short-circuit on Stopped. A fake that could only complete
/// once would make ATerminalTransitionHappensExactlyOnce vacuous — it would be asserting a property
/// of the fake rather than of the service.
///
/// ⚠ And StopAsync raises UserStopped INLINE, before it returns, for a second and separate reason:
/// RE-ENTRANCY, not multiplicity. Both real sources raise UserStopped from inside StopCoreAsync,
/// which EventPlaybackService.TearDownAsync calls WHILE IT HOLDS _gate — so the service's
/// "OnSourceCompleted must never wait on _gate" invariant is exercised on every teardown here, the
/// same way it is on the appliance. A fake that stayed silent in StopAsync (which this one did until
/// PHN-1c's review) leaves that invariant documented in two places and tested in none: re-adding an
/// await _gate.WaitAsync() to OnSourceCompleted would deadlock every user stop on the box, with a
/// fully green suite. Verified by doing exactly that and watching the suite hang; see the commit.
/// </remarks>
internal sealed class FakeEventSource : IEventAudioSource
{
  public string Id { get; } = "AudioFileEvent-" + Guid.NewGuid().ToString("N");

  public string Name => "fake";

  public AudioSourceType Type => AudioSourceType.AudioFileEvent;

  public AudioSourceCategory Category => AudioSourceCategory.Event;

  public AudioSourceState State { get; set; } = AudioSourceState.Ready;

  public float Volume { get; set; } = 1.0f;

  public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(30);

  public TimeSpan Position { get; set; } = TimeSpan.Zero;

  public bool IsSeekable { get; set; } = true;

  public int PlayCalls { get; private set; }

  public int StopCalls { get; private set; }

  public int DisposeCalls { get; private set; }

  /// <summary>
  /// The managed thread PlayAsync was entered on, or 0 if it has never been called.
  /// </summary>
  /// <remarks>
  /// ⚠ Recorded rather than counted, because the question TheWakeDoesNotStartAudioOnTheRaisingThread
  /// asks is WHERE the audio started, not whether it did. Managed thread ids are never 0, so an
  /// unset value cannot be mistaken for a real one — and a test that forgot to make PlayAsync happen
  /// at all would compare against 0 and pass, so that test asserts PlayCalls too.
  /// </remarks>
  public int PlayThreadId { get; private set; }

  /// <summary>
  /// When set, PlayAsync parks on it before returning. PlayCalls is incremented BEFORE the park, so a
  /// test can rendezvous on "the tail has entered PlayAsync" and then act while it is still in flight.
  /// </summary>
  /// <remarks>
  /// ⚠ This is the seam that makes PHN-1d's _gate serialisation testable at all. Without it there is
  /// no way to hold the acquisition tail inside the window a teardown must not be able to enter, so
  /// removing the gate left the whole suite green — measured. See
  /// ATeardownCannotLandWhilePlayAsyncIsInFlight.
  /// </remarks>
  public TaskCompletionSource? PlayGate { get; set; }

  /// <summary>
  /// Lifecycle calls in the order they were OBSERVED to happen: "play" is recorded when PlayAsync
  /// returns, "stop" and "dispose" when they are entered. Ordering, not counting, is what distinguishes
  /// a serialised teardown from one that interleaved with the start.
  /// </summary>
  public List<string> Calls { get; } = [];

  public TimeSpan? SoughtTo { get; private set; }

  public event EventHandler<AudioSourceStateChangedEventArgs>? StateChanged;

  public event EventHandler<AudioSourceCompletedEventArgs>? PlaybackCompleted;

  public object GetSoundComponent() => this;

  public async Task PlayAsync(CancellationToken cancellationToken = default)
  {
    PlayCalls++;
    PlayThreadId = Environment.CurrentManagedThreadId;
    if (PlayGate is { } gate)
    {
      await gate.Task;
    }

    State = AudioSourceState.Playing;
    lock (Calls) { Calls.Add("play"); }
  }

  public Task PauseAsync(CancellationToken cancellationToken = default)
  {
    State = AudioSourceState.Paused;
    return Task.CompletedTask;
  }

  public Task ResumeAsync(CancellationToken cancellationToken = default)
  {
    State = AudioSourceState.Playing;
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken = default)
  {
    StopCalls++;
    lock (Calls) { Calls.Add("stop"); }
    State = AudioSourceState.Stopped;
    // ⚠ Inline, before returning — see the class remarks. This is what the real sources do
    // (EventAudioSourceBase.StopAsync -> StopCoreAsync -> OnPlaybackCompleted(UserStopped)), and it
    // is what puts EventPlaybackService.OnSourceCompleted on a thread that is already holding _gate.
    RaiseCompleted(PlaybackCompletionReason.UserStopped);
    return Task.CompletedTask;
  }

  public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
  {
    // ⚠ Mirrors AudioFileEventSource.SeekCoreAsync, which throws ArgumentOutOfRangeException for a
    // position outside [0, _duration]. A fake that accepted ANY position made the seam's handling of
    // a seek past the end untestable — and that path escaped as a bare 500 until PHN-1c's review,
    // because Radio.API registers no UseExceptionHandler and no AddProblemDetails.
    if (position < TimeSpan.Zero || position > Duration)
    {
      throw new ArgumentOutOfRangeException(nameof(position), "Seek position out of range");
    }

    SoughtTo = position;
    return Task.CompletedTask;
  }

  public ValueTask DisposeAsync()
  {
    DisposeCalls++;
    lock (Calls) { Calls.Add("dispose"); }
    return ValueTask.CompletedTask;
  }

  public void RaiseCompleted(PlaybackCompletionReason reason, Exception? error = null) =>
    PlaybackCompleted?.Invoke(this, new AudioSourceCompletedEventArgs
    {
      SourceId = Id,
      Reason = reason,
      Error = error
    });

  public void RaiseStateChanged() =>
    StateChanged?.Invoke(this, new AudioSourceStateChangedEventArgs
    {
      SourceId = Id,
      PreviousState = State,
      NewState = State
    });
}

/// <summary>
/// Records what the seam asked of ducking, and models the four behaviours PR 4 depends on.
/// </summary>
/// <remarks>
/// ⚠ It raises both its events, even though nothing in PR 3 subscribed. That was deliberate: PR 4 is
/// the PR that subscribes to DuckingStateChanged, and a fake that never raised it would let PR 4 add a
/// subscription that deadlocks or re-enters without any existing test noticing.
///
/// ⚠ PHN-1d extended it in place rather than forking it, and the four behaviours it now models are
/// each load-bearing for a specific guard in EventPlaybackService.OnDuckingStateChanged:
///
/// (1) every start raise carries a real TriggeringSource, as DuckingService does after PHN-1d Task 1.
///     Without it the handler's whole decision is unreachable, because it pattern-matches null away.
/// (2) StopDuckingAsync DROPS the per-source priority entry, as the real service does — which is what
///     makes a late GetPriority resolution fail a test rather than only a review.
/// (3) IsDucking:false is raised only when the active set EMPTIES, as the real service does.
/// (4) StopGate lets a test park StopDuckingAsync, which is how "the stop is dispatched, not awaited"
///     is observed rather than asserted.
///
/// ⚠ PHN-1f's fixer added a FIFTH, and it is the one this fake got wrong for a whole cycle:
///
/// (5) StopDuckingAsync raises for a source that was actually IN the set, and for the removal that
///     empties it, and for nothing else — DuckingService's `wasPresent || needsRestore`, copied. The
///     fake previously raised unconditionally with `isDucking: remaining > 0`, which is what the
///     production COMMENT claimed rather than what the production CODE did, so the two could not
///     disagree in front of a test. See StopDuckingAsync below.
/// </remarks>
internal sealed class FakeDuckingService : IDuckingService
{
  private readonly List<IEventAudioSource> _active = [];

  // ⚠ Two structures, and the split is load-bearing. Priorities is an APPEND-ONLY SPY on SetPriority
  // calls — PHN-1c asserts against it that the request's Priority reached ducking at all, and that
  // assertion must survive a teardown. _effective is the map GetPriority answers from, and it is the
  // one StopDuckingAsync DELETES from, exactly as the real DuckingService deletes _sourcePriorities
  // before it raises. Modelling the deletion on the spy instead would erase the record PHN-1c tests
  // read; not modelling it at all would make the "a stopping source reads as priority 8" trap
  // unreachable, which is the trap this fake exists to expose.
  private readonly Dictionary<string, int> _effective = new(StringComparer.Ordinal);

  // The managed thread currently inside a DuckingStateChanged raise, or 0. Environment thread ids are
  // never 0, so a read taken anywhere else can never be mistaken for one taken inside the raise.
  private int _raisingThreadId;
  private int _readsOnTheRaisingThread;
  private int _readsElsewhere;

  public List<(string Id, int Priority)> Priorities { get; } = [];

  public List<string> Started { get; } = [];

  public List<IEventAudioSource> StartedSources { get; } = [];

  public List<string> Stopped { get; } = [];

  /// <summary>
  /// When set, StopDuckingAsync parks on it. Used to prove the preemption stop is dispatched rather
  /// than awaited on the raising thread.
  /// </summary>
  public TaskCompletionSource? StopGate { get; set; }

  /// <summary>
  /// How many times GetPriority was called from INSIDE a DuckingStateChanged raise, on the very thread
  /// doing the raising — i.e. synchronously, before the subscriber returned.
  /// </summary>
  public int PriorityReadsOnTheRaisingThread => Volatile.Read(ref _readsOnTheRaisingThread);

  /// <summary>
  /// How many times GetPriority was called from anywhere else — including from a task the subscriber
  /// dispatched, which is the exact restructure ADR-029's C-36 trap is about.
  /// </summary>
  public int PriorityReadsElsewhere => Volatile.Read(ref _readsElsewhere);

  public float CurrentDuckLevel
  {
    get { lock (Started) { return _active.Count > 0 ? 20f : 100f; } }
  }

  public bool IsDucking
  {
    get { lock (Started) { return _active.Count > 0; } }
  }

  public int ActiveEventCount
  {
    get { lock (Started) { return _active.Count; } }
  }

  public event EventHandler<DuckingStateChangedEventArgs>? DuckingStateChanged;

  public event EventHandler<DuckingLevelChangedEventArgs>? DuckingLevelChanged;

  public Task StartDuckingAsync(IEventAudioSource s, CancellationToken cancellationToken = default)
  {
    int count;
    bool newlyAdded;
    int priorityAtStart;
    lock (Started)
    {
      Started.Add(s.Id);
      StartedSources.Add(s);
      newlyAdded = !_active.Any(a => string.Equals(a.Id, s.Id, StringComparison.Ordinal));
      if (newlyAdded)
      {
        _active.Add(s);
      }
      priorityAtStart = GetPriorityUnlocked(s);
      count = _active.Count;
    }

    // ⚠ Only for a source that JOINS, matching DuckingService after PHN-1d. The fake used to raise
    // unconditionally, which made it MORE permissive than production — the wrong direction for a fake,
    // because a handler that misbehaved only on a repeat start would have been exercised here and not
    // on the box.
    //
    // Captured inside the lock, mirroring DuckingService: the priority the args carry is the one the
    // source had when it JOINED, not one resolved later.
    if (newlyAdded)
    {
      RaiseStateChanged(
        s, isDucking: true, activeCount: count, duckLevel: 20f,
        DuckingSourceTransition.Started, priorityAtStart);
    }

    return Task.CompletedTask;
  }

  public async Task StopDuckingAsync(IEventAudioSource s, CancellationToken cancellationToken = default)
  {
    if (StopGate is { } gate)
    {
      await gate.Task;
    }

    int remaining;
    int priorityBeforeRemoval;
    bool wasPresent;
    bool needsRestore;
    lock (Started)
    {
      Stopped.Add(s.Id);

      // ⚠ Captured BEFORE the removals, exactly as DuckingService does. Modelling the capture is the
      // whole point: a fake that read the priority after the removal would answer the category default
      // 8, and the starvation test would pass for the wrong reason.
      priorityBeforeRemoval = GetPriorityUnlocked(s);

      // This fake keeps no _isDucking field — its IsDucking IS "_active is non-empty" — so reading the
      // count before the removal is the same predicate DuckingService writes as `_isDucking`.
      var wasDucking = _active.Count > 0;

      // ⚠ The bool is KEPT, as DuckingService now keeps Dictionary.Remove's. A stop for a source that
      // is not in the set must raise NOTHING.
      wasPresent = _active.RemoveAll(a => string.Equals(a.Id, s.Id, StringComparison.Ordinal)) > 0;
      // The real service removes the priority override here, BEFORE it raises. That is what makes
      // GetPriority answer the category default for a source that has just stopped.
      _effective.Remove(s.Id);
      remaining = _active.Count;
      needsRestore = wasDucking && remaining == 0;
    }

    // ⚠ RAISES ON EVERY REMOVAL since PHN-1f, matching DuckingService, carrying the aggregate
    // DuckingService carries. This line is what makes
    // AHigherPrioritySourceEndingWhileALowerOneContinuesStillWakesTheQueue meaningful: revert it to
    // `if (remaining == 0)` — the pre-PHN-1f rule — and that test must go RED.
    //
    // ⚠ AND ON NO OTHER CALL. The `wasPresent || needsRestore` guard and the `!needsRestore` argument
    // are DuckingService's own, copied rather than approximated. This fake used to raise
    // unconditionally with `isDucking: remaining > 0`, which implemented the production COMMENT rather
    // than the production CODE — so a redundant stop emitted IsDucking:true with ActiveEventCount:0
    // in production while the fake emitted IsDucking:false, and no test in this suite could see the
    // divergence. Plan §2.2 item 2 makes this fake's fidelity the row's load-bearing assumption; this
    // is that assumption being kept rather than asserted.
    if (wasPresent || needsRestore)
    {
      RaiseStateChanged(
        s, isDucking: !needsRestore, activeCount: remaining,
        duckLevel: remaining > 0 ? 20f : 100f,
        DuckingSourceTransition.Ended, priorityBeforeRemoval);
    }

    DuckingLevelChanged?.Invoke(this, new DuckingLevelChangedEventArgs { TransitionComplete = true });
  }

  public Task StopAllDuckingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

  public int GetPriority(IAudioSource s)
  {
    if (Volatile.Read(ref _raisingThreadId) == Environment.CurrentManagedThreadId)
    {
      Interlocked.Increment(ref _readsOnTheRaisingThread);
    }
    else
    {
      Interlocked.Increment(ref _readsElsewhere);
    }

    lock (Started)
    {
      return GetPriorityUnlocked(s);
    }
  }

  public void SetPriority(IAudioSource s, int priority)
  {
    lock (Started)
    {
      Priorities.Add((s.Id, priority));
      _effective[s.Id] = priority;
    }
  }

  public IReadOnlyList<IEventAudioSource> GetActiveEventsByPriority()
  {
    lock (Started)
    {
      return _active
        .OrderByDescending(GetPriorityUnlocked)
        .ThenBy(a => a.Id, StringComparer.Ordinal)
        .ToList();
    }
  }

  public void Dispose()
  {
  }

  /// <summary>Models a foreign event source — an announcement — starting.</summary>
  public void RaiseStarted(IEventAudioSource source, int priority)
  {
    SetPriority(source, priority);
    StartDuckingAsync(source).GetAwaiter().GetResult();
  }

  /// <summary>
  /// Reproduces the args DuckingService.StopDuckingAsync raises when the set EMPTIES: IsDucking false,
  /// the stopping source as TriggeringSource, and its priority entry already deleted — so GetPriority
  /// answers DefaultEventPriority (8) for it.
  /// </summary>
  /// <remarks>
  /// ⚠ Driven directly rather than through StopDuckingAsync, because the set cannot empty while an
  /// attended playback still holds an entry in it. The handler's contract is about the ARGS, not about
  /// how they arose, and this is the one shape that would preempt on a stop if the IsDucking filter
  /// were dropped.
  /// </remarks>
  public void RaiseSetEmptied(IEventAudioSource source)
  {
    lock (Started)
    {
      _effective.Remove(source.Id);
    }

    RaiseStateChanged(
      source, isDucking: false, activeCount: 0, duckLevel: 100f,
      DuckingSourceTransition.Ended, DuckingService.DefaultEventPriority);
  }

  /// <summary>
  /// Models a higher-priority source LEAVING while a lower-priority one keeps ducking — the starvation
  /// case. Before PHN-1f this produced NO RAISE AT ALL from the real service.
  /// </summary>
  public void RaiseEndedWithOthersRemaining(IEventAudioSource source) =>
    StopDuckingAsync(source).GetAwaiter().GetResult();

  /// <summary>
  /// Raises a STARTED transition with a NULL TriggeringSource — the one shape the handler's null check
  /// is the only guard against, because rule 1 lets it through.
  /// </summary>
  /// <remarks>
  /// ⚠ Nothing in the tree produces these args: DuckingService.StartDuckingAsync refuses a null
  /// source outright. This exists because DuckingStateChangedEventArgs.TriggeringSource is nullable,
  /// so the subscriber's guard is part of its contract rather than an accident of who calls it.
  ///
  /// ⚠ THE PRIORITY IS DuckingService.DefaultEventPriority (8) AND MUST STAY AT OR ABOVE THE
  /// THRESHOLD. It was 0 until PHN-1f's fixer, and at 0 these args are turned away by
  /// `priority &lt; threshold` before the null check is ever reached — so
  /// AStartRaiseWithNoTriggeringSourceIsIgnored was green with the null check deleted, which is
  /// exactly the "test that cannot fail" this arc keeps finding. 8 is the value a real Started raise
  /// carries when its caller named no priority, so it is also the honest one.
  /// </remarks>
  public void RaiseStartedWithNoSource() =>
    RaiseStateChanged(
      null, isDucking: true, activeCount: 1, duckLevel: 20f,
      DuckingSourceTransition.Started, DuckingService.DefaultEventPriority);

  /// <summary>
  /// Reproduces DuckingService's Started TRANSITION raise arriving after the source has already left
  /// the set: the starting source as TriggeringSource, its priority override already deleted from the
  /// map GetPriority answers from, and ActiveEventCount zero — but the args still carrying the
  /// priority the source CLAIMED, because DuckingService captures it before the fade.
  /// </summary>
  /// <remarks>
  /// ⚠ These args are not hypothetical. DuckingService raises the transition event AFTER awaiting
  /// ApplyFadeAsync (Audio:DuckingAttackMs, 100 ms shipped), so a StopDuckingAsync for that same source
  /// landing inside the fade deletes the override and empties the set before the raise fires. Reached
  /// today only through PhoneCallIntegrationService, which is dormant — and reachable the moment the
  /// phone arc enables it, which is what this arc is building toward.
  ///
  /// ⚠ What this helper DEMONSTRATES changed at PHN-1f, and the change is the point. Before it, these
  /// args were the fade-window race: the subscriber resolved the priority for itself, read the
  /// category default 8, and PHN-1d's ActiveEventCount == 0 guard was what stopped it acting on a
  /// source that had already gone. PHN-1f closed the race at the source instead — the priority is
  /// captured inside the lock that adds the entry, so it survives the deletion — and the guard is
  /// gone. So the assertion this helper supports is now "the preemption still reads the priority the
  /// caller claimed, not the default 8", not "the guard rejects it". The helper stays because the
  /// args shape it produces is still reachable and still worth pinning.
  ///
  /// The priority is captured BEFORE the removal for exactly the reason DuckingService captures it
  /// before its own: capturing after would answer 8 and the test would pass for the wrong reason.
  /// </remarks>
  public void RaiseStartedAfterItAlreadyLeft(IEventAudioSource source)
  {
    int claimed;
    lock (Started)
    {
      claimed = GetPriorityUnlocked(source);
      _effective.Remove(source.Id);
      _active.RemoveAll(a => string.Equals(a.Id, source.Id, StringComparison.Ordinal));
    }

    RaiseStateChanged(
      source, isDucking: true, activeCount: 0, duckLevel: 100f,
      DuckingSourceTransition.Started, claimed);
  }

  /// <summary>Reproduces StopAllDuckingAsync's raise: IsDucking false and a NULL TriggeringSource.</summary>
  /// <remarks>
  /// ⚠ It also CLEARS the set, because the real StopAllDuckingAsync does — and since PHN-1f that is
  /// what makes this raise able to wake a D28 wait rather than merely be ignored by the preemption
  /// rule. A helper that raised AllCleared while leaving _active populated would let
  /// StopAllDuckingWakesAWaitingPlayback pass or fail for reasons unrelated to the wake.
  /// </remarks>
  public void RaiseStopAll()
  {
    lock (Started)
    {
      _active.Clear();
    }

    RaiseStateChanged(
      null, isDucking: false, activeCount: 0, duckLevel: 100f,
      DuckingSourceTransition.AllCleared, 0);
  }

  /// <summary>
  /// The single place this fake raises DuckingStateChanged, so every raise records which thread it
  /// happened on. That is what lets a test assert the subscriber resolved the priority SYNCHRONOUSLY
  /// rather than on a dispatched task — a distinction no outcome assertion can make, because both
  /// orderings produce the same result until a stop races the read.
  /// </summary>
  private void RaiseStateChanged(
    IEventAudioSource? source,
    bool isDucking,
    int activeCount,
    float duckLevel,
    DuckingSourceTransition transition,
    int triggeringSourcePriority)
  {
    var previous = Interlocked.Exchange(ref _raisingThreadId, Environment.CurrentManagedThreadId);
    try
    {
      DuckingStateChanged?.Invoke(this, new DuckingStateChangedEventArgs
      {
        IsDucking = isDucking,
        TriggeringSource = source,
        ActiveEventCount = activeCount,
        DuckLevel = duckLevel,
        Transition = transition,
        TriggeringSourcePriority = triggeringSourcePriority
      });
    }
    finally
    {
      Interlocked.Exchange(ref _raisingThreadId, previous);
    }
  }

  /// <summary>GetPriority's body without the lock, for callers that already hold it.</summary>
  private int GetPriorityUnlocked(IAudioSource s) =>
    _effective.TryGetValue(s.Id, out var priority) ? priority : DuckingService.DefaultEventPriority;
}

/// <summary>An HttpMessageHandler that answers from a function. No network, no timing.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
  private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

  public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    : this((request, _) => Task.FromResult(respond(request)))
  {
  }

  public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    => _respond = respond;

  public int Calls { get; private set; }

  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
  {
    Calls++;
    return _respond(request, cancellationToken);
  }
}
