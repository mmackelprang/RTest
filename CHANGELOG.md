# Changelog

This project follows [Semantic Versioning](https://semver.org/). Detailed per-change history is in the git log
and in [`archive/queue/BUILDER_QUEUE_ARCHIVE.md`](archive/queue/BUILDER_QUEUE_ARCHIVE.md).

## [1.0.0] - 2026-10-04

This is the first release. It is the build installed in the cabinet: an Intel N100 running Ubuntu, with a
1920x720 touch panel and four rotary encoders.

### Audio engine
- Every source and announcement feeds one SoundFlow (MiniAudio) master mixer. Balance, a soft limiter and the
  recognition and visualization taps run on the mixer output.
- Priority-based ducking (1–10) with configurable fades, and correct restore when events overlap.
- Native PipeWire capture over P/Invoke for Bluetooth, with clock-drift compensation and a capture watchdog.

### Sources
- **Radio:** an RTL-SDR software-defined radio (`RTLSDRCore`) for FM and NOAA weather band. It supports seek and
  scan, presets, RDS station names and text, and a swept band map that scan reads and writes.
- **Bluetooth A2DP** receiver on a dedicated adapter, with AVRCP metadata, transport controls and volume sync.
- **File player** with a persistent queue (played tracks survive a restart), shuffle and repeat, playlists, and
  adding whole folders.
- **Vinyl** and generic USB capture.

### Events and announcements
- Google Cloud and Azure text-to-speech, audio-file events, and a REST announcement API for smart-home
  triggers. The offline eSpeak engine was removed for security reasons.

### Outputs
- Local playback, Google Cast (an HTTP MP3 stream or a direct Cast-channel receiver) with automatic
  reconnection, and raw PCM and MP3 HTTP streams.

### Recognition and history
- SongRec recognition with MusicBrainz and Cover Art Archive album art. A source's own metadata (AVRCP or file
  tags) is kept per field, and recognition fills only what is missing.
- Play history with search.

### User interface
- A Blazor Server console UI for the 1920x720 panel: Home, Radio, Bluetooth, Devices, Phone, History,
  Diagnostics and System pages.
- Visualizers (spectrum, levels, waveform and the band map) over SignalR.
- Sleep and standby modes with a clock and National Weather Service conditions, plus panel power control.
- A kiosk launcher with its own Chrome profile, and desktop helpers.

### Cabinet hardware
- Four HID rotary encoders (Volume, Source, Presets and Tuning). They are detected on plug-in, configured at
  start-up and verified by read-back, and drive an on-screen HUD. Long-press, wake and standby gestures are
  supported.
- Integration with the companion RotaryPhone service: an incoming-call banner with the caller's name (phone book
  or contacts) and an Ignore button, call announcements, and Google Voice voicemail and texts.

### Operations
- JSON or SQLite configuration stores with encrypted secrets, backup and restore, and live reload into
  `IOptionsMonitor`.
- Metrics with SQLite rollups, and a diagnostics dashboard.
- Serilog with runtime-switchable log levels. Journald receives only warnings and above from `radio-api`.
- OpenAPI with a Scalar UI at `/scalar/v1`. `/api/health/version` reports the build SHA of each service.
- A deploy script that verifies by SHA, checks that the kiosk is live, and has a `-VerifyOnly` mode.
- Five libraries packable as NuGet packages: `RTLSDRCore`, `Radio.AudioAnalysis`, `Radio.Metrics`,
  `Radio.Configuration` and `Radio.Fingerprinting`.

### Known limitations
- AM and shortwave wait on all-band tuner hardware.
- Ignore stops the rotary phone ringing, but the call keeps ringing on the cell phone. The fix belongs to the
  RotaryPhone repo.
- See [docs/known-issues-and-future-work.md](docs/known-issues-and-future-work.md) for the full list.
