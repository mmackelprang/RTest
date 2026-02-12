using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Fingerprinting;

public class ChromaprintFingerprintServiceTests
{
    private readonly Mock<ILogger<ChromaprintFingerprintService>> _loggerMock;
    private readonly ChromaprintFingerprintService _service;

    public ChromaprintFingerprintServiceTests()
    {
        _loggerMock = new Mock<ILogger<ChromaprintFingerprintService>>();
        var options = Options.Create(new FingerprintingOptions());
        _service = new ChromaprintFingerprintService(_loggerMock.Object, options);
    }

    [SkippableFact]
    public async Task GenerateFingerprintAsync_WithValidSamples_ReturnsFingerprint()
    {
        // Arrange
        // Chromaprint needs enough data to generate a fingerprint.
        // 2 seconds might be minimal but usually okay for test.
        var samples = CreateTestSamples(5.0);

        // Act
        try
        {
            var result = await _service.GenerateFingerprintAsync(samples);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result.ChromaprintHash);
            Assert.True(IsBase64(result.ChromaprintHash), "Hash should be Base64 encoded");
        }
        catch (Exception ex) when (ex is DllNotFoundException
                                    or System.ComponentModel.Win32Exception
                                    or InvalidOperationException)
        {
            // fpcalc binary not available in this environment (CI/CD, missing native lib)
            Skip.If(true, $"fpcalc not available: {ex.Message}");
        }
    }

    private static bool IsBase64(string base64String)
    {
        if (string.IsNullOrEmpty(base64String)) return false;
        // Padding might be missing in some Base64 variants (URL safe), but Chromaprint usually returns standard Base64.
        // Let's just check if it converts.
        try
        {
            // Pad if necessary
            int mod4 = base64String.Length % 4;
            if (mod4 > 0) base64String += new string('=', 4 - mod4);
            
            Convert.FromBase64String(base64String);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AudioSampleBuffer CreateTestSamples(
        double durationSeconds,
        int sampleRate = 44100,
        int channels = 2,
        double frequency = 440.0)
    {
        var sampleCount = (int)(durationSeconds * sampleRate * channels);
        var samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            var t = (double)i / channels / sampleRate;
            samples[i] = (float)Math.Sin(2 * Math.PI * frequency * t);
        }

        return new AudioSampleBuffer
        {
            Samples = samples,
            SampleRate = sampleRate,
            Channels = channels,
            Duration = TimeSpan.FromSeconds(durationSeconds),
            SourceName = "Test Source"
        };
    }
}
