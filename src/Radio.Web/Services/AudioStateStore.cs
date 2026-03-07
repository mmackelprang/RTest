using Microsoft.Extensions.Logging;
using Radio.Web.Models;
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

  /// <summary>Raised when radio state changes.</summary>
  public event Func<Task>? RadioStateChanged;

  /// <summary>Raised when sleep state changes.</summary>
  public event Func<bool, Task>? SleepStateChanged;

  /// <summary>Raised when encoder connection state changes.</summary>
  public event Func<Task>? EncoderConnectionChanged;

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

  private async Task OnHubRadioStateChanged()
  {
    await NotifyAsync(RadioStateChanged);
  }

  private async Task OnHubSleepStateChanged(bool isSleeping)
  {
    if (SleepStateChanged != null)
    {
      await SleepStateChanged.Invoke(isSleeping);
    }
  }

  private async Task OnHubEncoderConnectionChanged()
  {
    await NotifyAsync(EncoderConnectionChanged);
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

    await Task.CompletedTask;
    GC.SuppressFinalize(this);
  }
}
