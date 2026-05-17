using FluentAssertions;
using Radio.Web.Formatting;

namespace Radio.Web.Tests.Formatting;

/// <summary>
/// Tests <see cref="UnitsFormatter.Format(double, Units)"/>. One boundary case per enum member.
/// </summary>
public class UnitsTests
{
  [Fact]
  public void Format_PercentBelow10_OneDecimal()
  {
    UnitsFormatter.Format(3.5, Units.Percent).Should().Be("3.5%");
  }

  [Fact]
  public void Format_PercentAt10OrAbove_Integer()
  {
    UnitsFormatter.Format(65.0, Units.Percent).Should().Be("65%");
  }

  [Fact]
  public void Format_Megabytes_Integer()
  {
    UnitsFormatter.Format(850.4, Units.Megabytes).Should().Be("850 MB");
  }

  [Fact]
  public void Format_MillisecondsUnder1000_IntegerWithMsSuffix()
  {
    UnitsFormatter.Format(215, Units.Milliseconds).Should().Be("215 ms");
  }

  [Fact]
  public void Format_MillisecondsAt1000OrMore_PromotesToSeconds()
  {
    UnitsFormatter.Format(1200, Units.Milliseconds).Should().Be("1.2 s");
  }

  [Fact]
  public void Format_Count_ThousandsSeparated()
  {
    UnitsFormatter.Format(135725, Units.Count).Should().Be("135,725");
  }

  [Fact]
  public void Format_PerMinute_OneDecimalWithSuffix()
  {
    UnitsFormatter.Format(12.4, Units.PerMinute).Should().Be("12.4/min");
  }

  [Fact]
  public void Format_Frequency_DelegatesToFrequencyFormatter()
  {
    // FrequencyFormatter.FormatStep treats values >= 1_000_000 as MHz, otherwise kHz.
    UnitsFormatter.Format(1_000_000, Units.Frequency).Should().Be("1 MHz");
    UnitsFormatter.Format(500, Units.Frequency).Should().Be("0.5 kHz");
  }

  [Fact]
  public void Format_DecibelsNegative_SignPreserved()
  {
    UnitsFormatter.Format(-3, Units.Decibels).Should().Be("-3 dB");
  }

  [Fact]
  public void Format_DecibelsPositive_NoSign()
  {
    UnitsFormatter.Format(6, Units.Decibels).Should().Be("6 dB");
  }

  [Fact]
  public void Format_Bare_Integer()
  {
    UnitsFormatter.Format(65, Units.Bare).Should().Be("65");
  }
}
