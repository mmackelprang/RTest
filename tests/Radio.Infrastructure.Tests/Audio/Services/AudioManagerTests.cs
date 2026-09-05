using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Configuration.Abstractions;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Tests.Audio.Services;

public class AudioManagerTests : IAsyncDisposable
{
  private readonly Mock<ILogger<AudioManager>> _loggerMock;
  private readonly Mock<IAudioEngine> _engineMock;
  private readonly Mock<IAudioSourceFactory> _sourceFactoryMock;
  private readonly Mock<IMasterMixer> _mixerMock;
  private readonly AudioManager _sut;

  public AudioManagerTests()
  {
    _loggerMock = new Mock<ILogger<AudioManager>>();
    _engineMock = new Mock<IAudioEngine>();
    _sourceFactoryMock = new Mock<IAudioSourceFactory>();
    _mixerMock = new Mock<IMasterMixer>();

    _engineMock.Setup(e => e.GetMasterMixer()).Returns(_mixerMock.Object);
    _engineMock.Setup(e => e.IsReady).Returns(true);
    _mixerMock.Setup(m => m.GetActiveSources()).Returns(Array.Empty<IAudioSource>());

    _sut = new AudioManager(
      _loggerMock.Object,
      _engineMock.Object,
      _sourceFactoryMock.Object);
  }

  public async ValueTask DisposeAsync()
  {
    await _sut.DisposeAsync();
    GC.SuppressFinalize(this);
  }

  // --- Source Management Tests ---

  [Fact]
  public void ActiveSource_Initially_IsNull()
  {
    Assert.Null(_sut.ActiveSource);
  }

  [Fact]
  public async Task SwitchSourceAsync_SetsActiveSource()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");

    // Act
    await _sut.SwitchSourceAsync(source.Object);

