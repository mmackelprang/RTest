using Radio.Core.Utilities;

namespace Radio.Core.Tests.Utilities;

/// <summary>
/// Pins the properties <c>TTS-11</c> and <c>PHN-5</c> rely on: the token is stable (so two log
/// lines about the same utterance, or the same caller, correlate), it differs for different
/// inputs, and it carries none of the input.
/// </summary>
/// <remarks>
/// ⚠ These tests pin a correlation property, not a confidentiality one. See the
/// <see cref="LogSafeText"/> remarks: a short utterance is recoverable by hashing candidates, and
/// no test here claims otherwise. ⚠ The caveat is STRONGER for <see cref="LogSafeText.ForPhone"/>:
/// a NANP number is ten digits, so the entire candidate space can be hashed in minutes. What it
/// defends against is a person READING the log, which is the exposure PHN-5 was filed for.
/// </remarks>
public class LogSafeTextTests
{
  /// <summary>
  /// A phrase chosen to be absent from every other fixture in the suite, so a "does not contain"
  /// assertion cannot pass by accident against generic text.
  /// </summary>
  private const string Sentinel = "Marmalade sentinel four seven";

  [Fact]
  public void For_SameInput_ProducesSameToken()
  {
    // The entire point of choosing a hash over a counter: two sites logging the same utterance
    // print the same token, which is what makes L1 → L2 → L9 → L4 a traceable chain.
    Assert.Equal(LogSafeText.For(Sentinel), LogSafeText.For(Sentinel));
  }

