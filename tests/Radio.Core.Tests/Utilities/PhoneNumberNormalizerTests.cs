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
}
