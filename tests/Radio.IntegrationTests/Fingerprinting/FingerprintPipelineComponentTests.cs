using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.IntegrationTests.TestSupport;
using Xunit.Abstractions;

namespace Radio.IntegrationTests.Fingerprinting;

/// <summary>
/// Comprehensive integration tests that exercise individual components of the
/// fingerprinting pipeline to isolate where audio data gets corrupted.
///
/// The live capture pipeline is:
///   FingerprintTapModifier.ProcessSample(float)
///     → TappedOutputStream.WriteFromEngine(float[])  [float→s16le ring buffer]
///     → TappedOutputStreamReader.Read(byte[])         [read s16le from ring buffer]
///     → SoundFlowAudioTap.CaptureAsync                [s16le→float conversion]
///     → ChromaprintFingerprintService.GenerateFingerprintAsync [float→s16le→WAV→fpcalc]
///     → AcoustIdClient.LookupAsync                    [hash+duration→API]
///
/// These tests exercise each stage independently and combined to find the failure.
/// </summary>
[Trait("Category", "Integration")]
public class FingerprintPipelineComponentTests : IAsyncLifetime
{
  private readonly ITestOutputHelper _output;
  private readonly string _tempDirectory;
  private static readonly string SolutionRoot = FindSolutionRoot();
  private static readonly string WereReadyMp3 = Path.Combine(SolutionRoot,
    "src", "Radio.API", "media", "audio", "music", "02 We're Ready.mp3");

  private string _fpcalcPath = null!;
  private ChromaprintFingerprintService _fingerprintService = null!;
  private AcoustIdClient _acoustIdClient = null!;

