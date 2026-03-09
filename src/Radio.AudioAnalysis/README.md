# Radio.AudioAnalysis

Audio analysis toolkit for waveform comparison, distortion detection, frequency analysis, silence detection, and WAV file I/O. Zero external dependencies.

## Installation

```bash
dotnet add package Radio.AudioAnalysis
```

## Features

- **Waveform Comparison** - Cross-correlation time alignment, sample-by-sample distortion detection
- **Frequency Analysis** - THD measurement via Goertzel algorithm (efficient single-frequency DFT)
- **Silence Detection** - Zero runs, repeated samples, clipping detection
- **WAV File I/O** - Read/write 16-bit PCM WAV files, generate sine wave test tones
- **Distortion Reporting** - Structured reports with SNR, RMS error, gain ratio, and categorized events

## Quick Start

```csharp
using Radio.AudioAnalysis;

// Generate a test tone and compare against captured audio
var reference = WavFileHelper.GenerateStereoSineWave(200, 300, sampleRate: 48000, durationSamples: 48000);
var captured = WavFileHelper.ReadWavFile("captured.wav", out var rate, out var channels);

// Find time offset between signals
var (offset, correlation) = WaveformComparison.FindTimeOffset(reference, captured);

// Compare with detailed distortion report
var report = WaveformComparison.Compare(reference, captured, offset);
Console.WriteLine(report); // SNR, RMS error, gain ratio, distortion events

// Measure Total Harmonic Distortion
var thd = FrequencyAnalysis.MeasureTotalHarmonicDistortion(
    captured, sampleRate: 48000, channels: 2, expectedFrequencyHz: 200);

// Detect silence gaps
var gaps = SilenceDetector.FindZeroRuns(captured, minRunLength: 48);
```

## Key Types

| Type | Description |
|------|-------------|
| `WaveformComparison` | Cross-correlation alignment + multi-metric distortion comparison |
| `FrequencyAnalysis` | THD measurement, Goertzel power detection |
| `SilenceDetector` | Zero runs, repeated samples, clipping detection |
| `WavFileHelper` | WAV I/O, sine wave generation, RMS/peak/dB utilities |
| `DistortionReport` | Structured result: SNR, RMS error, gain ratio, events |
| `DistortionEvent` | Individual distortion with type, offset, duration, severity |
| `ComparisonOptions` | Configurable thresholds for comparison |

## Audio Format

All methods expect interleaved float samples in the range [-1.0, 1.0]. For stereo: `[L0, R0, L1, R1, ...]`.

## License

MIT
