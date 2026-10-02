using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// AUD-98: a File Player list must never lose the tracks it has played. The owner, 2026-10-02: <i>"It seems
/// like as song are played, they're dropped from the playlist? … we need to make sure that repeat actually
/// "works", and not remove from the playlist."</i>
/// </summary>
/// <remarks>
/// In a session nothing was dropped — played tracks sit in the history and Repeat All rebuilds from the
/// unshuffled order. The loss was in persistence: only the current and upcoming tracks were saved, so every
/// restart (every deploy) restored the remainder as the whole list. These tests save from one source and
/// restore into a NEW source sharing the same preferences object, which is what a restart does. Without a
/// configuration manager the save writes <c>_preferences.CurrentValue</c> directly, so no store is needed;
/// <see cref="FilePlayerQueueRestoreSqliteTests"/> covers the real store.
/// <para>
/// The test files are not decodable audio, so a source that has LOADED a track marks it Error in the full
/// playlist. A freshly restored source has loaded nothing, which is why item states are asserted on the
/// restored source, before it advances.
/// </para>
/// </remarks>
public sealed class FilePlayerQueueRestoreTests : IDisposable
{
  private readonly string _dir = Path.Combine(Path.GetTempPath(), $"FilePlayerRestore_{Guid.NewGuid():N}");
  private readonly FilePlayerPreferences _preferences = new() { Repeat = RepeatMode.Off, Shuffle = false };
  private readonly Mock<IOptionsMonitor<FilePlayerPreferences>> _preferencesMonitor = new();
  private readonly Mock<IOptionsMonitor<FilePlayerOptions>> _optionsMonitor = new();

  public FilePlayerQueueRestoreTests()
  {
    Directory.CreateDirectory(_dir);
    _preferencesMonitor.Setup(m => m.CurrentValue).Returns(_preferences);
    _optionsMonitor.Setup(m => m.CurrentValue).Returns(new FilePlayerOptions { RootDirectory = "" });
  }

  public void Dispose()
  {
    try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
  }

  private FilePlayerAudioSource CreateSource() =>
    new(NullLogger<FilePlayerAudioSource>.Instance, _optionsMonitor.Object, _preferencesMonitor.Object, _dir);

  private List<string> CreateTracks(int count)
  {
    var paths = new List<string>();
    for (int i = 1; i <= count; i++)
    {
      string path = Path.Combine(_dir, $"track{i:D2}.mp3");
      File.WriteAllText(path, "not audio");
      paths.Add(path);
    }
    return paths;
  }

  private static async Task<List<string>> FullPathsAsync(IPlayQueue queue) =>
    (await queue.GetFullPlaylistAsync()).Select(i => i.Id).ToList();

  /// <summary>Plays <paramref name="advances"/> tracks into a list, then restarts into a new source.</summary>
  private async Task<(FilePlayerAudioSource Restored, List<string> BeforeRestart)> PlayThenRestartAsync(
    List<string> tracks, int advances)
  {
    FilePlayerAudioSource before = CreateSource();
    await before.LoadPlaylistAsync(tracks);
    for (int i = 0; i < advances; i++)
    {
      await before.NextAsync();
    }
    List<string> beforeRestart = await FullPathsAsync(before);
    await before.DisposeAsync();

    FilePlayerAudioSource restored = CreateSource();
    await restored.InitializeAsync();
    return (restored, beforeRestart);
  }

  [Fact]
  public async Task Restart_BringsBackPlayedCurrentAndUpcoming_InOrder_WithTheirStates()
  {
    List<string> tracks = CreateTracks(5);

    var (restored, beforeRestart) = await PlayThenRestartAsync(tracks, advances: 2);
    IReadOnlyList<QueueItem> full = await restored.GetFullPlaylistAsync();

    Assert.Equal(tracks, beforeRestart);
    Assert.Equal(tracks, full.Select(i => i.Id));
    Assert.Equal(
      new[] { QueueItemState.Played, QueueItemState.Played, QueueItemState.Current, QueueItemState.Upcoming, QueueItemState.Upcoming },
      full.Select(i => i.State));
    Assert.Equal(tracks[2], restored.CurrentFile);
    Assert.Equal(0, restored.CurrentIndex);
  }

