using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Makes the console's volume and mute drive a connected Cast speaker (AUD-81).
/// </summary>
/// <remarks>
/// <para><b>What it listens to.</b> The master mixer's <c>MasterVolumeChanged</c> and
/// <c>MuteStateChanged</c> — the single point every console volume path passes through (the UI
/// slider via <c>AudioController</c>, the rotary encoder, the sleep timer, the preference restore,
/// and the Cast external-change re-sync).</para>
/// <para><b>When it acts.</b> Only while the console's active output is <c>google-cast</c> AND the
/// Cast output is <c>Streaming</c> with a published connection
/// (<see cref="GoogleCastOutput.GetConsoleVolumeTarget"/>). Otherwise it only tracks the master
/// level. Master volume keeps its meaning as the console's one volume number; the local speakers'
/// mute while casting is the output gate's business and is not touched here.</para>
/// <para><b>Mapping.</b> <see cref="CastConsoleVolumeCurve"/>, anchored lazily per connection at
/// (the console level just before the first change on that connection, the speaker's level then).
/// A new connection re-anchors.</para>
/// <para><b>Loop safety.</b> When the new master level equals the speaker's known level (within
/// <see cref="SameLevelTolerance"/>)
/// nothing is pushed and the anchor becomes the identity. An external speaker change reaches here
/// as a master write equal to the speaker level (GoogleCastOutput records the level before raising
/// the event, and AudioStateUpdateService copies it to master), so it is a no-op whatever order the
/// subscribers run in. The device's confirmations of our own pushes are recognised by
/// GoogleCastOutput's recent-push memory and never become master writes.</para>
/// <para>Lives in <c>Radio.Infrastructure.Audio.Services</c> rather than beside the Cast output
/// because <c>Radio.Infrastructure.Audio</c> is held at Warning in the shipped log configuration
/// (LOG-2) while this namespace is not: its Information lines are the file-sink record a box UAT
/// reads.</para>
/// </remarks>
public sealed class CastConsoleVolumeFollower : IDisposable
{
  /// <summary>The active-output id of the Cast output, as the output gate records it.</summary>
  public const string CastOutputId = "google-cast";

  /// <summary>
  /// How close the master level must be to the speaker's known level to count as "already
  /// there" (no push, re-anchor to the identity).
  /// </summary>
  /// <remarks>
  /// Deliberately much tighter than the 0.01 the echo filter uses. The case it exists for is the
  /// external re-sync, where AudioStateUpdateService writes master volume to EXACTLY the level
  /// GoogleCastOutput has just recorded — the same float. A 0.01 tolerance would also swallow a
  /// genuine console move of one point, which is one rotary-encoder detent at the default
  /// <c>VolumeStepPercent = 1</c>: once the curve is the identity, every other single detent
  /// would never reach the speaker.
  /// </remarks>
  public const float SameLevelTolerance = 0.001f;

  private readonly ILogger<CastConsoleVolumeFollower> _logger;
  private readonly SoundFlowMasterMixer _mixer;
  private readonly IAudioEngine _engine;
  private readonly GoogleCastOutput _cast;

  // Guards everything below. Event handlers run on whichever thread set the mixer, and two can
  // overlap (UI request + encoder). Held while calling into GoogleCastOutput, whose console
  // methods only take its own short locks and queue the network work.
  private readonly object _lock = new();
  private float _lastMaster;
  private Anchor? _anchor;
  private Task<CastConsoleVolumeResult>? _watchedBurst;
  private bool _disposed;

  private readonly record struct Anchor(int Generation, float Console, float Speaker);

  /// <summary>
  /// <b>Test seam (kind B — observation).</b> The logging continuation of the most recent volume
  /// burst, so a test can await it instead of sleeping. Read by <c>CastConsoleVolumeFollowerTests</c>;
  /// nothing in production reads it.
  /// </summary>
  internal Task LastVolumeBurst { get; private set; } = Task.CompletedTask;

  /// <summary>
  /// <b>Test seam (kind B — observation).</b> The most recent console mute push. Read by
  /// <c>CastConsoleVolumeFollowerTests</c>; nothing in production reads it.
  /// </summary>
  internal Task LastMutePush { get; private set; } = Task.CompletedTask;

