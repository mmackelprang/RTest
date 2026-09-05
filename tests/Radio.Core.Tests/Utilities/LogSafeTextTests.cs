using Radio.Core.Utilities;

namespace Radio.Core.Tests.Utilities;

/// <summary>
/// Pins the properties <c>TTS-11</c> relies on: the token is stable (so two log lines about the
/// same utterance correlate), it differs for different text, and it carries none of the input.
/// </summary>
/// <remarks>
/// ⚠ These tests pin a correlation property, not a confidentiality one. See the
/// <see cref="LogSafeText"/> remarks: a short utterance is recoverable by hashing candidates, and
/// no test here claims otherwise.
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
    Assert.NotEqual(LogSafeText.For("The front door is open"), LogSafeText.For("The garage is open"));
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

  [Fact]
  public void For_IsStableAcrossProcesses_OverUtf8Bytes()
  {
    // Hard-coded rather than recomputed, which is what makes this a cross-run pin. It fails if the
    // encoding changes (Encoding.Unicode) and it fails if the hash is swapped for
    // string.GetHashCode(), which .NET randomises per process. Both mutations are one keystroke
    // away — TTSFactory already builds a cache key with text.GetHashCode().
    // "héllo" is 6 UTF-8 bytes but 5 UTF-16 code units, so it also pins which of the two the
    // length field reports.
    Assert.Equal("txt:3c48591d/5", LogSafeText.For("héllo"));
  }
}
