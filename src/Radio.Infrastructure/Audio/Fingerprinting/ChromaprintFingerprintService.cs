using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Fingerprinting;

/// <summary>
/// Fingerprint service that uses the native fpcalc binary (Chromaprint CLI)
/// to generate audio fingerprints compatible with the AcoustID database.
/// </summary>
/// <remarks>
/// The previous AcoustID.NET pure C# implementation produced fingerprints
/// incompatible with the AcoustID database. This implementation shells out
/// to the official fpcalc binary which uses the native Chromaprint library.
/// </remarks>
public class ChromaprintFingerprintService : IFingerprintService
{
  private readonly ILogger<ChromaprintFingerprintService> _logger;
  private readonly string _fpcalcPath;

  public ChromaprintFingerprintService(
    ILogger<ChromaprintFingerprintService> logger,
    IOptions<FingerprintingOptions> options)
  {
    _logger = logger;
    _fpcalcPath = ResolveFpcalcPath(options.Value.FpcalcPath);
    _logger.LogInformation("Using fpcalc at: {FpcalcPath}", _fpcalcPath);
  }

  public async Task<FingerprintData> GenerateFingerprintAsync(
    AudioSampleBuffer samples,
    CancellationToken ct = default)
  {
    if (samples.Samples.Length == 0)
    {
      _logger.LogWarning("Cannot generate fingerprint from empty sample buffer");
      return new FingerprintData
      {
        Id = Guid.NewGuid().ToString(),
        ChromaprintHash = string.Empty,
        DurationSeconds = 0,
        GeneratedAt = DateTime.UtcNow,
        SourcePath = samples.SourceName
      };
    }

    _logger.LogDebug(
      "Generating fingerprint for {Duration}s of audio ({SampleRate}Hz, {Channels}ch)",
      samples.Duration.TotalSeconds, samples.SampleRate, samples.Channels);

    // Write PCM samples to a WAV file so fpcalc can decode it reliably.
    // Raw PCM with -format/-rate/-channels flags is unreliable across fpcalc versions.
    var tempFile = Path.Combine(Path.GetTempPath(), $"fpcalc_{Guid.NewGuid():N}.wav");
    try
    {
      // Normalize audio to peak amplitude before fingerprinting.
      // The audio tap captures post-mixer samples (after volume control), so at low
      // system volumes the captured audio can be very quiet (-30dB or worse). Chromaprint
      // needs reasonable signal levels to extract meaningful features — without normalization,
      // quiet audio produces fingerprints too short (e.g., 75 chars vs 800+) to match.
      var normalizedSamples = NormalizeSamples(samples.Samples);

      // Convert float samples to s16le bytes
      var pcmDataLength = normalizedSamples.Length * 2;
      var byteBuffer = new byte[pcmDataLength];
      for (int i = 0; i < normalizedSamples.Length; i++)
      {
        float sample = Math.Clamp(normalizedSamples[i], -1.0f, 1.0f);
        short pcm = (short)(sample * 32767);
        byteBuffer[i * 2] = (byte)(pcm & 0xFF);
        byteBuffer[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
      }

      await WriteWavFileAsync(tempFile, byteBuffer, samples.SampleRate, samples.Channels, ct);

      // Call fpcalc on the WAV file — same as GenerateFingerprintFromFileAsync
      var result = await RunFpcalcAsync($"-json \"{tempFile}\"", ct);

      if (result == null)
      {
        return new FingerprintData
        {
          Id = Guid.NewGuid().ToString(),
          ChromaprintHash = string.Empty,
          DurationSeconds = (int)samples.Duration.TotalSeconds,
          GeneratedAt = DateTime.UtcNow,
          SourcePath = samples.SourceName
        };
      }

      return new FingerprintData
      {
        Id = Guid.NewGuid().ToString(),
        ChromaprintHash = result.Fingerprint,
        DurationSeconds = (int)result.Duration,
        GeneratedAt = DateTime.UtcNow,
        SourcePath = samples.SourceName
      };
    }
    finally
    {
      try { File.Delete(tempFile); } catch { /* best effort cleanup */ }
    }
  }

  public async Task<FingerprintData> GenerateFingerprintFromFileAsync(
    string filePath,
    CancellationToken ct = default)
  {
    ArgumentException.ThrowIfNullOrEmpty(filePath);

    if (!File.Exists(filePath))
      throw new FileNotFoundException($"Audio file not found: {filePath}", filePath);

    _logger.LogDebug("Generating fingerprint from file: {FilePath}", filePath);

    var result = await RunFpcalcAsync($"-json \"{filePath}\"", ct);

    if (result == null)
    {
      return new FingerprintData
      {
        Id = Guid.NewGuid().ToString(),
        ChromaprintHash = string.Empty,
        DurationSeconds = 0,
        GeneratedAt = DateTime.UtcNow,
        SourcePath = filePath
      };
    }

    return new FingerprintData
    {
      Id = Guid.NewGuid().ToString(),
      ChromaprintHash = result.Fingerprint,
      DurationSeconds = (int)result.Duration,
      GeneratedAt = DateTime.UtcNow,
      SourcePath = filePath
    };
  }

  /// <summary>
  /// Writes a standard 44-byte RIFF WAV header followed by s16le PCM data.
  /// This ensures fpcalc/FFmpeg decodes the audio correctly regardless of version.
  /// </summary>
  private static async Task WriteWavFileAsync(
    string path, byte[] pcmData, int sampleRate, int channels, CancellationToken ct)
  {
    const int bitsPerSample = 16;
    int byteRate = sampleRate * channels * (bitsPerSample / 8);
    short blockAlign = (short)(channels * (bitsPerSample / 8));
    int dataSize = pcmData.Length;
    int fileSize = 36 + dataSize; // RIFF chunk size = file size - 8

    using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
    var header = new byte[44];

    // RIFF header
    header[0] = (byte)'R'; header[1] = (byte)'I'; header[2] = (byte)'F'; header[3] = (byte)'F';
    BitConverter.TryWriteBytes(header.AsSpan(4), fileSize);
    header[8] = (byte)'W'; header[9] = (byte)'A'; header[10] = (byte)'V'; header[11] = (byte)'E';

    // fmt sub-chunk
    header[12] = (byte)'f'; header[13] = (byte)'m'; header[14] = (byte)'t'; header[15] = (byte)' ';
    BitConverter.TryWriteBytes(header.AsSpan(16), 16); // sub-chunk size
    BitConverter.TryWriteBytes(header.AsSpan(20), (short)1); // PCM format
    BitConverter.TryWriteBytes(header.AsSpan(22), (short)channels);
    BitConverter.TryWriteBytes(header.AsSpan(24), sampleRate);
    BitConverter.TryWriteBytes(header.AsSpan(28), byteRate);
    BitConverter.TryWriteBytes(header.AsSpan(32), blockAlign);
    BitConverter.TryWriteBytes(header.AsSpan(34), (short)bitsPerSample);

    // data sub-chunk
    header[36] = (byte)'d'; header[37] = (byte)'a'; header[38] = (byte)'t'; header[39] = (byte)'a';
    BitConverter.TryWriteBytes(header.AsSpan(40), dataSize);

    await fs.WriteAsync(header, ct);
    await fs.WriteAsync(pcmData, ct);
  }

  private async Task<FpcalcResult?> RunFpcalcAsync(string arguments, CancellationToken ct)
  {
    try
    {
      var psi = new ProcessStartInfo
      {
        FileName = _fpcalcPath,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      _logger.LogDebug("Running: {FpcalcPath} {Arguments}", _fpcalcPath, arguments);

      using var process = Process.Start(psi);
      if (process == null)
      {
        _logger.LogError("Failed to start fpcalc process");
        return null;
      }

      var stdout = await process.StandardOutput.ReadToEndAsync(ct);
      var stderr = await process.StandardError.ReadToEndAsync(ct);
      await process.WaitForExitAsync(ct);

      if (process.ExitCode != 0)
      {
        _logger.LogError(
          "fpcalc exited with code {ExitCode}: {StdErr}",
          process.ExitCode, stderr);
        return null;
      }

      var result = JsonSerializer.Deserialize<FpcalcResult>(stdout);
      if (result == null || string.IsNullOrEmpty(result.Fingerprint))
      {
        _logger.LogError("fpcalc returned invalid JSON output: {Output}", stdout[..Math.Min(200, stdout.Length)]);
        return null;
      }

      _logger.LogInformation(
        "fpcalc generated fingerprint: duration={Duration}s, hash length={HashLength}",
        result.Duration, result.Fingerprint.Length);

      return result;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogError(ex, "Error running fpcalc");
      return null;
    }
  }

  /// <summary>
  /// Normalizes audio samples to peak amplitude for fingerprinting.
  /// Captured audio may be very quiet due to low system volume (the tap is post-mixer).
  /// Chromaprint needs reasonable signal levels to extract meaningful features.
  /// </summary>
  private float[] NormalizeSamples(float[] samples)
  {
    // Find peak amplitude
    float peak = 0f;
    for (int i = 0; i < samples.Length; i++)
    {
      var abs = Math.Abs(samples[i]);
      if (abs > peak) peak = abs;
    }

    // If already at reasonable level or silent, no normalization needed.
    // Only normalize when audio is significantly quiet (below -6dB / 0.5 peak),
    // which indicates the system volume was low when the tap captured audio.
    if (peak < 0.001f)
    {
      _logger.LogWarning("Audio peak is near zero ({Peak:F6}), normalization skipped", peak);
      return samples;
    }

    if (peak > 0.5f)
    {
      _logger.LogDebug("Audio peak is {Peak:F3} (>0.5), normalization not needed", peak);
      return samples;
    }

    // Normalize to 0.95 peak (leave a little headroom)
    var gain = 0.95f / peak;
    _logger.LogInformation(
      "Normalizing audio for fingerprinting: peak={Peak:F4} ({PeakDb:F1}dB), gain={Gain:F1}x ({GainDb:F1}dB)",
      peak, 20 * Math.Log10(peak), gain, 20 * Math.Log10(gain));

    var normalized = new float[samples.Length];
    for (int i = 0; i < samples.Length; i++)
    {
      normalized[i] = samples[i] * gain;
    }

    return normalized;
  }

  private string ResolveFpcalcPath(string configuredPath)
  {
    // Use configured path if provided
    if (!string.IsNullOrEmpty(configuredPath))
    {
      if (File.Exists(configuredPath))
        return configuredPath;

      _logger.LogWarning(
        "Configured fpcalc path not found: {Path}, searching alternatives",
        configuredPath);
    }

    // Check if fpcalc is in PATH
    var fpcalcName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
      ? "fpcalc.exe" : "fpcalc";

    var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
    foreach (var dir in pathDirs)
    {
      var candidate = Path.Combine(dir, fpcalcName);
      if (File.Exists(candidate))
        return candidate;
    }

    // Check common installation locations
    string[] searchPaths;
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      string baseDir = AppDomain.CurrentDomain.BaseDirectory;
      // Search recursively up a few levels to find tools dir in typical manufacturing/dev layout
      searchPaths =
      [
        Path.Combine(baseDir, fpcalcName),
        Path.Combine(baseDir, "tools", fpcalcName),
        // Normalize paths to resolve relative segments
        Path.GetFullPath(Path.Combine(baseDir, "../../../../tools/fpcalc", fpcalcName)),
        Path.GetFullPath(Path.Combine(baseDir, "../../../../../tools/fpcalc", fpcalcName)),
      ];
    }
    else
    {
      string baseDir = AppDomain.CurrentDomain.BaseDirectory;
      searchPaths =
      [
        Path.Combine(baseDir, fpcalcName),
        "/usr/bin/fpcalc",
        "/usr/local/bin/fpcalc",
        // Look up directory tree for linux dev environments
        Path.GetFullPath(Path.Combine(baseDir, "../../../../tools/fpcalc", fpcalcName)),
      ];
    }

    foreach (var path in searchPaths)
    {
      if (File.Exists(path))
      {
        return path;
      }
    }

    _logger.LogWarning(
      "fpcalc not found. Install via 'apt install libchromaprint-tools' (Linux) " +
      "or download from https://acoustid.org/chromaprint (Windows). " +
      "Set Fingerprinting:FpcalcPath in config to specify location.");

    // Return the name and hope it's resolvable at runtime
    return fpcalcName;
  }

  private sealed class FpcalcResult
  {
    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;
  }
}
