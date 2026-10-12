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
        Assert.Contains(output.Skip(50), s => Math.Abs(s.I) > 0.1f);
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

    // AUD-101 (review L6): the feed-forward AgcProcessor, driven directly.

    private const int AgcRate = 48_000;

    private static AgcProcessor AmAgc() => new()
    {
        TargetLevel = 0.5f,
        AttackRate = AgcProcessor.CoefficientFor(0.005, AgcRate),
        DecayRate = AgcProcessor.CoefficientFor(1.0, AgcRate),
        MaxGain = 30f,
    };

    private static float[] Constant(int count, float level) => Enumerable.Repeat(level, count).ToArray();

    [Fact]
    public void AgcProcessor_SeedsTheEnvelope_FromTheFirstBlock()
    {
        var agc = AmAgc();
        // 48 whole cycles of a 1 kHz sine of amplitude 0.1: RMS 0.0707, so the seed (sqrt2 x RMS) is 0.1.
        var input = new float[AgcRate / 1000 * 48];
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = 0.1f * (float)Math.Sin(2 * Math.PI * 1000 * i / AgcRate);
        }
        var output = new float[input.Length];

        agc.Process(input, output);

        // Already at the target in the first cycle: not passed at the maximum gain (30 -> clipped at 1)
        // while the envelope climbs from zero.
        var firstCyclePeak = output.Take(AgcRate / 1000).Max(Math.Abs);
        Assert.InRange(firstCyclePeak, 0.45f, 0.55f);
        Assert.InRange(agc.Envelope, 0.095f, 0.105f);
    }

    [Fact]
    public void AgcProcessor_AttacksWithItsTimeConstant()
    {
        var agc = AmAgc();
        agc.Process(Constant(1, 0.1f), new float[1]);
        var e0 = agc.Envelope;

        // One attack time constant (5 ms = 240 samples) of a constant 0.5.
        agc.Process(Constant(240, 0.5f), new float[240]);

        var expected = 0.5 - (0.5 - e0) * Math.Exp(-1);
        Assert.Equal(expected, agc.Envelope, 2e-3);
        Assert.Equal(0.5f / agc.Envelope, agc.CurrentGain, 1e-4f);
    }

    [Fact]
    public void AgcProcessor_DecaysWithItsTimeConstant()
    {
        var agc = AmAgc();
        // Seeded at sqrt2 x (0.5 / sqrt2) = 0.5.
        agc.Process(Constant(1, 0.5f / MathF.Sqrt(2f)), new float[1]);
        var e0 = agc.Envelope;
        Assert.InRange(e0, 0.499f, 0.501f);

        // One decay time constant (1 s) of a constant 0.05.
        agc.Process(Constant(AgcRate, 0.05f), new float[AgcRate]);

        var expected = 0.05 + (e0 - 0.05) * Math.Exp(-1);
        Assert.Equal(expected, agc.Envelope, 2e-3);
    }

    [Fact]
    public void AgcProcessor_HoldsAtItsMaximumGain_OnAWeakInput()
    {
        var agc = AmAgc();
        var input = Constant(100, 1e-4f);
        var output = new float[100];

        agc.Process(input, output);

        // 0.5 / 1e-4 = 5000 is clamped to 30.
        Assert.Equal(30f, agc.CurrentGain);
        Assert.All(output, v => Assert.Equal(1e-4f * 30f, v, 1e-9f));
    }

    [Fact]
    public void AgcProcessor_Reset_SeedsAgainFromTheNextBlock()
    {
        var agc = AmAgc();
        agc.Process(Constant(2_400, 0.5f), new float[2_400]);

        agc.Reset();
        // A constant 0.01 seeds sqrt2 x 0.01, not the 0.5 the envelope held before the reset.
        agc.Process(Constant(1, 0.01f), new float[1]);

        Assert.InRange(agc.Envelope, 0.0141f, 0.0142f);
    }
}
