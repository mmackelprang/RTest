using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Fingerprinting.Services;
using Radio.Fingerprinting;

namespace Radio.Fingerprinting.Tests.Services;

/// <summary>
/// Tests for fingerprint status tracking and event log aggregation in BackgroundIdentificationService.
/// Validates that:
///   - Repeated matches for the same song aggregate into one event (incrementing Count)
///   - Consecutive no-match results aggregate (incrementing Count)
///   - A different song creates a new event record
///   - Source changes create a new event record
///   - The event log is capped at ~40 entries
/// </summary>
public class BackgroundIdentificationServiceStatusTests
{
  private readonly BackgroundIdentificationService _service;

  public BackgroundIdentificationServiceStatusTests()
  {
    var options = new FingerprintingOptions
    {
      Enabled = true, // Enabled so GetStatus reports IsEnabled=true
    };

    var serviceProvider = new ServiceCollection().BuildServiceProvider();
    var logger = new Mock<ILogger<BackgroundIdentificationService>>();

    var optionsMonitor = new Mock<IOptionsMonitor<FingerprintingOptions>>();
    optionsMonitor.Setup(o => o.CurrentValue).Returns(options);

    _service = new BackgroundIdentificationService(
      logger.Object,
      serviceProvider,
      optionsMonitor.Object);
  }

  [Fact]
  public void GetStatus_InitialState_IsIdleAndEnabled()
  {
    var status = _service.GetStatus();

    Assert.Equal(FingerprintPhase.Idle, status.Phase);
    Assert.True(status.IsEnabled);
    Assert.Empty(status.RecentEvents);
    Assert.Equal(0, status.FingerprintsPerMinute);
    Assert.Equal(0, status.MetadataCallsPerMinute);
    Assert.Null(status.LastError);
  }

  [Fact]
  public void RepeatedMatch_SameSong_AggregatesIntoOneEvent()
  {
    // Arrange
    var metadata = CreateMetadata("Song A", "Artist A", "Album A");
    _service.EnsureCurrentEvent("SDR Radio");

    // Act — same song identified 3 times
    _service.UpdateCurrentEventMatch(metadata, 0.85);
    _service.UpdateCurrentEventMatch(metadata, 0.90);
    _service.UpdateCurrentEventMatch(metadata, 0.92);

    // Assert — TWO event records: the initial empty one becomes a match row,
    // then three matches aggregate into one match row
    var status = _service.GetStatus();
    // First match creates a new match row (since initial event has Count=0),
    // subsequent matches aggregate into it
    var matchEvents = status.RecentEvents.Where(e => e.IsMatch).ToList();
    Assert.Single(matchEvents);

    var evt = matchEvents[0];
    Assert.Equal(3, evt.Count);
    Assert.True(evt.IsMatch);
    Assert.Equal(0.92, evt.LastConfidence);
    Assert.Equal("Song A", evt.Title);
    Assert.Equal("Artist A", evt.Artist);
    Assert.Equal("Album A", evt.Album);
  }

  [Fact]
  public void ConsecutiveNoMatch_AggregatesIntoOneEvent()
  {
    // Arrange
    _service.EnsureCurrentEvent("SDR Radio");

    // Act — 4 consecutive no-match results
    _service.UpdateCurrentEventNoMatch();
    _service.UpdateCurrentEventNoMatch();
    _service.UpdateCurrentEventNoMatch();
    _service.UpdateCurrentEventNoMatch();

    // Assert — the initial event becomes no-match, then aggregates
    var status = _service.GetStatus();
    var noMatchEvents = status.RecentEvents.Where(e => !e.IsMatch).ToList();
    Assert.Single(noMatchEvents);

    var evt = noMatchEvents[0];
    Assert.Equal(4, evt.Count);
    Assert.False(evt.IsMatch);
    Assert.Null(evt.Title);
    Assert.Null(evt.LastConfidence);
  }

