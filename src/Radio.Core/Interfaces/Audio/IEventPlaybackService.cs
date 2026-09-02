namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Attended event playback — ADR-029 D1.
///
/// Sits BESIDE <see cref="IAnnouncementService"/>, deliberately, and does not replace it.
/// <see cref="IAnnouncementService"/> serves UNATTENDED announcements: fire-and-forget, no
/// identity, one global stop. This serves ATTENDED playback: a user pressed a button, is
/// listening on purpose, and expects transport controls and a handle to address.
///
/// Both arms of <see cref="EventPlaybackRequest"/> share one lifecycle, one state model, one
/// stop path and one broadcast; they differ only in how the audio is acquired, and that
/// difference lives inside the implementation rather than at this contract.
/// </summary>
public interface IEventPlaybackService
{
  /// <summary>
  /// Starts an attended playback. Returns as soon as the request is accepted — the returned
  /// snapshot is normally <see cref="EventPlaybackState.Preparing"/>, because both arms have an
  /// acquisition phase (an HTTP fetch, or a TTS synthesis) before any audio exists.
  /// </summary>
  /// <param name="request">The request describing what to play.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A snapshot of the accepted playback.</returns>
  Task<EventPlaybackSnapshot> StartAsync(
    EventPlaybackRequest request,
    CancellationToken cancellationToken = default);

  /// <summary>Stops the playback with this id. False when no such playback is in flight.</summary>
  /// <param name="playbackId">The server-minted playback identifier.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>True when a playback was stopped.</returns>
  Task<bool> StopAsync(string playbackId, CancellationToken cancellationToken = default);

  /// <summary>
  /// Seeks the playback with this id. False when there is no such playback in flight, or when
  /// the source reports it cannot seek.
  /// </summary>
  /// <remarks>
  /// ⚠ The return is narrower than "the audio moved", and deliberately says so. The transport
  /// primitive underneath is <see cref="IEventAudioSource.SeekAsync"/>, which returns a bare
  /// Task and carries no outcome — so an implementation can pre-check
  /// <see cref="IEventAudioSource.IsSeekable"/> and answer false from that, but a seek the
  /// PLAYER refused is not distinguishable from one it honoured at this seam.
  ///
  /// Widening IEventAudioSource.SeekAsync to Task&lt;bool&gt; would close the gap and is an open
  /// question for PR 3, not something to settle here: ADR-029 D4 copies those signatures
  /// verbatim from IPrimaryAudioSource, so changing one changes both.
  /// </remarks>
  /// <param name="playbackId">The server-minted playback identifier.</param>
  /// <param name="position">The position to seek to, from the beginning of the content.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>
  /// True when a seek was dispatched to a playback that exists and reports itself seekable.
  /// Not a confirmation that the audio repositioned — see the remarks.
  /// </returns>
  Task<bool> SeekAsync(
    string playbackId,
    TimeSpan position,
    CancellationToken cancellationToken = default);

  /// <summary>Pauses the playback with this id. False when no such playback is in flight.</summary>
  /// <param name="playbackId">The server-minted playback identifier.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>
  /// True when a pause was dispatched to a playback in flight; false when there is no such
  /// playback. <see cref="IEventAudioSource.PauseAsync"/> returns a bare Task, so this reports
  /// that the request was addressed to something, not that the audio actually stopped.
  /// </returns>
  Task<bool> PauseAsync(string playbackId, CancellationToken cancellationToken = default);

  /// <summary>Resumes the playback with this id. False when no such playback is paused.</summary>
  /// <param name="playbackId">The server-minted playback identifier.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>
  /// True when a resume was dispatched to a paused playback; false when there is no such
  /// playback. <see cref="IEventAudioSource.ResumeAsync"/> returns a bare Task, so this reports
  /// that the request was addressed to something, not that the audio actually resumed.
  /// </returns>
  Task<bool> ResumeAsync(string playbackId, CancellationToken cancellationToken = default);

  /// <summary>
  /// The one in-flight attended playback, or null. There is one audio engine and one set of
  /// speakers, so this state is global rather than per-caller (ADR-029 D6).
  /// </summary>
  EventPlaybackSnapshot? Current { get; }

  /// <summary>
  /// Raised on every state transition. Deliberately NOT raised periodically: the snapshot
  /// carries a position anchor and clients interpolate locally (ADR-029 §8.2).
  /// </summary>
  event EventHandler<EventPlaybackSnapshot>? PlaybackChanged;
}

