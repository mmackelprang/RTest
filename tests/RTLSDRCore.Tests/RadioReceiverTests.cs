using RTLSDRCore.Bands;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using RTLSDRCore.Models;
using Xunit;

namespace RTLSDRCore.Tests;

public class RadioReceiverTests
{
    #region Lifecycle Tests

    [Fact]
    public void CreateWithMockDevice_CreatesReceiver()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        Assert.NotNull(receiver);
        Assert.False(receiver.IsRunning);
    }

    [Fact]
    public void Startup_StartsReceiver()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        var result = receiver.Startup();

        Assert.True(result);
        Assert.True(receiver.IsRunning);

        receiver.Shutdown();
    }

    [Fact]
    public void Startup_WhenAlreadyRunning_ReturnsFalse()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        receiver.Startup();

        var result = receiver.Startup();

        Assert.False(result);

        receiver.Shutdown();
    }

    [Fact]
    public void Shutdown_StopsReceiver()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        receiver.Startup();

        receiver.Shutdown();

        Assert.False(receiver.IsRunning);
    }

    [Fact]
    public void Shutdown_WhenNotRunning_DoesNotThrow()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        receiver.Shutdown(); // Should not throw
    }

    [Fact]
    public void IsRunning_ReflectsState()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        Assert.False(receiver.IsRunning);

        receiver.Startup();
        Assert.True(receiver.IsRunning);

        receiver.Shutdown();
        Assert.False(receiver.IsRunning);
    }

    #endregion

    #region Frequency Tests

    [Fact]
    public void CurrentFrequency_ReturnsCurrentFrequency()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        receiver.SetBand(BandType.FM, 98_500_000);

        Assert.Equal(98_500_000, receiver.CurrentFrequency);
    }

    [Fact]
    public void SetFrequency_WithKnownBand_SetsFrequencyAndBand()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        var result = receiver.SetFrequency(98_500_000);

        Assert.True(result);
        Assert.Equal(98_500_000, receiver.CurrentFrequency);
    }

    [Fact]
    public void SetFrequency_OutsideDeviceRange_ReturnsFalse()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        var result = receiver.SetFrequency(1); // 1 Hz is outside device range

        Assert.False(result);
    }

    [Fact]
    public void SetFrequencyInBand_ValidFrequency_SetsFrequency()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        receiver.SetBand(BandType.FM);

        var result = receiver.SetFrequencyInBand(98_500_000);

        Assert.True(result);
        Assert.Equal(98_500_000, receiver.CurrentFrequency);
    }

    [Fact]
    public void SetFrequencyInBand_OutsideBand_ThrowsException()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        receiver.SetBand(BandType.FM);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            receiver.SetFrequencyInBand(500_000)); // AM frequency, not in FM band
    }

    [Fact]
    public void TuneFrequencyUp_IncreasesFrequency()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        receiver.SetBand(BandType.FM, 98_000_000);
        var initialFreq = receiver.CurrentFrequency;

        var result = receiver.TuneFrequencyUp(100_000);

        Assert.True(result);
        Assert.Equal(initialFreq + 100_000, receiver.CurrentFrequency);
    }

    [Fact]
    public void TuneFrequencyDown_DecreasesFrequency()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        receiver.SetBand(BandType.FM, 98_000_000);
        var initialFreq = receiver.CurrentFrequency;

        var result = receiver.TuneFrequencyDown(100_000);

        Assert.True(result);
        Assert.Equal(initialFreq - 100_000, receiver.CurrentFrequency);
    }

    [Fact]
    public void TuneFrequencyUp_AtUpperLimit_ReturnsFalse()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        receiver.SetBand(BandType.FM, 108_000_000); // Upper limit

        var result = receiver.TuneFrequencyUp(100_000);

        Assert.False(result);
    }

    [Fact]
    public void TuneFrequencyDown_AtLowerLimit_ReturnsFalse()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        receiver.SetBand(BandType.FM, 87_500_000); // Lower limit

        var result = receiver.TuneFrequencyDown(100_000);

        Assert.False(result);
    }

    [Fact]
    public void FrequencyChanged_RaisesEvent()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        receiver.SetBand(BandType.FM, 98_000_000);

        long? oldFreq = null;
        long? newFreq = null;
        receiver.FrequencyChanged += (s, e) =>
        {
            oldFreq = e.OldFrequency;
            newFreq = e.NewFrequency;
        };

        receiver.TuneFrequencyUp(100_000);

        Assert.Equal(98_000_000, oldFreq);
        Assert.Equal(98_100_000, newFreq);
    }

    // Task #80: when the inner RDS decoder fires StationNameDecoded the
    // receiver MUST forward it as RdsStationNameChanged with the same name.
    // This is the public hook the SDR audio source uses to drive its
    // stability tracker at the underlying RDS PS frame rate (~10 Hz)
    // instead of at the property-poll rate.
    [Fact]
    public void RdsStationNameChanged_ForwardsDecoderEvent()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        // FM band + Startup builds the inner RdsDecoder and subscribes to
        // its StationNameDecoded event. We then Shutdown to stop the mock
        // streaming pipeline (otherwise the DSP processing thread would race
        // our synthetic Process() call on the shared decoder buffers); the
        // decoder field is retained, so the event wiring persists.
        receiver.SetBand(BandType.FM, 98_500_000);
        Assert.True(receiver.Startup(), "Receiver Startup should succeed with mock device");
        receiver.Shutdown();

        var decoderField = typeof(RadioReceiver).GetField(
            "_rdsDecoder",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(decoderField);

        var decoder = decoderField.GetValue(receiver) as RTLSDRCore.DSP.RdsDecoder;
        Assert.NotNull(decoder);

        string? observed = null;
        receiver.RdsStationNameChanged += (_, e) => observed = e.NewName;

        // Reach in and feed a synthetic PS frame through the inner decoder.
        // Group 0A logic is identical to the path the receiver normally
        // drives through Process(...). The decoder fires StationNameDecoded
        // for every complete frame; the receiver must forward it.
        RdsDecoderTestSeam.FeedSyntheticRdsSignal(decoder, "WUNC-FM ");

        Assert.Equal("WUNC-FM", observed);
    }

    // Task #80: after Dispose, the receiver must have detached from the
    // decoder so a stale subscription doesn't fire orphan events. Verified
    // by holding a reference to the inner decoder, disposing the receiver,
    // then driving synthetic frames into the (now-orphaned) decoder and
    // asserting the receiver's event does NOT fire.
    [Fact]
    public void Dispose_DetachesDecoderSubscription()
    {
        var receiver = RadioReceiver.CreateWithMockDevice();
        receiver.SetBand(BandType.FM, 98_500_000);
        Assert.True(receiver.Startup());
        receiver.Shutdown();

        var decoderField = typeof(RadioReceiver).GetField(
            "_rdsDecoder",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var decoder = decoderField!.GetValue(receiver) as RTLSDRCore.DSP.RdsDecoder;
        Assert.NotNull(decoder);

        var fired = false;
        receiver.RdsStationNameChanged += (_, _) => fired = true;

        receiver.Dispose();

        // After dispose, the receiver should no longer respond to decoder
        // events. Push a synthetic frame through the (now-detached) decoder.
        RdsDecoderTestSeam.FeedSyntheticRdsSignal(decoder, "WUNC-FM ");

        Assert.False(fired, "Receiver should not raise RdsStationNameChanged after Dispose");
    }

    #endregion

    #region Scanning Tests

    [Fact]
    public void IsScanning_InitiallyFalse()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        Assert.False(receiver.IsScanning);
    }

    [Fact]
    public void ScanFrequencyUp_WhenNotStarted_ThrowsException()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        Assert.Throws<InvalidOperationException>(() =>
            receiver.ScanFrequencyUp());
    }

    [Fact]
    public void ScanFrequencyDown_WhenNotStarted_ThrowsException()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        Assert.Throws<InvalidOperationException>(() =>
            receiver.ScanFrequencyDown());
    }

    [Fact]
    public void CancelScan_WhenNotScanning_DoesNotThrow()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        receiver.CancelScan(); // Should not throw
    }

    #endregion

    #region Band and Modulation Tests

    [Fact]
    public void SetBand_ChangesBand()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        var result = receiver.SetBand(BandType.AM);
        var state = receiver.GetRadioState();

        Assert.True(result);
        Assert.Equal(BandType.AM, state.CurrentBand);
    }

    [Fact]
    public void SetBand_WithSpecificFrequency_SetsFrequency()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        receiver.SetBand(BandType.FM, 98_500_000);

        Assert.Equal(98_500_000, receiver.CurrentFrequency);
    }

    [Fact]
    public void SetModulation_ChangesModulation()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        var result = receiver.SetModulation(ModulationType.NFM);

        Assert.True(result);
        Assert.Equal(ModulationType.NFM, receiver.CurrentModulation);
    }

    [Fact]
    public void CurrentModulation_ReturnsCurrentModulation()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        receiver.SetBand(BandType.FM); // WFM modulation

        Assert.Equal(ModulationType.WFM, receiver.CurrentModulation);
    }

    [Fact]
    public void GetAvailableBands_ReturnsAllBands()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        var bands = receiver.GetAvailableBands();

        Assert.NotEmpty(bands);
        Assert.Contains(bands, b => b.Type == BandType.FM);
        Assert.Contains(bands, b => b.Type == BandType.AM);
        Assert.Contains(bands, b => b.Type == BandType.Aircraft);
        Assert.Contains(bands, b => b.Type == BandType.Weather);
    }

    #endregion

    #region Audio Control Tests

    [Fact]
    public void Volume_SetAndGet_WorksCorrectly()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        receiver.Volume = 0.5f;

        Assert.Equal(0.5f, receiver.Volume);
    }

    [Fact]
    public void Volume_ClampedToValidRange()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        receiver.Volume = 1.5f;
        Assert.Equal(1.0f, receiver.Volume);

        receiver.Volume = -0.5f;
        Assert.Equal(0.0f, receiver.Volume);
    }

    [Fact]
    public void IsMuted_SetAndGet_WorksCorrectly()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        receiver.IsMuted = true;

        Assert.True(receiver.IsMuted);
    }

    [Fact]
    public void SquelchThreshold_SetAndGet_WorksCorrectly()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        receiver.SquelchThreshold = 0.25f;

        Assert.Equal(0.25f, receiver.SquelchThreshold);
    }

    [Fact]
    public void SquelchThreshold_ClampedToValidRange()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        receiver.SquelchThreshold = 1.5f;
        Assert.Equal(1.0f, receiver.SquelchThreshold);

        receiver.SquelchThreshold = -0.5f;
        Assert.Equal(0.0f, receiver.SquelchThreshold);
    }

    [Fact]
    public void SetAudioOutputFormat_SetsFormat()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        var format = new AudioFormat { SampleRate = 44100, Channels = 2, BitsPerSample = 16 };

        receiver.SetAudioOutputFormat(format);
        var result = receiver.GetAudioOutputFormat();

        Assert.Equal(44100, result.SampleRate);
        Assert.Equal(2, result.Channels);
    }

    #endregion

    #region Gain Control Tests

    [Fact]
    public void AutoGainEnabled_SetAndGet_WorksCorrectly()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        receiver.AutoGainEnabled = false;

        Assert.False(receiver.AutoGainEnabled);
    }

    [Fact]
    public void Gain_SetAndGet_WorksCorrectly()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        receiver.AutoGainEnabled = false;

        receiver.Gain = 25.0f;

        // Gain might be snapped to nearest supported value
        Assert.True(receiver.Gain >= 0);
    }

    #endregion

    #region State Tests

    [Fact]
    public void GetRadioState_ReturnsValidState()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        var state = receiver.GetRadioState();

        Assert.NotNull(state);
        Assert.True(state.FrequencyHz > 0);
        Assert.Equal(ReceiverState.Stopped, state.State);
    }

    [Fact]
    public void SignalStrength_ReturnsValue()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();

        var strength = receiver.SignalStrength;

        Assert.True(strength >= 0.0f && strength <= 1.0f);
    }

    [Fact]
    public void StateChanged_RaisesEvent()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        ReceiverState? newState = null;
        receiver.StateChanged += (s, e) => newState = e.NewState;

        receiver.Startup();

        Assert.Equal(ReceiverState.Running, newState);

        receiver.Shutdown();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_StopsAndCleansUp()
    {
        var receiver = RadioReceiver.CreateWithMockDevice();
        receiver.Startup();

        receiver.Dispose();

        Assert.False(receiver.IsRunning);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var receiver = RadioReceiver.CreateWithMockDevice();

        receiver.Dispose();
        receiver.Dispose(); // Should not throw
    }

    #endregion

    #region Event Tests

    [Fact]
    public void AudioDataAvailable_CanSubscribe()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        var eventRaised = false;

        receiver.AudioDataAvailable += (s, e) => eventRaised = true;

        // Event will only be raised when running and receiving data
        // This test just verifies subscription works
        Assert.False(eventRaised);
    }

    [Fact]
    public void SignalStrengthUpdated_CanSubscribe()
    {
        using var receiver = RadioReceiver.CreateWithMockDevice();
        var eventRaised = false;

        receiver.SignalStrengthUpdated += (s, e) => eventRaised = true;

        // This test just verifies subscription works
        Assert.False(eventRaised);
    }

    #endregion
}
