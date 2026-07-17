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
  /// <summary>
  /// Absolute filesystem path of the currently playing file (file-player sources only).
  /// Surfaced as a first-class property so <c>DisplayNames.Track</c> can parse a clean
  /// title from the filename when the metadata layer only produced "Track N".
  /// Null for non-file sources (Radio, Bluetooth, TTS, etc.).
  /// </summary>
  public string? FilePath { get; set; }
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
//
// AlbumArtUrl mirrors the API surface (Radio.API.Models.QueueItemDto.AlbumArtUrl)
// — populated by the server-side projection from the QueueItem source data
// (file ID3 / MusicBrainz proxy / fingerprint payload). When null, Up Next
// thumbs render the music_note placeholder glyph (PR D #15).
public record QueueItemDto(
  int Index,
  string? Title,
  string? Artist,
  string? Album,
  string? Duration,
  bool IsCurrent,
  string State = "Upcoming",
  int FullPlaylistIndex = 0,
  string? AlbumArtUrl = null
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

/// <summary>
/// Mirror of <c>Radio.Metrics.MetricDescriptor</c> on the Web side. The
/// dashboard fetches a list of these from <c>/api/metrics/descriptors</c>
/// and uses them to resolve units, categories, and threshold bands without
/// resorting to client-side key-pattern heuristics (PR D #11 of the Arc
/// follow-up backlog).
/// </summary>
public record MetricDescriptorDto(
  string Key,
  string Unit,
  string? Category,
  double? Warn,
  double? Critical,
  string? DisplayName
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

public record BookmarkDto(
  string Path,
  string Label,
  string Tag,
  bool IsAccessible
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
  double ScanStopThreshold,
  int? Gain,
  bool AutoGain,
  string? Equalizer,
  int? DeviceVolume,
  bool IsStereo = false,
  string? RdsStationName = null,
  string? RdsProgramType = null,
  bool Clip = false,
  double RssiDbu = 0.0,
  double AppliedGain = 0.0,
  // PR 2 of the Radio Controller Polish arc — anchors the NOW row in the
  // recognition stream to a specific fingerprint event. Null when no match
  // is currently anchored (e.g. dead air, no-match window, non-radio source).
  string? NowPlayingMatchId = null,
  // PR 3 of the Radio Controller Polish arc — RDS RadioText (RT) line
  // below the frequency well. Null/empty hides the RT row entirely.
  string? RdsRadioText = null,
  // PR D #40 of the Arc follow-up backlog — last-stable RDS PS value.
  // Task #80 v4 redefined this: it's now the NRSC-4-B Annex D call-sign
  // decode of the receiver's PI code (e.g. PI=0x8ACC → "WUNC"), not a
  // PS-rotation consensus. PI is invariant per station so a single
  // decode is authoritative. Save-preset dialog seeds from this when
  // present, then falls back to band + frequency.
  string? RdsStationNameStable = null,
  // Task #80 v4 — raw RDS PI code, exposed for diagnostics so the
  // decode that drives RdsStationNameStable is debuggable from the
  // wire without log scraping. Null for non-RDS sources or pre-lock.
  ushort? RdsPi = null,
  // Per-broadcast discriminator mirrored from the API DTO — true on RDS/tuning
  // changes, false on telemetry-only ticks. The RDS marquee path reads it to
  // skip the accumulator append + card refresh when nothing it shows changed.
  // Defaults true so REST /api/radio/state (which can't compute a delta) is
  // always treated as a full refresh.
  bool RdsRelevantChanged = true
);

public record RadioPowerStateDto(
  bool IsPoweredOn
);

public record RadioPresetDto(
  string Id,
  string Name,
  double Frequency,
  string Band,
  DateTimeOffset? CreatedAt = null,
  // PR 3 of the Radio Controller Polish arc — one-based ordinal slot within
  // the band, ordered by CreatedAt ascending. Surfaced by the API so the UI
  // can render a memory-slot column without re-computing the ordinal locally.
  int SlotNumber = 0
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
  public List<string> HiddenSources { get; set; } = new() { "TestTone" };
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

  /// <summary>
  /// Signal-strength percentage at which the scan stops on a station.
  /// Server-side <c>RadioStateMapper.PercentToDbu</c> clamps to <c>[0, 100]</c>;
  /// the API validation layer rejects values outside that range with a
  /// 400 so silent saturation isn't a footgun (PR D #30).
  /// </summary>
  [System.ComponentModel.DataAnnotations.Range(0, 100,
    ErrorMessage = "ScanStopThreshold must be between 0 and 100 (percent).")]
  public int ScanStopThreshold { get; set; } = 50;

  public int ScanStepDelayMs { get; set; } = 100;
  public int DefaultDeviceVolume { get; set; } = 50;

  // HANDOFF-rds-accumulating-scroll §5 — RDS RadioText ticker settings.
  // Defaults must match RdsScrollOptions in src/Radio.Web/Models so a save
  // round-trip from the System Config UI never changes the live behaviour
  // unless the user actually edits a value. Validation ranges are
  // enforced both client-side (the RadzenNumeric Min/Max attributes in
  // SystemConfigPage) and server-side (the configuration store accepts
  // whatever lands; out-of-band values get clamped by the buffer's own
  // Math.Max(8, ...) floor in RdsAccumulatingScrollBuffer).

  /// <summary>
  /// Rolling RDS RT buffer max length in characters. Default 256.
  /// Range 64–2048 per spec §5.
  /// </summary>
  [System.ComponentModel.DataAnnotations.Range(64, 2048,
    ErrorMessage = "RtBufferMaxChars must be between 64 and 2048.")]
  public int RtBufferMaxChars { get; set; } = 256;

  /// <summary>
  /// RDS ticker scroll speed in pixels per second. Default 40.
  /// Range 10–200 per spec §5.
  /// </summary>
  [System.ComponentModel.DataAnnotations.Range(10, 200,
    ErrorMessage = "RtScrollSpeedPxPerSec must be between 10 and 200.")]
  public int RtScrollSpeedPxPerSec { get; set; } = 40;

  /// <summary>
  /// Inter-chunk separator string. Default " • " (space, U+2022, space).
  /// 1–8 characters per spec §5. The buffer treats null/empty as a single
  /// space, but the UI shouldn't let the user save that.
  /// </summary>
  [System.ComponentModel.DataAnnotations.StringLength(8, MinimumLength = 1,
    ErrorMessage = "RtChunkSeparator must be 1–8 characters.")]
  public string RtChunkSeparator { get; set; } = " • ";
}

public class FilePlayerConfigDto
{
  public string RootDirectory { get; set; } = "media/audio";
  public string SupportedExtensions { get; set; } = ".mp3,.flac,.wav,.ogg,.aac,.m4a,.wma";
  public string[] AllowedBrowseDirectories { get; set; } = [];
  public List<BookmarkedPathDto> BookmarkedPaths { get; set; } = [];
}

public class BookmarkedPathDto
{
  public string Path { get; set; } = "";
  public string Label { get; set; } = "";
  public string Tag { get; set; } = "";
}

public class FingerprintingConfigDto
{
  public bool Enabled { get; set; } = true;
  public bool UseShazamForAllSources { get; set; } = false;
  public int SampleDurationSeconds { get; set; } = 15;
  public int IdentificationIntervalSeconds { get; set; } = 30;
  public double MinimumConfidenceThreshold { get; set; } = 0.5;
  public int DuplicateSuppressionMinutes { get; set; } = 5;
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

/// <summary>
/// Coarse confidence band for a fingerprint match (mirrors
/// <c>Radio.API.Models.ConfidenceBucket</c>). PR 2 of the Radio Controller
/// Polish arc replaces the raw <c>double?</c> on the wire so the UI renders
/// a word + pip count instead of a fractional percentage.
/// </summary>
/// <remarks>
/// The API serializes enums as strings via a global <c>JsonStringEnumConverter</c>
/// registered in <c>Radio.API/Program.cs</c>. The Web's <c>HttpClient</c> calls
/// (e.g. <c>GetFromJsonAsync</c>) use default <c>JsonSerializerOptions</c> with
/// no enum converter, so without this attribute deserialization throws
/// <c>JsonException: The JSON value could not be converted to ConfidenceBucket</c>
/// on every fingerprint status fetch — silently breaking the recognition UI.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConfidenceBucket
{
  None,
  Possible,
  Likely,
  Strong
}

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
  /// <summary>
  /// Stable identifier for this event record. The UI anchors the
  /// currently-playing match row when <c>RadioStateDto.NowPlayingMatchId</c>
  /// equals this value.
  /// </summary>
  public string MatchId { get; set; } = string.Empty;

  public string AudioSource { get; set; } = string.Empty;
  public string SourceType { get; set; } = string.Empty;
  public bool IsMatch { get; set; }
  public int Count { get; set; }

  /// <summary>
  /// Coarse confidence band. Replaces the prior raw <c>double? LastConfidence</c>
  /// field on the API surface (PR 2 of the Radio Controller Polish arc).
  /// </summary>
  public ConfidenceBucket Confidence { get; set; } = ConfidenceBucket.None;

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
  public bool IsReconnecting { get; set; }
  public string? LastDisconnectReason { get; set; }

  // A2DP codec observability (Plan C / FM-BT-6) — mirrors Radio.API.Models
  // BluetoothStatusDto. Null when no device is connected or the negotiated codec
  // is not yet known.
  public string? CodecName { get; set; }
  public int? SampleRateHz { get; set; }
  public int? Bitpool { get; set; }
}

public class BluetoothDeviceDto
{
  public string Address { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public bool IsPaired { get; set; }
  public bool IsConnected { get; set; }
  public DateTime? LastConnected { get; set; }
}

// Integration Status DTOs

public class EncoderStatusDto
{
  public bool Enabled { get; set; }
  public bool IsConnected { get; set; }
  public string DevicePath { get; set; } = "";
  public int VendorId { get; set; }
  public int ProductId { get; set; }
}

public class PhoneIntegrationStatusDto
{
  public bool Enabled { get; set; }
  public bool IsConnected { get; set; }
  public string CurrentState { get; set; } = "Idle";
  public string CallerNumber { get; set; } = "";
  public string CallerName { get; set; } = "";
  public string HubUrl { get; set; } = "";
}

// Integration Configuration DTOs

public class RotaryEncoderConfigDto
{
  public bool Enabled { get; set; }
  public int VendorId { get; set; }
  public int ProductId { get; set; }
  public string DevicePath { get; set; } = "";
  public int PollIntervalMs { get; set; }
  public int VolumeStepPercent { get; set; }
  public int TuningStepKHz { get; set; }
  public int ReconnectDelayMs { get; set; }
}

public class PhoneIntegrationConfigDto
{
  public bool Enabled { get; set; }
  public string HubUrl { get; set; } = "";
  public string ContactsApiBaseUrl { get; set; } = "";
  public string RingSoundPath { get; set; } = "";
  public int RingPriority { get; set; }
  public int AnnouncementPriority { get; set; }
  public int ReconnectBaseDelayMs { get; set; }
  public int ReconnectMaxDelayMs { get; set; }
}

public class PbapConfigDto
{
  public bool AutoSyncOnConnect { get; set; } = true;
  public int SyncStaleThresholdHours { get; set; } = 24;
  public int TransferTimeoutSeconds { get; set; } = 30;
}

// RotaryPhone.API DTOs (calls http://localhost:5004)

public class PhoneSystemStatusDto
{
  public string Platform { get; set; } = "";
  public bool IsRaspberryPi { get; set; }
  public bool BluetoothEnabled { get; set; }
  public bool BluetoothConnected { get; set; }
  public string? BluetoothDeviceAddress { get; set; }
  public bool SipListening { get; set; }
  public string? SipListenAddress { get; set; }
  public int SipPort { get; set; }
  public string? Ht801IpAddress { get; set; }
  public bool? Ht801Reachable { get; set; }
}

public class PhoneCallStateDto
{
  public string CallState { get; set; } = "Idle";
  public string? DialedNumber { get; set; }
  public string? IncomingNumber { get; set; }
  // CallerName and Duration intentionally absent — the RotaryPhone /api/phone/status
  // controller has never populated them (anonymous response with only the three fields
  // above). Re-add only if the server starts emitting them.
}

public record ContactDto
{
  public string Id { get; init; } = "";
  public string Name { get; init; } = "";
  public string PhoneNumber { get; init; } = "";
  public string? Email { get; init; }
  public string? Notes { get; init; }
  public DateTime CreatedAt { get; init; }
  public DateTime ModifiedAt { get; init; }
}

public class ContactFormDto
{
  public string Name { get; set; } = "";
  public string PhoneNumber { get; set; } = "";
  public string? Email { get; set; }
}

public class CallHistoryEntryDto
{
  public string? Id { get; set; }
  public DateTime StartTime { get; set; }
  public DateTime? EndTime { get; set; }
  public string? Duration { get; set; }
  // RotaryPhone serializes the CallDirection/CallAnsweredOn enums as int
  // (System.Text.Json default — no JsonStringEnumConverter is registered on
  // RotaryPhone). Typing these as string threw JsonException ("Cannot get the
  // value of a token type 'Number' as a string") and silently dropped the whole
  // list, so the Call History tab showed an empty state. Use matching int-backed
  // enums so deserialization succeeds against the numeric payload.
  public CallDirection Direction { get; set; } = CallDirection.Incoming;
  public string PhoneNumber { get; set; } = "";
  public string? CallerName { get; set; }
  public CallAnsweredOn AnsweredOn { get; set; } = CallAnsweredOn.NotAnswered;
  public string? PhoneId { get; set; }
}

// Must match the ordinal values of
// RotaryPhoneController.Core.CallHistory.CallDirection (Incoming = 0, Outgoing = 1).
public enum CallDirection
{
  Incoming = 0,
  Outgoing = 1
}

// Must match the ordinal values of
// RotaryPhoneController.Core.CallHistory.CallAnsweredOn
// (NotAnswered = 0, RotaryPhone = 1, CellPhone = 2).
public enum CallAnsweredOn
{
  NotAnswered = 0,
  RotaryPhone = 1,
  CellPhone = 2
}

// PBAP DTOs

public class PbapSyncResultDto
{
  public bool Success { get; set; }
  public int ContactCount { get; set; }
  public string? ErrorMessage { get; set; }
}

public class PbapSyncStatusDto
{
  public List<PbapDeviceSyncInfoDto> Devices { get; set; } = [];
}

public class PbapDeviceSyncInfoDto
{
  public string DeviceAddress { get; set; } = "";
  public string? DeviceName { get; set; }
  public int ContactCount { get; set; }
  public DateTime? LastSynced { get; set; }
  public bool IsStale { get; set; }
}

public class PbapContactDto
{
  public string DisplayName { get; set; } = "";
  public List<string> PhoneNumbers { get; set; } = [];
}

/// <summary>
/// Response shape of GET /api/bluetooth/pbap/lookup?phoneNumber= — a single
/// resolved contact name for one number, or 404 when there is no match. Consumed
/// by the Messages feed's contact-name resolution (Task #6).
/// </summary>
public class PbapLookupDto
{
  public string? DisplayName { get; set; }
  public string? PhoneNumber { get; set; }
}

/// <summary>
/// Merged view of manual (ContactDto) and phone-synced (PbapContactDto) contacts.
/// Used by PhonePage and PhoneContactsPanel for the unified contacts grid.
/// </summary>
public record MergedContact(string? Id, string Name, string Phone, string? Email, string Source);

// GV Bridge DTOs (calls RotaryPhone.API /api/gvbridge)

public class GvBridgeStatusDto
{
  // Server shape (post-SIP-WSS migration, March 2026): { available, activeMode }.
  // SipRegistered / CookiesValid added per ADR-022 §4.4 — defensive/optional:
  // RotaryPhone may not populate them yet, so defaults keep deserialization safe.
  public bool Available { get; set; }
  public string ActiveMode { get; set; } = "";
  public bool SipRegistered { get; set; }
  public bool CookiesValid { get; set; }
}

// ─────────────────────────────────────────────────────────────────────
// GV (gvbridge) Voicemail + SMS — consumed by the Messages UI (PhonePage).
// NOTE: GV (gvbridge) SMS is NOT the same product as VoIP.ms trunk SMS.
// Trunk SMS = GvSmsNotificationDto + GvTrunkHubService.SmsReceived on
// /hubs/gvtrunk. GV SMS = SmsMessageDto + PhoneHubService.GvSmsReceived on
// /hub. Do NOT merge or rename these. (ADR-022 §4.3.)
// ─────────────────────────────────────────────────────────────────────

// ── GV Voicemail ──────────────────────────────────────────────
public record VoicemailItemDto(
  string Id,
  string ThreadId,
  string FromNumber,            // E.164
  string? FromName,             // null → UI shows number / contact lookup
  DateTime ReceivedAt,          // UTC; format to local for display
  int DurationSeconds,          // 0 = unknown → do NOT render "0:00" as real
  bool IsRead,                  // UI-LOCAL only — GV mark-read not in v1
  string? Transcript,           // null = pending/absent
  string AudioUrl);             // RELATIVE from server; rebuild absolute (ADR D4)

public record VoicemailListDto(
  IReadOnlyList<VoicemailItemDto> Items,
  string? NextPageToken,        // null = no more pages
  DateTime FetchedAtUtc);

// ── GV SMS ────────────────────────────────────────────────────
public record SmsMessageDto(
  string Id,
  string ThreadId,
  string Direction,             // "Inbound" | "Outbound"; UNKNOWN → Inbound
  string CounterpartyNumber,    // E.164
  string? Text,                 // null → render placeholder, do not crash
  DateTime SentAt,              // UTC
  bool IsRead);

public record SmsThreadDto(
  string ThreadId,
  string CounterpartyNumber,
  string? CounterpartyName,
  DateTime LastMessageAt,
  bool HasUnread,
  string? LastMessagePreview);

public record SmsThreadListDto(
  IReadOnlyList<SmsThreadDto> Threads,
  DateTime FetchedAtUtc);

public record SmsThreadMessagesDto(
  string ThreadId,
  IReadOnlyList<SmsMessageDto> Messages,
  DateTime FetchedAtUtc);

// ── Send (flagged; wired in PR3, endpoint ships later) ─────────
public record SendSmsRequest(string ThreadId, string Text);
public record SendSmsResponse(SmsMessageDto? Message, string? Error);  // shape provisional

// Defensive direction mapping: anything not exactly "Outbound" → Inbound.
// Never throw on an unrecognized value (ADR §4.2 provisional-data rule).
public static class GvDirection
{
  public const string Inbound = "Inbound";
  public const string Outbound = "Outbound";

  public static bool IsOutbound(string? direction) =>
    string.Equals(direction, Outbound, StringComparison.OrdinalIgnoreCase);
}

public class GvAdapterModeDto
{
  public string ActiveMode { get; set; } = "";
  public List<GvModeEntryDto> Modes { get; set; } = [];
}

public class GvModeEntryDto
{
  public string Mode { get; set; } = "";
}

// ── Diagnostics (RotaryPhone /api/diagnostics/*) — consumed read-only by
// PhoneDiagnosticsPanel over the existing phone API link (consume-only per the
// BT/audio boundary). Shapes verified against live responses 2026-06-13.

/// <summary>Live GV audio-bridge stats from <c>/api/diagnostics/audio-bridge</c>.</summary>
public class AudioBridgeStatsDto
{
  public bool IsActive { get; set; }
  public int InboundFramesSent { get; set; }
  public int OutboundFramesReceived { get; set; }
  public int InboundErrors { get; set; }
  public int OutboundErrors { get; set; }
  public bool BidirectionalAudio { get; set; }
}

/// <summary>One SIP message from <c>/api/diagnostics/sip-log</c>. <c>Direction</c>: 0 = Sent, 1 = Received.</summary>
public class SipMessageDto
{
  public DateTime Timestamp { get; set; }
  public int Direction { get; set; }
  public string Method { get; set; } = "";
  public string? FromAddress { get; set; }
  public string? ToAddress { get; set; }
  public int? StatusCode { get; set; }
  public string? StatusText { get; set; }
  public string? DiagnosticNote { get; set; }
  public string? CallId { get; set; }
}

/// <summary>One call-timeline event from <c>/api/diagnostics/timeline</c>.</summary>
public class CallTimelineDto
{
  public DateTime Timestamp { get; set; }
  public string EventType { get; set; } = "";
  public string Description { get; set; } = "";
  public Dictionary<string, string>? Metadata { get; set; }
}

public class GvSmsNotificationDto
{
  public string FromNumber { get; set; } = "";
  public string? Body { get; set; }
  public DateTime ReceivedAt { get; set; }
  // Server serializes the SmsType enum as int (System.Text.Json default — no
  // JsonStringEnumConverter is registered on RotaryPhone). Use a matching
  // enum so deserialization succeeds for both numeric and string payloads.
  public GvSmsType Type { get; set; } = GvSmsType.Sms;
}

// Must match the ordinal values of RotaryPhoneController.GVTrunk.Models.SmsType
// (Sms = 0, MissedCall = 1).
public enum GvSmsType
{
  Sms = 0,
  MissedCall = 1
}

// GV Trunk DTOs (calls RotaryPhone.API /api/gvtrunk)

public class GvTrunkStatusDto
{
  public bool IsRegistered { get; set; }
  public string CallState { get; set; } = "Idle";
  public int ActiveCallDurationSeconds { get; set; }
}

public class GvTrunkCallLogEntryDto
{
  public int Id { get; set; }
  public DateTime StartedAt { get; set; }
  public DateTime? EndedAt { get; set; }
  public string Direction { get; set; } = "";
  public string RemoteNumber { get; set; } = "";
  public string Status { get; set; } = "";
  public int? DurationSeconds { get; set; }
  public string? Notes { get; set; }
}
