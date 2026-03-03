using System.Text.Json.Serialization;

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
  bool CanReorderQueue,
  bool IsShuffleEnabled,
  string RepeatMode
);

public record VolumeDto(float Volume, bool IsMuted);

public class NowPlayingDto
{
  public string SourceType { get; set; } = string.Empty;
  public string SourceName { get; set; } = string.Empty;
  public bool IsPlaying { get; set; }
  public bool IsPaused { get; set; }
  public string Title { get; set; } = "No Track";
  public string Artist { get; set; } = "--";
  public string Album { get; set; } = "--";
  public string AlbumArtUrl { get; set; } = "/images/default-album-art.png";
  public TimeSpan? Position { get; set; }
  public TimeSpan? Duration { get; set; }
  public double? ProgressPercentage { get; set; }
  public Dictionary<string, object>? ExtendedMetadata { get; set; }
}

public record UpdatePlaybackRequest(
  string? Action,
  float? Volume,
  bool? IsMuted,
  float? Balance,
  TimeSpan? SeekPosition = null
);

// Queue API DTOs
public record QueueItemDto(
  int Index,
  string? Title,
  string? Artist,
  string? Album,
  string? Duration,
  bool IsCurrent,
  string State = "Upcoming",
  int FullPlaylistIndex = 0
);

// Sources API DTOs
public class AvailableSourcesDto
{
  [JsonPropertyName("primarySources")]
  public List<string> PrimarySources { get; set; } = [];

  [JsonPropertyName("activeSourceType")]
  public string? ActiveSourceType { get; set; }

  [JsonPropertyName("activeSources")]
  public List<AudioSourceDto> ActiveSources { get; set; } = [];
}

public class AudioSourceDto
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("type")]
  public string Type { get; set; } = string.Empty;

  [JsonPropertyName("category")]
  public string Category { get; set; } = string.Empty;

  [JsonPropertyName("state")]
  public string State { get; set; } = string.Empty;

  [JsonPropertyName("volume")]
  public float Volume { get; set; }

  [JsonPropertyName("isSeekable")]
  public bool IsSeekable { get; set; }

  [JsonPropertyName("metadata")]
  public Dictionary<string, object>? Metadata { get; set; }

  [JsonPropertyName("isRadio")]
  public bool IsRadio { get; set; }

  [JsonPropertyName("isStreaming")]
  public bool IsStreaming { get; set; }

  [JsonPropertyName("hasQueue")]
  public bool HasQueue { get; set; }

  [JsonPropertyName("capabilities")]
  public Dictionary<string, bool>? Capabilities { get; set; }
}

// Devices API DTOs
public class AudioDeviceDto
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("type")]
  public string Type { get; set; } = string.Empty;

  [JsonPropertyName("isDefault")]
  public bool IsDefault { get; set; }

  [JsonPropertyName("isActive")]
  public bool IsActive { get; set; }

  [JsonPropertyName("isUSBDevice")]
  public bool IsUSBDevice { get; set; }

  [JsonPropertyName("usbPort")]
  public string? USBPort { get; set; }

  [JsonPropertyName("maxChannels")]
  public int MaxChannels { get; set; }

  [JsonPropertyName("supportedSampleRates")]
  public int[]? SupportedSampleRates { get; set; }
}

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
  double Value,
  int Count,
  double? Min,
  double? Max,
  double? Last,
  Dictionary<string, string>? Tags
);

public record MetricAggregateDto(
  int Count,
  double Sum,
  double Average,
  double Min,
  double Max,
  double StdDev
);

public record MetricEventRequest(
  string EventName,
  Dictionary<string, string>? Tags
);