  [Fact]
  public async Task RepeatAll_AfterRestart_RepeatsTheWholeList()
  {
    _preferences.Repeat = RepeatMode.All;
    List<string> tracks = CreateTracks(5);
    var (restored, _) = await PlayThenRestartAsync(tracks, advances: 2); // at track 3

    await restored.NextAsync(); // 4
    await restored.NextAsync(); // 5
    await restored.NextAsync(); // wraps

    Assert.Equal(tracks[0], restored.CurrentFile);
    Assert.Equal(tracks, await FullPathsAsync(restored));
    Assert.Equal(4, restored.RemainingTracks);
  }

  [Fact]
  public async Task Shuffle_Restart_KeepsThePlayOrder_AndTheUnshuffledOrder()
  {
    _preferences.Shuffle = true;
    List<string> tracks = CreateTracks(10);

    var (restored, beforeRestart) = await PlayThenRestartAsync(tracks, advances: 3);

    // The list comes back in the order it was being played (shuffled), with nothing missing.
    Assert.Equal(beforeRestart, await FullPathsAsync(restored));
    Assert.Equal(tracks.Order(), beforeRestart.Order());

    // Turning shuffle off puts the tracks not yet played back in the unshuffled order. That order is only
    // right if it was persisted: rebuilt from the shuffled list, it would come back shuffled.
    List<string> played = beforeRestart.Take(3).ToList();
    string current = restored.CurrentFile!;
    await restored.SetShuffleAsync(false);

    Assert.Equal(current, restored.CurrentFile);
    Assert.Equal(tracks.Except(played).Where(t => t != current), restored.Playlist);
    // Every track exactly once: no played track comes back as upcoming, no unplayed one leaves.
    Assert.Equal(tracks.Order(), (await FullPathsAsync(restored)).Order());
  }

  [Fact]
  public async Task ShuffleOff_InSession_KeepsEveryUnplayedTrack_AndRepeatsNoPlayedOne()
  {
    // Pins the pre-existing in-session half of the same defect, deterministically: original order
    // 1..5, played 3 then 1 (as a shuffle could), current 4, upcoming 5, 2.
    List<string> tracks = CreateTracks(5);
    _preferences.Shuffle = true;
    _preferences.QueueItems = new List<string> { tracks[2], tracks[0], tracks[3], tracks[4], tracks[1] };
    _preferences.CurrentQueueIndex = 2;
    _preferences.OriginalOrder = new List<string>(tracks);
    FilePlayerAudioSource source = CreateSource();
    await source.InitializeAsync();

    await source.SetShuffleAsync(false);

    Assert.Equal(tracks[3], source.CurrentFile);
    Assert.Equal(new[] { tracks[1], tracks[4] }, source.Playlist);
    Assert.Equal(new[] { tracks[2], tracks[0], tracks[3], tracks[1], tracks[4] }, await FullPathsAsync(source));
  }

  [Fact]
  public async Task Shuffle_RepeatAll_AfterRestart_RepeatsEveryTrack()
  {
    _preferences.Shuffle = true;
    _preferences.Repeat = RepeatMode.All;
    List<string> tracks = CreateTracks(6);
    var (restored, _) = await PlayThenRestartAsync(tracks, advances: 4); // 4 played, 1 upcoming

    await restored.NextAsync(); // last track
    await restored.NextAsync(); // wraps, reshuffled

    List<string> after = await FullPathsAsync(restored);
    Assert.Equal(tracks.Order(), after.Order());
    Assert.Equal(5, restored.RemainingTracks);
  }

  [Fact]
  public async Task OldShape_CurrentAndUpcomingOnly_LoadsAsItDidBefore()
  {
    // The box's rows on 2026-10-02: QueueItems = current + upcoming, CurrentQueueIndex = 0, no OriginalOrder.
    List<string> tracks = CreateTracks(4);
    _preferences.QueueItems = new List<string>(tracks);
    _preferences.CurrentQueueIndex = 0;
    _preferences.OriginalOrder = new List<string>();

    FilePlayerAudioSource source = CreateSource();
    await source.InitializeAsync();

    Assert.Equal(AudioSourceState.Ready, source.State);
    Assert.Equal(tracks[0], source.CurrentFile);
    Assert.Equal(tracks.Skip(1), source.Playlist);
    Assert.DoesNotContain(await source.GetFullPlaylistAsync(), i => i.State == QueueItemState.Played);

    // And Repeat All repeats all of it (the unshuffled order falls back to the saved list).
    _preferences.Repeat = RepeatMode.All;
    for (int i = 0; i < tracks.Count; i++)
    {
      await source.NextAsync();
    }
    Assert.Equal(tracks[0], source.CurrentFile);
    Assert.Equal(tracks, await FullPathsAsync(source));
  }

