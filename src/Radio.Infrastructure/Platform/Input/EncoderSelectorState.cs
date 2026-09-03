using Radio.Core.Interfaces.Input;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// The preview state of one selector overlay: which rows it holds, which one is highlighted, and
/// whether it is currently open.
///
/// <para>
/// Shared by ENC-5's SOURCE list and ENC-7's PRESETS list. The two knobs are adjacent and behave
/// identically by design — handoff §4.4: "two adjacent selector knobs that behave identically is a
/// feature: learn one, you have learned both" — so the grammar is written once here and the lists
/// differ only in their contents and in what a commit does.
/// </para>
///
/// <para>
/// Not thread-safe on its own; it holds no lock of its own and takes none. Its owner is expected to
/// serialize access. <c>SourceSelectorService</c> does that with a single gate: encoder events
/// arrive on the HID read loop and the idle-dismiss timer is the only other writer, and both go
/// through that gate.
/// </para>
/// </summary>
public sealed class EncoderSelectorState
{
  private IReadOnlyList<EncoderSelectorRow> _rows = [];
  private int _highlight = -1;

  /// <summary>True while the overlay is on screen.</summary>
  public bool IsOpen { get; private set; }

  /// <summary>The rows as last composed. Never null; empty means the instructional empty state.</summary>
  public IReadOnlyList<EncoderSelectorRow> Rows => _rows;

  /// <summary>Index of the highlighted row, or -1 when there are no rows.</summary>
  public int HighlightIndex => _highlight;

  /// <summary>The highlighted row, or null when the list is empty.</summary>
  public EncoderSelectorRow? Highlighted =>
    _highlight >= 0 && _highlight < _rows.Count ? _rows[_highlight] : null;

  /// <summary>
  /// Replaces the rows, keeping the highlight on the same <see cref="EncoderSelectorRow.Id"/> where
  /// possible.
  ///
  /// <para>
  /// Identity rather than position, because the reason to recompose mid-overlay is that a row's
  /// availability changed or (in ENC-7) a preset was added — and moving somebody's highlight because
  /// the list grew underneath them is how a selector loses its place.
  /// </para>
  /// </summary>
  public void SetRows(IReadOnlyList<EncoderSelectorRow> rows)
  {
    string? keep = Highlighted?.Id;
    _rows = rows;

    if (_rows.Count == 0)
    {
      _highlight = -1;
      return;
    }

    int found = keep is null ? -1 : IndexOfId(keep);
    _highlight = found >= 0 ? found : DefaultHighlight();
  }

  /// <summary>
  /// Opens the overlay, seeding the highlight on the current row.
  ///
  /// <para>
  /// Seeding on "current" is what makes handoff §4.4's one-rule press work: with the overlay closed
  /// the highlight is what is already playing, so a press commits the status quo — it changes
  /// nothing and opens the overlay showing you where you are. That is what makes a mis-grab free.
  /// </para>
  /// </summary>
  public void Open()
  {
    if (!IsOpen)
    {
      _highlight = DefaultHighlight();
      IsOpen = true;
    }
  }

  /// <summary>Closes the overlay without committing anything.</summary>
  public void Close() => IsOpen = false;

  /// <summary>
  /// Moves the highlight by <paramref name="delta"/> entries, wrapping.
  ///
  /// <para>
  /// The caller has already applied the ENC-3 per-event clamp of ±1, so one detent is one entry at
  /// every spin speed. Wrapping is host-side: the device is configured <c>wrap = false</c> on both
  /// selector knobs precisely so the host owns it (handoff §5.2).
  /// </para>
  /// </summary>
  public void Move(int delta)
  {
    if (_rows.Count == 0)
    {
      _highlight = -1;
      return;
    }

    int next = (_highlight < 0 ? 0 : _highlight) + delta;
    _highlight = ((next % _rows.Count) + _rows.Count) % _rows.Count;
  }

  private int IndexOfId(string id)
  {
    for (int i = 0; i < _rows.Count; i++)
    {
      if (string.Equals(_rows[i].Id, id, StringComparison.Ordinal))
      {
        return i;
      }
    }

    return -1;
  }

  /// <summary>The current row if there is one, otherwise the first row.</summary>
  private int DefaultHighlight()
  {
    for (int i = 0; i < _rows.Count; i++)
    {
      if (_rows[i].IsCurrent)
      {
        return i;
      }
    }

    return _rows.Count == 0 ? -1 : 0;
  }
}
