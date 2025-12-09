using RTLSDRCore.DSP;
using RTLSDRCore.Enums;
using RTLSDRCore.Models;
using Xunit;

namespace RTLSDRCore.Tests;

public class DemodulatorTests
{
    [Fact]
    public void AmDemodulator_Demodulates()
    {
        var demodulator = new AmDemodulator();
        var input = GenerateAmSignal(1000, 2_400_000, 1024);
        var output = new float[1024];

        var count = demodulator.Demodulate(input, output);

        Assert.Equal(1024, count);
        Assert.Contains(output, s => Math.Abs(s) > 0.001f);
    }

    [Fact]
    public void FmDemodulator_Demodulates()
    {
        var demodulator = new FmDemodulator();
        var input = GenerateFmSignal(1000, 2_400_000, 1024);
        var output = new float[1024];

        var count = demodulator.Demodulate(input, output);

        Assert.Equal(1024, count);
    }

    [Fact]
    public void WfmDemodulator_HasCorrectBandwidth()
    {
        var demodulator = new WfmDemodulator();

        Assert.Equal(200_000, demodulator.Bandwidth);
        Assert.Equal(75_000, demodulator.MaxDeviation);
    }

    [Fact]
    public void NfmDemodulator_HasCorrectBandwidth()
    {
        var demodulator = new NfmDemodulator();

        Assert.Equal(12_500, demodulator.Bandwidth);
        Assert.Equal(5_000, demodulator.MaxDeviation);
    }

    [Theory]
    [InlineData(ModulationType.AM)]
    [InlineData(ModulationType.NFM)]
    [InlineData(ModulationType.WFM)]
    [InlineData(ModulationType.USB)]
    [InlineData(ModulationType.LSB)]
    [InlineData(ModulationType.RAW)]
    public void DemodulatorFactory_CreatesCorrectDemodulator(ModulationType modulation)
    {
        var demodulator = DemodulatorFactory.Create(modulation);

        Assert.NotNull(demodulator);
    }

    [Fact]
    public void DemodulatorFactory_GetRecommendedBandwidth_ReturnsCorrectValues()
    {
        Assert.Equal(10_000, DemodulatorFactory.GetRecommendedBandwidth(ModulationType.AM));
        Assert.Equal(12_500, DemodulatorFactory.GetRecommendedBandwidth(ModulationType.NFM));
        Assert.Equal(200_000, DemodulatorFactory.GetRecommendedBandwidth(ModulationType.WFM));
        Assert.Equal(3_000, DemodulatorFactory.GetRecommendedBandwidth(ModulationType.USB));
        Assert.Equal(3_000, DemodulatorFactory.GetRecommendedBandwidth(ModulationType.LSB));
    }

    [Fact]
    public void AmDemodulator_Reset_ClearsState()
    {
        var demodulator = new AmDemodulator();
        var input = GenerateAmSignal(1000, 2_400_000, 1024);
        var output = new float[1024];
        demodulator.Demodulate(input, output);

        demodulator.Reset();

        // Should not throw and should work again
        var output2 = new float[1024];
        var count = demodulator.Demodulate(input, output2);
        Assert.Equal(1024, count);
    }

    [Fact]
    public void FmDemodulator_Reset_ClearsState()
    {
        var demodulator = new FmDemodulator();
        var input = GenerateFmSignal(1000, 2_400_000, 1024);
        var output = new float[1024];
        demodulator.Demodulate(input, output);

        demodulator.Reset();

        var output2 = new float[1024];
        var count = demodulator.Demodulate(input, output2);
        Assert.Equal(1024, count);
    }

    private static IqSample[] GenerateAmSignal(int audioFrequency, int sampleRate, int samples)
    {
        var result = new IqSample[samples];
        var carrierPhase = 0.0;
        var audioPhase = 0.0;
        var carrierFreq = 10_000.0; // 10 kHz carrier

        for (var i = 0; i < samples; i++)
        {
            var audioSample = Math.Sin(audioPhase);
            var modulated = (1 + 0.5 * audioSample); // 50% modulation depth

            var carrier = modulated * Math.Cos(carrierPhase);
            var quadrature = modulated * Math.Sin(carrierPhase);

            result[i] = new IqSample((float)carrier, (float)quadrature);

            carrierPhase += 2 * Math.PI * carrierFreq / sampleRate;
            audioPhase += 2 * Math.PI * audioFrequency / sampleRate;
        }

        return result;
    }

    private static IqSample[] GenerateFmSignal(int audioFrequency, int sampleRate, int samples)
    {
        var result = new IqSample[samples];
        var phase = 0.0;
        var audioPhase = 0.0;
        var deviation = 75_000.0; // Max frequency deviation

        for (var i = 0; i < samples; i++)
        {
            var audioSample = Math.Sin(audioPhase);
            var instantFreq = deviation * audioSample;

            result[i] = new IqSample(
                (float)Math.Cos(phase),
                (float)Math.Sin(phase));

            phase += 2 * Math.PI * instantFreq / sampleRate;
            audioPhase += 2 * Math.PI * audioFrequency / sampleRate;
        }

        return result;
    }
}
