using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Hubs;
using Radio.API.Models;
using Radio.API.Services;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.API.Tests.Services;

/// <summary>
/// The cache-advance ordering contract of <see cref="AudioStateUpdateService"/> (queue row `UI-13`):
/// a change-detection cache must never be advanced past a broadcast that did not complete.
/// </summary>
/// <remarks>
/// ⚠⚠ WHAT THESE DO NOT PROVE, SAID PLAINLY BECAUSE THE ROW ASSUMED OTHERWISE. They do NOT show a live
/// defect. Measured 2026-09-09 against Microsoft.AspNetCore.App 10.0.11 with a real Kestrel host: the
/// default in-process DefaultHubLifetimeManager cannot fault a broadcast for a dead client, a dead
/// circuit, an unserializable payload or an absent group — HubConnectionContext.cs:341-349 converts
/// every one of those into a successful FlushResult and aborts the CONNECTION instead. The only escape
/// is an OperationCanceledException while the caller's token is cancelled (the filter at :361), and on
/// this path that token is ExecuteAsync's stoppingToken, cancelled only at host shutdown — where
/// AudioStateUpdateService.cs:196 breaks the loop and the caches are discarded anyway.
///
/// These pin the ORDERING, so that the day a backplane or a different token makes the fault reachable,
/// it is already handled rather than silently live. See `UI-13` §0.1-§0.2.
///
/// ⚠ NO ASSERTION HERE USES A WALL CLOCK — CLAUDE.md § *Test Timing*. Each Check*Async is invoked
/// directly and awaited to completion before anything is asserted, so every observation is a fact
/// about control flow. ⛔ Do NOT introduce a TimeProvider seam for these: the defect is in the
/// Check*Async bodies, which read no clock, and ExecuteAsync's Task.Delay is never entered (§0.6).
/// </remarks>
public class AudioStateUpdateServiceCacheOrderingTests
{
  // ─── the fake hub ────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Records every attempted send and fails exactly the way DefaultHubLifetimeManager can: by
  /// honouring a cancelled caller token. The attempt is recorded BEFORE the throw, because the
  /// question these tests ask is "was the send attempted and did it fail", not "did it succeed".
  /// </summary>
  private sealed class RecordingClientProxy : IClientProxy
  {
    public List<string> Methods { get; } = [];

    public List<object?> Payloads { get; } = [];

