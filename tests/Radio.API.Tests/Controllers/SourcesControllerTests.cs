using System.Net.Http.Json;
using Radio.API.Tests.TestSupport;
using Radio.API.Models;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for the SourcesController.
/// </summary>
public class SourcesControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly CustomWebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;

  public SourcesControllerTests(CustomWebApplicationFactory<Program> factory)
  {
    _factory = factory;
    _client = _factory.CreateClient();
  }

  [Fact]
  public async Task GetSources_ReturnsAvailableSources()
  {
    // Act
    var response = await _client.GetAsync("/api/sources");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var sources = await response.Content.ReadFromJsonAsync<AvailableSourcesDto>();
    Assert.NotNull(sources);
    Assert.NotNull(sources.PrimarySources);
    Assert.NotEmpty(sources.PrimarySources);
    Assert.Contains("Radio", sources.PrimarySources);
    Assert.Contains("Vinyl", sources.PrimarySources);
    Assert.Contains("FilePlayer", sources.PrimarySources);
    Assert.Contains("GenericUSB", sources.PrimarySources);
    Assert.Contains("Bluetooth", sources.PrimarySources);
  }

  [Fact]
  public async Task GetActiveSources_ReturnsEmptyOrSourceList()
  {
    // Act
    var response = await _client.GetAsync("/api/sources/active");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var sources = await response.Content.ReadFromJsonAsync<List<AudioSourceDto>>();
    Assert.NotNull(sources);
  }

  [Fact]
  public async Task GetEventSources_ReturnsEmptyOrSourceList()
  {
    // Act
    var response = await _client.GetAsync("/api/sources/events");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var sources = await response.Content.ReadFromJsonAsync<List<AudioSourceDto>>();
    Assert.NotNull(sources);
  }

  [Fact]
  public async Task GetPrimarySource_ReturnsExpectedResponse()
  {
    // Act
    var response = await _client.GetAsync("/api/sources/primary");

    // Assert
    // In integration tests, other tests may have activated a source.
    // Accept either 404 (no source) or 200 (source active).
    Assert.True(
      response.StatusCode == System.Net.HttpStatusCode.NotFound ||
      response.StatusCode == System.Net.HttpStatusCode.OK,
      $"Expected NotFound or OK, got {response.StatusCode}");

    if (response.IsSuccessStatusCode)
    {
      var source = await response.Content.ReadFromJsonAsync<AudioSourceDto>();
      Assert.NotNull(source);
      Assert.NotNull(source.Type);
    }
  }

  [Fact]
  public async Task SelectSource_WithEmptySourceType_ReturnsBadRequest()
  {
    // Arrange
    var request = new SelectSourceRequest
    {
      SourceType = ""
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/sources", request);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task SelectSource_WithInvalidSourceType_ReturnsBadRequest()
  {
    // Arrange
    var request = new SelectSourceRequest
    {
      SourceType = "InvalidSource"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/sources", request);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task SelectSource_WithRemovedSource_ReturnsBadRequest()
  {
    // Arrange - Spotify was removed, so it should be treated as an invalid source
    var request = new SelectSourceRequest
    {
      SourceType = "Spotify"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/sources", request);

    // Assert - Should return BadRequest since Spotify is no longer a valid source type
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task SelectSource_WithRadio_ReturnsExpectedResult()
  {
    // Arrange - Radio source requires USB audio device configuration
    var request = new SelectSourceRequest
    {
      SourceType = "Radio"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/sources", request);

    // Assert - Radio returns success if configured, or error if USB device not set
    // With empty USBPort config, the factory won't create the source
    if (response.IsSuccessStatusCode)
    {
      var source = await response.Content.ReadFromJsonAsync<AudioSourceDto>();
      Assert.NotNull(source);
      Assert.Equal("Radio", source.Type);
    }
    else
    {
      // Expected when USB audio device is not configured
      Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
    }
  }

  [Fact]
  public async Task GetTTSEngines_ReturnsEngineList()
  {
    // Act
    var response = await _client.GetAsync("/api/sources/events/tts/engines");

    // Assert
    Assert.True(response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotImplemented,
      $"Expected success or not implemented, got {response.StatusCode}");

    if (response.IsSuccessStatusCode)
    {
      var engines = await response.Content.ReadFromJsonAsync<List<TTSEngineInfoDto>>();
      Assert.NotNull(engines);
    }
  }

  [Fact]
  public async Task GetNotificationSounds_ReturnsFileList()
  {
    // Act
    var response = await _client.GetAsync("/api/sources/events/sounds");

    // Assert
    Assert.True(response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotImplemented,
      $"Expected success or not implemented, got {response.StatusCode}");

    if (response.IsSuccessStatusCode)
    {
      var sounds = await response.Content.ReadFromJsonAsync<List<NotificationSoundDto>>();
      Assert.NotNull(sounds);
    }
  }

  [Fact]
  public async Task PlayTTSEvent_WithEmptyText_ReturnsBadRequest()
  {
    // Arrange
    var request = new PlayTTSRequest
    {
      Text = ""
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/sources/events/tts", request);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task PlayFileEvent_WithEmptyFilePath_ReturnsBadRequest()
  {
    // Arrange
    var request = new PlayFileEventRequest
    {
      FilePath = ""
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/sources/events/file", request);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }
}