public record MetricEventResponse(
  bool Success,
  string Metric
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

public record DriveInfoDto(
  string Name,
  string Label,
  string DriveType,
  bool IsReady,
  long TotalSize,
  long AvailableSpace,
  string? DriveFormat
);

public record QueueFilesResponseDto(
  bool Success,
  string? Message,
  int AddedCount,
  int FailedCount,
  List<string>? FailedPaths
);

// Play History API DTOs
public record PlayHistoryListDto(
  int TotalCount,
  List<PlayHistoryEntryDto> Items
);

public record PlayHistoryEntryDto(
  string Id,
  DateTime PlayedAt,
  DateTime? EndedAt,
  string Source,
  string? MetadataSource,
  string? SourceDetails,
  int? DurationSeconds,
  double? IdentificationConfidence,
  bool WasIdentified,
  PlayHistoryTrackDto? Track
)
{
  // Helper properties for flat access
  public string? Title => Track?.Title;
  public string? Artist => Track?.Artist;
  public string? Album => Track?.Album;
}

public record PlayHistoryTrackDto(
  string? Title,
  string? Artist,
  string? Album,
  string? AlbumArtist,
  string? CoverArtUrl
);

public record ArtistPlayCountDto(string Artist, int PlayCount);
public record TrackPlayCountDto(string Title, string Artist, int PlayCount);

public record PlayHistoryStatsDto(
  int TotalPlays,
  int IdentifiedPlays,
  int UnidentifiedPlays,
  Dictionary<string, int> PlaysBySource,
  List<ArtistPlayCountDto> TopArtists,
  List<TrackPlayCountDto> TopTracks
);

// Radio API DTOs
public record RadioStateDto(
  double Frequency,
  string Band,
  double Step,
  int? SignalStrength,
  bool IsScanning,
  string? ScanDirection,
  int ScanStopThreshold,
  int? Gain,
  bool AutoGain,
  string? Equalizer,
  int? DeviceVolume,
  bool IsStereo = false
);

public record RadioPowerStateDto(
  bool IsPoweredOn
);

public record RadioPresetDto(
  string Id,
  string Name,
  double Frequency,
  string Band,
  DateTimeOffset? CreatedAt = null
);

public record RadioDeviceDto(
  string Type,
  string Name,
  bool IsAvailable,
  Dictionary<string, string>? Capabilities
);

// Configuration API DTOs
public class AudioConfigurationDto
{
  public string? DefaultSource { get; set; }
  public int DuckingPercentage { get; set; }
  public string? DuckingPolicy { get; set; }
  public int DuckingAttackMs { get; set; }
  public int DuckingReleaseMs { get; set; }
}

public class VisualizerConfigurationDto
{
  public int FFTSize { get; set; }
  public int WaveformSampleCount { get; set; }
  public int PeakHoldTimeMs { get; set; }
  public bool ApplyWindowFunction { get; set; }
  public float SpectrumSmoothing { get; set; }
}

public class OutputConfigurationDto
{
  public LocalOutputSettingsDto? Local { get; set; }
  public HttpStreamSettingsDto? HttpStream { get; set; }
  public GoogleCastSettingsDto? GoogleCast { get; set; }
}

public class LocalOutputSettingsDto
{
  public bool Enabled { get; set; }
  public string? PreferredDeviceId { get; set; }
  public float DefaultVolume { get; set; }
}

public class HttpStreamSettingsDto
{
  public bool Enabled { get; set; }
  public int Port { get; set; }
  public string? EndpointPath { get; set; }
  public int SampleRate { get; set; }
  public int Channels { get; set; }
}

public class GoogleCastSettingsDto
{
  public bool Enabled { get; set; }
  public int DiscoveryTimeoutSeconds { get; set; }
  public float DefaultVolume { get; set; }
  public float DirectChannelMaxBufferAhead { get; set; } = 3.0f;
  public int DirectChannelBufferBeforePlay { get; set; } = 3;
  public float DirectChannelReaderLagSeconds { get; set; } = 1.0f;
}

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

// Visualization API DTOs
public record SpectrumDataDto
{
  public float[] Magnitudes { get; init; } = [];
  public float[] Frequencies { get; init; } = [];
  public int BinCount { get; init; }
  public float FrequencyResolution { get; init; }
  public float MaxFrequency { get; init; }
  public long TimestampMs { get; init; }
}

public record LevelDataDto
{
  public float LeftPeak { get; init; }
  public float RightPeak { get; init; }
  public float LeftRms { get; init; }
  public float RightRms { get; init; }
  public float LeftPeakDb { get; init; }
  public float RightPeakDb { get; init; }
  public bool IsClipping { get; init; }
  public long TimestampMs { get; init; }
}

public record WaveformDataDto
{
  public float[] LeftSamples { get; init; } = [];
  public float[] RightSamples { get; init; } = [];
  public int SampleCount { get; init; }
  public double DurationMs { get; init; }
  public long TimestampMs { get; init; }
}

public record VisualizationDataDto
{
  public SpectrumDataDto? Spectrum { get; init; }
  public LevelDataDto? Levels { get; init; }
  public WaveformDataDto? Waveform { get; init; }
  public bool IsActive { get; init; }
}

// Event Sources API DTOs
public record TTSEngineInfoDto(
  string Engine,
  string Name,
  bool IsAvailable,
  bool RequiresApiKey,
  bool IsOffline
);

public record TTSVoiceInfoDto(
  string Id,
  string Name,
  string Language,
  string Gender,
  bool IsFavorite = false,
  string PriceTier = "Standard"
);

public record PlayTTSRequest(
  string Text,
  string? Engine = null,
  string? Voice = null,
  float? Speed = null,
  float? Pitch = null
);

public record PlayFileEventRequest(
  string FilePath
);

public record NotificationSoundDto(
  string FileName,
  string FilePath,
  long FileSize
);

// Device Display DTOs
public class DeviceDisplayInfoDto
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("rawName")]
  public string RawName { get; set; } = string.Empty;

  [JsonPropertyName("displayName")]
  public string DisplayName { get; set; } = string.Empty;

  [JsonPropertyName("isHidden")]
  public bool IsHidden { get; set; }

  [JsonPropertyName("friendlyNameOverride")]
  public string? FriendlyNameOverride { get; set; }

  [JsonPropertyName("type")]
  public string Type { get; set; } = string.Empty;

  [JsonPropertyName("isDefault")]
  public bool IsDefault { get; set; }

  [JsonPropertyName("isUSBDevice")]
  public bool IsUSBDevice { get; set; }
}

