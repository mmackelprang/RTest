using System.Net;
using Microsoft.Extensions.Logging;
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
    CapturingLoggerProvider? logs = null)
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
      client);
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
  {
    var tcs = new TaskCompletionSource<EventPlaybackSnapshot>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    service.PlaybackChanged += (_, s) =>
    {
      if (s.State == state)
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
    Assert.Null(snapshot.Duration);              // no audio exists yet, so no duration is known
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

    source.RaiseCompleted(PlaybackCompletionReason.EndOfContent);
    source.RaiseCompleted(PlaybackCompletionReason.UserStopped);

    await WaitUntilAsync(
      () => service.Current is
      {
        State: EventPlaybackState.Completed or EventPlaybackState.Stopped
          or EventPlaybackState.Failed
      },
      TimeSpan.FromSeconds(5));

    lock (terminals)
    {
      Assert.Single(terminals);
      // The FIRST one wins. A guard that let the last write through would report Stopped for a
      // playback that ran to the end.
      Assert.Equal(EventPlaybackState.Completed, terminals[0].State);
    }

    // The second half of the same claim: without the guard the second completion runs its own
    // teardown, so these counts would be 2 rather than 1.
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
    Assert.False(await service.StopAsync(accepted.Id));   // idempotent through ClaimTerminal
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
    // the replacement occupies. Two guards make that true and this pins the observable outcome of
    // both: the replaced playback's terminal flag was already claimed by StartAsync, and the
    // ReferenceEquals check on the completion path would refuse to clear a slot it does not own.
    var first = new FakeEventSource();
    var second = new FakeEventSource();
    var queue = new Queue<IEventAudioSource>([first, second]);
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult(queue.Dequeue()) };
    using var service = CreateService(ttsFactory: tts);

    var firstPlaying = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await firstPlaying.WaitAsync(TimeSpan.FromSeconds(5));

    var two = await service.StartAsync(SpeechRequest());
    await WaitUntilAsync(() => second.PlayCalls == 1, TimeSpan.FromSeconds(5));

    first.RaiseCompleted(PlaybackCompletionReason.EndOfContent);

    // Nothing to wait FOR — the assertion is that a thing does not happen. Give the completion a
    // bounded chance to be processed by waiting on an observation the same handler would make.
    await WaitUntilAsync(() => first.StopCalls >= 1, TimeSpan.FromSeconds(5));

    Assert.Equal(two.Id, service.Current?.Id);
    Assert.Equal(EventPlaybackState.Playing, service.Current?.State);
  }

  // ── the TTSParameters pin ───────────────────────────────────────────────

  [Fact]
  public async Task SpeechFillsAllFourTtsParametersFromConfiguration()
  {
    // ⚠ THE C-25 PIN, and it must assert all four. TTSFactory resolves each field as
    // "parameters?.X ?? opts.X", and all four ?? are lifted by the null-conditional on the OBJECT —
    // so a partially-filled TTSParameters pins whatever it left unset. Two of the four still carry
    // that trap after TTS-9: Speed and Pitch are non-nullable with a 1.0f initializer.
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
  public async Task ASpeechSnapshotReportsPositionZeroForItsWholeLife()
  {
    // ⚠ THE C-27 HONESTY PIN, and it asserts the CURRENT behaviour rather than the desirable one.
    // The first assertion is the real one: TTSEventSource does not override Position, so it
    // inherits EventAudioSourceBase's TimeSpan.Zero for the whole playback. When PR 5 adds the
    // three-line override, THIS is what fails, which is how it should be found — update it, do not
    // delete it.
    var positionGetter = typeof(TTSEventSource).GetProperty(nameof(IEventAudioSource.Position))!
      .GetGetMethod()!;
    Assert.Equal(typeof(EventAudioSourceBase), positionGetter.DeclaringType);

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
    // exact about: this service takes no mixer at all, so there is no call to record. What this
    // pins is that it cannot acquire one — no constructor parameter and no field can hold an
    // IMasterMixer or a SoundFlow playback service. A grep over the file is the textual half.
    var constructor = Assert.Single(typeof(EventPlaybackService).GetConstructors());

    Assert.DoesNotContain(
      constructor.GetParameters(), p => typeof(IMasterMixer).IsAssignableFrom(p.ParameterType));

    var fields = typeof(EventPlaybackService).GetFields(
      System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
      | System.Reflection.BindingFlags.Public);

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

  public TimeSpan? SoughtTo { get; private set; }

  public event EventHandler<AudioSourceStateChangedEventArgs>? StateChanged;

  public event EventHandler<AudioSourceCompletedEventArgs>? PlaybackCompleted;

  public object GetSoundComponent() => this;

  public Task PlayAsync(CancellationToken cancellationToken = default)
  {
    PlayCalls++;
    State = AudioSourceState.Playing;
    return Task.CompletedTask;
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
    State = AudioSourceState.Stopped;
    return Task.CompletedTask;
  }

  public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
  {
    SoughtTo = position;
    return Task.CompletedTask;
  }

  public ValueTask DisposeAsync()
  {
    DisposeCalls++;
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

/// <summary>Records what the seam asked of ducking. Asserts nothing on its own.</summary>
/// <remarks>
/// ⚠ It raises both its events, even though nothing in PR 3 subscribes. That is deliberate: PR 4 is
/// the PR that subscribes to DuckingStateChanged, and a fake that never raised it would let PR 4
/// add a subscription that deadlocks or re-enters without any existing test noticing.
/// </remarks>
internal sealed class FakeDuckingService : IDuckingService
{
  public List<(string Id, int Priority)> Priorities { get; } = [];

  public List<string> Started { get; } = [];

  public List<IEventAudioSource> StartedSources { get; } = [];

  public List<string> Stopped { get; } = [];

  public float CurrentDuckLevel => 100f;

  public bool IsDucking => Started.Count > Stopped.Count;

  public int ActiveEventCount => Started.Count - Stopped.Count;

  public event EventHandler<DuckingStateChangedEventArgs>? DuckingStateChanged;

  public event EventHandler<DuckingLevelChangedEventArgs>? DuckingLevelChanged;

  public Task StartDuckingAsync(IEventAudioSource s, CancellationToken cancellationToken = default)
  {
    lock (Started)
    {
      Started.Add(s.Id);
      StartedSources.Add(s);
    }
    DuckingStateChanged?.Invoke(this, new DuckingStateChangedEventArgs { IsDucking = true });
    return Task.CompletedTask;
  }

  public Task StopDuckingAsync(IEventAudioSource s, CancellationToken cancellationToken = default)
  {
    lock (Started)
    {
      Stopped.Add(s.Id);
    }
    DuckingLevelChanged?.Invoke(this, new DuckingLevelChangedEventArgs { TransitionComplete = true });
    return Task.CompletedTask;
  }

  public Task StopAllDuckingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

  public int GetPriority(IAudioSource s)
  {
    lock (Started)
    {
      var match = Priorities.LastOrDefault(p => p.Id == s.Id);
      return match.Priority > 0 ? match.Priority : 8;
    }
  }

  public void SetPriority(IAudioSource s, int priority)
  {
    lock (Started)
    {
      Priorities.Add((s.Id, priority));
    }
  }

  public IReadOnlyList<IEventAudioSource> GetActiveEventsByPriority() =>
    Array.Empty<IEventAudioSource>();

  public void Dispose()
  {
  }
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
