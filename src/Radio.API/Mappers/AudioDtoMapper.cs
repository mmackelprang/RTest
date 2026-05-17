using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Events;

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
      IsStreaming = source.Type is AudioSourceType.Bluetooth or AudioSourceType.Radio,
      
      // Build capabilities dictionary
      Capabilities = new Dictionary<string, bool>()
    };

    if (source is IPrimaryAudioSource primary)
    {
      dto.IsSeekable = primary.IsSeekable;
      dto.Metadata = primary.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

      // Bluetooth metadata enrichment
      if (source.Type == AudioSourceType.Bluetooth)
      {
        dto.Metadata["Source"] = "Bluetooth";
        dto.Metadata["Device"] = primary.Metadata.TryGetValue("Device", out var device)
          ? device ?? "Bluetooth Device"
          : "Bluetooth Device";
      }

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
    else if (source is IEventAudioSource eventSource)
    {
      // Extract metadata for event sources
      dto.Metadata = new Dictionary<string, object>
      {
        ["Duration"] = eventSource.Duration.TotalSeconds
      };

      // Add TTS-specific metadata
      if (source is TTSEventSource ttsSource)
      {
        dto.Metadata["Text"] = ttsSource.Text;
        dto.Metadata["Engine"] = ttsSource.Parameters.Engine.ToString();
        dto.Metadata["Voice"] = ttsSource.Parameters.Voice;
        dto.Metadata["Speed"] = ttsSource.Parameters.Speed;
        dto.Metadata["Pitch"] = ttsSource.Parameters.Pitch;
      }
      // Add audio file event-specific metadata
      else if (source is AudioFileEventSource fileSource)
      {
        dto.Metadata["FilePath"] = fileSource.FilePath;
      }

      // Event sources support play and stop
      dto.Capabilities["SupportsPlay"] = true;
      dto.Capabilities["SupportsStop"] = true;
    }
    
    // Add radio-specific capabilities if source implements IRadioControl
    // All sources implementing IRadioControl support these core radio features by design
    if (source is IRadioControl)
    {
      dto.Capabilities["SupportsRadioControls"] = true;
      dto.Capabilities["SupportsFrequencyTuning"] = true; // Required by IRadioControl
      dto.Capabilities["SupportsScanning"] = true; // Required by IRadioControl
      dto.Capabilities["SupportsEqualizer"] = true; // Required by IRadioControl
      dto.Capabilities["SupportsDeviceVolume"] = true; // Required by IRadioControl
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

    // Promote FilePath out of the typeless metadata bag into a first-class DTO field.
    // The Web layer's DisplayNames.Track helper consumes this to derive a clean title
    // when only a generic "Track N" was produced by the metadata reader.
    nowPlaying.FilePath = GetMetadataValue(metadata, "FilePath");

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
  /// Maps a FingerprintStatusSnapshot to a FingerprintStatusDto.
  /// </summary>
  public static FingerprintStatusDto MapToDto(this FingerprintStatusSnapshot snapshot)
  {
    return new FingerprintStatusDto
    {
      Phase = snapshot.Phase.ToString(),
      IsEnabled = snapshot.IsEnabled,
      FingerprintsPerMinute = Math.Round(snapshot.FingerprintsPerMinute, 1),
      MetadataCallsPerMinute = Math.Round(snapshot.MetadataCallsPerMinute, 1),
      LastError = snapshot.LastError,
      RecentEvents = snapshot.RecentEvents.Select(e => new FingerprintEventDto
      {
        MatchId = e.MatchId,
        AudioSource = e.AudioSource,
        SourceType = e.SourceType,
        IsMatch = e.IsMatch,
        Count = e.Count,
        // PR 2 of the Radio Controller Polish arc — the raw double is folded
        // into a coarse bucket at the API boundary and dropped from the wire
        // shape. The raw score stays on the server-side record for logging.
        Confidence = ToConfidenceBucket(e.IsMatch, e.LastConfidence),
        Title = e.Title,
        Artist = e.Artist,
        Album = e.Album,
        HasAlbumArt = e.HasAlbumArt,
        Phase = e.Phase.ToString(),
        Timestamp = e.Timestamp
      }).ToList()
    };
  }

  /// <summary>
  /// Folds a raw fingerprint confidence score into a coarse
  /// <see cref="ConfidenceBucket"/>. Thresholds:
  /// ≥ 0.90 → <c>Strong</c>; 0.80–0.89 → <c>Likely</c>;
  /// 0.60–0.79 → <c>Possible</c>; otherwise <c>None</c>.
  /// </summary>
  /// <param name="isMatch">Whether the fingerprint pipeline produced a match.</param>
  /// <param name="rawConfidence">Raw confidence score on [0, 1], or null when no match.</param>
  public static ConfidenceBucket ToConfidenceBucket(bool isMatch, double? rawConfidence)
  {
    if (!isMatch || rawConfidence is not double score)
    {
      return ConfidenceBucket.None;
    }

    if (score >= 0.90)
    {
      return ConfidenceBucket.Strong;
    }

    if (score >= 0.80)
    {
      return ConfidenceBucket.Likely;
    }

    if (score >= 0.60)
    {
      return ConfidenceBucket.Possible;
    }

    return ConfidenceBucket.None;
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
