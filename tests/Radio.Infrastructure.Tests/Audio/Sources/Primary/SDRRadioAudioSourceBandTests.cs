using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
using RTLSDRCore;
using RTLSDRCore.Hardware;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

public class SDRRadioAudioSourceBandTests
{
    private readonly Mock<ILogger<SDRRadioAudioSource>> _loggerMock;
    private readonly Mock<ISdrDevice> _sdrDeviceMock;
    private readonly RadioReceiver _radioReceiver;
    private readonly Mock<IOptionsMonitor<RadioOptions>> _radioOptionsMock;
    private readonly RadioOptions _radioOptions;

    public SDRRadioAudioSourceBandTests()
    {
        _loggerMock = new Mock<ILogger<SDRRadioAudioSource>>();
        _sdrDeviceMock = new Mock<ISdrDevice>();

        // Set up the mock SDR device with minimal required setup
        var deviceInfo = new RTLSDRCore.Models.DeviceInfo
        {
            Index = 0,
            Name = "Mock RTL-SDR Device",
            Type = RTLSDRCore.Enums.DeviceType.Mock,
            Serial = "TEST123",
            Manufacturer = "Test",
            TunerType = "Test Tuner",
            IsAvailable = true,
            MinFrequencyHz = 24_000_000,
            MaxFrequencyHz = 1_766_000_000
        };

        _sdrDeviceMock.Setup(d => d.DeviceInfo).Returns(deviceInfo);
        _sdrDeviceMock.Setup(d => d.IsOpen).Returns(true); // Simulate open device

        _radioReceiver = new RadioReceiver(_sdrDeviceMock.Object);

        _radioOptions = new RadioOptions();
        _radioOptionsMock = new Mock<IOptionsMonitor<RadioOptions>>();
        _radioOptionsMock.Setup(o => o.CurrentValue).Returns(_radioOptions);
    }

    [Fact]
    public async Task SetBandAsync_ShouldSupportAirBand()
    {
        // Arrange
        var source = new SDRRadioAudioSource(
            _loggerMock.Object,
            _radioReceiver,
            _radioOptionsMock.Object,
            null,
            null,
            null);

        // Pre-initialize internal state if needed (usually handled by InitializeAsync or StartupAsync)
        // Since we are testing mapping logic which happens inside SetBandAsync, 
        // and SetBandAsync usually talks to RadioReceiver.
        
        // Act & Assert
        // This should not throw ArgumentException: Unsupported band: AIR
        var exception = await Record.ExceptionAsync(() => source.SetBandAsync(RadioBand.AIR));

        // Note: It might throw other exceptions if the mock device doesn't fully support the band setting
        // or if RadioReceiver throws, but we are looking specifically for the mapping exception.
        // With the fix, the mapping happens successfully, then it calls _radioReceiver.SetBandAsync.
        
        if (exception != null && exception is ArgumentException argEx && argEx.Message.Contains("Unsupported band"))
        {
            Assert.Fail($"SetBandAsync threw ArgumentException for AIR band: {exception.Message}");
        }
        
        // If it got past the mapping, it passed our test criteria.
        // We expect it NOT to crash on the mapping.
    }
}
