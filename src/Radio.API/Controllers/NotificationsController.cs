using Microsoft.AspNetCore.Mvc;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Utilities;

namespace Radio.API.Controllers;

/// <summary>
/// API controller for external notification announcements.
/// Any service can POST a message to be announced via TTS with audio ducking.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class NotificationsController : ControllerBase
{
  private readonly ILogger<NotificationsController> _logger;
  private readonly IAnnouncementService _announcementService;

  /// <summary>
  /// Initializes a new instance of the NotificationsController.
  /// </summary>
  public NotificationsController(
    ILogger<NotificationsController> logger,
    IAnnouncementService announcementService)
  {
    _logger = logger;
    _announcementService = announcementService;
  }

  /// <summary>
  /// Announce a message via TTS with audio ducking.
  /// </summary>
  /// <param name="request">The announcement request.</param>
  /// <returns>
  /// 200 once the announcement has played to its end (the request waits for it); 200 with
  /// <c>outcome: "interrupted"</c> if it was stopped part-way; 500 if it did not play (TTS-2).
  /// </returns>
  [HttpPost("announce")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<IActionResult> Announce([FromBody] AnnounceRequest request)
  {
    try
    {
      if (string.IsNullOrWhiteSpace(request.Message))
      {
        return BadRequest(new { error = "Message is required" });
      }

      // ⚠ The 8 is load-bearing and pinned by NotificationsControllerTests (TEST-8): it is where every
      // doorbell's priority comes from when the caller sends none, and GvMedia:PreemptAtPriority
      // (also 8) is set against it.
      var priority = Math.Clamp(request.Priority ?? 8, 1, 10);

      // Priority is what this announcement is registered at with IDuckingService, and it is the
      // only request field left on the line once the body is a token. It does NOT decide preemption
      // BETWEEN ANNOUNCEMENTS: a newer announcement replaces the one speaking whatever either
      // priority is, and the replaced request returns 200 with outcome "interrupted"
      // (AnnouncementService.BecomeActive, AUD-73). It DOES decide whether attended
      // playback (a voicemail) is preempted: EventPlaybackService observes the DuckingStateChanged
      // this route's StartDuckingAsync raises and compares the priority against
      // GvMedia:PreemptAtPriority.
      //
      // The token's length catches a truncated body. It cannot catch an EMPTY one — the guard above
      // already rejected that. See LogSafeText for what the token does not promise.
      _logger.LogInformation("Notification announce request: {Message} (priority {Priority})",
        LogSafeText.For(request.Message), priority);

      var outcome = await _announcementService.AnnounceAsync(request.Message, priority);

      // TTS-2: this used to return 200 "Announcement played" whatever happened, because
      // AnnounceAsync swallowed every failure. A misconfigured TTS voice therefore produced silence
      // and a success toast. The body is a constant — it echoes nothing from the request.
      return outcome switch
      {
        AnnouncementOutcome.Completed => Ok(new { message = "Announcement played", outcome = "completed" }),
        AnnouncementOutcome.Interrupted => Ok(new { message = "Announcement stopped before it finished", outcome = "interrupted" }),
        _ => StatusCode(500, new { error = "Announcement did not play; see the radio-api log", outcome = "failed" })
      };
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error processing announcement request");
      return StatusCode(500, new { error = "Failed to process announcement" });
    }
  }
}

/// <summary>
/// Request body for the announce endpoint.
/// </summary>
public class AnnounceRequest
{
  /// <summary>Text message to announce via TTS.</summary>
  public string Message { get; set; } = "";

  /// <summary>Ducking priority (1-10, default 8). Higher = more important.</summary>
  public int? Priority { get; set; }
}
