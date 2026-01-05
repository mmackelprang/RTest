namespace Radio.Core.Models.Audio;

/// <summary>
/// Represents the mode of operation for Spotify audio integration.
/// </summary>
public enum SpotifyMode
{
  /// <summary>
  /// Remote control mode using Spotify Connect API.
  /// Audio does not flow through the application.
  /// Cannot visualize or process audio streams.
  /// Suitable for remote playback control only.
  /// </summary>
  RemoteControl,

  /// <summary>
  /// Loopback audio capture mode.
  /// Captures audio from a Spotify client (raspotify/librespot) via loopback/virtual audio device.
  /// Audio flows through the SoundFlow mixer enabling visualization and processing.
  /// Requires external Spotify client and OS-level loopback configuration.
  /// </summary>
  Loopback,

  /// <summary>
  /// Integrated librespot management mode.
  /// Manages librespot process internally and captures audio via pipe (stdout).
  /// Audio flows through the SoundFlow mixer enabling visualization and processing.
  /// No external audio loopback device required.
  /// Requires librespot executable to be installed and path configured.
  /// </summary>
  Integrated
}
