namespace Radio.Core.Models.Audio;

/// <summary>
/// Repeat mode options for audio playback.
/// </summary>
public enum RepeatMode
{
  /// <summary>No repeat - stop after playlist ends.</summary>
  Off,

  /// <summary>Repeat the current track continuously.</summary>
  One,

  /// <summary>Repeat the entire playlist continuously.</summary>
  All
}
