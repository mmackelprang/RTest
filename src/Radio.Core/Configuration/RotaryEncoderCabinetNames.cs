namespace Radio.Core.Configuration;

/// <summary>
/// The engraving on the cabinet face, top to bottom, indexed by encoder index.
///
/// <para>
/// Fixed by owner decision D2 (encoder handoff §9.1) and <b>irreversible</b> — punch list constraint
/// O9 names the escutcheon drilling as the one step in the whole project that cannot be undone. This
/// is a fact about a piece of furniture, not a software mapping.
/// </para>
///
/// <para>
/// ⚠ <b>This is not <c>Radio.Infrastructure</c>'s action mapping and must never be derived from
/// it.</b> Since ENC-5 the router agrees with this order on every index except <b>2</b>, where it
/// dispatches the visualiser under an escutcheon reading PRESETS; ENC-7 closes that. A settings
/// page that showed one of these in place of the other would be asserting something false — which
/// is the whole reason they are separate lists rather than one derived from the other.
/// </para>
/// </summary>
public static class RotaryEncoderCabinetNames
{
  /// <summary>
  /// Top to bottom, as engraved: the knobs are a vertical column to the LEFT of the LCD (ENC-4c,
  /// handoff §9), so index 0 is the topmost knob. Index n is encoder n.
  /// </summary>
  public static readonly IReadOnlyList<string> Ordered = ["VOLUME", "SOURCE", "PRESETS", "TUNING"];

  /// <summary>The engraved name for an encoder index, or <c>KNOB {index}</c> if the index is off the face.</summary>
  public static string For(int encoderIndex) =>
    encoderIndex >= 0 && encoderIndex < Ordered.Count
      ? Ordered[encoderIndex]
      : $"KNOB {encoderIndex}";
}
