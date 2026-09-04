namespace Radio.API.Models;

/// <summary>
/// The wire shape of POST /api/audio/events.
/// </summary>
/// <remarks>
/// ⚠ A separate type from <c>EventPlaybackRequest</c> on purpose, and the reason is not layering
/// hygiene. Every field here is nullable and the two enums arrive as STRINGS, so a body with a
/// missing or unrecognised "kind" is answered with a NAMED rejection reason instead of
/// System.Text.Json's required-member or enum-parse exception, which the model binder turns into a
/// generic 400. Keeping EventPlaybackRequest off the wire also keeps the type whose whole posture is
/// "there is no URL field and there never will be one" free of any deserialisation concern.
///
/// The mapping is a TRANSLATION, not a second rule set: an unrecognised enum name becomes an
/// UNDEFINED enum value, and EventPlaybackRequest.Validate then produces UnknownKind /
/// UnknownMediaKind / ArmMismatch by its own rules. The controller decides nothing.
/// </remarks>
public sealed class EventPlaybackRequestDto
{
  /// <summary>"Speech" or "RemoteMedia".</summary>
  public string? Kind { get; set; }

  /// <summary>Speech arm: the literal utterance, composed by the caller (ADR-029 §4.2).</summary>
  public string? Text { get; set; }

  /// <summary>Speech arm: per-request voice override. Null means TTS:DefaultVoice.</summary>
  public string? VoiceId { get; set; }

  /// <summary>Speech arm: per-request engine override. Null means TTS:DefaultEngine.</summary>
  public string? Engine { get; set; }

  /// <summary>RemoteMedia arm: "GvVoicemail".</summary>
  public string? MediaKind { get; set; }

  /// <summary>RemoteMedia arm: the provider's recording id. ⚠ NEVER a URL.</summary>
  public string? MediaId { get; set; }

  /// <summary>RemoteMedia arm: the provider's duration. 0 means unknown (ADR-022 §4.2).</summary>
  public int? DurationSeconds { get; set; }

  /// <summary>Display label. Presentation only.</summary>
  public string? Label { get; set; }

  /// <summary>Ducking priority 1-10. Null takes EventPlaybackRequest's own default.</summary>
  public int? Priority { get; set; }
}

/// <summary>The wire shape of POST /api/audio/events/{id}/seek.</summary>
public sealed class EventPlaybackSeekDto
{
  /// <summary>Target position from the start of the content, in seconds.</summary>
  public double PositionSeconds { get; set; }
}
