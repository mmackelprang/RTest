using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Unit tests for AudioStateHubService
/// Tests SignalR hub initialization and event handling
/// </summary>
///
/// <remarks>
/// Every service this fixture builds goes through <see cref="CreateService"/> so it can be
/// disposed in <see cref="DisposeAsync"/>. That is not tidiness: a failed
/// <c>StartAsync</c> leaves a detached <c>StartRetryLoop</c> task behind, and an undisposed
/// service leaks one of those for the lifetime of the test process. The
/// <see cref="OfflineHubTransport"/> passed to each instance keeps those attempts off the
/// network in the first place.
/// </remarks>
public class AudioStateHubServiceTests : IAsyncLifetime
{
  private readonly List<AudioStateHubService> _created = [];
  private readonly AudioStateHubService _service;
  private readonly IConfiguration _configuration;

  public AudioStateHubServiceTests()
  {
    _configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl }
      })
      .Build();

    _service = CreateService();
  }

  /// <summary>Builds a tracked service wired to the offline transport.</summary>
  private AudioStateHubService CreateService()
  {
    var service = new AudioStateHubService(
      NullLogger<AudioStateHubService>.Instance,
      _configuration,
      transport: new OfflineHubTransport()
    );
    _created.Add(service);
    return service;
  }

  public Task InitializeAsync() => Task.CompletedTask;

  public async Task DisposeAsync()
  {
    // Disposing twice is safe (AudioStateHubService_MultipleDispose_DoesNotThrow pins that),
    // so services a test already disposed need no special handling here.
    foreach (AudioStateHubService service in _created)
    {
      await service.DisposeAsync();
    }
  }

  [Fact]
  public void AudioStateHubService_Constructor_InitializesSuccessfully()
  {
    // Arrange & Act
    AudioStateHubService service = CreateService();

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
    Func<NowPlayingDto?, Task> nowPlayingHandler = _ => Task.CompletedTask;
    Func<VolumeDto?, Task> volumeHandler = _ => Task.CompletedTask;
    // RadioStateChanged now carries the typed DTO so subscribers can read
    // NowPlayingMatchId directly without a REST refetch.
    Func<RadioStateDto, Task> radioStateHandler = _ => Task.CompletedTask;

    // Act - Subscribe to all event types (should not throw)
    _service.PlaybackStateChanged += handler;
    _service.NowPlayingChanged += nowPlayingHandler;
    _service.QueueChanged += handler;
    _service.RadioStateChanged += radioStateHandler;
    _service.VolumeChanged += volumeHandler;
    _service.SourceChanged += handler;

    // Cleanup - Unsubscribe from all (should not throw)
    _service.PlaybackStateChanged -= handler;
    _service.NowPlayingChanged -= nowPlayingHandler;
    _service.QueueChanged -= handler;
    _service.RadioStateChanged -= radioStateHandler;
    _service.VolumeChanged -= volumeHandler;
    _service.SourceChanged -= handler;

    // Assert - Verify all subscriptions completed successfully
    Assert.NotNull(_service);
  }

  [Fact]
  public void AudioStateHubService_ExposesEventPlaybackChanged()
  {
    // ADR-029 D6 §8.1. Typed, like NowPlayingChanged and unlike PlaybackStateChanged: the payload IS
    // the state, so a subscriber that re-fetched it over REST would add a round trip to a push that
    // already carries everything.
    //
    // ⚠ WHAT THIS DOES NOT PROVE, said plainly because the test's name would otherwise imply it.
    // It does NOT show that a delivered "EventPlaybackChanged" message reaches this event with its
    // payload intact. No test in this assembly can: the fixture runs on OfflineHubTransport, the
    // connection is never started, and there is no in-tree precedent for reflecting into
    // HubConnection's handler table to inject one — writing that harness for a single test would be
    // testing SignalR rather than testing us. Plan PHN-1e Task 10d asked for the delivery assertion;
    // this is the honest subset of it, matching AudioStateHubService_SupportsAllEventTypes above.
    //
    // End-to-end delivery — and with it the C-47 question of whether the payload survives the REAL
    // JsonHubProtocol — is settled on the appliance instead, per the plan's §2.2 item 1. That is
    // recorded as U1 and it is not a gap this file can close.
    Func<EventPlaybackSnapshotDto?, Task> handler = _ => Task.CompletedTask;

    _service.EventPlaybackChanged += handler;
    _service.EventPlaybackChanged -= handler;

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