    public int Attempts => Methods.Count;

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
      Methods.Add(method);
      Payloads.Add(args.Length > 0 ? args[0] : null);
      cancellationToken.ThrowIfCancellationRequested();
      return Task.CompletedTask;
    }
  }

  private static (AudioStateUpdateService Service, RecordingClientProxy Proxy) CreateService(
    IAudioManager? audioManager = null)
  {
    var proxy = new RecordingClientProxy();
    var clients = new Mock<IHubClients>();
    clients.SetupGet(c => c.All).Returns(proxy);
    clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy);

    var hubContext = new Mock<IHubContext<AudioStateHub>>();
    hubContext.SetupGet(h => h.Clients).Returns(clients.Object);

    var collection = new ServiceCollection();
    collection.AddSingleton(audioManager ?? StableAudioManager());

    var service = new AudioStateUpdateService(
      NullLogger<AudioStateUpdateService>.Instance,
      hubContext.Object,
      collection.BuildServiceProvider(),
      new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());

    return (service, proxy);
  }

  /// <summary>Constant readings, so a second tick with an unchanged world compares equal.</summary>
  private static IAudioManager StableAudioManager()
  {
    var mock = new Mock<IAudioManager>();
    mock.SetupGet(m => m.MasterVolume).Returns(0.42f);
    mock.SetupGet(m => m.IsMuted).Returns(false);
    mock.SetupGet(m => m.Balance).Returns(0.0f);
    return mock.Object;
  }

  // ─── reflection helpers, matching the idiom in AudioStateUpdateServiceTests ───────────────────

  private static Task InvokeCheck(
    AudioStateUpdateService svc, string name, params object?[] args)
  {
    var m = typeof(AudioStateUpdateService).GetMethod(
      name, BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(m);
    return (Task)m!.Invoke(svc, args)!;
  }

  private static CancellationToken Cancelled()
  {
    var cts = new CancellationTokenSource();
    cts.Cancel();
    return cts.Token;
  }

  // ─── source doubles ──────────────────────────────────────────────────────────────────────────

  private static Mock<IAudioSource> BareSource(AudioSourceType type = AudioSourceType.Radio)
  {
    var mock = new Mock<IAudioSource>();
    mock.SetupGet(s => s.Id).Returns("src-1");
    mock.SetupGet(s => s.Name).Returns("Test Source");
    mock.SetupGet(s => s.Type).Returns(type);
    mock.SetupGet(s => s.Category).Returns(AudioSourceCategory.Primary);
    mock.SetupGet(s => s.State).Returns(AudioSourceState.Playing);
    mock.SetupGet(s => s.Volume).Returns(1.0f);
    return mock;
  }

  private static QueueItem Item(string id, int index) => new()
  {
    Id = id,
    Title = $"Track {index}",
    Artist = "Test Artist",
    Album = "Test Album",
    Index = index,
    FullPlaylistIndex = index,
  };

  // ─── the six sites ───────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// ⭐ THE HEADLINE, and the cheapest of the six. Site :502.
  /// Tick 1 attempts VolumeChanged and is cancelled. Tick 2 sees an unchanged world; because the
  /// cache was never advanced past the failed send, it must re-broadcast.
  /// On `main` the cache advanced at :502 before the await, so tick 2 compares equal and sends
  /// nothing — Attempts is 1, not 2.
  /// </summary>
  [Fact]
  public async Task VolumeDeltaIsReSentAfterACancelledBroadcast()
  {
    var (svc, proxy) = CreateService();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => InvokeCheck(svc, "CheckVolumeAsync", Cancelled()));

    await InvokeCheck(svc, "CheckVolumeAsync", CancellationToken.None);

    Assert.Equal(2, proxy.Attempts);
    Assert.Equal(["VolumeChanged", "VolumeChanged"], proxy.Methods);
    svc.Dispose();
  }

  /// <summary>Site :266.</summary>
  [Fact]
  public async Task PlaybackStateDeltaIsReSentAfterACancelledBroadcast()
  {
    var (svc, proxy) = CreateService();
    var source = BareSource().Object;

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => InvokeCheck(svc, "CheckPlaybackStateAsync", source, Cancelled()));

    await InvokeCheck(svc, "CheckPlaybackStateAsync", source, CancellationToken.None);

    Assert.Equal(2, proxy.Attempts);
    Assert.Equal(["PlaybackStateChanged", "PlaybackStateChanged"], proxy.Methods);
    svc.Dispose();
  }

  /// <summary>Site :279.</summary>
  [Fact]
  public async Task NowPlayingDeltaIsReSentAfterACancelledBroadcast()
  {
    var (svc, proxy) = CreateService();
    var source = BareSource().Object;

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => InvokeCheck(svc, "CheckNowPlayingAsync", source, Cancelled()));

    await InvokeCheck(svc, "CheckNowPlayingAsync", source, CancellationToken.None);

    Assert.Equal(2, proxy.Attempts);
    Assert.Equal(["NowPlayingChanged", "NowPlayingChanged"], proxy.Methods);
    svc.Dispose();
  }

  /// <summary>
  /// Site :249 — ⭐ THE ONE THE ROW MISSED, and structurally the odd one out (§0.3).
  /// Three calls, not two: the first poll establishes the baseline WITHOUT broadcasting, so the
  /// failed send has to be the second call and the retry the third.
  /// </summary>
  [Fact]
  public async Task SourceChangedDeltaIsReSentAfterACancelledBroadcast()
  {
    var (svc, proxy) = CreateService();
    var radio = BareSource(AudioSourceType.Radio).Object;
    var bluetooth = BareSource(AudioSourceType.Bluetooth).Object;

    // Tick 1 — first poll. Baseline only, no broadcast.
    await InvokeCheck(svc, "CheckSourceChangedAsync", radio, CancellationToken.None);
    Assert.Equal(0, proxy.Attempts);

    // Tick 2 — the source changed, so this broadcasts. Cancelled.
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => InvokeCheck(svc, "CheckSourceChangedAsync", bluetooth, Cancelled()));

    // Tick 3 — the world is unchanged since tick 2, so only a non-advanced cache re-sends.
    await InvokeCheck(svc, "CheckSourceChangedAsync", bluetooth, CancellationToken.None);

    Assert.Equal(2, proxy.Attempts);
    Assert.Equal(["SourceChanged", "SourceChanged"], proxy.Methods);
    svc.Dispose();
  }

  /// <summary>Site :453 — the live half of the row's ":453-454" (§0.3).</summary>
  [Fact]
  public async Task QueueDeltaIsReSentAfterACancelledBroadcast()
  {
    var (svc, proxy) = CreateService();
    var playlist = (IReadOnlyList<QueueItem>)new List<QueueItem> { Item("a", 0), Item("b", 1) };

    var mock = BareSource(AudioSourceType.FilePlayer);
    mock.As<IPlayQueue>()
      .Setup(q => q.GetFullPlaylistAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(playlist);
    var source = mock.Object;

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => InvokeCheck(svc, "CheckQueueAsync", source, Cancelled()));

    await InvokeCheck(svc, "CheckQueueAsync", source, CancellationToken.None);

    Assert.Equal(2, proxy.Attempts);
    Assert.Equal(["QueueChanged", "QueueChanged"], proxy.Methods);
    svc.Dispose();
  }

  /// <summary>
  /// Site :477 — the row's headline line.
  /// ⭐ The second assertion is the one that would otherwise be missed: the re-sent DTO must carry
  /// RdsRelevantChanged = true, exactly as the send that failed did. That flag is computed against
  /// _lastRadioState (:475), so an advanced cache would have made the retry — if there were one —
  /// arrive with the flag FALSE, and the Web RDS accumulator would have skipped it.
  /// </summary>
  [Fact]
  public async Task RadioStateDeltaIsReSentAfterACancelledBroadcastWithItsRdsFlagIntact()
  {
    var (svc, proxy) = CreateService();

    var mock = BareSource(AudioSourceType.Radio);
    var radio = mock.As<IRadioControl>();
    radio.SetupGet(r => r.CurrentFrequency).Returns(Frequency.FromMegahertz(105.1));
    radio.SetupGet(r => r.CurrentBand).Returns(RadioBand.FM);
    radio.SetupGet(r => r.FrequencyStep).Returns(Frequency.FromKilohertz(200));
    radio.SetupGet(r => r.SignalStrength).Returns(60);
    radio.SetupGet(r => r.RdsRadioText).Returns("Hotel California");
    var source = mock.Object;

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => InvokeCheck(svc, "CheckRadioStateAsync", source, Cancelled()));

    await InvokeCheck(svc, "CheckRadioStateAsync", source, CancellationToken.None);

    Assert.Equal(2, proxy.Attempts);
    Assert.Equal(["RadioStateChanged", "RadioStateChanged"], proxy.Methods);

    var resent = Assert.IsType<RadioStateDto>(proxy.Payloads[1]);
    Assert.True(
      resent.RdsRelevantChanged,
      "the re-sent delta must still be RDS-relevant — it is the same delta the clients never got");
    svc.Dispose();
  }

  // ─── the regression half: the happy path must not have been broken ───────────────────────────

  /// <summary>
  /// ⚠ Without this, all six tests above would still pass against an implementation that simply
  /// never advanced the caches at all — which would re-broadcast every unchanged state twice a
  /// second forever, on a box where CPU churn is audible. Task 1 moves the assignment; it does not
  /// delete it.
  /// </summary>
  [Fact]
  public async Task AnUnchangedWorldIsBroadcastExactlyOnceWhenNothingFails()
  {
    var (svc, proxy) = CreateService();

    await InvokeCheck(svc, "CheckVolumeAsync", CancellationToken.None);
    await InvokeCheck(svc, "CheckVolumeAsync", CancellationToken.None);
    await InvokeCheck(svc, "CheckVolumeAsync", CancellationToken.None);

    Assert.Equal(1, proxy.Attempts);
    svc.Dispose();
  }

  /// <summary>The first poll must still establish the source baseline without broadcasting.</summary>
  [Fact]
  public async Task TheFirstSourcePollEstablishesTheBaselineWithoutBroadcasting()
  {
    var (svc, proxy) = CreateService();
    var radio = BareSource(AudioSourceType.Radio).Object;

    await InvokeCheck(svc, "CheckSourceChangedAsync", radio, CancellationToken.None);
    await InvokeCheck(svc, "CheckSourceChangedAsync", radio, CancellationToken.None);

    Assert.Equal(0, proxy.Attempts);
    svc.Dispose();
  }
}