/// <summary>Which arm of <see cref="EventPlaybackRequest"/> is populated.</summary>
public enum EventPlaybackKind
{
  /// <summary>Speak a literal string through the currently selected TTS engine.</summary>
  Speech = 0,

  /// <summary>Play a remote recording, addressed by identifier — never by URL.</summary>
  RemoteMedia = 1
}

/// <summary>
/// The closed set of remote media the server knows how to resolve. One member today.
/// Adding a member means adding a URL template in the server's own configuration — which is
/// the whole point: the caller never supplies a URL (ADR-029 D2).
/// </summary>
public enum RemoteMediaKind
{
  /// <summary>A Google Voice voicemail recording, fetched from the configured gvbridge host.</summary>
  GvVoicemail = 0
}

/// <summary>Lifecycle of one attended playback.</summary>
public enum EventPlaybackState
{
  /// <summary>Accepted; audio is being acquired (fetch or synthesis). No sound yet.</summary>
  Preparing = 0,

  /// <summary>Audio is being produced.</summary>
  Playing = 1,

  /// <summary>Audio is held at its current position.</summary>
  Paused = 2,

  /// <summary>Reached the end of the content.</summary>
  Completed = 3,

  /// <summary>Ended before the end of the content — user stop, preemption, or the duration cap.</summary>
  Stopped = 4,

  /// <summary>Never produced sound. <see cref="EventPlaybackSnapshot.FailureReason"/> says why.</summary>
  Failed = 5
}

/// <summary>
/// A closed discriminated request with deliberately ASYMMETRIC arms (ADR-029 D2).
///
/// Speech carries the literal utterance, because the text is already in the caller's hands, is
/// small, and the server has no business acquiring SMS content. Remote media carries a
/// (kind, id, duration) REFERENCE, because the recording is large, remote, and in nobody's hands
/// yet — so the fetch happens once, server-side, where it can be cached and authenticated.
///
/// ⚠ There is deliberately NO url/uri field on this type, and there must never be one. An
/// endpoint that fetches a caller-supplied URL is a server-side-request-forgery primitive, and
/// "it is a LAN kiosk" is not a defence. The server maps <see cref="RemoteMediaKind.GvVoicemail"/>
/// to a URL built from ITS OWN configuration. <see cref="Validate"/> pins this, and
/// EventPlaybackRequestTests pins that this type declares no URL-shaped property.
/// </summary>
public sealed record EventPlaybackRequest
{
  /// <summary>Which arm is populated. Every other field is validated against this.</summary>
  public required EventPlaybackKind Kind { get; init; }

  // ── Kind == Speech ────────────────────────────────────────────

  /// <summary>The literal utterance. Composed by the caller (ADR-029 §4.2).</summary>
  public string? Text { get; init; }

  /// <summary>Per-request voice override. Null means TTSOptions.DefaultVoice.</summary>
  public string? VoiceId { get; init; }

  /// <summary>
  /// Per-request engine override. Null means the currently selected engine, TTS:DefaultEngine
  /// (ADR-029 D10). Radio.Web sends null; this exists so one utterance can diverge without
  /// creating a second persistent place where engine selection lives.
  /// </summary>
  public string? Engine { get; init; }

  // ── Kind == RemoteMedia ───────────────────────────────────────

  /// <summary>Which closed-set media resolver to use.</summary>
  public RemoteMediaKind? MediaKind { get; init; }

  /// <summary>
  /// The provider's identifier for the recording — for GvVoicemail, VoicemailItemDto.Id.
  /// ⚠ NEVER a URL, and never VoicemailItemDto.AudioUrl. See the type remarks.
  /// </summary>
  public string? MediaId { get; init; }

  /// <summary>
  /// Authoritative duration from the provider's DTO. Per ADR-022 §4.2, 0 means UNKNOWN.
  /// This is a correctness fix, not decoration: AudioFileEventSource detects completion from
  /// this value, and AudioFileEventSourceFactory would otherwise estimate it from file size
  /// (MP3 at a flat 16000 B/s) and never decode.
  /// </summary>
  public int? DurationSeconds { get; init; }

  // ── Both arms ─────────────────────────────────────────────────

  /// <summary>Display label, e.g. "Voicemail from Jane". Presentation only.</summary>
  public string? Label { get; init; }

  /// <summary>
  /// Ducking priority. 6 is the attended-playback class (ADR-029 §6.1) — below the 8 that this
  /// system uses for "an event that did not state its importance", so anything that did not
  /// claim a rank still outranks a user listening to a recording.
  /// </summary>
  public int Priority { get; init; } = 6;

