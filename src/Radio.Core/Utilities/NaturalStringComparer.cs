namespace Radio.Core.Utilities;

/// <summary>
/// Orders strings the way a person reads them: runs of ASCII digits compare by numeric value, everything
/// else compares case-insensitively (ordinal, culture-free). So <c>"2 - Song"</c> sorts before <c>"10 - Song"</c>
/// and <c>"Disc 2"</c> before <c>"Disc 10"</c>, where a plain ordinal sort puts the 10s first.
/// </summary>
/// <remarks>
/// <para>Digit runs are compared without parsing them into an integer type, so a run of any length is safe:
/// leading zeros are skipped, then the longer run is the larger number, then the digits decide. When two digit runs
/// have the same value but different zero padding (<c>"01"</c> and <c>"1"</c>) they compare equal at that point and
/// the comparison carries on; if the whole strings are otherwise equal, a final case-insensitive then ordinal
/// comparison of the raw strings breaks the tie, so the order is total and stable.</para>
/// <para>Only <c>'0'</c>-<c>'9'</c> count as digits. Other Unicode digits compare as ordinary characters.</para>
/// </remarks>
public sealed class NaturalStringComparer : IComparer<string?>
{
  /// <summary>The shared instance. The comparer holds no state.</summary>
  public static NaturalStringComparer Instance { get; } = new();

  /// <inheritdoc/>
  public int Compare(string? x, string? y)
  {
    if (ReferenceEquals(x, y))
    {
      return 0;
    }
    if (x is null)
    {
      return -1;
    }
    if (y is null)
    {
      return 1;
    }

    int i = 0;
    int j = 0;
    while (i < x.Length && j < y.Length)
    {
      if (IsDigit(x[i]) && IsDigit(y[j]))
      {
        int result = CompareDigitRuns(x, ref i, y, ref j);
        if (result != 0)
        {
          return result;
        }
        continue;
      }

      int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
      if (c != 0)
      {
        return c;
      }
      i++;
      j++;
    }

    // One string is a prefix of the other (after digit-run equivalence): the shorter remainder sorts first.
    int remaining = (x.Length - i).CompareTo(y.Length - j);
    if (remaining != 0)
    {
      return remaining;
    }

    // Equal under the natural rules (e.g. "a01" and "a1", or "A" and "a"): fall back to raw comparisons so the
    // order is deterministic rather than leaving distinct strings tied.
    int ignoreCase = string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    return ignoreCase != 0 ? ignoreCase : string.CompareOrdinal(x, y);
  }

  private static bool IsDigit(char c) => c >= '0' && c <= '9';

  /// <summary>
  /// Compares the digit runs starting at <paramref name="i"/> and <paramref name="j"/> by numeric value and
  /// advances both indexes past their runs.
  /// </summary>
  private static int CompareDigitRuns(string x, ref int i, string y, ref int j)
  {
    // Skip leading zeros, but keep the last digit of an all-zero run so "0" still has a significant digit.
    while (i < x.Length - 1 && x[i] == '0' && IsDigit(x[i + 1]))
    {
      i++;
    }
    while (j < y.Length - 1 && y[j] == '0' && IsDigit(y[j + 1]))
    {
      j++;
    }

    int startX = i;
    int startY = j;
    while (i < x.Length && IsDigit(x[i]))
    {
      i++;
    }
    while (j < y.Length && IsDigit(y[j]))
    {
      j++;
    }

    int lengthX = i - startX;
    int lengthY = j - startY;
    if (lengthX != lengthY)
    {
      return lengthX.CompareTo(lengthY);
    }

    for (int k = 0; k < lengthX; k++)
    {
      int c = x[startX + k].CompareTo(y[startY + k]);
      if (c != 0)
      {
        return c;
      }
    }

    return 0;
  }
}
