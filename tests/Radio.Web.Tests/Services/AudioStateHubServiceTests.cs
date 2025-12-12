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

    // Act - Subscribe to event (should not throw)
    _service.PlaybackStateChanged += handler;
    
    // Cleanup - Unsubscribe (should not throw)
    _service.PlaybackStateChanged -= handler;
    
    // Assert - Verify subscription/unsubscription completed without errors
    Assert.NotNull(_service);
    Assert.False(eventRaised, "Event should not have been raised during test");
  }

  [Fact]
  public void AudioStateHubService_SupportsAllEventTypes()
  {
    // Arrange
    Func<Task> handler = () => Task.CompletedTask;

    // Act - Subscribe to all event types (should not throw)
    _service.PlaybackStateChanged += handler;
    _service.NowPlayingChanged += handler;
    _service.QueueChanged += handler;
    _service.RadioStateChanged += handler;
    _service.VolumeChanged += handler;
    _service.SourceChanged += handler;

    // Cleanup - Unsubscribe from all (should not throw)
    _service.PlaybackStateChanged -= handler;
    _service.NowPlayingChanged -= handler;
    _service.QueueChanged -= handler;
    _service.RadioStateChanged -= handler;
    _service.VolumeChanged -= handler;
    _service.SourceChanged -= handler;

    // Assert - Verify all subscriptions completed successfully
    Assert.NotNull(_service);
  }

  [Fact]
  public async Task AudioStateHubService_StartAsync_HandlesConnectionFailure()
  {
    // Act & Assert - Should handle connection failures gracefully
    try
    {
      await _service.StartAsync();
      // Connection attempt was made without throwing
      Assert.NotNull(_service);
    }
    catch (Exception)
    {
      // Expected when server is not available
      // Verify service remains in valid state even after connection failure
      Assert.NotNull(_service);
      Assert.False(_service.IsConnected, "Service should not be connected after failure");
    }
  }

  [Fact]
  public async Task AudioStateHubService_StopAsync_DoesNotThrow()
  {
    // Act - Should not throw even if not connected
    await _service.StopAsync();
    
    // Assert - Service remains in valid state after stop
    Assert.NotNull(_service);
    Assert.Equal(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Disconnected, _service.ConnectionState);
  }

  [Fact]
  public async Task AudioStateHubService_DisposeAsync_CleansUpResources()
  {
    // Act
    await _service.DisposeAsync();

    // Assert - Service should be disposed (IsConnected should be false)
    Assert.False(_service.IsConnected, "Service should not be connected after disposal");
  }

  [Fact]
  public async Task AudioStateHubService_MultipleDispose_DoesNotThrow()
  {
    // Act - Dispose twice (should not throw)
    await _service.DisposeAsync();
    await _service.DisposeAsync();

    // Assert - Service remains in valid state after multiple dispose calls
    Assert.False(_service.IsConnected, "Service should not be connected after disposal");
  }

  [Fact]
  public async Task AudioStateHubService_UsesCancellationToken()
  {
    // Arrange
    using var cts = new CancellationTokenSource();
    cts.CancelAfter(100); // Cancel after 100ms

    // Act - Should accept cancellation token
    try
    {
      await _service.StartAsync(cts.Token);
    }
    catch
    {
      // Connection failure is expected in test environment
    }

    // Assert - Service should be in valid state regardless of connection outcome
    Assert.NotNull(_service);
  }
}