  [Fact]
  public void For_DifferentInput_ProducesDifferentToken()
  {
    // ⚠ SAME LENGTH, deliberately. An earlier revision compared a 22-character string with an
    // 18-character one, so it also passed against a length-only implementation that had dropped
    // the hash entirely — which is the one thing this test exists to rule out.
    const string a = "The front door is open";
    const string b = "The front gate is shut";
    Assert.Equal(a.Length, b.Length);

    Assert.NotEqual(LogSafeText.For(a), LogSafeText.For(b));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  public void For_NullOrEmpty_ReturnsEmptyToken(string? text)
  {
    Assert.Equal(LogSafeText.Empty, LogSafeText.For(text));
  }

  [Fact]
  public void For_TokenCarriesNoneOfTheInput()
  {
    var token = LogSafeText.For(Sentinel);

    Assert.DoesNotContain("Marmalade", token, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("sentinel", token, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("four seven", token, StringComparison.OrdinalIgnoreCase);
    // Guard against a degenerate implementation that returns a constant: the token must still
    // identify this input.
    Assert.NotEqual(LogSafeText.Empty, token);
    Assert.EndsWith("/" + Sentinel.Length.ToString(), token, StringComparison.Ordinal);
  }

  /// <summary>
  /// A number chosen so a "does not contain" assertion cannot pass by accident against fixture
  /// data, and whose last four digits are searchable on their own. Every phone test below asserts
  /// on BOTH: a regression that reinstated the <c>***{last4}</c> mask PHN-5 deleted would pass a
  /// test that only looked for the whole number.
  /// </summary>
  private const string SentinelNumber = "5550137424";

  [Fact]
  public void ForPhone_SameNumberInDifferentFormats_ProducesSameToken()
  {
    // ⭐ The property the whole mask is for (PHN-5 C-98). The same subscriber reaches these log
    // lines in at least three spellings — the hub sends one, PBAP another, and
    // PhoneNumberNormalizer produces a third — so hashing the RAW string would give one caller
    // three different tokens and leave an operator worse off than "***7424", which at least
    // collapses all three.
    // Falsifying mutation: drop the Normalize call in ForPhone → this fails.
    var national = LogSafeText.ForPhone("+1 (555) 013-7424");
    var e164 = LogSafeText.ForPhone("15550137424");
    var normalized = LogSafeText.ForPhone(SentinelNumber);

    Assert.Equal(normalized, national);
    Assert.Equal(normalized, e164);
    // Not the degenerate answer: all three agreeing on "phn:empty" would also satisfy the above.
    Assert.NotEqual(LogSafeText.EmptyPhone, normalized);
  }

  [Theory]
  [InlineData("+1 (555) 013-7424")]
  [InlineData("15550137424")]
  [InlineData("555-013-7424")]
  [InlineData(SentinelNumber)]
  public void ForPhone_IsIdempotentUnderNormalization(string input)
  {
    // The property that lets every call site pass whatever spelling it happens to hold, which is
    // what keeps the eleven edits to one argument each. Normalize strips non-digits and then a
    // leading '1' only at 11 digits, so a second application is a no-op.
    // Falsifying mutation: drop the Normalize call in ForPhone → the first three cases fail.
    Assert.Equal(
      LogSafeText.ForPhone(PhoneNumberNormalizer.Normalize(input)),
      LogSafeText.ForPhone(input));
  }

  [Fact]
  public void ForPhone_DifferentNumbers_ProduceDifferentTokens()
  {
    // ⚠ EQUAL DIGIT LENGTH, deliberately, for the reason the text sibling above records: a pair of
    // different lengths would also pass against a length-only implementation that had dropped the
    // hash entirely, which is the one thing this test exists to rule out. ForPhone emits no length
    // at all, so the degenerate implementation here is a CONSTANT.
    // Falsifying mutation: replace the hash with a constant → this fails.
    const string a = "5550137424";
    const string b = "5550137425";
    Assert.Equal(
      PhoneNumberNormalizer.Normalize(a).Length,
      PhoneNumberNormalizer.Normalize(b).Length);

    Assert.NotEqual(LogSafeText.ForPhone(a), LogSafeText.ForPhone(b));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("unknown")]
  [InlineData("(withheld)")]
  public void ForPhone_NullEmptyOrDigitless_ReturnsEmptyPhone(string? phoneNumber)
  {
    // A non-empty input with no digits normalises to empty and therefore returns the SAME token as
    // null. That is the intended answer — both mean "no usable number" — and it is pinned here
    // because it is otherwise indistinguishable from a bug (PHN-5 C-98).
    Assert.Equal(LogSafeText.EmptyPhone, LogSafeText.ForPhone(phoneNumber));
  }

  [Fact]
  public void ForPhone_TokenCarriesNoDigitsOfTheInput()
  {
    var token = LogSafeText.ForPhone(SentinelNumber);

    Assert.DoesNotContain(SentinelNumber, token, StringComparison.Ordinal);
    // ⚠ The last four separately. This is the assertion that fails against a reinstated
    // "***{last4}" mask, which the whole-number check above would sail past.
    Assert.DoesNotContain("7424", token, StringComparison.Ordinal);
    // Guard against a degenerate implementation that returns a constant: the token must still
    // identify this input.
    // Falsifying mutation: return the input → the first two fail.
    Assert.NotEqual(LogSafeText.EmptyPhone, token);
  }

  [Fact]
  public void ForPhone_IsStableAcrossProcesses()
  {
    // Hard-coded rather than recomputed, which is what makes this a cross-run pin — exactly as
    // For_IsStableAcrossProcesses_OverUtf8Bytes hard-codes "txt:3c48591d/5".
    // ⛔ Writing this as Assert.Equal(ForPhone(x), ForPhone(x)) would pass against every
    // implementation including a constant, and would pin nothing across processes.
    // Falsifying mutations: Encoding.UTF8 → Encoding.Unicode fails; SHA256 → GetHashCode() fails
    // (and .NET randomises the latter per process, so two log lines about one caller would stop
    // correlating — the entire point of choosing a hash).
    // ⚠ No "/{length}" suffix, unlike For: a phone number's length answers no triage question and
    // would separate national from E.164 format for free.
    Assert.Equal("phn:c2d0c575", LogSafeText.ForPhone(SentinelNumber));
  }

  [Fact]
  public void For_IsStableAcrossProcesses_OverUtf8Bytes()
  {
    // Hard-coded rather than recomputed, which is what makes this a cross-run pin. It fails if the
    // encoding changes (Encoding.Unicode) and it fails if the hash is swapped for
    // string.GetHashCode(), which .NET randomises per process. Both mutations are one keystroke
    // away — TTSFactory already builds a cache key with text.GetHashCode().
    // "héllo" is 6 UTF-8 bytes but 5 UTF-16 code units, so it also pins which of the two the
    // length field reports.
    //
    // ⭐ PHN-5 gave this test a second job and did not need a second test for it. Task 1 extracted
    // the shared hash into LogSafeText.Token and routed For through it — an edit to a shipped,
    // tested method — and this exact literal is what made that safe: any change to the encoding,
    // the byte count, the prefix or the length format fails right here. It was run immediately
    // before the extraction and immediately after, and passed both times. A separately-named
    // For_IsUnchangedByTheTokenExtraction would assert the same equality against the same literal
    // and add nothing but a second place to update.
    Assert.Equal("txt:3c48591d/5", LogSafeText.For("héllo"));
  }
}
