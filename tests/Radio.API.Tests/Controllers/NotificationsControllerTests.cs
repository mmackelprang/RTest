using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radio.API.Tests.TestSupport;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for NotificationsController, driven over HTTP with the announcement service
/// replaced by a capturing double.
/// </summary>
/// <remarks>
/// ⚠ The previous version of <c>Announce_WithValidMessage_ReturnsOk</c> posted to a host where TTS
/// cannot work and asserted success — it passed only because AnnounceAsync swallowed every failure,
/// which is TTS-2 itself. These tests control the outcome instead, so each status below is one the
/// controller had to choose.
/// </remarks>
public class NotificationsControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly CustomWebApplicationFactory<Program> _factory;

  public NotificationsControllerTests(CustomWebApplicationFactory<Program> factory)
  {
    _factory = factory;
  }

  private (HttpClient Client, List<int> Priorities) ClientWith(AnnouncementOutcome outcome)
  {
    var priorities = new List<int>();
    var announcements = new Mock<IAnnouncementService>();
    announcements
      .Setup(a => a.AnnounceAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
      .Callback<string, int, CancellationToken>((_, priority, _) => priorities.Add(priority))
      .ReturnsAsync(outcome);

    var client = _factory
      .WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        s.AddSingleton<IAnnouncementService>(announcements.Object)))
      .CreateClient();
    return (client, priorities);
  }

  [Fact]
  public async Task Announce_WhenItPlays_Returns200Completed()
  {
    var (client, _) = ClientWith(AnnouncementOutcome.Completed);

    var response = await client.PostAsJsonAsync("/api/notifications/announce", new { Message = "Test", Priority = 5 });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("completed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString());
  }

  [Fact]
  public async Task Announce_WhenItFails_Returns500NotOk()
  {
    // ⭐ TTS-2. A swallowed failure used to come back 200 "Announcement played".
    // MUTATION: make the controller's switch return Ok for Failed — red.
    var (client, _) = ClientWith(AnnouncementOutcome.Failed);

    var response = await client.PostAsJsonAsync("/api/notifications/announce", new { Message = "Test", Priority = 5 });

    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("failed", body.GetProperty("outcome").GetString());
    Assert.DoesNotContain("played", body.ToString(), StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task Announce_WhenInterrupted_Returns200ButDoesNotClaimItPlayed()
  {
    var (client, _) = ClientWith(AnnouncementOutcome.Interrupted);

    var response = await client.PostAsJsonAsync("/api/notifications/announce", new { Message = "Test", Priority = 5 });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("interrupted", body.GetProperty("outcome").GetString());
    Assert.DoesNotContain("played", body.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task Announce_WithNoPriorityField_AnnouncesAtPriority8()
  {
    // ⭐ TEST-8. The body carries NO Priority property at all — not null, absent — which is what a
    // doorbell sends. The literal 8 is asserted as a NUMBER, not read from GvMediaOptions, so the
    // two cannot drift together and still pass.
    // MUTATION: change `request.Priority ?? 8` to `?? 5` in NotificationsController — red.
    var (client, priorities) = ClientWith(AnnouncementOutcome.Completed);

    var response = await client.PostAsJsonAsync("/api/notifications/announce", new { Message = "Doorbell" });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(8, Assert.Single(priorities));
  }

  [Fact]
  public async Task Announce_WithAnExplicitPriority_PassesItThrough()
  {
    // The control for the test above: a supplied priority is not overridden by the default.
    var (client, priorities) = ClientWith(AnnouncementOutcome.Completed);

    await client.PostAsJsonAsync("/api/notifications/announce", new { Message = "Test", Priority = 3 });

    Assert.Equal(3, Assert.Single(priorities));
  }

  [Fact]
  public async Task Announce_WithEmptyMessage_ReturnsBadRequest()
  {
    var (client, priorities) = ClientWith(AnnouncementOutcome.Completed);

    var response = await client.PostAsJsonAsync("/api/notifications/announce", new { Message = "", Priority = 5 });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Empty(priorities);
  }
}
