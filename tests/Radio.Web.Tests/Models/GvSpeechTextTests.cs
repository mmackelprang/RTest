using System.Text.RegularExpressions;
using Radio.Core.Configuration;
using Radio.Web.Models;

namespace Radio.Web.Tests.Models;

/// <summary>
/// GvSpeechText — the eight content rules of handoff §B3, feature B (plan PHN-3 Task 2).
/// </summary>
/// <remarks>
/// ⚠ The fixtures are the handoff's OWN examples, verbatim (:376-402), so a reviewer can diff this
/// file against the design rather than against a paraphrase of it.
///
/// ⚠ These tests prove the STRING. They cannot prove that the console speaks it, that the engine
/// pronounces a digit run intelligibly, or that an emoji-stripped body sounds right — those are
/// on-box UAT (plan §4.5 items 17-20) and nothing green here may be cited for them.
/// </remarks>
public class GvSpeechTextTests
{
  // ── Rule 3 vs rule 5: the collision this row exists to get right (plan C-109) ────────────────

  [Fact]
  public void ForMessage_DoesNotStripALeadingVerificationCode()
  {
    // ⭐ THE most important test in this row. Rule 5: "verification codes are the single most
    // valuable thing this feature reads." Rule 3 strips a leading E.164 run — and a loose reading
    // of rule 3 eats rule 5's headline example. The " - " separator is the entire guard.
    //
    // ⚠ TWO fixtures, because one of them cannot falsify the mutation the plan names. The handoff's
    // own example is a FIVE-digit short code, which `^\+?\d{7,15}\s+` does not match on length
    // alone — so it would survive that loosening and the test would pass against a broken guard.
    // The seven-digit fixture is what actually pins the separator: it satisfies the digit floor and
    // is followed by whitespace, so only the literal " - " requirement keeps it intact.
    const string shortCode = "77971 is your Facebook confirmation code";
    const string longRun = "8675309 is your verification code";

    Assert.Equal(shortCode, GvSpeechText.ForMessage(shortCode, null));
    Assert.Equal(longRun, GvSpeechText.ForMessage(longRun, null));
  }

  [Fact]
  public void ForMessage_StripsTheMmsSenderPrefix()
  {
    // Handoff rule 3 / UAT G-8: two live threads carry "+1XXXXXXXXXX - <text>" bodies, and left
    // alone TTS opens by reading a ten-digit number nobody asked for.
    Assert.Equal("Dinner at 7?", GvSpeechText.ForMessage("+15551234567 - Dinner at 7?", null));
  }

  [Fact]
  public void ForMessage_LeavesABodyWithNoPrefixAlone()
  {
    // Three digits is not an E.164-shaped token, whatever follows it. The digit floor is the other
    // half of C-109's guard: widening it below 7 makes ordinary bodies lose their opening number.
    Assert.Equal("555 - is not a prefix", GvSpeechText.ForMessage("555 - is not a prefix", null));

    // ⭐ Re-pinned when the pattern was loosened from a literal " - " to \s+-\s+ (see
    // ForMessage_StripsAnIrregularlySpacedMmsPrefix). Loosening the SPACING must not loosen the
    // digit floor, and these are the two shapes that would go first if it did.
    Assert.Equal(
      "2026-09-07 - meeting at noon",
      GvSpeechText.ForMessage("2026-09-07 - meeting at noon", null));
    Assert.Equal("10 - 15 minutes off", GvSpeechText.ForMessage("10 - 15 minutes off", null));
  }

  [Fact]
  public void ForMessage_StripsAnIrregularlySpacedMmsPrefix()
  {
    // ⭐ UAT G-8's symptom, surviving the first fix of it. The original pattern required a LITERAL
    // " - ", so "+15551234567  -  Dinner" (two spaces either side) passed through untouched and the
    // console opened by reading a ten-digit number — the one thing handoff :386 forbids. The strip
    // runs before the whitespace tidy-up (it is anchored to the RAW body), so nothing else was ever
    // going to normalise the spacing for it.
    //
    // ⛔ The HYPHEN is still required and en/em dashes are still not accepted — a deliberate
    // deferral, not an oversight. ForMessage_DoesNotStripALeadingVerificationCode is what that
    // requirement protects.
    Assert.Equal("Dinner", GvSpeechText.ForMessage("+15551234567  -  Dinner", null));
    Assert.Equal("Dinner", GvSpeechText.ForMessage("+15551234567\t-\tDinner", null));
  }