  /// <summary>Subscribes to the master mixer and wires itself into the Cast output.</summary>
  public CastConsoleVolumeFollower(
    ILogger<CastConsoleVolumeFollower> logger,
    SoundFlowMasterMixer mixer,
    IAudioEngine engine,
    GoogleCastOutput castOutput)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));
    _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    _cast = castOutput ?? throw new ArgumentNullException(nameof(castOutput));

    _lastMaster = _mixer.MasterVolume;
    _cast.AttachConsoleFollower(() => _mixer.IsMuted, _logger);
    _mixer.MasterVolumeChanged += OnMasterVolumeChanged;
    _mixer.MuteStateChanged += OnMuteStateChanged;
  }

  /// <summary>
  /// The Cast connection the console drives right now, or null when the active output is not
  /// Cast or the Cast output is not streaming to a published connection.
  /// </summary>
  private CastConsoleTarget? CastingTarget()
  {
    if (!string.Equals(_engine.ActiveOutputId, CastOutputId, StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    return _cast.GetConsoleVolumeTarget();
  }

  private void OnMasterVolumeChanged(object? sender, float volume)
  {
    try
    {
      lock (_lock)
      {
        var previous = _lastMaster;
        _lastMaster = volume;
        if (_disposed)
        {
          return;
        }

        if (CastingTarget() is not { } target)
        {
          // Not casting: startup restore, switching outputs, the AUD-84 fallback, or plain
          // local listening. The next connection anchors afresh.
          _anchor = null;
          return;
        }

        if (!float.IsNaN(target.SpeakerLevel) && Math.Abs(volume - target.SpeakerLevel) <= SameLevelTolerance)
        {
          // Master already equals the speaker — an external change re-synced to the console,
          // or the slider crossing the speaker's level. Nothing to send; from here the curve
          // is the identity.
          _anchor = new Anchor(target.Generation, volume, volume);
          return;
        }

        if (_anchor is not { } anchor || anchor.Generation != target.Generation)
        {
          anchor = new Anchor(target.Generation, previous, target.SpeakerLevel);
          _anchor = anchor;
        }

        var speaker = CastConsoleVolumeCurve.Map(volume, anchor.Console, anchor.Speaker);
        var burst = _cast.SetDeviceVolumeFromConsoleAsync(speaker, target.Generation, volume);
        if (!ReferenceEquals(burst, _watchedBurst))
        {
          _watchedBurst = burst;
          LastVolumeBurst = LogBurstAsync(burst);
        }
      }
    }
    catch (Exception ex)
    {
      // Never into the mixer's setter: a volume change must not fail because of Cast.
      _logger.LogWarning(ex, "Cast: could not apply the console volume to the speaker");
    }
  }

  private async Task LogBurstAsync(Task<CastConsoleVolumeResult> burst)
  {
    try
    {
      var result = await burst.ConfigureAwait(false);
      if (result.AppliedLevel is float applied)
      {
        _logger.LogInformation(
          "Cast: console volume {Console:P0} → speaker {Speaker:P0} on {Name}",
          result.AppliedConsoleLevel, applied, result.DeviceName);
      }
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Cast: console volume burst did not complete");
    }
  }

  private void OnMuteStateChanged(object? sender, bool muted)
  {
    try
    {
      lock (_lock)
      {
        if (_disposed || CastingTarget() is not { } target)
        {
          return;
        }

        LastMutePush = PushMuteAsync(muted, target);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Cast: could not apply the console mute to the speaker");
    }
  }

  private async Task PushMuteAsync(bool muted, CastConsoleTarget target)
  {
    try
    {
      if (await _cast.SetDeviceMuteFromConsoleAsync(muted, target.Generation).ConfigureAwait(false))
      {
        _logger.LogInformation(
          muted ? "Cast: console muted → speaker {Name} muted" : "Cast: console unmuted → speaker {Name} unmuted",
          target.DeviceName);
      }
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Cast: console mute push did not complete");
    }
  }

  /// <inheritdoc />
  public void Dispose()
  {
    lock (_lock)
    {
      if (_disposed)
      {
        return;
      }

      _disposed = true;
    }

    _mixer.MasterVolumeChanged -= OnMasterVolumeChanged;
    _mixer.MuteStateChanged -= OnMuteStateChanged;
  }
}
