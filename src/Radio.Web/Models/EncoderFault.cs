namespace Radio.Web.Models;

/// <summary>What the owner needs told about the knobs, in order of severity (ENC-12).</summary>
/// <remarks>
/// The declaration order is load-bearing, not cosmetic: <c>EncoderFaultAnnouncer</c> compares these
/// with <c>&lt;=</c> to decide whether an incoming state is an escalation. Reordering the members
/// would silently invert the anti-storm rule.
/// </remarks>
public enum EncoderFaultLevel
{
  /// <summary>Nothing to say. Includes Configured and Transient — see <see cref="EncoderFaultRules"/>.</summary>
  None = 0,

  /// <summary>The knobs work but may feel wrong, or are not plugged in. Amber.</summary>
  Warning = 1,

  /// <summary>A safety field did not apply and the host has tightened the volume clamp. Red.</summary>
  Critical = 2,
}

/// <summary>
/// Every decision the encoder fault badge and its notification make, as pure functions (ENC-12).
///
/// <para>
/// <b>Why this is a separate class rather than logic in <c>MainLayout.razor</c>.</b>
/// <c>MainLayoutTests</c> is a documented stub that renders nothing — Radzen plus JSInterop make the
/// layout impractical to render in bUnit — so nothing under <c>tests/</c> asserts
/// <c>.topbar-mute-chip</c> or <c>.phone-nav-fault</c> in markup. Logic written inline in that file
/// ships with no automated coverage at all. <c>BellHealthRules</c> solved this first and this
/// follows it: the rules are unit-tested here, the markup is covered by the browser Test Plan.
/// </para>
/// </summary>
public static class EncoderFaultRules
{
  /// <summary>
  /// How severe the current hardware state is.
  ///
  /// <para>
  /// ⚠ <c>Transient</c> is deliberately <see cref="EncoderFaultLevel.None"/>. Encoder handoff §7.6:
  /// a USB peripheral missing a report on the first try is ordinary, and reporting it would train the
  /// owner to ignore the badge that matters. Attempts 1-3 are silent by design.
  /// </para>
  ///
  /// <para>
  /// ⚠ An <b>unrecognised</b> status is also <see cref="EncoderFaultLevel.None"/>. A newer API build
  /// sending a tier this kiosk does not know must degrade to silence, not to a badge nobody can
  /// interpret.
  /// </para>
  /// </summary>
  /// <param name="status">Serialized <c>RotaryEncoderConfigStatus</c> name, or null if never observed.</param>
  /// <param name="isConnected">Whether the device is currently present. Null when never observed.</param>
  /// <param name="encoderEnabled">
  /// False when <c>RotaryEncoder:Enabled</c> is off. The owner switched the knobs off deliberately and
  /// must not be nagged about the consequence (encoder handoff §7.3), so this suppresses everything.
  ///
  /// <para>
  /// Today nothing passes <c>false</c>, and it does not need to:
  /// <c>RotaryEncoderHostedService.ExecuteAsync</c> returns immediately when
  /// <c>RotaryEncoder:Enabled</c> is false, so the reader never starts, nothing is ever broadcast,
  /// and every input here is null — which this class already scores as
  /// <see cref="EncoderFaultLevel.None"/>. The parameter states the rule where it is testable, so a
  /// future producer that does broadcast while disabled cannot reintroduce the nag.
  /// </para>
  /// </param>
  public static EncoderFaultLevel Level(string? status, bool? isConnected, bool encoderEnabled = true)
  {
    if (!encoderEnabled)
    {
      return EncoderFaultLevel.None;
    }

    // Absence outranks a stale configuration tier: a device that is not there cannot have its
    // configuration fixed, and "not connected" is the actionable fact.
    if (isConnected == false)
    {
      return EncoderFaultLevel.Warning;
    }

    return status switch
    {
      "HardFault" => EncoderFaultLevel.Critical,
      "Degraded" => EncoderFaultLevel.Warning,
      _ => EncoderFaultLevel.None,
    };
  }

  /// <summary>Material icon name for the badge. Empty when nothing should be shown.</summary>
  /// <remarks>
  /// Three states get three glyphs rather than one glyph in two colours. Designer separates Degraded
  /// from a hard fault by colour alone, which fails WCAG 1.4.1 and the project's own rule at bell
  /// handoff §8.3 — so the shape carries the distinction too.
  /// </remarks>
  public static string BadgeIcon(string? status, bool? isConnected, bool encoderEnabled = true)
  {
    if (Level(status, isConnected, encoderEnabled) == EncoderFaultLevel.None)
    {
      return "";
    }

    if (isConnected == false)
    {
      return "link_off";
    }

    return status == "HardFault" ? "error" : "warning";
  }

  /// <summary>CSS modifier for the badge colour. Empty when nothing should be shown.</summary>
  public static string BadgeClass(string? status, bool? isConnected, bool encoderEnabled = true) =>
    Level(status, isConnected, encoderEnabled) switch
    {
      EncoderFaultLevel.Critical => "encoder-nav-fault encoder-nav-fault-critical",
      EncoderFaultLevel.Warning => "encoder-nav-fault encoder-nav-fault-warning",
      _ => "",
    };

  /// <summary>
  /// Accessible name for the Settings nav pill, carrying the fault in words.
  ///
  /// <para>
  /// Not colour, not a glyph — text, because §8.3 requires the state to survive for a user who
  /// perceives neither.
  /// </para>
  /// </summary>
  public static string NavPillAriaLabel(string? status, bool? isConnected, bool encoderEnabled = true) =>
    Level(status, isConnected, encoderEnabled) switch
    {
      EncoderFaultLevel.Critical => "Settings — knob safety settings not applied, volume limited",
      EncoderFaultLevel.Warning when isConnected == false => "Settings — knobs not connected",
      EncoderFaultLevel.Warning => "Settings — knob settings not applied",
      _ => "Settings",
    };

  /// <summary>
  /// The notification copy, verbatim from encoder handoff §7.6. Null when nothing should be said.
  ///
  /// <para>
  /// ⚠ These strings are assertions about what the machine did and each one is true of the shipped
  /// behaviour: the Degraded line promises the knobs still work (they do — host clamps stay in force
  /// and acceleration is treated as absent), and the hard-fault line promises volume is limited (it
  /// is — <c>RotaryEncoderConfigVerifier.VolumeClampFor</c> returns 2 instead of 6 units per event
  /// until a push verifies). <b>Do not soften either one</b>; a reviewer should check them against
  /// that method rather than against plausibility.
  /// </para>
  /// </summary>
  public static (string Summary, string Detail)? NotificationCopy(string? status, bool? isConnected)
  {
    if (isConnected == false)
    {
      return ("Knobs disconnected", "Touch controls still work.");
    }

    return status switch
    {
      "HardFault" => ("Knob safety settings couldn't be applied",
                      "Volume is limited until this is fixed."),
      "Degraded" => ("Knob settings couldn't be applied",
                     "The knobs still work, but they may feel wrong."),
      _ => null,
    };
  }
}
