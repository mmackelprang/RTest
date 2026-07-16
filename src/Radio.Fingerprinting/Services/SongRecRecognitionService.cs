using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Fingerprinting.Services;

/// <summary>
/// Audio recognition service using SongRec (open-source Shazam client).
/// Writes captured audio to a temp WAV file, invokes `songrec recognize --json`,
/// and parses the Shazam response to extract track metadata.
/// </summary>
/// <remarks>
/// SongRec internally downsamples to 16kHz and recognizes from the central 12s
/// of the provided audio. It uses Shazam's algorithm which handles noisy/degraded
/// audio (vinyl, radio) much better than Chromaprint/AcoustID.
/// </remarks>
public class SongRecRecognitionService : ISongRecRecognitionService
{
  private readonly ILogger<SongRecRecognitionService> _logger;
  private readonly string _songRecPath;
  private readonly int _timeoutSeconds;

  public SongRecRecognitionService(
    ILogger<SongRecRecognitionService> logger,
    IOptions<FingerprintingOptions> options)
  {
    _logger = logger;
    var songRecOptions = options.Value.SongRec;
    _timeoutSeconds = songRecOptions.TimeoutSeconds;

    if (!songRecOptions.Enabled)
    {
      _logger.LogInformation("SongRec fallback recognition is disabled");
      _songRecPath = "songrec";
      IsAvailable = false;
      return;
    }

    _songRecPath = ResolveSongRecPath(songRecOptions.SongRecPath);

    if (IsAvailable)
    {
      _logger.LogInformation("SongRec available at: {SongRecPath}", _songRecPath);
    }
    else
    {
      _logger.LogWarning("SongRec binary not found. Install via: sudo add-apt-repository ppa:marin-m/songrec && sudo apt install songrec");
    }
  }

  /// <inheritdoc/>
  public bool IsAvailable { get; private set; }

  /// <inheritdoc/>
  public async Task<TrackMetadata?> RecognizeAsync(
    AudioSampleBuffer samples,
    CancellationToken ct = default)
  {
    if (!IsAvailable)
    {
      _logger.LogDebug("SongRec not available, skipping recognition");
      return null;
    }

    if (samples.Samples.Length == 0)
    {
      _logger.LogWarning("Cannot recognize empty audio buffer");
      return null;
    }

    _logger.LogDebug(
      "SongRec recognition: {Duration}s audio ({SampleRate}Hz, {Channels}ch)",
      samples.Duration.TotalSeconds, samples.SampleRate, samples.Channels);

    var tempFile = Path.Combine(Path.GetTempPath(), $"songrec_{Guid.NewGuid():N}.wav");
    try
    {
      // Write PCM samples as WAV file for songrec to process
      await WriteWavFileAsync(tempFile, samples, ct);

      // Run songrec recognize
      var result = await RunSongRecAsync(tempFile, ct);
      if (result == null)
      {
        return null;
      }

      // Parse track metadata from Shazam response
      return ParseResult(result);
    }
    finally
    {
      try { File.Delete(tempFile); } catch { /* best effort cleanup */ }
    }
  }

  /// <summary>
  /// Starts the songrec process from the given start info. Overridable so tests
  /// can substitute a long-running stand-in process to exercise timeout/kill
  /// behavior without depending on a real songrec binary.
  /// </summary>
  internal virtual Process? StartProcess(ProcessStartInfo startInfo) => Process.Start(startInfo);

