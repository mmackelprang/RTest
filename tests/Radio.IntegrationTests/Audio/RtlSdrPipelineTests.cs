using Microsoft.Extensions.Logging;
using Moq;
using Radio.Infrastructure.Audio.SoundFlow;
using RTLSDRCore;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using RTLSDRCore.Models;
using Xunit.Abstractions;

namespace Radio.IntegrationTests.Audio;

/// <summary>
/// Staged integration tests for the RTL-SDR audio pipeline.
/// Tests each stage independently to diagnose where audio may be lost.
/// Pipeline: MockSdrDevice → RadioReceiver → BufferedSoundGenerator → GenerateAudio output
/// </summary>
public class RtlSdrPipelineTests : IDisposable
{
  private readonly ITestOutputHelper _output;
  private readonly ILogger<RtlSdrPipelineTests> _logger;
  private MockSdrDevice? _mockDevice;
  private RadioReceiver? _receiver;

  public RtlSdrPipelineTests(ITestOutputHelper output)
  {
    _output = output;
    var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
    _logger = loggerFactory.CreateLogger<RtlSdrPipelineTests>();
  }

  public void Dispose()
  {
    _receiver?.Shutdown();
    _receiver?.Dispose();
    _mockDevice?.Close();
  }

  // ── Test 1: MockDevice emits IQ samples ──

  [Fact]
  public async Task MockDevice_EmitsIqSamples_WhenStreaming()
  {
    _mockDevice = new MockSdrDevice();
    _mockDevice.Open();
    _mockDevice.SetFrequency(94_700_000); // Tune to simulated FM station

    var samplesReceived = 0;
    var sampleTcs = new TaskCompletionSource<IqSample[]>();

    _mockDevice.SamplesAvailable += (_, e) =>
    {
      Interlocked.Add(ref samplesReceived, e.Samples.Length);
      if (!sampleTcs.Task.IsCompleted)
        sampleTcs.TrySetResult(e.Samples);
    };

    _mockDevice.StartStreaming();
    var firstBatch = await sampleTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

    _mockDevice.StopStreaming();

    _output.WriteLine($"Received {samplesReceived:N0} IQ samples total");
    _output.WriteLine($"First batch size: {firstBatch.Length}");

    // Verify IQ samples are non-zero (tuned to a simulated station)
    var rmsI = Math.Sqrt(firstBatch.Average(s => s.I * s.I));
    var rmsQ = Math.Sqrt(firstBatch.Average(s => s.Q * s.Q));
    var rmsDb = 20 * Math.Log10(Math.Max(rmsI, 1e-10));
    _output.WriteLine($"IQ RMS: I={rmsI:F6}, Q={rmsQ:F6} ({rmsDb:F1} dB)");

    Assert.True(firstBatch.Length > 0, "No IQ samples received");
    Assert.True(rmsI > 0.001, $"IQ I-channel RMS too low: {rmsI:F6} — MockDevice may not be generating signal");
  }

  // ── Test 2: RadioReceiver demodulates to audio ──

  [Fact]
  public async Task RadioReceiver_DemodulatesAudio_FromMockDevice()
  {
    _mockDevice = new MockSdrDevice();
    _receiver = new RadioReceiver(_mockDevice);

    var audioReceived = new List<float[]>();
    var audioBatchTcs = new TaskCompletionSource<float[]>();

    _receiver.AudioDataAvailable += (_, e) =>
    {
      audioReceived.Add(e.Samples);
      if (!audioBatchTcs.Task.IsCompleted)
        audioBatchTcs.TrySetResult(e.Samples);
    };

    // Tune to a simulated station with strong signal
    _receiver.SetFrequency(94_700_000);
    _receiver.SquelchThreshold = 0.0f; // Disable squelch to ensure audio passes
    _receiver.Startup();

    try
    {
      var firstAudio = await audioBatchTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

      var totalSamples = audioReceived.Sum(a => a.Length);
      var rms = Math.Sqrt(firstAudio.Average(s => s * s));
      var rmsDb = 20 * Math.Log10(Math.Max(rms, 1e-10));
      var peak = firstAudio.Max(s => Math.Abs(s));

      _output.WriteLine($"Audio batches received: {audioReceived.Count}");
      _output.WriteLine($"Total audio samples: {totalSamples:N0}");
      _output.WriteLine($"First batch size: {firstAudio.Length}");
      _output.WriteLine($"RMS: {rms:F6} ({rmsDb:F1} dB), Peak: {peak:F6}");

      Assert.True(firstAudio.Length > 0, "AudioDataAvailable fired with empty samples");
      Assert.True(rms > 1e-6, $"Demodulated audio is silent (RMS={rms:E2})");
    }
    catch (TimeoutException)
    {
      _output.WriteLine("TIMEOUT: AudioDataAvailable never fired within 5 seconds");
      _output.WriteLine($"Receiver state: {_receiver.GetRadioState().State}");
      _output.WriteLine($"Signal strength: {_receiver.SignalStrength}");
      _output.WriteLine($"Squelch threshold: {_receiver.SquelchThreshold}");
      Assert.Fail("RadioReceiver.AudioDataAvailable did not fire — demodulation pipeline is broken");
    }
  }