// Cast Device DTOs
public record CastDeviceDto(
  string Id,
  string Name,
  string IpAddress,
  int Port,
  string Model
);

public record ConnectCastDeviceRequest(
  string DeviceId,
  string? Name,
  string IpAddress,
  int? Port,
  string? Model
);

// Device Configuration DTOs
// Note: These DTOs mirror Radio.API.Models.ConfigurationModels but are duplicated
// here because Radio.Web does not reference Radio.API (architectural separation).
// The Web layer communicates with the API via HTTP and maintains its own DTOs.
public class DeviceOptionsDto
{
  public RadioDeviceOptionsDto Radio { get; set; } = new();
  public VinylDeviceOptionsDto Vinyl { get; set; } = new();
}

public class RadioDeviceOptionsDto
{
  public string USBPort { get; set; } = "/dev/ttyUSB0";
}

public class VinylDeviceOptionsDto
{
  public string USBPort { get; set; } = "/dev/ttyUSB1";
}

// Preferences DTOs (Phase 2)
public class AudioPreferencesDto
{
  public string CurrentSource { get; set; } = "Radio";
  public string CurrentOutput { get; set; } = "";
  public int MasterVolume { get; set; } = 75;
}

public class FilePlayerPreferencesDto
{
  public string LastSongPlayed { get; set; } = "";
  public long SongPositionMs { get; set; } = 0;
  public bool Shuffle { get; set; } = false;
  public string Repeat { get; set; } = "Off";
}

public class RadioPreferencesDto
{
  public float LastFrequency { get; set; } = 101.1f;
  public string LastBand { get; set; } = "FM";
  public string LastEQMode { get; set; } = "";
}

public class GenericSourcePreferencesDto
{
  public string USBPort { get; set; } = "";
}

// Secrets DTOs
public class TTSSecretsDto
{
  public string GoogleAPIKey { get; set; } = "";
  public string AzureAPIKey { get; set; } = "";
  public string AzureRegion { get; set; } = "";
}

public class AcoustIdSecretDto
{
  public string ApiKey { get; set; } = "";
}

// ========== Phase 5: Configuration Store Management DTOs ==========

public class ConfigurationStoreInfoDto
{
  public string StoreType { get; set; } = "";
  public string Location { get; set; } = "";
  public long SizeBytes { get; set; }
  public DateTime? LastModified { get; set; }
  public int EntryCount { get; set; }
}

public class ConfigurationComparisonDto
{
  public int JsonEntryCount { get; set; }
  public int SqliteEntryCount { get; set; }
  public List<ConfigurationDifferenceDto> Differences { get; set; } = new();
}

public class ConfigurationDifferenceDto
{
  public string Key { get; set; } = "";
  public string? JsonValue { get; set; }
  public string? SqliteValue { get; set; }
  public string Status { get; set; } = "";
}

public class ReconcileConfigurationRequestDto
{
  public string SourceStore { get; set; } = "";
  public string TargetStore { get; set; } = "";
  public List<string> Keys { get; set; } = new();
}

