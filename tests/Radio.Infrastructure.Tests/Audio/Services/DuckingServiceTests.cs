using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.Tests.Audio.Services;

public class DuckingServiceTests
{
  private readonly Mock<ILogger<DuckingService>> _loggerMock;
  private readonly Mock<IOptionsMonitor<AudioOptions>> _optionsMock;
  private readonly Mock<IMasterMixer> _mixerMock;
  private readonly AudioOptions _defaultOptions;

  public DuckingServiceTests()
  {
    _loggerMock = new Mock<ILogger<DuckingService>>();
    _optionsMock = new Mock<IOptionsMonitor<AudioOptions>>();
    _mixerMock = new Mock<IMasterMixer>();

    _defaultOptions = new AudioOptions
    {
      DuckingPercentage = 20,
      DuckingPolicy = DuckingPolicy.FadeSmooth,
      DuckingAttackMs = 100,
      DuckingReleaseMs = 500
    };

    _optionsMock.Setup(x => x.CurrentValue).Returns(_defaultOptions);
  }

  private DuckingService CreateService()
  {
    return new DuckingService(_loggerMock.Object, _optionsMock.Object, _mixerMock.Object);
  }

  private Mock<IEventAudioSource> CreateMockEventSource(string? id = null)
  {
    var mock = new Mock<IEventAudioSource>();
    mock.Setup(x => x.Id).Returns(id ?? Guid.NewGuid().ToString("N"));
    mock.Setup(x => x.Category).Returns(AudioSourceCategory.Event);
    mock.Setup(x => x.Type).Returns(AudioSourceType.TTS);
    mock.Setup(x => x.Duration).Returns(TimeSpan.FromSeconds(2));
    return mock;
  }

  private Mock<IAudioSource> CreateMockPrimarySource(string? id = null)
  {
    var mock = new Mock<IAudioSource>();
    mock.Setup(x => x.Id).Returns(id ?? Guid.NewGuid().ToString("N"));
    mock.Setup(x => x.Category).Returns(AudioSourceCategory.Primary);
    mock.Setup(x => x.Type).Returns(AudioSourceType.Spotify);
    return mock;
  }

  [Fact]
  public void Constructor_ThrowsOnNullLogger()
  {
    Assert.Throws<ArgumentNullException>(
      () => new DuckingService(null!, _optionsMock.Object, _mixerMock.Object));
  }

  [Fact]
  public void Constructor_ThrowsOnNullOptions()
  {
    Assert.Throws<ArgumentNullException>(
      () => new DuckingService(_loggerMock.Object, null!, _mixerMock.Object));
  }

  [Fact]
  public void Constructor_ThrowsOnNullMixer()
  {
    Assert.Throws<ArgumentNullException>(
      () => new DuckingService(_loggerMock.Object, _optionsMock.Object, null!));
  }

  [Fact]
  public void InitialState_IsDuckingFalse()
  {
    var service = CreateService();

    Assert.False(service.IsDucking);
  }

  [Fact]
  public void InitialState_CurrentDuckLevelIs100()
  {
    var service = CreateService();

    Assert.Equal(100f, service.CurrentDuckLevel);
  }

  [Fact]
  public void InitialState_ActiveEventCountIsZero()
  {
    var service = CreateService();

    Assert.Equal(0, service.ActiveEventCount);
  }

  [Fact]
  public async Task StartDuckingAsync_ThrowsOnNullSource()
  {
    var service = CreateService();

    await Assert.ThrowsAsync<ArgumentNullException>(
      () => service.StartDuckingAsync(null!));
  }

  [Fact]
  public async Task StartDuckingAsync_SetsIsDuckingTrue()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    // Use Instant policy for immediate effect
    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    await service.StartDuckingAsync(eventSource.Object);