  // ── Test 3: High squelch delivers silence (not real audio) ──

  [Fact]
  public async Task RadioReceiver_SquelchDeliversSilence_WhenSignalWeak()
  {
    _mockDevice = new MockSdrDevice();
    _mockDevice.SetNoiseFloor(0.001f); // Very low noise

    _receiver = new RadioReceiver(_mockDevice);

    var audioCallbackCount = 0;
    var nonZeroSamplesReceived = false;
    float? reportedSignalStrength = null;

    _receiver.AudioDataAvailable += (_, e) =>
    {
      audioCallbackCount++;
      // Check if any non-zero audio was delivered (should be all silence)
      for (int i = 0; i < e.SampleCount; i++)
      {
        if (Math.Abs(e.Samples[i]) > float.Epsilon)
        {
          nonZeroSamplesReceived = true;
          break;
        }
      }
    };
    _receiver.SignalStrengthUpdated += (_, e) =>
    {
      reportedSignalStrength ??= e.Strength;
    };

    // Tune to a frequency with NO simulated station
    _receiver.SetFrequency(90_000_000);
    _receiver.SquelchThreshold = 0.5f; // High squelch — should block weak/no signal
    _receiver.Startup();

    // Wait briefly — squelch now delivers silence instead of blocking callbacks
    await Task.Delay(2000);

    _output.WriteLine($"Audio callbacks: {audioCallbackCount}");
    _output.WriteLine($"Non-zero audio received: {nonZeroSamplesReceived}");
    _output.WriteLine($"Signal strength: {reportedSignalStrength?.ToString("F4") ?? "not reported"}");
    _output.WriteLine($"Squelch threshold: {_receiver.SquelchThreshold}");

    // Squelch delivers silence (zero-filled buffers) to keep downstream buffer fed.
    // Audio callbacks fire, but all samples should be zero.
    Assert.True(audioCallbackCount > 0, "Squelch should still deliver silence callbacks");
    Assert.False(nonZeroSamplesReceived, "Squelched audio should be all silence (zero samples)");
    Assert.True(reportedSignalStrength.HasValue, "Signal strength should still be reported even when squelched");
  }

  // ── Test 4: Samples reach BufferedSoundGenerator ──

  [Fact]
  public async Task Samples_ReachBufferedSoundGenerator_FromRadioReceiver()
  {
    _mockDevice = new MockSdrDevice();
    _receiver = new RadioReceiver(_mockDevice);

    // We can't create a real BufferedSoundGenerator without a SoundFlow AudioEngine,
    // so we simulate what SDRRadioAudioSource does: capture AddSamples calls.
    var samplesAdded = new List<float[]>();
    long totalSamplesAdded = 0;
    var samplesTcs = new TaskCompletionSource<bool>();

    _receiver.AudioDataAvailable += (_, e) =>
    {
      samplesAdded.Add(e.Samples.ToArray());
      Interlocked.Add(ref totalSamplesAdded, e.Samples.Length);
      if (Interlocked.Read(ref totalSamplesAdded) > 1000 && !samplesTcs.Task.IsCompleted)
        samplesTcs.TrySetResult(true);
    };

    _receiver.SetFrequency(94_700_000);
    _receiver.SquelchThreshold = 0.0f;
    _receiver.Startup();

    try
    {
      await samplesTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

      var total = Interlocked.Read(ref totalSamplesAdded);
      _output.WriteLine($"Total audio samples that would be AddSamples'd: {total:N0}");
      _output.WriteLine($"Audio batch count: {samplesAdded.Count}");

      // Calculate overall RMS
      var allSamples = samplesAdded.SelectMany(a => a).ToArray();
      var rms = Math.Sqrt(allSamples.Average(s => s * s));
      var rmsDb = 20 * Math.Log10(Math.Max(rms, 1e-10));
      _output.WriteLine($"Overall RMS: {rms:F6} ({rmsDb:F1} dB)");

      Assert.True(total > 0, "No audio samples reached BufferedSoundGenerator stage");
    }
    catch (TimeoutException)
    {
      _output.WriteLine("TIMEOUT: Not enough audio samples received in 5 seconds");
      _output.WriteLine($"Samples so far: {Interlocked.Read(ref totalSamplesAdded)}");
      Assert.Fail("Insufficient audio samples reached the BufferedSoundGenerator stage");
    }
  }

  // ── Test 5: Real RTL-SDR device detected ──

  [Fact(Skip = "Requires physical RTL-SDR hardware")]
  public void RealDevice_IsDetected_WhenPluggedIn()
  {
    var devices = SdrDeviceFactory.EnumerateDevices();
    var rtlDevices = devices.Where(d => d.Type == DeviceType.RTLSDR).ToList();

    _output.WriteLine($"Total devices found: {devices.Count}");
    foreach (var d in devices)
    {
      _output.WriteLine($"  [{d.Type}] {d.Name} - {d.Manufacturer} (Serial: {d.Serial}, Available: {d.IsAvailable})");
    }

    _output.WriteLine($"RTL-SDR devices: {rtlDevices.Count}");
    Assert.True(rtlDevices.Count > 0, "No RTL-SDR devices detected. Is the dongle plugged in?");
  }

