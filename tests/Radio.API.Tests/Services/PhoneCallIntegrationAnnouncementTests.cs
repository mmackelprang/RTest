using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Hubs;
using Radio.API.Services;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.External;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.Sources.Events;
using Radio.Infrastructure.External;

namespace Radio.API.Tests.Services;

/// <summary>
/// AUD-87 (owner ruling 2026-10-02): the phone's caller-name announcement defaults to priority 9, one
/// above a notification's default 8, and a hang-up stops only the phone's own announcement.
/// </summary>
/// <remarks>
/// <para>
/// Drives the REAL <see cref="PhoneCallIntegrationService.HandleCallStateChangedAsync"/> against the REAL
/// <see cref="AnnouncementService"/>; only TTS synthesis, ducking and the sources are faked. The
/// arbitration under test lives in the announcement service, and the stop under test lives in the phone
/// service, so a double of either would test nothing.
/// </para>
/// <para>
/// Nothing here races a clock. "Speaking" is a signal raised from inside a fake source's
/// <c>PlayAsync</c>; every end is awaited. <see cref="HangGuard"/> only bounds how long a BROKEN build
/// takes to fail — in a passing run every awaited task has already completed or completes without
/// waiting on time, so starvation can make a correct build slower but never red.
/// </para>
/// <para>
/// <c>NotificationsController.Announce</c> uses <c>request.Priority ?? 8</c>; <see cref="DoorbellPriority"/>
/// mirrors that literal, which this test cannot read.
/// </para>
/// </remarks>
public class PhoneCallIntegrationAnnouncementTests
{
  private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);
  private const int DoorbellPriority = 8;

  private readonly List<string> _duckLog = [];
  private readonly Mock<IDuckingService> _ducking = new();

  // Gate on the FIRST broadcast only; null means no broadcast blocks.
  private TaskCompletionSource? _firstBroadcastGate;
  private int _broadcasts;

  public PhoneCallIntegrationAnnouncementTests()
  {
    _ducking
      .Setup(d => d.StartDuckingAsync(It.IsAny<IEventAudioSource>(), It.IsAny<CancellationToken>()))
      .Returns<IEventAudioSource, CancellationToken>((s, _) =>
      {
        lock (_duckLog) { _duckLog.Add("start " + s.Id); }
        return Task.CompletedTask;
      });
    _ducking
      .Setup(d => d.StopDuckingAsync(It.IsAny<IEventAudioSource>(), It.IsAny<CancellationToken>()))
      .Returns<IEventAudioSource, CancellationToken>((s, _) =>
      {
        lock (_duckLog) { _duckLog.Add("stop " + s.Id); }
        return Task.CompletedTask;
      });
  }

  [Fact]
  public async Task ADoorbellAtTheNotificationDefaultNoLongerCutsOffTheCallersName()
  {
    // (b) The phone's default AnnouncementPriority is 9. At 8 — the old default — the doorbell, asked
    // for later at the same priority, replaced the caller's name (AUD-73's newest-of-equal rule).
    // MUTATION: set PhoneIntegrationOptions.AnnouncementPriority back to 8 — the phone's PlayToken is
    // then cancelled when the doorbell becomes active; red.
    var phone = new FakeSource("phone", completesOnPlay: null);
    var doorbell = new FakeSource("doorbell", completesOnPlay: PlaybackCompletionReason.EndOfContent);
    var announcements = CreateAnnouncementService(FactoryReturning(phone.Object, doorbell.Object));
    var service = CreatePhoneService(announcements, new PhoneIntegrationOptions());

    var ringing = service.HandleCallStateChangedAsync(Ringing());
    await phone.Playing.WaitAsync(HangGuard);

    var doorbellOutcome = await announcements.AnnounceAsync("someone is at the door", DoorbellPriority)
      .WaitAsync(HangGuard);

    Assert.Equal(AnnouncementOutcome.Completed, doorbellOutcome);
    Assert.False(phone.PlayToken.IsCancellationRequested, "the doorbell cut off the caller's name");
    Assert.False(ringing.IsCompleted, "the caller's name must still be speaking");

    await service.HandleCallStateChangedAsync(Ended()).WaitAsync(HangGuard);
    await ringing.WaitAsync(HangGuard);
  }

  [Fact]
  public async Task AHangUpStopsThePhonesAnnouncementAndLeavesAConcurrentDoorbellPlaying()
  {
    // (c) The hang-up stops only the phone's announcement. Also pins (a): the lower-priority doorbell
    // MIXES with the caller's name — it neither waits nor is dropped.
    // MUTATION 1: make the Ended arm call _announcementService.StopAsync() again (the pre-AUD-87
    // behaviour) — the doorbell is then stopped too; red.
    // MUTATION 2: stop passing announcementToken to AnnounceAsync — the hang-up then reaches nothing, and
    // the ringing handler never returns (HangGuard); red.
    var phone = new FakeSource("phone", completesOnPlay: null);
    var doorbell = new FakeSource("doorbell", completesOnPlay: null);
    var announcements = CreateAnnouncementService(FactoryReturning(phone.Object, doorbell.Object));
    var service = CreatePhoneService(announcements, new PhoneIntegrationOptions());

    var ringing = service.HandleCallStateChangedAsync(Ringing());
    await phone.Playing.WaitAsync(HangGuard);

    var doorbellTask = announcements.AnnounceAsync("someone is at the door", DoorbellPriority);
    await doorbell.Playing.WaitAsync(HangGuard);
    Assert.False(phone.PlayToken.IsCancellationRequested, "(a) a lower priority must mix, not replace");

    await service.HandleCallStateChangedAsync(Ended()).WaitAsync(HangGuard);
    await ringing.WaitAsync(HangGuard);

    phone.Mock.Verify(s => s.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    Assert.False(doorbell.PlayToken.IsCancellationRequested, "the hang-up silenced the doorbell");
    Assert.False(doorbellTask.IsCompleted, "the doorbell must still be playing after the hang-up");
    doorbell.Mock.Verify(s => s.StopAsync(It.IsAny<CancellationToken>()), Times.Never);

    // The stop-all path still exists and still stops everything.
    await announcements.StopAsync();
    Assert.Equal(AnnouncementOutcome.Interrupted, await doorbellTask.WaitAsync(HangGuard));
  }

  [Fact]
  public async Task AHangUpWhileTheNameIsStillSynthesisingNeitherPlaysNorDucks()
  {
    // The stop path StopAsync's stop-generation used to cover: hung up while TTS is in flight. The
    // fake synthesis ignores its token, as a slow engine may. MUTATION: remove
    // `token.IsCancellationRequested ||` from AnnounceAsync's pre-duck check — the announcement is still
    // refused, but only after "start phone" is logged; red.
    var releaseSynthesis = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var phone = new FakeSource("phone", completesOnPlay: PlaybackCompletionReason.EndOfContent);
    var factory = new Mock<ITTSFactory>();
    factory
      .Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<TTSParameters>(), It.IsAny<CancellationToken>()))
      .Returns<string, TTSParameters?, CancellationToken>(async (_, _, _) =>
      {
        await releaseSynthesis.Task;
        return phone.Object;
      });
    var announcements = CreateAnnouncementService(factory);
    var service = CreatePhoneService(announcements, new PhoneIntegrationOptions());

    var ringing = service.HandleCallStateChangedAsync(Ringing());
    await service.HandleCallStateChangedAsync(Ended()).WaitAsync(HangGuard);
    releaseSynthesis.SetResult();
    await ringing.WaitAsync(HangGuard);

    Assert.False(phone.Playing.IsCompleted, "an announcement hung up on must not play");
    lock (_duckLog) { Assert.DoesNotContain("start phone", _duckLog); }
    // Its source was still cleaned up.
    phone.Mock.Verify(s => s.DisposeAsync(), Times.Once);
  }

  [Fact]
  public async Task AHangUpThatArrivesWhileTheRingIsStillBroadcastingStillReachesTheAnnouncement()
  {
    // The call's token source is swapped BEFORE the broadcast await, so event order — not continuation
    // order — decides. Here the ringing event is held in its broadcast, the hang-up completes, and then
    // the ringing event resumes: it must not announce. MUTATION: move the switch that creates/cancels the
    // CTS below `await BroadcastPhoneStateAsync(e)` — the hang-up then finds no CTS, the ringing event
    // creates a fresh one, and the caller's name plays after the call ended; red.
    _firstBroadcastGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var phone = new FakeSource("phone", completesOnPlay: PlaybackCompletionReason.EndOfContent);
    var factory = FactoryReturning(phone.Object);
    var announcements = CreateAnnouncementService(factory);
    var service = CreatePhoneService(announcements, new PhoneIntegrationOptions());

    var ringing = service.HandleCallStateChangedAsync(Ringing());
    await service.HandleCallStateChangedAsync(Ended()).WaitAsync(HangGuard);
    _firstBroadcastGate.SetResult();
    await ringing.WaitAsync(HangGuard);

    Assert.False(phone.Playing.IsCompleted, "the caller's name played after the call had ended");
    factory.Verify(
      f => f.CreateAsync(It.IsAny<string>(), It.IsAny<TTSParameters>(), It.IsAny<CancellationToken>()),
      Times.Never);
  }

  private static PhoneCallStateChangedEventArgs Ringing() => new()
  {
    State = PhoneCallState.Ringing,
    PhoneNumber = "5550100000",
    CallerName = "A Caller"
  };

  private static PhoneCallStateChangedEventArgs Ended() => new() { State = PhoneCallState.Ended };

  private static Mock<ITTSFactory> FactoryReturning(params IEventAudioSource[] sources)
  {
    var queue = new Queue<IEventAudioSource>(sources);
    var factory = new Mock<ITTSFactory>();
    factory
      .Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<TTSParameters>(), It.IsAny<CancellationToken>()))
      .Returns(() =>
      {
        lock (queue) { return Task.FromResult(queue.Dequeue()); }
      });
    return factory;
  }

  private AnnouncementService CreateAnnouncementService(Mock<ITTSFactory> factory)
  {
    var options = new Mock<IOptionsMonitor<FilePlayerOptions>>();
    options.SetupGet(o => o.CurrentValue).Returns(new FilePlayerOptions());
    var fileFactory = new AudioFileEventSourceFactory(
      NullLogger<AudioFileEventSourceFactory>.Instance,
      NullLogger<AudioFileEventSource>.Instance,
      options.Object);

    return new AnnouncementService(
      NullLogger<AnnouncementService>.Instance, factory.Object, _ducking.Object, fileFactory);
  }

  private PhoneCallIntegrationService CreatePhoneService(
    IAnnouncementService announcements, PhoneIntegrationOptions options)
  {
    var phoneClient = new Mock<IPhoneIntegrationService>();
    phoneClient
      .Setup(p => p.ReportCallerResolvedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);

    var monitor = new Mock<IOptionsMonitor<PhoneIntegrationOptions>>();
    monitor.SetupGet(o => o.CurrentValue).Returns(options);
    // Every test supplies a CallerName, so no lookup is made; a request would throw.
    var contactLookup = new PhoneContactLookupService(
      NullLogger<PhoneContactLookupService>.Instance, monitor.Object,
      new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://unused.invalid") });

    var proxy = new Mock<IClientProxy>();
    proxy
      .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
      .Returns(() =>
      {
        var gate = _firstBroadcastGate;
        return Interlocked.Increment(ref _broadcasts) == 1 && gate != null ? gate.Task : Task.CompletedTask;
      });
    var clients = new Mock<IHubClients>();
    clients.SetupGet(c => c.All).Returns(proxy.Object);
    var hub = new Mock<IHubContext<AudioStateHub>>();
    hub.SetupGet(h => h.Clients).Returns(clients.Object);

    return new PhoneCallIntegrationService(
      NullLogger<PhoneCallIntegrationService>.Instance,
      phoneClient.Object,
      contactLookup,
      announcements,
      hub.Object,
      Options.Create(options));
  }

  private sealed class ThrowingHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken) =>
      throw new HttpRequestException("PhoneCallIntegrationAnnouncementTests must not reach the network.");
  }

  /// <summary>
  /// An event source whose <c>PlayAsync</c> signals <see cref="Playing"/> and then either raises
  /// PlaybackCompleted with the given reason or, for null, keeps "speaking" until stopped. StopAsync
  /// raises UserStopped, as the real sources do.
  /// </summary>
  private sealed class FakeSource
  {
    private readonly TaskCompletionSource _playing = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeSource(string id, PlaybackCompletionReason? completesOnPlay)
    {
      Mock.SetupGet(s => s.Id).Returns(id);
      Mock.SetupGet(s => s.Name).Returns("TTS event " + id);
      Mock
        .Setup(s => s.PlayAsync(It.IsAny<CancellationToken>()))
        .Returns<CancellationToken>(ct =>
        {
          PlayToken = ct;
          _playing.TrySetResult();
          if (completesOnPlay is { } reason)
          {
            Raise(reason);
          }
          return Task.CompletedTask;
        });
      Mock
        .Setup(s => s.StopAsync(It.IsAny<CancellationToken>()))
        .Returns(() =>
        {
          Raise(PlaybackCompletionReason.UserStopped);
          return Task.CompletedTask;
        });
    }

    public Mock<IEventAudioSource> Mock { get; } = new();

    public IEventAudioSource Object => Mock.Object;

    public Task Playing => _playing.Task;

    /// <summary>The token the service passed to PlayAsync — cancelled when this announcement is stopped.</summary>
    public CancellationToken PlayToken { get; private set; }

    private void Raise(PlaybackCompletionReason reason) =>
      Mock.Raise(s => s.PlaybackCompleted += null, Mock.Object,
        new AudioSourceCompletedEventArgs { SourceId = Mock.Object.Id, Reason = reason });
  }
}
