using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using RTLSDRCore.Models;
using Xunit;

namespace RTLSDRCore.Tests;

public class MockSdrDeviceTests
{
    [Fact]
    public void Constructor_CreatesDeviceWithDefaultInfo()
    {
        using var device = new MockSdrDevice();

        Assert.NotNull(device.DeviceInfo);
        Assert.Equal(DeviceType.Mock, device.DeviceInfo.Type);
        Assert.Equal("Mock SDR Device", device.DeviceInfo.Name);
        Assert.True(device.DeviceInfo.IsAvailable);
    }

    [Fact]
    public void Open_WhenNotOpen_ReturnsTrue()
    {
        using var device = new MockSdrDevice();

        var result = device.Open();

        Assert.True(result);
        Assert.True(device.IsOpen);
    }

    [Fact]
    public void Open_WhenAlreadyOpen_ReturnsFalse()
    {
        using var device = new MockSdrDevice();
        device.Open();

        var result = device.Open();

        Assert.False(result);
    }

    [Fact]
    public void Close_WhenOpen_ClosesDevice()
    {
        using var device = new MockSdrDevice();
        device.Open();

        device.Close();

        Assert.False(device.IsOpen);
    }

    [Fact]
    public void SetFrequency_WithinRange_ReturnsTrue()
    {
        using var device = new MockSdrDevice();
        device.Open();

        var result = device.SetFrequency(100_000_000);

        Assert.True(result);
        Assert.Equal(100_000_000, device.GetFrequency());
    }

    [Fact]
    public void SetFrequency_BelowMinimum_ReturnsFalse()
    {
        using var device = new MockSdrDevice();
        device.Open();

        var result = device.SetFrequency(1_000_000); // Below 24 MHz minimum

        Assert.False(result);
    }

    [Fact]
    public void SetFrequency_AboveMaximum_ReturnsFalse()
    {
        using var device = new MockSdrDevice();
        device.Open();

        var result = device.SetFrequency(2_000_000_000); // Above 1.766 GHz maximum

        Assert.False(result);
    }

    [Fact]
    public void SetSampleRate_SupportedRate_ReturnsTrue()
    {
        using var device = new MockSdrDevice();
        device.Open();

        var result = device.SetSampleRate(2_400_000);

        Assert.True(result);
        Assert.Equal(2_400_000, device.GetSampleRate());
    }

    [Fact]
    public void SetSampleRate_UnsupportedRate_ReturnsFalse()
    {
        using var device = new MockSdrDevice();
        device.Open();

        var result = device.SetSampleRate(999_999);

        Assert.False(result);
    }

    [Fact]
    public void SetGainMode_Automatic_ReturnsTrue()
    {
        using var device = new MockSdrDevice();
        device.Open();

        var result = device.SetGainMode(automatic: true);

        Assert.True(result);
    }

    [Fact]
    public void SetGain_ValidGain_ReturnsTrue()
    {
        using var device = new MockSdrDevice();
        device.Open();

        var result = device.SetGain(20.0f);

        Assert.True(result);
        // Note: gain is snapped to nearest supported value
    }

    [Fact]
    public void StartStreaming_WhenOpen_ReturnsTrue()
    {
        using var device = new MockSdrDevice();
        device.Open();

        var result = device.StartStreaming();

        Assert.True(result);
        Assert.True(device.IsStreaming);

        device.StopStreaming();
    }

    [Fact]
    public void StartStreaming_WhenNotOpen_ReturnsFalse()
    {
        using var device = new MockSdrDevice();

        var result = device.StartStreaming();

        Assert.False(result);
    }

    [Fact]
    public void StopStreaming_WhenStreaming_StopsDevice()
    {
        using var device = new MockSdrDevice();
        device.Open();
        device.StartStreaming();

        device.StopStreaming();

        Assert.False(device.IsStreaming);
    }

    [Fact]
    public void AddSimulatedSignal_AddsSignalAtFrequency()
    {
        using var device = new MockSdrDevice();

        device.AddSimulatedSignal(145_000_000, 0.8f);

        var strength = device.GetSignalStrengthAtFrequency(145_000_000);
        Assert.True(strength > 0.5f);
    }

    [Fact]
    public void GetSignalStrengthAtFrequency_NoSignal_ReturnsLow()
    {
        using var device = new MockSdrDevice();

        var strength = device.GetSignalStrengthAtFrequency(500_000_000);

        Assert.True(strength < 0.1f);
    }

    [Fact]
    public void SamplesAvailable_WhenStreaming_RaisesEvent()
    {
        using var device = new MockSdrDevice();
        device.Open();

        var eventRaised = false;
        device.SamplesAvailable += (s, e) => eventRaised = true;

        device.StartStreaming();
        Thread.Sleep(100); // Wait for samples

        Assert.True(eventRaised);

        device.StopStreaming();
    }

    [Fact]
    public void ReadSamples_ReturnsData()
    {
        using var device = new MockSdrDevice();
        device.Open();

        var buffer = new IqSample[1024];
        var count = device.ReadSamples(buffer);

        Assert.Equal(1024, count);
    }

    [Fact]
    public void Dispose_ClosesDevice()
    {
        var device = new MockSdrDevice();
        device.Open();
        device.StartStreaming();

        device.Dispose();

        Assert.False(device.IsOpen);
        Assert.False(device.IsStreaming);
    }
}