  // ── Test 6: Real device streams IQ samples ──

  [Fact(Skip = "Requires physical RTL-SDR hardware")]
  public async Task RealDevice_StreamsIqSamples_WithinTimeout()
  {
    var devices = SdrDeviceFactory.EnumerateDevices();
    var rtlInfo = devices.FirstOrDefault(d => d.Type == DeviceType.RTLSDR && d.IsAvailable);
    Assert.NotNull(rtlInfo);

    var device = SdrDeviceFactory.CreateDevice(rtlInfo);
    Assert.True(device.Open(), "Failed to open real RTL-SDR device");

    try
    {
      device.SetSampleRate(2_400_000);
      device.SetFrequency(94_700_000); // FM broadcast

      var samplesTcs = new TaskCompletionSource<IqSample[]>();
      device.SamplesAvailable += (_, e) =>
      {
        if (!samplesTcs.Task.IsCompleted)
          samplesTcs.TrySetResult(e.Samples);
      };

      Assert.True(device.StartStreaming(), "Failed to start streaming");

      var samples = await samplesTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

      device.StopStreaming();

      var rms = Math.Sqrt(samples.Average(s => s.I * s.I + s.Q * s.Q));
      _output.WriteLine($"Real device IQ samples: {samples.Length}");
      _output.WriteLine($"IQ RMS: {rms:F6}");
      Assert.True(samples.Length > 0, "No IQ samples from real device");
    }
    finally
    {
      device.Close();
    }
  }

  // ── Test 7: Full mock pipeline end-to-end ──

  [Fact]
  public async Task FullMockPipeline_ProducesNonZeroAudio_EndToEnd()
  {
    // Stage 1: MockSdrDevice generates IQ
    _mockDevice = new MockSdrDevice();
    _mockDevice.AddSimulatedSignal(94_700_000, 0.9f); // Strong signal

    // Stage 2: RadioReceiver demodulates
    _receiver = new RadioReceiver(_mockDevice);
    _receiver.SetFrequency(94_700_000);
    _receiver.SquelchThreshold = 0.0f;

    // Stage 3: Simulate BufferedSoundGenerator with a simple queue
    var audioQueue = new Queue<float>();
    var queueLock = new object();
    long totalEnqueued = 0;
    var enoughDataTcs = new TaskCompletionSource<bool>();

    _receiver.AudioDataAvailable += (_, e) =>
    {
      lock (queueLock)
      {
        foreach (var s in e.Samples)
          audioQueue.Enqueue(s);
        totalEnqueued += e.Samples.Length;
        if (totalEnqueued > 4800 && !enoughDataTcs.Task.IsCompleted) // ~100ms at 48kHz
          enoughDataTcs.TrySetResult(true);
      }
    };

    // Start pipeline
    _receiver.Startup();

    try
    {
      await enoughDataTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

      // Stage 4: Simulate GenerateAudio pull — dequeue into output buffer
      float[] outputBuffer;
      lock (queueLock)
      {
        var count = Math.Min(audioQueue.Count, 4800);
        outputBuffer = new float[count];
        for (int i = 0; i < count; i++)
          outputBuffer[i] = audioQueue.Dequeue();
      }

      // Analyze output
      var rms = Math.Sqrt(outputBuffer.Average(s => s * s));
      var rmsDb = 20 * Math.Log10(Math.Max(rms, 1e-10));
      var peak = outputBuffer.Max(s => Math.Abs(s));
      var nonZeroCount = outputBuffer.Count(s => s != 0f);
      var nonZeroPercent = 100.0 * nonZeroCount / outputBuffer.Length;

      _output.WriteLine("=== End-to-End Pipeline Results ===");
      _output.WriteLine($"Total samples enqueued: {totalEnqueued:N0}");
      _output.WriteLine($"Output buffer size: {outputBuffer.Length}");
      _output.WriteLine($"Non-zero samples: {nonZeroCount}/{outputBuffer.Length} ({nonZeroPercent:F1}%)");
      _output.WriteLine($"RMS: {rms:F6} ({rmsDb:F1} dB)");
      _output.WriteLine($"Peak: {peak:F6}");
      _output.WriteLine($"Signal strength: {_receiver.SignalStrength:F4}");

      Assert.True(outputBuffer.Length > 0, "No audio data in output buffer");
      Assert.True(rms > 1e-6, $"Output audio is silent (RMS={rms:E2})");
      Assert.True(nonZeroPercent > 50, $"Too many zero samples ({nonZeroPercent:F1}% non-zero)");
    }
    catch (TimeoutException)
    {
      _output.WriteLine("TIMEOUT: Pipeline did not produce enough audio in 5 seconds");
      _output.WriteLine($"Samples enqueued so far: {totalEnqueued}");
      _output.WriteLine($"Receiver state: {_receiver.GetRadioState().State}");
      _output.WriteLine($"Signal strength: {_receiver.SignalStrength}");
      Assert.Fail("End-to-end pipeline timed out — audio is not flowing");
    }
  }
}
