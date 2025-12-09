using RTLSDRCore.Enums;
using RTLSDRCore.Models;
using Xunit;

namespace RTLSDRCore.Tests;

public class ModelsTests
{
    public class IqSampleTests
    {
        [Fact]
        public void Magnitude_CalculatesCorrectly()
        {
            var sample = new IqSample(3, 4);

            Assert.Equal(5, sample.Magnitude, 4);
        }

        [Fact]
        public void MagnitudeSquared_CalculatesCorrectly()
        {
            var sample = new IqSample(3, 4);

            Assert.Equal(25, sample.MagnitudeSquared, 4);
        }

        [Fact]
        public void Phase_CalculatesCorrectly()
        {
            var sample = new IqSample(1, 1);

            Assert.Equal(MathF.PI / 4, sample.Phase, 4);
        }

        [Fact]
        public void Addition_WorksCorrectly()
        {
            var a = new IqSample(1, 2);
            var b = new IqSample(3, 4);

            var result = a + b;

            Assert.Equal(4, result.I);
            Assert.Equal(6, result.Q);
        }

        [Fact]
        public void Subtraction_WorksCorrectly()
        {
            var a = new IqSample(3, 4);
            var b = new IqSample(1, 2);

            var result = a - b;

            Assert.Equal(2, result.I);
            Assert.Equal(2, result.Q);
        }

        [Fact]
        public void ComplexMultiplication_WorksCorrectly()
        {
            var a = new IqSample(1, 2);
            var b = new IqSample(3, 4);

            var result = a * b;

            // (1+2j) * (3+4j) = 3 + 4j + 6j + 8j^2 = 3 + 10j - 8 = -5 + 10j
            Assert.Equal(-5, result.I, 4);
            Assert.Equal(10, result.Q, 4);
        }

        [Fact]
        public void ScalarMultiplication_WorksCorrectly()
        {
            var sample = new IqSample(2, 3);

            var result = sample * 2;

            Assert.Equal(4, result.I);
            Assert.Equal(6, result.Q);
        }

        [Fact]
        public void Conjugate_WorksCorrectly()
        {
            var sample = new IqSample(2, 3);

            var conj = sample.Conjugate;

            Assert.Equal(2, conj.I);
            Assert.Equal(-3, conj.Q);
        }

        [Fact]
        public void Normalize_CreatesUnitMagnitude()
        {
            var sample = new IqSample(3, 4);

            var normalized = sample.Normalize();

            Assert.Equal(1, normalized.Magnitude, 4);
        }

        [Fact]
        public void FromPolar_CreatesCorrectSample()
        {
            var sample = IqSample.FromPolar(5, MathF.PI / 4);

            Assert.Equal(5 * MathF.Cos(MathF.PI / 4), sample.I, 3);
            Assert.Equal(5 * MathF.Sin(MathF.PI / 4), sample.Q, 3);
        }
    }

    public class RadioBandTests
    {
        [Fact]
        public void ContainsFrequency_InRange_ReturnsTrue()
        {
            var band = new RadioBand
            {
                MinFrequencyHz = 87_500_000,
                MaxFrequencyHz = 108_000_000
            };

            Assert.True(band.ContainsFrequency(98_500_000));
        }

        [Fact]
        public void ContainsFrequency_OutOfRange_ReturnsFalse()
        {
            var band = new RadioBand
            {
                MinFrequencyHz = 87_500_000,
                MaxFrequencyHz = 108_000_000
            };

            Assert.False(band.ContainsFrequency(50_000_000));
        }

        [Fact]
        public void ClampFrequency_BelowMin_ReturnsMin()
        {
            var band = new RadioBand
            {
                MinFrequencyHz = 87_500_000,
                MaxFrequencyHz = 108_000_000
            };

            var clamped = band.ClampFrequency(50_000_000);

            Assert.Equal(87_500_000, clamped);
        }

        [Fact]
        public void ClampFrequency_AboveMax_ReturnsMax()
        {
            var band = new RadioBand
            {
                MinFrequencyHz = 87_500_000,
                MaxFrequencyHz = 108_000_000
            };

            var clamped = band.ClampFrequency(150_000_000);

            Assert.Equal(108_000_000, clamped);
        }

        [Fact]
        public void CenterFrequencyHz_CalculatesCorrectly()
        {
            var band = new RadioBand
            {
                MinFrequencyHz = 87_500_000,
                MaxFrequencyHz = 108_000_000
            };

            Assert.Equal(97_750_000, band.CenterFrequencyHz);
        }

        [Theory]
        [InlineData(100_000_000_000, "100.000 GHz")]
        [InlineData(100_000_000, "100.000 MHz")]
        [InlineData(100_000, "100.0 kHz")]
        [InlineData(100, "100 Hz")]
        public void FormatFrequency_FormatsCorrectly(long frequency, string expected)
        {
            var result = RadioBand.FormatFrequency(frequency);

            Assert.Equal(expected, result);
        }
    }

    public class AudioFormatTests
    {
        [Fact]
        public void Default_Returns48kHzMono()
        {
            var format = AudioFormat.Default;

            Assert.Equal(48000, format.SampleRate);
            Assert.Equal(1, format.Channels);
            Assert.Equal(16, format.BitsPerSample);
        }

        [Fact]
        public void ByteRate_CalculatesCorrectly()
        {
            var format = new AudioFormat
            {
                SampleRate = 48000,
                Channels = 2,
                BitsPerSample = 16
            };

            Assert.Equal(192000, format.ByteRate); // 48000 * 2 * 2
        }

        [Fact]
        public void BlockAlign_CalculatesCorrectly()
        {
            var format = new AudioFormat
            {
                SampleRate = 48000,
                Channels = 2,
                BitsPerSample = 16
            };

            Assert.Equal(4, format.BlockAlign); // 2 * 2
        }
    }

    public class RadioStateTests
    {
        [Fact]
        public void Clone_CreatesDeepCopy()
        {
            var state = new RadioState
            {
                FrequencyHz = 98_500_000,
                CurrentBand = BandType.FM,
                Modulation = ModulationType.WFM,
                State = ReceiverState.Running,
                SignalStrength = 0.8f,
                Volume = 0.5f
            };

            var clone = state.Clone();

            Assert.Equal(state.FrequencyHz, clone.FrequencyHz);
            Assert.Equal(state.CurrentBand, clone.CurrentBand);
            Assert.Equal(state.Volume, clone.Volume);

            // Modify original and verify clone is independent
            state.FrequencyHz = 99_000_000;
            Assert.NotEqual(state.FrequencyHz, clone.FrequencyHz);
        }

        [Fact]
        public void FrequencyDisplay_FormatsCorrectly()
        {
            var state = new RadioState { FrequencyHz = 98_500_000 };

            Assert.Equal("98.500 MHz", state.FrequencyDisplay);
        }
    }

    public class DeviceInfoTests
    {
        [Fact]
        public void ToString_IncludesRelevantInfo()
        {
            var info = new DeviceInfo
            {
                Index = 0,
                Name = "RTL-SDR",
                Manufacturer = "Generic",
                TunerType = "R820T"
            };

            var result = info.ToString();

            Assert.Contains("RTL-SDR", result);
            Assert.Contains("Generic", result);
        }
    }
}
