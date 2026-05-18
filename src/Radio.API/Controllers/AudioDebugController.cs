using Microsoft.AspNetCore.Mvc;

namespace Radio.API.Controllers;

/// <summary>
/// Debug-only audio endpoints surfaced to the DevTray. Today the only
/// route is a stubbed audio-frame dump that returns 501 with a clear
/// message — the SoundFlow output pipeline does not yet expose a way to
/// retain the last N seconds of mixed PCM in a re-readable buffer (PR D
/// #23 of the Arc follow-up backlog).
/// <para>
/// When the underlying buffer becomes available, replace the stub with
/// a real WAV serializer: grab the last 5 s from the tap, prefix a WAV
/// header, return as <c>application/octet-stream</c>. The DevTray opens
/// the URL in a new tab, so any 200/file download will trigger the
/// browser's save-as dialog.
/// </para>
/// <para>
/// <b>Authorization:</b> no auth policy exists in this project today
/// (no <c>[Authorize]</c> usages anywhere in <c>Radio.API</c>). Until a
/// kiosk-auth policy is wired the endpoint is reachable from the local
/// network. For the stub this is acceptable — the payload is a 501
/// description, not real audio. When the real endpoint lands the
/// follow-up PR must add an authorization gate.
/// </para>
/// </summary>
[ApiController]
[Route("api/audio/debug")]
[Produces("application/json")]
public class AudioDebugController : ControllerBase
{
  private readonly ILogger<AudioDebugController> _logger;

  public AudioDebugController(ILogger<AudioDebugController> logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// Dumps the last 5 seconds of mixed audio as a WAV download. Currently
  /// returns <c>501 Not Implemented</c> — see class summary for why and
  /// what the real implementation needs from the SoundFlow pipeline.
  /// </summary>
  [HttpGet("dump-frame")]
  [ProducesResponseType(typeof(object), StatusCodes.Status501NotImplemented)]
  public IActionResult DumpAudioFrame()
  {
    _logger.LogInformation("Audio-frame dump requested via DevTray; returning 501 stub");
    return StatusCode(StatusCodes.Status501NotImplemented, new
    {
      error = "Audio-frame dump not yet implemented.",
      reason = "The SoundFlow output stage does not currently retain a re-readable buffer of the last N seconds of mixed PCM. A follow-up PR will add a circular buffer tap that the dump endpoint can serialize to WAV.",
      tracked = "Arc follow-up #23"
    });
  }
}
