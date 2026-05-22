using Microsoft.Extensions.Logging;
using Moq;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Metrics;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Structs;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

  public class BufferedSoundGeneratorTests
  {
      private readonly Mock<ILogger> _loggerMock;
      private readonly Mock<AudioEngine> _engineMock;

      public BufferedSoundGeneratorTests()
      {
          _loggerMock = new Mock<ILogger>();
          _engineMock = new Mock<AudioEngine>();
      }

      // Subclass to expose protected method
      private class TestBufferedGenerator<T> : BufferedSoundGenerator<T> where T : struct
      {
          public TestBufferedGenerator(AudioEngine engine, AudioFormat format, ILogger logger,
              IMetricsCollector? metricsCollector = null)
              : base(engine, format, logger, metricsCollector: metricsCollector) { }

          public int Read(Span<float> buffer)
          {
              // Simulate SoundFlow Read: call GenerateAudio
              // Assuming SoundComponent.Read does this.
              // Since we can't call base.Read if it's not accessible, we call GenerateAudio directly.
              // GenerateAudio returns void, but Read returns int.
              // BufferedSoundGenerator implementation of GenerateAudio fills the buffer.
              // It doesn't return count.
              // So we assume it fills as much as possible up to buffer.Length.

              GenerateAudio(buffer, Format.Channels);
              return buffer.Length; // Approximation
          }
      }

      [Fact]
      public void AddSamples_Short_ConvertsToFloat()
      {
          // Arrange
          var format = new AudioFormat { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 };
          var generator = new TestBufferedGenerator<short>(_engineMock.Object, format, _loggerMock.Object);

          short[] input = { 32767, -32768, 0, 16384 }; // max, min, zero, half
          generator.AddSamples(input);

          float[] output = new float[4];
          
          // Act
          int read = generator.Read(output);

          // Assert
          Assert.Equal(4, read);
          
          // Check conversion (approximate due to float precision)
          Assert.Equal(32767f / 32768f, output[0], 0.0001f);
          Assert.Equal(-1f, output[1], 0.0001f);
          Assert.Equal(0f, output[2], 0.0001f);
          Assert.Equal(16384f / 32768f, output[3], 0.0001f);
      }

      [Fact]
      public void AddSamples_Float_PassesThrough()
      {
          // Arrange
          var format = new AudioFormat { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 };
          var generator = new TestBufferedGenerator<float>(_engineMock.Object, format, _loggerMock.Object);

          float[] input = { 1.0f, -1.0f, 0.5f, -0.5f };
          generator.AddSamples(input);

          float[] output = new float[4];
          
          // Act
          int read = generator.Read(output);

          // Assert
          Assert.Equal(4, read);
          Assert.Equal(1.0f, output[0]);
          Assert.Equal(-1.0f, output[1]);
          Assert.Equal(0.5f, output[2]);
          Assert.Equal(-0.5f, output[3]);
      }
      
      [Fact]
      public void Read_WithEmptyBuffer_ReturnsSilence()
      {
          // Arrange
          var format = new AudioFormat { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 };
          var generator = new TestBufferedGenerator<float>(_engineMock.Object, format, _loggerMock.Object);

          float[] output = new float[4];

          // Act
          int read = generator.Read(output);

          // Assert
          Assert.Equal(4, read);
          Assert.All(output, x => Assert.Equal(0f, x));
      }

      [Fact]
      public void Underrun_IncrementsCounter_OnEachEvent()
      {
          // Arrange
          var format = new AudioFormat { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 };
          var metrics = new Mock<IMetricsCollector>();
          var generator = new TestBufferedGenerator<float>(
              _engineMock.Object, format, _loggerMock.Object, metrics.Object);

          // Seed _totalSamplesReceived > 0 so underruns are counted (not idle).
          generator.AddSamples(new float[] { 0.1f, 0.2f });

          // Drain whatever is in the buffer
          var drain = new float[2];
          generator.Read(drain);

          // Act: 3 reads that all underrun (empty buffer, demanding 8 samples)
          var output = new float[8];
          for (int i = 0; i < 3; i++)
          {
              generator.Read(output);
          }

          // Assert: counter incremented per underrun event (3 events).
          metrics.Verify(
              m => m.Increment("audio.buffer.underrun_total", 1, It.IsAny<IDictionary<string, string>>()),
              Times.Exactly(3));
          // Samples counter incremented with the per-event deficit (8 samples each)
          metrics.Verify(
              m => m.Increment("audio.buffer.underrun_samples_total", 8, It.IsAny<IDictionary<string, string>>()),
              Times.Exactly(3));
      }

      [Fact]
      public void Underrun_LogsAtWarning_WithThrottle()
      {
          // Arrange
          var format = new AudioFormat { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 };
          var generator = new TestBufferedGenerator<float>(
              _engineMock.Object, format, _loggerMock.Object);

          // Seed _totalSamplesReceived > 0
          generator.AddSamples(new float[] { 0.1f, 0.2f });
          var drain = new float[2];
          generator.Read(drain);

          // Act: trigger an underrun
          var output = new float[8];
          generator.Read(output);

          // Assert: the underrun log is at Warning level (existing behavior).
          _loggerMock.Verify(
              l => l.Log(
                  LogLevel.Warning,
                  It.IsAny<EventId>(),
                  It.Is<It.IsAnyType>((v, _) => v!.ToString()!.Contains("Buffer underrun")),
                  null,
                  It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
              Times.AtLeastOnce);
      }

      [Fact]
      public void DriftCompensation_IncrementsCounter_OnEachEvent()
      {
          // Arrange — small buffer so compensation threshold is reachable in test time
          var format = new AudioFormat { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 };
          var metrics = new Mock<IMetricsCollector>();
          var generator = new TestBufferedGenerator<float>(
              _engineMock.Object, format, _loggerMock.Object, metrics.Object);

          // To trigger CompensateClockDrift we need:
          //  - samplesWritten == buffer.Length (full read) on each call
          //  - DropOldest strategy (default)
          //  - >= 2 seconds elapsed between drift checks
          //  - > 3 drift checks before first compensation
          //  - buffer below 15 % threshold + draining
          //
          // Approach: feed a small amount of audio, read it fully (full reads), wait
          // 2.1 s between checks. Total ≈ 8.5 s for 4 successful drift checks.
          // With default 4s maxBuffer at 48 kHz stereo = 384 000 samples,
          // threshold = 57 600 samples. We feed ~10 000 samples → well under threshold.
          var prefill = new float[10_000];
          for (int i = 0; i < prefill.Length; i++)
          {
              prefill[i] = 0.1f;
          }

          var output = new float[512]; // smaller than prefill so full reads succeed
          int readCount = 0;

          // First drift check: anchor timestamp (no compensation).
          generator.AddSamples(prefill);
          generator.Read(output); readCount++;

          // Wait > 2 s between each subsequent read so the drift-check timer fires.
          for (int check = 0; check < 4; check++)
          {
              Thread.Sleep(2100);
              // Top up just enough to keep level draining but under threshold.
              generator.AddSamples(new float[256]);
              generator.Read(output); readCount++;
          }

          // Assert: compensation counter was incremented at least once.
          // (Exact count varies with timing; >= 1 is the contract we care about.)
          metrics.Verify(
              m => m.Increment(
                  "audio.buffer.drift_compensation_total",
                  1,
                  It.IsAny<IDictionary<string, string>>()),
              Times.AtLeastOnce);

          metrics.Verify(
              m => m.Increment(
                  "audio.buffer.drift_compensation_samples_total",
                  It.Is<double>(v => v > 0),
                  It.IsAny<IDictionary<string, string>>()),
              Times.AtLeastOnce);
      }

      [Fact]
      public void DriftCompensation_LogsAtInformation_WhenTriggered()
      {
          // Arrange
          var format = new AudioFormat { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 };
          var generator = new TestBufferedGenerator<float>(
              _engineMock.Object, format, _loggerMock.Object);

          // Same setup as compensation counter test
          var prefill = new float[10_000];
          for (int i = 0; i < prefill.Length; i++)
          {
              prefill[i] = 0.1f;
          }
          var output = new float[512];

          generator.AddSamples(prefill);
          generator.Read(output);

          for (int check = 0; check < 4; check++)
          {
              Thread.Sleep(2100);
              generator.AddSamples(new float[256]);
              generator.Read(output);
          }

          // Assert: the compensation log is now at Information level (the change being tested).
          _loggerMock.Verify(
              l => l.Log(
                  LogLevel.Information,
                  It.IsAny<EventId>(),
                  It.Is<It.IsAnyType>((v, _) =>
                      v!.ToString()!.Contains("Clock drift compensation")),
                  null,
                  It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
              Times.AtLeastOnce);
      }

      // ---------------------------------------------------------------------
      // Path C refinement coverage
      // (docs/plans/2026-05-22-bt-drift-compensation-refinement.md)
      // ---------------------------------------------------------------------

      [Fact]
      public void CompensateClockDrift_PerCallCap_LimitsToTwoMillisecondsOfSamples()
      {
          // Arrange: a sustained drain scenario where the buffer stays well below the
          // drift threshold for many successive Read calls. We expect each compensation
          // event to be capped at 2 ms (= sample_rate * channels * 0.002 = 192 samples
          // for 48 kHz stereo), frame-aligned. Pre-refinement the cap was 10 ms.
          var format = new AudioFormat { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 };
          var metrics = new Mock<IMetricsCollector>();
          var generator = new TestBufferedGenerator<float>(
              _engineMock.Object, format, _loggerMock.Object, metrics.Object);

          // 2 ms cap at 48 kHz stereo = 192 samples per event.
          const int expectedCapSamples = 48_000 * 2 * 2 / 1000;
          Assert.Equal(192, expectedCapSamples);

          // Capture every drift-compensation samples increment so we can inspect the
          // per-event sizes. Counter is the second argument (a double) of Increment.
          var perEventSamples = new List<double>();
          metrics
              .Setup(m => m.Increment(
                  "audio.buffer.drift_compensation_samples_total",
                  It.IsAny<double>(),
                  It.IsAny<IDictionary<string, string>>()))
              .Callback<string, double, IDictionary<string, string>?>((_, v, _) => perEventSamples.Add(v));

          var prefill = new float[10_000];
          for (int i = 0; i < prefill.Length; i++)
          {
              prefill[i] = 0.1f;
          }
          var output = new float[512];

          generator.AddSamples(prefill);
          generator.Read(output); // anchor _lastDriftCheckTime

          // Tight loop — no Thread.Sleep, because Path C removed the 2-second cooldown.
          // Refill just enough each iteration to keep the drain going under the threshold.
          for (int i = 0; i < 20; i++)
          {
              generator.AddSamples(new float[256]);
              generator.Read(output);
          }

          // At least one compensation event must have fired (>3 drift-checks + draining).
          Assert.NotEmpty(perEventSamples);

          // Critical assertion: every single per-event sample count must be <= cap.
          // No event may exceed the 2 ms cap.
          foreach (var samples in perEventSamples)
          {
              Assert.True(samples <= expectedCapSamples,
                  $"Compensation event of {samples} samples exceeded the 2 ms cap of {expectedCapSamples} samples");
              Assert.True(samples > 0, "Compensation event recorded 0 samples (should not happen)");
          }
      }

      [Fact]
      public void CompensateClockDrift_AppliesCrossfade_OnFloatRingBuffer()
      {
          // Arrange: drive the generator into a compensation event, then inspect the
          // ring buffer through the next Read to confirm the first 32 samples of the
          // duplicated chunk follow a cosine ramp (start near 0, midpoint near 0.5,
          // end approaching original amplitude).
          var format = new AudioFormat { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 };
          var metrics = new Mock<IMetricsCollector>();

          // Track each compensation event so the test can drain just past the first one.
          int compEventCount = 0;
          metrics
              .Setup(m => m.Increment(
                  "audio.buffer.drift_compensation_total",
                  It.IsAny<double>(),
                  It.IsAny<IDictionary<string, string>>()))
              .Callback<string, double, IDictionary<string, string>?>((_, _, _) => compEventCount++);

          var generator = new TestBufferedGenerator<float>(
              _engineMock.Object, format, _loggerMock.Object, metrics.Object);

          // Fill with a uniform 1.0 signal so the ramp is easy to detect:
          // post-crossfade values should equal `1.0 * cosine_gain(i, 32)`.
          var prefill = new float[10_000];
          for (int i = 0; i < prefill.Length; i++)
          {
              prefill[i] = 1.0f;
          }

          generator.AddSamples(prefill);

          // Anchor + warm up _driftCheckCount > 3 + trigger the first compensation.
          // Capture the first read AFTER the compensation event so we observe the
          // crossfaded duplicated chunk at the start of the buffer.
          var output = new float[512];
          generator.Read(output); // anchor

          // Helper: 1.0f top-up keeps the buffer-content uniform so we can verify
          // the post-crossfade ramp against a known constant signal.
          static float[] OnesBlock(int n)
          {
              var b = new float[n];
              Array.Fill(b, 1.0f);
              return b;
          }

          // Loop until a compensation event fires; then do ONE MORE Read so we
          // observe the crossfaded region (which lives at the rewound _readPos —
          // i.e. the first samples the next GenerateAudio call will read).
          bool capturedAfterComp = false;
          float[]? observedAfterCompensation = null;
          for (int i = 0; i < 30 && !capturedAfterComp; i++)
          {
              int beforeComp = compEventCount;
              generator.AddSamples(OnesBlock(256)); // tiny top-up keeps draining
              generator.Read(output);
              if (compEventCount > beforeComp)
              {
                  // Next Read consumes the rewound (and now crossfaded) region first.
                  // Keep adding tiny samples so the buffer has data to read.
                  generator.AddSamples(OnesBlock(512));
                  generator.Read(output);
                  observedAfterCompensation = output.ToArray();
                  capturedAfterComp = true;
              }
          }

          Assert.True(compEventCount >= 1, "Expected at least one compensation event");
          Assert.NotNull(observedAfterCompensation);

          // The crossfaded region is the FIRST 32 samples of the read AFTER the
          // compensation event — these are the samples at the rewound _readPos.
          // Validate the cosine ramp shape on a uniform 1.0 input:
          //   gain(i) = (1 - cos(i/32 * π)) / 2
          //   gain(0)  = 0
          //   gain(16) = (1 - cos(π/2)) / 2 = 0.5
          //   gain(31) = (1 - cos(31π/32)) / 2 ≈ 0.9976
          //   gain(32+) past ramp → 1.0 (untouched original signal)
          //
          // We allow generous slack because the underlying signal is 1.0f, but the
          // ring-buffer ordering between consecutive 256-sample top-ups can fill
          // the seam with one zero frame; the dominant value past the ramp is 1.0.
          Assert.True(observedAfterCompensation[0] < 0.05f,
              $"Crossfade sample[0] expected ~0.0, got {observedAfterCompensation[0]}");
          Assert.InRange(observedAfterCompensation[16], 0.4f, 0.6f);
          Assert.True(observedAfterCompensation[31] > 0.9f,
              $"Crossfade sample[31] expected ~1.0, got {observedAfterCompensation[31]}");
      }

      [Fact]
      public void CompensateClockDrift_NoLongerRunsLessOftenThan2Seconds()
      {
          // The pre-refinement code returned early if (now - _lastDriftCheckTime).TotalSeconds < 2.0.
          // Path C removed that cooldown: compensation must now run on every Process call
          // while the buffer is draining. Verify by calling Process repeatedly within a
          // small wall-clock window and confirming compensation fires multiple times.
          var format = new AudioFormat { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 };
          var metrics = new Mock<IMetricsCollector>();
          var generator = new TestBufferedGenerator<float>(
              _engineMock.Object, format, _loggerMock.Object, metrics.Object);

          var prefill = new float[10_000];
          for (int i = 0; i < prefill.Length; i++)
          {
              prefill[i] = 0.1f;
          }
          var output = new float[512];

          generator.AddSamples(prefill);

          var startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
          // Burn the _driftCheckCount > 3 warm-up reads + several more reads. The total
          // wall time for ~30 Read calls is well under 2 s on any normal machine.
          for (int i = 0; i < 30; i++)
          {
              generator.AddSamples(new float[256]);
              generator.Read(output);
          }
          var elapsedSec = (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks)
              / (double)System.Diagnostics.Stopwatch.Frequency;

          // Sanity-guard: if the machine ran so slowly that >= 2 s elapsed, the test
          // doesn't actually verify the no-cooldown property. Fail loud rather than
          // produce a false PASS.
          Assert.True(elapsedSec < 2.0,
              $"Test loop took {elapsedSec:F2}s — too slow to verify no-cooldown property. "
              + "Run on a less-loaded machine.");

          metrics.Verify(
              m => m.Increment(
                  "audio.buffer.drift_compensation_total",
                  It.IsAny<double>(),
                  It.IsAny<IDictionary<string, string>>()),
              Times.AtLeast(2));
      }
  }
