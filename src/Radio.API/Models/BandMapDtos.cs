using Radio.Core.Models;

namespace Radio.API.Models;

/// <summary>
/// Response of <c>GET /api/radio/bandmap</c> (AUD-76). Before the first completed sweep,
/// <see cref="ScannedAtUtc"/> and <see cref="AgeSeconds"/> are null and <see cref="Channels"/> is empty.
/// </summary>
public sealed record BandMapResponseDto
{
  /// <summary>Band name.</summary>
  public string Band { get; init; } = "FM";

  /// <summary>When the stored map was produced, UTC, or null when there is no map.</summary>
  public DateTimeOffset? ScannedAtUtc { get; init; }

  /// <summary>Age of the stored map in seconds, or null when there is no map.</summary>
  public double? AgeSeconds { get; init; }

  /// <summary>Measured channels, ascending by frequency.</summary>
  public IReadOnlyList<BandMapChannelDto> Channels { get; init; } = Array.Empty<BandMapChannelDto>();

  /// <summary>Sweep status, including the last outcome.</summary>
  public BandSweepStatusDto Sweep { get; init; } = new();

  /// <summary>Maps the service's map and status.</summary>
  public static BandMapResponseDto From(BandMap? map, TimeSpan? age, BandSweepStatus status) => new()
  {
    Band = map?.Band ?? "FM",
    ScannedAtUtc = map?.ScannedAtUtc,
    AgeSeconds = age?.TotalSeconds,
    Channels = map?.Channels.Select(c => new BandMapChannelDto(c.FrequencyHz, c.LevelDbfs)).ToArray()
      ?? Array.Empty<BandMapChannelDto>(),
    Sweep = BandSweepStatusDto.From(status),
  };
}

/// <summary>One channel of the band map.</summary>
/// <param name="FrequencyHz">Channel centre, in Hz.</param>
/// <param name="LevelDbfs">Relative level, in dB.</param>
public sealed record BandMapChannelDto(long FrequencyHz, float LevelDbfs);

/// <summary>Band sweep status.</summary>
public sealed record BandSweepStatusDto
{
  /// <summary>True while a sweep is running.</summary>
  public bool IsSweeping { get; init; }

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
    Trigger = status.Trigger,
    Path = status.Path,
    Progress = status.Progress,
    StartedAtUtc = status.StartedAtUtc,
    EstimatedSecondsRemaining = status.EstimatedSecondsRemaining,
    Last = status.Last == null
      ? null
      : new BandSweepOutcomeDto(
        status.Last.Trigger, status.Last.Path, status.Last.StartedAtUtc, status.Last.DurationMs,
        status.Last.Result, status.Last.Reason, status.Last.ChannelsMeasured),
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
public sealed record BandSweepOutcomeDto(
  string Trigger, string? Path, DateTimeOffset StartedAtUtc, long DurationMs, string Result, string? Reason, int ChannelsMeasured);
