using Microsoft.Extensions.Logging;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Services;

/// <summary>
/// Centralized observable store for audio state.
/// Subscribes to SignalR events and caches playback, now-playing, volume,
/// fingerprint, and queue state. Components subscribe to change events
/// instead of managing their own SignalR subscriptions.
/// </summary>
public class AudioStateStore : IAsyncDisposable
{
  private readonly ILogger<AudioStateStore> _logger;
  private readonly AudioStateHubService _hubService;

  // --- Cached state ---
  public PlaybackStateDto? PlaybackState { get; private set; }
  public NowPlayingDto? NowPlaying { get; private set; }
  public float Volume { get; private set; } = 0.75f;
  public bool IsMuted { get; private set; }
  public FingerprintStatusDto? FingerprintStatus { get; private set; }
  // Most recent RadioStateDto delivered by SignalR. Carries NowPlayingMatchId
  // which the REST /api/radio/state endpoint cannot populate. Subscribers
  // can read this directly; null until the first hub broadcast.
  public RadioStateDto? RadioState { get; private set; }
  public int QueueCount { get; private set; }
  public Dictionary<string, float> SourceGainOffsets { get; private set; } = new();

  // --- Change events ---
  /// <summary>Raised when playback state (isPlaying, transport capabilities) changes.</summary>
  public event Func<Task>? PlaybackStateChanged;

  /// <summary>Raised when now-playing metadata (title, artist, album art) changes.</summary>
  public event Func<Task>? NowPlayingChanged;

  /// <summary>Raised when volume or mute state changes.</summary>
  public event Func<Task>? VolumeChanged;

  /// <summary>Raised when the active source changes.</summary>
  public event Func<Task>? SourceChanged;

  /// <summary>Raised when the queue changes.</summary>
  public event Func<Task>? QueueChanged;

  /// <summary>Raised when fingerprint status changes.</summary>
  public event Func<Task>? FingerprintStatusChanged;

  /// <summary>Raised when radio state changes. Carries the typed DTO so
  /// subscribers can read NowPlayingMatchId directly without a REST refetch.</summary>
  public event Func<RadioStateDto, Task>? RadioStateChanged;

  /// <summary>Raised when sleep state changes.</summary>
  public event Func<bool, Task>? SleepStateChanged;

  /// <summary>Raised when encoder connection state changes.</summary>
  public event Func<Task>? EncoderConnectionChanged;

  /// <summary>Raised when the encoder configuration tier changes.</summary>
  public event Func<Task>? EncoderConfigStatusChanged;

  /// <summary>Raised when the one attended event playback changes state (ADR-029 D6).</summary>
  public event Func<Task>? EventPlaybackChanged;

  public AudioStateStore(
    ILogger<AudioStateStore> logger,
    AudioStateHubService hubService)
  {
    _logger = logger;
    _hubService = hubService;

    // Subscribe to hub events
    _hubService.PlaybackStateChanged += OnHubPlaybackStateChanged;
    _hubService.NowPlayingChanged += OnHubNowPlayingChanged;
    _hubService.VolumeChanged += OnHubVolumeChanged;
    _hubService.SourceChanged += OnHubSourceChanged;
    _hubService.QueueChanged += OnHubQueueChanged;
    _hubService.FingerprintStatusChanged += OnHubFingerprintStatusChanged;
    _hubService.RadioStateChanged += OnHubRadioStateChanged;
    _hubService.SleepStateChanged += OnHubSleepStateChanged;
    _hubService.EncoderConnectionChanged += OnHubEncoderConnectionChanged;
    _hubService.EncoderConfigStatusChanged += OnHubEncoderConfigStatusChanged;
    _hubService.EventPlaybackChanged += OnHubEventPlaybackChanged;
  }

  /// <summary>
  /// Updates cached playback state. Call from initial data load or API refresh.
  /// </summary>
  public async Task UpdatePlaybackStateAsync(PlaybackStateDto? state)
  {
    PlaybackState = state;
    if (state != null)
    {
      Volume = state.Volume;
      IsMuted = state.IsMuted;
    }

    await NotifyAsync(PlaybackStateChanged);
  }

