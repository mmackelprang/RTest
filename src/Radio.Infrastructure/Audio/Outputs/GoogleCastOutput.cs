using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Extensions;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Metrics;
using Sharpcaster;
using Sharpcaster.Channels;
using Sharpcaster.Models;
using Sharpcaster.Models.ChromecastStatus;
using Sharpcaster.Models.Media;

namespace Radio.Infrastructure.Audio.Outputs;

/// <summary>
/// Google Chromecast audio output implementation using SharpCaster.
/// Streams audio to Chromecast devices via HTTP stream endpoint.
/// </summary>
public class GoogleCastOutput : AudioOutputBase
{
  private readonly ILogger<GoogleCastOutput> _logger;
  private readonly GoogleCastOutputOptions _options;
  private readonly CastDeviceCacheRepository? _cacheRepository;
  private readonly IMetricsCollector? _metricsCollector;
  private readonly ICastDeviceVolumeStore? _volumeStore;
  private ChromecastClient? _client;
  private ChromecastReceiver? _connectedReceiver;

  // Serializes the connection-state SWAPS of _client / _connectedReceiver /
  // ConnectedDevice — not every access of them. (An earlier revision of this
  // comment said it guarded every read. It never did, and believing it makes the
  // unlocked dereferences described below look safer than they actually are.)
  //
  // WHAT IT SERIALIZES: nine await-free critical sections, each touching
  // _connectionGeneration and/or those three fields as one consistent unit:
  //     InitializeAsync         bump, install a fresh client, clear receiver+device
  //     ConnectAsync's claim    bump, snapshot _client
  //     TryPublishConnection    generation check, then publish client+receiver+device
  //     IsCurrentGeneration     generation check only — mutates nothing
  //     DisconnectAsync         bump, snapshot, clear receiver+device
  //     DisposeAsync            bump, snapshot _client (deliberately NOT clearing it)
  //     StopAsync               snapshot _client and _publishedGeneration — mutates nothing
  //     HandleConnectionLost    generation+published check, bump, snapshot, clear
  //                             receiver+device (AUD-84; also leaves _client set)
  //     SnapshotPublished       generation+published check, snapshot client+device —
  //                             mutates nothing (AUD-81: the console volume/mute
  //                             commands, the live status read and the target accessor;
  //                             the only section also entered with a synchronous Wait)
  // _publishedGeneration and the loss-watch pair (_lossWatchedClient/_lossWatchHandler)
  // are written only inside these sections too, alongside the fields they describe.
  // Being await-free is the point: no writer can be preempted mid-swap, so no
  // reader THAT TAKES THE LOCK can observe a half-applied connection. Readers
  // that skip the lock get no such guarantee — see below for why that is sound.
  //
  // Held ONLY for those swaps — never across a SharpCaster network call. That is
  // deliberate: a connect can sit for tens of seconds inside calls that do not
  // observe cancellation, and a teardown that had to queue behind it would turn a
  // data race into a hang. Instead teardown takes the state out from under the
  // connect and does its network work on a snapshot.
  //
  // READS ARE DELIBERATELY UNSYNCHRONIZED. Most reads of these fields — across
  // the start/stream/teardown paths — take no lock at all. Some null-check a field
  // and then dereference it on a second, separate read: TestPlayUrlAsync, and the
  // `_client!` dereferences in the Start/stream helpers. (SyncInitialVolumeAsync was
  // one until AUD-5 — it now takes its client as a parameter — and SetCastVolumeAsync
  // and SetCastMuteAsync were two more until AUD-81 replaced them with the console
  // volume/mute commands, which take their client from the SnapshotPublished section.)
  // Reference assignment is
  // atomic, so such a read always yields a whole reference — but "whole" is not
  // "non-null", and check-then-dereference is sound only because of this:
  //
  //     PRECONDITION: _client IS NEVER SET BACK TO NULL. It is assigned in
  //     exactly two places — InitializeAsync and TryPublishConnectionAsync — and
  //     both assign a non-null client, so the field only ever moves null -> set
  //     -> set, never set -> null. DisconnectAsync, DisposeAsync and
  //     HandleConnectionLostAsync deliberately leave it set (see the note in
  //     DisposeAsync). A reader that has ONCE
  //     observed non-null can therefore never subsequently observe null; it may
  //     see a stale or a newer client, but not a null one.
  //
  //     Note what that does NOT say: the field IS null before the first
  //     successful InitializeAsync, and stays null if the ChromecastClient ctor
  //     throws. So the `_client!` dereferences are not licensed by this
  //     precondition alone — each sits behind a caller's null guard (StartAsync
  //     for the Start/stream chain, the reload guard for the metadata path).
  //     What the precondition buys them is that their guard stays valid across
  //     the awaits that follow it. TestPlayUrlAsync's check-then-dereference
  //     relies on it directly.
  //
  //     NULL THIS FIELD AND EVERY UNLOCKED DEREFERENCE ABOVE BECOMES AN NRE —
  //     snapshot it under the lock at each of those sites first.
  //
  // _connectedReceiver and ConnectedDevice ARE cleared (InitializeAsync,
  // DisconnectAsync — which DisposeAsync calls — and HandleConnectionLostAsync), so unlocked reads of those two can legitimately see null.
  // That is safe only because no unlocked site dereferences them —
  // _connectedReceiver is read purely as an "is anything connected" flag, and
  // ConnectedDevice only through `?.`. Keep it that way.
  //
  // What the unlocked reads do accept is a bounded race: a command aimed at a
  // client that has since been superseded or torn down. The console volume/mute
  // commands (AUD-81) narrow it — they send only to the connection the
  // SnapshotPublished section found current, and only for the generation the caller
  // asked for — but the lock is released before the network call, so a teardown
  // can still land between the snapshot and the send. That is genuinely cheap —
  // SharpCaster throws or the stale client ignores it, the surrounding try/catch
  // logs it, and the winning connection is untouched. A wasted network call, not
  // corrupt state.
  //
  // SyncInitialVolumeAsync WAS the exception to that reassurance, and AUD-5 addressed
  // it. It is a read rather than a command, and its success path fires
  // CastVolumeChanged — whose subscriber wrote AudioManager.MasterVolume, a setter
  // that schedules a persist. Nothing re-checked _connectionGeneration between the
  // status response and the event fire, so a teardown landing in that window could
  // publish the volume of a connection that was no longer current, and the console
  // kept it across a restart. Two things changed, and which one does what matters:
  //   - The method now takes its client AND its generation as parameters, and
  //     re-checks the generation immediately before the fire. That removes the network
  //     round-trip from the window. It does NOT make the window empty: the lock is
  //     released before the Invoke, because subscriber code must never run under it.
  //   - AudioStateUpdateService.OnCastVolumeChanged now ignores IsInitialSync events
  //     outright. THAT is what makes an initial read unable to move master volume, and
  //     it has no timing dependence at all.
  //   - AUD-81 found the side door: the same GET_STATUS response also arrives as a
  //     ReceiverStatusChanged event, which OnReceiverStatusChanged reported as EXTERNAL
  //     (IsInitialSync = false) when it beat the baseline priming. Statuses arriving
  //     while a connect's initial sync is in progress are now baseline-only — see
  //     _initialSyncPendingGeneration. That mark is an Interlocked int, not lock state.
  // Widening this lock was and remains the wrong fix: the exposure is the event fire,
  // not the field read, and holding it across a SharpCaster call is the hang described
  // above.
  private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

  // Bumped by anything that supersedes an in-flight connect: InitializeAsync, a newer
  // connect, a disconnect, disposal, or a handled connection loss (AUD-84). A connect captures this when it starts and
  // re-validates before publishing its result — if it changed, the attempt lost
  // the race and tears down what it built instead of overwriting the winner.
  private int _connectionGeneration;

  // AUD-84. The generation whose connection is currently PUBLISHED (connected and
  // visible through _connectedReceiver/ConnectedDevice), or -1 when none is. A loss
  // report carries the generation it was armed for and is acted on only if it is
  // both current AND published: a report for a connect still in flight, or for one
  // already torn down, is stale. Written under _lifecycleLock; read unlocked only by
  // StartDirectChannelAsync to arm the streaming loop's report, which is re-checked
  // under the lock when it fires.
  private int _publishedGeneration = -1;

  // AUD-84. The ChromecastClient.Disconnected subscription for the published
  // connection. Kept as a pair so it can be removed from exactly the client it was
  // added to — a client is reused across connects. Written under _lifecycleLock.
  private ChromecastClient? _lossWatchedClient;
  private EventHandler? _lossWatchHandler;

  /// <summary>
  /// <b>Test seam (kind C — substitution).</b> Awaited inside <see cref="ConnectAsync"/>
  /// after the receiver has been resolved but before the network connect, which is
  /// precisely where the connect/teardown race used to corrupt state. Set by
  /// <c>GoogleCastOutputConcurrencyTests</c> (<c>:51</c>, <c>:116</c>).
  /// <b>Why the real path is unreachable:</b> the window is microseconds wide, so a stress
  /// loop lands on it only by luck; the hook makes the interleaving deterministic.
  /// <b>NOT covered by this seam:</b> nothing — it inserts a pause, it does not replace a
  /// collaborator. The connect either side of it is the real one.
  /// Null (and therefore free) in production.
  /// </summary>
  internal Func<Task>? ConnectRaceHookForTests { get; set; }

  /// <summary>
  /// <b>Test seam (kind C — substitution).</b> Substitutes the SharpCaster transport
  /// connect. Set by <c>GoogleCastOutputConcurrencyTests:121</c>.
  /// <b>Why the real path is unreachable:</b> a fake socket can never complete a Cast
  /// handshake, so offline the connect always throws and diverts into the error handler —
  /// making the supersede-after-a-SUCCESSFUL-connect path, which is the whole point of the
  /// generation check, unreachable without hardware.
  /// <b>NOT covered by this seam:</b> the real SharpCaster handshake and everything its
  /// failure modes imply. The generation check, the publish and the teardown either side
  /// are real; the socket is not. Only hardware UAT covers the transport itself — and per
  /// <c>AUD-3</c> residue (a), that UAT has never been performed.
  /// Null (and therefore free) in production.
  /// </summary>
  internal Func<ChromecastReceiver, Task>? ConnectTransportOverrideForTests { get; set; }

  /// <summary>
  /// <b>Test seam (kind C — substitution).</b> Substitutes the Cast status read inside
  /// <see cref="SyncInitialVolumeAsync"/> and, since AUD-81, <see cref="ReadSpeakerVolumeAsync"/>.
  /// Set by <c>GoogleCastOutputConcurrencyTests</c> and <c>GoogleCastOutputConsoleVolumeTests</c>.
  /// <b>Why the real path is unreachable:</b> no fake socket can answer a Cast GET_STATUS,
  /// so offline the read always throws and the method diverts into its catch before the
  /// generation check is ever evaluated. Awaiting inside this delegate is also what lets a
  /// test interleave a teardown at exactly the point the network round-trip occupies in
  /// production.
  /// <b>NOT covered by this seam:</b> SharpCaster's status parsing. The generation check,
  /// the priming and the event fire either side of it are real.
  /// Null (and therefore free) in production.
  /// </summary>
  internal Func<Task<(float Volume, bool Muted)?>>? CastStatusReadOverrideForTests { get; set; }

  /// <summary>
  /// <b>Test seam (kind C — substitution).</b> Substitutes the Cast SET_VOLUME inside
  /// <see cref="PushVolumeToDeviceAsync"/>. Set by <c>GoogleCastOutputVolumeMemoryTests</c>,
  /// <c>GoogleCastOutputConsoleVolumeTests</c> and <c>CastConsoleVolumeFollowerTests</c>.
  /// <b>Why the real path is unreachable:</b> the same as
  /// <see cref="CastStatusReadOverrideForTests"/> — no fake socket speaks the Cast protocol.
  /// Awaiting inside the delegate is also how a test holds a push in flight to drive the
  /// AUD-81 coalescing deterministically.
  /// <b>NOT covered by this seam:</b> whether the device honours the level. The choice of
  /// level, the coalescing, and the echo-filter baseline and memory recorded before sending
  /// it are real.
  /// Null (and therefore free) in production.
  /// </summary>
  internal Func<float, Task>? CastSetVolumeOverrideForTests { get; set; }

  /// <summary>
  /// <b>Test seam (kind C — substitution).</b> Substitutes the Cast SET_MUTE inside
  /// <see cref="SendMuteToDeviceAsync"/> (AUD-81). Set by <c>GoogleCastOutputConsoleVolumeTests</c>
  /// and <c>CastConsoleVolumeFollowerTests</c>.
  /// <b>Why the real path is unreachable:</b> the same as
  /// <see cref="CastSetVolumeOverrideForTests"/> — no fake socket speaks the Cast protocol, so
  /// offline the send always throws before the "muted by console" bookkeeping it guards.
  /// <b>NOT covered by this seam:</b> whether the device honours the mute. Which mute is sent,
  /// when (console change, start of streaming, teardown), the echo memory and the
  /// muted-by-console mark around it are real.
  /// Null (and therefore free) in production.
  /// </summary>
  internal Func<bool, Task>? CastSetMuteOverrideForTests { get; set; }

  /// <summary>
  /// <b>Test seam (kind C — substitution).</b> Substitutes the receiver-application stop inside
  /// <see cref="StopReceiverApplicationAsync"/> (AUD-81, pre-merge review H1): returns whether no
  /// application of ours is left running, or throws. Set by <c>CastConsoleTestHarness</c>.
  /// <b>Why the real path is unreachable:</b> the same as
  /// <see cref="CastSetMuteOverrideForTests"/> — no fake socket speaks the Cast protocol, so the
  /// STOP is never answered.
  /// <b>NOT covered by this seam:</b> how the device's status is read to decide whether our
  /// application is running (checked against SharpCaster 3.0.0's decompiled
  /// <c>ReceiverChannel.StopApplication</c>, which stops the FIRST application in the status).
  /// WHEN the stop is attempted, that the unmute follows it, and that a failed stop leaves the
  /// speaker muted are real.
  /// Null (and therefore free) in production.
  /// </summary>
  internal Func<Task<bool>>? CastStopApplicationOverrideForTests { get; set; }
  private string? _streamUrl;

  // Direct Channel streaming (experimental)
  private IAudioEngine? _audioEngine;
  private DirectCastStreamingService? _directStreaming;
  private DirectCastAudioChannel? _directChannel;

  // Cache discovered receivers to use the original objects for connection
  // Indexed by device ID (DeviceUri.ToString()) and also by IP address for fallback matching.
  // AUD-54 (6): concurrent discoveries write these while a connect reads them, so they are
  // concurrent dictionaries. Each single operation is atomic; the two maps are NOT updated as
  // one unit, so a reader can see a device in one and not yet (or no longer) in the other.
  private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ChromecastReceiver> _discoveredReceivers = new();
  private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ChromecastReceiver> _discoveredReceiversByIp = new();
  private CastNowPlayingMetadata? _nowPlayingMetadata;

  // Metadata update debounce: when metadata changes rapidly (source switch
  // → "No Track" → actual track → fingerprint ID), coalesce into a single
  // Cast media reload to avoid garbled audio from rapid reconnections.
  private CancellationTokenSource? _metadataDebouncesCts;
  private readonly object _debounceLock = new();

  // Volume sync: the echo filter's baseline — the level and mute this application last set or
  // observed on the device. A status within 0.01 of it is not a change.
  //
  // AUD-81 removed the third field that used to live here, _suppressNextVolumeEvent. It
  // dropped the NEXT status after any set, whatever that status said — so it could swallow a
  // genuine external change — and it covered exactly one confirmation, so a dragged slider's
  // confirmations of intermediate levels (0.30, 0.32 while 0.34 was the latest) arrived as
  // external changes and were written back to master volume. The recent-push memory below
  // recognises a confirmation by its LEVEL, for every push in the window, and lets anything
  // else through; it subsumes the flag.
  private float _lastSetVolume = -1f;
  private bool _lastSetMute;

  // AUD-81 (connect race): the generation whose connect — including its initial sync — is still
  // in progress, or -1. Set by ConnectAsync at its claim, before any network work (hostile review
  // F1: a reused client still carries the handler, so a status can arrive during the transport
  // connect), and cleared (for that generation only) on every exit from the block that runs from
  // the claim through SyncInitialVolumeAsync. While it is set,
  // OnReceiverStatusChanged treats a status as part of the initial sync: it re-baselines the
  // echo filter and reports nothing. Measured on the box: SharpCaster raises
  // ReceiverStatusChanged for the GET_STATUS response on its receive thread, racing the
  // continuation that primes the baseline, so the handler saw the -1f sentinel and reported
  // the speaker's state as an EXTERNAL change — unmuting a muted console on connect, which is
  // exactly the write AUD-5's IsInitialSync rule exists to prevent. Accessed through
  // Volatile/Interlocked only.
  private int _initialSyncPendingGeneration = -1;

  // AUD-81 (pre-merge review L5): how many diagnostic live reads (ReadSpeakerVolumeAsync) are
  // in flight. SharpCaster raises their GET_STATUS responses as ReceiverStatusChanged too, so a
  // status arriving while one is pending is absorbed into the baseline exactly like one during
  // an initial sync — a diagnostic read must never move master volume. Interlocked only.
  private int _diagnosticReadsPending;

  // AUD-81: every level and mute this application sent to the device, kept while the send is
  // in flight and for EchoWindow after it completes, from any path (console follow, AUD-80
  // restore, after-start sync, start-time mute, teardown). A status matching one of them is the
  // device confirming our own command, even when it arrives late or out of order. For a level,
  // "matching" means lying within the range the recent levels span, widened by
  // EchoLevelTolerance (hostile review F3; see IsRecentVolumePush). Guarded by
  // _echoLock, which guards nothing else and is never held across an await.
  //
  // Pre-merge review L3: the window used to run from the send's START only, and the sends it
  // covers are bounded at up to 10 s, so the confirmation of a slow send could arrive after its
  // entry had expired and read as an external change. An entry now stays while its send is in
  // flight and is re-stamped at completion; EchoInFlightLimit only reclaims an entry whose send
  // never completed (SharpCaster gives up on a response after 30 s).
  private static readonly TimeSpan EchoWindow = TimeSpan.FromSeconds(3);
  private static readonly TimeSpan EchoInFlightLimit = TimeSpan.FromSeconds(35);