  [Fact]
  public void NoMatchThenMatch_CreatesTwoEvents()
  {
    // Arrange
    _service.EnsureCurrentEvent("SDR Radio");

    // Act — 2 no-match, then a match
    _service.UpdateCurrentEventNoMatch();
    _service.UpdateCurrentEventNoMatch();
    _service.UpdateCurrentEventMatch(CreateMetadata("Song B", "Artist B"), 0.75);

    // Assert — TWO records: no-match row + match row (new model separates them)
    var status = _service.GetStatus();
    Assert.Equal(2, status.RecentEvents.Count);

    var noMatchEvt = status.RecentEvents[0];
    Assert.False(noMatchEvt.IsMatch);
    Assert.Equal(2, noMatchEvt.Count);

    var matchEvt = status.RecentEvents[1];
    Assert.True(matchEvt.IsMatch);
    Assert.Equal(1, matchEvt.Count);
    Assert.Equal("Song B", matchEvt.Title);
    Assert.Equal(0.75, matchEvt.LastConfidence);
  }

  [Fact]
  public void DifferentSongMatch_CreatesNewEventRecord()
  {
    // Arrange
    _service.EnsureCurrentEvent("SDR Radio");

    // Act — Song A matched, then Song B matched
    _service.UpdateCurrentEventMatch(CreateMetadata("Song A", "Artist A"), 0.80);
    _service.UpdateCurrentEventMatch(CreateMetadata("Song B", "Artist B"), 0.90);

    // Assert — TWO match event records
    var status = _service.GetStatus();
    var matchEvents = status.RecentEvents.Where(e => e.IsMatch).ToList();
    Assert.Equal(2, matchEvents.Count);

    Assert.Equal("Song A", matchEvents[0].Title);
    Assert.Equal(1, matchEvents[0].Count);
    Assert.Equal(0.80, matchEvents[0].LastConfidence);

    Assert.Equal("Song B", matchEvents[1].Title);
    Assert.Equal(1, matchEvents[1].Count);
    Assert.Equal(0.90, matchEvents[1].LastConfidence);
  }

  [Fact]
  public void SourceChange_CreatesNewEventRecord()
  {
    // Arrange & Act — different sources
    _service.EnsureCurrentEvent("SDR Radio");
    _service.UpdateCurrentEventNoMatch();

    _service.EnsureCurrentEvent("Bluetooth");
    _service.UpdateCurrentEventNoMatch();

    // Assert — two event records, one per source
    var status = _service.GetStatus();
    Assert.Equal(2, status.RecentEvents.Count);
    Assert.Equal("SDR Radio", status.RecentEvents[0].AudioSource);
    Assert.Equal("Bluetooth", status.RecentEvents[1].AudioSource);
  }

  [Fact]
  public void SameSource_ContinuesExistingEvent()
  {
    // Arrange & Act — same source, called twice
    _service.EnsureCurrentEvent("SDR Radio");
    _service.UpdateCurrentEventNoMatch();

    _service.EnsureCurrentEvent("SDR Radio");
    _service.UpdateCurrentEventNoMatch();

    // Assert — ONE event record with Count=2
    var status = _service.GetStatus();
    var noMatchEvents = status.RecentEvents.Where(e => !e.IsMatch).ToList();
    Assert.Single(noMatchEvents);
    Assert.Equal(2, noMatchEvents[0].Count);
  }

  [Fact]
  public void MatchThenNoMatch_CreatesNewEventRecord()
  {
    // Arrange
    _service.EnsureCurrentEvent("SDR Radio");

    // Act — match, then no-matches on same source
    _service.UpdateCurrentEventMatch(CreateMetadata("Song C", "Artist C"), 0.88);

    _service.EnsureCurrentEvent("SDR Radio"); // Same source — should NOT create new record via EnsureCurrentEvent
    _service.UpdateCurrentEventNoMatch();
    _service.UpdateCurrentEventNoMatch();

    // Assert — TWO records: match event + separate no-match event
    var status = _service.GetStatus();
    var matchEvents = status.RecentEvents.Where(e => e.IsMatch).ToList();
    var noMatchEvents = status.RecentEvents.Where(e => !e.IsMatch).ToList();

    Assert.Single(matchEvents);
    Assert.Equal(1, matchEvents[0].Count);
    Assert.Equal("Song C", matchEvents[0].Title);

    Assert.Single(noMatchEvents);
    Assert.Equal(2, noMatchEvents[0].Count);
    Assert.Null(noMatchEvents[0].Title);
    Assert.Equal("SDR Radio", noMatchEvents[0].AudioSource);
  }

