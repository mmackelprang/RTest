using SoundFlow.Abstracts;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// A SoundFlow modifier that applies stereo balance (pan) to audio output.
/// Reads balance gain values from the SoundFlowMasterMixer.
/// </summary>
public class BalanceModifier : SoundModifier
{
  private readonly SoundFlowMasterMixer _mixer;

  /// <summary>
  /// Initializes a new instance of the <see cref="BalanceModifier"/> class.
  /// </summary>
  /// <param name="mixer">The master mixer to read balance values from.</param>
  public BalanceModifier(SoundFlowMasterMixer mixer)
  {
    _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));
    Name = "Balance";
  }

  /// <inheritdoc/>
  public override float ProcessSample(float sample, int channel)
  {
    // channel 0 = left, channel 1 = right
    return channel switch
    {
      0 => sample * _mixer.GetLeftChannelGain(),
      1 => sample * _mixer.GetRightChannelGain(),
      _ => sample
    };
  }
}