  // How far beyond the range of recent pushes a reported level may lie and still be recognised
  // as their echo (see IsRecentVolumePush). The same 0.01 the baseline comparison uses.
  private const float EchoLevelTolerance = 0.01f;
  private readonly TimeProvider _timeProvider;
  private readonly object _echoLock = new();
  private readonly List<EchoPush<float>> _recentVolumePushes = new();
  private readonly List<EchoPush<bool>> _recentMutePushes = new();

  /// <summary>One command recorded in the echo memory. Mutated only under <c>_echoLock</c>.</summary>
  private sealed class EchoPush<T>(T value, long stamp)
  {
    public T Value { get; } = value;
    public long Stamp { get; set; } = stamp;
    public bool InFlight { get; set; } = true;
  }

  // AUD-81: console-driven SET_VOLUME coalescing. At most one drain runs per output; a
  // request made while it runs overwrites the single pending target (latest wins) and is sent
  // when the in-flight push completes. These fields, and _externalChangeSerial, are written only
  // inside _consoleVolumeLock, which is never held across an await; _latestConsoleTarget is also
  // read unlocked (through Volatile) by KnownSpeakerLevel.
  private readonly object _consoleVolumeLock = new();
  private (float Level, int Generation, float ConsoleLevel)? _pendingConsoleVolume;
  private Task<CastConsoleVolumeResult>? _consoleVolumeDrain;
  private float _latestConsoleTarget = float.NaN;

  // AUD-81 (pre-merge review L1/L2): bumped, together with the _connectionVolume write, by every
  // external speaker change OnReceiverStatusChanged reports. The volume drain records a level it
  // sent as the connection's level only if no external change landed while that send was on the
  // network (and its connection is still the published one) — otherwise the speaker's own report
  // is the newer truth and must not be overwritten.
  private int _externalChangeSerial;

  // AUD-81 (pre-merge review L4): console-driven SET_MUTE coalescing, the same one-slot,
  // latest-wins drain as the volume's. Guarded by _consoleMuteLock, never held across an await.
  private readonly object _consoleMuteLock = new();
  private (bool Muted, int Generation)? _pendingConsoleMute;
  private Task<bool?>? _consoleMuteDrain;

  // AUD-81 (pre-merge review M1): set on the raising thread for exactly the duration of a
  // CastVolumeChanged raised for a change made ON THE SPEAKER. AudioStateUpdateService copies
  // that change into master volume and mute synchronously inside the event, so the follower's
  // mixer handlers run on this thread while it is set and can tell a speaker re-sync from a
  // console move without comparing floats. [ThreadStatic], so an owner check keeps one output's
  // mark from being read by another.
  [ThreadStatic] private static GoogleCastOutput? t_speakerChangeOwner;
  [ThreadStatic] private static float t_speakerChangeLevel;

  // AUD-81: the generation of the connection this application muted because the console was
  // muted, or -1. A deliberate teardown of that connection stops the receiver application and
  // then unmutes the speaker, so it is not left muted for its next user (and is left muted when
  // the stop cannot be confirmed — see ReleaseConsoleMuteAsync). Accessed through
  // Interlocked/Volatile only.
  private int _consoleMutedGeneration = -1;

  // AUD-81: set once by CastConsoleVolumeFollower. The console's mute state, read when
  // streaming starts, and the logger the console lines go to (Radio.Infrastructure.Audio.Outputs
  // is held at Warning by LOG-2; the follower's namespace is not).
  private Func<bool>? _isConsoleMuted;
  private ILogger? _consoleLogger;

  // Bound on a console-driven SET_VOLUME/SET_MUTE; and on the receiver-application stop and the
  // unmute sent before a teardown. Timed by _timeProvider (hostile review F9), the same clock as
  // the echo window. A timed-out command is abandoned, not cancelled: its send stays in flight in
  // SharpCaster, and its echo entry with it.
  private static readonly TimeSpan ConsoleCommandTimeout = TimeSpan.FromSeconds(5);
  private static readonly TimeSpan TeardownUnmuteTimeout = TimeSpan.FromSeconds(2);
  private static readonly TimeSpan TeardownAppStopTimeout = TimeSpan.FromSeconds(3);

  // AUD-54 (1). The ReceiverChannel that currently carries OnReceiverStatusChanged, so
  // SubscribeToReceiverStatus can detach it from there before attaching it elsewhere.
  // Read and written only inside _receiverStatusSubscriptionLock, which guards nothing else
  // and is never held across an await.
  private ReceiverChannel? _receiverStatusSubscribedChannel;
  private readonly object _receiverStatusSubscriptionLock = new();

  // AUD-80: the level the current connection should hold on the device — the volume
  // remembered for it, else the level it reported when first seen, else NaN (unknown).
  // SyncVolumeAfterStartAsync pushes this after the receiver app launches; NaN means
  // "leave the device alone". Written from the connect path and from SharpCaster's
  // status callback, so accessed through Volatile (a float write is atomic; a float?
  // would not be).
  private float _connectionVolume = float.NaN;

  /// <inheritdoc />
  protected override ILogger Logger => _logger;

  /// <inheritdoc />
  public override AudioOutputType Type => AudioOutputType.GoogleCast;

  /// <summary>
  /// Event raised when a Chromecast device is discovered.
  /// </summary>
  public event EventHandler<ChromecastDeviceDiscoveredEventArgs>? DeviceDiscovered;

  /// <summary>
  /// Event raised when connected to a Chromecast device.
  /// </summary>
  public event EventHandler<ChromecastConnectedEventArgs>? Connected;

  /// <summary>
  /// Event raised when disconnected from a Chromecast device.
  /// </summary>
  public event EventHandler<ChromecastDisconnectedEventArgs>? Disconnected;

  /// <summary>
  /// Event raised with the Cast device's volume and mute state: once after each connect
  /// with the level the device holds after the initial sync — which may be a remembered
  /// level this application just pushed (AUD-80) — flagged <c>IsInitialSync</c>; and
  /// whenever the state changes externally (Google Home app, voice command, physical
  /// controls). Not fired for the device's confirmation of a level this application set.
  /// </summary>
  public event EventHandler<CastVolumeChangedEventArgs>? CastVolumeChanged;

  /// <summary>
  /// Gets the currently connected Chromecast device information.
  /// </summary>
  public ChromecastDeviceInfo? ConnectedDevice { get; private set; }

  /// <summary>
  /// Gets the Google Cast output options for external inspection (e.g., by controllers
  /// to determine the streaming mode).
  /// </summary>
  public GoogleCastOutputOptions Options => _options;

  /// <summary>
  /// Initializes a new instance of the <see cref="GoogleCastOutput"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="options">The Google Cast output options.</param>
  /// <param name="cacheRepository">Optional SQLite-backed cache repository.</param>
  /// <param name="metricsCollector">Optional metrics collector for streaming metrics.</param>
  /// <param name="volumeStore">
  /// Optional per-device volume memory (AUD-80). Without it every connection adopts the
  /// device's own level and nothing is restored.
  /// </param>
  /// <param name="timeProvider">
  /// Clock for the echo memory's window and for the timeouts on console-driven and teardown
  /// SET_VOLUME / SET_MUTE / application-stop commands (AUD-81). Defaults to
  /// <see cref="TimeProvider.System"/>.
  /// </param>
  public GoogleCastOutput(
    ILogger<GoogleCastOutput> logger,
    IOptions<AudioOutputOptions> options,
    CastDeviceCacheRepository? cacheRepository = null,
    IMetricsCollector? metricsCollector = null,
    ICastDeviceVolumeStore? volumeStore = null,
    TimeProvider? timeProvider = null)
    : base("cast-output", "Google Cast Output",
        options?.Value?.GoogleCast?.DefaultVolume ?? 0.7f,
        options?.Value?.GoogleCast?.Enabled ?? false)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _options = options?.Value?.GoogleCast ?? throw new ArgumentNullException(nameof(options));
    _cacheRepository = cacheRepository;
    _metricsCollector = metricsCollector;
    _volumeStore = volumeStore;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  /// <inheritdoc />
  /// <remarks>
  /// Nothing in production assigns this output's <c>Volume</c>; the console reaches the speaker
  /// through <see cref="SetDeviceVolumeFromConsoleAsync"/> (AUD-81). Routed to the same coalesced,
  /// echo-recorded path so a future caller cannot bypass the echo memory.
  /// </remarks>
  protected override void OnVolumeChanged(float volume)
  {
    var target = GetConsoleVolumeTarget();
    if (target != null)
    {
      SetDeviceVolumeFromConsoleAsync(volume, target.Value.Generation)
        .SafeFireAndForget(_logger, "SetCastVolume");
    }
  }

  /// <inheritdoc />
  /// <remarks>Routed like <see cref="OnVolumeChanged"/>, to <see cref="SetDeviceMuteFromConsoleAsync"/>.</remarks>
  protected override void OnMuteChanged(bool muted)
  {
    var target = GetConsoleVolumeTarget();
    if (target != null)
    {
      SetDeviceMuteFromConsoleAsync(muted, target.Value.Generation)
        .SafeFireAndForget(_logger, "SetCastMute");
    }
  }

  /// <inheritdoc />
  public override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    ValidateCanInitialize();

    State = AudioOutputState.Initializing;

    try
    {
      _logger.LogInformation("Initializing Google Cast output");

      // Swap in a fresh client under the lock, taking the old one as a snapshot.
      // Bumping the generation invalidates any connect still in flight — it will
      // discard itself rather than publish onto the client we just replaced.
      ChromecastClient? stale;
      await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        _connectionGeneration++;
        _publishedGeneration = -1;
        UnwatchConnectionLoss_Locked();
        stale = _client;
        _client = new ChromecastClient();
        _connectedReceiver = null;
        ConnectedDevice = null;
      }
      finally
      {
        _lifecycleLock.Release();
      }

      // Network work happens on the snapshot, outside the lock.
      if (stale != null)
      {
        UnsubscribeFromReceiverStatus(stale);
        try { await stale.DisconnectAsync().ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogDebug(ex, "Error disconnecting previous Cast client during reinit"); }
      }

