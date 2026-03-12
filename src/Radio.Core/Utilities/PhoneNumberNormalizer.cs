namespace Radio.Core.Utilities;

public static class PhoneNumberNormalizer
{
  public static string Normalize(string phoneNumber)
  {
    if (string.IsNullOrWhiteSpace(phoneNumber))
      return string.Empty;

    var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());

    // Strip leading '1' for 11-digit US numbers
    if (digits.Length == 11 && digits[0] == '1')
      digits = digits[1..];

    return digits;
  }

  public static string GetLast7(string normalizedNumber)
  {
    if (normalizedNumber.Length <= 7)
      return normalizedNumber;

    return normalizedNumber[^7..];
  }
}
