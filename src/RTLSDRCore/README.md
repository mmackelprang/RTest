# RTLSDRCore

A modern C# library for RTL-SDR USB radio receivers. Supports AM, FM, Shortwave, Aircraft, Weather, and VHF bands with built-in DSP demodulators, stereo FM decoding, RDS, and a hardware abstraction layer.

## Installation

```bash
dotnet add package RTLSDRCore
```

## Quick Start

```csharp
using RTLSDRCore;
using RTLSDRCore.Enums;

// Create a receiver (mock device for testing, no hardware needed)
using var receiver = RadioReceiver.CreateWithMockDevice();
receiver.Startup();

// Or use real hardware:
// using var receiver = RadioReceiver.CreateWithFirstAvailableDevice();

// Set band and frequency
receiver.SetBand(BandType.FM, 98_500_000); // 98.5 MHz

// Handle audio output
receiver.AudioDataAvailable += (s, e) =>
{
    // e.Samples is float[], e.Format describes sample rate/channels
    ProcessAudio(e.Samples, e.Format);
};

// Tune around
receiver.TuneFrequencyUp(100_000);  // +100 kHz
receiver.TuneFrequencyDown(50_000); // -50 kHz

// Scan for signals
receiver.ScanFrequencyUp(stepHz: 100_000, signalThreshold: 0.3f);

// Read RDS data (FM only)
Console.WriteLine($"Station: {receiver.RdsStationName}");
Console.WriteLine($"Text: {receiver.RdsRadioText}");

receiver.Shutdown();
```

## Supported Bands

| Band | Frequency Range | Modulation | Use Case |
|------|----------------|------------|----------|
| AM Broadcast | 530 kHz - 1710 kHz | AM | AM radio stations |
| FM Broadcast | 87.5 MHz - 108 MHz | WFM | FM radio stations |
| Shortwave | 1.6 MHz - 30 MHz | AM/SSB | International broadcasts, amateur radio |
| Aircraft | 108 MHz - 137 MHz | AM | Aviation communications |
| Weather | 162.4 MHz - 162.55 MHz | NFM | NOAA Weather Radio |
| VHF | 30 MHz - 300 MHz | NFM | Amateur radio, public safety |

## Key Types

| Type | Description |
|------|-------------|
| `RadioReceiver` | Main class — create via `CreateWithMockDevice()` or `CreateWithFirstAvailableDevice()` |
| `IRadioControl` | Control interface for tuning, scanning, band selection, volume |
| `ISdrDevice` | Hardware abstraction — implement for custom SDR devices |
| `MockSdrDevice` | Built-in mock for testing without hardware |
| `BandPresets` | Predefined band configurations with frequency ranges and step sizes |
| `IDemodulator` | DSP demodulation interface (AM, FM, SSB implementations included) |
| `StereoFmDecoder` | Stereo FM multiplex decoder with pilot tone detection |
| `RdsDecoder` | Radio Data System decoder (station name, radio text, program type) |

## Signal Flow

```
SDR Device -> IQ Samples -> Demodulator -> Decimator -> AGC -> Audio Output
                            |
                            +-> Signal Meter
                            +-> Stereo FM Decoder -> RDS Decoder
```

## Hardware Requirements

**For development/testing:** .NET 10+ SDK only (mock device included).

**For real hardware:**
- RTL-SDR USB dongle (RTL2832U-based)
- librtlsdr drivers:
  - Windows: Install with Zadig or SDR# installer
  - Linux: `apt install librtlsdr-dev`
  - macOS: `brew install librtlsdr`

## Logging

RTLSDRCore uses Serilog for structured logging. Configure in your application:

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();
```

## License

MIT