  // ── The remaining transformations ───────────────────────────────────────────────────────────

  [Fact]
  public void ForMessage_ReplacesUrlsWithTheWordsALink()
  {
    // Handoff rule 4: the live corpus is full of long tracking URLs, and reading one character by
    // character is the single most likely cause of "I hit play and it read gibberish".
    Assert.Equal("See a link now", GvSpeechText.ForMessage("See https://ex.com/a?b=1 now", null));
  }

  [Fact]
  public void ForMessage_LeavesThePunctuationThatFollowsAUrl()
  {
    // ⭐ \S+ swallowed the sentence break: "Check https://ex.com. Thanks" became "Check a link
    // Thanks", so the engine ran two sentences together — on the one rule whose entire purpose is
    // keeping a link from sounding like gibberish.
    Assert.Equal("Check a link. Thanks", GvSpeechText.ForMessage("Check https://ex.com. Thanks", null));
    Assert.Equal("a link, then", GvSpeechText.ForMessage("https://ex.com, then", null));

    // The neighbours the narrowed pattern must not disturb: a URL at position 0, two of them in one
    // body, a path whose own dots are interior, and a token that only LOOKS like it starts one.
    Assert.Equal("a link is nice", GvSpeechText.ForMessage("https://ex.com is nice", null));
    Assert.Equal("a link and a link!", GvSpeechText.ForMessage("https://a.com and www.b.com!", null));
    Assert.Equal("a link", GvSpeechText.ForMessage("https://ex.com/a.b", null));
    Assert.Equal("xhttps://ex.com", GvSpeechText.ForMessage("xhttps://ex.com", null));
  }

  [Fact]
  public void ForMessage_StripsEmojiAndTheSpacesTheyLeave()
  {
    // Handoff rule 6, its own example. ❤ is U+2764 followed by the U+FE0F variation selector; both
    // go, and the space the leading one left goes with the whitespace tidy-up.
    //
    // ⚠ This also pins the RUNE enumeration — but NOT for the reason first written here. The claim
    // was that a char-wise filter "would leave a lone surrogate behind"; measured, it leaves the
    // emoji FULLY INTACT, because neither surrogate of U+1F600 (0xD83D, 0xDE00) falls in any range
    // IsEmojiLike lists, so a char loop strips nothing at all. A char-wise loop cannot observe an
    // astral code point, so every non-BMP emoji survives unstripped. The second fixture is what
    // makes that visible, and the plan's named mutation (EnumerateRunes → a char loop) still fails
    // it — for that reason rather than the stated one.
    Assert.Equal("Love you too!", GvSpeechText.ForMessage("❤️Love you too! ❤️", null));
    Assert.Equal("Love you too!", GvSpeechText.ForMessage("\U0001F600Love you too! \U0001F600", null));
  }

  [Fact]
  public void ForMessage_StripsTheKeycapCombiningMark()
  {
    // ⭐ U+20E3 COMBINING ENCLOSING KEYCAP was in no listed range. "1️⃣" is U+0031 U+FE0F U+20E3, so
    // the digit and the variation selector went and the combining mark stayed — a stray mark
    // reaching the TTS engine attached to nothing.
    //
    // ⚠ Escapes, not literals: U+FE0F and U+20E3 are invisible in a source listing, so a literal
    // fixture cannot be reviewed and cannot survive a re-encoding intact.
    Assert.Equal("Press one", GvSpeechText.ForMessage("\uFE0F\u20E3 Press one", null));
    Assert.Equal("1 Press one", GvSpeechText.ForMessage("1\uFE0F\u20E3 Press one", null));
  }