  /// <summary>
  /// Validates the closed set and its asymmetric arms.
  /// Returns <see cref="EventPlaybackRejection.None"/> when the request is acceptable.
  /// </summary>
  /// <param name="maxSpeechChars">
  /// Cap on <see cref="Text"/>, GvMedia:MaxSpeechChars. The default matches the ADR's shipping
  /// value so this method is usable from tests without a configuration object.
  /// </param>
  /// <returns>The reason the request was refused, or None.</returns>
  public EventPlaybackRejection Validate(int maxSpeechChars = 1000)
  {
    if (Priority is < 1 or > 10)
    {
      return EventPlaybackRejection.PriorityOutOfRange;
    }

    switch (Kind)
    {
      case EventPlaybackKind.Speech:
        // The arms are closed, not merely optional: a Speech request carrying media fields is
        // a caller confusion, and accepting it would let a future refactor read the wrong arm.
        if (MediaKind is not null || MediaId is not null || DurationSeconds is not null)
        {
          return EventPlaybackRejection.ArmMismatch;
        }
        if (string.IsNullOrWhiteSpace(Text))
        {
          return EventPlaybackRejection.MissingText;
        }
        return Text.Length > maxSpeechChars
          ? EventPlaybackRejection.TextTooLong
          : EventPlaybackRejection.None;

      case EventPlaybackKind.RemoteMedia:
        if (Text is not null || VoiceId is not null || Engine is not null)
        {
          return EventPlaybackRejection.ArmMismatch;
        }
        if (MediaKind is null)
        {
          return EventPlaybackRejection.MissingMediaKind;
        }
        if (!Enum.IsDefined(MediaKind.Value))
        {
          return EventPlaybackRejection.UnknownMediaKind;
        }
        if (DurationSeconds is < 0)
        {
          return EventPlaybackRejection.NegativeDuration;
        }
        return ValidateMediaId(MediaId);

      default:
        return EventPlaybackRejection.UnknownKind;
    }
  }

  /// <summary>
  /// Defence in depth for the SSRF property. The primary defence is structural — this type has
  /// no URL field, and the server builds the URL from its own configuration — but the id still
  /// becomes a URL path segment and a cache key downstream, so it is constrained here too.
  ///
  /// The rule is an ALLOW-LIST: <c>[A-Za-z0-9._~-]</c>, the RFC 3986 unreserved set. The named
  /// checks above it are kept only so a recognisable id gets a precise reason — a pasted URL is
  /// told it looks like a URL rather than that some character is illegal — and everything they
  /// do not name falls through to the allow-list.
  ///
  /// ⚠ A deny-list alone is NOT sufficient, and this is the reason. Under RFC 3986 §4.2 a
  /// relative reference that begins with a scheme is not a relative reference at all: it is an
  /// absolute URI, and reference resolution returns it rather than confining it to the base.
  /// "http:evil.example", "mailto:x@y" and "data:audio;base64,..." all carry a scheme while
  /// carrying neither "//" nor a path separator, so every deny rule above passes them — inside
  /// a validator whose whole purpose is to stop a caller choosing the host that gets fetched.
  /// The allow-list refuses ':' outright, which is the entire class rather than the examples.
  ///
  /// ⚠ Declared assumption, now tighter than it was: a Google Voice voicemail id contains only
  /// unreserved characters. If one ever does not, this rejects it — as MediaIdHasPathSeparator
  /// for a '/' or '\', otherwise as MediaIdHasIllegalCharacter — a loud, named 400 rather than
  /// a silent misbehaviour, and the fix is one line here.
  /// </summary>
  private static EventPlaybackRejection ValidateMediaId(string? mediaId)
  {
    if (string.IsNullOrWhiteSpace(mediaId))
    {
      return EventPlaybackRejection.MissingMediaId;
    }
    if (mediaId.Length > MaxMediaIdChars)
    {
      return EventPlaybackRejection.MediaIdTooLong;
    }
    // Checked before the separator rule so a pasted URL gets the precise reason.
    if (mediaId.Contains("://", StringComparison.Ordinal)
        || mediaId.StartsWith("//", StringComparison.Ordinal))
    {
      return EventPlaybackRejection.MediaIdLooksLikeUrl;
    }
    if (mediaId.Contains('/') || mediaId.Contains('\\'))
    {
      return EventPlaybackRejection.MediaIdHasPathSeparator;
    }
    if (mediaId is "." or "..")
    {
      return EventPlaybackRejection.MediaIdHasPathSeparator;
    }
    // The control/whitespace test stays first inside the loop so a space keeps reporting
    // MediaIdHasControlCharacter; the allow-list below it is the backstop that catches
    // everything the named rules above do not, ':' included.
    foreach (var c in mediaId)
    {
      if (char.IsControl(c) || char.IsWhiteSpace(c))
      {
        return EventPlaybackRejection.MediaIdHasControlCharacter;
      }
      if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or '~'))
      {
        return EventPlaybackRejection.MediaIdHasIllegalCharacter;
      }
    }
    return EventPlaybackRejection.None;
  }

  /// <summary>Upper bound on a media identifier. Generous; the point is that it is bounded.</summary>
  public const int MaxMediaIdChars = 256;
}

