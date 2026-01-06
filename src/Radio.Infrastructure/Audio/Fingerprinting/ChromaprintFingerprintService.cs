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

    public Task<FingerprintData> GenerateFingerprintFromFileAsync(
        string filePath,
        CancellationToken ct = default)
    {
        // TODO: Implement file decoding using MiniAudio
        _logger.LogWarning("GenerateFingerprintFromFileAsync not implemented");
        throw new NotImplementedException("File fingerprinting not yet implemented");
    }
}
