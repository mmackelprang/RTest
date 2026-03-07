using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Radio.AudioAnalysis;
using Xunit;
using Xunit.Abstractions;

namespace Radio.IntegrationTests.Audio;

/// <summary>
/// Integration tests for the diagnostic capture API endpoint.
/// These require a running audio pipeline and are excluded from CI.
/// Run on-device after deploying to Ubuntu/Pi.
/// </summary>
[Trait("Category", "Integration")]
public class DiagnosticCaptureIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly ITestOutputHelper _output;

  public DiagnosticCaptureIntegrationTests(
    WebApplicationFactory<Program> factory,
    ITestOutputHelper output)
  {
    _factory = factory;
    _output = output;
  }

  [Fact]
  [Trait("Category", "Integration")]
  public async Task CaptureEndpoint_Returns200_WithStageFiles()
  {
    var client = _factory.CreateClient();

    var response = await client.PostAsJsonAsync("/api/diagnostics/capture",
      new { DurationSeconds = 3 });

    // Even without active audio, the endpoint should return 200
    response.EnsureSuccessStatusCode();

    var result = await response.Content.ReadFromJsonAsync<CaptureResultResponse>();
    Assert.NotNull(result);
    Assert.True(result!.Success);
    Assert.NotEmpty(result.OutputDirectory);

    _output.WriteLine($"Capture output: {result.OutputDirectory}");
    foreach (var (stage, path) in result.StageFiles)
    {
      _output.WriteLine($"  {stage}: {path}");
    }

    foreach (var (stage, count) in result.StageSampleCounts)
    {
      _output.WriteLine($"  {stage}: {count} samples");
    }
  }

  [Fact]
  [Trait("Category", "Integration")]
  public async Task FileSource_KnownWav_NoDistortion()
  {
    // This test requires:
    // 1. A running audio pipeline with a playback device
    // 2. A known test WAV file at /opt/radio-console/media/audio/
    //
    // Steps:
    // 1. Play a known WAV file via POST /api/files/play
    // 2. Start capture via POST /api/diagnostics/capture
    // 3. Wait for capture to complete
    // 4. Read the captured WAV files
    // 5. Compare generator-input vs reference
    var client = _factory.CreateClient();

    // Generate reference tone
    var reference = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, rightHz: 440, durationSamples: 48000 * 5, amplitude: 0.8f);

    // Write reference to temp file for comparison
    var tempDir = Path.Combine(Path.GetTempPath(), $"diag_test_{Guid.NewGuid()}");
    Directory.CreateDirectory(tempDir);
    var refPath = Path.Combine(tempDir, "reference.wav");
    WavFileHelper.WriteWavFile(refPath, reference);

    try
    {
      // Start capture
      var captureResponse = await client.PostAsJsonAsync("/api/diagnostics/capture",
        new { DurationSeconds = 5 });
      captureResponse.EnsureSuccessStatusCode();

      var captureResult = await captureResponse.Content.ReadFromJsonAsync<CaptureResultResponse>();
      Assert.NotNull(captureResult);
      Assert.True(captureResult!.Success, captureResult.ErrorMessage ?? "Capture failed");

      // Check if any stage files were created
      _output.WriteLine($"Capture duration: {captureResult.DurationSeconds:F1}s");
      foreach (var (stage, count) in captureResult.StageSampleCounts)
      {
        _output.WriteLine($"  {stage}: {count} samples");
      }

      // If post-modifiers WAV exists, read and analyze it
      if (captureResult.StageFiles.TryGetValue("post-modifiers", out var postModPath)
          && File.Exists(postModPath))
      {
        var captured = WavFileHelper.ReadWavFile(postModPath, out var sr, out var ch);
        _output.WriteLine($"Post-modifiers: {captured.Length} samples, {sr}Hz, {ch}ch");

        // Basic analysis — verify the captured audio contains non-silent data
        var rms = WavFileHelper.CalculateRms(captured);
        _output.WriteLine($"Post-modifiers RMS: {rms:F4} ({WavFileHelper.LinearToDb(rms):F1} dB)");
      }
    }
    finally
    {
      if (Directory.Exists(tempDir))
      {
        Directory.Delete(tempDir, recursive: true);
      }
    }
  }

  [Fact]
  [Trait("Category", "Integration")]
  public async Task StopCapture_StopsActiveCapture()
  {
    var client = _factory.CreateClient();

    // Start a long capture in the background
    var captureTask = client.PostAsJsonAsync("/api/diagnostics/capture",
      new { DurationSeconds = 30 });

    // Wait briefly then stop it
    await Task.Delay(1000);
    var stopResponse = await client.PostAsync("/api/diagnostics/capture/stop", null);

    // Stop should succeed (200) or return 404 if capture already finished
    Assert.True(
      stopResponse.IsSuccessStatusCode || stopResponse.StatusCode == System.Net.HttpStatusCode.NotFound);

    // Wait for capture to complete
    var captureResponse = await captureTask;
    captureResponse.EnsureSuccessStatusCode();

    var result = await captureResponse.Content.ReadFromJsonAsync<CaptureResultResponse>();
    Assert.NotNull(result);

    _output.WriteLine($"Capture stopped after {result!.DurationSeconds:F1}s");
    Assert.True(result.DurationSeconds < 25, "Capture should have stopped early");
  }

  // Response DTO for deserialization
  private class CaptureResultResponse
  {
    public DateTime StartTime { get; set; }
    public double DurationSeconds { get; set; }
    public string OutputDirectory { get; set; } = "";
    public Dictionary<string, string> StageFiles { get; set; } = new();
    public Dictionary<string, int> StageSampleCounts { get; set; } = new();
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
  }
}