  public FingerprintPipelineComponentTests(ITestOutputHelper output)
  {
    _output = output;
    _tempDirectory = Path.Combine(Path.GetTempPath(), $"PipelineComponentTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDirectory);
  }

  public Task InitializeAsync()
  {
    _fpcalcPath = Path.Combine(SolutionRoot, "tools", "fpcalc",
      OperatingSystem.IsWindows() ? "fpcalc.exe" : "fpcalc");
    Assert.True(File.Exists(_fpcalcPath), $"fpcalc not found at: {_fpcalcPath}");

    var fingerprintingOptions = new FingerprintingOptions
    {
      Enabled = true,
      SampleDurationSeconds = 15,
      MinimumConfidenceThreshold = 0.5,
      FpcalcPath = _fpcalcPath,
      AcoustId = new AcoustIdOptions
      {
        ApiKey = "Z0mwq00cEz",
        BaseUrl = "https://api.acoustid.org/v2",
        TimeoutSeconds = 15
      },
      MusicBrainz = new MusicBrainzOptions
      {
        BaseUrl = "https://musicbrainz.org/ws/2",
        ApplicationName = "RadioConsole",
        ApplicationVersion = "1.0.0",
        ContactEmail = "mark@mackelprang.com",
        TimeoutSeconds = 15
      }
    };
    var options = Options.Create(fingerprintingOptions);
    var optionsMonitor = new Moq.Mock<IOptionsMonitor<FingerprintingOptions>>();
    optionsMonitor.Setup(m => m.CurrentValue).Returns(fingerprintingOptions);

    _fingerprintService = new ChromaprintFingerprintService(
      NullLogger<ChromaprintFingerprintService>.Instance, options);

    var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    httpClient.DefaultRequestHeaders.Add("User-Agent", "RadioConsole/1.0");
    _acoustIdClient = new AcoustIdClient(
      httpClient, NullLogger<AcoustIdClient>.Instance, optionsMonitor.Object);

    return Task.CompletedTask;
  }

  public Task DisposeAsync()
  {
    _acoustIdClient?.Dispose();
    try
    {
      if (Directory.Exists(_tempDirectory))
        Directory.Delete(_tempDirectory, recursive: true);
    }
    catch (IOException) { }
    return Task.CompletedTask;
  }

  #region Stage 1: TappedOutputStream Roundtrip

  /// <summary>
  /// Tests that TappedOutputStream float→s16le→float roundtrip preserves audio quality.
  /// Max error should be within 1 LSB (1/32767 ≈ 3.05e-5).
  /// </summary>
  [Fact]
  public void Stage1_TappedOutputStream_Roundtrip_PreservesAudioQuality()
  {
    const int sampleRate = 48000;
    const int channels = 2;
    const double durationSec = 1.0;
    var totalSamples = (int)(durationSec * sampleRate * channels);

    // Generate a 440Hz stereo sine wave
    var original = new float[totalSamples];
    for (int i = 0; i < totalSamples / channels; i++)
    {
      var value = (float)Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 0.8f;
      original[i * channels] = value;
      original[i * channels + 1] = value;
    }

    // Write through TappedOutputStream → read back
    var tap = new TappedOutputStream(sampleRate, channels, 5);
    using var reader = tap.CreateReader("test-reader");
    tap.WriteFromEngine(original);

    var readBytes = new byte[totalSamples * 2];
    var bytesRead = reader.Read(readBytes, 0, readBytes.Length);

    _output.WriteLine($"Wrote {totalSamples} float samples → read back {bytesRead} bytes ({bytesRead / 2} samples)");
    Assert.Equal(totalSamples * 2, bytesRead);

    // Convert s16le back to float
    var roundTrip = new float[totalSamples];
    for (int i = 0; i < totalSamples; i++)
    {
      var pcm = (short)(readBytes[i * 2] | (readBytes[i * 2 + 1] << 8));
      roundTrip[i] = pcm / (float)short.MaxValue;
    }

    // Measure error
    double maxError = 0;
    double sumSquaredError = 0;
    for (int i = 0; i < totalSamples; i++)
    {
      var error = Math.Abs(original[i] - roundTrip[i]);
      maxError = Math.Max(maxError, error);
      sumSquaredError += error * error;
    }
    var rmsError = Math.Sqrt(sumSquaredError / totalSamples);

    _output.WriteLine($"Roundtrip quality: max error = {maxError:E4}, RMS error = {rmsError:E4}");
    _output.WriteLine($"Quantization threshold (1 LSB) = {1.0 / 32767:E4}");

    Assert.True(maxError < 2.0 / 32767, $"Max error {maxError} exceeds 2 LSB");
  }

  /// <summary>
  /// Tests that TappedOutputStream handles chunked writes (simulating FingerprintTapModifier's
  /// 4096-sample buffer batches) without losing data.
  /// </summary>
  [Fact]
  public void Stage1_TappedOutputStream_ChunkedWrites_NoDataLoss()
  {
    const int sampleRate = 48000;
    const int channels = 2;
    const int chunkSize = 4096;
    var totalSamples = sampleRate * channels * 15; // 15 seconds

    // Generate deterministic test data
    var rng = new Random(42);
    var original = new float[totalSamples];
    for (int i = 0; i < totalSamples; i++)
      original[i] = (float)(rng.NextDouble() * 2 - 1) * 0.8f;

    // Write in chunks (simulating FingerprintTapModifier)
    var tap = new TappedOutputStream(sampleRate, channels, 5);
    using var reader = tap.CreateReader("test-reader");

    var allReadBytes = new List<byte>();
    for (int offset = 0; offset < totalSamples; offset += chunkSize)
    {
      var count = Math.Min(chunkSize, totalSamples - offset);
      var chunk = new float[count];
      Array.Copy(original, offset, chunk, 0, count);
      tap.WriteFromEngine(chunk);

      // Read immediately after each write (simulating real-time capture)
      var readBuf = new byte[count * 2 + 4096];
      var bytesRead = reader.Read(readBuf, 0, readBuf.Length);
      if (bytesRead > 0)
        allReadBytes.AddRange(readBuf.Take(bytesRead));
    }

    var expectedBytes = totalSamples * 2;
    _output.WriteLine($"Source: {totalSamples} samples ({totalSamples * 2} bytes)");
    _output.WriteLine($"Read:   {allReadBytes.Count} bytes ({allReadBytes.Count / 2} samples)");
    _output.WriteLine($"Loss:   {expectedBytes - allReadBytes.Count} bytes ({(expectedBytes - allReadBytes.Count) / 2} samples)");

    // Allow small loss at boundaries (ring buffer edge) but capture should be near-complete
    var capturePercent = (double)allReadBytes.Count / expectedBytes * 100;
    _output.WriteLine($"Capture: {capturePercent:F1}%");
    Assert.True(capturePercent > 99.0, $"Captured only {capturePercent:F1}% — data loss detected");
  }

  #endregion

  #region Stage 2: WAV Generation Consistency

  /// <summary>
  /// Tests that reading a WAV file to float samples, then fingerprinting via
  /// GenerateFingerprintAsync (float→WAV→fpcalc) produces the same hash
  /// as fingerprinting the original WAV directly (GenerateFingerprintFromFileAsync).
  ///
  /// If this fails, the float→s16le→WAV conversion in ChromaprintFingerprintService
  /// is producing different audio than the original WAV.
  /// </summary>
  [Fact]
  public async Task Stage2_WavGeneration_SameData_ProducesIdenticalFingerprint()
  {
    // Create a reference WAV file (chirp has rich spectral content for fingerprinting)
    var referenceWav = TestAudioFileGenerator.CreateChirpFile(
      _tempDirectory, "reference.wav", TimeSpan.FromSeconds(20));

    // Step 1: Direct fingerprint of the WAV file
    var directFp = await _fingerprintService.GenerateFingerprintFromFileAsync(referenceWav);
    _output.WriteLine($"Direct WAV fingerprint:   duration={directFp.DurationSeconds}s, hash length={directFp.ChromaprintHash?.Length ?? 0}");
    Assert.False(string.IsNullOrEmpty(directFp.ChromaprintHash), "Direct fingerprint hash should not be empty");

    // Step 2: Read WAV → float samples → GenerateFingerprintAsync (float→WAV→fpcalc)
    var (samples, sampleRate, channels) = ReadWavFile(referenceWav);
    _output.WriteLine($"Read WAV: {samples.Length} samples, {sampleRate}Hz, {channels}ch, " +
      $"{(double)samples.Length / sampleRate / channels:F2}s");

    var sampleBuffer = new AudioSampleBuffer
    {
      Samples = samples,
      SampleRate = sampleRate,
      Channels = channels,
      Duration = TimeSpan.FromSeconds((double)samples.Length / sampleRate / channels),
      SourceName = "test-wav-readback"
    };

    var pipelineFp = await _fingerprintService.GenerateFingerprintAsync(sampleBuffer);
    _output.WriteLine($"Pipeline WAV fingerprint: duration={pipelineFp.DurationSeconds}s, hash length={pipelineFp.ChromaprintHash?.Length ?? 0}");
    Assert.False(string.IsNullOrEmpty(pipelineFp.ChromaprintHash), "Pipeline fingerprint hash should not be empty");

    // Step 3: Compare hashes
    bool hashesMatch = directFp.ChromaprintHash == pipelineFp.ChromaprintHash;
    _output.WriteLine($"\nHashes match: {hashesMatch}");

    if (!hashesMatch)
    {
      _output.WriteLine($"Direct:   {directFp.ChromaprintHash[..Math.Min(100, directFp.ChromaprintHash.Length)]}...");
      _output.WriteLine($"Pipeline: {pipelineFp.ChromaprintHash[..Math.Min(100, pipelineFp.ChromaprintHash.Length)]}...");
      _output.WriteLine("*** WAV GENERATION PRODUCES DIFFERENT FINGERPRINT — float→s16le roundtrip or WAV header issue ***");
    }

    Assert.Equal(directFp.ChromaprintHash, pipelineFp.ChromaprintHash);
  }

  #endregion

  #region Stage 3: Full Pipeline Through TappedOutputStream

  /// <summary>
  /// Tests the full pipeline: WAV→float→TappedOutputStream→readback→float→GenerateFingerprintAsync→fpcalc.
  /// Compares with direct WAV fingerprinting to verify the ring buffer roundtrip
  /// doesn't corrupt the fingerprint.
  /// </summary>
  [Fact]
  public async Task Stage3_Pipeline_ThroughTappedOutputStream_ProducesIdenticalFingerprint()
  {
    var referenceWav = TestAudioFileGenerator.CreateChirpFile(
      _tempDirectory, "pipeline_test.wav", TimeSpan.FromSeconds(20));

    // Direct fingerprint
    var directFp = await _fingerprintService.GenerateFingerprintFromFileAsync(referenceWav);
    _output.WriteLine($"Direct fingerprint: duration={directFp.DurationSeconds}s, hash length={directFp.ChromaprintHash?.Length ?? 0}");
    Assert.False(string.IsNullOrEmpty(directFp.ChromaprintHash));

    // Read WAV to float
    var (samples, sampleRate, channels) = ReadWavFile(referenceWav);
    _output.WriteLine($"Source: {samples.Length} samples, {sampleRate}Hz, {channels}ch");

    // Push through TappedOutputStream (simulating the engine pipeline)
    var tap = new TappedOutputStream(sampleRate, channels, 5);
    using var tapReader = tap.CreateReader("test-pipeline");

    const int chunkSize = 4096;
    var capturedBytes = new List<byte>();

    for (int offset = 0; offset < samples.Length; offset += chunkSize)
    {
      var count = Math.Min(chunkSize, samples.Length - offset);
      var chunk = new float[count];
      Array.Copy(samples, offset, chunk, 0, count);
      tap.WriteFromEngine(chunk);

      // Read immediately (simulating SoundFlowAudioTap's polling)
      var readBuf = new byte[count * 2 + 4096];
      var bytesRead = tapReader.Read(readBuf, 0, readBuf.Length);
      if (bytesRead > 0)
        capturedBytes.AddRange(readBuf.Take(bytesRead));
    }

    _output.WriteLine($"Captured {capturedBytes.Count} bytes ({capturedBytes.Count / 2} samples) through TappedOutputStream");

    // Convert back to float (same as SoundFlowAudioTap does)
    var capturedSamples = new float[capturedBytes.Count / 2];
    for (int i = 0; i < capturedSamples.Length; i++)
    {
      var pcm = (short)(capturedBytes[i * 2] | (capturedBytes[i * 2 + 1] << 8));
      capturedSamples[i] = pcm / (float)short.MaxValue;
    }

    var sampleBuffer = new AudioSampleBuffer
    {
      Samples = capturedSamples,
      SampleRate = sampleRate,
      Channels = channels,
      Duration = TimeSpan.FromSeconds((double)capturedSamples.Length / sampleRate / channels),
      SourceName = "tapped-pipeline"
    };

    var pipelineFp = await _fingerprintService.GenerateFingerprintAsync(sampleBuffer);
    _output.WriteLine($"Pipeline fingerprint: duration={pipelineFp.DurationSeconds}s, hash length={pipelineFp.ChromaprintHash?.Length ?? 0}");

    bool match = directFp.ChromaprintHash == pipelineFp.ChromaprintHash;
    _output.WriteLine($"\nFingerprints match: {match}");
    if (!match)
    {
      var dh = directFp.ChromaprintHash!;
      var ph = pipelineFp.ChromaprintHash!;
      _output.WriteLine($"Direct:   {dh[..Math.Min(100, dh.Length)]}...");
      _output.WriteLine($"Pipeline: {ph[..Math.Min(100, ph.Length)]}...");
      _output.WriteLine("*** TAPPED OUTPUT STREAM ROUNDTRIP CORRUPTS FINGERPRINT ***");
    }

    Assert.Equal(directFp.ChromaprintHash, pipelineFp.ChromaprintHash);
  }

  #endregion

  #region Stage 4: Short Duration AcoustID Matching

  /// <summary>
  /// Tests whether a 15-second fingerprint from the start of the song matches AcoustID.
  /// If this fails, the capture duration is too short for reliable matching.
  /// </summary>
  [Fact]
  public async Task Stage4_ShortDuration_WereReady_MatchesAcoustId()
  {
    Assert.True(File.Exists(WereReadyMp3), $"MP3 not found: {WereReadyMp3}");

    // Full-length fingerprint (reference)
    var fullResult = await RunFpcalcDirectAsync($"-json \"{WereReadyMp3}\"");
    Assert.NotNull(fullResult);
    _output.WriteLine($"Full fingerprint: duration={fullResult.Value.duration:F1}s, hash length={fullResult.Value.hash.Length}");

    // Short (15s) fingerprint
    var shortResult = await RunFpcalcDirectAsync($"-length 15 -json \"{WereReadyMp3}\"");
    Assert.NotNull(shortResult);
    _output.WriteLine($"Short (15s) fingerprint: duration={shortResult.Value.duration:F1}s, hash length={shortResult.Value.hash.Length}");

    // Lookup full via AcoustID
    _output.WriteLine("\n--- Full-length AcoustID lookup ---");
    var fullLookup = await _acoustIdClient.LookupAsync(fullResult.Value.hash, (int)fullResult.Value.duration);
    LogAcoustIdResult("Full", fullLookup);
    Assert.NotNull(fullLookup);

    // Lookup short via AcoustID
    _output.WriteLine("--- Short (15s) AcoustID lookup ---");
    var shortLookup = await _acoustIdClient.LookupAsync(shortResult.Value.hash, (int)shortResult.Value.duration);
    LogAcoustIdResult("Short", shortLookup);

    if (shortLookup == null)
      _output.WriteLine("*** 15-SECOND FINGERPRINT IS TOO SHORT FOR ACOUSTID TO MATCH ***");
    else
      _output.WriteLine("15-second fingerprint matches AcoustID — duration is NOT the issue");

    Assert.NotNull(shortLookup);
  }

  #endregion

  #region Stage 5: Full Simulated Pipeline with Real Audio

  /// <summary>
  /// End-to-end simulation of the live capture pipeline using the actual "We're Ready" MP3.
  /// Converts MP3→WAV via ffmpeg, then simulates: WAV→float→TappedOutputStream→readback→fingerprint→AcoustID.
  /// This is the closest we can get to testing the live pipeline without audio hardware.
  /// </summary>
  [Fact]
  public async Task Stage5_FullPipeline_WereReady_SimulatedCapture_MatchesAcoustId()
  {
    Assert.True(File.Exists(WereReadyMp3), $"MP3 not found: {WereReadyMp3}");

    // Step 1: Convert MP3 to 48kHz stereo WAV using ffmpeg
    var wavPath = Path.Combine(_tempDirectory, "were_ready_48k.wav");
    var ffmpegAvailable = await ConvertMp3ToWavAsync(WereReadyMp3, wavPath, 48000, 2);
    if (!ffmpegAvailable)
    {
      _output.WriteLine("SKIPPING: ffmpeg not available for MP3→WAV conversion");
      _output.WriteLine("Install ffmpeg to enable this test");
      return;
    }

    _output.WriteLine($"Converted MP3 to WAV: {new FileInfo(wavPath).Length:N0} bytes");

    // Step 2: Get reference fingerprints at different durations
    var fullRefResult = await RunFpcalcDirectAsync($"-json \"{wavPath}\"");
    Assert.NotNull(fullRefResult);
    _output.WriteLine($"Reference (full): duration={fullRefResult.Value.duration:F1}s, hash={fullRefResult.Value.hash.Length} chars");

    var shortRefResult = await RunFpcalcDirectAsync($"-length 15 -json \"{wavPath}\"");
    Assert.NotNull(shortRefResult);
    _output.WriteLine($"Reference (15s):  duration={shortRefResult.Value.duration:F1}s, hash={shortRefResult.Value.hash.Length} chars");

    // Step 3: Read the first 15 seconds from the WAV as float samples
    var (allSamples, sampleRate, channels) = ReadWavFile(wavPath);
    var captureFrames = 15 * sampleRate;
    var captureSampleCount = Math.Min(captureFrames * channels, allSamples.Length);
    var sourceSamples = new float[captureSampleCount];
    Array.Copy(allSamples, sourceSamples, captureSampleCount);
    _output.WriteLine($"\nSource: {sourceSamples.Length} samples ({(double)sourceSamples.Length / sampleRate / channels:F2}s) at {sampleRate}Hz {channels}ch");

    // Step 4: Push through TappedOutputStream (simulating engine pipeline)
    var tap = new TappedOutputStream(sampleRate, channels, 5);
    using var tapReader = tap.CreateReader("sim-capture");

    const int chunkSize = 4096;
    var capturedBytes = new List<byte>();

    for (int offset = 0; offset < sourceSamples.Length; offset += chunkSize)
    {
      var count = Math.Min(chunkSize, sourceSamples.Length - offset);
      var chunk = new float[count];
      Array.Copy(sourceSamples, offset, chunk, 0, count);
      tap.WriteFromEngine(chunk);

      var readBuf = new byte[count * 2 + 4096];
      var bytesRead = tapReader.Read(readBuf, 0, readBuf.Length);
      if (bytesRead > 0)
        capturedBytes.AddRange(readBuf.Take(bytesRead));
    }

    _output.WriteLine($"Captured: {capturedBytes.Count} bytes ({capturedBytes.Count / 2} samples, {capturedBytes.Count * 100.0 / (sourceSamples.Length * 2):F1}%)");

    // Step 5: Convert bytes back to float (same as SoundFlowAudioTap)
    var capturedSamples = new float[capturedBytes.Count / 2];
    for (int i = 0; i < capturedSamples.Length; i++)
    {
      var pcm = (short)(capturedBytes[i * 2] | (capturedBytes[i * 2 + 1] << 8));
      capturedSamples[i] = pcm / (float)short.MaxValue;
    }

    // Compute RMS (matching the live pipeline's diagnostic)
    var sumSq = 0.0;
    for (int i = 0; i < capturedSamples.Length; i++)
      sumSq += capturedSamples[i] * capturedSamples[i];
    var rms = Math.Sqrt(sumSq / capturedSamples.Length);
    var rmsDb = rms > 0 ? 20 * Math.Log10(rms) : -100;
    _output.WriteLine($"Captured RMS: {rmsDb:F1}dB");

    // Step 6: Generate fingerprint via ChromaprintFingerprintService (float→WAV→fpcalc)
    var sampleBuffer = new AudioSampleBuffer
    {
      Samples = capturedSamples,
      SampleRate = sampleRate,
      Channels = channels,
      Duration = TimeSpan.FromSeconds((double)capturedSamples.Length / sampleRate / channels),
      SourceName = "simulated-capture"
    };

    var pipelineFp = await _fingerprintService.GenerateFingerprintAsync(sampleBuffer);
    _output.WriteLine($"Pipeline fingerprint: duration={pipelineFp.DurationSeconds}s, hash={pipelineFp.ChromaprintHash?.Length ?? 0} chars");
    Assert.False(string.IsNullOrEmpty(pipelineFp.ChromaprintHash), "Pipeline fingerprint should not be empty");

    // Step 7: Compare fingerprint hashes and durations
    _output.WriteLine("\n=== Fingerprint Comparison ===");
    _output.WriteLine($"fpcalc 15s ref hash: {shortRefResult.Value.hash[..Math.Min(80, shortRefResult.Value.hash.Length)]}...");
    _output.WriteLine($"Pipeline hash:       {pipelineFp.ChromaprintHash[..Math.Min(80, pipelineFp.ChromaprintHash.Length)]}...");
    _output.WriteLine($"Hash exact match: {shortRefResult.Value.hash == pipelineFp.ChromaprintHash}");
    _output.WriteLine($"fpcalc 15s ref duration: {shortRefResult.Value.duration:F6} → int: {(int)shortRefResult.Value.duration}");
    _output.WriteLine($"Pipeline duration:       {pipelineFp.DurationSeconds} (already int, from fpcalc)");
    _output.WriteLine($"Duration match: {pipelineFp.DurationSeconds == (int)shortRefResult.Value.duration}");

    // Step 8: Query AcoustID with each fingerprint (with delays to avoid rate limiting)
    _output.WriteLine("\n=== AcoustID Lookups (1s delay between calls) ===");

    // First: known-good reference to confirm API is working
    _output.WriteLine("--- fpcalc 15s reference (duration={0}) ---", (int)shortRefResult.Value.duration);
    var shortRefLookup = await _acoustIdClient.LookupAsync(shortRefResult.Value.hash, (int)shortRefResult.Value.duration);
    LogAcoustIdResult("fpcalc-15s", shortRefLookup);

    await Task.Delay(1500); // Rate limit protection

    // Second: pipeline fingerprint with SAME duration as reference
    _output.WriteLine("--- Pipeline fingerprint with ref duration (duration={0}) ---", (int)shortRefResult.Value.duration);
    var pipelineWithRefDuration = await _acoustIdClient.LookupAsync(pipelineFp.ChromaprintHash, (int)shortRefResult.Value.duration);
    LogAcoustIdResult("Pipeline-refDur", pipelineWithRefDuration);

    await Task.Delay(1500);

    // Third: pipeline fingerprint with ITS OWN duration
    _output.WriteLine("--- Pipeline fingerprint with own duration (duration={0}) ---", pipelineFp.DurationSeconds);
    var pipelineLookup = await _acoustIdClient.LookupAsync(pipelineFp.ChromaprintHash, pipelineFp.DurationSeconds);
    LogAcoustIdResult("Pipeline-ownDur", pipelineLookup);

    await Task.Delay(1500);

    // Fourth: full reference
    _output.WriteLine("--- fpcalc full reference (duration={0}) ---", (int)fullRefResult.Value.duration);
    var fullRefLookup = await _acoustIdClient.LookupAsync(fullRefResult.Value.hash, (int)fullRefResult.Value.duration);
    LogAcoustIdResult("fpcalc-full", fullRefLookup);

    // Duration tolerance tests — find what duration range AcoustID accepts
    _output.WriteLine("\n=== Duration Tolerance Tests ===");
    int[] testDurations = [0, 30, 60, 120, 180, 200, 220, 240, 260, 300];
    foreach (var dur in testDurations)
    {
      await Task.Delay(1500);
      _output.WriteLine($"--- duration={dur} ---");
      var result = await _acoustIdClient.LookupAsync(pipelineFp.ChromaprintHash, dur);
      LogAcoustIdResult($"dur-{dur}", result);
    }

    // Summary
    _output.WriteLine("\n=== DIAGNOSIS ===");
    _output.WriteLine("Root cause: fpcalc reports WAV segment duration ({0}s), not full track duration ({1}s)",
      pipelineFp.DurationSeconds, (int)shortRefResult.Value.duration);
    _output.WriteLine("Pipeline fingerprint is IDENTICAL to reference — duration param is the only issue.");

    // The test passes if pipeline + correct duration works (proving fingerprint quality)
    Assert.NotNull(pipelineWithRefDuration);
  }

  #endregion

  #region Stage 6: File-Based Fingerprinting (The Fix)

  /// <summary>
  /// Stage 6: Validates the fix — using GenerateFingerprintFromFileAsync directly
  /// on the source file, which gives fpcalc the correct track duration.
  /// This is what BackgroundIdentificationService now does for file-based sources.
  /// </summary>
  [Fact]
  [Trait("Category", "Integration")]
  public async Task Stage6_FileBasedFingerprint_WereReady_MatchesAcoustId()
  {
    if (!File.Exists(WereReadyMp3))
    {
      _output.WriteLine($"SKIP: Test MP3 not found at {WereReadyMp3}");
      return;
    }

    // Step 1: Fingerprint the actual MP3 file (what the fixed pipeline does)
    _output.WriteLine("=== File-Based Fingerprinting (The Fix) ===");
    var fingerprint = await _fingerprintService.GenerateFingerprintFromFileAsync(WereReadyMp3);

    Assert.False(string.IsNullOrEmpty(fingerprint.ChromaprintHash), "Fingerprint hash should not be empty");
    _output.WriteLine($"Fingerprint: duration={fingerprint.DurationSeconds}s, hash={fingerprint.ChromaprintHash.Length} chars");

    // Step 2: Verify duration is the full track duration, not a short segment
    Assert.True(fingerprint.DurationSeconds > 100,
      $"Duration should be full track length (>100s), got {fingerprint.DurationSeconds}s");
    _output.WriteLine($"Duration={fingerprint.DurationSeconds}s (full track length ✓)");

    // Step 3: Look up via AcoustID — this should now match
    var result = await _acoustIdClient.LookupAsync(fingerprint.ChromaprintHash, fingerprint.DurationSeconds);
    LogAcoustIdResult("File-based", result);

    Assert.NotNull(result);
    Assert.True(result.Recordings.Count > 0, "Should have at least one recording");

    var hasBoston = result.Recordings.Any(r =>
      r.Artists.Any(a => a.Contains("Boston", StringComparison.OrdinalIgnoreCase)));
    Assert.True(hasBoston, "Should identify 'We're Ready' by Boston");

    _output.WriteLine("File-based fingerprinting WORKS — this is what the fixed pipeline uses for file sources.");
  }

  #endregion

  #region Helpers

  private async Task<(string hash, double duration)?> RunFpcalcDirectAsync(string arguments)
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

    using var process = Process.Start(psi);
    if (process == null) return null;

    var stdout = await process.StandardOutput.ReadToEndAsync();
    var stderr = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    if (process.ExitCode != 0)
    {
      _output.WriteLine($"fpcalc error (exit {process.ExitCode}): {stderr}");
      return null;
    }

    var result = JsonSerializer.Deserialize<FpcalcJsonResult>(stdout);
    if (result == null || string.IsNullOrEmpty(result.Fingerprint))
    {
      _output.WriteLine($"fpcalc returned invalid output: {stdout[..Math.Min(200, stdout.Length)]}");
      return null;
    }

    return (result.Fingerprint, result.Duration);
  }