  /// <summary>
  /// Updates cached now-playing metadata. Call from initial data load or API refresh.
  /// </summary>
  public async Task UpdateNowPlayingAsync(NowPlayingDto? nowPlaying)
  {
    NowPlaying = nowPlaying;
    await NotifyAsync(NowPlayingChanged);
  }

  /// <summary>
  /// Updates cached volume/mute state.
  /// </summary>
  public async Task UpdateVolumeAsync(float volume, bool isMuted)
  {
    Volume = volume;
    IsMuted = isMuted;
    await NotifyAsync(VolumeChanged);
  }

  /// <summary>
  /// Updates cached fingerprint status.
  /// </summary>
  public async Task UpdateFingerprintStatusAsync(FingerprintStatusDto? status)
  {
    FingerprintStatus = status;
    await NotifyAsync(FingerprintStatusChanged);
  }

  /// <summary>
  /// Updates cached queue count.
  /// </summary>
  public async Task UpdateQueueCountAsync(int count)
  {
    QueueCount = count;
    await NotifyAsync(QueueChanged);
  }

  /// <summary>
  /// Updates cached source gain offsets.
  /// </summary>
  public void UpdateSourceGainOffsets(Dictionary<string, float> offsets)
  {
    SourceGainOffsets = offsets;
  }

  /// <summary>
  /// Triggers a source changed notification (e.g., after API call).
  /// </summary>
  public async Task NotifySourceChangedAsync()
  {
    await NotifyAsync(SourceChanged);
  }

  // --- Hub event handlers ---

  private async Task OnHubPlaybackStateChanged()
  {
    await NotifyAsync(PlaybackStateChanged);
  }

  private async Task OnHubNowPlayingChanged(NowPlayingDto? dto)
  {
    if (dto != null)
    {
      NowPlaying = dto;
    }

    await NotifyAsync(NowPlayingChanged);
  }

  private async Task OnHubVolumeChanged(VolumeDto? dto)
  {
    if (dto != null)
    {
      Volume = dto.Volume;
      IsMuted = dto.IsMuted;
    }

    await NotifyAsync(VolumeChanged);
  }

  private async Task OnHubSourceChanged()
  {
    await NotifyAsync(SourceChanged);
  }

  private async Task OnHubQueueChanged()
  {
    await NotifyAsync(QueueChanged);
  }

  private async Task OnHubFingerprintStatusChanged()
  {
    await NotifyAsync(FingerprintStatusChanged);
  }

  private async Task OnHubRadioStateChanged(RadioStateDto dto)
  {
    RadioState = dto;
    if (RadioStateChanged != null)
    {
      try
      {
        await RadioStateChanged.Invoke(dto);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error notifying AudioStateStore subscriber");
      }
    }
  }

  private async Task OnHubSleepStateChanged(bool isSleeping)
  {
    if (SleepStateChanged != null)
    {
      await SleepStateChanged.Invoke(isSleeping);
    }
  }

  private async Task OnHubEncoderConnectionChanged(EncoderConnectionDto dto)
  {
    // Cached so a component that mounts after the transition can still tell absent-at-boot from
    // dropped-mid-session — the two call for different notifications and share IsConnected=false.
    EncoderConnection = dto;
    await NotifyAsync(EncoderConnectionChanged);
  }

  /// <summary>
  /// Latest encoder presence transition, or null if none has been observed since this process
  /// started.
  /// </summary>
  /// <remarks>
  /// This said "this circuit" until ENC-12. It is not per circuit: AudioStateStore is registered
  /// AddSingleton in Program.cs, so the field is process-wide and outlives every circuit that reads
  /// it. Singleton is the right lifetime for a cache of one cabinet's hardware state — the comment
  /// was what was wrong, not the registration.
  /// </remarks>
  public EncoderConnectionDto? EncoderConnection { get; private set; }

