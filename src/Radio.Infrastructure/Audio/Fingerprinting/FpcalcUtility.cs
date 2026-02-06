using AcoustID;
using AcoustID.Chromaprint;
using Microsoft.Extensions.Logging;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Fingerprinting;

/// <summary>
/// Options for fingerprint calculation similar to fpcalc command-line tool.
/// </summary>
public sealed class FpcalcOptions
{
  /// <summary>Maximum duration to process in seconds (default: 120).</summary>
  public double MaxDurationSeconds { get; init; } = 120.0;

  /// <summary>Chunk duration in seconds (0 = no chunking).</summary>
  public double ChunkDurationSeconds { get; init; } = 0.0;

  /// <summary>Overlap chunks slightly to ensure edge audio is fingerprinted.</summary>
  public bool OverlapChunks { get; init; } = false;

  /// <summary>Output raw (uncompressed) fingerprints.</summary>
  public bool RawOutput { get; init; } = false;

  /// <summary>Use signed integers for raw output (for pg_acoustid compatibility).</summary>
  public bool SignedOutput { get; init; } = false;

  /// <summary>Include absolute timestamps for chunked results.</summary>
  public bool IncludeTimestamps { get; init; } = false;

  // Note: Algorithm selection is not supported by AcoustID.NET 1.3.3
  // The library uses a default algorithm internally
}

/// <summary>
/// Result from fingerprint calculation containing fingerprint and metadata.
/// </summary>
public sealed record FpcalcResult
{
  /// <summary>The generated fingerprint (compressed or raw).</summary>
  public required string Fingerprint { get; init; }

  /// <summary>Duration of the audio segment in seconds.</summary>
  public required double DurationSeconds { get; init; }

  /// <summary>Timestamp of when this chunk was processed (only if IncludeTimestamps is true).</summary>
  public double? TimestampSeconds { get; init; }

  /// <summary>Whether this is a raw (uncompressed) fingerprint.</summary>
  public bool IsRaw { get; init; }

  /// <summary>Number of raw fingerprint values (only for raw output).</summary>
  public int? RawSize { get; init; }
}

/// <summary>
/// Utility for generating Chromaprint fingerprints from audio streams,
/// modeled after the fpcalc command-line tool from the chromaprint library.
/// Supports streaming audio, chunking, and various output formats.
/// </summary>
/// <remarks>
/// Based on: https://github.com/acoustid/chromaprint/blob/master/src/cmd/fpcalc.cpp
/// 
/// Key features:
/// - Stream-based fingerprinting for real-time audio
/// - Chunking support for long audio streams
/// - Configurable overlap between chunks
/// - Raw (uncompressed) or compressed fingerprint output
/// - Chromaprint algorithm selection
/// </remarks>
public sealed class FpcalcUtility
{
  private readonly ILogger<FpcalcUtility> _logger;

