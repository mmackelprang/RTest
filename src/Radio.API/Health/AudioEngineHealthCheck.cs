using Microsoft.Extensions.Diagnostics.HealthChecks;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Health;

/// <summary>
/// Health check that verifies the audio engine is initialized and ready.
/// </summary>
public class AudioEngineHealthCheck : IHealthCheck
{
  private readonly IAudioEngine _audioEngine;

  public AudioEngineHealthCheck(IAudioEngine audioEngine)
  {
    _audioEngine = audioEngine;
  }

  public Task<HealthCheckResult> CheckHealthAsync(
    HealthCheckContext context,
    CancellationToken cancellationToken = default)
  {
    if (!_audioEngine.IsReady)
    {
      return Task.FromResult(HealthCheckResult.Unhealthy(
        "Audio engine is not ready",
        data: new Dictionary<string, object> { ["state"] = _audioEngine.State.ToString() }));
    }

    return Task.FromResult(HealthCheckResult.Healthy("Audio engine is running"));
  }
}
