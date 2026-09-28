using Microsoft.Extensions.Logging;
using SoundFlow.Metadata;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Tags and format facts read from an audio file. Blank tag values are reported as <c>null</c>,
/// never as an empty or whitespace string.
/// </summary>
/// <param name="Title">Title tag.</param>
/// <param name="Artist">Artist tag (TagLib: first performer).</param>
/// <param name="Album">Album tag.</param>
/// <param name="Genre">Genre tag (TagLib: first genre).</param>
/// <param name="Year">Release year, when tagged.</param>
/// <param name="TrackNumber">Track number, when tagged.</param>
/// <param name="Duration">TagLib's duration when it has one (it parses VBR headers), else SoundFlow's.</param>
/// <param name="SampleRate">Sample rate in Hz, when known.</param>
/// <param name="Channels">Channel count, when known.</param>
/// <param name="Bitrate">Average bitrate in bits per second (SoundFlow reports bps; TagLib's kbps is converted).</param>
/// <param name="ReadBy">Which reader produced the tags: <see cref="AudioTagReader.SoundFlowReader"/> or <see cref="AudioTagReader.TagLibReader"/>.</param>
public sealed record AudioFileTags(
  string? Title,
  string? Artist,
  string? Album,
  string? Genre,
  int? Year,
  int? TrackNumber,
  TimeSpan? Duration,
  int? SampleRate,
  int? Channels,
  int? Bitrate,
  string ReadBy);

/// <summary>
/// The one place audio-file tags are read (AUD-32). Tries SoundFlow's <see cref="SoundMetadataReader"/>
/// first and falls back to TagLib when SoundFlow fails.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ SoundFlow 1.4.1 appears to reject ID3v2.2 tags wholesale (<c>CorruptFrameError: A 'ID3v2.2' frame is
/// corrupted</c>), while TagLib reads them without complaint. First measured on two library files on the
/// appliance; a hand-built, well-formed v2.2 tag is rejected the same way. Before this class every caller treated that failure as "the file has no tags", so the
/// file player showed placeholders and AUD-1's per-field rule let fingerprinting fill the tagged fields.
/// </para>
/// <para>
/// Everything here logs at Debug only: queue rendering re-reads every queued file on each render, and on
/// the appliance log volume correlates with audible distortion. A caller that reads a file once — track
/// load — decides whether a <c>null</c> deserves a Warning.
/// </para>
/// </remarks>
public static class AudioTagReader
{
  /// <summary><see cref="AudioFileTags.ReadBy"/> when SoundFlow read the file.</summary>
  public const string SoundFlowReader = "SoundFlow";

  /// <summary><see cref="AudioFileTags.ReadBy"/> when SoundFlow failed and TagLib read the file.</summary>
  public const string TagLibReader = "TagLib";

  /// <summary>
  /// Reads <paramref name="filePath"/>'s tags. Returns <c>null</c> only when neither SoundFlow nor TagLib
  /// can read the file.
  /// </summary>
  public static AudioFileTags? Read(string filePath, ILogger? logger = null)
  {
    string? soundFlowFailure;
    try
    {
      var result = SoundMetadataReader.Read(filePath);
      if (result.IsSuccess && result.Value != null)
      {
        var formatInfo = result.Value;
        var tags = formatInfo.Tags;

        // TagLib for duration: SoundFlow miscalculates VBR MP3s (see AccurateDurationReader).
        TimeSpan? duration = AccurateDurationReader.GetDuration(filePath, logger);
        if (duration == null && formatInfo.Duration != TimeSpan.Zero)
        {
          duration = formatInfo.Duration;
        }

        return new AudioFileTags(
          Blank(tags?.Title),
          Blank(tags?.Artist),
          Blank(tags?.Album),
          Blank(tags?.Genre),
          (int?)tags?.Year,
          (int?)tags?.TrackNumber,
          duration,
          formatInfo.SampleRate,
          formatInfo.ChannelCount,
          formatInfo.Bitrate,
          SoundFlowReader);
      }

      soundFlowFailure = result.Error?.ToString() ?? "no result";
    }
    catch (Exception ex)
    {
      soundFlowFailure = ex.Message;
    }

    try
    {
      using var file = TagLib.File.Create(filePath);
      var tag = file.Tag;
      var properties = file.Properties;

      logger?.LogDebug(
        "SoundFlow could not read tags from {File} ({Failure}); read them with TagLib",
        filePath, soundFlowFailure);

      return new AudioFileTags(
        Blank(tag.Title),
        Blank(tag.FirstPerformer),
        Blank(tag.Album),
        Blank(tag.FirstGenre),
        tag.Year > 0 ? (int)tag.Year : null,
        tag.Track > 0 ? (int)tag.Track : null,
        properties?.Duration > TimeSpan.Zero ? properties.Duration : null,
        properties?.AudioSampleRate > 0 ? properties.AudioSampleRate : null,
        properties?.AudioChannels > 0 ? properties.AudioChannels : null,
        // TagLib reports kbps; SoundFlow (and so this record) uses bps. The metadata dictionary is
        // published over the API, so the two readers must agree.
        properties?.AudioBitrate > 0 ? properties.AudioBitrate * 1000 : null,
        TagLibReader);
    }
    catch (Exception ex)
    {
      logger?.LogDebug(
        "Could not read tags from {File}: SoundFlow failed ({Failure}) and TagLib failed ({TagLibFailure})",
        filePath, soundFlowFailure, ex.Message);
      return null;
    }
  }

  private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