  private async Task OnHubEncoderConfigStatusChanged(EncoderConfigStatusDto dto)
  {
    // Cached so a circuit that connects after the transition still knows the current tier — the badge
    // has to be right on a page loaded ten minutes after the fault, not only on the one that was open
    // when it happened.
    EncoderConfigStatus = dto;
    await NotifyAsync(EncoderConfigStatusChanged);
  }

  /// <summary>
  /// Latest encoder configuration tier, or null if none has been observed since this process started.
  /// </summary>
  public EncoderConfigStatusDto? EncoderConfigStatus { get; private set; }

  /// <summary>
  /// Latest attended-playback snapshot, or null if none has been observed since this process
  /// started.
  /// </summary>
  /// <remarks>
  /// Process-wide, not per circuit, for the reason EncoderConfigStatus is: this store is registered
  /// AddSingleton, and there is one audio engine and one set of speakers, so the state it caches is
  /// global by nature (ADR-029 D6 §8.1). A terminal snapshot is RETAINED here exactly as it is on the
  /// server, so "nothing is playing" is a state rather than the absence of one — a chip that hid on a
  /// null would never show a failure.
  /// </remarks>
  public EventPlaybackSnapshotDto? EventPlayback { get; private set; }

  // 0 or 1. Set the first time a broadcast lands, read by the seed so a response already in flight
  // cannot overwrite something newer. The ENC-12 rule — the pull precedent is MainLayout.razor:388-397
  // and the ORDERING rule this field implements is stated at MainLayout.razor:399-401.
  private int _eventPlaybackBroadcastSeen;

  // 0 or 1, claimed with Interlocked so two circuits opening at once seed exactly once.
  private int _eventPlaybackSeedStarted;

  /// <summary>
  /// Applies one "EventPlaybackChanged" broadcast. Subscribed to the hub client in the constructor.
  /// </summary>
  /// <remarks>
  /// ⚠ internal rather than private, and only for the test seam — Radio.Web.csproj already declares
  /// InternalsVisibleTo("Radio.Web.Tests"). A field-like event can only be raised from inside the
  /// type that declares it, so a test holding an AudioStateHubService cannot make it fire
  /// EventPlaybackChanged; without this, neither the seed's broadcast-wins ordering nor the circuit
  /// backstop's "there is a live playback" precondition could be driven at all, because both are
  /// reachable only through a broadcast having landed. Driving the real handler is also more faithful
  /// than exposing a setter for EventPlayback: the test sets exactly the state a broadcast sets,
  /// including _eventPlaybackBroadcastSeen, rather than a subset a future edit could drift from.
  /// </remarks>
  internal async Task OnHubEventPlaybackChanged(EventPlaybackSnapshotDto? dto)
  {
    EventPlayback = dto;
    Volatile.Write(ref _eventPlaybackBroadcastSeen, 1);
    await NotifyAsync(EventPlaybackChanged);
  }

