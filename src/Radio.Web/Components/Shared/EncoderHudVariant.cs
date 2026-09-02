namespace Radio.Web.Components.Shared;

/// <summary>Which host the HUD is rendering into.</summary>
public enum EncoderHudVariant
{
  /// <summary>
  /// MainLayout, on every normal route. Quartered geometry: the card sits above the knob that
  /// produced it, bottom-anchored over the 1920 px viewport.
  /// </summary>
  Normal,

  /// <summary>
  /// Inside <c>Sleep.razor</c>'s anti-burn-in drift wrapper. Centered rather than quartered, and
  /// stripped to one emissive colour.
  ///
  /// <para>
  /// Centering is not a simplification. The drift wrapper is what stops a static composition
  /// burning into the panel over an overnight park, and a quartered card would have to sit outside
  /// it to reach its quarter — which is exactly the fixed-position bright element that wrapper
  /// exists to prevent.
  /// </para>
  /// </summary>
  Sleep,
}