  [Fact]
  public void ForMessage_KeepsTheDigitOfAKeycapOnlyBody()
  {
    // ⚠ RECORDED BECAUSE IT LOOKS LIKE IT BELONGS IN THE EMOJI-ONLY→NULL THEORY AND DOES NOT.
    // "1️⃣" is a digit wearing an emoji; stripping U+FE0F and U+20E3 leaves "1", which is not
    // nothing. Rule 5 ("keep digit runs verbatim") is the reason that is the right answer rather
    // than a gap: the ASCII digit is content, the enclosing mark is decoration. Adding "1️⃣" to
    // ForMessage_ReturnsNullForNullEmptyOrEmojiOnly would assert the opposite and fail.
    Assert.Equal("1", GvSpeechText.ForMessage("1\uFE0F\u20E3", null));
  }

  // ── Rule 6 applies to the NAME too (the lead-in escaped it) ─────────────────────────────────

  [Fact]
  public void ForMessage_StripsEmojiFromTheSenderName()
  {
    // ⭐ The lead-in is concatenated AFTER StripEmoji has run over the body, so the name never met
    // rule 6 on that path: ForMessage("Dinner at 7?", "Mom ❤️") produced "Message from Mom ❤️.
    // Dinner at 7?" and the engine said "red heart" — precisely what handoff rule 6 (:396) exists
    // to prevent. "Mom 💕" and "Work 📞" are ordinary Google contact names, and the resolved-name
    // path is the one UAT item 18 exercises.
    var spoken = GvSpeechText.ForMessage("Dinner at 7?", "Mom ❤️");

    Assert.Equal("Message from Mom. Dinner at 7?", spoken);
    Assert.DoesNotContain("❤", spoken);
  }

  [Fact]
  public void ForMessage_TreatsAnEmojiOnlySenderNameAsNoName()
  {
    // A name that sanitises to nothing is NO name, not an empty one. The alternative speaks
    // "Message from . Dinner at 7?" — a lead-in naming nobody, worse than the no-lead-in case rule
    // 1 already calls the common one.
    var spoken = GvSpeechText.ForMessage("Dinner at 7?", "❤️");

    Assert.Equal("Dinner at 7?", spoken);
    Assert.DoesNotContain("Message from", spoken);
  }

  [Fact]
  public void ForMessage_AddsTheLeadInOnlyWhenANameResolved()
  {
    // Handoff rule 1. 14 of the 20 live threads have no resolvable name, so "no lead-in" is the
    // COMMON case, not the fallback — an unconditional lead-in would speak an identifier to the
    // room 70% of the time.
    Assert.Equal("Message from Jane. Dinner at 7?", GvSpeechText.ForMessage("Dinner at 7?", "Jane"));
    Assert.Equal("Dinner at 7?", GvSpeechText.ForMessage("Dinner at 7?", null));
    Assert.Equal("Dinner at 7?", GvSpeechText.ForMessage("Dinner at 7?", "   "));
  }

  [Fact]
  public void ForMessage_NeverIncludesATimestamp()
  {
    // Handoff rule 2. A guard rather than a transformation: the timestamp lives on SmsMessageDto
    // and in the bubble's .msg-meta, and the only way it could reach the utterance is if someone
    // added it here. Nothing to strip; something to keep out.
    var spoken = GvSpeechText.ForMessage("Dinner at seven", "Jane");

    // ⚠ WORD-BOUNDED, not a substring search. The meridiem clauses were
    // DoesNotContain("AM", …, OrdinalIgnoreCase), which a sender called "Pam" or "Sam" and a body
    // containing "I am" would have failed — a fixture change away from a red suite that says
    // nothing about rule 2.
    Assert.NotNull(spoken);
    Assert.DoesNotMatch(new Regex(@"\d{1,2}:\d{2}"), spoken);
    Assert.DoesNotMatch(new Regex(@"\b(?:AM|PM)\b", RegexOptions.IgnoreCase), spoken);
  }