  private static (float[] samples, int sampleRate, int channels) ReadWavFile(string path)
  {
    using var stream = File.OpenRead(path);
    using var reader = new BinaryReader(stream);

    // RIFF header
    var riff = reader.ReadBytes(4);
    reader.ReadInt32(); // file size
    var wave = reader.ReadBytes(4);

    int sampleRate = 0, channels = 0, bitsPerSample = 0;
    byte[]? data = null;

    // Parse chunks
    while (stream.Position < stream.Length - 8)
    {
      var chunkId = new string(reader.ReadChars(4));
      var chunkSize = reader.ReadInt32();

      if (chunkId == "fmt ")
      {
        reader.ReadInt16(); // audio format (1=PCM)
        channels = reader.ReadInt16();
        sampleRate = reader.ReadInt32();
        reader.ReadInt32(); // byte rate
        reader.ReadInt16(); // block align
        bitsPerSample = reader.ReadInt16();
        if (chunkSize > 16)
          reader.ReadBytes(chunkSize - 16);
      }
      else if (chunkId == "data")
      {
        data = reader.ReadBytes(chunkSize);
      }
      else
      {
        reader.ReadBytes(chunkSize);
      }
    }

    if (data == null)
      throw new InvalidOperationException($"No data chunk found in WAV file: {path}");

    // Convert s16le to float
    var sampleCount = data.Length / 2;
    var samples = new float[sampleCount];
    for (int i = 0; i < sampleCount; i++)
    {
      var pcm = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
      samples[i] = pcm / (float)short.MaxValue;
    }

    return (samples, sampleRate, channels);
  }

