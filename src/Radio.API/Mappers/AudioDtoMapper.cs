using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.API.Mappers;

/// <summary>
/// Provides extension methods for mapping audio domain objects to DTOs.
/// Centralizes DTO mapping logic to avoid duplication across controllers and services.
/// </summary>
public static class AudioDtoMapper
{
  /// <summary>
  /// Maps an IAudioSource to an AudioSourceDto.
  /// </summary>
  /// <param name="source">The audio source to map.</param>
  /// <returns>The mapped DTO.</returns>
  public static AudioSourceDto MapToDto(this IAudioSource source)
  {
    var dto = new AudioSourceDto
    {
      Id = source.Id,
      Name = source.Name,
      Type = source.Type.ToString(),
      Category = source.Category.ToString(),
      State = source.State.ToString(),
      Volume = source.Volume,
      
      // Determine source characteristics based on type
      IsRadio = source.Type == AudioSourceType.Radio,
      IsStreaming = source.Type == AudioSourceType.Spotify, // Spotify is the only guaranteed streaming source
      
      // Build capabilities dictionary
      Capabilities = new Dictionary<string, bool>()
    };

    if (source is IPrimaryAudioSource primary)
    {
      dto.IsSeekable = primary.IsSeekable;
      dto.Metadata = primary.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
      
      // Check if source implements IPlayQueue interface
      dto.HasQueue = source is IPlayQueue;
      
      // Add primary source capabilities to dictionary
      dto.Capabilities["SupportsPlay"] = true; // All primary sources support play
      dto.Capabilities["SupportsPause"] = true; // All primary sources support pause
      dto.Capabilities["SupportsStop"] = true; // All primary sources support stop
      dto.Capabilities["SupportsSeek"] = primary.IsSeekable;
      dto.Capabilities["SupportsNext"] = primary.SupportsNext;
      dto.Capabilities["SupportsPrevious"] = primary.SupportsPrevious;
      dto.Capabilities["SupportsShuffle"] = primary.SupportsShuffle;
      dto.Capabilities["SupportsRepeat"] = primary.SupportsRepeat;
      dto.Capabilities["SupportsQueue"] = primary.SupportsQueue;
    }
    
    // Add radio-specific capabilities if source implements IRadioControls
    // All sources implementing IRadioControls support these core radio features by design
    if (source is IRadioControls)
    {
      dto.Capabilities["SupportsRadioControls"] = true;
      dto.Capabilities["SupportsFrequencyTuning"] = true; // Required by IRadioControls
      dto.Capabilities["SupportsScanning"] = true; // Required by IRadioControls
      dto.Capabilities["SupportsEqualizer"] = true; // Required by IRadioControls
      dto.Capabilities["SupportsDeviceVolume"] = true; // Required by IRadioControls
    }

    return dto;
  }

  /// <summary>
  /// Maps an IPrimaryAudioSource to a NowPlayingDto by extracting metadata.
  /// </summary>
  /// <param name="source">The primary audio source to map.</param>
  /// <returns>The mapped DTO.</returns>
  public static NowPlayingDto MapToNowPlaying(this IPrimaryAudioSource source)
  {
    var dto = new NowPlayingDto();
    
    if (source.Metadata != null)
    {
      ExtractMetadataToNowPlaying(source.Metadata, dto);
    }
    
    return dto;
  }

  /// <summary>
  /// Extracts metadata from the source and populates the NowPlayingDto with defaults for missing values.
  /// </summary>
  private static void ExtractMetadataToNowPlaying(IReadOnlyDictionary<string, object> metadata, NowPlayingDto nowPlaying)
  {
    // Extract standard metadata keys with fallback to defaults
    nowPlaying.Title = GetMetadataValue(metadata, "Title") ?? "No Track";
    nowPlaying.Artist = GetMetadataValue(metadata, "Artist") ?? "--";
    nowPlaying.Album = GetMetadataValue(metadata, "Album") ?? "--";
    nowPlaying.AlbumArtUrl = GetMetadataValue(metadata, "AlbumArtUrl") ?? "/images/default-album-art.png";

    // Build extended metadata dictionary from non-standard keys
    var extendedKeys = metadata.Keys.Except(new[] { "Title", "Artist", "Album", "AlbumArtUrl" });
    if (extendedKeys.Any())
    {
      nowPlaying.ExtendedMetadata = new Dictionary<string, object>();
      foreach (var key in extendedKeys)
      {
        nowPlaying.ExtendedMetadata[key] = metadata[key];
      }
    }
  }

  /// <summary>
  /// Gets a metadata value as a string, handling null and type conversion.
  /// </summary>
  private static string? GetMetadataValue(IReadOnlyDictionary<string, object> metadata, string key)
  {
    if (metadata.TryGetValue(key, out var value) && value != null)
    {
      return value.ToString();
    }
    return null;
  }
}
