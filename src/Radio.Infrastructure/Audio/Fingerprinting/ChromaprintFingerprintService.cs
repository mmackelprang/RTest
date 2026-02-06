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

    // Write PCM samples to a temp file as signed 16-bit little-endian
    var tempFile = Path.Combine(Path.GetTempPath(), $"fpcalc_{Guid.NewGuid():N}.raw");
    try
    {
      // Convert float samples to s16le bytes
      var byteBuffer = new byte[samples.Samples.Length * 2];
      for (int i = 0; i < samples.Samples.Length; i++)
      {
        float sample = Math.Clamp(samples.Samples[i], -1.0f, 1.0f);
        short pcm = (short)(sample * 32767);
        byteBuffer[i * 2] = (byte)(pcm & 0xFF);
        byteBuffer[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
      }

      await File.WriteAllBytesAsync(tempFile, byteBuffer, ct);

      // Call fpcalc with raw PCM format
      var result = await RunFpcalcAsync(
        $"-format s16le -rate {samples.SampleRate} -channels {samples.Channels} -json \"{tempFile}\"",
        ct);

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

      _logger.LogDebug(
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
      searchPaths =
      [
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fpcalcName),
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", fpcalcName),
      ];
    }
    else
    {
      searchPaths =
      [
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fpcalcName),
        "/usr/bin/fpcalc",
        "/usr/local/bin/fpcalc",
      ];
    }

    foreach (var path in searchPaths)
    {
      if (File.Exists(path))
        return path;
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
