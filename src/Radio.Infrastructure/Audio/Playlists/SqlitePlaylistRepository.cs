using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces;
using Radio.Infrastructure.Audio.Fingerprinting.Data;

namespace Radio.Infrastructure.Audio.Playlists;

/// <summary>
/// SQLite-backed playlist repository using the shared FingerprintDbContext.
/// </summary>
public sealed class SqlitePlaylistRepository : IPlaylistRepository
{
  private readonly FingerprintDbContext _dbContext;
  private readonly ILogger<SqlitePlaylistRepository> _logger;

  public SqlitePlaylistRepository(
    FingerprintDbContext dbContext,
    ILogger<SqlitePlaylistRepository> logger)
  {
    _dbContext = dbContext;
    _logger = logger;
  }

  public async Task<IReadOnlyList<Playlist>> GetAllAsync(CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT Id, Name, Description, CreatedAt, ModifiedAt, ItemCount FROM Playlists ORDER BY ModifiedAt DESC";

    var playlists = new List<Playlist>();
    using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
    {
      playlists.Add(new Playlist
      {
        Id = reader.GetString(0),
        Name = reader.GetString(1),
        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
        CreatedAt = DateTime.Parse(reader.GetString(3)),
        ModifiedAt = DateTime.Parse(reader.GetString(4)),
        ItemCount = reader.GetInt32(5)
      });
    }

    return playlists;
  }

  public async Task<(Playlist? Playlist, IReadOnlyList<PlaylistItem> Items)> GetByIdAsync(
    string id, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    // Get playlist
    using var playlistCmd = conn.CreateCommand();
    playlistCmd.CommandText = "SELECT Id, Name, Description, CreatedAt, ModifiedAt, ItemCount FROM Playlists WHERE Id = @Id";
    playlistCmd.Parameters.AddWithValue("@Id", id);

    Playlist? playlist = null;
    using (var reader = await playlistCmd.ExecuteReaderAsync(ct))
    {
      if (await reader.ReadAsync(ct))
      {
        playlist = new Playlist
        {
          Id = reader.GetString(0),
          Name = reader.GetString(1),
          Description = reader.IsDBNull(2) ? null : reader.GetString(2),
          CreatedAt = DateTime.Parse(reader.GetString(3)),
          ModifiedAt = DateTime.Parse(reader.GetString(4)),
          ItemCount = reader.GetInt32(5)
        };
      }
    }

    if (playlist == null)
    {
      return (null, Array.Empty<PlaylistItem>());
    }

    // Get items
    using var itemsCmd = conn.CreateCommand();
    itemsCmd.CommandText = """
      SELECT Id, PlaylistId, Position, FilePath, Title, Artist, Album, DurationMs
      FROM PlaylistItems WHERE PlaylistId = @PlaylistId ORDER BY Position
      """;
    itemsCmd.Parameters.AddWithValue("@PlaylistId", id);

    var items = new List<PlaylistItem>();
    using (var reader = await itemsCmd.ExecuteReaderAsync(ct))
    {
      while (await reader.ReadAsync(ct))
      {
        items.Add(new PlaylistItem
        {
          Id = reader.GetString(0),
          PlaylistId = reader.GetString(1),
          Position = reader.GetInt32(2),
          FilePath = reader.GetString(3),
          Title = reader.IsDBNull(4) ? null : reader.GetString(4),
          Artist = reader.IsDBNull(5) ? null : reader.GetString(5),
          Album = reader.IsDBNull(6) ? null : reader.GetString(6),
          DurationMs = reader.IsDBNull(7) ? null : reader.GetInt32(7)
        });
      }
    }

    return (playlist, items);
  }

  public async Task<Playlist> CreateAsync(
    string name, string? description, IReadOnlyList<PlaylistItem> items,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);
    using var transaction = conn.BeginTransaction();

    try
    {
      var playlistId = Guid.NewGuid().ToString();
      var now = DateTime.UtcNow.ToString("O");

      // Insert playlist
      using var playlistCmd = conn.CreateCommand();
      playlistCmd.Transaction = transaction;
      playlistCmd.CommandText = """
        INSERT INTO Playlists (Id, Name, Description, CreatedAt, ModifiedAt, ItemCount)
        VALUES (@Id, @Name, @Description, @CreatedAt, @ModifiedAt, @ItemCount)
        """;
      playlistCmd.Parameters.AddWithValue("@Id", playlistId);
      playlistCmd.Parameters.AddWithValue("@Name", name);
      playlistCmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
      playlistCmd.Parameters.AddWithValue("@CreatedAt", now);
      playlistCmd.Parameters.AddWithValue("@ModifiedAt", now);
      playlistCmd.Parameters.AddWithValue("@ItemCount", items.Count);
      await playlistCmd.ExecuteNonQueryAsync(ct);

      // Insert items
      for (var i = 0; i < items.Count; i++)
      {
        var item = items[i];
        using var itemCmd = conn.CreateCommand();
        itemCmd.Transaction = transaction;
        itemCmd.CommandText = """
          INSERT INTO PlaylistItems (Id, PlaylistId, Position, FilePath, Title, Artist, Album, DurationMs)
          VALUES (@Id, @PlaylistId, @Position, @FilePath, @Title, @Artist, @Album, @DurationMs)
          """;
        itemCmd.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString());
        itemCmd.Parameters.AddWithValue("@PlaylistId", playlistId);
        itemCmd.Parameters.AddWithValue("@Position", i);
        itemCmd.Parameters.AddWithValue("@FilePath", item.FilePath);
        itemCmd.Parameters.AddWithValue("@Title", (object?)item.Title ?? DBNull.Value);
        itemCmd.Parameters.AddWithValue("@Artist", (object?)item.Artist ?? DBNull.Value);
        itemCmd.Parameters.AddWithValue("@Album", (object?)item.Album ?? DBNull.Value);
        itemCmd.Parameters.AddWithValue("@DurationMs", (object?)item.DurationMs ?? DBNull.Value);
        await itemCmd.ExecuteNonQueryAsync(ct);
      }

      transaction.Commit();

      _logger.LogInformation("Created playlist '{Name}' with {Count} items", name, items.Count);

      return new Playlist
      {
        Id = playlistId,
        Name = name,
        Description = description,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        ItemCount = items.Count
      };
    }
    catch
    {
      transaction.Rollback();
      throw;
    }
  }

  public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);
    using var transaction = conn.BeginTransaction();

    try
    {
      // Delete items first (FK)
      using var deleteItemsCmd = conn.CreateCommand();
      deleteItemsCmd.Transaction = transaction;
      deleteItemsCmd.CommandText = "DELETE FROM PlaylistItems WHERE PlaylistId = @PlaylistId";
      deleteItemsCmd.Parameters.AddWithValue("@PlaylistId", id);
      await deleteItemsCmd.ExecuteNonQueryAsync(ct);

      // Delete playlist
      using var deletePlaylistCmd = conn.CreateCommand();
      deletePlaylistCmd.Transaction = transaction;
      deletePlaylistCmd.CommandText = "DELETE FROM Playlists WHERE Id = @Id";
      deletePlaylistCmd.Parameters.AddWithValue("@Id", id);
      var affected = await deletePlaylistCmd.ExecuteNonQueryAsync(ct);

      transaction.Commit();

      if (affected > 0)
      {
        _logger.LogInformation("Deleted playlist {Id}", id);
      }

      return affected > 0;
    }
    catch
    {
      transaction.Rollback();
      throw;
    }
  }
}
