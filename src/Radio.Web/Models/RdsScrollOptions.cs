namespace Radio.Web.Models;

/// <summary>
/// Strongly-typed binding for the RDS ticker settings under the <c>Radio</c>
/// configuration section. Controls the behaviour of the accumulating RDS
/// RadioText ticker that lives beneath the frequency well in
/// <c>RadioControlPanel</c>.
/// </summary>
/// <remarks>
/// Read by the Razor component via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>
/// so live SQLite-store writes (PR #298 config bridge) take effect without a
/// page reload. The defaults match HANDOFF-rds-accumulating-scroll §5: 256
/// chars rolling, 40 px/s scroll, " • " (space-bullet-space) chunk separator.
/// </remarks>
public class RdsScrollOptions
{
  /// <summary>
  /// Configuration section name. Bind with
  /// <c>builder.Services.Configure&lt;RdsScrollOptions&gt;(builder.Configuration.GetSection(RdsScrollOptions.SectionName))</c>.
  /// </summary>
  /// <remarks>
  /// Bound to "Radio" (not "Radio:Rds" as the spec §5 originally hinted) because
  /// the existing Web → API config save path round-trips a flat <c>RadioConfigDto</c>
  /// with one SQLite key per property (e.g. <c>radio:DefaultFMFrequencyMHz</c>),
  /// not a nested object. Putting the three new RDS keys directly on
  /// RadioConfigDto means they land at <c>radio:RtBufferMaxChars</c> etc., which
  /// this section name picks up via the standard .NET configuration binder. The
  /// alternative — carving out a nested <c>Rds</c> sub-object — would require
  /// server-side ConfigurationController changes that are out of scope for this
  /// PR. The end user observes the same UI; only the on-disk key layout differs
  /// from the §5 hint.
  /// </remarks>
  public const string SectionName = "Radio";

  /// <summary>
  /// Maximum buffer length in characters. Once exceeded, the oldest characters
  /// are dropped from the front of the buffer on whole-char boundaries until
  /// the new total is within the cap. Default 256 ≈ 4 full Group 2A RT
  /// messages, giving ~2 minutes of rolling history on a typical RDS-rich
  /// station while keeping the scroll cycle to a comfortable ~17 s at 40 px/s.
  /// </summary>
  public int RtBufferMaxChars { get; set; } = 256;

  /// <summary>
  /// Marquee scroll speed in pixels per second. Default 40 px/s ≈ broadcast-
  /// caption pace, which keeps the peripheral RT line readable without forcing
  /// the user to actively track it.
  /// </summary>
  public int RtScrollSpeedPxPerSec { get; set; } = 40;

  /// <summary>
  /// String inserted between accumulated RT chunks. Default is the bullet
  /// pattern " • " (space, U+2022, space) — visually clean, mono-friendly,
  /// reads as a clear chunk boundary without being noisy. Validation in the
  /// System Config UI rejects empty, &gt; 8 chars, and control characters
  /// (\n / \r / \t) that would break the single-line marquee.
  /// </summary>
  public string RtChunkSeparator { get; set; } = " • ";
}
