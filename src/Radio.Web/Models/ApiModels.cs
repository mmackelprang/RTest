namespace Radio.Web.Models;

// Audio API DTOs
public record PlaybackStateDto(
  bool IsPlaying,
  bool IsPaused,
  float Volume,
  bool IsMuted,
  float Balance,
  string? Position,
  string? Duration,
  bool CanPlay,
  bool CanPause,
  bool CanStop,
  bool CanSeek,
  bool CanNext,
  bool CanPrevious,
  bool CanShuffle,
  bool CanRepeat,
  bool CanQueue,
  bool IsShuffleEnabled,
  string RepeatMode
);

public record VolumeDto(float Volume, bool IsMuted);

public record NowPlayingDto(
  string? Title,
  string? Artist,
  string? Album,
  string? AlbumArtUrl,
  string? Source
);

public record UpdatePlaybackRequest(
  string? Action,
  float? Volume,
  bool? IsMuted,
  float? Balance
);

// Queue API DTOs
public record QueueItemDto(
  int Index,
  string? Title,
  string? Artist,
  string? Album,
  string? Duration,
  bool IsCurrent
);

// System API DTOs
public record SystemStatsDto(
  double CpuUsage,
  long RamUsageMb,
  int ThreadCount,
  double? Temperature
);
