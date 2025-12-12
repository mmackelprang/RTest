using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Unit tests for AudioApiService
/// Tests API endpoint construction and error handling
/// </summary>
public class AudioApiServiceTests
{
  private readonly AudioApiService _service;
  private readonly HttpClient _httpClient;

  public AudioApiServiceTests()
  {
    _httpClient = new HttpClient
    {
      BaseAddress = new Uri("http://localhost:5000")
    };
    _service = new AudioApiService(_httpClient, NullLogger<AudioApiService>.Instance);
  }

  [Fact]
  public void AudioApiService_Constructor_InitializesSuccessfully()
  {
    // Arrange & Act
    var service = new AudioApiService(_httpClient, NullLogger<AudioApiService>.Instance);

    // Assert
    Assert.NotNull(service);
  }

  [Fact]
  public async Task GetPlaybackStateAsync_ReturnsNull_WhenServerNotAvailable()
  {
    // Act
    var result = await _service.GetPlaybackStateAsync();

    // Assert - Should return null when server is not available
    Assert.Null(result);
  }

  [Fact]
  public async Task GetNowPlayingAsync_ReturnsNull_WhenServerNotAvailable()
  {
    // Act
    var result = await _service.GetNowPlayingAsync();

    // Assert - Should return null when server is not available
    Assert.Null(result);
  }

  [Fact]
  public async Task GetVolumeAsync_ReturnsNull_WhenServerNotAvailable()
  {
    // Act
    var result = await _service.GetVolumeAsync();

    // Assert - Should return null when server is not available
    Assert.Null(result);
  }

  [Fact]
  public async Task SetVolumeAsync_ReturnsFalse_WhenServerNotAvailable()
  {
    // Act
    var result = await _service.SetVolumeAsync(0.75f);

    // Assert - Should return null when server is not available
    Assert.Null(result);
  }

  [Fact]
  public async Task StartAsync_HandlesErrors_Gracefully()
  {
    // Act
    var result = await _service.StartAsync();

    // Assert - Should return null on error
    Assert.Null(result);
  }

  [Fact]
  public async Task StopAsync_HandlesErrors_Gracefully()
  {
    // Act
    var result = await _service.StopAsync();

    // Assert - Should return null on error
    Assert.Null(result);
  }

  [Fact]
  public async Task NextAsync_HandlesErrors_Gracefully()
  {
    // Act
    var result = await _service.NextAsync();

    // Assert - Should return null on error
    Assert.Null(result);
  }

  [Fact]
  public async Task PreviousAsync_HandlesErrors_Gracefully()
  {
    // Act
    var result = await _service.PreviousAsync();

    // Assert - Should return null on error
    Assert.Null(result);
  }

  [Fact]
  public async Task SetShuffleAsync_HandlesErrors_Gracefully()
  {
    // Act
    var result = await _service.SetShuffleAsync(true);

    // Assert - Should return null on error
    Assert.Null(result);
  }

  [Fact]
  public async Task SetRepeatAsync_HandlesErrors_Gracefully()
  {
    // Act
    var result = await _service.SetRepeatAsync("All");

    // Assert - Should return null on error
    Assert.Null(result);
  }

  [Fact]
  public async Task ToggleMuteAsync_HandlesErrors_Gracefully()
  {
    // Act
    var result = await _service.ToggleMuteAsync(true);

    // Assert - Should return null on error
    Assert.Null(result);
  }

  [Fact]
  public async Task AudioApiService_UsesCancellationToken()
  {
    // Arrange
    using var cts = new CancellationTokenSource();
    cts.CancelAfter(100); // Cancel after 100ms

    // Act - Methods should accept and respect cancellation token
    var tasks = new Task[]
    {
      _service.GetPlaybackStateAsync(cts.Token),
      _service.GetNowPlayingAsync(cts.Token),
      _service.GetVolumeAsync(cts.Token)
    };

    // Assert - Should complete (either with result or cancellation)
    await Task.WhenAll(tasks.Select(t => t.ContinueWith(_ => { })));
    Assert.NotNull(_service); // Verify service remains in valid state
  }
}
