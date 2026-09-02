using System.Reflection;
using Radio.Core.Interfaces.Audio;

namespace Radio.Core.Tests;

/// <summary>
/// Contract tests for ADR-029 D2 — the closed discriminated request set.
/// Two of these are security pins rather than behaviour tests; they are labelled as such.
/// </summary>
public class EventPlaybackRequestTests
{
  private static EventPlaybackRequest Speech(string text = "hello") =>
    new() { Kind = EventPlaybackKind.Speech, Text = text };

  private static EventPlaybackRequest Voicemail(string mediaId = "vm-abc123") =>
    new()
    {
      Kind = EventPlaybackKind.RemoteMedia,
      MediaKind = RemoteMediaKind.GvVoicemail,
      MediaId = mediaId,
      DurationSeconds = 42
    };

  [Fact]
  public void Validate_AcceptsAWellFormedSpeechRequest()
  {
    Assert.Equal(EventPlaybackRejection.None, Speech().Validate());
  }

  [Fact]
  public void Validate_AcceptsAWellFormedVoicemailRequest()
  {
    Assert.Equal(EventPlaybackRejection.None, Voicemail().Validate());
  }

  [Fact]
  public void Validate_DefaultsPriorityToTheAttendedClass()
  {
    // ADR-029 §6.1 — 6, deliberately below the 8 this system uses for "did not state a rank".
    Assert.Equal(6, Speech().Priority);
  }

  // ── SSRF pin 1: a URL-bearing request is REFUSED ────────────────────────
  // ADR-029 D2 / §13: an endpoint that fetches a caller-supplied URL is an SSRF primitive.
  // The structural defence is that no URL field exists (pin 2); this is defence in depth for
  // the one string a caller does control.
  [Theory]
  [InlineData("http://169.254.169.254/latest/meta-data/")]
  [InlineData("https://evil.example/payload.mp3")]
  [InlineData("file:///etc/shadow")]
  [InlineData("//evil.example/payload.mp3")]
  public void Validate_RejectsAUrlBearingMediaId(string mediaId)
  {
    Assert.Equal(EventPlaybackRejection.MediaIdLooksLikeUrl, Voicemail(mediaId).Validate());
  }

  [Theory]
  [InlineData("../../etc/passwd")]
  [InlineData("a/b")]
  [InlineData("a\\b")]
  [InlineData(".")]
  [InlineData("..")]
  public void Validate_RejectsAMediaIdCarryingAPathSeparator(string mediaId)
  {
    Assert.Equal(EventPlaybackRejection.MediaIdHasPathSeparator, Voicemail(mediaId).Validate());
  }

  [Theory]
  [InlineData("vm abc")]
  [InlineData("vm\nabc")]
  [InlineData("vm\tabc")]
  [InlineData("vm\0abc")]
  public void Validate_RejectsAMediaIdCarryingWhitespaceOrControlCharacters(string mediaId)
  {
    Assert.Equal(EventPlaybackRejection.MediaIdHasControlCharacter, Voicemail(mediaId).Validate());
  }

  [Fact]
  public void Validate_RejectsAnOverlongMediaId()
  {
    var tooLong = new string('a', EventPlaybackRequest.MaxMediaIdChars + 1);
    Assert.Equal(EventPlaybackRejection.MediaIdTooLong, Voicemail(tooLong).Validate());
  }

  // ── SSRF pin 2: the type cannot carry a URL at all ──────────────────────
  // This is the structural property. If someone later adds AudioUrl to the request because
  // VoicemailItemDto has one, this test fails and says why.
  [Fact]
  public void EventPlaybackRequest_DeclaresNoUrlShapedProperty()
  {
    var offenders = typeof(EventPlaybackRequest)
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => p.Name.Contains("Url", StringComparison.OrdinalIgnoreCase)
                  || p.Name.Contains("Uri", StringComparison.OrdinalIgnoreCase)
                  || p.PropertyType == typeof(Uri))
      .Select(p => p.Name)
      .ToList();