      State = AudioOutputState.Ready;
      _logger.LogInformation("Google Cast output initialized");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to initialize Google Cast output");
      State = AudioOutputState.Error;
      throw;
    }
  }

  /// <summary>
  /// Returns cached Cast devices without running mDNS discovery.
  /// Fast — no network I/O. Removes stale entries before returning.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A list of cached Chromecast devices.</returns>
  public async Task<IReadOnlyList<ChromecastDeviceInfo>> GetCachedDevicesAsync(
    CancellationToken ct = default)
  {
    ThrowIfDisposed();

    var cachedDevices = await LoadCacheAsync();

    // Remove stale entries
    var expirationCutoff = DateTime.UtcNow.AddDays(-_options.CacheExpirationDays);
    var staleKeys = cachedDevices
      .Where(kv => kv.Value.LastSeen < expirationCutoff)
      .Select(kv => kv.Key)
      .ToList();
    foreach (var key in staleKeys)
    {
      cachedDevices.Remove(key);
    }

    _logger.LogDebug("Returning {Count} cached Cast devices", cachedDevices.Count);
    return cachedDevices.Values.Select(c => c.Device).ToList();
  }

  /// <summary>
  /// Discovers available Chromecast devices on the network.
  /// Merges live mDNS results with a persistent cache so previously seen
  /// devices remain available even if they are temporarily offline.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A list of discovered Chromecast devices.</returns>
  public async Task<IReadOnlyList<ChromecastDeviceInfo>> DiscoverDevicesAsync(
    CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    _logger.LogInformation(
      "Starting Chromecast device discovery (timeout: {Timeout}s)",
      _options.DiscoveryTimeoutSeconds);

    // Load persistent cache first (SQLite if available, fallback to JSON)
    var cachedDevices = await LoadCacheAsync();

    try
    {
      ChromecastLocator locator = new ChromecastLocator();
      var discoveredDevices = await locator.FindReceiversAsync(TimeSpan.FromSeconds(10));

      foreach (var device in discoveredDevices)
      {
        if (device?.DeviceUri == null)
        {
          _logger.LogDebug("Skipping discovered device with null DeviceUri");
          continue;
        }

        var deviceId = device.DeviceUri.ToString();

        var deviceInfo = new ChromecastDeviceInfo
        {
          Id = deviceId,
          FriendlyName = device.Name ?? "Unknown",
          IpAddress = device.DeviceUri.Host,
          Port = device.DeviceUri.Port,
          Model = device.Model ?? "Unknown"
        };

        // Update or add to cache
        cachedDevices[deviceId] = new CachedCastDevice
        {
          Device = deviceInfo,
          LastSeen = DateTime.UtcNow
        };

        // Keep the live receiver for connection (indexed by ID and by IP for fallback)
        _discoveredReceivers[deviceId] = device;
        _discoveredReceiversByIp[deviceInfo.IpAddress] = device;

        DeviceDiscovered?.Invoke(this, new ChromecastDeviceDiscoveredEventArgs { Device = deviceInfo });

        _logger.LogDebug(
          "Discovered Chromecast: {Name} at {IP}:{Port}",
          deviceInfo.FriendlyName, deviceInfo.IpAddress, deviceInfo.Port);
      }

      _logger.LogInformation("Discovered {Count} Chromecast device(s) via mDNS, {CacheCount} total cached",
        discoveredDevices.Count(), cachedDevices.Count);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error during Chromecast device discovery");
    }

    // Remove stale entries
    var expirationCutoff = DateTime.UtcNow.AddDays(-_options.CacheExpirationDays);
    var staleKeys = cachedDevices
      .Where(kv => kv.Value.LastSeen < expirationCutoff)
      .Select(kv => kv.Key)
      .ToList();
    foreach (var key in staleKeys)
    {
      if (cachedDevices.TryGetValue(key, out var stale))
      {
        _discoveredReceiversByIp.TryRemove(stale.Device.IpAddress, out _);
      }
      cachedDevices.Remove(key);
      _discoveredReceivers.TryRemove(key, out _);
    }

    // Save merged cache
    await SaveCacheAsync(cachedDevices);

    return cachedDevices.Values.Select(c => c.Device).ToList();
  }

  /// <summary>
  /// Loads the persistent device cache. Uses SQLite if available, falling back to JSON.
  /// Migrates JSON data to SQLite on first load.
  /// </summary>
  private async Task<Dictionary<string, CachedCastDevice>> LoadCacheAsync()
  {
    // Try SQLite first
    if (_cacheRepository != null)
    {
      var sqliteCache = await _cacheRepository.GetAllAsync();

      // One-time migration: if JSON file exists and SQLite is empty, import from JSON
      if (sqliteCache.Count == 0 && !string.IsNullOrEmpty(_options.CacheFilePath) &&
          File.Exists(_options.CacheFilePath))
      {
        try
        {
          var json = await File.ReadAllTextAsync(_options.CacheFilePath);
          var jsonCache = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, CachedCastDevice>>(json);
          if (jsonCache != null && jsonCache.Count > 0)
          {
            _logger.LogInformation("Migrating {Count} Cast devices from JSON to SQLite", jsonCache.Count);
            await _cacheRepository.SaveAllAsync(jsonCache);
            sqliteCache = jsonCache;

            // Remove the old JSON file after successful migration
            File.Delete(_options.CacheFilePath);
            _logger.LogInformation("Deleted old JSON cache file: {Path}", _options.CacheFilePath);
          }
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Failed to migrate Cast device cache from JSON to SQLite");
        }
      }

      return sqliteCache;
    }

    // Fallback to JSON file
    try
    {
      if (!string.IsNullOrEmpty(_options.CacheFilePath) && File.Exists(_options.CacheFilePath))
      {
        var json = await File.ReadAllTextAsync(_options.CacheFilePath);
        var cached = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, CachedCastDevice>>(json);
        if (cached != null)
        {
          _logger.LogDebug("Loaded {Count} cached Cast devices from JSON", cached.Count);
          return cached;
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to load Cast device cache");
    }

    return new Dictionary<string, CachedCastDevice>();
  }

  /// <summary>
  /// Saves the device cache. Uses SQLite if available, falling back to JSON.
  /// </summary>
  private async Task SaveCacheAsync(Dictionary<string, CachedCastDevice> cache)
  {
    if (_cacheRepository != null)
    {
      await _cacheRepository.SaveAllAsync(cache);
      var expirationCutoff = DateTime.UtcNow.AddDays(-_options.CacheExpirationDays);
      await _cacheRepository.RemoveStaleAsync(expirationCutoff);
      return;
    }

    // Fallback to JSON file
    try
    {
      var directory = Path.GetDirectoryName(_options.CacheFilePath);
      if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
      {
        Directory.CreateDirectory(directory);
      }

      var json = System.Text.Json.JsonSerializer.Serialize(cache, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
      await File.WriteAllTextAsync(_options.CacheFilePath!, json);
      _logger.LogDebug("Saved {Count} Cast devices to JSON cache", cache.Count);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to save Cast device cache");
    }
  }

  /// <summary>
  /// Connects to a specific Chromecast device.
  /// </summary>
  /// <param name="device">The device to connect to.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task ConnectAsync(ChromecastDeviceInfo device, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(device);

    // Recover from Error state by reinitializing
    if (State == AudioOutputState.Error)
    {
      _logger.LogInformation("Recovering from Error state before connecting");
      await InitializeAsync(cancellationToken);
    }

    // Stop streaming before switching to a new device, and remember to restart after
    var wasStreaming = State == AudioOutputState.Streaming;
    if (wasStreaming)
    {
      _logger.LogInformation("Stopping current Cast stream before connecting to new device");
      await StopAsync(cancellationToken);
    }

    if (State != AudioOutputState.Ready && State != AudioOutputState.Stopped)
    {
      throw new InvalidOperationException(
        $"Cannot connect in state {State}. Output must be in Ready or Stopped state.");
    }

    State = AudioOutputState.Connecting;

    // Everything from here on must leave State somewhere recoverable. Connecting
    // is a dead end for this class: ConnectAsync refuses to run unless the state
    // is Ready/Stopped, and ValidateCanInitialize only accepts Created/Error — so
    // any path that returns while still Connecting wedges the output until the
    // process restarts. That includes the claim block below, which can throw
    // ObjectDisposedException on a disposed lock.
    try
    {
      // Claim this attempt. Anything that supersedes it (another connect, a
      // disconnect, disposal) bumps the generation, and the commit below then
      // abandons rather than publishing over the winner.
      //
      // AUD-81 (connect race; hostile review F1). The initial-sync mark is set HERE, at the
      // claim, before any network work — not after the publish. On a live-discovered device the
      // claim reuses _client, whose ReceiverChannel still carries OnReceiverStatusChanged from
      // the previous connection; a RECEIVER_STATUS arriving during ConnectChromecast, or between
      // the publish (which resets the echo baseline to its sentinels) and the subscribe, would
      // otherwise be compared with the previous speaker's baseline or with the sentinels and
      // reported as an EXTERNAL change — written to master volume, or unmuting a muted console.
      // While the mark is set such a status only re-baselines. It is cleared in the finally
      // below for THIS generation only (compare-and-swap), on every exit from the block: a
      // superseded or failed connect clears its own mark, and never lifts a newer connect's.
      int myGeneration;
      ChromecastClient? client;
      await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        myGeneration = ++_connectionGeneration;
        client = _client;
        Volatile.Write(ref _initialSyncPendingGeneration, myGeneration);
      }
      finally
      {
        _lifecycleLock.Release();
      }

      try
      {
        _logger.LogInformation(
          "Connecting to Chromecast: {Name} at {IP}:{Port}",
          device.FriendlyName, device.IpAddress, device.Port);

        // Resolve the receiver into a LOCAL, so a concurrent teardown clearing
        // _connectedReceiver cannot null it out from under the connect below.
        // Try live discovery first by exact ID, then by IP (IDs can differ due to
        // URI normalization).
        ChromecastReceiver? receiver;
        if (_discoveredReceivers.TryGetValue(device.Id, out receiver))
        {
          _logger.LogDebug("Using live ChromecastReceiver from discovery (matched by ID)");
        }
        else if (_discoveredReceiversByIp.TryGetValue(device.IpAddress, out receiver))
        {
          _logger.LogInformation("Live ChromecastReceiver matched by IP {IP} (ID mismatch: cached={CachedId}, live={LiveId})",
            device.IpAddress, device.Id, receiver.DeviceUri);
        }
        else
        {
          // Device is from persistent cache, not live discovery.
          // Verify reachability with a TCP connect check before attempting full connection.
          var connectPort = await FindReachablePortAsync(device, cancellationToken).ConfigureAwait(false);

          var deviceUri = new Uri($"https://{device.IpAddress}:{connectPort}");
          _logger.LogInformation("TCP check passed, creating ChromecastReceiver from cache: {Uri}", deviceUri);

          // Create a fresh ChromecastClient for cached connections to avoid stale
          // socket state. Built locally and only published at commit time.
          var stale = client;
          client = new ChromecastClient();
          if (stale != null)
          {
            try { await stale.DisconnectAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogDebug(ex, "Error disconnecting previous Cast client for cached connection"); }
          }

          receiver = new ChromecastReceiver
          {
            DeviceUri = deviceUri,
            Port = connectPort,
            Name = device.FriendlyName,
            Model = device.Model
          };
        }

        if (client == null)
        {
          throw new InvalidOperationException("Client not initialized. Call InitializeAsync first.");
        }

        // AUD-84. Before every transport connect, never once per client: SharpCaster's own
        // DisconnectAsync swaps the guarded heartbeat channel back for an unguarded one,
        // and a client is reused across connects. The fault sink is bound to THIS attempt's
        // generation, so a report from a connection that has since been replaced is stale.
        var generationForFaults = myGeneration;
        SharpCasterCallbackGuard.TryHarden(
          client,
          fault => ReportConnectionLost(generationForFaults, "a SharpCaster background send failed", fault),
          _logger);

        if (ConnectRaceHookForTests != null)
        {
          await ConnectRaceHookForTests().ConfigureAwait(false);
        }

        _logger.LogDebug("Calling ConnectChromecast with URI: {Uri}", receiver.DeviceUri);
        if (ConnectTransportOverrideForTests != null)
        {
          await ConnectTransportOverrideForTests(receiver).ConfigureAwait(false);
        }
        else
        {
          await client.ConnectChromecast(receiver).ConfigureAwait(false);
        }

        // Publish only if nothing superseded us while we were on the network.
        if (!await TryPublishConnectionAsync(myGeneration, client, receiver, device, cancellationToken).ConfigureAwait(false))
        {
          _logger.LogWarning(
            "Cast connect to {Name} was superseded while connecting — discarding this connection",
            device.FriendlyName);
          try { await client.DisconnectAsync().ConfigureAwait(false); }
          catch (Exception ex) { _logger.LogDebug(ex, "Error discarding superseded Cast connection"); }

          // Hand the state machine back to the winner. Returning while still
          // Connecting would leave the output permanently unusable — and losing
          // this race is the NORMAL outcome of a teardown during startup
          // auto-connect, not an edge case.
          State = AudioOutputState.Ready;
          return;
        }

        // Still under the initial-sync mark set at the claim: the device answers the status
        // read below with a status that SharpCaster also raises as ReceiverStatusChanged, on
        // its own thread, possibly before the read's continuation has primed the echo
        // baseline. Such a status is part of the initial sync, never an external change.
        // Subscribe to receiver status changes for bidirectional volume sync
        SubscribeToReceiverStatus(client);

        // Read initial device volume. The generation goes with it: the read is a
        // network round-trip, and this connection can be superseded inside it.
        await SyncInitialVolumeAsync(client, myGeneration, device).ConfigureAwait(false);
      }
      finally
      {
        Interlocked.CompareExchange(ref _initialSyncPendingGeneration, -1, myGeneration);
      }

      Connected?.Invoke(this, new ChromecastConnectedEventArgs { Device = device });

      _logger.LogInformation(
        "Connected to Chromecast: {Name}",
        device.FriendlyName);

      State = AudioOutputState.Ready;

      // Resume streaming if we auto-stopped to switch devices
      if (wasStreaming)
      {
        _logger.LogInformation("Restarting Cast stream after device switch");
        await StartAsync(cancellationToken).ConfigureAwait(false);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to connect to Chromecast: {Name}", device.FriendlyName);
      State = AudioOutputState.Error;
      throw;
    }
  }

  /// <summary>
  /// Publishes a completed connection, but only if this attempt is still the
  /// current one. Returns false when a newer connect, a disconnect, or disposal
  /// superseded it while it was on the network — in which case the caller owns
  /// tearing down what it built.
  /// </summary>
  private async Task<bool> TryPublishConnectionAsync(
    int generation,
    ChromecastClient client,
    ChromecastReceiver receiver,
    ChromecastDeviceInfo device,
    CancellationToken cancellationToken)
  {
    await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (_connectionGeneration != generation)
      {
        return false;
      }

      _client = client;
      _connectedReceiver = receiver;
      ConnectedDevice = device;
      _publishedGeneration = generation;
      ResetSpeakerStateForNewConnection();
      WatchForConnectionLoss_Locked(client, generation);
      Name = $"Cast: {device.FriendlyName}";
      return true;
    }
    finally
    {
      _lifecycleLock.Release();
    }
  }

  /// <summary>
  /// Forgets everything this output believed about the PREVIOUS connection's speaker, as a new
  /// connection is published (AUD-81, pre-merge review M2). Called inside the publish section.
  /// </summary>
  /// <remarks>
  /// The echo baseline used to survive across connections. After a console-muted speaker was
  /// LOST, <c>_lastSetMute</c> stayed true, so on a reconnect whose initial read failed the
  /// start-time mute concluded the speaker was already muted and streamed to it unmuted under a
  /// muted console. Now an unread speaker is "unknown", which the start-time mute treats as
  /// unmuted (it sends the mute). Takes only the short echo/console locks, never an await, so it
  /// keeps the publish section await-free.
  /// </remarks>
  private void ResetSpeakerStateForNewConnection()
  {
    _lastSetVolume = -1f;
    _lastSetMute = false;
    Interlocked.Exchange(ref _consoleMutedGeneration, -1);

    lock (_echoLock)
    {
      _recentVolumePushes.Clear();
      _recentMutePushes.Clear();
    }

    lock (_consoleVolumeLock)
    {
      _pendingConsoleVolume = null;
      Volatile.Write(ref _latestConsoleTarget, float.NaN);
      Volatile.Write(ref _connectionVolume, float.NaN);
    }

    lock (_consoleMuteLock)
    {
      _pendingConsoleMute = null;
    }
  }

  /// <summary>
  /// Disconnects from the currently connected Chromecast device.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task DisconnectAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    // Take the connection state out from under any in-flight connect FIRST,
    // then do the network work on the snapshot. Bumping the generation here is
    // what lets a teardown preempt a connect parked in an uncancellable
    // SharpCaster call instead of queueing behind it — queueing would convert
    // this race into a multi-second hang on the output picker.
    ChromecastClient? client;
    ChromecastDeviceInfo? disconnectedDevice;
    bool hadConnection;
    int closedGeneration;
    await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      closedGeneration = _publishedGeneration;
      _connectionGeneration++;
      _publishedGeneration = -1;
      UnwatchConnectionLoss_Locked();
      hadConnection = _connectedReceiver != null;
      client = _client;
      disconnectedDevice = ConnectedDevice;
      _connectedReceiver = null;
      ConnectedDevice = null;
    }
    finally
    {
      _lifecycleLock.Release();
    }

    CancelPendingMetadataUpdate();

    if (!hadConnection)
    {
      // Debug, not Warning (AUD-54 ②, AUD-84 follow-up 3): this is the normal outcome of a
      // teardown after a handled connection loss, which has already cleared the connection.
      _logger.LogDebug("Disconnect requested but no Chromecast device is connected");
      return;
    }

    try
    {
      _logger.LogInformation("Disconnecting from Chromecast: {Name}", disconnectedDevice?.FriendlyName);

      // AUD-81: before closing, so the speaker is not left muted by a muted console. Usually a
      // no-op — StopAsync has released it — but DisposeAsync reaches here without a StopAsync,
      // and a release StopAsync could not complete (receiver application not confirmed stopped)
      // is retried here. Either way the application is stopped before any unmute.
      await ReleaseConsoleMuteAsync(client, closedGeneration, disconnectedDevice?.FriendlyName).ConfigureAwait(false);

      UnsubscribeFromReceiverStatus(client);

      if (client != null)
      {
        await client.DisconnectAsync().ConfigureAwait(false);
      }

      Name = "Google Cast Output";

      Disconnected?.Invoke(this, new ChromecastDisconnectedEventArgs
      {
        Device = disconnectedDevice,
        Reason = "User requested disconnect"
      });

      State = AudioOutputState.Ready;
      _logger.LogInformation("Disconnected from Chromecast");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error disconnecting from Chromecast");
      throw;
    }
  }

  /// <inheritdoc />
  public override async Task StartAsync(CancellationToken cancellationToken = default)
  {
    ValidateCanStart();

    if (_connectedReceiver == null)
    {
      _logger.LogInformation("No Chromecast device connected yet — output ready, waiting for device connection");
      State = AudioOutputState.Ready;
      return;
    }

    try
    {
      var startTimer = System.Diagnostics.Stopwatch.StartNew();
      _logger.LogInformation("Starting Google Cast output (mode: {Mode})", _options.StreamingMode);

      if (_client != null)
      {
        // Launch the receiver application (default CC1AD845 or custom receiver)
        var launchStatus = await _client.LaunchApplicationAsync(_options.ApplicationId);
        _logger.LogInformation("Cast: Receiver launched on {Device} ({LaunchMs}ms)",
          ConnectedDevice?.FriendlyName, startTimer.ElapsedMilliseconds);

        // Allow receiver to start initializing before sending commands
        await Task.Delay(250, cancellationToken);

        // Branch on streaming mode
        if (string.Equals(_options.StreamingMode, "DirectChannel", StringComparison.OrdinalIgnoreCase))
        {
          await StartDirectChannelAsync(launchStatus, cancellationToken);
        }
        else
        {
          await StartHttpMp3Async(cancellationToken);
        }
      }

      IsEnabledInternal = true;
      State = AudioOutputState.Streaming;

      await OnReachedStreamingAsync().ConfigureAwait(false);

      _logger.LogInformation("Google Cast output started streaming to {Name} (mode: {Mode}, setupMs: {SetupMs})",
        ConnectedDevice?.FriendlyName, _options.StreamingMode, startTimer.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start Google Cast output");
      State = AudioOutputState.Error;
      throw;
    }
  }

  /// <summary>
  /// What <see cref="StartAsync"/> does the moment the output is <c>Streaming</c>. Never throws.
  /// </summary>
  /// <remarks>
  /// <para>Replays a connection loss reported while the start was still running: it was
  /// deferred, not dropped, and there is now a streaming output to rescue.</para>
  /// <para>AUD-81 (pre-merge review M3): then re-checks the console's mute. The start-time mute
  /// ran before <c>Streaming</c>, and until this point the follower ignored console changes
  /// (nothing is driven before <c>Streaming</c>), so a console mute made in between would
  /// otherwise be lost and the speaker would play out loud under a muted console. From here on
  /// the follower sees every change itself. A no-op when the speaker is already muted.</para>
  /// <para><c>internal</c> so a test can drive it: the <see cref="StartAsync"/> that calls it
  /// needs a launched receiver application, which no offline test can produce.</para>
  /// </remarks>
  internal async Task OnReachedStreamingAsync()
  {
    ReplayDeferredConnectionLoss();

    try
    {
      await MuteForConsoleAtStartAsync().ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Cast: the console mute re-check after reaching Streaming did not complete");
    }
  }

  /// <summary>
  /// Starts the DirectChannel streaming mode: sends Base64-encoded WAV chunks
  /// directly over a custom Cast message bus, bypassing HTTP entirely.
  /// </summary>
  private async Task StartDirectChannelAsync(
    Sharpcaster.Models.ChromecastStatus.ChromecastStatus? launchStatus,
    CancellationToken cancellationToken)
  {
    if (_audioEngine == null)
    {
      _logger.LogWarning("Cast: DirectChannel mode requires audio engine — call SetAudioEngine() first. Falling back to HttpMp3.");
      await StartHttpMp3Async(cancellationToken);
      return;
    }

    // Extract the transport ID from the launched application status.
    // This is the destination address for all custom messages to the receiver.
    var transportId = launchStatus?.Application?.TransportId;
    if (string.IsNullOrEmpty(transportId))
    {
      _logger.LogWarning("Cast: Could not get transport ID from launch status — falling back to HttpMp3");
      await StartHttpMp3Async(cancellationToken);
      return;
    }

    _logger.LogInformation(
      "Cast: Starting DirectChannel streaming — transport: {TransportId}, namespace: {Namespace}, chunk: {ChunkMs}ms",
      transportId, _options.DirectChannelNamespace, _options.DirectChannelChunkSizeMs);

    // Create the custom audio channel and wire it to the client.
    _directChannel = new DirectCastAudioChannel(_options.DirectChannelNamespace, _logger);
    _directChannel.Client = _client!;

    // Register the channel with SharpCaster's internal channel list (replacing any earlier
    // one for this namespace) so SharpCaster routes our namespace to it. SharpCaster v3.0.0
    // has no public RegisterChannel API, so we inject via reflection. Routing to the channel
    // is not delivery: SharpCaster drops the receiver's `pong` before OnMessageReceived —
    // see the remarks on RegisterCustomChannel.
    RegisterCustomChannel(_client!, _directChannel);

    // Create the streaming service and start sending audio. The loss report is armed
    // with the connection published now; HandleConnectionLostAsync re-checks it under
    // the lock, so a report that outlives this connection is ignored. If nothing is
    // published (-1) the report can never match and is in effect disarmed. That happens
    // only if the connection was torn down while StartAsync was running; stopping this
    // stream is then up to whoever calls StopAsync next (DisconnectAsync does not stop
    // it), which is a pre-existing gap of the Start/teardown race, not closed here.
    var streamingGeneration = Volatile.Read(ref _publishedGeneration);
    _directStreaming = new DirectCastStreamingService(
      _logger, _audioEngine, _directChannel, _options, _metricsCollector,
      onSendsFailing: fault => ReportConnectionLost(
        streamingGeneration, "DirectChannel audio sends kept failing", fault));
    _directStreaming.SetTransportId(transportId);

    // AUD-81: mute for a muted console BEFORE the first chunk is sent. Repeated (as a no-op,
    // the speaker then being muted) by SyncVolumeAfterStartAsync below.
    await MuteForConsoleAtStartAsync().ConfigureAwait(false);
    _directStreaming.Start();

    // Sync volume to Cast device
    await SyncVolumeAfterStartAsync();
  }

  /// <summary>
  /// Starts the standard HttpMp3 streaming mode: the Cast device fetches audio
  /// from an HTTP MP3 stream endpoint.
  /// </summary>
  private async Task StartHttpMp3Async(CancellationToken cancellationToken)
  {
    // Subscribe to status changes to monitor device transitions
    var mediaChannel = _client!.GetChannel<MediaChannel>();
    if (mediaChannel != null)
    {
      mediaChannel.StatusChanged += (_, status) =>
      {
        _logger.LogInformation(
          "Cast: StatusChanged event — PlayerState: {State}, IdleReason: {IdleReason}, MediaSessionId: {SessionId}",
          status?.PlayerState, status?.IdleReason, status?.MediaSessionId);
      };
    }

    // Load media if we have a stream URL
    if (!string.IsNullOrEmpty(_streamUrl))
    {
      // AUD-81: mute for a muted console before the media starts playing.
      await MuteForConsoleAtStartAsync().ConfigureAwait(false);
      await LoadMediaOnCastAsync(mediaChannel, cancellationToken);
      await SyncVolumeAfterStartAsync();
    }
    else
    {
      _logger.LogWarning("Cast: No stream URL set — Chromecast will not receive audio");
    }
  }

  /// <summary>
  /// Re-applies this connection's volume (<c>_connectionVolume</c>, AUD-80) after the
  /// receiver application launches. Leaves the device alone when that level is unknown.
  /// </summary>
  /// <remarks>
  /// This used to push the output's own <c>Volume</c> — which nothing sets after
  /// construction, so it was always <c>GoogleCast.DefaultVolume</c> — on every start, and
  /// without baselining the echo filter. Measured on the box 2026-09-29: every connect was
  /// followed by <c>Synced volume from Cast device: 70 % (initial: false)</c>.
  /// <c>internal</c> so a test can drive it: the StartAsync chain that calls it needs a
  /// launched receiver application, which no offline test can produce.
  /// </remarks>
  internal async Task SyncVolumeAfterStartAsync()
  {
    // AUD-81: a muted console must not play out loud on the speaker, so the mute goes first.
    await MuteForConsoleAtStartAsync().ConfigureAwait(false);

    var target = Volatile.Read(ref _connectionVolume);
    if (float.IsNaN(target))
    {
      _logger.LogInformation(
        "Cast: no remembered or reported volume for {Name} — leaving the device's own volume unchanged",
        ConnectedDevice?.FriendlyName);
      return;
    }

    try
    {
      // Bounded: on a connection whose socket write has stalled, this SET_VOLUME queues
      // behind the stalled write on SharpCaster's send lock indefinitely, and StartAsync
      // could not reach Streaming (and replay a deferred loss) until the TCP timeout.
      if (await PushVolumeToDeviceAsync(_client!, target).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))
      {
        _logger.LogInformation("Cast: Volume synced to {Volume:P0}", target);
      }
    }
    catch (Exception volEx)
    {
      _logger.LogWarning(volEx, "Cast: Failed to sync volume after start");
    }
  }

  /// <summary>
  /// Registers a custom channel with SharpCaster's internal channel list via reflection,
  /// REPLACING any channel already registered for the same namespace. SharpCaster v3.0.0 has
  /// no public API for this; its private <c>Channels</c> property is an
  /// <c>IEnumerable&lt;IChromecastChannel&gt;</c> that we swap for a new array.
  /// </summary>
  /// <remarks>
  /// <para>
  /// AUD-54 (2). This used to append, so every StartAsync on a reused client added one more
  /// <see cref="DirectCastAudioChannel"/> for the namespace. SharpCaster's receive loop
  /// (<c>ChromecastClient.Receive</c>) routes an inbound message to
  /// <c>Channels.FirstOrDefault(c =&gt; c.Namespace == castMessage.Namespace)</c> — the FIRST
  /// match — so after one Stop/Start the live streaming service's channel was never the one
  /// chosen. Replacing by namespace keeps exactly one channel per namespace, and the
  /// <c>total channels</c> count in the log line stays constant across Stop/Start.
  /// </para>
  /// <para>
  /// What this does NOT fix (verified by decompiling SharpCaster 3.0.0, <c>lib/net9.0</c>):
  /// even the first channel never receives the receiver's <c>pong</c>. In
  /// <c>ChromecastClient.Receive</c>, after choosing the channel, a message is dispatched to
  /// <c>OnMessageReceived</c> only if its <c>type</c> is a key of the private
  /// <c>MessageTypes</c> dictionary, built with the default (case-sensitive) comparer from the
  /// <c>Type</c> of SharpCaster's own registered messages (<c>PongMessage</c>'s is
  /// <c>"PONG"</c>). Our receiver sends <c>type: 'pong'</c>, which is not a key, so the loop
  /// takes its else branch — a log call when the client has a logger (ours has none) and
  /// <c>Debugger.Break()</c> — and the message is never delivered. It does still restart the
  /// heartbeat timeout, which runs for any non-heartbeat channel's message before the type check.
  /// So <c>DirectCastStreamingService.LastRttMs</c> stays 0 regardless of this method.
  /// </para>
  /// <para>
  /// The swap is a single reference assignment, so the receive loop enumerates either the old
  /// array or the new one, never a half-built one. It is not ordered against SharpCaster's own
  /// rebuild of the list in <c>RecreateHeartbeatChannel</c> (run by its DisconnectAsync), nor
  /// against <c>SharpCasterCallbackGuard.TryHarden</c>, which also rewrites it; a collision with
  /// either can lose one of the two writes.
  /// </para>
  /// <c>internal</c> so a test can drive it against a real, unconnected
  /// <see cref="ChromecastClient"/>.
  /// </remarks>
  internal void RegisterCustomChannel(ChromecastClient client, DirectCastAudioChannel channel)
  {
    try
    {
      if (!TryRewriteChannels(client, existing =>
          existing.Where(ch => !(ch is Sharpcaster.Interfaces.IChromecastChannel c && c.Namespace == channel.Namespace))
            .Append(channel)
            .ToList(), out var count))
      {
        return;
      }

      _logger.LogInformation("Cast: Registered custom channel for namespace {Ns} (total channels: {Count})",
        channel.Namespace, count);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Cast: Failed to register custom channel via reflection");
    }
  }

  /// <summary>
  /// Removes <paramref name="channel"/> (by reference) from the client it was registered on, if
  /// it is still there. Never throws. A no-op when the channel has no client or was already
  /// replaced. <c>internal</c> for the same reason as <see cref="RegisterCustomChannel"/>.
  /// </summary>
  internal void UnregisterCustomChannel(DirectCastAudioChannel? channel)
  {
    var client = channel?.Client;
    if (channel == null || client == null)
    {
      return;
    }

    try
    {
      var removed = false;
      if (TryRewriteChannels(client, existing =>
          {
            var kept = existing.Where(ch => !ReferenceEquals(ch, channel)).ToList();
            removed = kept.Count != existing.Count;
            return kept;
          }, out var count) && removed)
      {
        _logger.LogDebug("Cast: Unregistered custom channel for namespace {Ns} (total channels: {Count})",
          channel.Namespace, count);
      }
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Cast: Failed to unregister custom channel via reflection");
    }
  }

  /// <summary>
  /// Replaces SharpCaster's private <c>Channels</c> with <paramref name="rewrite"/>'s result,
  /// as an array of the property's element type. False (with a Warning) when the property
  /// cannot be found or read.
  /// </summary>
  private bool TryRewriteChannels(ChromecastClient client, Func<List<object>, List<object>> rewrite, out int count)
  {
    count = 0;
    var prop = client.GetType().GetProperty("Channels",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    if (prop == null)
    {
      _logger.LogWarning("Cast: Could not find Channels property on ChromecastClient");
      return false;
    }

    if (prop.GetValue(client) is not System.Collections.IEnumerable existing)
    {
      _logger.LogWarning("Cast: Channels property is null");
      return false;
    }

    var newList = rewrite(existing.Cast<object>().ToList());

    var interfaceType = prop.PropertyType.GetGenericArguments().FirstOrDefault();
    if (interfaceType != null)
    {
      var arr = Array.CreateInstance(interfaceType, newList.Count);
      for (int i = 0; i < newList.Count; i++)
      {
        arr.SetValue(newList[i], i);
      }
      prop.SetValue(client, arr);
    }
    else
    {
      prop.SetValue(client, newList);
    }

    count = newList.Count;
    return true;
  }

  /// <inheritdoc />
  public override async Task StopAsync(CancellationToken cancellationToken = default)
  {
    if (!ValidateCanStop())
    {
      return;
    }

    try
    {
      State = AudioOutputState.Stopping;
      _logger.LogInformation("Stopping Google Cast output");

      CancelPendingMetadataUpdate();

      // Stop DirectChannel streaming if active. Taken with an exchange so this and
      // HandleConnectionLostAsync, which can run concurrently, never both stop it.
      // The channel is taken together with the streaming service, before the (up to 5 s)
      // stop, and released in a finally, so a throwing StopAsync still unregisters it.
      var directStreaming = Interlocked.Exchange(ref _directStreaming, null);
      if (directStreaming != null)
      {
        var directChannel = Interlocked.Exchange(ref _directChannel, null);
        try
        {
          await directStreaming.StopAsync();
        }
        finally
        {
          await directStreaming.DisposeAsync();
          UnregisterCustomChannel(directChannel);
        }
        _logger.LogInformation("DirectChannel streaming stopped");
      }

      // Stop media playback on the Chromecast. Snapshot the client under the
      // lock so a concurrent connect swapping _client cannot make us send the
      // media stop down a half-built connection.
      ChromecastClient? stopClient;
      int stopGeneration;
      string? stopDeviceName;
      await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        stopClient = _client;
        stopGeneration = _publishedGeneration;
        stopDeviceName = ConnectedDevice?.FriendlyName;
      }
      finally
      {
        _lifecycleLock.Release();
      }

      if (stopClient != null)
      {
        var mediaChannel = stopClient.GetChannel<MediaChannel>();
        if (mediaChannel != null && mediaChannel.MediaStatus == null)
        {
          // AUD-54 ②. This client holds no current media status (none was ever received, or
          // SharpCaster cleared it after a failed media send or an empty MEDIA_STATUS), so there
          // is no media session to stop — the normal case in DirectChannel mode, which never loads media.
          // SharpCaster 3.0.0's MediaChannel.StopAsync would throw InvalidOperationException
          // ("MediaSessionID is not available") here before sending anything, which logged a
          // Warning on every DirectChannel teardown.
          _logger.LogDebug("Cast: no media session on this connection — skipping media stop");
        }
        else if (mediaChannel != null)
        {
          try
          {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await mediaChannel.StopAsync().WaitAsync(cts.Token);
          }
          catch (TimeoutException)
          {
            _logger.LogWarning("Timed out stopping Cast media — device may be unreachable");
          }
          catch (OperationCanceledException)
          {
            _logger.LogWarning("Cast media stop cancelled — device may be unreachable");
          }
          catch (System.Reflection.TargetInvocationException tie)
            when (tie.InnerException?.Message.Contains("INVALID_MEDIA_SESSION_ID") == true)
          {
            _logger.LogDebug("Cast media session already ended, ignoring stop error");
          }
          catch (InvalidOperationException ioe)
            when (ioe.Message.Contains("INVALID_MEDIA_SESSION_ID"))
          {
            _logger.LogDebug("Cast media session already ended, ignoring stop error");
          }
          catch (ArgumentNullException)
          {
            _logger.LogDebug("Cast: No active media session to stop (MediaSessionId is null)");
          }
          catch (InvalidOperationException ioe)
            when (ioe.Message.Contains("MediaSessionID is not available", StringComparison.Ordinal))
          {
            // The media status was cleared between the check above and the call (a failed
            // media send clears it). Thrown before anything is sent, so nothing was stopped.
            _logger.LogDebug("Cast: no media session to stop (media status cleared), ignoring");
          }
          catch (Exception ex)
          {
            // SharpCaster's MediaChannel.StopAsync → SendAsync can throw once the
            // session is already tearing down (socket closed / channel disposed by
            // the concurrent disconnect). The disconnect itself still succeeds, so
            // log-and-continue rather than letting it escape to the outer catch —
            // which logged a spurious ERROR *and* skipped the clean teardown below
            // (leaving IsEnabledInternal stuck true).
            _logger.LogWarning(ex, "Cast media stop failed during teardown; continuing");
          }
        }
      }

      // AUD-81 (pre-merge review H1). After the media stop, never before it: in HttpMp3 the live
      // MP3 keeps playing until then. And the media stop alone is not enough — in DirectChannel
      // stopping the send loop does not stop the receiver, which still holds up to
      // DirectChannelMaxBufferAhead of our audio, and the gate deactivates the HTTP output only
      // after this method returns. So a speaker the console muted is unmuted only after its
      // receiver application has been stopped, and is left muted if that cannot be confirmed.
      // ReleaseConsoleMuteAsync states exactly what that guarantees.
      await ReleaseConsoleMuteAsync(stopClient, stopGeneration, stopDeviceName).ConfigureAwait(false);

      IsEnabledInternal = false;
      State = AudioOutputState.Stopped;

      _logger.LogInformation("Google Cast output stopped");
    }
    catch (Exception ex)
    {
      // AUD-54 (4). The same end state as the success path. Steps outside the media-stop
      // try (stopping the DirectChannel loop, taking the lock) can throw, and leaving
      // IsEnabledInternal true here was exactly what the media-stop catch exists to prevent.
      _logger.LogError(ex, "Failed to stop Google Cast output");
      IsEnabledInternal = false;
      State = AudioOutputState.Stopped;
    }
  }

  /// <summary>
  /// Sets the stream URL that will be used when streaming to Chromecast.
  /// The Chromecast will connect to this URL to receive the audio stream.
  /// </summary>
  /// <param name="streamUrl">The HTTP stream URL.</param>
  public void SetStreamUrl(string streamUrl)
  {
    _streamUrl = streamUrl;
    _logger.LogDebug("Stream URL set to: {Url}", streamUrl);
  }

  /// <summary>
  /// Gets the active DirectChannel streaming service, if any.
  /// Used for diagnostics and latency measurement.
  /// </summary>
  public DirectCastStreamingService? DirectStreaming => _directStreaming;

  /// <summary>
  /// Sets the audio engine reference for DirectChannel streaming mode.
  /// When set, the DirectCastStreamingService can create a stream reader
  /// to read PCM audio directly from the engine's output tap.
  /// Only required when <see cref="GoogleCastOutputOptions.StreamingMode"/> is "DirectChannel".
  /// </summary>
  /// <param name="audioEngine">The audio engine instance.</param>
  public void SetAudioEngine(IAudioEngine audioEngine)
  {
    _audioEngine = audioEngine;
    _logger.LogInformation("Cast: Audio engine set for DirectChannel streaming");
  }

  /// <summary>
  /// Sets now-playing metadata that will be sent to the Cast device.
  /// The metadata is included when loading media and can be updated mid-stream
  /// via <see cref="UpdateNowPlayingMetadataAsync"/>.
  /// </summary>
  public void SetNowPlayingMetadata(string? title, string? artist, string? album, string? albumArtUrl)
  {
    _nowPlayingMetadata = new CastNowPlayingMetadata(title, artist, album, albumArtUrl);
    _logger.LogDebug("Cast now-playing metadata set: {Title} - {Artist} [{Album}]", title, artist, album);
  }

  /// <summary>
  /// Tests Cast playback with an arbitrary URL to diagnose audio issues.
  /// Re-launches the receiver app and loads the URL fresh.
  /// </summary>
  public async Task<object> TestPlayUrlAsync(string url, string contentType)
  {
    if (_client == null)
    {
      return new { success = false, error = "Not connected to a Cast device" };
    }

    // AUD-54 (3). This relaunches the receiver application on the connected device. On a
    // live session that hands the device a new transport id while the DirectChannel loop keeps
    // sending to the old one, so it is refused while an operation of ours owns the session.
    // A StartAsync still launching the receiver leaves the state at Ready until it reaches
    // Streaming, so this check does not see that window.
    var state = State;
    if (state is AudioOutputState.Streaming or AudioOutputState.Connecting or AudioOutputState.Stopping)
    {
      return new
      {
        success = false,
        error = $"Cast output is {state} — stop casting before running a test playback, " +
                "because the test relaunches the receiver app and would break the live session."
      };
    }

    try
    {
      // Re-launch the media receiver app to get a clean session
      _logger.LogInformation("Cast test: Launching media receiver app");
      await _client.LaunchApplicationAsync(_options.ApplicationId);
      await Task.Delay(500);

      var media = new Media
      {
        ContentId = url,
        ContentUrl = url,
        ContentType = contentType,
        StreamType = StreamType.Buffered
      };

      var payload = JsonSerializer.Serialize(media);
      _logger.LogInformation("Cast test: Payload = {Payload}", payload);

      var mediaChannel = _client.GetChannel<MediaChannel>();
      if (mediaChannel == null)
      {
        return new { success = false, error = "MediaChannel not available" };
      }

      var loadStatus = await mediaChannel.LoadAsync(media, true);
      _logger.LogInformation(
        "Cast test: Load response — PlayerState: {State}, IdleReason: {Reason}, MediaSessionId: {Id}",
        loadStatus?.PlayerState, loadStatus?.IdleReason, loadStatus?.MediaSessionId);

      // Wait for playback to start
      await Task.Delay(3000);
      var finalStatus = await mediaChannel.GetMediaStatusAsync().WaitAsync(TimeSpan.FromSeconds(5));
      _logger.LogInformation(
        "Cast test: Final status (3s) — PlayerState: {State}, IdleReason: {Reason}",
        finalStatus?.PlayerState, finalStatus?.IdleReason);

      return new
      {
        success = finalStatus?.PlayerState is PlayerStateType.Playing or PlayerStateType.Buffering,
        loadState = loadStatus?.PlayerState.ToString(),
        finalState = finalStatus?.PlayerState.ToString(),
        finalIdleReason = finalStatus?.IdleReason,
        url,
        contentType,
        payload
      };
    }
    catch (Exception ex)
    {
      return new { success = false, error = ex.Message, url };
    }
  }

  /// <summary>
  /// Updates now-playing metadata on the Cast device by reloading media.
  /// Uses debouncing to coalesce rapid metadata changes (e.g., source switch
  /// → "No Track" → actual track → fingerprint identification) into a single
  /// Cast media reload. Without debouncing, each change triggers a full stream
  /// reconnection, causing garbled audio during the first few seconds.
  /// </summary>
  public async Task UpdateNowPlayingMetadataAsync(
    string? title, string? artist, string? album, string? albumArtUrl,
    CancellationToken cancellationToken = default)
  {
    _nowPlayingMetadata = new CastNowPlayingMetadata(title, artist, album, albumArtUrl);

    if (State != AudioOutputState.Streaming || _client == null || string.IsNullOrEmpty(_streamUrl))
    {
      _logger.LogDebug("Cast not streaming, metadata stored for next load");
      return;
    }

    // Debounce: cancel any pending metadata reload and schedule a new one.
    // This ensures rapid changes coalesce into a single Cast media reload.
    // AUD-54 (6): the linked source is built INSIDE the lock, from the CTS this call just
    // installed. Reading the field after the lock (as this used to) could pick up a CTS that a
    // concurrent update or CancelPendingMetadataUpdate had already disposed.
    CancellationTokenSource linkedCts;
    lock (_debounceLock)
    {
      _metadataDebouncesCts?.Cancel();
      _metadataDebouncesCts?.Dispose();
      var debounceCts = new CancellationTokenSource();
      _metadataDebouncesCts = debounceCts;
      linkedCts = CancellationTokenSource.CreateLinkedTokenSource(debounceCts.Token, cancellationToken);
    }

    _logger.LogDebug("Cast metadata update debounced: {Title} - {Artist}", title, artist);

    // Fire-and-forget the delayed reload so we don't block the caller
    _ = Task.Run(async () =>
    {
      try
      {
        // Wait for metadata to stabilize — if another update comes
        // within this window, this task gets cancelled via debounceCts.
        // 3s covers the typical gap between source switch ("No Track")
        // and actual track metadata being available (~2s).
        await Task.Delay(3000, linkedCts.Token);

        // Re-checked after the wait: a StopAsync that ran between the Streaming check at the
        // top of this method and the debounce lock found nothing pending to cancel.
        if (State != AudioOutputState.Streaming)
        {
          return;
        }

        // The linked token, so a stop or disconnect that cancels the pending update also
        // abandons the wait on a reload already under way. It cannot recall a LOAD that
        // SharpCaster has already written to the socket.
        await LoadMediaWithRecoveryAsync(linkedCts.Token);
        _logger.LogInformation(
          "Cast metadata updated: {Title} - {Artist}", title, artist);
      }
      catch (OperationCanceledException)
      {
        // Debounced away — a newer update superseded this one
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to update Cast now-playing metadata");
      }
      finally
      {
        linkedCts.Dispose();
      }
    }, CancellationToken.None);
  }

  /// <summary>
  /// Cancels a metadata reload still waiting out its debounce (AUD-54 (6)), so it cannot fire
  /// a media LOAD after the stream it was meant for has been stopped or disconnected. Called by
  /// StopAsync, DisconnectAsync, HandleConnectionLostAsync and DisposeAsync. Idempotent. A
  /// reload already past its debounce is only told to stop waiting; see the call site.
  /// </summary>
  private void CancelPendingMetadataUpdate()
  {
    lock (_debounceLock)
    {
      _metadataDebouncesCts?.Cancel();
      _metadataDebouncesCts?.Dispose();
      _metadataDebouncesCts = null;
    }
  }

  /// <summary>
  /// Attempts to load media on the Cast device. If the session has expired
  /// (receiver app closed after idle), relaunches the app and retries once.
  /// </summary>
  private async Task LoadMediaWithRecoveryAsync(CancellationToken cancellationToken)
  {
    var media = BuildMedia();
    var mediaChannel = _client!.GetChannel<MediaChannel>();
    if (mediaChannel == null)
    {
      return;
    }

    try
    {
      await mediaChannel.LoadAsync(media, true).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }
    catch (Exception ex) when (IsCastSessionExpired(ex))
    {
      _logger.LogInformation("Cast session expired — relaunching media receiver");
      await RelaunchMediaReceiverAsync(cancellationToken);

      // Retry load after relaunch
      mediaChannel = _client.GetChannel<MediaChannel>();
      if (mediaChannel != null)
      {
        try
        {
          await mediaChannel.LoadAsync(media, true).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
          _logger.LogInformation("Cast: Media loaded successfully after session recovery");
        }
        catch (TimeoutException)
        {
          _logger.LogDebug("Cast: Recovered LoadAsync timed out — device may still be processing");
        }
      }
    }
    catch (TimeoutException)
    {
      _logger.LogDebug("Cast: Metadata LoadAsync timed out — device may still be processing");
    }
  }

  /// <summary>
  /// Determines whether an exception indicates the Cast session has expired.
  /// </summary>
  private static bool IsCastSessionExpired(Exception ex)
  {
    var message = ex is System.Reflection.TargetInvocationException tie
      ? tie.InnerException?.Message ?? ex.Message
      : ex.Message;

    return message.Contains("INVALID_MEDIA_SESSION_ID", StringComparison.OrdinalIgnoreCase)
        || message.Contains("No running applications", StringComparison.OrdinalIgnoreCase)
        || message.Contains("session not found", StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Relaunches the media receiver after an idle timeout.
  /// </summary>
  private async Task RelaunchMediaReceiverAsync(CancellationToken cancellationToken)
  {
    try
    {
      await _client!.LaunchApplicationAsync(_options.ApplicationId);
      await Task.Delay(500, cancellationToken);
      _logger.LogInformation("Cast: Media receiver relaunched after idle recovery");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Cast: Failed to relaunch media receiver");
    }
  }

  /// <summary>
  /// Standard Google Cast protocol port.
  /// </summary>
  private const int StandardCastPort = 8009;

  /// <summary>
  /// Finds a reachable TCP port on a cached Cast device.
  /// Tries the cached port first, then falls back to the standard Cast port (8009).
  /// </summary>
  private async Task<int> FindReachablePortAsync(ChromecastDeviceInfo device, CancellationToken ct)
  {
    // Always try the standard Cast protocol port (8009) first.
    // Port 443 on Google Home devices is HTTPS management, not Cast protocol —
    // it accepts TCP but times out on Cast protocol messages.
    var portsToTry = device.Port == StandardCastPort
      ? new[] { StandardCastPort }
      : new[] { StandardCastPort, device.Port };

    foreach (var port in portsToTry)
    {
      _logger.LogDebug("Verifying Cast device reachability at {IP}:{Port}", device.IpAddress, port);
      using var tcpCheck = new TcpClient();
      try
      {
        using var tcpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        tcpCts.CancelAfter(TimeSpan.FromSeconds(3));
        await tcpCheck.ConnectAsync(device.IpAddress, port, tcpCts.Token);
        if (port != device.Port)
        {
          _logger.LogInformation(
            "Cast device '{Name}': using standard port {Port} instead of discovered port {DiscoveredPort}",
            device.FriendlyName, port, device.Port);
        }
        return port;
      }
      catch
      {
        _logger.LogDebug("TCP check failed on {IP}:{Port}", device.IpAddress, port);
      }
    }

    throw new InvalidOperationException(
      $"Cast device '{device.FriendlyName}' at {device.IpAddress} is not reachable (tried ports {string.Join(", ", portsToTry)})");
  }

  /// <summary>
  /// Loads media on the Cast device with retry logic. The first load after
  /// LaunchApplicationAsync often fails silently (receiver not yet ready).
  /// If the first load results in FINISHED/Idle quickly, we retry once.
  /// </summary>
  private async Task LoadMediaOnCastAsync(MediaChannel? mediaChannel, CancellationToken cancellationToken)
  {
    if (mediaChannel == null)
    {
      _logger.LogWarning("Cast: MediaChannel is null — cannot load media");
      return;
    }

    _logger.LogInformation(
      "Cast: Loading media URL {StreamUrl} (type: audio/mpeg, stream: Live, metadata: {HasMetadata})",
      _streamUrl, _nowPlayingMetadata != null);

    var media = BuildMedia();

    // First attempt
    MediaStatus? status = null;
    try
    {
      status = await mediaChannel.LoadAsync(media, true).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
      _logger.LogInformation(
        "Cast: Media load response — PlayerState: {State}, IdleReason: {IdleReason}, MediaSessionId: {SessionId}",
        status?.PlayerState, status?.IdleReason, status?.MediaSessionId);
    }
    catch (TimeoutException)
    {
      _logger.LogInformation("Cast: Media load timed out (5s) — continuing in background");
    }

    // If the load immediately resulted in Idle/FINISHED or didn't start playing,
    // wait and retry — receiver may not have been fully initialized
    if (status?.PlayerState is PlayerStateType.Idle ||
        status?.IdleReason is "FINISHED" or "ERROR" or "CANCELLED")
    {
      _logger.LogInformation("Cast: First load resulted in {State}/{Reason} — retrying after 1s delay",
        status?.PlayerState, status?.IdleReason);
      await Task.Delay(1000, cancellationToken);

      try
      {
        media = BuildMedia(); // Rebuild in case metadata changed
        status = await mediaChannel.LoadAsync(media, true).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        _logger.LogInformation(
          "Cast: Retry load response — PlayerState: {State}, IdleReason: {IdleReason}, MediaSessionId: {SessionId}",
          status?.PlayerState, status?.IdleReason, status?.MediaSessionId);
      }
      catch (TimeoutException)
      {
        _logger.LogInformation("Cast: Retry load timed out — Cast device may still be processing");
      }
    }
  }

  /// <summary>
  /// Builds a Media object with the current stream URL and now-playing metadata.
  /// </summary>
  private Media BuildMedia()
  {
    var media = new Media
    {
      ContentId = _streamUrl,
      ContentUrl = _streamUrl,
      ContentType = "audio/mpeg",
      StreamType = StreamType.Live
    };

    if (_nowPlayingMetadata != null)
    {
      var metadata = new MediaMetadata
      {
        MetadataType = MetadataType.Music,
        Title = _nowPlayingMetadata.Title ?? "",
        SubTitle = _nowPlayingMetadata.Artist ?? ""
      };

      // Add album art if we have a valid absolute URL
      if (!string.IsNullOrEmpty(_nowPlayingMetadata.AlbumArtUrl) &&
          _nowPlayingMetadata.AlbumArtUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
      {
        metadata.Images = new[]
        {
          new Image { Url = _nowPlayingMetadata.AlbumArtUrl }
        };
      }

      media.Metadata = metadata;
    }

    return media;
  }

  /// <summary>
  /// Monitors a background LoadAsync task and runs connectivity diagnostics on failure.
  /// </summary>
  private async Task MonitorBackgroundLoadAsync(Task<MediaStatus?> loadTask)
  {
    try
    {
      var status = await loadTask;
      _logger.LogInformation(
        "Cast: Background load completed — PlayerState: {State}, IdleReason: {IdleReason}",
        status?.PlayerState, status?.IdleReason);
    }
    catch (TimeoutException)
    {
      _logger.LogWarning("Cast: LoadAsync timed out (30s) — Cast device never reached PLAYING state");
      await DiagnoseStreamConnectivityAsync();
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Cast: Background load failed");
      await DiagnoseStreamConnectivityAsync();
    }
  }

  /// <summary>
  /// Self-tests the stream URL from this machine to diagnose Cast connectivity issues.
  /// If the URL is reachable locally but Cast can't play it, the issue is likely a firewall.
  /// </summary>
  private async Task DiagnoseStreamConnectivityAsync()
  {
    if (string.IsNullOrEmpty(_streamUrl))
    {
      return;
    }

    try
    {
      using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
      var response = await httpClient.GetAsync(_streamUrl, HttpCompletionOption.ResponseHeadersRead);
      _logger.LogWarning(
        "Cast: Stream URL {Url} IS reachable from this machine (HTTP {Status}) but the Cast device cannot reach it. " +
        "This is almost certainly a FIREWALL issue. Fix with: " +
        "netsh advfirewall firewall add rule name=\"Radio Console Stream\" dir=in action=allow protocol=TCP localport=8080",
        _streamUrl, (int)response.StatusCode);
    }
    catch (Exception ex)
    {
      _logger.LogError(
        "Cast: Stream URL {Url} is NOT reachable even from this machine: {Error}. Is the HTTP stream server running?",
        _streamUrl, ex.Message);
    }
  }

  /// <summary>
  /// Subscribes to ReceiverChannel status events for bidirectional volume sync.
  /// </summary>
  /// <param name="client">
  /// The client to subscribe on, passed explicitly rather than read from
  /// <c>_client</c> so the caller's snapshot is used — a concurrent connect or
  /// teardown may already have swapped the field.
  /// </param>
  /// <remarks>
  /// AUD-54 (1). A client is reused when the next device was live-discovered, so a second
  /// connect used to attach <see cref="OnReceiverStatusChanged"/> a second time to the same
  /// <see cref="ReceiverChannel"/>, and a disconnect removed only one of the two. This method
  /// now removes the handler from the channel it was last attached to (which may belong to an
  /// older client that was replaced without being unsubscribed) and from this channel, then
  /// attaches it once. Within these two methods, at most one channel carries the handler at a
  /// time. It does not order this call against a teardown that runs between a connect's publish
  /// and its subscribe; a handler attached after such a teardown stays until the next subscribe
  /// or unsubscribe.
  /// </remarks>
  private void SubscribeToReceiverStatus(ChromecastClient? client)
  {
    if (client == null)
    {
      return;
    }

    var receiverChannel = client.GetChannel<ReceiverChannel>();
    if (receiverChannel == null)
    {
      return;
    }

    lock (_receiverStatusSubscriptionLock)
    {
      if (_receiverStatusSubscribedChannel != null)
      {
        _receiverStatusSubscribedChannel.ReceiverStatusChanged -= OnReceiverStatusChanged;
      }

      // Removing before adding makes a repeat subscribe on the same channel a no-op.
      receiverChannel.ReceiverStatusChanged -= OnReceiverStatusChanged;
      receiverChannel.ReceiverStatusChanged += OnReceiverStatusChanged;
      _receiverStatusSubscribedChannel = receiverChannel;
    }

    _logger.LogDebug("Subscribed to Cast receiver status changes for volume sync");
  }

  /// <summary>
  /// Unsubscribes from ReceiverChannel status events.
  /// </summary>
  /// <param name="client">The client to unsubscribe from (caller's snapshot).</param>
  private void UnsubscribeFromReceiverStatus(ChromecastClient? client)
  {
    if (client == null)
    {
      return;
    }

    var receiverChannel = client.GetChannel<ReceiverChannel>();
    if (receiverChannel == null)
    {
      return;
    }

    lock (_receiverStatusSubscriptionLock)
    {
      receiverChannel.ReceiverStatusChanged -= OnReceiverStatusChanged;
      if (ReferenceEquals(_receiverStatusSubscribedChannel, receiverChannel))
      {
        _receiverStatusSubscribedChannel = null;
      }
    }
  }

  /// <summary>
  /// Reads the initial device volume after connecting, restores the volume remembered for
  /// this device if there is one (AUD-80), and syncs our local state.
  /// </summary>
  /// <param name="client">
  /// The client to read from, passed explicitly rather than read from <c>_client</c>
  /// so the caller's snapshot is used — the same reason
  /// <see cref="SubscribeToReceiverStatus"/> takes one: a concurrent connect or
  /// teardown may already have swapped the field.
  /// </param>
  /// <param name="generation">
  /// The connection generation the caller claimed. Re-checked after the network read
  /// and before anything is published; the comment on that check states exactly what
  /// it does and does not guarantee.
  /// </param>
  /// <param name="device">The device connected to; its <c>Id</c> keys the volume memory.</param>
  private async Task SyncInitialVolumeAsync(ChromecastClient client, int generation, ChromecastDeviceInfo device)
  {
    try
    {
      (float Volume, bool Muted)? reading = null;
      try
      {
        reading = await ReadCastVolumeStatusAsync(client).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Could not read initial Cast device volume — will sync on first status update");
      }

      if (reading != null)
      {
        // Primed BEFORE the currency check, and therefore primed even for a reading
        // that is about to be discarded. That is deliberate. These two fields are the
        // echo filter's baseline, not connection state: _lastSetVolume starts at the
        // -1f sentinel, and OnReceiverStatusChanged reports a status event arriving
        // AFTER this sync completes while it is still -1f as an EXTERNAL change. (One
        // arriving DURING the sync only re-baselines — see _initialSyncPendingGeneration.)
        // Skipping the priming here would convert a suppressed initial sync into a
        // spurious user-authored one on the next status — the same write to master
        // volume, through the other door.
        _lastSetVolume = reading.Value.Volume;
        _lastSetMute = reading.Value.Muted;

        _logger.LogInformation(
          "Cast device initial volume: {Volume:P0}, Muted: {Muted}",
          reading.Value.Volume, reading.Value.Muted);
      }

      var remembered = await GetRememberedVolumeAsync(device.Id).ConfigureAwait(false);

      // AUD-5. The read above is a network round-trip, and four sites bump the
      // generation while it is in flight: InitializeAsync, a newer connect's claim,
      // DisconnectAsync and DisposeAsync. Publishing this reading for a connection
      // that has since been superseded is what the defect was — the subscriber wrote
      // AudioManager.MasterVolume, whose setter schedules a persist.
      //
      // What this check does and does not do, stated precisely because this file
      // has shipped comments that claimed more than the code enforced:
      //   IT DOES remove the network round-trip from the window. A supersede landing
      //     any time between the claim and the status response is caught here.
      //   IT DOES NOT make the window empty. The lock is released before anything
      //     below runs — subscriber code must never run under _lifecycleLock — so a
      //     bump landing after the release can still see this connection write
      //     _connectionVolume, push its remembered level to its own device, and fire.
      //   IT IS NOT what stops an initial read moving master volume. That is
      //     AudioStateUpdateService.OnCastVolumeChanged, which ignores IsInitialSync
      //     events outright and has no timing dependence at all. This check is the
      //     producer honouring its own contract for whatever subscribes next.
      if (!await IsCurrentGenerationAsync(generation).ConfigureAwait(false))
      {
        _logger.LogInformation(
          "Cast initial volume read belongs to a superseded connection (generation {Generation}) — not published",
          generation);
        return;
      }

      // AUD-80. Which level this connection holds, in order of preference:
      //   1. the level remembered for THIS device — pushed to it if it differs;
      //   2. for a device never seen, the level it reported — adopted and remembered;
      //   3. neither (the read failed, nothing remembered) — unknown; the device is left
      //      alone and its first status update is handled as before.
      // Deliberately NOT the console's master volume for a never-seen device, and NOT
      // GoogleCast.DefaultVolume: master volume is the local speakers' level (casting
      // never reads it), and pushing a configured default is exactly what made every
      // reconnect land at 70 %.
      float? effectiveVolume = reading?.Volume;
      if (remembered is float target)
      {
        Volatile.Write(ref _connectionVolume, target);
        if (reading == null || Math.Abs(reading.Value.Volume - target) > 0.01f)
        {
          try
          {
            if (await PushVolumeToDeviceAsync(client, target).ConfigureAwait(false))
            {
              effectiveVolume = target;
              _logger.LogInformation(
                "Cast: restored remembered volume {Volume:P0} on {Name} (device reported {Reported:P0})",
                target, device.FriendlyName, reading?.Volume);
            }
          }
          catch (Exception ex)
          {
            // _connectionVolume keeps the target, so the push after the receiver
            // launches (SyncVolumeAfterStartAsync) tries again.
            _logger.LogWarning(ex, "Cast: could not restore remembered volume on {Name}", device.FriendlyName);
          }
        }
      }
      else if (reading != null)
      {
        Volatile.Write(ref _connectionVolume, reading.Value.Volume);
        _volumeStore?.Remember(device.Id, reading.Value.Volume);
      }
      else
      {
        Volatile.Write(ref _connectionVolume, float.NaN);
      }

      if (reading == null || effectiveVolume == null)
      {
        // Nothing was observed on the device, so there is nothing to report — the same
        // contract as before AUD-80.
        return;
      }

      // Fire event so subscribers can observe the device's volume after this sync
      CastVolumeChanged?.Invoke(this, new CastVolumeChangedEventArgs
      {
        Volume = effectiveVolume.Value,
        IsMuted = reading.Value.Muted,
        IsInitialSync = true
      });
    }
    catch (Exception ex)
    {
      // Also where an ObjectDisposedException from IsCurrentGenerationAsync lands when
      // disposal races the read. Swallowing it is intended: a disposed output must not
      // publish, and an exception escaping into ConnectAsync would put the output in
      // Error for a sync that is not needed to stream.
      _logger.LogDebug(ex, "Initial Cast volume sync did not complete");
    }
  }

  /// <summary>
  /// The volume remembered for <paramref name="deviceId"/>, or null when there is no
  /// store, nothing is remembered, or the store cannot be read.
  /// </summary>
  private async Task<float?> GetRememberedVolumeAsync(string deviceId)
  {
    if (_volumeStore == null)
    {
      return null;
    }

    try
    {
      return await _volumeStore.GetVolumeAsync(deviceId).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Could not read the remembered volume for Cast device {DeviceId}", deviceId);
      return null;
    }
  }

  /// <summary>
  /// Sends <paramref name="volume"/> to the device as a SET_VOLUME. Returns false when the
  /// client exposes no receiver channel (nothing sent). Throws what SharpCaster throws.
  /// </summary>
  private async Task<bool> PushVolumeToDeviceAsync(ChromecastClient client, float volume)
  {
    // Baseline the echo filter to the level being set BEFORE sending it. The device
    // confirms a SET_VOLUME with a status carrying the new level, and
    // OnReceiverStatusChanged must see that as our own change (within 0.01 of
    // _lastSetVolume), not an external one. Until AUD-80 the after-start push skipped
    // this, so every connect's confirmation of DefaultVolume (70 %) arrived as an
    // external change and was written — and persisted — as master volume.
    // AUD-81: also recorded in the recent-push memory, which recognises the confirmation
    // even when a later push has already moved the baseline on — for as long as the send is in
    // flight and EchoWindow after it completes (pre-merge review L3).
    var echo = RecordVolumePush(volume);
    var previous = _lastSetVolume;
    _lastSetVolume = volume;
    try
    {
      if (CastSetVolumeOverrideForTests != null)
      {
        await CastSetVolumeOverrideForTests(volume).ConfigureAwait(false);
        return true;
      }

      var receiverChannel = client.GetChannel<ReceiverChannel>();
      if (receiverChannel == null)
      {
        _lastSetVolume = previous;
        return false;
      }

      await receiverChannel.SetVolume(volume).ConfigureAwait(false);
      return true;
    }
    catch
    {
      _lastSetVolume = previous;
      throw;
    }
    finally
    {
      CompleteEchoPush(echo);
    }
  }

  /// <summary>
  /// Performs the Cast status read behind <see cref="SyncInitialVolumeAsync"/> and
  /// <see cref="ReadSpeakerVolumeAsync"/>, or
  /// the test substitute for it. Returns null when the client exposes no receiver
  /// channel or the status carries no volume.
  /// </summary>
  private async Task<(float Volume, bool Muted)?> ReadCastVolumeStatusAsync(ChromecastClient client)
  {
    if (CastStatusReadOverrideForTests != null)
    {
      return await CastStatusReadOverrideForTests().ConfigureAwait(false);
    }

    var receiverChannel = client.GetChannel<ReceiverChannel>();
    if (receiverChannel == null)
    {
      return null;
    }

    var status = await receiverChannel.GetChromecastStatusAsync().ConfigureAwait(false);
    if (status?.Volume?.Level == null)
    {
      return null;
    }

    return ((float)status.Volume.Level.Value, status.Volume.Muted ?? false);
  }

  /// <summary>
  /// True when <paramref name="generation"/> is still the current connection
  /// generation. Await-free inside the lock, like every other critical section here.
  /// </summary>
  /// <remarks>
  /// Deliberately takes no <see cref="CancellationToken"/>. Callers must sit inside a
  /// catch that tolerates <see cref="ObjectDisposedException"/>: <c>DisposeAsync</c>
  /// disposes <c>_lifecycleLock</c>, and a disposed output must not publish anything
  /// anyway, so the throw is the right outcome rather than a case to handle.
  /// </remarks>
  private async Task<bool> IsCurrentGenerationAsync(int generation)
  {
    await _lifecycleLock.WaitAsync().ConfigureAwait(false);
    try
    {
      return _connectionGeneration == generation;
    }
    finally
    {
      _lifecycleLock.Release();
    }
  }

  /// <summary>
  /// Handles ReceiverStatusChanged events from SharpCaster.
  /// Detects external volume/mute changes and fires <see cref="CastVolumeChanged"/>.
  /// </summary>
  private void OnReceiverStatusChanged(object? sender, ChromecastStatus status)
  {
    if (status.Volume == null)
    {
      return;
    }

    var deviceVolume = (float)(status.Volume.Level ?? 0);
    var deviceMuted = status.Volume.Muted ?? false;

    // AUD-81 (connect race). A status that arrives while a connection's initial sync is in
    // progress is the device answering that sync — typically SharpCaster raising the
    // GET_STATUS response as an event, concurrently with the read's own continuation. It
    // re-baselines the echo filter and nothing else: no CastVolumeChanged, nothing
    // remembered, no queued console target dropped. Reporting it was how a connect unmuted a
    // muted console (it compared against the -1f sentinel, so it looked external).
    // What this gives up: a genuine change made on the speaker inside that window is absorbed
    // into the baseline rather than reported. The window runs from the connect's claim (hostile
    // review F1), so it covers the transport connect too — on a reused client that includes a
    // change made on the previously connected speaker while the next connect is in progress. When the read itself fails, the device state is
    // still unknown to subscribers (no initial-sync event), as before — but the baseline now
    // holds what the device said, so its next identical status is not mistaken for a change.
    // Pre-merge review L5: the same for a status arriving while a diagnostic live read
    // (ReadSpeakerVolumeAsync) is in flight — SharpCaster raises its response the same way.
    if (Volatile.Read(ref _initialSyncPendingGeneration) != -1 || Volatile.Read(ref _diagnosticReadsPending) > 0)
    {
      _lastSetVolume = deviceVolume;
      _lastSetMute = deviceMuted;
      _logger.LogDebug(
        "Cast status during an initial sync or a diagnostic read: {Volume:P0}, Muted: {Muted} — baseline only, not an external change",
        deviceVolume, deviceMuted);
      return;
    }

    // AUD-81. A level or mute that this application sent within EchoWindow is the device
    // confirming our own command — even when the confirmation is late, or a later push has
    // already moved the baseline on — so it is not an external change. For the level that means
    // anywhere within the range our recent pushes span, ±0.01 (F3: a device may round our level
    // more coarsely than 0.01; see IsRecentVolumePush for what that does and does not cover).
    // The baseline follows the device. Anything else is compared with the baseline as before.
    var volumeEcho = IsRecentVolumePush(deviceVolume);
    var muteEcho = deviceMuted != _lastSetMute && IsRecentMutePush(deviceMuted);

    var volumeChanged = !volumeEcho && Math.Abs(deviceVolume - _lastSetVolume) > 0.01f;
    var muteChanged = !muteEcho && deviceMuted != _lastSetMute;

    if (volumeEcho)
    {
      _lastSetVolume = deviceVolume;
    }

    if (muteEcho)
    {
      _lastSetMute = deviceMuted;
    }

    if (!volumeChanged && !muteChanged)
    {
      return;
    }

    _lastSetVolume = deviceVolume;
    _lastSetMute = deviceMuted;

    // AUD-81 (pre-merge review M1). On EVERY reported change — a mute-only one too — the
    // speaker's own report becomes the connection's level, and console targets still queued are
    // dropped. It used to happen only when the level changed, so after a mute-only event the
    // known level stayed the pending console target, which a quantising device's report (0.46
    // for 0.4567) need not match. A push already in flight is not recalled; whichever of the two
    // the device applies last is where it stays, and the drain does not overwrite this level
    // with its own when it completes (L1/L2, _externalChangeSerial). Done before the event
    // below, so a subscriber reading KnownSpeakerLevel sees the speaker's level.
    lock (_consoleVolumeLock)
    {
      _pendingConsoleVolume = null;
      Volatile.Write(ref _latestConsoleTarget, float.NaN);
      Volatile.Write(ref _connectionVolume, deviceVolume);
      _externalChangeSerial++;
    }

    if (volumeChanged)
    {
      // AUD-80: a level set on the speaker (buttons, Google Home) is the level to come
      // back to. Keyed by ConnectedDevice, which a handler left attached to a torn-down
      // client can misattribute (C-126 in the AUD-5 plan) — bounded, as that note says.
      RememberVolume(deviceVolume);
    }

    if (muteChanged && !deviceMuted)
    {
      // Unmuted on the speaker itself: nothing of ours is left to release at teardown.
      Interlocked.Exchange(ref _consoleMutedGeneration, -1);
    }

    _logger.LogInformation(
      "Cast device volume changed externally: {Volume:P0}, Muted: {Muted} (level changed: {VolumeChanged})",
      deviceVolume, deviceMuted, volumeChanged);

    // AUD-81 (pre-merge review M1): the re-sync this event causes is made explicit rather than
    // inferred from float equality. While it is raised, SpeakerChangeBeingApplied reports the
    // speaker's level on this thread, so the follower recognises the master/mute writes
    // AudioStateUpdateService makes synchronously inside it as the speaker's own and pushes
    // nothing back. Restored (not cleared) afterwards, in case of nesting.
    var previousOwner = t_speakerChangeOwner;
    var previousLevel = t_speakerChangeLevel;
    t_speakerChangeOwner = this;
    t_speakerChangeLevel = deviceVolume;
    try
    {
      CastVolumeChanged?.Invoke(this, new CastVolumeChangedEventArgs
      {
        Volume = deviceVolume,
        IsMuted = deviceMuted,
        IsInitialSync = false,
        VolumeChanged = volumeChanged
      });
    }
    finally
    {
      t_speakerChangeOwner = previousOwner;
      t_speakerChangeLevel = previousLevel;
    }
  }

  /// <summary>
  /// While this output is raising <see cref="CastVolumeChanged"/> for a change made on the
  /// speaker, and only on the thread raising it: the speaker's reported level. Otherwise null.
  /// </summary>
  /// <remarks>
  /// AUD-81 (pre-merge review M1). <c>AudioStateUpdateService</c> copies an external change into
  /// master volume and mute synchronously inside the event, so the console follower's mixer
  /// handlers run inside this window and can tell that re-sync from a console move by asking,
  /// instead of by comparing floats.
  /// </remarks>
  public float? SpeakerChangeBeingApplied =>
    ReferenceEquals(t_speakerChangeOwner, this) ? t_speakerChangeLevel : null;

  /// <summary>Records <paramref name="volume"/> for the connected device, if any (AUD-80).</summary>
  private void RememberVolume(float volume)
  {
    var deviceId = ConnectedDevice?.Id;
    if (deviceId != null)
    {
      _volumeStore?.Remember(deviceId, volume);
    }
  }

  /// <summary>
  /// Subscribes to <paramref name="client"/>'s <c>Disconnected</c> event for the connection
  /// being published, replacing any previous subscription. Caller holds <c>_lifecycleLock</c>.
  /// </summary>
  /// <remarks>
  /// SharpCaster raises <c>Disconnected</c> from its own teardown, which it runs on a
  /// heartbeat timeout and on any CLOSE the receiver sends (the receiver app closing,
  /// another sender taking over — not only a speaker going away). Against a speaker that
  /// has simply gone silent, the heartbeat timeout fires only because
  /// <see cref="GuardedTimerInvoker"/> re-arms SharpCaster's one-shot timer after each
  /// elapse; the library itself never re-arms it after a PING that goes unanswered. It raises it for OUR disconnects too; those are told apart by generation, since
  /// every deliberate teardown here bumps the generation before touching the client.
  /// The handler runs inside SharpCaster's unguarded <c>async void HeartBeatTimedOut</c>
  /// (see <see cref="SharpCasterCallbackGuard"/>), so it must not throw.
  /// </remarks>
  private void WatchForConnectionLoss_Locked(ChromecastClient client, int generation)
  {
    UnwatchConnectionLoss_Locked();
    EventHandler handler = (_, _) => ReportConnectionLost(
      generation, "the Cast connection closed (heartbeat timeout or the receiver closed it)", null);
    client.Disconnected += handler;
    _lossWatchedClient = client;
    _lossWatchHandler = handler;
  }

  /// <summary>Removes the subscription added by <see cref="WatchForConnectionLoss_Locked"/>, if any. Caller holds <c>_lifecycleLock</c>.</summary>
  private void UnwatchConnectionLoss_Locked()
  {
    if (_lossWatchedClient != null && _lossWatchHandler != null)
    {
      _lossWatchedClient.Disconnected -= _lossWatchHandler;
    }

    _lossWatchedClient = null;
    _lossWatchHandler = null;
  }

  /// <summary>
  /// Reports that the connection armed at <paramref name="generation"/> appears to be gone.
  /// Never throws and never blocks: the work is queued, because callers run on threads that must
  /// not wait on it — the guard's fault context (a thread-pool work item, or the timer/receive-loop
  /// thread for a synchronous throw), SharpCaster's teardown raising <c>Disconnected</c>, and our
  /// own streaming loop.
  /// </summary>
  /// <param name="generation">The connection generation the reporter was armed for.</param>
  /// <param name="reason">What was observed, for the log line and the event.</param>
  /// <param name="cause">The exception observed, if any.</param>
  internal void ReportConnectionLost(int generation, string reason, Exception? cause)
  {
    try
    {
      LastConnectionLossHandling = Task.Run(() => HandleConnectionLostAsync(generation, reason, cause));
    }
    catch
    {
      // Nothing to do: this is called from places where an escape would end the process.
    }
  }

  /// <summary>
  /// <b>Test seam (kind B — observation).</b> The most recently queued loss-handling task, so a
  /// test can await its completion instead of sleeping. Read by <c>GoogleCastOutputConnectionLossTests</c>.
  /// Written on every report in production too; nothing there reads it.
  /// </summary>
  internal Task LastConnectionLossHandling { get; private set; } = Task.CompletedTask;

  /// <summary>A loss report that arrived while the output was not yet Streaming.</summary>
  private sealed record DeferredConnectionLoss(int Generation, string Reason, Exception? Cause);

  // Written by HandleConnectionLostAsync, taken (exchange) by ReplayDeferredConnectionLoss.
  private DeferredConnectionLoss? _deferredLoss;

  /// <summary>
  /// Re-reports a loss that was deferred because the output was not yet Streaming. Called by
  /// StartAsync once it sets Streaming. <c>internal</c> so a test can drive it: StartAsync
  /// needs a launched receiver application.
  /// </summary>
  internal void ReplayDeferredConnectionLoss()
  {
    var deferred = Interlocked.Exchange(ref _deferredLoss, null);
    if (deferred != null)
    {
      ReportConnectionLost(deferred.Generation, deferred.Reason, deferred.Cause);
    }
  }

  /// <summary>
  /// Tears down a connection that was lost rather than closed: leaves <c>Streaming</c>, stops the
  /// DirectChannel loop, raises <see cref="Disconnected"/> with
  /// <see cref="ChromecastDisconnectedEventArgs.IsConnectionLost"/> set so the console can restore
  /// its local speakers, and closes the client. Acts at most once per connection.
  /// </summary>
  private async Task HandleConnectionLostAsync(int generation, string reason, Exception? cause)
  {
    ChromecastClient? client;
    ChromecastDeviceInfo? device;
    var deferredNow = false;

    try
    {
      await _lifecycleLock.WaitAsync().ConfigureAwait(false);
    }
    catch (ObjectDisposedException)
    {
      return; // Output disposed; nothing left to tear down.
    }

    try
    {
      // Current AND published. The generation bump below is what makes this once-only:
      // every further report for this connection — a second guard fault, the client's
      // own Disconnected when we close it below, the streaming loop — now finds a
      // different generation and stops here.
      if (_connectionGeneration != generation || _publishedGeneration != generation)
      {
        _logger.LogDebug(cause,
          "Cast: loss report for connection generation {Generation} ignored — not the current connection ({Reason})",
          generation, reason);
        return;
      }

      // Only a STREAMING output is rescued. Any other state means an operation of ours is
      // mid-flight on this connection — StartAsync still launching the receiver, or the gate's
      // StopAsync / a device switch tearing it down — and that operation owns the outcome.
      // Acting here would race it: marking Error under a StartAsync that then sets Streaming
      // left a zombie stream nothing could report on, and marking Error under a device switch
      // made its ConnectAsync throw (pre-merge review M1, M3). The report is DEFERRED, not
      // dropped: several reporters fire only once (the client's Disconnected, the streaming
      // loop's report), so dropping one could leave a dead connection nothing reports again.
      // StartAsync replays it on reaching Streaming; if a teardown wins instead it bumps the
      // generation, and the replayed report is then ignored as stale.
      if (State != AudioOutputState.Streaming)
      {
        _logger.LogInformation(cause,
          "Cast: loss reported while the output is {State}, not Streaming — deferred until it streams ({Reason})",
          State, reason);
        Interlocked.Exchange(ref _deferredLoss, new DeferredConnectionLoss(generation, reason, cause));
        deferredNow = true;
        client = null;
        device = null;
      }
      else
      {
        _connectionGeneration++;
        _publishedGeneration = -1;
        UnwatchConnectionLoss_Locked();
        client = _client;
        device = ConnectedDevice;
        _connectedReceiver = null;
        ConnectedDevice = null;
      }
    }
    finally
    {
      // Guarded: DisposeAsync disposes the lock after its own release (with the client's
      // disconnect in between), and a report granted the semaphore in that window would
      // otherwise fault on the way out.
      try { _lifecycleLock.Release(); }
      catch (ObjectDisposedException) { /* output disposed */ }
    }

    if (deferredNow)
    {
      // StartAsync may have set Streaming and looked for a deferred report between the state
      // check above and the write. Both sides take it with an exchange, so exactly one acts.
      if (State == AudioOutputState.Streaming)
      {
        ReplayDeferredConnectionLoss();
      }
      return;
    }

    _logger.LogWarning(cause,
      "Cast: lost the connection to {Name} ({Reason}) — Cast output stopped",
      device?.FriendlyName ?? "<unknown device>", reason);

    // AUD-81: a LOST connection cannot be unmuted — there is no socket left to send SET_MUTE
    // on. If the console had muted this speaker it stays muted until it is unmuted on the
    // speaker or in Google Home, or by a console unmute while casting to it again (a connect
    // never unmutes a speaker by itself). Clearing the mark keeps a later teardown from
    // aiming the unmute at whatever connection comes next.
    Interlocked.Exchange(ref _consoleMutedGeneration, -1);

    IsEnabledInternal = false;
    Name = "Google Cast Output";
    // Error, not Ready: the output did not stop cleanly, and ConnectAsync recovers from Error
    // by building a fresh ChromecastClient — which is what a dead socket calls for.
    State = AudioOutputState.Error;
    CancelPendingMetadataUpdate();

    // Raised BEFORE the slow cleanup below, so the local speakers come back without waiting
    // on it: stopping the streaming loop can take up to its 5 s cap when a send is stuck on
    // the dead socket.
    try
    {
      Disconnected?.Invoke(this, new ChromecastDisconnectedEventArgs
      {
        Device = device,
        Reason = reason,
        IsConnectionLost = true
      });
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Cast: a Disconnected subscriber threw while handling a lost connection");
    }

    var directStreaming = Interlocked.Exchange(ref _directStreaming, null);
    if (directStreaming != null)
    {
      UnregisterCustomChannel(Interlocked.Exchange(ref _directChannel, null));
      try { await directStreaming.DisposeAsync().ConfigureAwait(false); }
      catch (Exception ex) { _logger.LogDebug(ex, "Cast: error stopping DirectChannel streaming after connection loss"); }
    }

    try
    {
      UnsubscribeFromReceiverStatus(client);
      if (client != null)
      {
        await client.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
      }
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Cast: error closing the client after connection loss");
    }
  }

  // ---------------------------------------------------------------------------------------
  // AUD-81 — the console drives the connected speaker's volume and mute.
  // ---------------------------------------------------------------------------------------

  /// <summary>A published connection, snapshotted as one unit under <c>_lifecycleLock</c>.</summary>
  private sealed record PublishedConnection(int Generation, ChromecastClient Client, ChromecastDeviceInfo Device);

  /// <summary>
  /// The published connection, or null when none is (nothing published, or the published
  /// generation has been superseded). Caller holds <c>_lifecycleLock</c>. Mutates nothing.
  /// </summary>
  private PublishedConnection? SnapshotPublishedConnection_Locked()
  {
    if (_publishedGeneration < 0 || _publishedGeneration != _connectionGeneration ||
        _client == null || ConnectedDevice == null)
    {
      return null;
    }

    return new PublishedConnection(_publishedGeneration, _client, ConnectedDevice);
  }

  /// <summary>
  /// The SnapshotPublished critical section, awaited. Null after disposal (the lock is
  /// disposed) as well as when nothing is published.
  /// </summary>
  private async Task<PublishedConnection?> SnapshotPublishedConnectionAsync()
  {
    try
    {
      await _lifecycleLock.WaitAsync().ConfigureAwait(false);
    }
    catch (ObjectDisposedException)
    {
      return null;
    }

    try
    {
      return SnapshotPublishedConnection_Locked();
    }
    finally
    {
      try { _lifecycleLock.Release(); }
      catch (ObjectDisposedException) { /* output disposed */ }
    }
  }

  /// <summary>
  /// The SnapshotPublished critical section, entered with a synchronous <c>Wait</c> for callers
  /// that cannot await — the master mixer's event handlers. Safe because every holder of the
  /// lock is await-free inside it, so the wait is bounded by a few field writes.
  /// </summary>
  private PublishedConnection? SnapshotPublishedConnection()
  {
    try
    {
      _lifecycleLock.Wait();
    }
    catch (ObjectDisposedException)
    {
      return null;
    }

    try
    {
      return SnapshotPublishedConnection_Locked();
    }
    finally
    {
      try { _lifecycleLock.Release(); }
      catch (ObjectDisposedException) { /* output disposed */ }
    }
  }

  /// <summary>
  /// The connection the console may drive right now — present only while this output is
  /// <c>Streaming</c> with a published connection — with the speaker's level as this output
  /// currently understands it: the latest console target still queued or in flight, else the
  /// level last set, restored or observed on the device (<c>NaN</c> when unknown).
  /// </summary>
  public CastConsoleTarget? GetConsoleVolumeTarget()
  {
    if (IsDisposed || State != AudioOutputState.Streaming)
    {
      return null;
    }

    var connection = SnapshotPublishedConnection();
    if (connection == null)
    {
      return null;
    }

    return new CastConsoleTarget(
      connection.Generation,
      connection.Device.Id,
      connection.Device.FriendlyName,
      KnownSpeakerLevel,
      _lastSetMute);
  }

  /// <summary>
  /// The speaker level this output last set, restored or observed (or the latest console
  /// target still queued or in flight), 0.0-1.0; <c>NaN</c> when unknown. Not a live read —
  /// see <see cref="ReadSpeakerVolumeAsync"/>.
  /// </summary>
  public float KnownSpeakerLevel
  {
    get
    {
      var latest = Volatile.Read(ref _latestConsoleTarget);
      return float.IsNaN(latest) ? Volatile.Read(ref _connectionVolume) : latest;
    }
  }

  /// <summary>The speaker mute this output last set or observed. Not a live read.</summary>
  public bool KnownSpeakerMuted => _lastSetMute;

  /// <summary>
  /// True while this application has muted the connected speaker because the console was
  /// muted; a deliberate teardown stops the receiver application and then unmutes it.
  /// </summary>
  public bool IsSpeakerMutedByConsole => Volatile.Read(ref _consoleMutedGeneration) >= 0;

  /// <summary>
  /// Wires the console follower in (AUD-81): <paramref name="isConsoleMuted"/> is read when
  /// streaming starts, and the console lines are logged to <paramref name="consoleLogger"/>.
  /// </summary>
  internal void AttachConsoleFollower(Func<bool> isConsoleMuted, ILogger consoleLogger)
  {
    _isConsoleMuted = isConsoleMuted ?? throw new ArgumentNullException(nameof(isConsoleMuted));
    _consoleLogger = consoleLogger ?? throw new ArgumentNullException(nameof(consoleLogger));
  }

  private ILogger ConsoleLogger => _consoleLogger ?? _logger;

  /// <summary>
  /// Sends <paramref name="level"/> to the speaker of connection <paramref name="generation"/>,
  /// coalesced: at most one SET_VOLUME is in flight per output, and requests made while one is
  /// in flight replace a single pending target (latest wins), sent when it completes. A target
  /// whose connection is no longer the published one, or arriving while the output is not
  /// <c>Streaming</c>, is dropped.
  /// </summary>
  /// <param name="level">The speaker level, 0.0-1.0.</param>
  /// <param name="generation">The connection the caller mapped the level for
  /// (<see cref="CastConsoleTarget.Generation"/>).</param>
  /// <param name="consoleLevel">The console level it was mapped from, carried through to the
  /// result for logging only.</param>
  /// <returns>
  /// The burst this request joined: every request made while one drain runs gets the same task,
  /// which completes when the drain has nothing left to send. On completion the final applied
  /// level has been written as the connection's level and remembered for the device (once per
  /// burst, not per request) — unless a change reported by the speaker, or a new connection,
  /// superseded it while it was on the network, in which case neither happens. A failure is
  /// logged at Warning once per burst and reported in the result, never thrown.
  /// </returns>
  public Task<CastConsoleVolumeResult> SetDeviceVolumeFromConsoleAsync(
    float level, int generation, float consoleLevel = float.NaN)
  {
    var clamped = float.IsNaN(level) ? 0f : Math.Clamp(level, 0f, 1f);
    lock (_consoleVolumeLock)
    {
      _pendingConsoleVolume = (clamped, generation, consoleLevel);
      Volatile.Write(ref _latestConsoleTarget, clamped);
      return _consoleVolumeDrain ??= Task.Run(DrainConsoleVolumeAsync);
    }
  }

  /// <summary>
  /// <b>Test seam (kind B — observation).</b> The drain currently running, if any, so a test can
  /// await it. Read by <c>GoogleCastOutputConsoleVolumeTests</c>; nothing in production reads it.
  /// </summary>
  internal Task<CastConsoleVolumeResult>? CurrentConsoleVolumeDrainForTests
  {
    get
    {
      lock (_consoleVolumeLock)
      {
        return _consoleVolumeDrain;
      }
    }
  }

  /// <summary>
  /// Sends pending console targets one at a time until none is left. Runs on the thread pool,
  /// never under <c>_lifecycleLock</c> (it takes it only for the SnapshotPublished section
  /// before each send).
  /// </summary>
  private async Task<CastConsoleVolumeResult> DrainConsoleVolumeAsync()
  {
    float? applied = null;
    var appliedConsole = float.NaN;
    ChromecastDeviceInfo? appliedDevice = null;
    Exception? failure = null;
    var sends = 0;

    while (true)
    {
      (float Level, int Generation, float ConsoleLevel) next;
      lock (_consoleVolumeLock)
      {
        if (_pendingConsoleVolume is not { } pending)
        {
          _consoleVolumeDrain = null;
          Volatile.Write(ref _latestConsoleTarget, float.NaN);
          break;
        }

        next = pending;
        _pendingConsoleVolume = null;
      }

      // Re-checked per send, not once per burst: the connection can be superseded or torn
      // down while an earlier push in the same burst was on the network.
      var connection = State == AudioOutputState.Streaming
        ? await SnapshotPublishedConnectionAsync().ConfigureAwait(false)
        : null;
      if (connection == null || connection.Generation != next.Generation)
      {
        _logger.LogDebug(
          "Cast: console volume {Volume:P0} dropped — connection generation {Generation} is no longer the streaming one",
          next.Level, next.Generation);
        continue;
      }

      int serialAtSend;
      lock (_consoleVolumeLock)
      {
        serialAtSend = _externalChangeSerial;
      }

      sends++;
      try
      {
        if (await PushVolumeToDeviceAsync(connection.Client, next.Level)
              .WaitAsync(ConsoleCommandTimeout, _timeProvider).ConfigureAwait(false))
        {
          // Pre-merge review L1/L2. Recorded as the connection's level (and, at the end of the
          // burst, remembered) only if nothing has superseded it since the send went out: the
          // connection is still the published one, and no external change was reported
          // meanwhile — that report is the newer truth, and overwriting it would also hand the
          // follower a known level the speaker never reported. Checked and written under the
          // lock the external path writes under, so neither write can land between the other's
          // check and write.
          bool current;
          lock (_consoleVolumeLock)
          {
            current = _externalChangeSerial == serialAtSend &&
              Volatile.Read(ref _publishedGeneration) == next.Generation;
            if (current)
            {
              Volatile.Write(ref _connectionVolume, next.Level);
            }
          }

          if (current)
          {
            applied = next.Level;
            appliedConsole = next.ConsoleLevel;
            appliedDevice = connection.Device;
          }
          else
          {
            applied = null;
            appliedDevice = null;
            _logger.LogDebug(
              "Cast: console volume {Volume:P0} was sent, but a speaker change or a new connection superseded it — not recorded",
              next.Level);
          }
        }
      }
      catch (Exception ex)
      {
        failure ??= ex;
      }
    }

    if (applied is float level && appliedDevice != null)
    {
      // Once per burst: the store persists the whole map on every change, so remembering
      // every slider tick would be one config-store write per tick.
      _volumeStore?.Remember(appliedDevice.Id, level);
    }

    if (failure != null)
    {
      _logger.LogWarning(failure,
        "Cast: the console volume could not be applied to the speaker ({Sends} send(s) in this burst)",
        sends);
    }

    return new CastConsoleVolumeResult(applied, appliedConsole, appliedDevice?.FriendlyName, sends, failure != null);
  }

  /// <summary>
  /// Sends the console's mute state to the speaker of connection <paramref name="generation"/>,
  /// coalesced like the volume (pre-merge review L4): at most one SET_MUTE is in flight per
  /// output, and a request made meanwhile replaces a single pending state (latest wins), sent
  /// when it completes. A state is skipped when the speaker already holds it as far as this
  /// output knows (which is what keeps an external mute, re-synced to the console, from being
  /// pushed back), when the output is not <c>Streaming</c>, or when its connection is no longer
  /// the published one. Muting marks the connection "muted by console"; see
  /// <see cref="IsSpeakerMutedByConsole"/>.
  /// </summary>
  /// <returns>
  /// True when the burst this request joined ended with the speaker acknowledging
  /// <paramref name="muted"/>; false when nothing was sent, a later request in the same burst
  /// won, or the send failed. Failures are logged, never thrown.
  /// </returns>
  public async Task<bool> SetDeviceMuteFromConsoleAsync(bool muted, int generation)
  {
    Task<bool?> drain;
    lock (_consoleMuteLock)
    {
      _pendingConsoleMute = (muted, generation);
      drain = _consoleMuteDrain ??= Task.Run(DrainConsoleMuteAsync);
    }

    return await drain.ConfigureAwait(false) == muted;
  }

  /// <summary>
  /// Sends pending console mute states one at a time until none is left; returns the last state
  /// the speaker acknowledged, or null. The sends are sequential, so the "muted by console" mark
  /// is set and cleared in the order the speaker received them.
  /// </summary>
  private async Task<bool?> DrainConsoleMuteAsync()
  {
    bool? applied = null;

    while (true)
    {
      (bool Muted, int Generation) next;
      lock (_consoleMuteLock)
      {
        if (_pendingConsoleMute is not { } pending)
        {
          _consoleMuteDrain = null;
          break;
        }

        next = pending;
        _pendingConsoleMute = null;
      }

      if (State != AudioOutputState.Streaming || next.Muted == _lastSetMute)
      {
        continue;
      }

      var connection = await SnapshotPublishedConnectionAsync().ConfigureAwait(false);
      if (connection == null || connection.Generation != next.Generation)
      {
        continue;
      }

      try
      {
        if (!await SendMuteToDeviceAsync(connection.Client, next.Muted)
              .WaitAsync(ConsoleCommandTimeout, _timeProvider).ConfigureAwait(false))
        {
          continue;
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Cast: the console {Action} could not be applied to the speaker",
          next.Muted ? "mute" : "unmute");
        continue;
      }

      if (next.Muted)
      {
        Volatile.Write(ref _consoleMutedGeneration, connection.Generation);
      }
      else
      {
        Interlocked.CompareExchange(ref _consoleMutedGeneration, -1, connection.Generation);
      }

      applied = next.Muted;
    }

    return applied;
  }

  /// <summary>
  /// Mutes the speaker as streaming starts when the console is muted, so a muted console never
  /// plays out loud on it. Never UNmutes: a speaker muted on its own side while the console is
  /// not stays muted. Not gated on the console's active output — this runs only from
  /// <c>StartAsync</c>, i.e. because Cast is being made the output, and on the common path
  /// (<c>cast/connect</c>) the output gate records <c>google-cast</c> only after the stream has
  /// started.
  /// </summary>
  /// <remarks>
  /// Skipped only when this connection has positively seen the speaker muted. A speaker whose
  /// state is unknown — its initial read failed, so the baseline is still the "not muted" that
  /// <see cref="ResetSpeakerStateForNewConnection"/> sets — is treated as unmuted, and the mute is
  /// sent (pre-merge review M2). Sending a mute to an already-muted speaker is harmless; skipping
  /// one is how a muted console would play out loud.
  /// </remarks>
  private async Task MuteForConsoleAtStartAsync()
  {
    bool consoleMuted;
    try
    {
      consoleMuted = _isConsoleMuted?.Invoke() ?? false;
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Cast: could not read the console's mute state");
      return;
    }

    if (!consoleMuted || _lastSetMute)
    {
      // Not muted, or this connection has already seen the speaker muted — by us earlier in
      // this start (then it is already marked), or on its own side (then a teardown must not
      // unmute it, so it is not marked).
      return;
    }

    var connection = await SnapshotPublishedConnectionAsync().ConfigureAwait(false);
    if (connection == null)
    {
      return;
    }

    try
    {
      if (await SendMuteToDeviceAsync(connection.Client, true)
            .WaitAsync(ConsoleCommandTimeout, _timeProvider).ConfigureAwait(false))
      {
        Volatile.Write(ref _consoleMutedGeneration, connection.Generation);
        ConsoleLogger.LogInformation(
          "Cast: console is muted → speaker {Name} muted as casting starts", connection.Device.FriendlyName);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Cast: could not mute {Name} for the muted console as casting started",
        connection.Device.FriendlyName);
    }
  }

  /// <summary>
  /// Before a deliberate teardown of connection <paramref name="generation"/>: if this
  /// application muted its speaker for the console, stops the receiver application on the device
  /// and THEN unmutes the speaker, so it is not left muted for its next user. Bounded by short
  /// timeouts; never throws. A connection that was LOST is not handled here (see
  /// <c>HandleConnectionLostAsync</c>): it cannot be unmuted.
  /// </summary>
  /// <remarks>
  /// <para><b>What is guaranteed</b> (pre-merge review H1): an unmute is sent only when the
  /// device's most recent receiver status — the one SharpCaster holds for the channel, or the
  /// answer to the stop sent here — lists no receiver application with our <c>ApplicationId</c>, so
  /// neither the live HttpMp3 stream nor DirectChannel audio the receiver had buffered can play out
  /// loud after it. When that held status already lists none, no stop is sent and the unmute
  /// follows without a new round-trip; the status is then as current as the device last made it.
  /// If it cannot be established (the held status has no applications list, which is also how a
  /// channel that never received a status looks — hostile review F4 — the stop
  /// failed or timed out, the device's answer still lists our application, or a different
  /// application is first in its status, which the library's stop would close instead) the
  /// speaker is deliberately LEFT MUTED and an Information line says so: a muted speaker the owner
  /// has to unmute is recoverable; a muted console playing out loud is the one outcome this
  /// feature must never produce. The mark is kept in that case, so a later teardown of the same
  /// connection (DisconnectAsync after StopAsync) tries again.</para>
  /// <para><b>What is not:</b> that the unmute itself lands (a failure is logged; the speaker stays
  /// muted). Only a connection the console muted is touched: otherwise a teardown leaves the
  /// receiver application to the media-stop and close paths, as before AUD-81.</para>
  /// <para>A later <c>StartAsync</c> on the same client still launches the application:
  /// SharpCaster 3.0.0's <c>ChromecastClient.LaunchApplicationAsync</c> joins an existing session
  /// only when its last receiver status lists the application, and the stop's response — a
  /// RECEIVER_STATUS without it — is what that status is replaced with (the receive loop runs
  /// <c>ReceiverChannel.OnMessageReceived</c> before completing the request).</para>
  /// </remarks>
  private async Task ReleaseConsoleMuteAsync(ChromecastClient? client, int generation, string? deviceName)
  {
    if (client == null || generation < 0 || Volatile.Read(ref _consoleMutedGeneration) != generation)
    {
      return;
    }

    bool applicationGone;
    try
    {
      applicationGone = await StopReceiverApplicationAsync(client)
        .WaitAsync(TeardownAppStopTimeout, _timeProvider).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Cast: stopping the receiver application before the teardown unmute failed");
      applicationGone = false;
    }

    if (!applicationGone)
    {
      ConsoleLogger.LogInformation(
        "Cast: speaker {Name} left muted — its receiver application could not be confirmed stopped, and " +
        "unmuting could play our audio under a muted console; unmute it on the speaker or in Google Home",
        deviceName);
      return;
    }

    // Claimed only now, so a failed stop above keeps the mark for a later teardown to retry.
    if (Interlocked.CompareExchange(ref _consoleMutedGeneration, -1, generation) != generation)
    {
      return;
    }

    try
    {
      if (await SendMuteToDeviceAsync(client, false)
            .WaitAsync(TeardownUnmuteTimeout, _timeProvider).ConfigureAwait(false))
      {
        ConsoleLogger.LogInformation(
          "Cast: speaker {Name} unmuted before closing, after its receiver application stopped — it had been muted for the console",
          deviceName);
      }
    }
    catch (Exception ex)
    {
      ConsoleLogger.LogInformation(
        "Cast: could not unmute speaker {Name} before closing ({Error}) — it may stay muted",
        deviceName, ex.GetType().Name);
      _logger.LogDebug(ex, "Cast: teardown unmute failed");
    }
  }

  /// <summary>
  /// Stops our receiver application (<c>ApplicationId</c>) on the device behind
  /// <paramref name="client"/>. True when the channel's most recent receiver status lists no
  /// application of ours (none was running, or the stop sent here removed it); false when the held
  /// status has no applications list (never received, or not reported), or that cannot otherwise
  /// be established. Throws what SharpCaster throws.
  /// </summary>
  private async Task<bool> StopReceiverApplicationAsync(ChromecastClient client)
  {
    if (CastStopApplicationOverrideForTests != null)
    {
      return await CastStopApplicationOverrideForTests().ConfigureAwait(false);
    }

    var receiverChannel = client.GetChannel<ReceiverChannel>();
    if (receiverChannel == null)
    {
      return false;
    }

    var applicationId = _options.ApplicationId;
    var status = receiverChannel.ReceiverStatus;
    if (status?.Applications == null)
    {
      // Hostile review F4. Unknown is not "stopped": the speaker stays muted. A held status with
      // no applications list is treated as unknown, not as "nothing running", because that is
      // exactly how SharpCaster 3.0.0 presents a channel that has never received a status — its
      // ReceiverStatus is a default object, not null (measured in
      // GoogleCastOutputConsoleVolumeTests.WithNoReceiverStatusEverReceived_…). The cost: a device
      // whose real status omits the list is left muted at teardown, which is logged and
      // recoverable. (The stop's own answer below is the device's report, so there a missing
      // list does mean nothing is running.)
      return false;
    }

    var ours = status.Applications.FirstOrDefault(a => a.AppId == applicationId);
    if (ours == null)
    {
      // The device's last status lists no application of ours: nothing of ours can be playing.
      return true;
    }

    if (!ReferenceEquals(status.Application, ours))
    {
      // SharpCaster's StopApplication stops the FIRST application in the status. Closing someone
      // else's is not ours to do, so our stop cannot be made.
      return false;
    }

    var after = await receiverChannel.StopApplication().ConfigureAwait(false);
    return after != null && after.Applications?.Any(a => a.AppId == applicationId) != true;
  }

  /// <summary>
  /// Sends <paramref name="muted"/> as a SET_MUTE, recording it in the echo memory and baseline
  /// first. Returns false when the client exposes no receiver channel (nothing sent). Throws
  /// what SharpCaster throws, with the baseline restored.
  /// </summary>
  private async Task<bool> SendMuteToDeviceAsync(ChromecastClient client, bool muted)
  {
    var echo = RecordMutePush(muted);
    var previous = _lastSetMute;
    _lastSetMute = muted;
    try
    {
      if (CastSetMuteOverrideForTests != null)
      {
        await CastSetMuteOverrideForTests(muted).ConfigureAwait(false);
        return true;
      }

      var receiverChannel = client.GetChannel<ReceiverChannel>();
      if (receiverChannel == null)
      {
        _lastSetMute = previous;
        return false;
      }

      await receiverChannel.SetMute(muted).ConfigureAwait(false);
      return true;
    }
    catch
    {
      _lastSetMute = previous;
      throw;
    }
    finally
    {
      CompleteEchoPush(echo);
    }
  }

  /// <summary>
  /// A LIVE read of the connected speaker's level and mute, bounded at 3 s, for diagnostics
  /// (<c>GET /api/devices/cast/volume</c>). Null when the output is not <c>Streaming</c> with a
  /// published connection. Read-only: changes nothing here and fires nothing — including through
  /// SharpCaster's side door, which raises the read's GET_STATUS response as a
  /// <c>ReceiverStatusChanged</c>: a status arriving while the read is in flight only re-baselines
  /// the echo filter (pre-merge review L5; see <c>_diagnosticReadsPending</c>). The price: a
  /// change made on the speaker inside that window is absorbed, not reported.
  /// </summary>
  /// <exception cref="TimeoutException">The speaker did not answer within 3 s.</exception>
  public async Task<CastSpeakerVolumeReading?> ReadSpeakerVolumeAsync(CancellationToken cancellationToken = default)
  {
    if (IsDisposed || State != AudioOutputState.Streaming)
    {
      return null;
    }

    var connection = await SnapshotPublishedConnectionAsync().ConfigureAwait(false);
    if (connection == null)
    {
      return null;
    }

    // Marked BEFORE the request is sent and lifted only when the READ completes — not when the
    // 3 s wait below gives up — because SharpCaster raises the response's status event before it
    // completes the read, however late that is.
    Interlocked.Increment(ref _diagnosticReadsPending);
    Task<(float Volume, bool Muted)?> read;
    try
    {
      read = ReadCastVolumeStatusAsync(connection.Client);
    }
    catch
    {
      Interlocked.Decrement(ref _diagnosticReadsPending);
      throw;
    }

    _ = read.ContinueWith(
      _ => Interlocked.Decrement(ref _diagnosticReadsPending),
      CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    var reading = await read.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);

    var known = KnownSpeakerLevel;
    return new CastSpeakerVolumeReading(
      connection.Device.FriendlyName,
      reading?.Volume,
      reading?.Muted,
      float.IsNaN(known) ? null : known,
      _lastSetMute,
      IsSpeakerMutedByConsole);
  }

  /// <summary>Records a level sent to the device, for <see cref="IsRecentVolumePush"/>.</summary>
  private EchoPush<float> RecordVolumePush(float level)
  {
    lock (_echoLock)
    {
      PruneEchoMemory_Locked();
      var push = new EchoPush<float>(level, _timeProvider.GetTimestamp());
      _recentVolumePushes.Add(push);
      return push;
    }
  }

  /// <summary>Records a mute sent to the device, for <see cref="IsRecentMutePush"/>.</summary>
  private EchoPush<bool> RecordMutePush(bool muted)
  {
    lock (_echoLock)
    {
      PruneEchoMemory_Locked();
      var push = new EchoPush<bool>(muted, _timeProvider.GetTimestamp());
      _recentMutePushes.Add(push);
      return push;
    }
  }

  /// <summary>
  /// Marks a recorded send complete (acknowledged, failed or abandoned) and re-stamps it, so its
  /// confirmation is recognised for <see cref="EchoWindow"/> from completion, not from start.
  /// </summary>
  private void CompleteEchoPush<T>(EchoPush<T> push)
  {
    lock (_echoLock)
    {
      push.Stamp = _timeProvider.GetTimestamp();
      push.InFlight = false;
    }
  }

  /// <summary>
  /// True when <paramref name="level"/> lies within the closed range spanned by the levels whose
  /// sends are in flight or completed within the last <see cref="EchoWindow"/> — from the lowest
  /// to the highest of them — widened by <see cref="EchoLevelTolerance"/> at each end. False when
  /// there are none.
  /// </summary>
  /// <remarks>
  /// <para>Hostile review F3. This used to be "within 0.01 of one recent push", so a speaker that
  /// quantises more coarsely than 0.01 reported our own push as an external change — master volume
  /// rewritten and AUD-80 remembering it. A level between two of our recent pushes is now an echo
  /// however the device rounded it: during a console drag (0.437 then 0.50) a 1/15-step speaker's
  /// 0.4667 is recognised.</para>
  /// <para>What it does NOT recognise: a SINGLE recent push (or the extremes of several) reported
  /// more than <see cref="EchoLevelTolerance"/> beyond it — 0.437 reported as 0.4667 with nothing
  /// above it in the window still reads as external. And what it gives up: a genuine change made
  /// on the speaker within the window that lands inside the range is absorbed, not reported.
  /// Changes outside the range are reported as before (AUD-5).</para>
  /// </remarks>
  private bool IsRecentVolumePush(float level)
  {
    lock (_echoLock)
    {
      PruneEchoMemory_Locked();
      if (_recentVolumePushes.Count == 0)
      {
        return false;
      }

      float lowest = float.MaxValue;
      float highest = float.MinValue;
      foreach (EchoPush<float> push in _recentVolumePushes)
      {
        lowest = Math.Min(lowest, push.Value);
        highest = Math.Max(highest, push.Value);
      }

      return level >= lowest - EchoLevelTolerance && level <= highest + EchoLevelTolerance;
    }
  }

  /// <summary>
  /// True when <paramref name="muted"/> was sent by a send in flight or completed within the last
  /// <see cref="EchoWindow"/>.
  /// </summary>
  private bool IsRecentMutePush(bool muted)
  {
    lock (_echoLock)
    {
      PruneEchoMemory_Locked();
      return _recentMutePushes.Exists(p => p.Value == muted);
    }
  }

  private void PruneEchoMemory_Locked()
  {
    _recentVolumePushes.RemoveAll(IsExpired);
    _recentMutePushes.RemoveAll(IsExpired);
  }

  private bool IsExpired<T>(EchoPush<T> push) =>
    _timeProvider.GetElapsedTime(push.Stamp) > (push.InFlight ? EchoInFlightLimit : EchoWindow);

  /// <inheritdoc />
  public override async ValueTask DisposeAsync()
  {
    if (IsDisposed)
    {
      return;
    }

    if (_connectedReceiver != null)
    {
      try
      {
        await DisconnectAsync();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error during disconnect in dispose");
      }
    }

    var directStreaming = Interlocked.Exchange(ref _directStreaming, null);
    if (directStreaming != null)
    {
      var directChannel = Interlocked.Exchange(ref _directChannel, null);
      try
      {
        await directStreaming.DisposeAsync();
      }
      finally
      {
        UnregisterCustomChannel(directChannel);
      }
    }

    CancelPendingMetadataUpdate();

    // Final generation bump so a connect still in flight discards itself rather
    // than publishing onto a disposed output, and take the client as a snapshot.
    ChromecastClient? client;
    await _lifecycleLock.WaitAsync().ConfigureAwait(false);
    try
    {
      _connectionGeneration++;
      _publishedGeneration = -1;
      UnwatchConnectionLoss_Locked();
      client = _client;
      // Deliberately NOT nulled: several Start/stream helpers dereference
      // _client with `!` on the assumption it outlives the output, and nulling
      // it here would turn disposal-during-startup into an NRE instead of the
      // ObjectDisposedException those paths already expect.
    }
    finally
    {
      _lifecycleLock.Release();
    }

    if (client != null)
    {
      await client.DisconnectAsync().ConfigureAwait(false);
    }

    _lifecycleLock.Dispose();
    DisposeBase();
  }
}

/// <summary>
/// Information about a discovered Chromecast device.
/// </summary>
public record ChromecastDeviceInfo
{
  /// <summary>
  /// Gets or sets the unique identifier for the device.
  /// </summary>
  public required string Id { get; init; }

  /// <summary>
  /// Gets or sets the friendly name of the device.
  /// </summary>
  public required string FriendlyName { get; init; }

  /// <summary>
  /// Gets or sets the IP address of the device.
  /// </summary>
  public required string IpAddress { get; init; }

  /// <summary>
  /// Gets or sets the port number.
  /// </summary>
  public required int Port { get; init; }

  /// <summary>
  /// Gets or sets the device model.
  /// </summary>
  public required string Model { get; init; }
}

/// <summary>
/// Event arguments for Chromecast device discovery.
/// </summary>
public class ChromecastDeviceDiscoveredEventArgs : EventArgs
{
  /// <summary>
  /// Gets the discovered device.
  /// </summary>
  public required ChromecastDeviceInfo Device { get; init; }
}

/// <summary>
/// Event arguments for Chromecast connection.
/// </summary>
public class ChromecastConnectedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the connected device.
  /// </summary>
  public required ChromecastDeviceInfo Device { get; init; }
}

/// <summary>
/// Event arguments for Chromecast disconnection.
/// </summary>
public class ChromecastDisconnectedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the disconnected device.
  /// </summary>
  public ChromecastDeviceInfo? Device { get; init; }

  /// <summary>
  /// Gets the reason for disconnection.
  /// </summary>
  public string? Reason { get; init; }

  /// <summary>
  /// True when the connection was lost rather than closed by this application — the
  /// speaker lost power or Wi-Fi (a write to it failed or stalled, or it stopped answering
  /// heartbeats), or the receiver closed the session itself (AUD-84). False for a disconnect this application asked for.
  /// </summary>
  public bool IsConnectionLost { get; init; }
}