    Assert.True(service.IsDucking);
  }

  [Fact]
  public async Task StartDuckingAsync_IncrementsActiveEventCount()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    await service.StartDuckingAsync(eventSource.Object);

    Assert.Equal(1, service.ActiveEventCount);
  }

  [Fact]
  public async Task StartDuckingAsync_InstantPolicy_SetsDuckLevelImmediately()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;
    _defaultOptions.DuckingPercentage = 25;

    await service.StartDuckingAsync(eventSource.Object);

    Assert.Equal(25f, service.CurrentDuckLevel);
  }

  [Fact]
  public async Task StartDuckingAsync_RaisesDuckingStateChangedEvent()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();
    DuckingStateChangedEventArgs? capturedArgs = null;

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    service.DuckingStateChanged += (_, args) => capturedArgs = args;

    await service.StartDuckingAsync(eventSource.Object);

    Assert.NotNull(capturedArgs);
    Assert.True(capturedArgs.IsDucking);
    Assert.Same(eventSource.Object, capturedArgs.TriggeringSource);
    Assert.Equal(1, capturedArgs.ActiveEventCount);
  }

  [Fact]
  public async Task StartDuckingAsync_MultipleEvents_DoesNotDuckAgain()
  {
    var service = CreateService();
    var eventSource1 = CreateMockEventSource("source1");
    var eventSource2 = CreateMockEventSource("source2");
    var stateChangeCount = 0;

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    service.DuckingStateChanged += (_, _) => stateChangeCount++;

    await service.StartDuckingAsync(eventSource1.Object);
    await service.StartDuckingAsync(eventSource2.Object);

    // Only one state change event (the first one)
    Assert.Equal(1, stateChangeCount);
    Assert.Equal(2, service.ActiveEventCount);
  }

  [Fact]
  public async Task StopDuckingAsync_ThrowsOnNullSource()
  {
    var service = CreateService();

    await Assert.ThrowsAsync<ArgumentNullException>(
      () => service.StopDuckingAsync(null!));
  }

  [Fact]
  public async Task StopDuckingAsync_RestoresFullVolume_WhenLastEvent()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    await service.StartDuckingAsync(eventSource.Object);
    await service.StopDuckingAsync(eventSource.Object);

    Assert.False(service.IsDucking);
    Assert.Equal(100f, service.CurrentDuckLevel);
    Assert.Equal(0, service.ActiveEventCount);
  }

  [Fact]
  public async Task StopDuckingAsync_DoesNotRestoreVolume_WhenOtherEventsActive()
  {
    var service = CreateService();
    var eventSource1 = CreateMockEventSource("source1");
    var eventSource2 = CreateMockEventSource("source2");

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    await service.StartDuckingAsync(eventSource1.Object);
    await service.StartDuckingAsync(eventSource2.Object);
    await service.StopDuckingAsync(eventSource1.Object);

    Assert.True(service.IsDucking);
    Assert.Equal(_defaultOptions.DuckingPercentage, service.CurrentDuckLevel);
    Assert.Equal(1, service.ActiveEventCount);
  }

  [Fact]
  public async Task StopDuckingAsync_RaisesDuckingStateChangedEvent_WhenLastEvent()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();
    var stateChanges = new List<DuckingStateChangedEventArgs>();

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    service.DuckingStateChanged += (_, args) => stateChanges.Add(args);

    await service.StartDuckingAsync(eventSource.Object);
    await service.StopDuckingAsync(eventSource.Object);

    Assert.Equal(2, stateChanges.Count);
    Assert.True(stateChanges[0].IsDucking);  // Start ducking
    Assert.False(stateChanges[1].IsDucking); // Stop ducking
  }

  [Fact]
  public async Task StopAllDuckingAsync_RestoresFullVolume()
  {
    var service = CreateService();
    var eventSource1 = CreateMockEventSource("source1");
    var eventSource2 = CreateMockEventSource("source2");

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    await service.StartDuckingAsync(eventSource1.Object);
    await service.StartDuckingAsync(eventSource2.Object);

    await service.StopAllDuckingAsync();

    Assert.False(service.IsDucking);
    Assert.Equal(100f, service.CurrentDuckLevel);
    Assert.Equal(0, service.ActiveEventCount);
  }

  [Fact]
  public void GetPriority_ReturnsDefaultForEventSource()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    var priority = service.GetPriority(eventSource.Object);

    Assert.Equal(DuckingService.DefaultEventPriority, priority);
  }

  [Fact]
  public void GetPriority_ReturnsDefaultForPrimarySource()
  {
    var service = CreateService();
    var primarySource = CreateMockPrimarySource();

    var priority = service.GetPriority(primarySource.Object);

    Assert.Equal(DuckingService.DefaultPrimaryPriority, priority);
  }

  [Fact]
  public void SetPriority_ThrowsOnNullSource()
  {
    var service = CreateService();

    Assert.Throws<ArgumentNullException>(
      () => service.SetPriority(null!, 5));
  }

  [Fact]
  public void SetPriority_ThrowsOnOutOfRangePriority()
  {
    var service = CreateService();
    var source = CreateMockEventSource();

    Assert.Throws<ArgumentOutOfRangeException>(
      () => service.SetPriority(source.Object, 0));

    Assert.Throws<ArgumentOutOfRangeException>(
      () => service.SetPriority(source.Object, 11));
  }

  [Fact]
  public void SetPriority_SetsPriority()
  {
    var service = CreateService();
    var source = CreateMockEventSource();

    service.SetPriority(source.Object, 7);

    Assert.Equal(7, service.GetPriority(source.Object));
  }

  [Fact]
  public async Task GetActiveEventsByPriority_ReturnsSortedList()
  {
    var service = CreateService();
    var lowPriority = CreateMockEventSource("low");
    var highPriority = CreateMockEventSource("high");
    var mediumPriority = CreateMockEventSource("medium");

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    service.SetPriority(lowPriority.Object, 3);
    service.SetPriority(highPriority.Object, 9);
    service.SetPriority(mediumPriority.Object, 5);

    await service.StartDuckingAsync(lowPriority.Object);
    await service.StartDuckingAsync(highPriority.Object);
    await service.StartDuckingAsync(mediumPriority.Object);

    var activeEvents = service.GetActiveEventsByPriority();

    Assert.Equal(3, activeEvents.Count);
    Assert.Same(highPriority.Object, activeEvents[0]);
    Assert.Same(mediumPriority.Object, activeEvents[1]);
    Assert.Same(lowPriority.Object, activeEvents[2]);
  }

  [Fact]
  public async Task DuckingLevelChanged_IsRaisedDuringFade()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();
    var levelChanges = new List<DuckingLevelChangedEventArgs>();

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    service.DuckingLevelChanged += (_, args) => levelChanges.Add(args);

    await service.StartDuckingAsync(eventSource.Object);

    Assert.NotEmpty(levelChanges);
    Assert.True(levelChanges.Last().TransitionComplete);
  }

  [Fact]
  public async Task FadeSmooth_ProducesMultipleLevelChanges()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();
    var levelChanges = new List<DuckingLevelChangedEventArgs>();

    _defaultOptions.DuckingPolicy = DuckingPolicy.FadeSmooth;
    _defaultOptions.DuckingAttackMs = 100; // Short for testing

    service.DuckingLevelChanged += (_, args) => levelChanges.Add(args);

    await service.StartDuckingAsync(eventSource.Object);

    // FadeSmooth should produce multiple level changes
    Assert.True(levelChanges.Count > 1);
    Assert.True(levelChanges.Last().TransitionComplete);
  }

  [Fact]
  public async Task FadeQuick_ProducesSomeLevelChanges()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();
    var levelChanges = new List<DuckingLevelChangedEventArgs>();

    _defaultOptions.DuckingPolicy = DuckingPolicy.FadeQuick;
    _defaultOptions.DuckingAttackMs = 100;

    service.DuckingLevelChanged += (_, args) => levelChanges.Add(args);

    await service.StartDuckingAsync(eventSource.Object);

    Assert.NotEmpty(levelChanges);
    Assert.True(levelChanges.Last().TransitionComplete);
  }

  [Fact]
  public void Dispose_ClearsActiveEvents()
  {
    var service = CreateService();

    service.Dispose();

    Assert.Equal(0, service.ActiveEventCount);
  }

  [Fact]
  public async Task StartDuckingAsync_ThrowsWhenDisposed()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    service.Dispose();

    await Assert.ThrowsAsync<ObjectDisposedException>(
      () => service.StartDuckingAsync(eventSource.Object));
  }

  [Fact]
  public async Task StopDuckingAsync_ThrowsWhenDisposed()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    service.Dispose();

    await Assert.ThrowsAsync<ObjectDisposedException>(
      () => service.StopDuckingAsync(eventSource.Object));
  }

  [Fact]
  public async Task StopAllDuckingAsync_ThrowsWhenDisposed()
  {
    var service = CreateService();

    service.Dispose();

    await Assert.ThrowsAsync<ObjectDisposedException>(
      () => service.StopAllDuckingAsync());
  }

  [Fact]
  public void Dispose_CanBeCalledMultipleTimes()
  {
    var service = CreateService();

    service.Dispose();
    service.Dispose(); // Should not throw
  }

  [Fact]
  public async Task DuplicateStartDucking_DoesNotAddDuplicateEvents()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource("same-source");

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    await service.StartDuckingAsync(eventSource.Object);
    await service.StartDuckingAsync(eventSource.Object);

    Assert.Equal(1, service.ActiveEventCount);
  }

  [Fact]
  public async Task StopDucking_ForNonActiveEvent_DoesNotThrow()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    // Stop without starting should not throw
    await service.StopDuckingAsync(eventSource.Object);

    Assert.Equal(0, service.ActiveEventCount);
  }
}
