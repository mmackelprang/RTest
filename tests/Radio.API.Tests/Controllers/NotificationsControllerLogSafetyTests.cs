using Microsoft.AspNetCore.Mvc;
using Moq;
using Radio.API.Controllers;
using Radio.API.Tests.TestSupport;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Utilities;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// <c>TTS-11</c> <c>T6</c> (announce half): <see cref="NotificationsController"/> writes no
/// announcement body to the log.
/// </summary>
/// <remarks>
/// The controller is constructed directly rather than driven through
/// <c>CustomWebApplicationFactory</c>, because the assertion is about what a specific logger
/// received and the factory's pipeline gives no seam to capture it. The action method itself is
/// real and so is the request binding shape.
///
/// This is the shared entry point for external announcements — the route a doorbell or an SMS
/// relay posts to — so the body reaching it is household content by exactly the standard the
/// utterance rule protects.
/// </remarks>
public class NotificationsControllerLogSafetyTests
{
  /// <summary>
  /// Chosen to be absent from every other fixture in the suite, so a <c>DoesNotContain</c> cannot
  /// pass by accident against generic text.
  /// </summary>
  private const string Sentinel = "Marmalade sentinel four seven";

  private static async Task<CapturingLoggerProvider> AnnounceAsync()
  {
    var logs = new CapturingLoggerProvider();
    var announcements = new Mock<IAnnouncementService>();

    var controller = new NotificationsController(
      logs.CreateLogger<NotificationsController>(), announcements.Object);

    var result = await controller.Announce(new AnnounceRequest { Message = Sentinel, Priority = 5 });

    // The request really was accepted: an early BadRequest would skip the log line and make every
    // assertion below vacuous in a way "no sentinel in the log" cannot distinguish.
    Assert.IsType<OkObjectResult>(result);
    announcements.Verify(a => a.AnnounceAsync(Sentinel, 5, It.IsAny<CancellationToken>()), Times.Once);

    return logs;
  }

  [Fact]
  public async Task NoLogLineFromAnAnnounceRequestCarriesTheMessageBody()
  {
    var logs = await AnnounceAsync();

    // ⚠ Without this the whole test passes vacuously against a controller that logs nothing.
    Assert.NotEmpty(logs.Messages);

    foreach (var message in logs.Messages)
    {
      Assert.DoesNotContain(Sentinel, message, StringComparison.Ordinal);
      Assert.DoesNotContain("Marmalade", message, StringComparison.Ordinal);
    }
  }

  [Fact]
  public async Task TheAnnounceLineKeepsTheTokenAndThePriority()
  {
    // "No body" must not be achieved by logging nothing. Priority is what the announcement is
    // registered at with IDuckingService and it is untouched by this row — it does NOT decide
    // preemption on this route, see the controller's own comment. The token's length catches a
    // TRUNCATED body; it cannot catch an empty one, because the controller rejects those before
    // this line runs.
    var logs = await AnnounceAsync();

    var line = Assert.Single(
      logs.Messages, m => m.StartsWith("Notification announce request:", StringComparison.Ordinal));

    Assert.Contains(LogSafeText.For(Sentinel), line, StringComparison.Ordinal);
    Assert.Contains("priority 5", line, StringComparison.Ordinal);
  }
}
