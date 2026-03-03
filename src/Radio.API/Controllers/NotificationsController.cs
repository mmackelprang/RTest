using Microsoft.AspNetCore.Mvc;
using Radio.Core.Interfaces.Audio;

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

      _logger.LogInformation("Notification announce request: '{Message}' (priority {Priority})",
        request.Message, priority);

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
