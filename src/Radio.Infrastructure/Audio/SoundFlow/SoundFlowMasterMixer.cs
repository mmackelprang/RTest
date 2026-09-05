using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// SoundFlow implementation of the master audio mixer.
/// Combines all audio sources into a single output stream.
/// </summary>
public class SoundFlowMasterMixer : IMasterMixer
{
  private readonly ILogger<SoundFlowMasterMixer> _logger;
  private readonly List<IAudioSource> _sources = [];
  private readonly object _sourcesLock = new();

  private float _masterVolume = 0.75f;
  private float _balance;
  private bool _isMuted;

  /// <summary>
  /// Event fired when master volume changes.
  /// </summary>
  public event EventHandler<float>? MasterVolumeChanged;

  /// <summary>
  /// Event fired when balance changes.
  /// </summary>
  public event EventHandler<float>? BalanceChanged;

  /// <summary>
  /// Event fired when mute state changes.
  /// </summary>
  public event EventHandler<bool>? MuteStateChanged;

  /// <summary>
  /// Initializes a new instance of the <see cref="SoundFlowMasterMixer"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  public SoundFlowMasterMixer(ILogger<SoundFlowMasterMixer> logger)
  {
    _logger = logger;
  }

  /// <inheritdoc/>
  public float MasterVolume
  {
    get => _masterVolume;
    set
    {
      var clampedValue = Math.Clamp(value, 0f, 1f);
      if (Math.Abs(_masterVolume - clampedValue) > float.Epsilon)
      {
        _masterVolume = clampedValue;
        _logger.LogDebug("Master volume set to {Volume:P0}", clampedValue);
        MasterVolumeChanged?.Invoke(this, _masterVolume);
      }
    }
  }

  /// <inheritdoc/>
  public float Balance
  {
    get => _balance;
    set
    {
      var clampedValue = Math.Clamp(value, -1f, 1f);
      if (Math.Abs(_balance - clampedValue) > float.Epsilon)
      {
        _balance = clampedValue;
        _logger.LogDebug("Balance set to {Balance:F2}", clampedValue);
        BalanceChanged?.Invoke(this, _balance);
      }
    }
  }

  /// <inheritdoc/>
  public bool IsMuted
  {
    get => _isMuted;
    set
    {
      if (_isMuted != value)
      {
        _isMuted = value;
        _logger.LogDebug("Mute state set to {IsMuted}", value);
        MuteStateChanged?.Invoke(this, _isMuted);
      }
    }
  }

  /// <inheritdoc/>
  public void AddSource(IAudioSource source)
  {
    ArgumentNullException.ThrowIfNull(source);

    lock (_sourcesLock)
    {
      if (!_sources.Contains(source))
      {
        _sources.Add(source);
        // Type, not Name: this is domain-agnostic bookkeeping, and TTSEventSource.Name embeds
        // the utterance text (TTS-11). Id was already here and identifies WHICH source the line is
        // about without naming it.
        //
        // ⚠ Be honest about what this trade cost, because the plan understated it. Type is
        // REDUNDANT on this line — AudioSourceBase builds Id as $"{Type}-{Guid:N}", so {SourceType}
        // is already a prefix of {SourceId}. It is kept for readability, not information. And Name
        // was NOT redundant here: RadioAudioSource and SDRRadioAudioSource both return
        // AudioSourceType.Radio while their names differ ("Radio (RF320)" vs
        // "SDR Radio (RTL-SDR)"), so this line no longer distinguishes the two radio backends.
        // That discrimination is still available one layer up, from
        // AudioManager.SwitchSourceAsync's "Adding new source {SourceName} to mixer", which logs
        // Name on the primary path and is deliberately untouched by TTS-11 (every primary
        // implementation returns a constant Name, so there is nothing to leak there).
        //
        // ⚠ Id does NOT join this line to AudioManager's ducking lines, and an earlier revision of
        // this comment claimed it did. NO path emits both for the same source: the two services
        // that duck (AnnouncementService, EventPlaybackService) never call AddSource, and the one
        // route that adds a TTS source here — SourcesController.PlayTTSEvent — does not duck.
        _logger.LogInformation(
          "Added audio source {SourceId} ({SourceType}) to mixer",
          source.Id, source.Type);
      }
    }
  }

  /// <inheritdoc/>
  public void RemoveSource(IAudioSource source)
  {
    ArgumentNullException.ThrowIfNull(source);

    lock (_sourcesLock)
    {
      if (_sources.Remove(source))
      {
        _logger.LogInformation(
          "Removed audio source {SourceId} ({SourceType}) from mixer",
          source.Id, source.Type);
      }
    }
  }

  /// <inheritdoc/>
  public IReadOnlyList<IAudioSource> GetActiveSources()
  {
    lock (_sourcesLock)
    {
      return _sources.ToList().AsReadOnly();
    }
  }

  /// <summary>
  /// Clears all sources from the mixer.
  /// </summary>
  public void ClearSources()
  {
    lock (_sourcesLock)
    {
      _sources.Clear();
      _logger.LogInformation("Cleared all sources from mixer");
    }
  }

  /// <summary>
  /// Gets the effective volume after applying mute state.
  /// </summary>
  /// <returns>The effective volume (0 if muted).</returns>
  public float GetEffectiveVolume() => _isMuted ? 0f : _masterVolume;

  /// <summary>
  /// Calculates the left channel gain based on balance.
  /// </summary>
  /// <returns>The left channel gain (0.0 to 1.0).</returns>
  public float GetLeftChannelGain()
  {
    // When balance is positive (right), reduce left channel
    return _balance > 0 ? 1f - _balance : 1f;
  }

  /// <summary>
  /// Calculates the right channel gain based on balance.
  /// </summary>
  /// <returns>The right channel gain (0.0 to 1.0).</returns>
  public float GetRightChannelGain()
  {
    // When balance is negative (left), reduce right channel
    return _balance < 0 ? 1f + _balance : 1f;
  }
}
