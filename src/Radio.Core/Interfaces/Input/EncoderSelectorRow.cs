namespace Radio.Core.Interfaces.Input;

/// <summary>
/// One row of a selector overlay — a source, a radio band, or a saved preset.
///
/// <para>
/// Deliberately flat and presentation-shaped rather than a union of domain types. The overlay
/// renders it without knowing what it stands for, which is what lets ENC-5's source list and
/// ENC-7's preset list share one component instead of two that drift apart. What a commit
/// <i>does</i> is decided on the API side from <see cref="Id"/>; the Web never parses it.
/// </para>
/// </summary>
public sealed class EncoderSelectorRow
{
  /// <summary>
  /// Stable identity for this row, in a <c>kind:value</c> shape — <c>"band:FM"</c>,
  /// <c>"source:Bluetooth"</c>, <c>"preset:0f3c…"</c>. The owning service parses it on commit; it is
  /// also the Blazor <c>@key</c>, so it must not change while the overlay is open.
  /// </summary>
  public required string Id { get; init; }

  /// <summary>Primary line — "FM", "BLUETOOTH", or a preset's name.</summary>
  public required string Primary { get; init; }

  /// <summary>Secondary line — a frequency, a paired device name, or null.</summary>
  public string? Secondary { get; init; }

  /// <summary>
  /// Leading ordinal, zero-padded by the caller ("01"), or null for rows that have no slot.
  /// Source rows never have one; preset rows carry the same per-band slot the on-screen bank shows.
  /// </summary>
  public string? Ordinal { get; init; }

  /// <summary>
  /// Radzen icon name for the row glyph, or null for no glyph. Sourced from
  /// <c>SourceTypeHelper.GetIcon</c>'s vocabulary so the overlay and the topbar strip cannot drift.
  /// </summary>
  public string? Icon { get; init; }

  /// <summary>
  /// CSS custom-property name for this row's accent — e.g. <c>"--source-radio"</c>. Null falls back
  /// to <c>--accent-primary</c> in CSS. Values come from <c>SourceTypeHelper.GetAccentVar</c>; this
  /// row introduces no new colour.
  /// </summary>
  public string? AccentVar { get; init; }

  /// <summary>True for the row that is currently playing. At most one row should carry it.</summary>
  public bool IsCurrent { get; init; }

  /// <summary>
  /// False when committing this row cannot succeed right now. A false value renders the row dimmed
  /// and makes a commit flash it rather than act.
  /// </summary>
  public bool IsAvailable { get; init; } = true;

  /// <summary>
  /// Why the row is unavailable, as a short phrase with no leading separator — "no device paired",
  /// "no tuner detected". The overlay renders it with SourceBubble's " · " idiom. Required whenever
  /// <see cref="IsAvailable"/> is false: handoff §6.6 State B is "dimmed <b>with a reason</b>",
  /// because a dimmed row with no reason is a dead end.
  /// </summary>
  public string? UnavailableReason { get; init; }
}
