using System.Text.Json;
using Microsoft.Extensions.Logging;
using Radio.Core.Models;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// JSON file store for band maps: one file per band,
/// <c>&lt;Database:RootPath&gt;/bandmap/&lt;band code, lower case&gt;.json</c>. The FM map is
/// <c>bandmap/fm.json</c>, the path AUD-76 used (AUD-91 added the other bands).
/// </summary>
public sealed class BandMapStore
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    WriteIndented = false,
  };

  private readonly ILogger _logger;

  /// <summary>Creates a store for map files under <paramref name="rootPath"/>.</summary>
  /// <param name="rootPath">The <c>Database:RootPath</c> directory.</param>
  /// <param name="logger">Logger.</param>
  public BandMapStore(string rootPath, ILogger logger)
  {
    ArgumentNullException.ThrowIfNull(rootPath);
    DirectoryPath = Path.Combine(rootPath, "bandmap");
    _logger = logger;
  }

  /// <summary>The directory holding the map files.</summary>
  public string DirectoryPath { get; }

  /// <summary>Full path of <paramref name="band"/>'s map file.</summary>
  /// <param name="band">Band code: ASCII letters only, e.g. <c>"FM"</c>.</param>
  /// <exception cref="ArgumentException"><paramref name="band"/> is empty or not ASCII letters only.</exception>
  public string GetFilePath(string band)
  {
    // The code becomes a file name: letters only, so it cannot leave the directory.
    if (string.IsNullOrEmpty(band) || !band.All(char.IsAsciiLetter))
    {
      throw new ArgumentException($"Band code '{band}' is not ASCII letters only.", nameof(band));
    }
    return Path.Combine(DirectoryPath, band.ToLowerInvariant() + ".json");
  }

  /// <summary>
  /// Reads <paramref name="band"/>'s stored map. Returns null when the file is missing; returns
  /// null and logs a warning when it cannot be read or parsed.
  /// </summary>
  /// <param name="band">Band code, e.g. <c>"FM"</c>.</param>
  public BandMap? Load(string band)
  {
    string filePath = GetFilePath(band);
    if (!File.Exists(filePath))
    {
      return null;
    }

    try
    {
      using FileStream stream = File.OpenRead(filePath);
      BandMap? map = JsonSerializer.Deserialize<BandMap>(stream, JsonOptions);
      if (map?.Channels == null)
      {
        _logger.LogWarning("Band map file {Path} has no channel list; ignoring it", filePath);
        return null;
      }
      return map;
    }
    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
    {
      _logger.LogWarning(ex, "Band map file {Path} could not be read; starting with no map", filePath);
      return null;
    }
  }

  /// <summary>
  /// Writes <paramref name="map"/> to <see cref="GetFilePath"/> of its <see cref="BandMap.Band"/>:
  /// first to a temporary file in the same directory, then moved over the map file, so a write
  /// interrupted before the move leaves the previous map file intact.
  /// </summary>
  /// <exception cref="IOException">The file could not be written.</exception>
  /// <exception cref="ArgumentException">The map's band code is not ASCII letters only.</exception>
  public void Save(BandMap map)
  {
    ArgumentNullException.ThrowIfNull(map);
    string filePath = GetFilePath(map.Band);
    Directory.CreateDirectory(DirectoryPath);

    string tempPath = filePath + ".tmp";
    using (FileStream stream = File.Create(tempPath))
    {
      JsonSerializer.Serialize(stream, map, JsonOptions);
    }
    File.Move(tempPath, filePath, overwrite: true);
  }
}
