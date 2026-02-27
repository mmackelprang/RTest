namespace Radio.Infrastructure.Audio.Validation;

/// <summary>
/// Fans out Submit calls to multiple validators (e.g., FrequencyValidator + LevelValidator).
/// </summary>
public sealed class CompositeAudioValidator : IAudioValidator, IDisposable
{
  private readonly IAudioValidator[] _validators;

  public CompositeAudioValidator(params IAudioValidator[] validators)
  {
    _validators = validators;
  }

  /// <inheritdoc/>
  public void Submit(ReadOnlySpan<float> samples, string stageName)
  {
    foreach (var validator in _validators)
    {
      validator.Submit(samples, stageName);
    }
  }

  /// <inheritdoc/>
  public async Task FlushAsync(CancellationToken cancellationToken = default)
  {
    foreach (var validator in _validators)
    {
      await validator.FlushAsync(cancellationToken);
    }
  }

  public void Dispose()
  {
    foreach (var validator in _validators)
    {
      if (validator is IDisposable disposable)
        disposable.Dispose();
    }
  }
}
