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
/// and the Cast external-change re-sync) — plus the Cast output's own <c>CastVolumeChanged</c>
/// and the engine's <c>ActiveOutputChanged</c>.</para>
/// <para><b>When it acts.</b> The console's VOLUME, and an UNmute, reach the speaker only while the
/// console's active output is <c>google-cast</c> AND the Cast output is <c>Streaming</c> with a
/// published connection (<see cref="GoogleCastOutput.GetConsoleVolumeTarget"/>). A console MUTE
/// reaches it whenever the Cast output is streaming to a published connection, whatever the
/// active output says (pre-merge review M3): the output gate records <c>google-cast</c> only
/// after <c>StartAsync</c> returns, and a mute made in that window must not be lost — muting is
/// the safe direction. When the gate does make <c>google-cast</c> active, the console's mute is
/// reconciled onto the speaker once. Master volume keeps its meaning as the console's one volume
/// number; the local speakers' mute while casting is the output gate's business.</para>
/// <para><b>Mapping.</b> <see cref="CastConsoleVolumeCurve"/>, anchored lazily per connection at
/// (the console level just before the first change on that connection, the speaker's level then).
/// A new connection re-anchors, and so does every change reported by the speaker. So does a console
/// move UP that starts below the anchor's console level when the anchor's lower segment is steeper
/// than <see cref="CastConsoleVolumeCurve.MaxUpperSlope"/> (hostile review F2): it re-anchors at
/// (the console level it starts from, the level the curve gave that), so that move is mapped by an
/// upper segment, whose slope is capped. Moves down are mapped by the current anchor unchanged. The
/// price is hysteresis on such a connection: after moving down and back up, the speaker does NOT
/// return to the original speaker level at the original console level — it is lower, because the
/// way back up is less steep than the way down was. A change made on the speaker, or a new
/// connection, re-anchors from the speaker's real level.</para>
/// <para><b>Loop safety rests on an explicit marker, not on float equality</b> (pre-merge review
/// M1). <c>AudioStateUpdateService</c> copies a change made on the speaker into master volume and
/// mute synchronously inside <c>GoogleCastOutput.CastVolumeChanged</c>, and the Cast output
/// reports <see cref="GoogleCastOutput.SpeakerChangeBeingApplied"/> on that thread for exactly
/// that long. A mixer change seen under it is the speaker's own: the follower re-anchors to (new
/// master, the speaker's reported level) and pushes nothing. The device's confirmations of our own
/// pushes never get that far — GoogleCastOutput's recent-push memory recognises them. The
/// "master already equals the speaker" check (<see cref="SameLevelTolerance"/>) remains, but only
/// as a courtesy for a slider crossing the speaker's level; nothing depends on it.</para>
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
  /// Deliberately much tighter than the 0.01 the echo filter uses: a 0.01 tolerance would also
  /// swallow a genuine console move of one point, which is one rotary-encoder detent at the
  /// default <c>VolumeStepPercent = 1</c> — once the curve is the identity, every other single
  /// detent would never reach the speaker. Not what makes the external re-sync loop-free; that is
  /// <see cref="GoogleCastOutput.SpeakerChangeBeingApplied"/>.
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

  /// <summary>Subscribes to the master mixer, the engine and the Cast output.</summary>
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
    _cast.CastVolumeChanged += OnCastVolumeChanged;
    _engine.ActiveOutputChanged += OnActiveOutputChanged;
  }

  private bool CastIsTheActiveOutput =>
    string.Equals(_engine.ActiveOutputId, CastOutputId, StringComparison.OrdinalIgnoreCase);

  /// <summary>
  /// The Cast connection the console's volume drives right now, or null when the active output is
  /// not Cast or the Cast output is not streaming to a published connection.
  /// </summary>
  private CastConsoleTarget? CastingTarget() => CastIsTheActiveOutput ? _cast.GetConsoleVolumeTarget() : null;

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

        if (_cast.SpeakerChangeBeingApplied is float speakerLevel)
        {
          // The speaker's own change, copied to master inside CastVolumeChanged. Never pushed
          // back: from here the curve runs through (this master level, the speaker's level).
          _anchor = CastingTarget() is { } synced ? new Anchor(synced.Generation, volume, speakerLevel) : null;
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
          // The slider has reached the speaker's level. Nothing to send; from here the curve
          // is the identity.
          _anchor = new Anchor(target.Generation, volume, volume);
          return;
        }

        if (_anchor is not { } anchor || anchor.Generation != target.Generation)
        {
          anchor = new Anchor(target.Generation, previous, target.SpeakerLevel);
          _anchor = anchor;
        }
        else if (volume > previous && previous < anchor.Console && IsSteeperThanTheUpperCap(anchor))
        {
          // Hostile review F2. Moving UP from a lower segment steeper than MaxUpperSlope (its
          // slope is s0/m0 — 8 for an anchor at (0.10, 0.80)), which used to apply upward too:
          // one detent up after a move down raised the speaker 8 points. Re-anchor at the current
          // point — the previous console level and the level the curve gave it — so the move is
          // mapped by the new anchor's upper segment, whose slope is capped at MaxUpperSlope.
          // Continuous, and console 0 stays silent (the new anchor's lower segment still runs to
          // (0, 0)) — with one exception: when the previous level is at or below
          // CastConsoleVolumeCurve.LowAnchorLimit the new anchor maps with the identity, so the
          // speaker goes to the console's level, which from the bottom of a lower segment this
          // steep is a step up smaller than the console's own move, or a step DOWN (the safe
          // direction). A lower segment no steeper than the cap is left alone: moving up along
          // it already rises at most MaxUpperSlope times the step, and re-anchoring it would
          // only add hysteresis.
          anchor = new Anchor(
            target.Generation, previous, CastConsoleVolumeCurve.Map(previous, anchor.Console, anchor.Speaker));
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

  /// <summary>
  /// True when <paramref name="anchor"/>'s lower segment (slope s0/m0) is steeper than
  /// <see cref="CastConsoleVolumeCurve.MaxUpperSlope"/>. False for an anchor the curve maps with
  /// the identity (unknown speaker level, or a console anchor at or below
  /// <see cref="CastConsoleVolumeCurve.LowAnchorLimit"/>), which has no lower segment.
  /// </summary>
  private static bool IsSteeperThanTheUpperCap(Anchor anchor) =>
    anchor.Console > CastConsoleVolumeCurve.LowAnchorLimit
    && !float.IsNaN(anchor.Speaker)
    && anchor.Speaker > CastConsoleVolumeCurve.MaxUpperSlope * anchor.Console;

  /// <summary>
  /// A change reported by the speaker: re-anchor at (current master, the speaker's level), so
  /// the next console move continues from where the speaker now is. Covers the case the marker
  /// cannot — a change too small for <c>AudioStateUpdateService</c> to write master at all.
  /// Whichever of this and the master write runs first, the anchor ends at (master after the
  /// event, speaker's level).
  /// </summary>
  private void OnCastVolumeChanged(object? sender, CastVolumeChangedEventArgs e)
  {
    if (e.IsInitialSync)
    {
      return;
    }

    try
    {
      lock (_lock)
      {
        if (_disposed)
        {
          return;
        }

        _anchor = CastingTarget() is { } target ? new Anchor(target.Generation, _mixer.MasterVolume, e.Volume) : null;
      }
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Cast: could not re-anchor the console volume after a speaker change");
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
        if (_disposed || _cast.SpeakerChangeBeingApplied != null)
        {
          // The second: the speaker's own mute, copied to the console. Already true there.
          return;
        }

        // A mute goes to any Cast connection that is streaming (M3: the safe direction, and the
        // gate marks google-cast active only after the stream has started); an unmute only while
        // Cast is the active output.
        if (_cast.GetConsoleVolumeTarget() is not { } target || (!muted && !CastIsTheActiveOutput))
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

  /// <summary>
  /// The output gate has made an output active. When it is Cast, reconcile the console's mute
  /// onto the speaker once (pre-merge review M3): a console mute made while the stream was
  /// starting is applied, and a speaker this application muted for a console that has since
  /// been unmuted is unmuted.
  /// </summary>
  private void OnActiveOutputChanged(object? sender, string outputId)
  {
    if (!string.Equals(outputId, CastOutputId, StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    try
    {
      lock (_lock)
      {
        if (_disposed || _cast.GetConsoleVolumeTarget() is not { } target)
        {
          return;
        }

        var consoleMuted = _mixer.IsMuted;
        if (consoleMuted && !target.SpeakerMuted)
        {
          LastMutePush = PushMuteAsync(true, target);
        }
        else if (!consoleMuted && target.SpeakerMuted && _cast.IsSpeakerMutedByConsole)
        {
          LastMutePush = PushMuteAsync(false, target);
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Cast: could not reconcile the console mute with the speaker");
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
    _cast.CastVolumeChanged -= OnCastVolumeChanged;
    _engine.ActiveOutputChanged -= OnActiveOutputChanged;
  }
}
