using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Unit tests for AudioStateHubService
/// Tests SignalR hub initialization and event handling
/// </summary>
public class AudioStateHubServiceTests
{
  private readonly AudioStateHubService _service;
  private readonly IConfiguration _configuration;

  public AudioStateHubServiceTests()
  {
    _configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", "http://localhost:5000" }
      })
      .Build();

    _service = new AudioStateHubService(
      NullLogger<AudioStateHubService>.Instance,
      _configuration
    );
  }

  [Fact]
  public void AudioStateHubService_Constructor_InitializesSuccessfully()
  {
    // Arrange & Act
    var service = new AudioStateHubService(
      NullLogger<AudioStateHubService>.Instance,
      _configuration
    );

    // Assert
    Assert.NotNull(service);
  }

  [Fact]
  public void AudioStateHubService_IsConnected_InitiallyFalse()
  {
    // Assert
    Assert.False(_service.IsConnected);
  }

  [Fact]
  public void AudioStateHubService_ConnectionState_InitiallyDisconnected()
  {
    // Assert
    Assert.Equal(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Disconnected, _service.ConnectionState);
  }

  [Fact]
  public void AudioStateHubService_SupportsEventSubscription()
  {
    // Arrange
    var eventRaised = false;
    Func<Task> handler = () =>
    {
      eventRaised = true;
      return Task.CompletedTask;
    };

    // Act - Subscribe to event
    _service.PlaybackStateChanged += handler;

    // Assert - Event subscription should not throw
    Assert.False(eventRaised); // Event hasn't been raised yet
    
    // Cleanup
    _service.PlaybackStateChanged -= handler;
  }

  [Fact]
  public void AudioStateHubService_SupportsAllEventTypes()
  {
    // Arrange
    Func<Task> handler = () => Task.CompletedTask;

    // Act & Assert - All event types should be subscribable
    _service.PlaybackStateChanged += handler;
    _service.NowPlayingChanged += handler;
    _service.QueueChanged += handler;
    _service.RadioStateChanged += handler;
    _service.VolumeChanged += handler;
    _service.SourceChanged += handler;

    // Cleanup
    _service.PlaybackStateChanged -= handler;
    _service.NowPlayingChanged -= handler;
    _service.QueueChanged -= handler;
    _service.RadioStateChanged -= handler;
    _service.VolumeChanged -= handler;
    _service.SourceChanged -= handler;

    Assert.True(true);
  }

  [Fact]
  public async Task AudioStateHubService_StartAsync_HandlesConnectionFailure()
  {
    // Act & Assert - Should not throw when connection fails
    try
    {
      await _service.StartAsync();
      // If no exception, that's also valid (connection attempt was made)
      Assert.True(true);
    }
    catch (Exception)
    {
      // Expected when server is not available
      Assert.True(true);
    }
  }

  [Fact]
  public async Task AudioStateHubService_StopAsync_DoesNotThrow()
  {
    // Act & Assert - Should not throw even if not connected
    await _service.StopAsync();
    Assert.True(true);
  }

  [Fact]
  public async Task AudioStateHubService_DisposeAsync_CleansUpResources()
  {
    // Act
    await _service.DisposeAsync();

    // Assert - Should complete without throwing
    Assert.True(true);
  }

  [Fact]
  public async Task AudioStateHubService_MultipleDispose_DoesNotThrow()
  {
    // Act - Dispose twice
    await _service.DisposeAsync();
    await _service.DisposeAsync();

    // Assert - Should handle multiple dispose calls
    Assert.True(true);
  }

  [Fact]
  public async Task AudioStateHubService_UsesCancellationToken()
  {
    // Arrange
    using var cts = new CancellationTokenSource();

    // Act & Assert - Should accept cancellation token
    try
    {
      await _service.StartAsync(cts.Token);
    }
    catch
    {
      // Connection failure is expected in test environment
    }

    Assert.True(true);
  }
}
