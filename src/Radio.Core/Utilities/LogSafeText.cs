using System.Security.Cryptography;
using System.Text;

namespace Radio.Core.Utilities;

/// <summary>
/// Renders a user-supplied value — free text, or a phone number — as a log-safe token: a stable
/// hash prefix, plus a character count for text.
/// </summary>
/// <remarks>
/// The shape deliberately mirrors <c>GvMediaCache.MaskFor</c> — a short literal prefix plus the
/// first 8 hex characters of a SHA-256 — so that a reader who knows one form recognises the other.
///
/// ⚠ THIS IS A CORRELATION TOKEN, NOT A CONFIDENTIALITY BOUNDARY, and the difference is not
/// pedantic. Announcement text is drawn from a small candidate space ("Yes", "The front door is
/// open"), so anyone holding both the log file and a word list can recover a short utterance by
/// hashing candidates. What this defends against is a person READING the log — the family member or
/// technician who opens Settings → Logs, which is the actual exposure TTS-11 was filed for. It does
/// not defend against an adversary who can enumerate. Do not describe it as anonymised, and do not
/// reach for it in a context where enumeration is the threat: there, log nothing.
///
/// The character count is deliberately exact rather than bucketed. What it answers is "is this
/// payload absurdly long, or shorter than the request implied" — a truncated or mis-encoded body
/// is the realistic operator question, and once the text is gone, length is the only field left
/// that can answer it.
///
/// ⚠ It does NOT also answer "was the string empty", although an earlier revision of this comment
/// said so. No production caller can ask that: every one of the seven string sites has an upstream
/// non-empty guard. NotificationsController and SourcesController reject on
/// <c>IsNullOrWhiteSpace</c> before logging; AnnouncementService and TTSFactory use
/// <c>ArgumentException.ThrowIfNullOrWhiteSpace</c>; and TTSEventSource's text arrives from
/// <c>TTSFactory.CreateAsync</c>, already past that check. <see cref="Empty"/> is correct
/// defensive behaviour for a helper whose parameter is <c>string?</c> — it is not a diagnostic
/// anybody in this solution can reach, and the design should not be justified as though it were.
///
/// ⚠ Nor is an exact count free, and the earlier claim that it "leaks nothing the hash has not
/// already leaked" was true only for half the population. For a SMALL candidate space it holds:
/// smart-home announcements are enumerable, so a word list already yields both the text and its
/// length. For HIGH-ENTROPY text it does not, and that is the case TTS-11 was filed for — an SMS
/// body read aloud is not enumerable, the hash IS protective there, and an exact character count
/// is then strictly additional disclosure. It is kept because a truncated-body diagnosis on a
/// family appliance is worth that much, not because it costs nothing.
/// </remarks>
public static class LogSafeText
{
  /// <summary>The token for null or empty text. Distinguishable from a hash at a glance.</summary>
  public const string Empty = "txt:empty";

  /// <summary>
  /// Returns <c>txt:{8 hex}/{length}</c> for <paramref name="text"/>, or <see cref="Empty"/>
  /// when it is null or empty.
  /// </summary>
  /// <remarks>
  /// The hash is taken over the UTF-8 bytes, so the token is stable across processes and machines.
  /// It must not be replaced with <c>string.GetHashCode()</c>, which is randomised per process and
  /// would make two log lines about the same utterance uncorrelatable.
  ///
  /// <paramref name="text"/>'s <c>Length</c> counts UTF-16 code units, so an emoji counts 2 and a
  /// combining sequence counts more than its glyphs. That is fine for the diagnostic question
  /// ("absurdly long? shorter than the request implied?") and it matches what every truncation
  /// site this replaced already used.
  /// </remarks>
  public static string For(string? text)
  {
    if (string.IsNullOrEmpty(text))
    {
      return Empty;
    }

    return Token("txt:", text, withLength: true);
  }

  /// <summary>The token for a null, empty, or digitless phone number.</summary>
  public const string EmptyPhone = "phn:empty";

  /// <summary>
  /// Returns <c>phn:{8 hex}</c> for <paramref name="phoneNumber"/>, or <see cref="EmptyPhone"/>
  /// when it is null, empty, or contains no digits.
  /// </summary>
  /// <remarks>
  /// ⚠ THE NUMBER IS NORMALISED BEFORE HASHING, and that is the whole reason this is a method
  /// rather than a call to <see cref="For"/>. The same subscriber reaches these log lines in at
  /// least three spellings — "+1 (555) 123-4567" from the hub, "15551234567" from PBAP,
  /// "5551234567" after normalisation — and hashing the raw string would give one caller three
  /// different tokens, which is strictly worse than the "***4567" form this replaced. Normalising
  /// first is what makes the token answer "is this the same caller?", which is the only question
  /// these lines are ever used for. See plan PHN-5 C-98.
  ///
  /// ⚠ NO LENGTH SUFFIX, unlike <see cref="For"/>, and the omission is deliberate. A phone
  /// number's length is near-constant so it answers no triage question, and it would separate
  /// national from E.164 format for free. GvMediaCache.MaskFor is the in-tree precedent for the
  /// no-length variant; For's length is kept because "was this empty or absurd" is a real question
  /// about an utterance and not about a number.
  ///
  /// ⚠ A non-empty input with no digits — "unknown", "Anonymous", "(withheld)" — normalises to
  /// empty and returns EmptyPhone, the same token as null. That is the intended answer: both mean
  /// "no usable number". It is documented because it is otherwise indistinguishable from a bug.
  ///
  /// ⚠ THIS IS A CORRELATION TOKEN, NOT A CONFIDENTIALITY BOUNDARY, and the caveat is STRONGER
  /// here than on For. A NANP number is ten digits: the entire candidate space can be hashed in
  /// minutes. What this defends against is a person READING the log — the family member or
  /// technician who opens Settings → Logs — which is the exposure PHN-5 was filed for. It is not
  /// anonymisation and must never be described as such.
  /// </remarks>
  public static string ForPhone(string? phoneNumber)
  {
    var normalized = PhoneNumberNormalizer.Normalize(phoneNumber ?? string.Empty);
    return normalized.Length == 0 ? EmptyPhone : Token("phn:", normalized, withLength: false);
  }

  /// <summary>
  /// The one hash the tokens in this class are built from: SHA-256 over the UTF-8 bytes, first
  /// four bytes as lowercase hex.
  /// </summary>
  /// <remarks>
  /// ⚠ Extracted so there is provably ONE algorithm behind every prefix this class emits, which is
  /// the property PHN-5 §1.2 argues for. The shape it produces — a short literal prefix plus 8 hex
  /// — is also GvMediaCache.MaskFor's, so a reader who knows one form recognises the others.
  /// ⚠ Encoding.UTF8 and SHA256 are both load-bearing and both pinned by exact-value tests. Do not
  /// substitute string.GetHashCode(): .NET randomises it per process, and TTSFactory already
  /// builds a cache key with it, so the "consistency" edit is one keystroke away.
  /// </remarks>
  private static string Token(string prefix, string value, bool withLength)
  {
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    var hex = Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    return withLength
      ? string.Concat(prefix, hex, "/", value.Length.ToString())
      : string.Concat(prefix, hex);
  }
}