/// <summary>
/// Event arguments for Cast device volume changes (external).
/// </summary>
public class CastVolumeChangedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the new volume level (0.0 to 1.0).
  /// </summary>
  public required float Volume { get; init; }

  /// <summary>
  /// Gets whether the device is muted.
  /// </summary>
  public required bool IsMuted { get; init; }

  /// <summary>
  /// Gets whether this is the initial sync after connecting (not an external change).
  /// </summary>
  public bool IsInitialSync { get; init; }

  /// <summary>
  /// Whether the speaker's LEVEL changed (AUD-81, pre-merge review M1). False for a change of
  /// mute only — <see cref="Volume"/> is then just the level the speaker reported alongside it,
  /// possibly quantised, and a subscriber must not copy it anywhere as if it were news. True by
  /// default, so a producer that does not distinguish keeps the old meaning.
  /// </summary>
  public bool VolumeChanged { get; init; } = true;
}

/// <summary>
/// The connection the console may drive, from <see cref="GoogleCastOutput.GetConsoleVolumeTarget"/> (AUD-81).
/// </summary>
/// <param name="Generation">The connection's generation; pass it back with each command.</param>
/// <param name="DeviceId">The speaker's device id (the per-device volume key).</param>
/// <param name="DeviceName">The speaker's friendly name.</param>
/// <param name="SpeakerLevel">The speaker's level as this output understands it; NaN when unknown.</param>
/// <param name="SpeakerMuted">The speaker's mute as this output last set or observed it.</param>
public readonly record struct CastConsoleTarget(
  int Generation, string DeviceId, string DeviceName, float SpeakerLevel, bool SpeakerMuted);

