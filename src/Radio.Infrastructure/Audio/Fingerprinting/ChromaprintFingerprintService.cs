using AcoustID;
using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Fingerprinting;

public class ChromaprintFingerprintService : IFingerprintService
{
    private readonly ILogger<ChromaprintFingerprintService> _logger;

    public ChromaprintFingerprintService(ILogger<ChromaprintFingerprintService> logger)
    {
        _logger = logger;
    }

    public Task<FingerprintData> GenerateFingerprintAsync(
        AudioSampleBuffer samples,
        CancellationToken ct = default)
    {
        if (samples.Samples.Length == 0)
        {
            _logger.LogWarning("Cannot generate fingerprint from empty sample buffer");
            return Task.FromResult(new FingerprintData
            {
                Id = Guid.NewGuid().ToString(),
                ChromaprintHash = string.Empty,
                DurationSeconds = 0,
                GeneratedAt = DateTime.UtcNow,
                SourcePath = samples.SourceName
            });
        }

        _logger.LogDebug("Generating fingerprint for {Duration}s of audio", samples.Duration.TotalSeconds);

        // Convert float[] samples to short[] PCM
        short[] pcmSamples = new short[samples.Samples.Length];
        for (int i = 0; i < samples.Samples.Length; i++)
        {
            // Clamp and scale
            float sample = samples.Samples[i];
            if (sample > 1.0f) sample = 1.0f;
            if (sample < -1.0f) sample = -1.0f;
            pcmSamples[i] = (short)(sample * 32767);
        }

        try 
        {
            var context = new ChromaContext();
            context.Start(samples.SampleRate, samples.Channels);
            context.Feed(pcmSamples, pcmSamples.Length);
            context.Finish();

            string fingerprint = context.GetFingerprint();

            return Task.FromResult(new FingerprintData
            {
                    Id = Guid.NewGuid().ToString(),
                    ChromaprintHash = fingerprint,
                    DurationSeconds = (int)samples.Duration.TotalSeconds,
                    GeneratedAt = DateTime.UtcNow,
                    SourcePath = samples.SourceName
            });
        }
        catch (NullReferenceException ex)
        {
            // Catch specific AcoustID NRE
            _logger.LogError(ex, "AcoustID Native Library Error: NullReferenceException during compression. Check native dependencies.");
             return Task.FromResult(new FingerprintData
            {
                Id = Guid.NewGuid().ToString(),
                ChromaprintHash = string.Empty, // Empty hash signals failure
                DurationSeconds = (int)samples.Duration.TotalSeconds,
                GeneratedAt = DateTime.UtcNow,
                SourcePath = samples.SourceName
            });
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Error generating fingerprint");
             throw;
        }
    }

    public Task<FingerprintData> GenerateFingerprintFromFileAsync(
        string filePath,
        CancellationToken ct = default)
    {
        // TODO: Implement file decoding using MiniAudio
        _logger.LogWarning("GenerateFingerprintFromFileAsync not implemented");
        throw new NotImplementedException("File fingerprinting not yet implemented");
    }
}
