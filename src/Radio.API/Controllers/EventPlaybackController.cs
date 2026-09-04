using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Radio.API.Models;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.External;

namespace Radio.API.Controllers;

/// <summary>
/// Attended event playback — voicemail recordings and spoken messages (ADR-029 §3.3).
/// </summary>
/// <remarks>
/// One mechanism, not two features: both arms share this route family, one lifecycle, one stop path
/// and one state model, differing only in how the audio is acquired.
///
/// ⚠ POST returns 202, not 200. Both arms have an acquisition phase — an HTTP fetch or a TTS
/// synthesis — before any audio exists, so the response describes an ACCEPTED playback in Preparing.
/// Acquisition failures therefore arrive as EventPlaybackState.Failed with a named FailureReason on
/// a later snapshot, NOT as a status code on this response; GET current is how a caller that cares
/// reads them (and from PR 5, /hubs/audio pushes them). The one exception is GvMedia:Enabled being
/// false, which is knowable without touching the network and is answered here as a 409.
///
/// ⚠ A new controller rather than a method on AudioController. That one is already large with seven
/// constructor parameters, and adding a REQUIRED constructor dependency to a controller that several
/// tests construct directly would break them.
/// </remarks>
[ApiController]
[Route("api/audio/events")]
[Produces("application/json")]
public class EventPlaybackController : ControllerBase
{
  private readonly ILogger<EventPlaybackController> _logger;
  private readonly IEventPlaybackService _playback;
  private readonly IOptionsMonitor<GvMediaOptions> _gvMediaOptions;

  /// <summary>Initializes a new instance of the EventPlaybackController.</summary>
  /// <param name="logger">The logger.</param>
  /// <param name="playback">The attended-playback seam.</param>
  /// <param name="gvMediaOptions">
  /// Read for GvMedia:MaxSpeechChars, which is the cap Validate applies to the utterance.
  /// </param>
  public EventPlaybackController(
    ILogger<EventPlaybackController> logger,
    IEventPlaybackService playback,
    IOptionsMonitor<GvMediaOptions> gvMediaOptions)
  {
    _logger = logger;
    _playback = playback;
    _gvMediaOptions = gvMediaOptions;
  }