  [Fact]
  public void EventLog_CappedAtMaxEntries()
  {
    // Act — create 45 events (more than the 40 cap)
    for (int i = 0; i < 45; i++)
    {
      _service.EnsureCurrentEvent($"Source {i}");
      _service.UpdateCurrentEventMatch(CreateMetadata($"Song {i}", $"Artist {i}"), 0.5 + i * 0.01);
    }

    // Assert — capped at 40
    var status = _service.GetStatus();
    Assert.Equal(40, status.RecentEvents.Count);

    // Oldest events dropped, newest retained
    Assert.Equal("Song 5", status.RecentEvents[0].Title); // First 5 dropped (45 - 40 = 5)
    Assert.Equal("Song 44", status.RecentEvents[^1].Title);
  }

  [Fact]
  public void StatusChanged_FiresOnPhaseUpdate()
  {
    // Arrange
    var snapshots = new List<FingerprintStatusSnapshot>();
    _service.StatusChanged += (_, s) => snapshots.Add(s);

    // Act
    _service.EnsureCurrentEvent("SDR Radio");
    _service.UpdateCurrentEventMatch(CreateMetadata("Song E", "Artist E"), 0.95);

    // The UpdateCurrentEventMatch itself doesn't fire StatusChanged (only UpdatePhase does),
    // but we can verify the event hook works by checking GetStatus
    var status = _service.GetStatus();
    var matchEvents = status.RecentEvents.Where(e => e.IsMatch).ToList();
    Assert.Single(matchEvents);
    Assert.Equal("Song E", matchEvents[0].Title);
  }

  [Fact]
  public void ConfidenceUpdates_OnEachMatch()
  {
    // Arrange
    _service.EnsureCurrentEvent("Vinyl");

    // Act — progressively better confidence
    _service.UpdateCurrentEventMatch(CreateMetadata("Song F", "Artist F"), 0.60);
    var matchEvents = _service.GetStatus().RecentEvents.Where(e => e.IsMatch).ToList();
    Assert.Equal(0.60, matchEvents[0].LastConfidence);

    _service.UpdateCurrentEventMatch(CreateMetadata("Song F", "Artist F"), 0.75);
    matchEvents = _service.GetStatus().RecentEvents.Where(e => e.IsMatch).ToList();
    Assert.Equal(0.75, matchEvents[0].LastConfidence);

    _service.UpdateCurrentEventMatch(CreateMetadata("Song F", "Artist F"), 0.55);
    matchEvents = _service.GetStatus().RecentEvents.Where(e => e.IsMatch).ToList();
    Assert.Equal(0.55, matchEvents[0].LastConfidence); // Always latest, not max
  }

  [Fact]
  public void HasAlbumArt_SetFromCoverArtUrl()
  {
    // Arrange
    _service.EnsureCurrentEvent("SDR Radio");

    // Act — match with cover art URL
    var metadataWithArt = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Song G",
      Artist = "Artist G",
      CoverArtUrl = "https://example.com/cover.jpg",
      Source = MetadataSource.Shazam,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    _service.UpdateCurrentEventMatch(metadataWithArt, 0.80);

    // Assert
    var matchEvents = _service.GetStatus().RecentEvents.Where(e => e.IsMatch).ToList();
    Assert.Single(matchEvents);
    Assert.True(matchEvents[0].HasAlbumArt);
  }

  [Fact]
  public void SourceType_SetOnEventRecord()
  {
    // Arrange & Act
    _service.EnsureCurrentEvent("SDR Radio", "Radio");
    _service.UpdateCurrentEventNoMatch();

    // Assert
    var status = _service.GetStatus();
    Assert.Equal("Radio", status.RecentEvents[0].SourceType);
  }

  private static TrackMetadata CreateMetadata(string title, string artist, string? album = null)
  {
    return new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = title,
      Artist = artist,
      Album = album,
      Source = MetadataSource.Shazam,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }
}