  [Fact]
  public async Task OldShape_WithANonZeroIndex_KeepsTheEarlierTracksAsPlayed()
  {
    // The old restore dequeued every track before the index and kept none of them.
    List<string> tracks = CreateTracks(4);
    _preferences.QueueItems = new List<string>(tracks);
    _preferences.CurrentQueueIndex = 2;

    FilePlayerAudioSource source = CreateSource();
    await source.InitializeAsync();

    Assert.Equal(tracks[2], source.CurrentFile);
    Assert.Equal(tracks, await FullPathsAsync(source));
  }

  [Fact]
  public async Task Previous_AfterRestart_GoesToTheLastPlayedTrack_AndPersistsIt()
  {
    List<string> tracks = CreateTracks(5);
    var (restored, _) = await PlayThenRestartAsync(tracks, advances: 2); // at track 3

    await restored.PreviousAsync();

    Assert.Equal(tracks[1], restored.CurrentFile);
    Assert.Equal(tracks, await FullPathsAsync(restored));

    // Previous raises no QueueChanged, so it has to save on its own; restart again and it is still there.
    FilePlayerAudioSource again = CreateSource();
    await again.InitializeAsync();
    Assert.Equal(tracks[1], again.CurrentFile);
    Assert.Equal(tracks, await FullPathsAsync(again));
  }

  [Fact]
  public async Task JumpIntoThePlayedTracks_AfterRestart_KeepsTheList()
  {
    List<string> tracks = CreateTracks(5);
    var (restored, _) = await PlayThenRestartAsync(tracks, advances: 3); // at track 4

    await restored.JumpToFullPlaylistIndexAsync(0);

    Assert.Equal(tracks[0], restored.CurrentFile);
    Assert.Equal(tracks, await FullPathsAsync(restored));
    Assert.Equal(tracks, _preferences.QueueItems);
    Assert.Equal(0, _preferences.CurrentQueueIndex);
  }

  [Fact]
  public async Task RepeatOff_EndOfList_ListsTheLastTrackOnce_AndPreviousStepsBack()
  {
    List<string> tracks = CreateTracks(3);
    FilePlayerAudioSource source = CreateSource();
    await source.LoadPlaylistAsync(tracks);
    await source.PlayAsync();

    await source.NextAsync();
    await source.NextAsync();
    await source.NextAsync(); // past the end: stops on the last track

    Assert.Equal(AudioSourceState.Stopped, source.State);
    Assert.Equal(tracks[2], source.CurrentFile);
    Assert.Equal(tracks, await FullPathsAsync(source));

    await source.PreviousAsync();
    Assert.Equal(tracks[1], source.CurrentFile);
    Assert.Equal(tracks, await FullPathsAsync(source));
  }

  [Fact]
  public async Task RepeatAll_PreviousAtTheFirstTrack_GoesToTheLast_WithoutLosingOne()
  {
    _preferences.Repeat = RepeatMode.All;
    List<string> tracks = CreateTracks(3);
    FilePlayerAudioSource source = CreateSource();
    await source.LoadPlaylistAsync(tracks);

    await source.PreviousAsync();

    Assert.Equal(tracks[2], source.CurrentFile);
    Assert.Equal(tracks, await FullPathsAsync(source));
    Assert.Equal(tracks, _preferences.QueueItems);
    Assert.Equal(2, _preferences.CurrentQueueIndex);
  }

  [Fact]
  public async Task Restore_PrimesTheMetadataCache_WithEveryRestoredRow_PlayedIncluded()
  {
    // AUD-96's cache must be warmed for the played rows too: the panel shows them, and Save as playlist
    // waits for every full-playlist row before storing the metadata.
    List<string> tracks = CreateTracks(5);
    _preferences.QueueItems = new List<string>(tracks);
    _preferences.CurrentQueueIndex = 2;
    FilePlayerAudioSource source = CreateSource();
    var read = new System.Collections.Concurrent.ConcurrentBag<string>();
    Func<string, QueueItemMetadata> real = source.QueueMetadataReader;
    source.QueueMetadataReader = path => { read.Add(path); return real(path); };

    await source.InitializeAsync();
    await source.WhenQueueMetadataIdleAsync();

    Assert.Equal(tracks.Order(), read.Distinct().Order());
  }

