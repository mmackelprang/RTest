using Microsoft.Extensions.Logging;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Reads accurate audio file duration using TagLib#.
/// SoundFlow's SoundMetadataReader miscalculates VBR MP3 duration (uses first frame
/// bitrate for CBR estimation instead of reading Xing/VBRI headers), resulting in
/// durations that are often 1.5-2x too long. TagLib# correctly parses VBR headers.
/// </summary>
public static class AccurateDurationReader
{
  /// <summary>
  /// Gets the accurate duration for an audio file.
  /// Returns null if the file cannot be read or duration cannot be determined.
  /// </summary>
  public static TimeSpan? GetDuration(string filePath, ILogger? logger = null)
  {
    try
    {
      using var tagFile = TagLib.File.Create(filePath);
      var duration = tagFile.Properties.Duration;
      if (duration > TimeSpan.Zero)
      {
        return duration;
      }
    }
    catch (Exception ex)
    {
      logger?.LogDebug(ex, "TagLib failed to read duration for {File}, will fall back to SoundFlow", filePath);
    }

    return null;
  }
}
