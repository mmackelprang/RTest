# RTLSDRCore - Modern C# RTLSDR Library

A modern, reusable C# library for working with RTL-SDR USB dongles, providing support for AM, FM, SW, AIR, WB, and VHF bands with comprehensive signal processing and demodulation capabilities.

## Overview

RTLSDRCore provides a clean, modular architecture for building Software Defined Radio (SDR) applications in C#. The core library is designed to be reusable across different radio-based projects, offering hardware abstraction, signal processing, and band management out of the box.

## Features

- **Multiple Band Support**: AM, FM, Shortwave, Aircraft, Weather, and VHF bands with extensible architecture for custom bands
- **Interactive Terminal UI**: Rich console interface using Spectre.Console with single-key controls
- **IRadioControl Interface**: Advanced control API for frequency tuning, scanning, and state management
- **Hardware Abstraction**: Interface-based design (`ISdrDevice`) for easy integration with different SDR hardware
- **Signal Processing**: Built-in AM, FM (narrow/wideband), and SSB demodulation with filtering
- **Mock Device**: Test and develop without physical hardware
- **Structured Logging**: Built-in Serilog integration for debugging
- **Clean API**: Simple, intuitive interface for radio operations
- **Event-Driven**: Real-time audio data and signal strength events
- **Well Documented**: Comprehensive XML documentation and markdown examples
- **Extensive Tests**: Unit tests with mock hardware

## Supported Bands

| Band | Frequency Range | Modulation | Use Case |
|------|----------------|------------|----------|
| AM Broadcast | 530 kHz - 1710 kHz | AM | AM radio stations |
| FM Broadcast | 87.5 MHz - 108 MHz | WFM | FM radio stations |
| Shortwave | 1.6 MHz - 30 MHz | AM/SSB | International broadcasts, amateur radio |
| Aircraft | 108 MHz - 137 MHz | AM | Aviation communications |
| Weather | 162.4 MHz - 162.55 MHz | NFM | NOAA Weather Radio |
| VHF | 30 MHz - 300 MHz | NFM | Amateur radio, public safety |

## Project Structure

```
SDRRadio/
├── src/
│   ├── RTLSDRCore/           # Core library
│   │   ├── Bands/            # Band presets and management
│   │   ├── DSP/              # Signal processing (demodulators, filters)
│   │   ├── Enums/            # BandType, ModulationType, etc.
│   │   ├── Hardware/         # Device abstraction (ISdrDevice, Mock, RTL-SDR)
│   │   ├── Models/           # RadioState, AudioFormat, IqSample, etc.
│   │   ├── IRadioControl.cs  # Main control interface
│   │   └── RadioReceiver.cs  # Main receiver implementation
│   └── SDRDemo/              # Interactive terminal demo
├── tests/
│   └── RTLSDRCore.Tests/     # Unit tests
├── docs/
│   ├── API.md                # API documentation
│   └── GettingStarted.md     # Getting started guide
└── SDRRadio.sln              # Solution file
```

## Quick Start

### Installation

```bash
git clone https://github.com/yourusername/SDRRadio.git
cd SDRRadio
dotnet restore
dotnet build
```

### Basic Usage

```csharp
using RTLSDRCore;
using RTLSDRCore.Enums;

// Create a receiver (mock device for testing)
using var receiver = RadioReceiver.CreateWithMockDevice();

// Or use real hardware
// using var receiver = RadioReceiver.CreateWithFirstAvailableDevice();

// Set band and frequency
receiver.SetBand(BandType.FM, 98_500_000);

// Handle audio output
receiver.AudioDataAvailable += (s, e) => {
    // Process audio samples (e.Samples is float[])
    ProcessAudio(e.Samples, e.Format);
};

// Start receiving
receiver.Start();

// Tune around
receiver.TuneFrequencyUp(100_000);  // +100 kHz
receiver.TuneFrequencyDown(50_000); // -50 kHz

// Scan for signals
receiver.ScanFrequencyUp(stepHz: 100_000, signalThreshold: 0.3f);

// Stop
receiver.Stop();
```

### Running the Demo

```bash
cd src/SDRDemo
dotnet run
```

#### Demo Controls

| Key | Action |
|-----|--------|
| Space | Start/Stop receiver |
| Up/Down | Tune frequency (band step) |
| Left/Right | Fine tune (10 kHz) |
| PgUp/PgDn | Coarse tune (1 MHz) |
| S | Scan up for signals |
| Shift+S | Scan down |
| B | Select band |
| M | Toggle mute |
| +/- | Volume up/down |
| G | Toggle AGC |
| F | Enter frequency directly |
| Q | Quit |

### Running Tests

```bash
dotnet test
```

## Architecture

### Core Components

1. **RadioReceiver** - Main class coordinating hardware, DSP, and audio output
2. **ISdrDevice** - Hardware abstraction interface
3. **IRadioControl** - Control interface for tuning, scanning, band selection
4. **IDemodulator** - Demodulation interface (AM, FM, SSB)
5. **BandPresets** - Predefined band configurations

### Signal Flow

```
RTL-SDR Device → IQ Samples → Demodulator → Decimator → AGC → Audio Output
                              ↓
                        Signal Meter
```

## Hardware Requirements

### For Development/Testing
- .NET 8.0 SDK
- No physical hardware needed (mock device available)

### For Real Hardware
- RTL-SDR USB dongle (RTL2832U based)
- librtlsdr / rtl-sdr drivers
  - Windows: Install with Zadig or SDR# installer
  - Linux: `apt install librtlsdr-dev`
  - macOS: `brew install librtlsdr`

## Logging

RTLSDRCore uses Serilog with this format:
```
{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}
```

Configure in your application:
```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("radio.log")
    .CreateLogger();
```

## Documentation

- [Getting Started Guide](docs/GettingStarted.md)
- [API Documentation](docs/API.md)
- XML documentation in source code

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

## License

MIT License - see LICENSE file for details.

## Acknowledgments

This project provides a modern C# implementation for RTL-SDR radio reception. Initially created for reimagining Grandpa Anderson's Phillips radio console from the 1930's with modern SDR technology.