  [Fact]
  public async Task Restore_MovesQueueVersion_SoThePollerBroadcastsThePlayedRows()
  {
    // AUD-96's poller skips the full-playlist read while QueueVersion is unchanged. A restore that brings
    // back played tracks must move it. This shows only that the restore moves it; that the PLAYED segment is
    // part of the hash is read from QueueVersion (each played path is mixed in under its own marker), not
    // isolated here — the current and upcoming segments change in the same restore.
    List<string> tracks = CreateTracks(4);
    _preferences.QueueItems = new List<string>(tracks);
    _preferences.CurrentQueueIndex = 2;
    FilePlayerAudioSource source = CreateSource();
    long before = source.QueueVersion;

    await source.InitializeAsync();

    Assert.NotEqual(before, source.QueueVersion);
  }

  [Fact]
  public async Task ClearedList_PersistsEmpty_WithIndexMinusOne()
  {
    List<string> tracks = CreateTracks(2);
    FilePlayerAudioSource source = CreateSource();
    await source.LoadPlaylistAsync(tracks);
    await source.NextAsync();

    await source.ClearQueueAsync();

    Assert.Empty(_preferences.QueueItems);
    Assert.Equal(-1, _preferences.CurrentQueueIndex);
    Assert.Empty(_preferences.OriginalOrder);
  }

  // ---- BuildRestoredQueue, directly ----

  private static FilePlayerAudioSource.RestoredQueue? Build(
    string[] saved, int index, string[]? original = null, params string[] missing) =>
    FilePlayerAudioSource.BuildRestoredQueue(saved, index, original, p => !missing.Contains(p));

  [Fact]
  public void Build_SplitsAtTheIndex()
  {
    var r = Build(["a", "b", "c", "d"], 2)!;

    Assert.Equal(new[] { "a", "b" }, r.Played);
    Assert.Equal("c", r.Current);
    Assert.Equal(new[] { "d" }, r.Upcoming);
    Assert.Equal(new[] { "a", "b", "c", "d" }, r.OriginalOrder);
  }

  [Fact]
  public void Build_MissingFiles_AreDropped_WithoutMovingTheBoundary()
  {
    var r = Build(["a", "b", "c", "d", "e"], 3, null, "a", "c")!;

    Assert.Equal(new[] { "b" }, r.Played);
    Assert.Equal("d", r.Current);
    Assert.Equal(new[] { "e" }, r.Upcoming);
  }

  [Fact]
  public void Build_MissingCurrent_TheNextExistingTrackIsCurrent()
  {
    var r = Build(["a", "b", "c", "d"], 1, null, "b")!;

    Assert.Equal(new[] { "a" }, r.Played);
    Assert.Equal("c", r.Current);
    Assert.Equal(new[] { "d" }, r.Upcoming);
  }

  [Fact]
  public void Build_NothingAtOrAfterTheIndex_TheLastPlayedTrackIsCurrent()
  {
    var r = Build(["a", "b", "c"], 2, null, "c")!;

    Assert.Equal(new[] { "a" }, r.Played);
    Assert.Equal("b", r.Current);
    Assert.Empty(r.Upcoming);
  }

  [Theory]
  [InlineData(-1)]
  [InlineData(0)]
  public void Build_IndexZeroOrNegative_NothingPlayed(int index)
  {
    var r = Build(["a", "b"], index)!;

    Assert.Empty(r.Played);
    Assert.Equal("a", r.Current);
    Assert.Equal(new[] { "b" }, r.Upcoming);
  }

  [Fact]
  public void Build_IndexPastTheEnd_TheLastTrackIsCurrent()
  {
    var r = Build(["a", "b", "c"], 9)!;

    Assert.Equal(new[] { "a", "b" }, r.Played);
    Assert.Equal("c", r.Current);
  }

  [Fact]
  public void Build_NothingExists_ReturnsNull()
  {
    Assert.Null(Build(["a", "b"], 0, null, "a", "b"));
    Assert.Null(Build([], 0));
  }

  [Fact]
  public void Build_OriginalOrder_IsKept_AndCompletedWithAnyTrackItLacks()
  {
    var r = Build(["c", "a", "x", "b"], 1, ["a", "b", "c", "gone"], "gone")!;

    // Its own order, its missing file dropped, then the restored track it did not have.
    Assert.Equal(new[] { "a", "b", "c", "x" }, r.OriginalOrder);
  }

  [Fact]
  public void Build_ChecksEachPathOnce()
  {
    var calls = new List<string>();
    FilePlayerAudioSource.BuildRestoredQueue(["a", "b", "c"], 1, ["c", "b", "a"], p => { calls.Add(p); return true; });

    Assert.Equal(new[] { "a", "b", "c" }, calls);
  }
}
