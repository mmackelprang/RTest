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
        _logger.LogInformation(
          "Added audio source {SourceId} ({SourceName}) to mixer",
          source.Id, source.Name);
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
          "Removed audio source {SourceId} ({SourceName}) from mixer",
          source.Id, source.Name);
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
