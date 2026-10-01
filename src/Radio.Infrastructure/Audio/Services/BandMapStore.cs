using System.Text.Json;
using Microsoft.Extensions.Logging;
using Radio.Core.Models;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// JSON file store for the FM band map (AUD-76): <c>&lt;Database:RootPath&gt;/bandmap/fm.json</c>.
/// </summary>
public sealed class BandMapStore
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    WriteIndented = false,
  };

  private readonly ILogger _logger;

  /// <summary>Creates a store for the map file under <paramref name="rootPath"/>.</summary>
  /// <param name="rootPath">The <c>Database:RootPath</c> directory.</param>
  /// <param name="logger">Logger.</param>
  public BandMapStore(string rootPath, ILogger logger)
  {
    ArgumentNullException.ThrowIfNull(rootPath);
    FilePath = Path.Combine(rootPath, "bandmap", "fm.json");
    _logger = logger;
  }

  /// <summary>Full path of the map file.</summary>
  public string FilePath { get; }

  /// <summary>
  /// Reads the stored map. Returns null when the file is missing; returns null and
  /// logs a warning when it cannot be read or parsed.
  /// </summary>
  public BandMap? Load()
  {
    if (!File.Exists(FilePath))
    {
      return null;
    }

    try
    {
      using FileStream stream = File.OpenRead(FilePath);
      BandMap? map = JsonSerializer.Deserialize<BandMap>(stream, JsonOptions);
      if (map?.Channels == null)
      {
        _logger.LogWarning("Band map file {Path} has no channel list; ignoring it", FilePath);
        return null;
      }
      return map;
    }
    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
    {
      _logger.LogWarning(ex, "Band map file {Path} could not be read; starting with no map", FilePath);
      return null;
    }
  }

  /// <summary>
  /// Writes <paramref name="map"/> to a temporary file in the same directory and then
  /// moves it over the map file, so a write interrupted before the move leaves the
  /// previous map file intact.
  /// </summary>
  /// <exception cref="IOException">The file could not be written.</exception>
  public void Save(BandMap map)
  {
    ArgumentNullException.ThrowIfNull(map);
    string directory = Path.GetDirectoryName(FilePath)!;
    Directory.CreateDirectory(directory);

    string tempPath = FilePath + ".tmp";
    using (FileStream stream = File.Create(tempPath))
    {
      JsonSerializer.Serialize(stream, map, JsonOptions);
    }
    File.Move(tempPath, FilePath, overwrite: true);
  }
}
