using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;

namespace Radio.Infrastructure.Tests.Audio.Fingerprinting;

/// <summary>
/// Tests for song change detection in BackgroundIdentificationService.
/// </summary>
public class BackgroundIdentificationServiceSongChangeTests
{
  private readonly BackgroundIdentificationService _service;
  private readonly FingerprintingOptions _options;
  private readonly List<SongChangedEventArgs> _songChangedEvents = new();
  private readonly List<TrackIdentifiedEventArgs> _trackIdentifiedEvents = new();

  public BackgroundIdentificationServiceSongChangeTests()
  {
    _options = new FingerprintingOptions
    {
      Enabled = false, // Disable background loop for unit tests
      MinimumSecondsBetweenSongChanges = 20
    };

    var serviceProvider = new ServiceCollection().BuildServiceProvider();
    var logger = new Mock<ILogger<BackgroundIdentificationService>>();

    _service = new BackgroundIdentificationService(
      logger.Object,
      serviceProvider,
      Options.Create(_options));

    _service.SongChanged += (_, e) => _songChangedEvents.Add(e);
    _service.TrackIdentified += (_, e) => _trackIdentifiedEvents.Add(e);
  }

  [Fact]
  public void ResetSongChangeState_DoesNotThrow()
  {
    // Act & Assert — should not throw
    _service.ResetSongChangeState();
  }

  [Fact]
  public void SongChanged_Event_Is_Accessible()
  {
    // Verify the event exists and can be subscribed to
    var raised = false;
    _service.SongChanged += (_, _) => raised = true;
    Assert.False(raised); // Not raised yet
  }

  [Fact]
  public void ResetSongChangeState_CanBeCalledMultipleTimes()
  {
    // Act & Assert — multiple resets should not throw
    _service.ResetSongChangeState();
    _service.ResetSongChangeState();
    _service.ResetSongChangeState();
  }

  [Fact]
  public void RequestImmediateIdentification_DoesNotThrow()
  {
    // Act & Assert — should not throw even when no delay is active
    _service.RequestImmediateIdentification();
  }
}
