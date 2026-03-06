using Radio.Infrastructure.Audio.Diagnostics;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Diagnostics;

/// <summary>
/// Unit tests for <see cref="CaptureSession"/>.
/// Verifies bounded sample accumulation, auto-stop, WAV output, and thread safety.
/// </summary>
public class CaptureSessionTests
{
  [Fact]
  public void AddSamples_AccumulatesCorrectly()
  {
    var session = new CaptureSession("test", maxDurationSeconds: 1.0f, sampleRate: 48000, channels: 2);
    session.Start();

    var samples = new float[] { 0.1f, -0.1f, 0.2f, -0.2f, 0.3f, -0.3f };
    session.AddSamples(samples);

    Assert.Equal(6, session.CapturedSamples);

    var result = session.GetSamples();
    Assert.Equal(samples.Length, result.Length);
    for (int i = 0; i < samples.Length; i++)
      Assert.Equal(samples[i], result[i]);
  }

  [Fact]
  public void AddSamples_MultipleChunks_AccumulatesAll()
  {
    var session = new CaptureSession("test", maxDurationSeconds: 1.0f, sampleRate: 48000, channels: 2);
    session.Start();

    var chunk1 = new float[] { 0.1f, 0.2f };
    var chunk2 = new float[] { 0.3f, 0.4f };
    var chunk3 = new float[] { 0.5f, 0.6f };

    session.AddSamples(chunk1);
    session.AddSamples(chunk2);
    session.AddSamples(chunk3);

    Assert.Equal(6, session.CapturedSamples);

    var result = session.GetSamples();
    Assert.Equal(new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f }, result);
  }

  [Fact]
  public void AddSamples_StopsAcceptingAfterMaxDuration()
  {
    // 0.001 seconds at 48000Hz stereo = 96 samples max
    var session = new CaptureSession("test", maxDurationSeconds: 0.001f, sampleRate: 48000, channels: 2);
    session.Start();

    var bigChunk = new float[200];
    for (int i = 0; i < bigChunk.Length; i++)
      bigChunk[i] = (float)i / bigChunk.Length;

    session.AddSamples(bigChunk);

    Assert.True(session.CapturedSamples <= 96);
    Assert.False(session.IsCapturing); // Auto-stopped
  }

  [Fact]
  public void AddSamples_IgnoredWhenNotStarted()
  {
    var session = new CaptureSession("test", maxDurationSeconds: 1.0f);

    session.AddSamples(new float[] { 0.1f, 0.2f });
    Assert.Equal(0, session.CapturedSamples);
  }

  [Fact]
  public void AddSamples_IgnoredAfterStop()
  {
    var session = new CaptureSession("test", maxDurationSeconds: 1.0f);
    session.Start();
    session.AddSamples(new float[] { 0.1f, 0.2f });
    session.Stop();
    session.AddSamples(new float[] { 0.3f, 0.4f });

    Assert.Equal(2, session.CapturedSamples);
  }

  [Fact]
  public void Start_ResetsBuffer()
  {
    var session = new CaptureSession("test", maxDurationSeconds: 1.0f);

    session.Start();
    session.AddSamples(new float[] { 0.1f, 0.2f, 0.3f });
    session.Stop();
    Assert.Equal(3, session.CapturedSamples);

    session.Start(); // Should reset
    Assert.Equal(0, session.CapturedSamples);
  }

  [Fact]
  public void WriteToFile_ProducesValidWav()
  {
    var session = new CaptureSession("test", maxDurationSeconds: 1.0f, sampleRate: 48000, channels: 2);
    session.Start();

    // Generate a short sine wave
    var samples = new float[960]; // 10ms stereo
    for (int i = 0; i < 480; i++)
    {
      var val = MathF.Sin(2 * MathF.PI * 440f * i / 48000f) * 0.5f;
      samples[i * 2] = val;
      samples[i * 2 + 1] = val;
    }
    session.AddSamples(samples);
    session.Stop();

    var tempFile = Path.Combine(Path.GetTempPath(), $"capture_test_{Guid.NewGuid()}.wav");
    try
    {
      session.WriteToFile(tempFile);

      Assert.True(File.Exists(tempFile));
      var fileInfo = new FileInfo(tempFile);
      Assert.True(fileInfo.Length > 44); // WAV header + data

      // Verify WAV header
      using var fs = new FileStream(tempFile, FileMode.Open);
      using var reader = new BinaryReader(fs);
      var riff = new string(reader.ReadChars(4));
      Assert.Equal("RIFF", riff);
      reader.ReadInt32(); // file size
      var wave = new string(reader.ReadChars(4));
      Assert.Equal("WAVE", wave);
    }
    finally
    {
      if (File.Exists(tempFile))
        File.Delete(tempFile);
    }
  }

  [Fact]
  public async Task ConcurrentWrites_NoCorruption()
  {
    var session = new CaptureSession("test", maxDurationSeconds: 2.0f, sampleRate: 48000, channels: 2);
    session.Start();

    var tasks = new Task[4];
    var samplesPerThread = 48000; // 0.5s per thread

    for (int t = 0; t < tasks.Length; t++)
    {
      var threadIndex = t;
      tasks[t] = Task.Run(() =>
      {
        var chunk = new float[960]; // 10ms per write
        for (int i = 0; i < chunk.Length; i++)
          chunk[i] = (float)(threadIndex + 1) * 0.1f;

        for (int i = 0; i < samplesPerThread / chunk.Length; i++)
          session.AddSamples(chunk);
      });
    }

    await Task.WhenAll(tasks);
    session.Stop();

    // Verify total samples is reasonable (may be less than sum if buffer filled)
    Assert.True(session.CapturedSamples > 0);
    var result = session.GetSamples();
    Assert.Equal(session.CapturedSamples, result.Length);

    // Verify no NaN or Infinity values (corruption indicator)
    foreach (var sample in result)
    {
      Assert.False(float.IsNaN(sample), "NaN detected — concurrent corruption");
      Assert.False(float.IsInfinity(sample), "Infinity detected — concurrent corruption");
    }
  }
}
