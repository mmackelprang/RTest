namespace Radio.Core.Interfaces;

/// <summary>
/// Represents a saved playlist.
/// </summary>
public sealed record Playlist
{
  public required string Id { get; init; }
  public required string Name { get; init; }
  public string? Description { get; init; }
  public DateTime CreatedAt { get; init; }
  public DateTime ModifiedAt { get; init; }
  public int ItemCount { get; init; }
}

/// <summary>
/// Represents an item within a playlist.
/// </summary>
public sealed record PlaylistItem
{
  public required string Id { get; init; }
  public required string PlaylistId { get; init; }
  public int Position { get; init; }
  public required string FilePath { get; init; }
  public string? Title { get; init; }
  public string? Artist { get; init; }
  public string? Album { get; init; }
  public int? DurationMs { get; init; }
}

/// <summary>
/// Repository for managing saved playlists.
/// </summary>
public interface IPlaylistRepository
{
  /// <summary>Gets all playlists (without items).</summary>
  Task<IReadOnlyList<Playlist>> GetAllAsync(CancellationToken ct = default);

  /// <summary>Gets a playlist by ID including its items.</summary>
  Task<(Playlist? Playlist, IReadOnlyList<PlaylistItem> Items)> GetByIdAsync(string id, CancellationToken ct = default);

  /// <summary>Creates a new playlist with items.</summary>
  Task<Playlist> CreateAsync(string name, string? description, IReadOnlyList<PlaylistItem> items, CancellationToken ct = default);

  /// <summary>Deletes a playlist and its items.</summary>
  Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