  // ── Rule 7, the cap ─────────────────────────────────────────────────────────────────────────

  [Fact]
  public void ForMessage_CapsAtMaxChars()
  {
    var spoken = GvSpeechText.ForMessage(new string('a', 1500), null);

    Assert.NotNull(spoken);
    Assert.Equal(GvSpeechText.MaxChars, spoken.Length);
  }

  [Fact]
  public void ForMessage_CapsTheWholeStringIncludingTheLeadIn()
  {
    // ⭐ Not a formality. The server REJECTS over-length text as TextTooLong rather than truncating
    // it (plan C-106), so capping the body and THEN prepending "Message from Jane. " produces a 400
    // on exactly the messages that have a resolved sender — the 30% of threads a demo would use.
    var spoken = GvSpeechText.ForMessage(new string('a', 995), "Jane");

    Assert.NotNull(spoken);
    Assert.True(
      spoken.Length <= GvSpeechText.MaxChars,
      $"the lead-in pushed the utterance to {spoken.Length}, over the server's cap");
    Assert.Equal(GvSpeechText.MaxChars, spoken.Length);
    Assert.StartsWith("Message from Jane. ", spoken, StringComparison.Ordinal);
  }

  [Fact]
  public void ForMessage_NeverCutsASurrogatePairAtTheCap()
  {
    // ⭐ A SILENT failure, which is what makes it worth a test. System.Text.Json does NOT throw on a
    // lone surrogate — it substitutes U+FFFD — so a mid-pair cut ships a replacement character to
    // the TTS engine with no exception, no rejection, no toast and no log line anywhere.
    //
    // ⚠ Rule 6 does not make this unreachable. U+1D400-1D7FF MATHEMATICAL BOLD CAPITALS are outside
    // every IsEmojiLike range and are common in spam SMS; U+1FB00+ and CJK Ext-B survive too. The
    // leading "x" is what puts the cut MID-PAIR: without it every pair starts on an even index and
    // a 1000-char cut lands on a boundary by luck rather than by design.
    var body = "x" + string.Concat(Enumerable.Repeat("\U0001D400", 600));

    var spoken = GvSpeechText.ForMessage(body, null);

    Assert.NotNull(spoken);
    Assert.InRange(spoken.Length, GvSpeechText.MaxChars - 1, GvSpeechText.MaxChars);
    Assert.False(HasLoneSurrogate(spoken), "the cap split a surrogate pair");
  }

  private static bool HasLoneSurrogate(string s)
  {
    for (var i = 0; i < s.Length; i++)
    {
      if (char.IsHighSurrogate(s[i]))
      {
        if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))
        {
          return true;
        }

        i++;
        continue;
      }

      if (char.IsLowSurrogate(s[i]))
      {
        return true;
      }
    }

    return false;
  }

  // ── Nothing to say ──────────────────────────────────────────────────────────────────────────

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("  ")]
  [InlineData("❤️")]
  public void ForMessage_ReturnsNullForNullEmptyOrEmojiOnly(string? body)
  {
    // NULL, not "". MessageBubble gates the whole button on this being non-null (CanSpeak), so an
    // empty string here would render a play button that starts a playback with nothing in it.
    Assert.Null(GvSpeechText.ForMessage(body, "Jane"));
  }

  // ── The coupling to the server's constant ───────────────────────────────────────────────────

  [Fact]
  public void MaxChars_MatchesTheServersDefault()
  {
    // ⭐ The two numbers are duplicated deliberately — Radio.Web has no route that exposes the
    // server's value — so this is the only thing standing between them and a silent drift. If it
    // ever fails, the fix is to move MaxChars, not to change this assertion: the server REJECTS
    // over-length text, so a client cap above the server's is a 400 on a live surface.
    // (Argument order is xUnit2000's, not a statement about which one is authoritative — the
    // analyzer requires the constant in the 'expected' slot.)
    Assert.Equal(GvSpeechText.MaxChars, new GvMediaOptions().MaxSpeechChars);
  }
}
