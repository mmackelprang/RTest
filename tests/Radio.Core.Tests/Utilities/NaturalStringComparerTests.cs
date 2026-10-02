using Radio.Core.Utilities;
using Xunit;

namespace Radio.Core.Tests.Utilities;

/// <summary>
/// UI-32: "Add folder" queues tracks by natural name order, so a folder of "1 - …" … "12 - …" plays in track order.
/// </summary>
public class NaturalStringComparerTests
{
  private static readonly NaturalStringComparer Comparer = NaturalStringComparer.Instance;

  [Fact]
  public void NumbersCompareByValue_NotCharacterByCharacter()
  {
    string[] names = { "10 - Ten.mp3", "2 - Two.mp3", "1 - One.mp3", "11 - Eleven.mp3" };

    var sorted = names.OrderBy(n => n, Comparer).ToArray();

    Assert.Equal(new[] { "1 - One.mp3", "2 - Two.mp3", "10 - Ten.mp3", "11 - Eleven.mp3" }, sorted);
  }

  [Fact]
  public void NumbersInsideNames_CompareByValue()
  {
    string[] folders = { "Disc 10", "Disc 2", "Disc 1" };

    Assert.Equal(new[] { "Disc 1", "Disc 2", "Disc 10" }, folders.OrderBy(n => n, Comparer).ToArray());
  }

  [Fact]
  public void LettersCompareCaseInsensitively()
  {
    Assert.True(Comparer.Compare("apple", "Banana") < 0);
    Assert.True(Comparer.Compare("Banana", "apple") > 0);
  }

  [Fact]
  public void ZeroPaddedAndPlainNumbers_InterleaveByValue()
  {
    string[] names = { "02.mp3", "1.mp3", "010.mp3", "3.mp3" };

    Assert.Equal(new[] { "1.mp3", "02.mp3", "3.mp3", "010.mp3" }, names.OrderBy(n => n, Comparer).ToArray());
  }

  [Fact]
  public void ValueEqualButDifferentlyPadded_StillHasADeterministicOrder()
  {
    Assert.NotEqual(0, Comparer.Compare("a01", "a1"));
    Assert.Equal(-Math.Sign(Comparer.Compare("a01", "a1")), Math.Sign(Comparer.Compare("a1", "a01")));
  }

  [Fact]
  public void CaseOnlyDifference_IsNotATie()
  {
    Assert.NotEqual(0, Comparer.Compare("Song", "song"));
    Assert.Equal(0, Comparer.Compare("Song", "Song"));
  }

  [Fact]
  public void DigitRunsLongerThanAnyIntegerType_DoNotOverflow()
  {
    var big = new string('9', 40);
    var bigger = "1" + new string('0', 40);

    Assert.True(Comparer.Compare(big, bigger) < 0);
  }

  [Fact]
  public void PrefixSortsFirst()
  {
    Assert.True(Comparer.Compare("Track", "Track 2") < 0);
    Assert.True(Comparer.Compare("Track 2", "Track 2b") < 0);
  }

  [Fact]
  public void NullsSortFirst()
  {
    Assert.True(Comparer.Compare(null, "a") < 0);
    Assert.True(Comparer.Compare("a", null) > 0);
    Assert.Equal(0, Comparer.Compare(null, null));
  }
}