  /// <summary>
  /// Seeds <see cref="EventPlayback"/> from GET /api/audio/events/current. Runs at most once per
  /// process; every later call returns immediately.
  /// </summary>
  /// <remarks>
  /// ADR-029 §8.1 ⟨A1·4⟩ makes this a requirement rather than a nicety: broadcasts fire on
  /// TRANSITIONS, so a client connecting between two of them would render "nothing is playing" while
  /// the room is talking. A fresh circuit arriving mid-playback is now routine — the user navigated
  /// away and back, the kiosk refreshed, a second browser opened.
  ///
  /// ⚠ A one-shot PULL, not a poll. Trap 5 of the arc breakdown disqualifies a poll outright.
  ///
  /// ⚠ The API client is a PARAMETER rather than a constructor dependency, and that is not style. A
  /// Web singleton cannot inject a typed HttpClient (ADR-022 §6.2, and BellHealthService's class
  /// remark says so in as many words) — holding one for the process lifetime pins a handler that is
  /// meant to rotate. The caller resolves it in a scope and hands it in, so this store stays free of
  /// HTTP.
  ///
  /// ⚠ Ordering, and it is ENC-12's rule rather than a new one: a broadcast that lands while the
  /// pull is in flight describes a LATER moment than the response now in hand, so it wins and the
  /// response is discarded. Seeding from the cache alone was wrong on exactly the boot the seed
  /// exists for — a deploy restarts both services together, so the API can broadcast while
  /// AudioStateHubService.StartAsync is still in its retry loop, and that broadcast reaches nobody.
  ///
  /// ⚠ Never throws. Its callers are a CircuitHandler and, from PR 6, a layout; neither is worth a
  /// blank screen.
  ///
  /// ⚠ KNOWN LIMITATIONS — three, all accepted rather than overlooked, all filed in
  /// design/FUTURE-WORK.md §19 and §21.
  ///
  /// 1. A hub connection that drops and reconnects can miss transitions, and nothing re-seeds. This
  ///    is shared with every other cached broadcast in this store. What bounds the damage is that the
  ///    next transition corrects it, and that GvMedia:MaxPlaybackSeconds bounds how long a missed one
  ///    can matter.
  ///
  /// 2. The broadcast-wins rule above is stated more absolutely than this code enforces it. The guard
  ///    is an unlocked read of _eventPlaybackBroadcastSeen followed by an assignment to EventPlayback,
  ///    with no lock spanning the two — so a broadcast landing between them is overwritten by the
  ///    older seed response after all. The window is a few instructions wide and self-corrects on the
  ///    next transition; closing it means a lock across an assignment this store otherwise takes none
  ///    for.
  ///
  /// 3. The one shot is BURNED BEFORE THE HTTP CALL, not after a successful one. Interlocked.Exchange
  ///    runs at the top of this method, so if GetCurrentAsync throws — which is exactly what a deploy
  ///    restarting both services produces, the API still booting while a circuit opens — the catch
  ///    swallows it and _eventPlaybackSeedStarted stays 1. No later circuit can ever seed, and the
  ///    store is unseeded for the life of the process. That is the co-restart this seed exists for,
  ///    so this is the sharpest of the three; it is left alone here only because moving the claim
  ///    after a successful response also lets N concurrent circuits issue N pulls, and choosing
  ///    between those is a design call rather than a fix.
  /// </remarks>
  public async Task EnsureEventPlaybackSeededAsync(
    EventPlaybackApiService api, CancellationToken cancellationToken = default)
  {
    if (Interlocked.Exchange(ref _eventPlaybackSeedStarted, 1) != 0)
    {
      return;
    }

    try
    {
      var snapshot = await api.GetCurrentAsync(cancellationToken);

      if (Volatile.Read(ref _eventPlaybackBroadcastSeen) != 0)
      {
        return;
      }

      if (snapshot is not null)
      {
        EventPlayback = snapshot;
        await NotifyAsync(EventPlaybackChanged);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error seeding attended playback state");
    }
  }

  private async Task NotifyAsync(Func<Task>? handler)
  {
    if (handler != null)
    {
      try
      {
        await handler.Invoke();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error notifying AudioStateStore subscriber");
      }
    }
  }

  public async ValueTask DisposeAsync()
  {
    _hubService.PlaybackStateChanged -= OnHubPlaybackStateChanged;
    _hubService.NowPlayingChanged -= OnHubNowPlayingChanged;
    _hubService.VolumeChanged -= OnHubVolumeChanged;
    _hubService.SourceChanged -= OnHubSourceChanged;
    _hubService.QueueChanged -= OnHubQueueChanged;
    _hubService.FingerprintStatusChanged -= OnHubFingerprintStatusChanged;
    _hubService.RadioStateChanged -= OnHubRadioStateChanged;
    _hubService.SleepStateChanged -= OnHubSleepStateChanged;
    _hubService.EncoderConnectionChanged -= OnHubEncoderConnectionChanged;
    _hubService.EncoderConfigStatusChanged -= OnHubEncoderConfigStatusChanged;
    _hubService.EventPlaybackChanged -= OnHubEventPlaybackChanged;

    await Task.CompletedTask;
    GC.SuppressFinalize(this);
  }
}
