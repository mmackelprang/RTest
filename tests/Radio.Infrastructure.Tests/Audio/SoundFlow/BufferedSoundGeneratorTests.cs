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
  }