/// <summary>
/// The outcome of one coalesced burst of console volume pushes (AUD-81).
/// </summary>
/// <param name="AppliedLevel">The last level the speaker acknowledged, or null when none was.</param>
/// <param name="AppliedConsoleLevel">The console level that level was mapped from (NaN if not given).</param>
/// <param name="DeviceName">The speaker the applied level went to.</param>
/// <param name="Sends">How many SET_VOLUMEs were sent in the burst.</param>
/// <param name="Failed">True when at least one send failed.</param>
public sealed record CastConsoleVolumeResult(
  float? AppliedLevel, float AppliedConsoleLevel, string? DeviceName, int Sends, bool Failed);

/// <summary>
/// A live read of the connected Cast speaker's volume (AUD-81), with this output's own view beside it.
/// </summary>
/// <param name="DeviceName">The speaker's friendly name.</param>
/// <param name="Level">The level the speaker reported, or null when its status carried none.</param>
/// <param name="Muted">The mute the speaker reported, or null when its status carried no volume.</param>
/// <param name="KnownLevel">The level this output last set or observed, or null when unknown.</param>
/// <param name="KnownMuted">The mute this output last set or observed.</param>
/// <param name="MutedByConsole">True when this application muted the speaker for the console.</param>
public sealed record CastSpeakerVolumeReading(
  string DeviceName, float? Level, bool? Muted, float? KnownLevel, bool KnownMuted, bool MutedByConsole);

/// <summary>
/// Now-playing metadata to display on Cast devices (Google Home app).
/// </summary>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Artist name.</param>
/// <param name="Album">Album name.</param>
/// <param name="AlbumArtUrl">Absolute URL to album art image.</param>
public record CastNowPlayingMetadata(string? Title, string? Artist, string? Album, string? AlbumArtUrl);

/// <summary>
/// A cached Cast device entry with a last-seen timestamp.
/// </summary>
public class CachedCastDevice
{
  /// <summary>
  /// Gets or sets the device info.
  /// </summary>
  public required ChromecastDeviceInfo Device { get; set; }

  /// <summary>
  /// Gets or sets when the device was last seen on the network.
  /// </summary>
  public DateTime LastSeen { get; set; }
}
