using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.API.Extensions;

/// <summary>
/// Extension methods for IAudioEngine to simplify common operations.
/// </summary>
public static class AudioEngineExtensions
{
  /// <summary>
  /// Gets the currently active primary audio source, if any.
  /// </summary>
  /// <param name="audioEngine">The audio engine.</param>
  /// <returns>The active primary audio source, or null if none is active.</returns>
  public static IPrimaryAudioSource? GetActivePrimarySource(this IAudioEngine audioEngine)
  {
    var mixer = audioEngine.GetMasterMixer();
    var activeSources = mixer.GetActiveSources();
    return activeSources.FirstOrDefault(s => s.Category == AudioSourceCategory.Primary) as IPrimaryAudioSource;
  }

  /// <summary>
  /// Gets the currently active radio source (source that implements IRadioControls), if any.
  /// </summary>
  /// <param name="audioEngine">The audio engine.</param>
  /// <returns>The active radio source, or null if none is active.</returns>
  public static IRadioControls? GetActiveRadioSource(this IAudioEngine audioEngine)
  {
    var mixer = audioEngine.GetMasterMixer();
    var activeSources = mixer.GetActiveSources();
    return activeSources.FirstOrDefault(s => 
      s.Category == AudioSourceCategory.Primary && 
      s is IRadioControls) as IRadioControls;
  }

  /// <summary>
  /// Gets the currently active primary IAudioSource (not cast to IPrimaryAudioSource), if any.
  /// </summary>
  /// <param name="audioEngine">The audio engine.</param>
  /// <returns>The active primary audio source, or null if none is active.</returns>
  public static IAudioSource? GetActivePrimaryAudioSource(this IAudioEngine audioEngine)
  {
    var mixer = audioEngine.GetMasterMixer();
    var activeSources = mixer.GetActiveSources();
    return activeSources.FirstOrDefault(s => s.Category == AudioSourceCategory.Primary);
  }
}
