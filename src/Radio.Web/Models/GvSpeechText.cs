using System.Text;
using System.Text.RegularExpressions;

namespace Radio.Web.Models;

/// <summary>
/// Turns an SMS body into the utterance the console speaks (handoff §B3, feature B).
/// </summary>
/// <remarks>
/// Pure, static, no services and no I/O — deliberately, so all eight rules are unit-testable
/// without a browser. ADR-029 §4.2: composition happens in Radio.Web; Radio.API speaks a finished
/// string and must not learn about MMS prefixes or emoji.
///
/// ⚠ THE ORDER OF THE RULES IS PART OF THE SPEC. The prefix strip is anchored to the start of the
/// RAW body, so it runs before anything else can shift it; the cap runs LAST and over the whole
/// string including the lead-in, because GvMedia:MaxSpeechChars is measured on the text the server
/// receives and it REJECTS rather than truncates (plan PHN-3 C-106).
/// </remarks>
public static class GvSpeechText
{
  /// <summary>
  /// Mirrors <c>GvMediaOptions.MaxSpeechChars</c>'s shipped default.
  /// </summary>
  /// <remarks>
  /// ⚠ Duplicated deliberately rather than fetched: Radio.Web has no route that exposes the
  /// server's value, and one round-trip per bubble to learn a constant would be worse than this
  /// coupling. GvSpeechTextTests pins the two together so the pair cannot drift silently. If a box
  /// configures the server BELOW this number, the caller's TextTooLong branch is what catches it.
  /// </remarks>
  public const int MaxChars = 1000;

  /// <summary>Rule 3 — the MMS sender prefix, e.g. <c>+15551234567 - Body…</c>.</summary>
  /// <remarks>
  /// ⚠⚠ THE " - " SEPARATOR IS THE ENTIRE GUARD AND IS NOT OPTIONAL. Rule 5 says digit runs are
  /// kept verbatim because "verification codes are the single most valuable thing this feature
  /// reads", and the handoff's own example body BEGINS with one: "77971 is your Facebook
  /// confirmation code". A looser pattern — ^\d+\s+ — would eat it. Requiring a literal
  /// space-hyphen-space after an E.164-shaped run is what separates a sender prefix from a code.
  /// Pinned by ForMessage_DoesNotStripALeadingVerificationCode. See plan PHN-3 C-109.
  /// </remarks>
  private static readonly Regex MmsSenderPrefix = new(
    @"^\+?\d{7,15} - ", RegexOptions.Compiled | RegexOptions.CultureInvariant);

  /// <summary>Rule 4 — any URL becomes the words "a link".</summary>
  private static readonly Regex Url = new(
    @"\b(?:https?://|www\.)\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

  private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

  /// <summary>
  /// The utterance for one inbound message, or null when there is nothing to speak.
  /// </summary>
  /// <param name="body">The message body — <c>SmsMessageDto.Text</c>, never <c>DisplayText</c>.</param>
  /// <param name="senderName">
  /// A RESOLVED contact name, or null. ⚠ Never a phone number: handoff :386 says "Do not read the
  /// identifier aloud", and the panel's name resolution falls back to one — so the caller passes
  /// null rather than that fallback. See plan PHN-3 C-108.
  /// </param>
  public static string? ForMessage(string? body, string? senderName)
  {
    if (string.IsNullOrWhiteSpace(body))
    {
      return null;
    }

    // Rule 3 — strip the MMS sender prefix, before anything can move it off the start.
    var text = MmsSenderPrefix.Replace(body, string.Empty);

    // Rule 4 — a URL is unspeakable; the words are more useful than the characters.
    text = Url.Replace(text, "a link");

    // Rule 6 — emoji go. "❤️Love you too! ❤️" must not become "red heart Love you too".
    text = StripEmoji(text);

    // Tidy up after 3/4/6, which can leave doubled or leading spaces. Rule 2 needs no code: the
    // timestamp is never part of the body and is simply never added.
    text = Whitespace.Replace(text, " ").Trim();

    if (text.Length == 0)
    {
      // A body that was ONLY emoji, or only a stripped prefix. Nothing to say.
      return null;
    }

    // Rule 1 — the lead-in, and only when a name actually resolved.
    if (!string.IsNullOrWhiteSpace(senderName))
    {
      text = string.Concat("Message from ", senderName.Trim(), ". ", text);
    }

    // Rule 7 — the safety valve, LAST and over the whole string. No UI indication, per handoff
    // :398: the Stop button is the real control for a long read.
    return text.Length <= MaxChars ? text : text[..MaxChars];
  }

  /// <summary>
  /// Removes emoji, pictographs, variation selectors and zero-width joiners.
  /// </summary>
  /// <remarks>
  /// ⚠ AN APPROXIMATION BY CODE-POINT RANGE, not a Unicode-property query, and it is stated as one
  /// rather than implied. .NET has no built-in Emoji_Presentation predicate, and the ranges below
  /// are the ones that carry emoji in practice. Two known consequences, both accepted:
  /// a legitimate arrow or a dingbat in a message body is dropped, and a novel emoji outside these
  /// ranges survives. Both are strictly better than speaking "red heart".
  ///
  /// ⚠ Enumerates RUNES, not chars. Most emoji are surrogate pairs, and a char-wise filter would
  /// leave half of one behind — which renders as a replacement character and may well be SPOKEN.
  /// </remarks>
  private static string StripEmoji(string text)
  {
    var sb = new StringBuilder(text.Length);

    foreach (var rune in text.EnumerateRunes())
    {
      if (IsEmojiLike(rune.Value))
      {
        continue;
      }

      sb.Append(rune.ToString());
    }

    return sb.ToString();
  }

  private static bool IsEmojiLike(int v) =>
    v is 0x200D or 0xFE0E or 0xFE0F        // ZWJ and the two variation selectors
    || (v >= 0x2600 && v <= 0x27BF)        // misc symbols + dingbats
    || (v >= 0x2B00 && v <= 0x2BFF)        // misc symbols and arrows
    || (v >= 0x1F000 && v <= 0x1FAFF);     // emoticons, pictographs, flags, supplemental
}
