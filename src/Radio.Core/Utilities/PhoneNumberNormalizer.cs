namespace Radio.Core.Utilities;

public static class PhoneNumberNormalizer
{
  /// <summary>
  /// Digits only, with the North American country code dropped from an 11-digit number, so
  /// <c>+1 (919) 555-0142</c>, <c>19195550142</c> and <c>9195550142</c> all become <c>9195550142</c>.
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
  /// <see cref="Normalize"/>d digits, then the <b>local-entry</b> tier — a stored number of exactly seven digits
  /// (no area code) that equals the caller's last seven. The first item in <paramref name="items"/> wins within a
  /// tier, so the caller's order is the tie-break.
  /// </summary>
  /// <remarks>
  /// Owner ruling, 2026-10-03 (<c>PHN-14</c>): a stored 10- or 11-digit number matches only on the full number
  /// (North American <c>1</c> dropped), never on its last seven, so a stranger who shares a contact's last seven
  /// digits in another area code is not announced as that contact. Until then the last-7 tier matched any stored
  /// number ending in the same seven digits.
  /// </remarks>
  /// <returns>The matching item, or <c>default</c> when nothing matches or the number has no digits.</returns>
  public static T? FindMatch<T>(IEnumerable<T>? items, Func<T, string?> numberOf, string? number) =>
    TryFindMatch(items, numberOf, number, out T? item, out _) ? item : default;

  /// <summary>
  /// <see cref="FindMatch{T}"/>, also reporting the tier: <paramref name="isExact"/> is true for a full-number
  /// match and false for the local-entry (last-seven) tier. Callers that rank across sources need it: an exact
  /// match on any source beats a local-entry match on any source (owner ruling, 2026-10-03).
  /// </summary>
  public static bool TryFindMatch<T>(IEnumerable<T>? items, Func<T, string?> numberOf, string? number,
    out T? match, out bool isExact)
  {
    match = default;
    isExact = false;
    string key = Normalize(number ?? "");
    if (items is null || key.Length == 0)
    {
      return false;
    }

    var list = items as IReadOnlyList<T> ?? items.ToList();
    foreach (var item in list)
    {
      if (Normalize(numberOf(item) ?? "") == key)
      {
        match = item;
        isExact = true;
        return true;
      }
    }

    if (key.Length <= 7)
    {
      return false;   // a 7-digit caller equal to a 7-digit entry was already an exact match
    }

    string last7 = GetLast7(key);
    foreach (var item in list)
    {
      // Only a stored LOCAL entry (seven digits, no area code) may match on the caller's last seven.
      if (Normalize(numberOf(item) ?? "") == last7)
      {
        match = item;
        return true;
      }
    }
    return false;
  }
}