  /// <summary>Starts an attended playback.</summary>
  /// <param name="dto">The request body.</param>
  /// <param name="cancellationToken">
  /// The request's own token. Used for the SYNCHRONOUS part only — acquisition deliberately outlives
  /// this response and runs on a token the service owns.
  /// </param>
  /// <returns>202 with the accepted snapshot, 400 with a named reason, or 409.</returns>
  [HttpPost]
  [ProducesResponseType(typeof(EventPlaybackSnapshot), StatusCodes.Status202Accepted)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<EventPlaybackSnapshot>> Start(
    [FromBody] EventPlaybackRequestDto dto, CancellationToken cancellationToken)
  {
    var request = Map(dto);

    // The controller validates so a bad request gets a clean 400 without an exception; the seam
    // validates too, for callers that are not this one. Both call Validate — the rules live there
    // and neither re-derives them.
    //
    // ⚠ MaxSpeechChars comes from configuration, not from Validate's parameterless default. That
    // default exists so tests need no configuration object; using it here would mean a change to
    // GvMedia:MaxSpeechChars silently did nothing at the only place a user can reach it.
    var rejection = request.Validate(_gvMediaOptions.CurrentValue.MaxSpeechChars);
    if (rejection != EventPlaybackRejection.None)
    {
      // ⚠ The reason NAME only. Echoing MediaId or Text back would put a raw media id, or a private
      // message body, into a response and into whatever logs it.
      _logger.LogDebug("Event playback request refused: {Reason}", rejection);
      return BadRequest(new { error = "Event playback request refused", reason = rejection.ToString() });
    }

    try
    {
      var snapshot = await _playback.StartAsync(request, cancellationToken);
      return Accepted(snapshot);
    }
    catch (EventPlaybackRejectedException ex)
    {
      // Unreachable while the check above matches, and kept anyway: the seam is the authority, and a
      // controller that silently disagreed with it would be worse than a duplicated 400.
      return BadRequest(new { error = "Event playback request refused", reason = ex.Reason.ToString() });
    }
    catch (GvMediaUnavailableException ex) when (ex.Reason == GvMediaFailure.Disabled)
    {
      return Conflict(new
      {
        error = "Remote media playback is disabled; set GvMedia:Enabled.",
        reason = ex.Reason.ToString()
      });
    }
  }

  /// <summary>The one attended playback, or 204 when nothing has been started.</summary>
  /// <remarks>
  /// ⚠ This does NOT go back to 204 when a playback ends. The last snapshot is retained until a new
  /// playback replaces it, because POST answered 202 before any audio existed and this is the only
  /// surface an acquisition failure can be read from (ADR-029 §8.1's re-attach path). Read the
  /// snapshot's state, not the status code, to know whether audio is being produced.
  /// </remarks>
  /// <returns>200 with the snapshot, or 204.</returns>
  [HttpGet("current")]
  [ProducesResponseType(typeof(EventPlaybackSnapshot), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  public ActionResult<EventPlaybackSnapshot> GetCurrent()
  {
    var snapshot = _playback.Current;
    return snapshot is null ? NoContent() : Ok(snapshot);
  }

  /// <summary>Stops the playback with this id.</summary>
  /// <param name="id">The server-minted playback id.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>204, or 404 when no such playback is in flight.</returns>
  [HttpDelete("{id}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Stop(string id, CancellationToken cancellationToken)
    => await _playback.StopAsync(id, cancellationToken)
      ? NoContent()
      : NotFound(new { error = "No such playback", reason = "UnknownPlaybackId" });

  /// <summary>Seeks the playback with this id.</summary>
  /// <param name="id">The server-minted playback id.</param>
  /// <param name="dto">The target position.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>200 with the snapshot, 400, 404 or 409.</returns>
  [HttpPost("{id}/seek")]
  [ProducesResponseType(typeof(EventPlaybackSnapshot), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<EventPlaybackSnapshot>> Seek(
    string id, [FromBody] EventPlaybackSeekDto dto, CancellationToken cancellationToken)
  {
    // Range-checked before TimeSpan.FromSeconds, which throws on NaN and on infinity.
    if (dto.PositionSeconds < 0 || double.IsNaN(dto.PositionSeconds)
        || double.IsInfinity(dto.PositionSeconds))
    {
      return BadRequest(new
      {
        error = "positionSeconds must be a finite, non-negative number", reason = "BadPosition"
      });
    }

    var moved = await _playback.SeekAsync(
      id, TimeSpan.FromSeconds(dto.PositionSeconds), cancellationToken);
    return Transport(id, moved, "NotSeekable");
  }

  /// <summary>Pauses the playback with this id.</summary>
  /// <param name="id">The server-minted playback id.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>200 with the snapshot, 404 or 409.</returns>
  [HttpPost("{id}/pause")]
  [ProducesResponseType(typeof(EventPlaybackSnapshot), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<EventPlaybackSnapshot>> Pause(
    string id, CancellationToken cancellationToken)
    => Transport(id, await _playback.PauseAsync(id, cancellationToken), "NotPlaying");

  /// <summary>Resumes the playback with this id.</summary>
  /// <param name="id">The server-minted playback id.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>200 with the snapshot, 404 or 409.</returns>
  [HttpPost("{id}/resume")]
  [ProducesResponseType(typeof(EventPlaybackSnapshot), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<EventPlaybackSnapshot>> Resume(
    string id, CancellationToken cancellationToken)
    => Transport(id, await _playback.ResumeAsync(id, cancellationToken), "NotPaused");

  /// <summary>
  /// Turns a transport method's single bool into 200 / 404 / 409.
  /// </summary>
  /// <remarks>
  /// The seam returns one bool for two different situations — no such playback, and a playback that
  /// cannot do this — because IEventPlaybackService says so. Current is what separates them, and it
  /// is a shipped seam member rather than a widening of the contract.
  ///
  /// ⚠ Because Current RETAINS the last snapshot after a playback ends, a transport call against an
  /// id that has just completed or failed answers 409, not 404 — the id is still the one Current
  /// describes, it simply cannot pause or scrub any more. That is the honest answer of the two:
  /// 404 would say the caller invented the id, when in fact the UI is holding a handle that was
  /// real a moment ago. 404 is reserved for an id Current has never described.
  ///
  /// ⚠ Honest about the race, because a reviewer will find it: Current is read AFTER the call, so a
  /// playback replaced in between reports 404 rather than 409. Both are refusals, the window is
  /// microseconds, and there is one user in front of one console — the alternative is a lock across
  /// an HTTP handler, which is worse than the imprecision it would buy.
  /// </remarks>
  private ActionResult<EventPlaybackSnapshot> Transport(
    string id, bool succeeded, string refusalReason)
  {
    var current = _playback.Current;
    if (succeeded)
    {
      return current is null ? NoContent() : Ok(current);
    }
    return current is null || current.Id != id
      ? NotFound(new { error = "No such playback", reason = "UnknownPlaybackId" })
      : Conflict(new { error = "The playback cannot do that right now", reason = refusalReason });
  }

  /// <summary>
  /// Translates the wire shape into the Core request. Decides nothing.
  /// </summary>
  /// <remarks>
  /// An absent or unrecognised enum name becomes an UNDEFINED enum value rather than a
  /// controller-side error, so Validate reports it under its own rules — UnknownKind,
  /// UnknownMediaKind on the RemoteMedia arm, and ArmMismatch on the Speech arm, which is where an
  /// unparseable mediaKind on a speech request genuinely belongs.
  ///
  /// Priority is applied with `with` only when the caller sent one, so the default stays a single
  /// constant on EventPlaybackRequest rather than being repeated here.
  /// </remarks>
  private static EventPlaybackRequest Map(EventPlaybackRequestDto dto)
  {
    const int Undefined = -1;

    var kind = Enum.TryParse<EventPlaybackKind>(dto.Kind, ignoreCase: true, out var k)
      && Enum.IsDefined(k) ? k : (EventPlaybackKind)Undefined;

    RemoteMediaKind? mediaKind = dto.MediaKind is null
      ? null
      : Enum.TryParse<RemoteMediaKind>(dto.MediaKind, ignoreCase: true, out var mk) && Enum.IsDefined(mk)
        ? mk
        : (RemoteMediaKind)Undefined;

    var request = new EventPlaybackRequest
    {
      Kind = kind,
      Text = dto.Text,
      VoiceId = dto.VoiceId,
      Engine = dto.Engine,
      MediaKind = mediaKind,
      MediaId = dto.MediaId,
      DurationSeconds = dto.DurationSeconds,
      Label = dto.Label
    };

    return dto.Priority is int priority ? request with { Priority = priority } : request;
  }
}
