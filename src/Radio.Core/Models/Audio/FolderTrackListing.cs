namespace Radio.Core.Models.Audio;

/// <summary>
/// The playable audio files in one folder, in the order "Add folder" queues them (UI-32).
/// Produced by <see cref="Radio.Core.Interfaces.Audio.IFileBrowser.ListFolderTracksAsync"/>.
/// </summary>
/// <remarks>
/// Order is depth-first: the folder's own files first, sorted by name with
/// <see cref="Radio.Core.Utilities.NaturalStringComparer"/> (so "2 - …" precedes "10 - …"), then each subfolder in the same
/// natural order, each one complete before the next. An artist folder therefore queues album by album, track by
/// track.
/// </remarks>
public sealed record FolderTrackListing
{
  /// <summary>The folder's full filesystem path, after the allowed-directory check.</summary>
  public required string FolderPath { get; init; }

  /// <summary>
  /// Full paths of the files to queue, in queue order. At most <see cref="MaxTracks"/> entries; when
  /// <see cref="Truncated"/> is set these are the first <see cref="MaxTracks"/> in that order.
  /// </summary>
  public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();

  /// <summary>
  /// How many of <see cref="Paths"/> sit directly in the folder rather than in a subfolder. They are always the
  /// first entries of <see cref="Paths"/>.
  /// </summary>
  public int TopLevelCount { get; init; }

  /// <summary>Whether subfolders were walked.</summary>
  public bool IncludeSubfolders { get; init; }

  /// <summary>
  /// Whether the walk found more than <see cref="MaxTracks"/> files and stopped. The total is not known: the walk
  /// stops at the first file past the cap, so a large tree is never read in full.
  /// </summary>
  public bool Truncated { get; init; }

  /// <summary>The cap the walk ran with.</summary>
  public int MaxTracks { get; init; }

  /// <summary>Immediate subfolders of the folder that the walk would enter (hidden and linked ones excluded).</summary>
  public int SubfolderCount { get; init; }

  /// <summary>Supported files left out because they could not be opened for reading.</summary>
  public int SkippedUnreadable { get; init; }

  /// <summary>
  /// Entries left out because they are symbolic links (or other reparse points). The walk never follows a link, so
  /// it cannot leave the chosen folder or loop.
  /// </summary>
  public int SkippedLinks { get; init; }

  /// <summary>Subfolders whose contents could not be listed (permission denied, or vanished mid-walk).</summary>
  public int UnreadableFolders { get; init; }
}
