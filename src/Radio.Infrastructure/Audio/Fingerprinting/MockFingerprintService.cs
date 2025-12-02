using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Fingerprinting;

/// <summary>
/// Service that generates audio fingerprints using a simple hash-based algorithm.
/// This is a mock implementation that simulates Chromaprint fingerprint generation.
/// For production, integrate with actual Chromaprint.NET library.
/// </summary>
public sealed class MockFingerprintService : IFingerprintService
{
  private readonly ILogger<MockFingerprintService> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="MockFingerprintService"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  public MockFingerprintService(ILogger<MockFingerprintService> logger)
  {
    _logger = logger;
  }

  /// <inheritdoc/>
  public Task<FingerprintData> GenerateFingerprintAsync(
    AudioSampleBuffer samples,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(samples);

    var sw = System.Diagnostics.Stopwatch.StartNew();

    // Generate a simple hash from the audio samples
    // In production, this would use Chromaprint to generate an acoustic fingerprint
    var hash = GenerateSimpleHash(samples.Samples);

    sw.Stop();
    _logger.LogDebug(
      "Generated fingerprint in {ElapsedMs}ms for {Duration}s of audio from {Source}",
      sw.ElapsedMilliseconds,
      samples.Duration.TotalSeconds,
      samples.SourceName ?? "unknown");

    return Task.FromResult(new FingerprintData
    {
      Id = Guid.NewGuid().ToString(),
      ChromaprintHash = hash,
      DurationSeconds = (int)samples.Duration.TotalSeconds,
      GeneratedAt = DateTime.UtcNow,
      SourcePath = samples.SourceName
    });
  }

  /// <inheritdoc/>
  public async Task<FingerprintData> GenerateFingerprintFromFileAsync(
    string filePath,
    CancellationToken ct = default)
  {
    ArgumentException.ThrowIfNullOrEmpty(filePath);

    if (!File.Exists(filePath))
    {
      throw new FileNotFoundException("Audio file not found", filePath);
    }

    _logger.LogDebug("Generating fingerprint from file: {FilePath}", filePath);

    // In production, this would read and decode the audio file
    // For now, generate a mock fingerprint based on file content
    var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
    var hash = Convert.ToBase64String(
      System.Security.Cryptography.SHA256.HashData(fileBytes)[..16]);

    return new FingerprintData
    {
      Id = Guid.NewGuid().ToString(),
      ChromaprintHash = hash,
      DurationSeconds = fileBytes.Length / (44100 * 2 * 2), // Estimate duration
      GeneratedAt = DateTime.UtcNow,
      SourcePath = filePath
    };
  }

  private static string GenerateSimpleHash(float[] samples)
  {
    // Simple hash generation from audio samples
    // Takes samples at regular intervals and creates a hash
    // In production, Chromaprint would create an acoustic fingerprint
    const int samplePoints = 128;
    var step = Math.Max(1, samples.Length / samplePoints);
    var hashBytes = new byte[samplePoints];

    for (int i = 0; i < samplePoints && i * step < samples.Length; i++)
    {
      var sample = samples[i * step];
      // Convert float sample (-1 to 1) to byte (0 to 255), with clamping for safety
      hashBytes[i] = (byte)Math.Clamp((sample + 1f) * 127.5f, 0f, 255f);
    }

    return Convert.ToBase64String(
      System.Security.Cryptography.SHA256.HashData(hashBytes));
  }
}