    Assert.True(
      offenders.Count == 0,
      "EventPlaybackRequest must never carry a URL (ADR-029 D2 - an endpoint that fetches a "
      + "caller-supplied URL is an SSRF primitive). Offending properties: "
      + string.Join(", ", offenders));
  }

  // ── The closed set ──────────────────────────────────────────────────────
  [Fact]
  public void Validate_RejectsASpeechRequestCarryingMediaFields()
  {
    var mixed = new EventPlaybackRequest
    {
      Kind = EventPlaybackKind.Speech,
      Text = "hello",
      MediaKind = RemoteMediaKind.GvVoicemail,
      MediaId = "vm-abc123"
    };

    Assert.Equal(EventPlaybackRejection.ArmMismatch, mixed.Validate());
  }

  [Fact]
  public void Validate_RejectsARemoteMediaRequestCarryingSpeechFields()
  {
    var mixed = new EventPlaybackRequest
    {
      Kind = EventPlaybackKind.RemoteMedia,
      MediaKind = RemoteMediaKind.GvVoicemail,
      MediaId = "vm-abc123",
      Text = "hello"
    };

    Assert.Equal(EventPlaybackRejection.ArmMismatch, mixed.Validate());
  }

  [Fact]
  public void Validate_RejectsAnUndefinedKind()
  {
    var bogus = new EventPlaybackRequest { Kind = (EventPlaybackKind)99 };

    Assert.Equal(EventPlaybackRejection.UnknownKind, bogus.Validate());
  }

  [Fact]
  public void Validate_RejectsAnUndefinedMediaKind()
  {
    var bogus = new EventPlaybackRequest
    {
      Kind = EventPlaybackKind.RemoteMedia,
      MediaKind = (RemoteMediaKind)99,
      MediaId = "vm-abc123"
    };

    Assert.Equal(EventPlaybackRejection.UnknownMediaKind, bogus.Validate());
  }

  [Fact]
  public void Validate_RejectsMissingMediaKind()
  {
    var noKind = new EventPlaybackRequest
    {
      Kind = EventPlaybackKind.RemoteMedia,
      MediaId = "vm-abc123"
    };

    Assert.Equal(EventPlaybackRejection.MissingMediaKind, noKind.Validate());
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public void Validate_RejectsEmptySpeech(string text)
  {
    var empty = new EventPlaybackRequest { Kind = EventPlaybackKind.Speech, Text = text };

    Assert.Equal(EventPlaybackRejection.MissingText, empty.Validate());
  }

  [Fact]
  public void Validate_RejectsSpeechOverTheCharacterCap()
  {
    var longText = new string('a', 1001);

    Assert.Equal(EventPlaybackRejection.TextTooLong, Speech(longText).Validate(maxSpeechChars: 1000));
  }

  [Theory]
  [InlineData(0)]
  [InlineData(11)]
  public void Validate_RejectsAPriorityOutsideOneToTen(int priority)
  {
    var request = Speech() with { Priority = priority };

    Assert.Equal(EventPlaybackRejection.PriorityOutOfRange, request.Validate());
  }

  [Fact]
  public void Validate_AcceptsDurationZeroAsUnknown()
  {
    // ADR-022 §4.2 / ADR-029 §4.1 — 0 means UNKNOWN, not invalid.
    var unknownDuration = Voicemail() with { DurationSeconds = 0 };

    Assert.Equal(EventPlaybackRejection.None, unknownDuration.Validate());
  }

  [Fact]
  public void Validate_RejectsANegativeDuration()
  {
    var negative = Voicemail() with { DurationSeconds = -1 };

    Assert.Equal(EventPlaybackRejection.NegativeDuration, negative.Validate());
  }

  [Fact]
  public void RemoteMediaKind_IsAClosedSetOfOne()
  {
    // If this fails, someone added a media kind. That is fine - but the server must also have
    // gained a URL template for it in its OWN configuration (ADR-029 D2).
    Assert.Single(Enum.GetValues<RemoteMediaKind>());
    Assert.Equal(RemoteMediaKind.GvVoicemail, Enum.GetValues<RemoteMediaKind>()[0]);
  }

  [Fact]
  public void EventPlaybackState_CarriesTheSixLifecycleStates()
  {
    var states = Enum.GetValues<EventPlaybackState>();

    Assert.Contains(EventPlaybackState.Preparing, states);
    Assert.Contains(EventPlaybackState.Playing, states);
    Assert.Contains(EventPlaybackState.Paused, states);
    Assert.Contains(EventPlaybackState.Completed, states);
    Assert.Contains(EventPlaybackState.Stopped, states);
    Assert.Contains(EventPlaybackState.Failed, states);
  }
}
