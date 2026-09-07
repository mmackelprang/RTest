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
/// ⚠ THE ORDER OF THE RULES IS PART OF THE SPEC — AND THE CHOSEN ORDER COSTS SOMETHING TWICE.
/// Both costs are stated here rather than left implied, because both were paid silently before
/// they were found:
///
/// 1. The prefix strip is anchored to the start of the RAW body, so it runs FIRST — nothing can
///    shift the token off the start before it is seen. The price is that it sees raw, un-normalised
///    spacing, because the whitespace tidy-up has not run yet. So the PATTERN has to tolerate
///    irregular spacing itself: "+15551234567  -  Dinner" (two spaces) is a real MMS shape, and a
///    pattern requiring a literal " - " left it alone and read the ten-digit number aloud — UAT
///    G-8's exact symptom. Running the tidy-up first would have normalised it, at the cost of the
///    anchor no longer being against the raw body. The anchor is worth more; the pattern absorbs
///    the difference.
/// 2. The lead-in is concatenated AFTER StripEmoji has already run over the body, so a sender name
///    never travels the body's path and never meets rule 6 there. Rule 6 applied to the body alone
///    is rule 6 half-applied: "Mom ❤️" is an ordinary Google contact and the engine says "red
///    heart". The name is therefore sanitised on its OWN path, in SanitizeName, immediately before
///    the lead-in is built.
///
/// The cap runs LAST and over the whole string including the lead-in, because GvMedia:MaxSpeechChars
/// is measured on the text the server receives and it REJECTS rather than truncates (plan PHN-3
/// C-106).
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
  /// ⚠⚠ THE HYPHEN SEPARATOR IS THE ENTIRE GUARD AND IS NOT OPTIONAL. Rule 5 says digit runs are
  /// kept verbatim because "verification codes are the single most valuable thing this feature
  /// reads", and the handoff's own example body BEGINS with one: "77971 is your Facebook
  /// confirmation code". A looser pattern — ^\d+\s+ — would eat it. Requiring a hyphen after an
  /// E.164-shaped run is what separates a sender prefix from a code.
  /// Pinned by ForMessage_DoesNotStripALeadingVerificationCode. See plan PHN-3 C-109.
  ///
  /// ⚠ THE SPACING AROUND THE HYPHEN IS TOLERANT; THE HYPHEN ITSELF IS NOT. "\s+-\s+" rather than a
  /// literal " - ", because "+15551234567  -  Dinner" (two spaces) is a live MMS shape that the
  /// literal form left intact — and this strip runs before the whitespace tidy-up, so nothing else
  /// normalises it first. ⛔ Do NOT go further: the hyphen requirement stays, and en/em dashes are
  /// deliberately NOT accepted (an owner deferral, not an oversight).
  ///
  /// ⚠ [0-9], not \d. \d matches Arabic-Indic and other Unicode decimal digits, so a body opening
  /// with a non-ASCII digit run would be treated as E.164-shaped. E.164 is ASCII by definition.
  /// </remarks>
  private static readonly Regex MmsSenderPrefix = new(
    @"^\+?[0-9]{7,15}\s+-\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

  /// <summary>Rule 4 — any URL becomes the words "a link".</summary>
  /// <remarks>
  /// ⚠ THE MATCH STOPS BEFORE TRAILING PUNCTUATION, and the final character class is what does it.
  /// A plain \S+ swallowed the sentence break: "Check https://ex.com. Thanks" became "Check a link
  /// Thanks", losing the period the engine needs for prosody — on the one rule whose entire purpose
  /// is stopping a link from sounding like gibberish. A URL that genuinely ends in a period is not
  /// a case worth protecting against that.
  /// </remarks>
  private static readonly Regex Url = new(
    @"\b(?:https?://|www\.)\S*[^\s.,;:!?)\]}]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

    // Rule 1 — the lead-in, and only when a name actually resolved. The name goes through rule 6
    // and the whitespace collapse on its own path; see SanitizeName for why it has to.
    var name = SanitizeName(senderName);
    if (name is not null)
    {
      text = string.Concat("Message from ", name, ". ", text);
    }

    // Rule 7 — the safety valve, LAST and over the whole string. No UI indication, per handoff
    // :398: the Stop button is the real control for a long read.
    if (text.Length <= MaxChars)
    {
      return text;
    }

    // ⚠ NEVER CUT A SURROGATE PAIR. System.Text.Json does not throw on a lone surrogate — it
    // substitutes U+FFFD — so a mid-pair cut ships a replacement character to the TTS engine with
    // no exception, no rejection, no toast and no log entry anywhere. Rule 6 does not make this
    // unreachable: U+1D400-1D7FF mathematical alphanumerics (common in spam SMS), U+1FB00+ and CJK
    // Ext-B are all astral and all survive the emoji strip.
    var cut = MaxChars;
    if (char.IsHighSurrogate(text[cut - 1]))
    {
      cut--;
    }

    return text[..cut];
  }

  /// <summary>
  /// The speakable form of a resolved contact name, or null when nothing speakable is left.
  /// </summary>
  /// <remarks>
  /// ⚠ RULE 6 APPLIES TO THE NAME AS WELL AS THE BODY, and it did not until PHN-3's review. The
  /// lead-in is concatenated after StripEmoji has run over the body, so a name handed to
  /// <see cref="ForMessage"/> had never been through it: ForMessage("Dinner at 7?", "Mom ❤️")
  /// produced "Message from Mom ❤️. Dinner at 7?" and the engine said "red heart" — exactly what
  /// handoff rule 6 (:396) exists to prevent. Emoji in a Google contact name ("Mom 💕", "Work 📞")
  /// are ordinary, and the resolved-name path is the one a demo exercises.
  ///
  /// ⚠ A name that reduces to NOTHING is treated as no name at all, so the caller falls through to
  /// the body alone. The alternative speaks "Message from . " — a lead-in naming nobody, which is
  /// strictly worse than the no-lead-in case rule 1 already calls the common one.
  /// </remarks>
  private static string? SanitizeName(string? senderName)
  {
    if (string.IsNullOrWhiteSpace(senderName))
    {
      return null;
    }

    var name = Whitespace.Replace(StripEmoji(senderName), " ").Trim();
    return name.Length == 0 ? null : name;
  }

  /// <summary>
  /// Removes emoji, pictographs, variation selectors and zero-width joiners.
  /// </summary>
  /// <remarks>
  /// ⚠ AN APPROXIMATION BY CODE-POINT RANGE, not a Unicode-property query, and it is stated as one
  /// rather than implied. .NET has no built-in Emoji_Presentation predicate, and the ranges below
  /// are the ones that carry emoji in practice. THREE known consequences, all accepted:
  ///
  /// 1. a legitimate arrow or a dingbat in a message body is dropped;
  /// 2. a novel emoji outside these ranges survives;
  /// 3. the DINGBAT CIRCLED DIGITS U+2776-2793 (➊➋➌) sit inside 0x2600-0x27BF and are dropped with
  ///    the rest of that block — which brushes rule 5's "keep digit runs verbatim". Recorded rather
  ///    than fixed: a circled digit is decoration and an ASCII digit run is a verification code, the
  ///    live corpus carries the latter and not the former, and carving three holes out of the
  ///    dingbat range to save them would cost more than it buys. It is a cost, not an absence of
  ///    one, and the original "two known consequences, both accepted" was not exhaustive.
  ///
  /// ⚠ Enumerates RUNES, not chars — and NOT for the reason this comment used to give. It claimed a
  /// char-wise filter "would leave half of one behind"; measured, that is false. A char loop over
  /// "😀Love you too! 😀" returns it FULLY INTACT, because neither surrogate of U+1F600 (0xD83D,
  /// 0xDE00) falls in any range below, so such a loop strips nothing at all. The real failure mode:
  /// a char-wise loop cannot observe an astral code point, so every non-BMP emoji would survive
  /// unstripped — silently, and on the majority of modern emoji.
  ///
  /// ⚠ The rune is encoded straight into the builder rather than appended via ToString(). There is
  /// no StringBuilder.Append(Rune) overload — verified, it binds to Append(object) and BOXES — so
  /// the span encode is the allocation-free form.
  /// </remarks>
  private static string StripEmoji(string text)
  {
    var sb = new StringBuilder(text.Length);
    Span<char> utf16 = stackalloc char[2];

    foreach (var rune in text.EnumerateRunes())
    {
      if (IsEmojiLike(rune.Value))
      {
        continue;
      }

      sb.Append(utf16[..rune.EncodeToUtf16(utf16)]);
    }

    return sb.ToString();
  }

  private static bool IsEmojiLike(int v) =>
    v is 0x200D or 0xFE0E or 0xFE0F        // ZWJ and the two variation selectors
    // ⚠ The keycap's other half. "1️⃣" is U+0031 U+FE0F U+20E3; with only the variation selector
    // listed, the digit and the selector went and the COMBINING ENCLOSING KEYCAP stayed, so
    // "1️⃣ Press one" reached the engine as "1⃣ Press one" — a stray combining mark. Note what this
    // deliberately does NOT do: the ASCII digit survives, per rule 5.
    || (v >= 0x20D0 && v <= 0x20FF)        // combining marks for symbols, incl. U+20E3 keycap
    || (v >= 0x2600 && v <= 0x27BF)        // misc symbols + dingbats
    || (v >= 0x2B00 && v <= 0x2BFF)        // misc symbols and arrows
    || (v >= 0x1F000 && v <= 0x1FAFF);     // emoticons, pictographs, flags, supplemental
}