  /// <summary>
  /// Runs the songrec recognize command and returns the parsed JSON result.
  /// </summary>
  internal async Task<SongRecResult?> RunSongRecAsync(string wavFilePath, CancellationToken ct)
  {
    Process? process = null;
    try
    {
      var psi = new ProcessStartInfo
      {
        FileName = _songRecPath,
        Arguments = $"recognize --json \"{wavFilePath}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      _logger.LogDebug("Running: {SongRecPath} {Arguments}", _songRecPath, psi.Arguments);

      process = StartProcess(psi);
      if (process == null)
      {
        _logger.LogError("Failed to start songrec process");
        return null;
      }

      using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

      // Drain stdout and stderr concurrently to avoid a pipe-buffer deadlock: if
      // songrec fills one pipe (e.g. stderr) while we block reading the other
      // (stdout), neither side can progress and we'd hang until the timeout.
      var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
      var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
      await Task.WhenAll(stdoutTask, stderrTask);
      await process.WaitForExitAsync(timeoutCts.Token);

      var stdout = await stdoutTask;
      var stderr = await stderrTask;

      if (process.ExitCode != 0)
      {
        _logger.LogWarning(
          "songrec exited with code {ExitCode}: {StdErr}",
          process.ExitCode, stderr.Length > 200 ? stderr[..200] : stderr);
        return null;
      }

      if (string.IsNullOrWhiteSpace(stdout))
      {
        _logger.LogInformation("SongRec returned empty output (no match)");
        return null;
      }

      _logger.LogDebug("SongRec raw output ({Length} chars): {Output}",
        stdout.Length, stdout.Length > 300 ? stdout[..300] + "..." : stdout);

      var result = JsonSerializer.Deserialize<SongRecResult>(stdout);
      if (result?.Track == null)
      {
        _logger.LogInformation("SongRec returned no track match");
        return null;
      }

      _logger.LogInformation(
        "SongRec recognized: '{Title}' by '{Artist}'",
        result.Track.Title, result.Track.Subtitle);

      return result;
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      _logger.LogWarning(
        "SongRec process timed out after {Timeout}s; killing it so it is not orphaned",
        _timeoutSeconds);
      return null;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogError(ex, "Error running songrec");
      return null;
    }
    finally
    {
      // Guarantee the OS process (and any children songrec spawned) is terminated
      // on every exit path — timeout, caller cancellation, exception, or normal
      // return. Disposing the Process only releases the managed handle; it does NOT
      // kill the underlying process. Skipping this is what leaked ~34 orphaned
      // songrec processes (~650 MB) over 12 days of uptime in production.
      KillProcessTree(process);
    }
  }

  /// <summary>
  /// Best-effort termination of the songrec process and its child process tree.
  /// Safe to call with a null process or one that has already exited.
  /// </summary>
  private void KillProcessTree(Process? process)
  {
    if (process == null)
    {
      return;
    }

    try
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
      }
    }
    catch (Exception ex)
    {
      // The process may have exited between the HasExited check and the kill (benign
      // race), or we may lack permission to signal it. Log at warning: a songrec
      // process we failed to reap is a potential leak, and lack of visibility into
      // exactly this failure is what let the original leak run unnoticed for 12 days.
      _logger.LogWarning(ex, "Failed to kill songrec process during cleanup");
    }
    finally
    {
      process.Dispose();
    }
  }

  /// <summary>
  /// Converts SongRec/Shazam result to TrackMetadata.
  /// </summary>
  internal static TrackMetadata? ParseResult(SongRecResult result)
  {
    if (result.Track == null)
    {
      return null;
    }

    var track = result.Track;

    // Extract album and genre from sections metadata
    string? album = null;
    string? genre = null;
    int? releaseYear = null;
    if (track.Sections != null)
    {
      foreach (var section in track.Sections)
      {
        if (section.Metadata == null)
        {
          continue;
        }
        foreach (var meta in section.Metadata)
        {
          if (string.Equals(meta.Title, "Album", StringComparison.OrdinalIgnoreCase))
          {
            album = meta.Text;
          }
          else if (string.Equals(meta.Title, "Released", StringComparison.OrdinalIgnoreCase))
          {
            if (int.TryParse(meta.Text, out var year))
            {
              releaseYear = year;
            }
          }
          else if (string.Equals(meta.Title, "Genre", StringComparison.OrdinalIgnoreCase))
          {
            genre = meta.Text;
          }
        }
      }
    }

    // Genre: prefer sections metadata, fall back to track.genres.primary
    if (string.IsNullOrEmpty(genre))
    {
      genre = track.Genres?.Primary;
    }

    // Cover art: prefer high-res, fall back to standard
    string? coverArtUrl = track.Images?.CoverArtHq
      ?? track.Images?.CoverArt;

    return new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = track.Title ?? "Unknown Title",
      Artist = track.Subtitle ?? "Unknown Artist",
      Album = album,
      Genre = genre,
      ReleaseYear = releaseYear,
      CoverArtUrl = coverArtUrl,
      Source = MetadataSource.Shazam,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }

  /// <summary>
  /// Writes audio samples as a standard WAV file for songrec to process.
  /// </summary>
  private static async Task WriteWavFileAsync(
    string path, AudioSampleBuffer samples, CancellationToken ct)
  {
    const int bitsPerSample = 16;
    int byteRate = samples.SampleRate * samples.Channels * (bitsPerSample / 8);
    short blockAlign = (short)(samples.Channels * (bitsPerSample / 8));

    // Convert float samples to s16le bytes
    var pcmDataLength = samples.Samples.Length * 2;
    var byteBuffer = new byte[pcmDataLength];
    for (int i = 0; i < samples.Samples.Length; i++)
    {
      float sample = Math.Clamp(samples.Samples[i], -1.0f, 1.0f);
      short pcm = (short)(sample * 32767);
      byteBuffer[i * 2] = (byte)(pcm & 0xFF);
      byteBuffer[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
    }

    int dataSize = byteBuffer.Length;
    int fileSize = 36 + dataSize;

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
    BitConverter.TryWriteBytes(header.AsSpan(22), (short)samples.Channels);
    BitConverter.TryWriteBytes(header.AsSpan(24), samples.SampleRate);
    BitConverter.TryWriteBytes(header.AsSpan(28), byteRate);
    BitConverter.TryWriteBytes(header.AsSpan(32), blockAlign);
    BitConverter.TryWriteBytes(header.AsSpan(34), (short)bitsPerSample);

    // data sub-chunk
    header[36] = (byte)'d'; header[37] = (byte)'a'; header[38] = (byte)'t'; header[39] = (byte)'a';
    BitConverter.TryWriteBytes(header.AsSpan(40), dataSize);

    await fs.WriteAsync(header, ct);
    await fs.WriteAsync(byteBuffer, ct);
  }

  private string ResolveSongRecPath(string configuredPath)
  {
    // Use configured path if provided
    if (!string.IsNullOrEmpty(configuredPath))
    {
      if (File.Exists(configuredPath))
      {
        IsAvailable = true;
        return configuredPath;
      }

      _logger.LogWarning(
        "Configured songrec path not found: {Path}, searching alternatives",
        configuredPath);
    }

    // Check if songrec is in PATH
    var binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
      ? "songrec.exe" : "songrec";

    var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
    foreach (var dir in pathDirs)
    {
      var candidate = Path.Combine(dir, binaryName);
      if (File.Exists(candidate))
      {
        IsAvailable = true;
        return candidate;
      }
    }

    // Check common installation locations (Linux)
    string[] searchPaths = ["/usr/bin/songrec", "/usr/local/bin/songrec", "/snap/bin/songrec"];
    foreach (var path in searchPaths)
    {
      if (File.Exists(path))
      {
        IsAvailable = true;
        return path;
      }
    }

    IsAvailable = false;
    return binaryName;
  }

  // --- SongRec/Shazam JSON response DTOs ---

  internal sealed class SongRecResult
  {
    [JsonPropertyName("track")]
    public SongRecTrack? Track { get; set; }

    [JsonPropertyName("matches")]
    public List<SongRecMatch>? Matches { get; set; }
  }

  internal sealed class SongRecTrack
  {
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("images")]
    public SongRecImages? Images { get; set; }

    [JsonPropertyName("sections")]
    public List<SongRecSection>? Sections { get; set; }

    [JsonPropertyName("genres")]
    public SongRecGenres? Genres { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }
  }

  internal sealed class SongRecImages
  {
    [JsonPropertyName("coverart")]
    public string? CoverArt { get; set; }

    [JsonPropertyName("coverarthq")]
    public string? CoverArtHq { get; set; }
  }

  internal sealed class SongRecSection
  {
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("metadata")]
    public List<SongRecMetadataItem>? Metadata { get; set; }
  }

  internal sealed class SongRecMetadataItem
  {
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
  }

  internal sealed class SongRecGenres
  {
    [JsonPropertyName("primary")]
    public string? Primary { get; set; }
  }

  internal sealed class SongRecMatch
  {
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("offset")]
    public double Offset { get; set; }
  }
}
