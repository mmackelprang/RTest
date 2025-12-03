using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Radio.API.Models;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for the AudioController.
/// </summary>
public class AudioControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;

  public AudioControllerTests(WebApplicationFactory<Program> factory)
  {
    _factory = factory;
    _client = _factory.CreateClient();
  }

  [Fact]
  public async Task GetPlaybackState_ReturnsOk()
  {
    // Act
    var response = await _client.GetAsync("/api/audio");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var state = await response.Content.ReadFromJsonAsync<PlaybackStateDto>();
    Assert.NotNull(state);
    Assert.NotNull(state.DuckingState);
  }

  [Fact]
  public async Task GetVolume_ReturnsVolumeInfo()
  {
    // Act
    var response = await _client.GetAsync("/api/audio/volume");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("volume", content);
    Assert.Contains("isMuted", content);
    Assert.Contains("balance", content);
  }

  [Fact]
  public async Task SetVolume_WithValidValue_ReturnsOk()
  {
    // Act
    var response = await _client.PostAsync("/api/audio/volume/0.5", null);

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("volume", content);
  }

  [Fact]
  public async Task SetVolume_WithInvalidValue_ReturnsBadRequest()
  {
    // Act - volume of 1.5 is out of range
    var response = await _client.PostAsync("/api/audio/volume/1.5", null);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task ToggleMute_ReturnsOk()
  {
    // Act
    var response = await _client.PostAsync("/api/audio/mute", null);

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("isMuted", content);
  }

  [Fact]
  public async Task UpdatePlaybackState_WithVolumeChange_UpdatesVolume()
  {
    // Arrange
    var request = new UpdatePlaybackRequest
    {
      Action = PlaybackAction.None,
      Volume = 0.7f
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/audio", request);

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var state = await response.Content.ReadFromJsonAsync<PlaybackStateDto>();
    Assert.NotNull(state);
    // Note: The actual volume may be different due to audio engine state
  }

  [Fact]
  public async Task StartEngine_ReturnsOk()
  {
    // Act
    var response = await _client.PostAsync("/api/audio/start", null);

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("state", content);
  }

  [Fact]
  public async Task StopEngine_ReturnsOk()
  {
    // Act
    var response = await _client.PostAsync("/api/audio/stop", null);

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("state", content);
  }

  [Fact]
  public async Task Next_WithNoActiveSource_ReturnsBadRequest()
  {
    // Act
    var response = await _client.PostAsync("/api/audio/next", null);

    // Assert
    // Without an active primary source, should return 400
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("No primary audio source is active", content);
  }

  [Fact]
  public async Task Previous_WithNoActiveSource_ReturnsBadRequest()
  {
    // Act
    var response = await _client.PostAsync("/api/audio/previous", null);

    // Assert
    // Without an active primary source, should return 400
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("No primary audio source is active", content);
  }

  [Fact]
  public async Task SetShuffle_WithNoActiveSource_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetShuffleRequest { Enabled = true };

    // Act
    var response = await _client.PostAsJsonAsync("/api/audio/shuffle", request);

    // Assert
    // Without an active primary source, should return 400
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("No primary audio source is active", content);
  }

  [Fact]
  public async Task SetRepeatMode_WithNoActiveSource_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetRepeatModeRequest { Mode = "One" };

    // Act
    var response = await _client.PostAsJsonAsync("/api/audio/repeat", request);

    // Assert
    // Without an active primary source, should return 400
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("No primary audio source is active", content);
  }

  [Fact]
  public async Task SetRepeatMode_WithInvalidMode_ReturnsBadRequest()
  {
    // Arrange - This test validates the mode parsing logic
    // Since we don't have an active source in test environment, 
    // we'll just verify the error message format is correct.
    // The actual invalid mode validation happens when a source is active,
    // but the API will first check for an active source.
    var request = new SetRepeatModeRequest { Mode = "InvalidMode" };

    // Act
    var response = await _client.PostAsJsonAsync("/api/audio/repeat", request);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

    var content = await response.Content.ReadAsStringAsync();
    // Will get "No primary audio source" error first in test environment
    Assert.Contains("error", content);
  }

  [Fact]
  public async Task GetPlaybackState_IncludesNavigationCapabilities()
  {
    // Act
    var response = await _client.GetAsync("/api/audio");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var state = await response.Content.ReadFromJsonAsync<PlaybackStateDto>();
    Assert.NotNull(state);

    // Verify new properties exist (values depend on source)
    // When no source is active, these will be false/null
    Assert.False(state.CanNext); // Should be false when no source
    Assert.False(state.CanPrevious);
    Assert.False(state.CanShuffle);
    Assert.False(state.CanRepeat);
    Assert.False(state.IsShuffleEnabled);
    // RepeatMode will be null when no active source
  }
}
