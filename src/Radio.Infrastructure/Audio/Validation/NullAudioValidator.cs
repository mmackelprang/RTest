namespace Radio.Infrastructure.Audio.Validation;

/// <summary>
/// No-op validator used when validation is disabled. Zero overhead — no allocations, no branching.
/// </summary>
public sealed class NullAudioValidator : IAudioValidator
{
  /// <summary>
  /// Singleton instance.
  /// </summary>
  public static readonly NullAudioValidator Instance = new();

  private NullAudioValidator() { }

  /// <inheritdoc/>
  public void Submit(ReadOnlySpan<float> samples, string stageName) { }

  /// <inheritdoc/>
  public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
