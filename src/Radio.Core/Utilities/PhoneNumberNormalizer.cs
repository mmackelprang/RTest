namespace Radio.Core.Utilities;

public static class PhoneNumberNormalizer
{
  /// <summary>
  /// Digits only, with the North American country code dropped from an 11-digit number, so
  /// <c>+1 (919) 371-8044</c>, <c>19193718044</c> and <c>9193718044</c> all become <c>9193718044</c>.
  /// Other country codes are kept (<c>+44 20 7946 0958</c> → <c>442079460958</c>); the last-7 tier of
  /// <see cref="FindMatch{T}"/> is what lets those match a number stored without one.
  /// </summary>
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

  /// <summary>
  /// PHN-14: the one matching rule every caller-name lookup uses — the API's stored PBAP contacts
  /// (<c>PbapContactRepository</c>, in SQL), RotaryPhone's contacts in the API's announcement, and the same
  /// list in the Web's incoming-call banner. Two tiers, in order: an exact match on the
  /// <see cref="Normalize"/>d digits, then a match on the last seven digits (both sides at least seven).
  /// The first item in <paramref name="items"/> wins within a tier, so the caller's order is the tie-break.
  /// </summary>
  /// <remarks>
  /// ⚠ The last-7 tier is the PBAP repository's long-standing rule (its test
  /// <c>FindByPhoneNumber_Last7Fallback_ShouldMatch</c> pins a different area code matching), carried here
  /// unchanged so every source answers alike. It tolerates any country code, and it will also match two
  /// different numbers that share their last seven digits.
  /// </remarks>
  /// <returns>The matching item, or <c>default</c> when nothing matches or the number has no digits.</returns>
  public static T? FindMatch<T>(IEnumerable<T>? items, Func<T, string?> numberOf, string? number)
  {
    string key = Normalize(number ?? "");
    if (items is null || key.Length == 0)
    {
      return default;
    }

    var list = items as IReadOnlyList<T> ?? items.ToList();
    foreach (var item in list)
    {
      if (Normalize(numberOf(item) ?? "") == key)
      {
        return item;
      }
    }

    if (key.Length < 7)
    {
      return default;
    }

    string last7 = GetLast7(key);
    foreach (var item in list)
    {
      string candidate = Normalize(numberOf(item) ?? "");
      if (candidate.Length >= 7 && GetLast7(candidate) == last7)
      {
        return item;
      }
    }
    return default;
  }
}
