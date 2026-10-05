using Radio.Core.Utilities;

namespace Radio.Core.Tests.Utilities;

public class PhoneNumberNormalizerTests
{
  [Theory]
  [InlineData("+1 (555) 123-4567", "5551234567")]
  [InlineData("555.123.4567", "5551234567")]
  [InlineData("15551234567", "5551234567")]     // 11-digit with leading 1
  [InlineData("5551234567", "5551234567")]       // already 10 digits
  [InlineData("+44 20 7946 0958", "442079460958")] // international, no strip
  [InlineData("", "")]
  [InlineData("  ", "")]
  public void Normalize_ShouldStripNonDigitsAndLeading1(string input, string expected)
  {
    Assert.Equal(expected, PhoneNumberNormalizer.Normalize(input));
  }

  [Theory]
  [InlineData("5551234567", "1234567")]
  [InlineData("1234567", "1234567")]
  [InlineData("123", "123")]
  public void GetLast7_ShouldReturnTrailingDigits(string input, string expected)
  {
    Assert.Equal(expected, PhoneNumberNormalizer.GetLast7(input));
  }

  // ── PHN-14: FindMatch, the one rule every caller-name source uses ──

  private sealed record Entry(string Name, string? Phone);

  [Theory]
  [InlineData("+19195550142")]
  [InlineData("19195550142")]
  [InlineData("9195550142")]
  [InlineData("+1 (919) 555-0142")]
  public void FindMatch_ToleratesALeadingCountryCode_OnEitherSide(string incoming)
  {
    // Stored three ways, as a phone book or RotaryPhone might hold it; each must match every incoming form.
    foreach (string stored in new[] { "+19195550142", "19195550142", "9195550142", "(919) 555-0142" })
    {
      var match = PhoneNumberNormalizer.FindMatch(new[] { new Entry("Owner", stored) }, e => e.Phone, incoming);
      Assert.True(match is not null, $"stored '{stored}' did not match incoming '{incoming}'");
      Assert.Equal("Owner", match!.Name);
    }
  }

  [Fact]
  public void FindMatch_AnExactMatchBeatsAnEarlierLast7Match()
  {
    // The last-7 candidate comes FIRST in the list, so only the tiering can pick the exact one.
    var entries = new[] { new Entry("Local entry", "555-0142"), new Entry("Exact", "9195550142") };

    Assert.Equal("Exact", PhoneNumberNormalizer.FindMatch(entries, e => e.Phone, "+1 919 555 0142")!.Name);
  }

  [Fact]
  public void FindMatch_FallsBackToTheLastSevenDigits_LikeThePbapRepository()
  {
    var entries = new[] { new Entry("Local", "555-0142") };

    Assert.Equal("Local", PhoneNumberNormalizer.FindMatch(entries, e => e.Phone, "9195550142")!.Name);
  }

  [Theory]
  [InlineData("5555550142")]      // another area code, same last seven
  [InlineData("+1 555 555 0142")]
  [InlineData("15555550142")]
  public void FindMatch_AStrangerWhoSharesTheLastSeven_DoesNotMatchAFullStoredNumber(string stranger)
  {
    // Owner ruling 2026-10-03: a stored 10/11-digit number matches only on the full number, never its last seven.
    var entries = new[] { new Entry("Owner", "+1 (919) 555-0142") };

    Assert.Null(PhoneNumberNormalizer.FindMatch(entries, e => e.Phone, stranger));
  }

  [Theory]
  [InlineData("9195550142")]
  [InlineData("+19195550142")]
  [InlineData("5550142")]
  public void FindMatch_ASevenDigitLocalEntry_StillMatches(string incoming)
  {
    var entries = new[] { new Entry("Local", "555-0142") };

    Assert.Equal("Local", PhoneNumberNormalizer.FindMatch(entries, e => e.Phone, incoming)!.Name);
  }

  [Theory]
  [InlineData("9195550142", "9195550142", true)]
  [InlineData("555-0142", "9195550142", false)]
  [InlineData("5550142", "5550142", true)]
  public void TryFindMatch_ReportsTheTier(string stored, string incoming, bool exact)
  {
    Assert.True(PhoneNumberNormalizer.TryFindMatch(new[] { new Entry("X", stored) }, e => e.Phone, incoming,
      out var match, out bool isExact));
    Assert.Equal("X", match!.Name);
    Assert.Equal(exact, isExact);
  }

  [Fact]
  public void FindMatch_WithinATier_TheFirstItemWins()
  {
    var entries = new[] { new Entry("First", "9195550142"), new Entry("Second", "+19195550142") };

    Assert.Equal("First", PhoneNumberNormalizer.FindMatch(entries, e => e.Phone, "9195550142")!.Name);
  }

  [Theory]
  [InlineData("")]
  [InlineData("Unknown")]
  [InlineData("123")]          // too short for the last-7 tier, and no exact entry
  public void FindMatch_NoDigitsOrNoMatch_ReturnsNull(string incoming)
  {
    var entries = new[] { new Entry("Owner", "9195550142"), new Entry("Blank", null) };

    Assert.Null(PhoneNumberNormalizer.FindMatch(entries, e => e.Phone, incoming));
  }

  [Fact]
  public void FindMatch_ANullList_ReturnsNull()
  {
    Assert.Null(PhoneNumberNormalizer.FindMatch<Entry>(null, e => e.Phone, "9195550142"));
  }
}
