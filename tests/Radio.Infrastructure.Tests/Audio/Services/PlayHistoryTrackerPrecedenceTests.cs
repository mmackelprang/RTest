using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Events;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Fingerprinting;
using Radio.Fingerprinting.Services;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// AUD-19: History follows the per-field metadata rule AUD-1 put on now-playing. Where the
/// source (AVRCP on Bluetooth, tags on the file player) supplied a field, the History row keeps
/// it; an identification fills only the fields the source left missing — and a fingerprint
/// title that differs from the source's does not start a new History row.
/// </summary>
/// <remarks>
/// Every repository mock completes synchronously, so each async-void handler in
/// <see cref="PlayHistoryTracker"/> has finished by the time the event raise returns.
/// </remarks>
public class PlayHistoryTrackerPrecedenceTests
{
  private const string ArtUrl = "/api/albumart/0f924e4c2dd0504e.jpg";

  private readonly Mock<IPlayHistoryRepository> _history = new();
  private readonly Mock<ITrackMetadataRepository> _trackRows = new();
  private readonly Mock<IBluetoothService> _bluetooth = new();
  private readonly Mock<IPrimaryAudioSource> _source = new();
  private readonly Dictionary<string, object> _sourceMetadata = new();
  private readonly BackgroundIdentificationService _identification;

  // In-memory History: entry id -> entry, in insertion order.
  private readonly List<PlayHistoryEntry> _entries = [];
  private readonly Dictionary<string, TrackMetadata> _storedRows = new();
  private readonly List<string> _finalized = [];

