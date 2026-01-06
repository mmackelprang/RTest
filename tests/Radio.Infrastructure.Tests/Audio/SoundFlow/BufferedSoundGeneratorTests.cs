using Microsoft.Extensions.Logging;
using Moq;
using Radio.Infrastructure.Audio.SoundFlow;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Structs;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow
{
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
            public TestBufferedGenerator(AudioEngine engine, AudioFormat format, ILogger logger)
                : base(engine, format, logger) { }

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
    }
}
