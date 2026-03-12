namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for RotaryPhone integration (incoming call announcements).
/// Loaded from the 'PhoneIntegration' configuration section.
/// </summary>
public class PhoneIntegrationOptions
{
  /// <summary>Configuration section name.</summary>
  public const string SectionName = "PhoneIntegration";

  /// <summary>Enable/disable phone call integration.</summary>
  public bool Enabled { get; set; } = false;

  /// <summary>SignalR hub URL for the RotaryPhone server.</summary>
  public string HubUrl { get; set; } = "http://localhost:5555/hubs/phone";

  /// <summary>Base URL for the RotaryPhone contacts REST API.</summary>
  public string ContactsApiBaseUrl { get; set; } = "http://localhost:5555";

  /// <summary>Play ring sound through radio speakers on incoming call. Disable when physical phone handles ringing.</summary>
  public bool PlayRingSound { get; set; } = false;

  /// <summary>Path to ring sound WAV/MP3 file (relative to app root).</summary>
  public string RingSoundPath { get; set; } = "media/sounds/phone-ring.wav";

  /// <summary>Audio ducking priority for ring sound (1-10, higher = more important).</summary>
  public int RingPriority { get; set; } = 9;

  /// <summary>Audio ducking priority for caller name TTS announcement (1-10).</summary>
  public int AnnouncementPriority { get; set; } = 8;

  /// <summary>Base delay for reconnection backoff in milliseconds.</summary>
  public int ReconnectBaseDelayMs { get; set; } = 2000;

  /// <summary>Maximum delay for reconnection backoff in milliseconds.</summary>
  public int ReconnectMaxDelayMs { get; set; } = 30000;
}
