namespace Radio.Core.Models.Audio;

/// <summary>
/// Represents a radio frequency value with automatic unit conversion and formatting.
/// Internally stores frequency in Hertz (Hz) to avoid ambiguity.
/// </summary>
public readonly struct Frequency : IEquatable<Frequency>, IComparable<Frequency>
{
  private readonly long _hertz;

  /// <summary>
  /// Initializes a new instance of the <see cref="Frequency"/> struct from Hertz.
  /// </summary>
  /// <param name="hertz">Frequency value in Hertz (Hz).</param>
  public Frequency(long hertz)
  {
    _hertz = hertz;
  }

  /// <summary>
  /// Gets the frequency value in Hertz (Hz).
  /// </summary>
  public long Hertz => _hertz;

  /// <summary>
  /// Gets the frequency value in Kilohertz (kHz).
  /// </summary>
  public double Kilohertz => _hertz / 1_000.0;

  /// <summary>
  /// Gets the frequency value in Megahertz (MHz).
  /// </summary>
  public double Megahertz => _hertz / 1_000_000.0;

  /// <summary>
  /// Creates a Frequency from Kilohertz (kHz).
  /// </summary>
  /// <param name="kilohertz">Frequency value in kHz.</param>
  /// <returns>A Frequency instance.</returns>
  public static Frequency FromKilohertz(double kilohertz) => new((long)(kilohertz * 1_000));

  /// <summary>
  /// Creates a Frequency from Megahertz (MHz).
  /// </summary>
  /// <param name="megahertz">Frequency value in MHz.</param>
  /// <returns>A Frequency instance.</returns>
  public static Frequency FromMegahertz(double megahertz) => new((long)(megahertz * 1_000_000));

  /// <summary>
  /// Formats the frequency as a human-readable string with appropriate units.
  /// </summary>
  /// <returns>Formatted frequency string (e.g., "101.5 MHz", "540 kHz").</returns>
  public string ToDisplayString()
  {
    if (_hertz >= 1_000_000)
    {
      return $"{Megahertz:F1} MHz";
    }
    else if (_hertz >= 1_000)
    {
      return $"{Kilohertz:F0} kHz";
    }
    else
    {
      return $"{_hertz} Hz";
    }
  }

  /// <inheritdoc/>
  public override string ToString() => ToDisplayString();

  /// <inheritdoc/>
  public bool Equals(Frequency other) => _hertz == other._hertz;

  /// <inheritdoc/>
  public override bool Equals(object? obj) => obj is Frequency other && Equals(other);

  /// <inheritdoc/>
  public override int GetHashCode() => _hertz.GetHashCode();

  /// <inheritdoc/>
  public int CompareTo(Frequency other) => _hertz.CompareTo(other._hertz);

  /// <summary>
  /// Determines whether two Frequency values are equal.
  /// </summary>
  public static bool operator ==(Frequency left, Frequency right) => left.Equals(right);

  /// <summary>
  /// Determines whether two Frequency values are not equal.
  /// </summary>
  public static bool operator !=(Frequency left, Frequency right) => !left.Equals(right);

  /// <summary>
  /// Determines whether one Frequency is less than another.
  /// </summary>
  public static bool operator <(Frequency left, Frequency right) => left._hertz < right._hertz;

  /// <summary>
  /// Determines whether one Frequency is greater than another.
  /// </summary>
  public static bool operator >(Frequency left, Frequency right) => left._hertz > right._hertz;

  /// <summary>
  /// Determines whether one Frequency is less than or equal to another.
  /// </summary>
  public static bool operator <=(Frequency left, Frequency right) => left._hertz <= right._hertz;

  /// <summary>
  /// Determines whether one Frequency is greater than or equal to another.
  /// </summary>
  public static bool operator >=(Frequency left, Frequency right) => left._hertz >= right._hertz;

  /// <summary>
  /// Adds two frequencies.
  /// </summary>
  public static Frequency operator +(Frequency left, Frequency right) => new(left._hertz + right._hertz);

  /// <summary>
  /// Subtracts two frequencies.
  /// </summary>
  public static Frequency operator -(Frequency left, Frequency right) => new(left._hertz - right._hertz);
}
