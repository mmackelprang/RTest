using Radio.Core.Models;

namespace Radio.API.Models;

/// <summary>
/// Response of <c>GET /api/radio/bandmap</c> (AUD-76; per band since AUD-91). Before the band's
/// first completed sweep, <see cref="ScannedAtUtc"/> and <see cref="AgeSeconds"/> are null and
/// <see cref="Channels"/> is empty.
/// </summary>
public sealed record BandMapResponseDto
{
  /// <summary>Band code the response describes: the one asked for, or the radio's current band.</summary>
  public string Band { get; init; } = "FM";

  /// <summary>True when the band has a sweep plan and so can be scanned.</summary>
  public bool Mappable { get; init; } = true;

  /// <summary>Why the band cannot be scanned, as a sentence for the owner; null when <see cref="Mappable"/>.</summary>
  public string? UnavailableReason { get; init; }

  /// <summary>Lower edge of the band's frequency axis, in Hz.</summary>
  public long DisplayMinHz { get; init; }

  /// <summary>Upper edge of the band's frequency axis, in Hz.</summary>
  public long DisplayMaxHz { get; init; }

  /// <summary>First channel centre of the band's grid, in Hz (the band's lower edge when not mappable).</summary>
  public long FirstChannelHz { get; init; }

  /// <summary>Last channel centre of the band's grid, in Hz (the band's upper edge when not mappable).</summary>
  public long LastChannelHz { get; init; }

  /// <summary>Channel grid spacing, in Hz (the band's default tuning step when not mappable).</summary>
  public long ChannelSpacingHz { get; init; }

  /// <summary>When the stored map was produced, UTC, or null when there is no map.</summary>
  public DateTimeOffset? ScannedAtUtc { get; init; }

  /// <summary>Age of the stored map in seconds, or null when there is no map.</summary>
  public double? AgeSeconds { get; init; }

  /// <summary>Measured channels, ascending by frequency.</summary>
  public IReadOnlyList<BandMapChannelDto> Channels { get; init; } = Array.Empty<BandMapChannelDto>();

  /// <summary>
  /// Stations a live Scan Up/Down stopped on since the band's last sweep of their frequency
  /// (AUD-100), ascending by frequency. Separate from <see cref="Channels"/>, and they never change
  /// <see cref="ScannedAtUtc"/> or <see cref="AgeSeconds"/>: a band with only these has no map.
  /// </summary>
  public IReadOnlyList<BandMapSeekStationDto> SeekStations { get; init; } = Array.Empty<BandMapSeekStationDto>();

  /// <summary>Sweep status, including the last outcome. Not filtered by band: see <see cref="BandSweepStatusDto.Band"/>.</summary>
  public BandSweepStatusDto Sweep { get; init; } = new();

  /// <summary>
  /// Maps the service's map, seek-observed stations and status. The band axis fields are left at
  /// their defaults.
  /// </summary>
  public static BandMapResponseDto From(
    BandMap? map, TimeSpan? age, BandSweepStatus status, IReadOnlyList<BandMapSeekStation>? seekStations = null) => new()
  {
    Band = map?.Band ?? "FM",
    ScannedAtUtc = map?.ScannedAtUtc,
    AgeSeconds = age?.TotalSeconds,
    Channels = map?.Channels.Select(c => new BandMapChannelDto(c.FrequencyHz, c.LevelDbfs)).ToArray()
      ?? Array.Empty<BandMapChannelDto>(),
    SeekStations = seekStations?.Select(s => new BandMapSeekStationDto(s.FrequencyHz, s.SeekStrength, s.ObservedAtUtc)).ToArray()
      ?? Array.Empty<BandMapSeekStationDto>(),
    Sweep = BandSweepStatusDto.From(status),
  };
}

/// <summary>One channel of the band map.</summary>
/// <param name="FrequencyHz">Channel centre, in Hz.</param>
/// <param name="LevelDbfs">Relative level, in dB.</param>
public sealed record BandMapChannelDto(long FrequencyHz, float LevelDbfs);

/// <summary>A station a live seek stopped on (AUD-100).</summary>
/// <param name="FrequencyHz">Where the seek stopped, in Hz.</param>
/// <param name="SeekStrength">The seek's own wideband signal reading, 0 to about 1.2; not comparable with <see cref="BandMapChannelDto.LevelDbfs"/>.</param>
/// <param name="ObservedAtUtc">When, UTC.</param>
public sealed record BandMapSeekStationDto(long FrequencyHz, float SeekStrength, DateTimeOffset ObservedAtUtc);

/// <summary>Band sweep status.</summary>
public sealed record BandSweepStatusDto
{
  /// <summary>True while a sweep is running.</summary>
  public bool IsSweeping { get; init; }

  /// <summary>Band code of the running sweep, or null.</summary>
  public string? Band { get; init; }

  /// <summary>Trigger of the running sweep (<c>timer</c>, <c>sleep</c>, <c>request</c>), or null.</summary>
  public string? Trigger { get; init; }

  /// <summary>Path of the running sweep (<c>idle</c>, <c>live</c>), or null.</summary>
  public string? Path { get; init; }

  /// <summary>Fraction of channels processed, 0 to 1.</summary>
  public double Progress { get; init; }

  /// <summary>When the running sweep started, UTC, or null.</summary>
  public DateTimeOffset? StartedAtUtc { get; init; }

  /// <summary>Extrapolated seconds remaining, or null.</summary>
  public double? EstimatedSecondsRemaining { get; init; }

  /// <summary>The last finished or skipped attempt, or null.</summary>
  public BandSweepOutcomeDto? Last { get; init; }

  /// <summary>Maps a service status.</summary>
  public static BandSweepStatusDto From(BandSweepStatus status) => new()
  {
    IsSweeping = status.IsSweeping,
    Band = status.Band,
    Trigger = status.Trigger,
    Path = status.Path,
    Progress = status.Progress,
    StartedAtUtc = status.StartedAtUtc,
    EstimatedSecondsRemaining = status.EstimatedSecondsRemaining,
    Last = status.Last == null
      ? null
      : new BandSweepOutcomeDto(
        status.Last.Trigger, status.Last.Path, status.Last.StartedAtUtc, status.Last.DurationMs,
        status.Last.Result, status.Last.Reason, status.Last.ChannelsMeasured, status.Last.Band),
  };
}

/// <summary>The last sweep attempt.</summary>
/// <param name="Trigger">What started it.</param>
/// <param name="Path">Device path, or null when no sweep ran.</param>
/// <param name="StartedAtUtc">Start time, UTC.</param>
/// <param name="DurationMs">Duration in milliseconds.</param>
/// <param name="Result"><c>completed</c>, <c>cancelled</c>, <c>failed</c> or <c>skipped</c>.</param>
/// <param name="Reason">Why it did not complete, or null.</param>
/// <param name="ChannelsMeasured">Channels measured.</param>
/// <param name="Band">Band code of the attempt, or null when not recorded.</param>
public sealed record BandSweepOutcomeDto(
  string Trigger, string? Path, DateTimeOffset StartedAtUtc, long DurationMs, string Result, string? Reason, int ChannelsMeasured,
  string? Band = null);
