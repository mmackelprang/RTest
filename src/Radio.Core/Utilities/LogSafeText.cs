using System.Security.Cryptography;
using System.Text;

namespace Radio.Core.Utilities;

/// <summary>
/// Renders user-supplied text as a log-safe token: a stable hash prefix plus a character count.
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
/// The character count is deliberately exact rather than bucketed. It is the field that answers
/// "was the string empty, or absurdly long" — the two questions an operator actually asks about a
/// TTS payload — and an exact count over a space an attacker can already enumerate leaks nothing
/// the hash has not already leaked.
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
  /// It must not be replaced with <c>string.GetHashCode()</c>, which is randomised per process
  /// since .NET Core 3.0 and would make two log lines about the same utterance uncorrelatable.
  ///
  /// <paramref name="text"/>'s <c>Length</c> counts UTF-16 code units, so an emoji counts 2 and a
  /// combining sequence counts more than its glyphs. That is fine for the diagnostic question
  /// ("empty? absurd?") and it matches what every truncation site this replaced already used.
  /// </remarks>
  public static string For(string? text)
  {
    if (string.IsNullOrEmpty(text))
    {
      return Empty;
    }

    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
    return string.Concat(
      "txt:", Convert.ToHexString(hash, 0, 4).ToLowerInvariant(), "/", text.Length.ToString());
  }
}
