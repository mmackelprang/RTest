using RTLSDRCore.DSP;
using RTLSDRCore.Models;
using Xunit;

namespace RTLSDRCore.Tests;

public class FiltersTests
{
    [Fact]
    public void LowPassFilter_AttenuatesHighFrequencies()
    {
        var filter = new LowPassFilter(48000, 1000, 63);
        var input = new float[1000];
        var output = new float[1000];

        // Generate high frequency signal (10 kHz)
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = (float)Math.Sin(2 * Math.PI * 10000 * i / 48000);
        }

        filter.Process(input, output);

        // Output should be attenuated
        var inputPower = input.Average(x => x * x);
        var outputPower = output.Skip(100).Average(x => x * x); // Skip initial transient

        Assert.True(outputPower < inputPower * 0.1); // At least 10x attenuation
    }

    [Fact]
    public void LowPassFilter_PassesLowFrequencies()
    {
        var filter = new LowPassFilter(48000, 5000, 63);
        var input = new float[1000];
        var output = new float[1000];

        // Generate low frequency signal (100 Hz)
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = (float)Math.Sin(2 * Math.PI * 100 * i / 48000);
        }

        filter.Process(input, output);

        // Output should be similar to input (after initial transient)
        var inputPower = input.Skip(100).Average(x => x * x);
        var outputPower = output.Skip(100).Average(x => x * x);

        Assert.True(outputPower > inputPower * 0.5); // At least 50% power retained
    }

    [Fact]
    public void LowPassFilter_Reset_ClearsState()
    {
        var filter = new LowPassFilter(48000, 1000, 63);
        var input = new float[] { 1, 0, 0, 0, 0 };
        var output = new float[5];

        filter.Process(input, output);
        filter.Reset();

        var output2 = new float[5];
        filter.Process(input, output2);

        // After reset, output should be the same
        for (var i = 0; i < output.Length; i++)
        {
            Assert.Equal(output[i], output2[i], 4);
        }
    }

    [Fact]
    public void IqLowPassFilter_FiltersIqSignals()
    {
        var filter = new IqLowPassFilter(2_400_000, 100_000, 31);
        var input = new IqSample[1000];
        var output = new IqSample[1000];

        // Generate signal
        for (var i = 0; i < input.Length; i++)
        {
            var phase = 2 * Math.PI * 50000 * i / 2_400_000;
            input[i] = new IqSample((float)Math.Cos(phase), (float)Math.Sin(phase));
        }

        filter.Process(input, output);

        // Output should have valid values
        Assert.True(output.Skip(50).Any(s => Math.Abs(s.I) > 0.1f));
    }

    [Fact]
    public void Decimator_ReducesSampleRate()
    {
        var decimator = new Decimator(2_400_000, 50); // Decimate to 48 kHz
        var input = new IqSample[2400];
        var output = new IqSample[100];

        // Fill with test signal
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = new IqSample(1.0f, 0.0f);
        }

        var count = decimator.Decimate(input, output);

        Assert.Equal(48, count); // 2400 / 50 = 48
        Assert.Equal(48000, decimator.OutputSampleRate);
    }

    [Fact]
    public void AgcProcessor_NormalizesSignal()
    {
        var agc = new AgcProcessor();
        agc.TargetLevel = 0.5f;

        var input = new float[1000];
        var output = new float[1000];

        // Generate low level signal
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = 0.01f * (float)Math.Sin(2 * Math.PI * 1000 * i / 48000);
        }

        agc.Process(input, output);

        // Output should be boosted
        var inputPeak = input.Max(Math.Abs);
        var outputPeak = output.Skip(100).Max(Math.Abs);

        Assert.True(outputPeak > inputPeak);
    }

    [Fact]
    public void AgcProcessor_LimitsHighSignals()
    {
        var agc = new AgcProcessor();
        agc.TargetLevel = 0.5f;

        var input = new float[1000];
        var output = new float[1000];

        // Generate high level signal
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = 2.0f * (float)Math.Sin(2 * Math.PI * 1000 * i / 48000);
        }

        agc.Process(input, output);

        // Output should be clipped to -1 to 1 range
        Assert.True(output.All(s => s >= -1.0f && s <= 1.0f));
    }

    [Fact]
    public void AgcProcessor_Reset_ClearsState()
    {
        var agc = new AgcProcessor();
        var input = new float[] { 1, 1, 1, 1, 1 };
        var output = new float[5];

        agc.Process(input, output);
        var gainBefore = agc.CurrentGain;

        agc.Reset();

        Assert.Equal(1.0f, agc.CurrentGain);
    }
}
