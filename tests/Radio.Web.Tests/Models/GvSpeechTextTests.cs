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
  public void ForMessage_StripsEmojiAndTheSpacesTheyLeave()
  {
    // Handoff rule 6, its own example. ❤ is U+2764 followed by the U+FE0F variation selector; both
    // go, and the space the leading one left goes with the whitespace tidy-up.
    //
    // ⚠ This also pins the RUNE enumeration. A char-wise filter passes for U+2764 (it is a single
    // char) but would leave a lone surrogate behind for any astral emoji, so the second fixture is
    // a U+1F600 pair — the case that makes the difference visible.
    Assert.Equal("Love you too!", GvSpeechText.ForMessage("❤️Love you too! ❤️", null));
    Assert.Equal("Love you too!", GvSpeechText.ForMessage("\U0001F600Love you too! \U0001F600", null));
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

    Assert.NotNull(spoken);
    Assert.DoesNotMatch(new Regex(@"\d{1,2}:\d{2}"), spoken);
    Assert.DoesNotContain("AM", spoken, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("PM", spoken, StringComparison.OrdinalIgnoreCase);
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