    // Assert
    Assert.Equal(source.Object, _sut.ActiveSource);
  }

  [Fact]
  public async Task SwitchSourceAsync_StopsOldSource()
  {
    // Arrange
    var oldSource = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    var newSource = CreateMockPrimarySource(AudioSourceType.Bluetooth, "BT");

    _mixerMock.Setup(m => m.GetActiveSources())
      .Returns(new[] { oldSource.Object });

    await _sut.SwitchSourceAsync(oldSource.Object);

    // Act
    await _sut.SwitchSourceAsync(newSource.Object);

    // Assert
    oldSource.Verify(s => s.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    Assert.Equal(newSource.Object, _sut.ActiveSource);
  }

  [Fact]
  public async Task SwitchSourceAsync_AddsNewSourceToMixer()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");

    // Act
    await _sut.SwitchSourceAsync(source.Object);

    // Assert
    _mixerMock.Verify(m => m.AddSource(source.Object), Times.Once);
  }

  [Fact]
  public async Task SwitchSourceAsync_AutoPlaysRadioSource()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    source.Setup(s => s.State).Returns(AudioSourceState.Ready);

    // Act
    await _sut.SwitchSourceAsync(source.Object);

    // Assert
    source.Verify(s => s.PlayAsync(It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task SwitchSourceAsync_DoesNotAutoPlayFilePlayer()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.FilePlayer, "File Player");
    source.Setup(s => s.State).Returns(AudioSourceState.Ready);

    // Act
    await _sut.SwitchSourceAsync(source.Object);

    // Assert
    source.Verify(s => s.PlayAsync(It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task SwitchSourceAsync_ThrowsForNonPrimarySource()
  {
    // Arrange
    var source = new Mock<IAudioSource>();
    source.Setup(s => s.Category).Returns(AudioSourceCategory.Event);

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(
      () => _sut.SwitchSourceAsync(source.Object));
  }

  [Fact]
  public async Task GetOrCreateSourceAsync_CachesSource()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    _sourceFactoryMock.Setup(f => f.CreateSource(AudioSourceType.Radio))
      .Returns(source.Object);

    // Act
    var result1 = await _sut.GetOrCreateSourceAsync(AudioSourceType.Radio);
    var result2 = await _sut.GetOrCreateSourceAsync(AudioSourceType.Radio);

    // Assert
    Assert.Same(result1, result2);
    _sourceFactoryMock.Verify(f => f.CreateSource(AudioSourceType.Radio), Times.Once);
  }

  [Fact]
  public async Task GetOrCreateSourceAsync_ReturnsNullForUnsupportedType()
  {
    // Arrange
    _sourceFactoryMock.Setup(f => f.CreateSource(It.IsAny<AudioSourceType>()))
      .Throws<ArgumentOutOfRangeException>();

    // Act
    var result = await _sut.GetOrCreateSourceAsync(AudioSourceType.TestTone, switchToSource: false);

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetOrCreateSourceAsync_SwitchesToSourceByDefault()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    _sourceFactoryMock.Setup(f => f.CreateSource(AudioSourceType.Radio))
      .Returns(source.Object);

    // Act
    await _sut.GetOrCreateSourceAsync(AudioSourceType.Radio);

    // Assert
    Assert.Equal(source.Object, _sut.ActiveSource);
  }

  [Fact]
  public async Task GetOrCreateSourceAsync_WithSwitchFalse_DoesNotSwitch()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    _sourceFactoryMock.Setup(f => f.CreateSource(AudioSourceType.Radio))
      .Returns(source.Object);

    // Act
    await _sut.GetOrCreateSourceAsync(AudioSourceType.Radio, switchToSource: false);

    // Assert — active source should still be null since we didn't switch
    Assert.Null(_sut.ActiveSource);
  }

  [Fact]
  public void GetCachedSource_ReturnsNullIfNotCreated()
  {
    Assert.Null(_sut.GetCachedSource(AudioSourceType.Radio));
  }

  [Fact]
  public async Task GetCachedSource_ReturnsCachedSourceAfterCreation()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    _sourceFactoryMock.Setup(f => f.CreateSource(AudioSourceType.Radio))
      .Returns(source.Object);

    await _sut.GetOrCreateSourceAsync(AudioSourceType.Radio, switchToSource: false);

    // Act
    var cached = _sut.GetCachedSource(AudioSourceType.Radio);

    // Assert
    Assert.Same(source.Object, cached);
  }

  // --- Volume & Mixer Tests ---

  [Fact]
  public void MasterVolume_DelegatesToMixer()
  {
    // Arrange
    _mixerMock.SetupProperty(m => m.MasterVolume, 0.5f);

    // Act
    _sut.MasterVolume = 0.75f;

    // Assert
    _mixerMock.VerifySet(m => m.MasterVolume = 0.75f, Times.Once);
  }

  [Fact]
  public void MasterVolume_ReadsFromMixer()
  {
    _mixerMock.Setup(m => m.MasterVolume).Returns(0.42f);

    Assert.Equal(0.42f, _sut.MasterVolume);
  }

  [Fact]
  public void IsMuted_DelegatesToMixer()
  {
    _mixerMock.SetupProperty(m => m.IsMuted, false);

    _sut.IsMuted = true;

    _mixerMock.VerifySet(m => m.IsMuted = true, Times.Once);
  }

  [Fact]
  public void Balance_DelegatesToMixer()
  {
    _mixerMock.SetupProperty(m => m.Balance, 0.0f);

    _sut.Balance = -0.5f;

    _mixerMock.VerifySet(m => m.Balance = -0.5f, Times.Once);
  }

  // --- Gain Tests ---

  [Fact]
  public void GetSourceGain_ReturnsDefaultWhenNoPersistence()
  {
    // Without persistence service, should return 1.0 default
    Assert.Equal(1.0f, _sut.GetSourceGain(AudioSourceType.Radio));
  }

  [Fact]
  public void GetAllSourceGains_ReturnsEmptyWhenNoPersistence()
  {
    Assert.Empty(_sut.GetAllSourceGains());
  }

  // --- Lifecycle Tests ---

  [Fact]
  public async Task InitializeAsync_InitializesEngine()
  {
    // Arrange
    _engineMock.Setup(e => e.IsReady).Returns(false);

    // Act
    await _sut.InitializeAsync();

    // Assert
    _engineMock.Verify(e => e.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task InitializeAsync_SkipsIfAlreadyReady()
  {
    // Arrange
    _engineMock.Setup(e => e.IsReady).Returns(true);

    // Act
    await _sut.InitializeAsync();

    // Assert
    _engineMock.Verify(e => e.InitializeAsync(It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task InitializeAsync_OnlyRunsOnce()
  {
    // Act
    await _sut.InitializeAsync();
    await _sut.InitializeAsync();

    // Assert — should only try to initialize once
    _engineMock.Verify(e => e.InitializeAsync(It.IsAny<CancellationToken>()), Times.AtMostOnce);
  }

  [Fact]
  public async Task StopAsync_StopsActiveSource()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    await _sut.SwitchSourceAsync(source.Object);

    // Act
    await _sut.StopAsync();

    // Assert
    source.Verify(s => s.StopAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
  }

  [Fact]
  public async Task StopAsync_NoopWhenNoActiveSource()
  {
    // Should not throw
    await _sut.StopAsync();
  }

  [Fact]
  public async Task DisposeAsync_DisposesSourceCache()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    _sourceFactoryMock.Setup(f => f.CreateSource(AudioSourceType.Radio))
      .Returns(source.Object);

    await _sut.GetOrCreateSourceAsync(AudioSourceType.Radio, switchToSource: false);

    // Act
    await _sut.DisposeAsync();

    // Assert
    source.Verify(s => s.DisposeAsync(), Times.Once);
  }

  // --- Concurrent Source Switching ---

  [Fact]
  public async Task SwitchSourceAsync_ConcurrentCalls_SerializedCorrectly()
  {
    // Arrange
    var source1 = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    var source2 = CreateMockPrimarySource(AudioSourceType.Bluetooth, "BT");

    // Act — switch concurrently
    var t1 = _sut.SwitchSourceAsync(source1.Object);
    var t2 = _sut.SwitchSourceAsync(source2.Object);

    await Task.WhenAll(t1, t2);

    // Assert — one of them should be the final active source
    Assert.NotNull(_sut.ActiveSource);
  }

  // --- Ducking handler (PHN-1f C-58) ---

  /// <summary>
  /// Builds an AudioManager wired to a real <see cref="SoundFlowPlaybackService"/> and a mocked
  /// <see cref="IDuckingService"/>, with a primary source already active.
  /// </summary>
  /// <remarks>
  /// ⚠ THE PLAYBACK SERVICE IS REAL, NOT MOCKED, AND THAT IS FORCED RATHER THAN CHOSEN.
  /// AudioManager's constructor takes the CONCRETE SoundFlowPlaybackService — there is no
  /// ISoundFlowPlaybackService in the tree — and ClearDuckingMultiplier is a non-virtual public
  /// method, so Moq can neither substitute it nor record the call. Adding an interface would be a
  /// production seam this row is not scoped to add, so the two tests below instrument the call
  /// differently and each says how.
  ///
  /// Constructing it reaches no hardware: SoundFlowAudioEngine's constructor stores its
  /// collaborators and subscribes to three of their events, and the MiniAudio device is not created
  /// until InitializeAsync, which nothing here calls. SoundFlowPlaybackServiceTransportTests does the
  /// same construction for the same reason.
  /// </remarks>
  private async Task<(AudioManager Manager, Mock<IDuckingService> Ducking,
      SoundFlowPlaybackService Playback, Mock<IPrimaryAudioSource> Source, int IdReads)>
    CreateManagerWithDuckingAsync()
  {
    var ducking = new Mock<IDuckingService>();
    var playback = CreatePlaybackService();

    var manager = new AudioManager(
      _loggerMock.Object,
      _engineMock.Object,
      _sourceFactoryMock.Object,
      playbackService: playback,
      duckingService: ducking.Object);

    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    await manager.SwitchSourceAsync(source.Object);

    return (manager, ducking, playback, source, 0);
  }

  private static SoundFlowPlaybackService CreatePlaybackService()
  {
    var engineOptions = new Mock<IOptions<AudioEngineOptions>>();
    engineOptions.Setup(o => o.Value).Returns(new AudioEngineOptions
    {
      EnableHotPlugDetection = false
    });

    var audioPreferences = new Mock<IOptionsMonitor<AudioPreferences>>();
    audioPreferences.Setup(m => m.CurrentValue).Returns(new AudioPreferences());

    var audioOutputOptions = new Mock<IOptionsMonitor<AudioOutputOptions>>();
    audioOutputOptions.Setup(m => m.CurrentValue).Returns(new AudioOutputOptions());

    var masterMixer = new SoundFlowMasterMixer(Mock.Of<ILogger<SoundFlowMasterMixer>>());
    var deviceManager = new SoundFlowDeviceManager(
      Mock.Of<ILogger<SoundFlowDeviceManager>>(),
      Mock.Of<IConfigurationManager>(),
      audioPreferences.Object,
      audioOutputOptions.Object);

    var engine = new SoundFlowAudioEngine(
      Mock.Of<ILogger<SoundFlowAudioEngine>>(),
      engineOptions.Object,
      masterMixer,
      deviceManager);

    return new SoundFlowPlaybackService(Mock.Of<ILogger<SoundFlowPlaybackService>>(), engine);
  }

  [Fact]
  public async Task ADefaultedTransitionOnAnIsDuckingFalseRaiseStillClearsTheMultiplier()
  {
    // ⛔ THE C-58 PIN, and the whole of C-58 rests on it. DuckingSourceTransition.Started is 0, so it
    // is the value an args object gets when nothing sets the field. If AudioManager's OUTER branch
    // were keyed on Transition rather than on IsDucking, this raise — IsDucking false, Transition
    // never set — would take the "started" arm, skip ClearDuckingMultiplier, and leave the radio
    // STUCK DUCKED. That is a worse failure than the mislabelled log line the field exists to fix.
    //
    // ⚠ TWO INSTRUMENTS, because neither alone is a proof and the reason is stated in
    // CreateManagerWithDuckingAsync's remark: ClearDuckingMultiplier is non-virtual on a concrete
    // class, so Moq cannot record it.
    //
    //   (1) The ACTIVE SOURCE'S Id getter. `_playbackService.ClearDuckingMultiplier(_activeSource.Id)`
    //       is the only statement in the handler that reads it, and C# evaluates a call's arguments
    //       only as part of making the call — so a read during the raise means the statement ran.
    //   (2) A DISPOSED playback service. ClearDuckingMultiplier opens with ThrowIfDisposed(), so an
    //       ObjectDisposedException escaping the raise proves the method body was ENTERED, not merely
    //       that its argument was evaluated. (1) alone could not distinguish an argument evaluated
    //       for a call that was then optimised away; (2) alone could not run before disposal.
    //
    // MUTATION (§2.1): key AudioManager's outer branch on Transition and both halves red.
    var (manager, ducking, playback, source, _) = await CreateManagerWithDuckingAsync();
    await using (manager)
    {
      var idReads = 0;
      var id = source.Object.Id;
      source.Setup(s => s.Id).Returns(() => { idReads++; return id; });

      // ⚠ Transition is DELIBERATELY NOT SET. That is the whole point: it defaults to Started.
      var args = new DuckingStateChangedEventArgs
      {
        IsDucking = false,
        TriggeringSource = null,
        DuckLevel = 100f,
        ActiveEventCount = 0
      };

      Assert.Equal(DuckingSourceTransition.Started, args.Transition);

      ducking.Raise(d => d.DuckingStateChanged += null, ducking.Object, args);

      Assert.Equal(1, idReads);

      // …and the call really reached ClearDuckingMultiplier's body.
      playback.Dispose();
      var thrown = Record.Exception(
        () => ducking.Raise(d => d.DuckingStateChanged += null, ducking.Object, args));
      Assert.IsType<ObjectDisposedException>(thrown);
    }
  }

  [Fact]
  public async Task AnEndedRaiseWithOthersRemainingDoesNotClearTheDuckingMultiplier()
  {
    // The mirror of the pin above, and the hazard PHN-1f's DuckingService change would have created
    // without it: since this row StopDuckingAsync raises for EVERY source that leaves, so a
    // priority-8 blocker ending while a priority-3 announcement keeps ducking now reaches this
    // handler. IsDucking is TRUE on that raise — the aggregate is still ducking — and clearing the
    // multiplier there would restore the radio to full volume MID-ANNOUNCEMENT.
    //
    // Instrumented by the disposed playback service alone, because the assertion is a NEGATIVE: if
    // ClearDuckingMultiplier were reached it would throw, and the absence of the throw is the claim.
    // The Id-read counter is asserted too, so "nothing happened at all" cannot pass as "the right
    // thing happened".
    //
    // MUTATION: drop the `return;` from the IsDucking arm of OnDuckingStateChanged — so the raise
    // falls through into the ducking-ended block — and this throws.
    var (manager, ducking, playback, source, _) = await CreateManagerWithDuckingAsync();
    await using (manager)
    {
      var idReads = 0;
      var id = source.Object.Id;
      source.Setup(s => s.Id).Returns(() => { idReads++; return id; });

      playback.Dispose();

      var args = new DuckingStateChangedEventArgs
      {
        IsDucking = true,
        TriggeringSource = null,
        DuckLevel = 20f,
        ActiveEventCount = 1,
        Transition = DuckingSourceTransition.Ended,
        TriggeringSourcePriority = 8
      };

      var thrown = Record.Exception(
        () => ducking.Raise(d => d.DuckingStateChanged += null, ducking.Object, args));

      Assert.Null(thrown);
      Assert.Equal(0, idReads);
    }
  }

  // --- Helper Methods ---

  private static Mock<IPrimaryAudioSource> CreateMockPrimarySource(
    AudioSourceType type, string name)
  {
    var source = new Mock<IPrimaryAudioSource>();
    source.Setup(s => s.Type).Returns(type);
    source.Setup(s => s.Name).Returns(name);
    source.Setup(s => s.Id).Returns(Guid.NewGuid().ToString());
    source.Setup(s => s.Category).Returns(AudioSourceCategory.Primary);
    source.Setup(s => s.State).Returns(AudioSourceState.Ready);
    source.As<IAsyncDisposable>()
      .Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);
    source.As<IAudioSource>()
      .Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);

    // Set up StateChanged event
    source.SetupAdd(s => s.StateChanged += It.IsAny<EventHandler<AudioSourceStateChangedEventArgs>>());
    source.SetupRemove(s => s.StateChanged -= It.IsAny<EventHandler<AudioSourceStateChangedEventArgs>>());

    return source;
  }
}