  public FpcalcUtility(ILogger<FpcalcUtility> logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// Generates fingerprints from audio samples using specified options.
  /// Supports chunking for long audio streams.
  /// </summary>
  /// <param name="samples">The audio sample buffer.</param>
  /// <param name="options">Options for fingerprint generation.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>List of fingerprint results (one per chunk, or single result if not chunked).</returns>
  public Task<List<FpcalcResult>> GenerateFingerprintsAsync(
    AudioSampleBuffer samples,
    FpcalcOptions? options = null,
    CancellationToken ct = default)
  {
    options ??= new FpcalcOptions();

    if (samples.Samples.Length == 0)
    {
      _logger.LogWarning("Cannot generate fingerprint from empty sample buffer");
      return Task.FromResult(new List<FpcalcResult>());
    }

    _logger.LogDebug(
      "Generating fingerprint for {Duration}s of audio (chunk: {Chunk}s, overlap: {Overlap})",
      samples.Duration.TotalSeconds,
      options.ChunkDurationSeconds,
      options.OverlapChunks);

    // Convert float[] samples to short[] PCM for Chromaprint
    short[] pcmSamples = ConvertToPcm(samples.Samples);

    try
    {
      var results = new List<FpcalcResult>();

      if (options.ChunkDurationSeconds > 0)
      {
        // Process in chunks
        results = ProcessChunked(pcmSamples, samples, options);
      }
      else
      {
        // Process entire audio as single fingerprint
        var result = ProcessSingleFingerprint(pcmSamples, samples, options, 0.0);
        if (result != null)
        {
          results.Add(result);
        }
      }

      return Task.FromResult(results);
    }
    catch (NullReferenceException ex)
    {
      _logger.LogError(ex, "AcoustID Native Library Error: NullReferenceException. Check native dependencies.");
      return Task.FromResult(new List<FpcalcResult>());
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error generating fingerprint");
      throw;
    }
  }

  /// <summary>
  /// Generates a single compressed fingerprint from audio samples.
  /// Convenience method for simple use cases.
  /// </summary>
  /// <param name="samples">The audio sample buffer.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>Compressed fingerprint string or empty if failed.</returns>
  public async Task<string> GenerateSimpleFingerprintAsync(
    AudioSampleBuffer samples,
    CancellationToken ct = default)
  {
    var results = await GenerateFingerprintsAsync(samples, new FpcalcOptions(), ct);
    return results.FirstOrDefault()?.Fingerprint ?? string.Empty;
  }

  /// <summary>
  /// Processes audio in chunks, generating a fingerprint for each chunk.
  /// </summary>
  private List<FpcalcResult> ProcessChunked(
    short[] pcmSamples,
    AudioSampleBuffer originalSamples,
    FpcalcOptions options)
  {
    var results = new List<FpcalcResult>();
    
    var sampleRate = originalSamples.SampleRate;
    var channels = originalSamples.Channels;
    
    // Calculate chunk size in samples (per channel)
    var chunkSamplesPerChannel = (int)(options.ChunkDurationSeconds * sampleRate);
    var streamLimit = (int)(options.MaxDurationSeconds * sampleRate);
    
    // Calculate overlap size if enabled
    int extraChunkSamples = 0;
    double overlapSeconds = 0.0;
    
    if (options.OverlapChunks)
    {
      // Calculate overlap - approximately 0.1-0.2 seconds
      // This ensures audio on chunk edges is fingerprinted
      extraChunkSamples = 4096; // Default overlap (~85ms at 48kHz)
      overlapSeconds = extraChunkSamples / (double)sampleRate;
      
      _logger.LogDebug("Using chunk overlap of {Overlap}s ({Samples} samples)", 
        overlapSeconds, extraChunkSamples);
    }
    
    int processedSamplesPerChannel = 0;
    int chunkIndex = 0;
    double timestamp = 0.0;
    bool firstChunk = true;
    
    while (processedSamplesPerChannel < pcmSamples.Length / channels)
    {
      // Apply stream limit
      if (streamLimit > 0 && processedSamplesPerChannel >= streamLimit)
      {
        _logger.LogDebug("Reached stream limit of {Limit}s", options.MaxDurationSeconds);
        break;
      }
      
      // Calculate chunk boundaries
      int chunkStartPerChannel = processedSamplesPerChannel;
      int chunkSizePerChannel = Math.Min(
        chunkSamplesPerChannel + (firstChunk ? extraChunkSamples : 0),
        (pcmSamples.Length / channels) - chunkStartPerChannel);
      
      if (chunkSizePerChannel <= 0)
        break;
      
      // Extract chunk (interleaved samples)
      int chunkStartInterleaved = chunkStartPerChannel * channels;
      int chunkSizeInterleaved = chunkSizePerChannel * channels;
      var chunkSamples = new short[chunkSizeInterleaved];
      Array.Copy(pcmSamples, chunkStartInterleaved, chunkSamples, 0, chunkSizeInterleaved);
      
      // Generate fingerprint for this chunk
      var result = ProcessChunkWithContext(
        chunkSamples, 
        sampleRate, 
        channels, 
        options, 
        timestamp,
        extraChunkSamples,
        overlapSeconds);
      
      if (result != null)
      {
        results.Add(result);
        _logger.LogDebug("Generated fingerprint for chunk {Index} at timestamp {Timestamp}s", 
          chunkIndex, timestamp);
      }
      
      // Update for next chunk
      var actualChunkDuration = (chunkSizePerChannel - extraChunkSamples) / (double)sampleRate;
      timestamp += actualChunkDuration + overlapSeconds;
      
      processedSamplesPerChannel += chunkSamplesPerChannel;
      
      if (firstChunk)
      {
        extraChunkSamples = 0; // Only apply extra to first chunk
        firstChunk = false;
      }
      
      chunkIndex++;
    }
    
    _logger.LogInformation("Generated {Count} fingerprint chunks", results.Count);
    return results;
  }

  /// <summary>
  /// Processes a single audio chunk or complete audio stream.
  /// </summary>
  private FpcalcResult? ProcessSingleFingerprint(
    short[] pcmSamples,
    AudioSampleBuffer originalSamples,
    FpcalcOptions options,
    double timestamp)
  {
    var context = new ChromaContext();
    context.Start(originalSamples.SampleRate, originalSamples.Channels);
    context.Feed(pcmSamples, pcmSamples.Length);
    context.Finish();
    
    return ExtractFingerprint(context, options, timestamp, originalSamples.Duration.TotalSeconds);
  }

  /// <summary>
  /// Processes a chunk with a chromaprint context.
  /// </summary>
  private FpcalcResult? ProcessChunkWithContext(
    short[] chunkSamples,
    int sampleRate,
    int channels,
    FpcalcOptions options,
    double timestamp,
    int extraSamples,
    double overlapSeconds)
  {
    var context = new ChromaContext();
    context.Start(sampleRate, channels);
    context.Feed(chunkSamples, chunkSamples.Length);
    context.Finish();
    
    // Calculate actual duration (excluding overlap)
    var samplesPerChannel = chunkSamples.Length / channels;
    var duration = (samplesPerChannel - extraSamples) / (double)sampleRate + overlapSeconds;
    
    return ExtractFingerprint(context, options, timestamp, duration);
  }

  /// <summary>
  /// Extracts fingerprint from a chromaprint context.
  /// </summary>
  private FpcalcResult? ExtractFingerprint(
    ChromaContext context,
    FpcalcOptions options,
    double timestamp,
    double duration)
  {
    string fingerprint;
    int? rawSize = null;
    
    if (options.RawOutput)
    {
      // Get raw (uncompressed) fingerprint
      var rawFp = context.GetRawFingerprint();
      if (rawFp == null || rawFp.Length == 0)
      {
        _logger.LogWarning("Empty raw fingerprint");
        return null;
      }
      
      rawSize = rawFp.Length;
      
      // Format as comma-separated values
      if (options.SignedOutput)
      {
        // Cast to signed int32 for pg_acoustid compatibility
        fingerprint = string.Join(",", rawFp.Select(x => (int)x));
      }
      else
      {
        fingerprint = string.Join(",", rawFp);
      }
    }
    else
    {
      // Get compressed (encoded) fingerprint
      fingerprint = context.GetFingerprint();
      
      if (string.IsNullOrEmpty(fingerprint))
      {
        _logger.LogWarning("Empty compressed fingerprint");
        return null;
      }
    }
    
    return new FpcalcResult
    {
      Fingerprint = fingerprint,
      DurationSeconds = duration,
      TimestampSeconds = options.IncludeTimestamps ? timestamp : null,
      IsRaw = options.RawOutput,
      RawSize = rawSize
    };
  }

  /// <summary>
  /// Converts float samples to 16-bit PCM samples.
  /// </summary>
  private short[] ConvertToPcm(float[] samples)
  {
    var pcmSamples = new short[samples.Length];
    
    for (int i = 0; i < samples.Length; i++)
    {
      // Clamp and scale to 16-bit range
      float sample = samples[i];
      if (sample > 1.0f) sample = 1.0f;
      if (sample < -1.0f) sample = -1.0f;
      pcmSamples[i] = (short)(sample * 32767);
    }
    
    return pcmSamples;
  }

  /// <summary>
  /// Formats results as JSON output (similar to fpcalc -json).
  /// </summary>
  public string FormatAsJson(List<FpcalcResult> results)
  {
    if (results.Count == 0)
      return "[]";
    
    if (results.Count == 1)
    {
      var r = results[0];
      if (r.IsRaw)
      {
        return $"{{\"duration\": {r.DurationSeconds:F2}, \"fingerprint\": [{r.Fingerprint}]}}";
      }
      else
      {
        return $"{{\"duration\": {r.DurationSeconds:F2}, \"fingerprint\": \"{r.Fingerprint}\"}}";
      }
    }
    
    // Multiple chunks
    var chunks = results.Select(r =>
    {
      var ts = r.TimestampSeconds.HasValue ? $"\"timestamp\": {r.TimestampSeconds.Value:F2}, " : "";
      if (r.IsRaw)
      {
        return $"{{{ts}\"duration\": {r.DurationSeconds:F2}, \"fingerprint\": [{r.Fingerprint}]}}";
      }
      else
      {
        return $"{{{ts}\"duration\": {r.DurationSeconds:F2}, \"fingerprint\": \"{r.Fingerprint}\"}}";
      }
    });
    
    return "[\n  " + string.Join(",\n  ", chunks) + "\n]";
  }

  /// <summary>
  /// Formats results as plain text output (similar to fpcalc -text).
  /// </summary>
  public string FormatAsText(List<FpcalcResult> results)
  {
    var output = new System.Text.StringBuilder();
    
    foreach (var result in results)
    {
      if (output.Length > 0)
        output.AppendLine();
      
      if (result.TimestampSeconds.HasValue)
      {
        output.AppendLine($"TIMESTAMP={result.TimestampSeconds.Value:F2}");
      }
      
      output.AppendLine($"DURATION={result.DurationSeconds:F0}");
      output.AppendLine($"FINGERPRINT={result.Fingerprint}");
    }
    
    return output.ToString();
  }
}
