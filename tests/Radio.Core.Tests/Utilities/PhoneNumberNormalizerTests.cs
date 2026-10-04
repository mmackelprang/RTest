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
  [InlineData("+19193718044")]
  [InlineData("19193718044")]
  [InlineData("9193718044")]
  [InlineData("+1 (919) 371-8044")]
  public void FindMatch_ToleratesALeadingCountryCode_OnEitherSide(string incoming)
  {
    // Stored three ways, as a phone book or RotaryPhone might hold it; each must match every incoming form.
    foreach (string stored in new[] { "+19193718044", "19193718044", "9193718044", "(919) 371-8044" })
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
    var entries = new[] { new Entry("Same last seven", "5553718044"), new Entry("Exact", "9193718044") };

    Assert.Equal("Exact", PhoneNumberNormalizer.FindMatch(entries, e => e.Phone, "+1 919 371 8044")!.Name);
  }

  [Fact]
  public void FindMatch_FallsBackToTheLastSevenDigits_LikeThePbapRepository()
  {
    var entries = new[] { new Entry("Local", "371-8044") };

    Assert.Equal("Local", PhoneNumberNormalizer.FindMatch(entries, e => e.Phone, "9193718044")!.Name);
  }

  [Fact]
  public void FindMatch_WithinATier_TheFirstItemWins()
  {
    var entries = new[] { new Entry("First", "9193718044"), new Entry("Second", "+19193718044") };

    Assert.Equal("First", PhoneNumberNormalizer.FindMatch(entries, e => e.Phone, "9193718044")!.Name);
  }

  [Theory]
  [InlineData("")]
  [InlineData("Unknown")]
  [InlineData("123")]          // too short for the last-7 tier, and no exact entry
  public void FindMatch_NoDigitsOrNoMatch_ReturnsNull(string incoming)
  {
    var entries = new[] { new Entry("Owner", "9193718044"), new Entry("Blank", null) };

    Assert.Null(PhoneNumberNormalizer.FindMatch(entries, e => e.Phone, incoming));
  }

  [Fact]
  public void FindMatch_ANullList_ReturnsNull()
  {
    Assert.Null(PhoneNumberNormalizer.FindMatch<Entry>(null, e => e.Phone, "9193718044"));
  }
}