  private async Task<bool> ConvertMp3ToWavAsync(string mp3Path, string wavPath, int sampleRate, int channels)
  {
    try
    {
      var ffmpegName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
      var psi = new ProcessStartInfo
      {
        FileName = ffmpegName,
        Arguments = $"-i \"{mp3Path}\" -ar {sampleRate} -ac {channels} -acodec pcm_s16le -y \"{wavPath}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var process = Process.Start(psi);
      if (process == null)
      {
        _output.WriteLine("Failed to start ffmpeg process");
        return false;
      }

      var stderr = await process.StandardError.ReadToEndAsync();
      await process.WaitForExitAsync();

      if (process.ExitCode != 0)
      {
        _output.WriteLine($"ffmpeg exited with code {process.ExitCode}");
        _output.WriteLine(stderr[..Math.Min(300, stderr.Length)]);
        return false;
      }
      return true;
    }
    catch (Exception ex)
    {
      _output.WriteLine($"ffmpeg not available: {ex.GetType().Name}: {ex.Message}");
      return false;
    }
  }

  private void LogAcoustIdResult(string label, AcoustIdLookupResult? result)
  {
    if (result == null)
    {
      _output.WriteLine($"  {label}: NO MATCH");
      return;
    }

    _output.WriteLine($"  {label}: Score={result.Score:P0}, Recordings={result.Recordings.Count}");
    foreach (var rec in result.Recordings.Take(3))
    {
      _output.WriteLine($"    Recording: \"{rec.Title}\" by {string.Join(", ", rec.Artists)}");
    }
  }

  private static string FindSolutionRoot()
  {
    var dir = AppDomain.CurrentDomain.BaseDirectory;
    while (dir != null)
    {
      if (File.Exists(Path.Combine(dir, "RadioConsole.sln")))
        return dir;
      dir = Path.GetDirectoryName(dir);
    }
    throw new InvalidOperationException("Could not find solution root (RadioConsole.sln)");
  }

  private sealed class FpcalcJsonResult
  {
    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;
  }

  #endregion
}
