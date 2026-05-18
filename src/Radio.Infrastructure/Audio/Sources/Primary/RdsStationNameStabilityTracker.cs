namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Tracks an RDS Program-Service (PS) signal and emits a "stable" value
/// after <c>N</c> consecutive identical samples. Used by the SDR radio
/// source to filter rolling-PS artifacts before exposing
/// <c>IRadioControl.RdsStationNameStable</c> on the wire.
/// <para>
/// Implementation: a tiny FIFO sized to <c>WindowSize</c>. The stable
/// value is the most recent value that has been seen at least
/// <c>WindowSize</c> times in a row. When the buffer hasn't filled (or
/// the latest <c>WindowSize</c> samples are not all identical) the
/// previously emitted stable value is held until a new consensus
/// emerges.
/// </para>
/// <para>
/// Null / empty / whitespace samples reset the candidate run — a
/// momentary RDS dropout doesn't poison the consensus, but also
/// doesn't promote a transient null to "stable". The previously
/// observed stable value is retained until a real, stable PS appears.
/// PR D #40 of the Arc follow-up backlog.
/// </para>
/// </summary>
public sealed class RdsStationNameStabilityTracker
{
  /// <summary>
  /// Number of consecutive identical PS samples required to promote a
  /// candidate to "stable". Default of 3 is a balance between capturing
  /// fast-changing-but-real PS updates (a quick band change should
  /// promote within a few seconds) and rejecting mid-roll fragments
  /// (typical rolling PS cycles every 1-2 seconds).
  /// </summary>
  public int WindowSize { get; }

  private readonly Queue<string?> _window;
  private string? _stable;
  private readonly object _lock = new();

  public RdsStationNameStabilityTracker(int windowSize = 3)
  {
    if (windowSize < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(windowSize), "WindowSize must be ≥ 1");
    }
    WindowSize = windowSize;
    _window = new Queue<string?>(windowSize);
  }

  /// <summary>
  /// Pushes a new PS sample into the tracker. After enough consecutive
  /// identical samples, <see cref="Stable"/> updates. Returns the
  /// current stable value (after this sample is observed). Null when
  /// no stable value has yet been emitted.
  /// </summary>
  public string? Observe(string? sample)
  {
    lock (_lock)
    {
      // Normalize null/empty/whitespace to null so an RDS-dropped frame
      // doesn't get treated as a distinct value alongside the real PS.
      var normalized = string.IsNullOrWhiteSpace(sample) ? null : sample;

      if (_window.Count == WindowSize)
      {
        _window.Dequeue();
      }
      _window.Enqueue(normalized);

      // Only promote a candidate when the window is full and every entry
      // is identical and non-null. A null-only window doesn't promote a
      // null stable — the previously emitted value is held.
      if (_window.Count == WindowSize)
      {
        string? first = null;
        var firstSet = false;
        var allSame = true;
        foreach (var entry in _window)
        {
          if (!firstSet)
          {
            first = entry;
            firstSet = true;
          }
          else if (!string.Equals(first, entry, StringComparison.Ordinal))
          {
            allSame = false;
            break;
          }
        }

        if (allSame && first != null)
        {
          _stable = first;
        }
      }

      return _stable;
    }
  }

  /// <summary>
  /// Current stable PS value, or <c>null</c> when no consensus has yet
  /// been reached. Safe to read concurrently with <see cref="Observe"/>.
  /// </summary>
  public string? Stable
  {
    get
    {
      lock (_lock)
      {
        return _stable;
      }
    }
  }

  /// <summary>
  /// Clears the stability tracker. Use after a frequency/band change so
  /// the previous station's stable name doesn't leak across tunes.
  /// </summary>
  public void Reset()
  {
    lock (_lock)
    {
      _window.Clear();
      _stable = null;
    }
  }
}