// Playlist DTOs
public class PlaylistSummaryDto
{
  public string Id { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string? Description { get; set; }
  public int ItemCount { get; set; }
  public string CreatedAt { get; set; } = string.Empty;
  public string ModifiedAt { get; set; } = string.Empty;
}

public class PlaylistDetailDto : PlaylistSummaryDto
{
  public List<PlaylistItemDto> Items { get; set; } = new();
}

public class PlaylistItemDto
{
  public string FilePath { get; set; } = string.Empty;
  public string? Title { get; set; }
  public string? Artist { get; set; }
  public string? Album { get; set; }
  public int? DurationMs { get; set; }
}

// ========== Configuration Section DTOs ==========

public class BluetoothConfigDto
{
  public string DeviceName { get; set; } = "Radio Console";
  public bool AutoAcceptConnections { get; set; } = true;
  public bool RequirePairing { get; set; }
  public bool Enabled { get; set; } = true;
  public bool EnableOnStartup { get; set; } = true;
  public bool AutoSwitchOnConnect { get; set; } = true;
  public string AudioQuality { get; set; } = "High";
}

public class RadioConfigDto
{
  public string DefaultDevice { get; set; } = "RTLSDRCore";
  public double DefaultFMFrequencyMHz { get; set; } = 101.5;
  public double DefaultAMFrequencyKHz { get; set; } = 1000.0;
  public double DefaultFMStepMHz { get; set; } = 0.1;
  public double DefaultAMStepKHz { get; set; } = 10.0;
  public double MinFMFrequencyMHz { get; set; } = 87.5;
  public double MaxFMFrequencyMHz { get; set; } = 108.0;
  public double MinAMFrequencyKHz { get; set; } = 520.0;
  public double MaxAMFrequencyKHz { get; set; } = 1710.0;
  public int ScanStopThreshold { get; set; } = 50;
  public int ScanStepDelayMs { get; set; } = 100;
  public int DefaultDeviceVolume { get; set; } = 50;
}

public class FilePlayerConfigDto
{
  public string RootDirectory { get; set; } = "media/audio";
  public string SupportedExtensions { get; set; } = ".mp3,.flac,.wav,.ogg,.aac,.m4a,.wma";
}

public class FingerprintingConfigDto
{
  public bool Enabled { get; set; } = true;
  public int SampleDurationSeconds { get; set; } = 15;
  public int IdentificationIntervalSeconds { get; set; } = 30;
  public double MinimumConfidenceThreshold { get; set; } = 0.5;
  public int DuplicateSuppressionMinutes { get; set; } = 5;
  public string FpcalcPath { get; set; } = string.Empty;
  public string DatabasePath { get; set; } = "./data/fingerprints.db";
}

public class TTSConfigDto
{
  public string DefaultEngine { get; set; } = "ESpeak";
  public string DefaultVoice { get; set; } = "en";
  public float DefaultPitch { get; set; } = 1.0f;
  public float DefaultSpeed { get; set; } = 1.0f;
  public string ESpeakPath { get; set; } = "espeak-ng";
  public int GenerationTimeoutSeconds { get; set; } = 30;
}

public class MetricsConfigDto
{
  public bool Enabled { get; set; } = true;
  public int FlushIntervalSeconds { get; set; } = 60;
  public string DatabasePath { get; set; } = "./data/metrics.db";
  public int RetentionMinuteData { get; set; } = 120;
  public int RetentionHourData { get; set; } = 48;
  public int RetentionDayData { get; set; } = 365;
  public int RollupIntervalMinutes { get; set; } = 60;
}

public class AudioEngineConfigDto
{
  public int SampleRate { get; set; } = 48000;
  public int Channels { get; set; } = 2;
  public int BufferSize { get; set; } = 1024;
  public int HotPlugIntervalSeconds { get; set; } = 5;
  public int OutputBufferSizeSeconds { get; set; } = 5;
  public bool EnableHotPlugDetection { get; set; } = true;
}

// Fingerprint Status DTOs
public class FingerprintStatusDto
{
  public string Phase { get; set; } = "Idle";
  public bool IsEnabled { get; set; }
  public double FingerprintsPerMinute { get; set; }
  public double MetadataCallsPerMinute { get; set; }
  public List<FingerprintEventDto> RecentEvents { get; set; } = [];
  public string? LastError { get; set; }
}

public class FingerprintEventDto
{
  public string AudioSource { get; set; } = string.Empty;
  public string SourceType { get; set; } = string.Empty;
  public bool IsMatch { get; set; }
  public int Count { get; set; }
  public double? LastConfidence { get; set; }
  public string? Title { get; set; }
  public string? Artist { get; set; }
  public string? Album { get; set; }
  public bool HasAlbumArt { get; set; }
  public string Phase { get; set; } = "Idle";
  public DateTime Timestamp { get; set; }
}

// Bluetooth DTOs
public class BluetoothStatusDto
{
  public bool IsAvailable { get; set; }
  public string State { get; set; } = string.Empty;
  public bool IsDiscovering { get; set; }
  public BluetoothDeviceDto? ConnectedDevice { get; set; }
  public List<BluetoothDeviceDto> PairedDevices { get; set; } = [];
  public List<BluetoothDeviceDto> DiscoveredDevices { get; set; } = [];
}

public class BluetoothDeviceDto
{
  public string Address { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public bool IsPaired { get; set; }
  public bool IsConnected { get; set; }
  public DateTime? LastConnected { get; set; }
}
