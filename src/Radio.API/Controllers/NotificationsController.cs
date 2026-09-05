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
  /// <returns>200 OK if the announcement was queued.</returns>
  [HttpPost("announce")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> Announce([FromBody] AnnounceRequest request)
  {
    try
    {
      if (string.IsNullOrWhiteSpace(request.Message))
      {
        return BadRequest(new { error = "Message is required" });
      }

      var priority = Math.Clamp(request.Priority ?? 8, 1, 10);

      // Priority is kept because it is what this announcement is registered at with
      // IDuckingService, and it is the only request field left on the line once the body is a
      // token. It does NOT decide preemption on THIS route: AnnouncementService.SetActiveSource
      // cancels whatever announcement was active, unconditionally and without consulting either
      // priority, and the GvMedia:PreemptAtPriority threshold is read only by EventPlaybackService,
      // which /api/notifications/announce does not go through.
      //
      // The token's length catches a truncated body. It cannot catch an EMPTY one — the guard four
      // lines up already rejected that. See LogSafeText for what the token does not promise.
      _logger.LogInformation("Notification announce request: {Message} (priority {Priority})",
        LogSafeText.For(request.Message), priority);

      await _announcementService.AnnounceAsync(request.Message, priority);

      return Ok(new { message = "Announcement played" });
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
