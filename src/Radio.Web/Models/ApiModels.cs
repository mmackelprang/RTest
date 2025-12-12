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

// Play History API DTOs
public record PlayHistoryListDto(
  int TotalCount,
  List<PlayHistoryItemDto> Items
);

public record PlayHistoryItemDto(
  string Id,
  string? Title,
  string? Artist,
  string? Album,
  string Source,
  DateTime PlayedAt,
  int DurationSeconds
);

public record PlayHistoryStatsDto(
  int TotalPlays,
  int UniqueTracksCount,
  string MostPlayedTrack,
  string MostPlayedArtist,
  Dictionary<string, int> PlaysBySource
);

// Spotify API DTOs
public record SpotifyAuthStatusDto(
  bool IsAuthenticated,
  string? UserName,
  DateTime? ExpiresAt
);

public record SpotifySearchResultsDto(
  List<SpotifyTrackDto>? Tracks,
  List<SpotifyAlbumDto>? Albums,
  List<SpotifyArtistDto>? Artists,
  List<SpotifyPlaylistDto>? Playlists
);

public record SpotifyTrackDto(
  string Id,
  string Name,
  string Artist,
  string Album,
  string? AlbumArtUrl,
  int DurationMs,
  string Uri
);

public record SpotifyAlbumDto(
  string Id,
  string Name,
  string Artist,
  string? ImageUrl,
  int TotalTracks
);

public record SpotifyArtistDto(
  string Id,
  string Name,
  string? ImageUrl,
  int Followers
);

public record SpotifyPlaylistDto(
  string Id,
  string Name,
  string? Description,
  string? ImageUrl,
  int TotalTracks
);

public record SpotifyCategoryDto(
  string Id,
  string Name,
  string? IconUrl
);

public record SpotifyUserDto(
  string Id,
  string DisplayName,
  string? Email,
  string? ImageUrl
);

public record SpotifyAuthUrlDto(
  string Url,
  string State,
  string CodeVerifier
);

// Radio API DTOs
public record RadioStateDto(
  double Frequency,
  string Band,
  double Step,
  int? SignalStrength,
  bool IsScanning,
  string? ScanDirection,
  int? Gain,
  bool AutoGain,
  string? Equalizer,
  int? DeviceVolume
);

public record RadioPowerStateDto(
  bool IsPoweredOn
);

public record RadioPresetDto(
  int Slot,
  string Name,
  double Frequency,
  string Band
);

public record RadioDeviceDto(
  string Type,
  string Name,
  bool IsAvailable,
  Dictionary<string, string>? Capabilities
);

// System API DTOs
public record SystemStatsDto(
  double CpuUsagePercent,
  double RamUsageMb,
  double DiskUsagePercent,
  int ThreadCount,
  string AppUptime,
  string SystemUptime,
  string AudioEngineState,
  string SystemTemperature
);

public record LogEntryDto(
  DateTime Timestamp,
  string Level,
  string Message,
  string? Exception,
  string SourceContext
);

public record SystemLogsResponse(
  List<LogEntryDto> Logs,
  int TotalCount,
  LogFilters Filters
);

public record LogFilters(
  string Level,
  int Limit,
  int? MaxAgeMinutes
);
