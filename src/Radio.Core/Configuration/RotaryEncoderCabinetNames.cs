namespace Radio.Core.Configuration;

/// <summary>
/// The engraving on the cabinet face, left to right, indexed by encoder index.
///
/// <para>
/// Fixed by owner decision D2 (encoder handoff §9.1) and <b>irreversible</b> — punch list constraint
/// O9 names the escutcheon drilling as the one step in the whole project that cannot be undone. This
/// is a fact about a piece of furniture, not a software mapping.
/// </para>
///
/// <para>
/// ⚠ <b>This is not <c>Radio.Infrastructure</c>'s action mapping and must never be derived from
/// it.</b> The router currently dispatches index 1 to tuning and index 2 to source, which does not
/// match this order; that mismatch is deliberate and tracked (ENC-5 / ENC-7 own the remap). A
/// settings page that showed one of these in place of the other would be asserting something false.
/// Index 0 is VOLUME under both, which is why the knob with a safety hazard on it is already right.
/// </para>
/// </summary>
public static class RotaryEncoderCabinetNames
{
  /// <summary>Left to right, as engraved. Index n is encoder n.</summary>
  public static readonly IReadOnlyList<string> Ordered = ["VOLUME", "SOURCE", "PRESETS", "TUNING"];

  /// <summary>The engraved name for an encoder index, or <c>KNOB {index}</c> if the index is off the face.</summary>
  public static string For(int encoderIndex) =>
    encoderIndex >= 0 && encoderIndex < Ordered.Count
      ? Ordered[encoderIndex]
      : $"KNOB {encoderIndex}";
}