  public PlayHistoryTrackerPrecedenceTests()
  {
    _source.SetupGet(s => s.Name).Returns("Test Source");
    _source.SetupGet(s => s.Metadata).Returns(_sourceMetadata);

    _history.Setup(r => r.RecordPlayAsync(It.IsAny<PlayHistoryEntry>(), It.IsAny<CancellationToken>()))
      .Callback<PlayHistoryEntry, CancellationToken>((e, _) => _entries.Add(e))
      .Returns(Task.CompletedTask);
    _history.Setup(r => r.UpdateAsync(It.IsAny<PlayHistoryEntry>(), It.IsAny<CancellationToken>()))
      .Callback<PlayHistoryEntry, CancellationToken>((e, _) =>
      {
        int index = _entries.FindIndex(x => x.Id == e.Id);
        _entries[index] = e;
      })
      .ReturnsAsync(true);
    _history.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string id, CancellationToken _) => _entries.FirstOrDefault(e => e.Id == id));
    _history.Setup(r => r.GetRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((int n, CancellationToken _) =>
        (IReadOnlyList<PlayHistoryEntry>)_entries.AsEnumerable().Reverse().Take(n).ToList());
    _history.Setup(r => r.GetRecentUnidentifiedAsync(It.IsAny<PlaySource>(), It.IsAny<int>(),
        It.IsAny<CancellationToken>()))
      .ReturnsAsync((PlaySource source, int _, CancellationToken _) =>
        _entries.LastOrDefault(e => e.Source == source && !e.WasIdentified));
    _history.Setup(r => r.ExistsRecentlyPlayedAsync(It.IsAny<string>(), It.IsAny<string>(),
        It.IsAny<int>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(false);
    _history.Setup(r => r.FinalizeEntryAsync(It.IsAny<string>(), It.IsAny<DateTime>(),
        It.IsAny<CancellationToken>()))
      .Callback<string, DateTime, CancellationToken>((id, endedAt, _) =>
      {
        _finalized.Add(id);
        int index = _entries.FindIndex(x => x.Id == id);
        if (index >= 0)
        {
          _entries[index] = _entries[index] with { EndedAt = endedAt };
        }
      })
      .ReturnsAsync(true);

    _trackRows.Setup(r => r.StoreAsync(It.IsAny<TrackMetadata>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((TrackMetadata t, CancellationToken _) =>
      {
        _storedRows[t.Id] = t;
        return t;
      });

    Mock<IOptionsMonitor<FingerprintingOptions>> options = new();
    options.Setup(o => o.CurrentValue).Returns(new FingerprintingOptions());
    _identification = new BackgroundIdentificationService(
      NullLogger<BackgroundIdentificationService>.Instance,
      new ServiceCollection().BuildServiceProvider(),
      options.Object);
  }

  private PlayHistoryTracker BuildTracker(AudioSourceType type)
  {
    _source.SetupGet(s => s.Type).Returns(type);

    ServiceCollection services = new();
    services.AddSingleton(_history.Object);
    services.AddSingleton(_trackRows.Object);
    IServiceScopeFactory scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

    PlayHistoryTracker tracker = new(
      NullLogger<PlayHistoryTracker>.Instance,
      scopes,
      () => _source.Object,
      _bluetooth.Object,
      _identification);
    tracker.SubscribeToSource(_source.Object);
    return tracker;
  }

  private static TrackMetadata Identified(
    string title, string artist, string? album = null, string? art = null) => new()
  {
    Id = Guid.NewGuid().ToString(),
    Title = title,
    Artist = artist,
    Album = album,
    CoverArtUrl = art,
    Genre = "Punk",
    Source = MetadataSource.Shazam,
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
  };

  private void RaiseAvrcp(string title, string artist, string album)
  {
    // Pass "" to model a phone that publishes no track names: the event still arrives.
    // The Bluetooth source writes AVRCP into its own metadata; History reads it from there.
    _sourceMetadata[StandardMetadataKeys.Title] = title;
    _sourceMetadata[StandardMetadataKeys.Artist] = artist;
    _sourceMetadata[StandardMetadataKeys.Album] = album;
    _sourceMetadata["Device"] = "Pixel 10 Pro XL";
    _bluetooth.Raise(b => b.MetadataChanged += null, _bluetooth.Object,
      new BluetoothPlaybackMetadata { Title = title, Artist = artist, Album = album });
  }

  private void RaisePlaying() =>
    _source.Raise(s => s.StateChanged += null, _source.Object, new AudioSourceStateChangedEventArgs
    {
      PreviousState = AudioSourceState.Ready,
      NewState = AudioSourceState.Playing,
      SourceId = "src"
    });

  private void SetFileTags(string title, string artist, string album)
  {
    _sourceMetadata[StandardMetadataKeys.Title] = title;
    _sourceMetadata[StandardMetadataKeys.Artist] = artist;
    _sourceMetadata[StandardMetadataKeys.Album] = album;
    _sourceMetadata[StandardMetadataKeys.AlbumArtUrl] = StandardMetadataKeys.DefaultAlbumArtUrl;
  }

  private void RaiseSongChanged(TrackMetadata track) =>
    _identification.RaiseSongChangedForTesting(new SongChangedEventArgs(null, track, 0.8));

  private void RaiseIdentified(TrackMetadata track) =>
    _identification.RaiseTrackIdentifiedForTesting(new TrackIdentifiedEventArgs(track, 0.8));

  // --- Bluetooth -----------------------------------------------------------------------------

  [Fact]
  public void Bluetooth_FingerprintTitleDiffersFromAvrcp_KeepsTheAvrcpRow_AndFillsOnlyMissingFields()
  {
    using PlayHistoryTracker tracker = BuildTracker(AudioSourceType.Bluetooth);
    RaiseAvrcp("Basket Case", "Green Day", album: "");
    Assert.Single(_entries);
    string avrcpEntryId = _entries[0].Id;

    TrackMetadata shazam = Identified("Basket Case (Remastered 2024)", "Green Day", "Dookie", ArtUrl);
    RaiseSongChanged(shazam);

    PlayHistoryEntry entry = Assert.Single(_entries);
    Assert.Equal(avrcpEntryId, entry.Id);
    Assert.Empty(_finalized);
    Assert.Equal("Basket Case", entry.Track!.Title);
    Assert.Equal("Green Day", entry.Track.Artist);
    Assert.Equal("Dookie", entry.Track.Album);           // AVRCP sent "" — filled
    Assert.Equal(ArtUrl, entry.Track.CoverArtUrl);       // AVRCP sent none — filled
    Assert.Equal(MetadataSource.Avrcp, entry.MetadataSource); // the title is AVRCP's
    Assert.True(entry.WasIdentified);
    Assert.Equal(0.8, entry.IdentificationConfidence);

    // The merged row is History's own, so it must not be written under the identification's id.
    Assert.NotEqual(shazam.Id, entry.TrackMetadataId);
    Assert.Equal("Basket Case", _storedRows[entry.TrackMetadataId!].Title);
  }

  [Fact]
  public void Bluetooth_SameSong_KeepsTheAvrcpAlbum_RatherThanTheFingerprintAlbum()
  {
    using PlayHistoryTracker tracker = BuildTracker(AudioSourceType.Bluetooth);
    RaiseAvrcp("Heart and Soul", "Huey Lewis & The News", album: "Greatest Hits");

    RaiseSongChanged(Identified("Heart and Soul", "Huey Lewis & The News", "Sports", ArtUrl));

    PlayHistoryEntry entry = Assert.Single(_entries);
    Assert.Equal("Greatest Hits", entry.Track!.Album);
    Assert.Equal(ArtUrl, entry.Track.CoverArtUrl);
    Assert.Equal(MetadataSource.Avrcp, entry.MetadataSource);
  }

  [Fact]
  public void Bluetooth_Misidentification_DoesNotStartANewRow_OrReplaceTheTitle()
  {
    // The AUD-1 production case: SongRec matched residual audio while the phone played Green Day.
    using PlayHistoryTracker tracker = BuildTracker(AudioSourceType.Bluetooth);
    RaiseAvrcp("Basket Case", "Green Day", album: "Dookie");

    RaiseSongChanged(Identified("Spirit In The Sky", "Norman Greenbaum", "Spirit In The Sky", ArtUrl));

    PlayHistoryEntry entry = Assert.Single(_entries);
    Assert.Empty(_finalized);
    Assert.Equal("Basket Case", entry.Track!.Title);
    Assert.Equal("Green Day", entry.Track.Artist);
    Assert.Equal("Dookie", entry.Track.Album);
    // Art: the owner's 2026-09-26 AUD-1 decision — fingerprint art fills when the source has
    // none, with no artist-match guard. History follows now-playing here too.
    Assert.Equal(ArtUrl, entry.Track.CoverArtUrl);
  }

  [Fact]
  public void Bluetooth_WithoutAvrcpTitles_ATitleTheSourceFilledEarlier_DoesNotBlockTheNextSongChange()
  {
    // A phone that publishes no track names: Bluetooth fills its title from the identification
    // and keeps it until AVRCP writes the field again. That filled title is not the source naming
    // the song, so it must not hold History on the first song forever.
    //
    // The real sequence: the phone's AVRCP events carry no title (no History row — the handler
    // needs a title and artist); the FIRST identification raises no SongChanged, only
    // TrackIdentified, which reaches no row (none is unidentified) while BluetoothAudioSource
    // fills its title from it and keeps that title; every later identification raises SongChanged.
    using PlayHistoryTracker tracker = BuildTracker(AudioSourceType.Bluetooth);
    RaiseAvrcp("", "", album: "");
    Assert.Empty(_entries);

    TrackMetadata first = Identified("Africa", "Toto", "Toto IV", ArtUrl);
    RaiseIdentified(first);
    Assert.Empty(_entries);
    // What BluetoothAudioSource.OnTrackIdentified does with it — and keeps doing, because AUD-1
    // never overwrites a field it has filled.
    _sourceMetadata[StandardMetadataKeys.Title] = "Africa";
    _sourceMetadata[StandardMetadataKeys.Artist] = "Toto";
    _sourceMetadata[StandardMetadataKeys.Album] = "Toto IV";

    RaiseSongChanged(Identified("Rosanna", "Toto", "Toto IV", ArtUrl));
    PlayHistoryEntry rosanna = Assert.Single(_entries);
    Assert.Equal("Rosanna", rosanna.Track!.Title);
    Assert.Equal(MetadataSource.Fingerprinting, rosanna.MetadataSource);

    RaiseSongChanged(Identified("Hold the Line", "Toto", "Toto", ArtUrl));

    Assert.Equal([rosanna.Id], _finalized);
    Assert.Equal(2, _entries.Count);
    Assert.Equal("Hold the Line", _entries[1].Track!.Title);
    Assert.Equal("Toto", _entries[1].Track!.Album);
    Assert.Equal(MetadataSource.Fingerprinting, _entries[1].MetadataSource);
  }

  [Fact]
  public void Bluetooth_ArtAndAlbumFilledByAMisidentification_AreCorrectedByTheNextIdentification()
  {
    // The owner's 2026-09-26 AUD-1 ruling accepted a misidentification's art under the phone's
    // title because the next identification corrects it. It must in History too.
    using PlayHistoryTracker tracker = BuildTracker(AudioSourceType.Bluetooth);
    RaiseAvrcp("Basket Case", "Green Day", album: "");
    RaiseSongChanged(Identified("Spirit In The Sky", "Norman Greenbaum", "Spirit In The Sky", "/api/albumart/wrong.jpg"));
    Assert.Equal("/api/albumart/wrong.jpg", Assert.Single(_entries).Track!.CoverArtUrl);

    RaiseSongChanged(Identified("Basket Case", "Green Day", "Dookie", ArtUrl));

    PlayHistoryEntry entry = Assert.Single(_entries);
    Assert.Empty(_finalized);
    Assert.Equal("Basket Case", entry.Track!.Title);
    Assert.Equal("Dookie", entry.Track.Album);
    Assert.Equal(ArtUrl, entry.Track.CoverArtUrl);
    Assert.Equal(MetadataSource.Avrcp, entry.MetadataSource);
  }

  // --- File player -----------------------------------------------------------------------------

  [Fact]
  public void File_SongChangeOnAutoAdvance_RecordsTheNewFilesTags_FilledFromTheFingerprint()
  {
    using PlayHistoryTracker tracker = BuildTracker(AudioSourceType.FilePlayer);
    SetFileTags("Song A", "Band A", "Album A");
    RaisePlaying();
    PlayHistoryEntry first = Assert.Single(_entries);

    // Auto-advance stays in Playing (no StateChanged); only the identification marks the boundary.
    SetFileTags("Song B", "Band B", StandardMetadataKeys.DefaultAlbum);
    RaiseSongChanged(Identified("Song B (Live)", "Band B", "Album B", ArtUrl));

    Assert.Equal([first.Id], _finalized);
    Assert.Equal(2, _entries.Count);
    PlayHistoryEntry second = _entries[1];
    Assert.Equal("Song B", second.Track!.Title);
    Assert.Equal("Band B", second.Track.Artist);
    Assert.Equal("Album B", second.Track.Album);     // tag was "--" — filled
    Assert.Equal(ArtUrl, second.Track.CoverArtUrl);  // seeded fallback art — filled
    Assert.Equal(MetadataSource.FileTag, second.MetadataSource);
    Assert.True(second.WasIdentified);
  }

  [Fact]
  public void File_TrackIdentified_OnARowWithATitleTagButNoArtist_KeepsTheTitle_FillsTheArtist()
  {
    using PlayHistoryTracker tracker = BuildTracker(AudioSourceType.FilePlayer);
    SetFileTags("Song C", StandardMetadataKeys.DefaultArtist, StandardMetadataKeys.DefaultAlbum);
    RaisePlaying();
    PlayHistoryEntry recorded = Assert.Single(_entries);
    Assert.False(recorded.WasIdentified); // artist fell back to "File Player": a placeholder row

    RaiseIdentified(Identified("Song C (2011 Remaster)", "Band C", "Album C"));

    PlayHistoryEntry entry = Assert.Single(_entries);
    Assert.Equal("Song C", entry.Track!.Title);
    Assert.Equal("Band C", entry.Track.Artist);
    Assert.Equal("Album C", entry.Track.Album);
    Assert.Equal(MetadataSource.FileTag, entry.MetadataSource);
    Assert.True(entry.WasIdentified);
  }

  [Fact]
  public void File_TrackIdentified_DoesNotRewriteAFinishedRow()
  {
    // An identification is of what is playing now. A finished unidentified row (an earlier file
    // SongRec never matched) must not be merged with a later file's identification.
    using PlayHistoryTracker tracker = BuildTracker(AudioSourceType.FilePlayer);
    SetFileTags("Song C", StandardMetadataKeys.DefaultArtist, StandardMetadataKeys.DefaultAlbum);
    RaisePlaying();
    _entries[0] = _entries[0] with { EndedAt = DateTime.UtcNow };

    RaiseIdentified(Identified("Song D", "Band D", "Album D", ArtUrl));

    PlayHistoryEntry entry = Assert.Single(_entries);
    Assert.Equal("Song C", entry.Track!.Title);
    Assert.Equal("File Player", entry.Track.Artist);
    Assert.False(entry.WasIdentified);
  }

  // --- Sources AUD-1 did not put under the rule: unchanged ------------------------------------

  [Fact]
  public void Radio_SongChange_StillRecordsTheIdentificationAsIs()
  {
    // SDR now-playing still takes an identification whole (AUD-1 covered BT and File only), so
    // History does too. Control: passes before and after AUD-19.
    using PlayHistoryTracker tracker = BuildTracker(AudioSourceType.Radio);
    TrackMetadata shazam = Identified("Africa", "Toto", "Toto IV", ArtUrl);

    RaiseSongChanged(shazam);

    PlayHistoryEntry entry = Assert.Single(_entries);
    Assert.Equal(shazam.Id, entry.TrackMetadataId);
    Assert.Equal("Africa", entry.Track!.Title);
    Assert.Equal(MetadataSource.Fingerprinting, entry.MetadataSource);
  }

  [Fact]
  public void Radio_TrackIdentified_StillReplacesThePlaceholderRow()
  {
    using PlayHistoryTracker tracker = BuildTracker(AudioSourceType.Radio);
    // History titles a radio row with the frequency. Under the per-field rule that would read
    // as a source-supplied title and survive; radio is outside the rule, so it must not.
    _sourceMetadata["Frequency"] = "101.5 MHz";
    RaisePlaying();
    PlayHistoryEntry recorded = Assert.Single(_entries);
    Assert.Equal("101.5 MHz", recorded.Track!.Title);
    Assert.False(recorded.WasIdentified);
    TrackMetadata shazam = Identified("Africa", "Toto", "Toto IV", ArtUrl);

    RaiseIdentified(shazam);

    PlayHistoryEntry entry = Assert.Single(_entries);
    Assert.Equal(shazam.Id, entry.TrackMetadataId);
    Assert.Equal("Africa", entry.Track!.Title);
    Assert.Equal("Toto", entry.Track.Artist);
    Assert.Equal(MetadataSource.Fingerprinting, entry.MetadataSource);
  }
}