/// <summary>Why a request was refused. None means it was acceptable.</summary>
public enum EventPlaybackRejection
{
  /// <summary>The request is acceptable.</summary>
  None = 0,

  /// <summary><see cref="EventPlaybackRequest.Kind"/> is not a defined member.</summary>
  UnknownKind,

  /// <summary>Fields from the other arm were populated.</summary>
  ArmMismatch,

  /// <summary><see cref="EventPlaybackRequest.Priority"/> is outside 1-10.</summary>
  PriorityOutOfRange,

  /// <summary>A Speech request carried no text.</summary>
  MissingText,

  /// <summary>The utterance exceeds the character cap.</summary>
  TextTooLong,

  /// <summary>A RemoteMedia request named no media kind.</summary>
  MissingMediaKind,

  /// <summary>The media kind is not a defined member.</summary>
  UnknownMediaKind,

  /// <summary>A RemoteMedia request carried no media identifier.</summary>
  MissingMediaId,

  /// <summary>The media identifier exceeds <see cref="EventPlaybackRequest.MaxMediaIdChars"/>.</summary>
  MediaIdTooLong,

  /// <summary>The media identifier looks like a URL. See the SSRF note on the request type.</summary>
  MediaIdLooksLikeUrl,

  /// <summary>The media identifier carries a path separator or is a relative path segment.</summary>
  MediaIdHasPathSeparator,

  /// <summary>The media identifier carries a control or whitespace character.</summary>
  MediaIdHasControlCharacter,

  /// <summary>The reported duration is negative. Zero is valid and means unknown.</summary>
  NegativeDuration,

  /// <summary>
  /// The media identifier carries a character outside the allow-list <c>[A-Za-z0-9._~-]</c>.
  /// Appended deliberately at the END so that no member above it is renumbered. Nothing today
  /// depends on the numeric values — every reference in this repo is by name — but a rejection
  /// reason is the kind of thing that ends up in a log line or on the wire, and inserting into
  /// the middle of the list is how that quietly stops meaning what it used to.
  /// </summary>
  MediaIdHasIllegalCharacter
}

/// <summary>
/// The state of the one attended playback, as an ANCHOR rather than a tick (ADR-029 §8.2).
///
/// <see cref="PositionAtBroadcast"/> plus <see cref="BroadcastAtUtc"/> plus <see cref="State"/>
/// is enough for a client to interpolate its own progress bar locally, which is why there is
/// deliberately no periodic position broadcast: a tick would put a timer on the server and a
/// message on the wire for every open client, continuously, on a box where CPU churn is audible.
/// </summary>
/// <param name="Id">The server-minted playbackId. See the identity note in ADR-029 §3.3.</param>
/// <param name="Kind">Which arm of the request produced this playback.</param>
/// <param name="Label">Display label carried through from the request. Presentation only.</param>
/// <param name="State">Where this playback is in its lifecycle.</param>
/// <param name="Duration">
/// Null while Preparing, and null when the provider reported duration 0 (unknown) — so the UI
/// renders an indeterminate bar rather than a confident lie.
/// </param>
/// <param name="PositionAtBroadcast">The playback position at the instant this snapshot was minted.</param>
/// <param name="BroadcastAtUtc">When this snapshot was minted; the anchor for local interpolation.</param>
/// <param name="FailureReason">Why the playback failed, when <see cref="State"/> is Failed.</param>
public sealed record EventPlaybackSnapshot(
  string Id,
  EventPlaybackKind Kind,
  string? Label,
  EventPlaybackState State,
  TimeSpan? Duration,
  TimeSpan PositionAtBroadcast,
  DateTimeOffset BroadcastAtUtc,
  string? FailureReason);
