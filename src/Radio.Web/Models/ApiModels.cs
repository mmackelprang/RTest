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

// Sources API DTOs
public record AudioSourceDto(
  string Id,
  string Name,
  string Type,
  string Category,
  string State,
  float Volume,
  Dictionary<string, string>? Metadata
);

// Devices API DTOs
public record AudioDeviceDto(
  string Id,
  string Name,
  string Type,
  bool IsDefault,
  bool IsActive
);

public record UsbPortDto(
  string Id,
  string Name,
  bool IsReserved,
  string? ReservedBy
);

// Metrics API DTOs
public record MetricDto(
  string Name,
  double Value,
  string Unit,
  DateTime Timestamp,
  Dictionary<string, string>? Tags
);

public record MetricHistoryDto(
  DateTime Timestamp,
  double Value
);

// File API DTOs
public record FileListDto(
  string CurrentPath,
  List<FileItemDto> Items
);

public record FileItemDto(
  string Name,
  string Path,
  bool IsDirectory,
  long? Size,
  string? Duration,
  string? Artist,
  string? Album
);

// System API DTOs
public record SystemStatsDto(
  double CpuUsage,
  long RamUsageMb,
  int ThreadCount,
  double? Temperature
);
