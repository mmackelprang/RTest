# Audio UAT Tool - Phased Development Plan

## Overview

This document outlines the phased development plan for the **Radio.Tools.AudioUAT** application—a comprehensive User Acceptance Testing (UAT) tool designed to validate audio functionality across all phases of the Radio Console project.

The tool will be implemented as a .NET console application that grows incrementally with each project phase, providing interactive testing capabilities for QA testers and developers.

---

## Table of Contents

1. [Tool Architecture](#tool-architecture)
2. [Phase 2: Core Audio Engine Testing](#phase-2-core-audio-engine-testing)
3. [Phase 3: Primary Audio Sources Testing](#phase-3-primary-audio-sources-testing)
4. [Phase 4: Event Audio Sources Testing](#phase-4-event-audio-sources-testing)
5. [Phase 5: Ducking & Priority System Testing](#phase-5-ducking--priority-system-testing)
6. [Phase 6: Audio Outputs Testing](#phase-6-audio-outputs-testing)
7. [Phase 7: Visualization & Monitoring Testing](#phase-7-visualization--monitoring-testing)
8. [Phase 10: Database Backup & Restore Testing](#phase-10-database-backup--restore-testing)
9. [Development Prompts](#development-prompts)

---

## Tool Architecture

```
tools/
└── Radio.Tools.AudioUAT/
    ├── Radio.Tools.AudioUAT.csproj
    ├── Program.cs
    ├── TestRunner.cs
    ├── appsettings.json           (includes Database configuration)
    ├── TestResults/
    │   └── TestResultsManager.cs
    ├── Phases/
    │   ├── Phase2/
    │   │   └── CoreAudioEngineTests.cs
    │   ├── Phase3/
    │   │   └── PrimaryAudioSourceTests.cs
    │   ├── Phase4/
    │   │   └── EventAudioSourceTests.cs
    │   ├── Phase5/
    │   │   └── DuckingPriorityTests.cs
    │   ├── Phase6/
    │   │   └── AudioOutputTests.cs
    │   ├── Phase7/
    │   │   └── VisualizationTests.cs
    │   ├── Phase8/
    │   │   └── ApiSignalRTests.cs
    │   ├── Phase9/
    │   │   └── FingerprintingTests.cs
    │   └── Phase10/
    │       └── BackupRestoreTests.cs  (NEW)
    ├── Utilities/
    │   ├── AudioTestHelpers.cs
    │   ├── ConsoleUI.cs
    │   └── TestReportGenerator.cs
    └── Assets/
        └── test-audio/
            ├── tone-440hz.wav
            ├── silence.wav
            └── test-announcement.wav
```

---

## Phase 2: Core Audio Engine Testing

**Objective:** Validate SoundFlow audio engine initialization, device management, and master mixer functionality.

### Test Cases

| ID | Test Case | Description | Expected Result |
|----|-----------|-------------|-----------------|
| P2-001 | Engine Initialization | Initialize SoundFlow with MiniAudio backend | Engine state transitions to `Ready` |
| P2-002 | Engine Start/Stop | Start and stop audio engine | State transitions correctly, no audio artifacts |
| P2-003 | Device Enumeration | List all audio output devices | Returns valid device list with ALSA IDs |
| P2-004 | Default Device Detection | Identify default audio output | Returns system default device |
| P2-005 | Device Selection | Switch between output devices | Audio routes to selected device |
| P2-006 | USB Device Detection | Detect USB audio devices | USB devices listed with port info |
| P2-007 | Hot-Plug Detection | Connect/disconnect USB device during runtime | Device list updates automatically |
| P2-008 | Master Volume Control | Adjust master volume 0-100% | Volume changes audibly |
| P2-009 | Master Mute Toggle | Mute/unmute master output | Audio silences/resumes correctly |
| P2-010 | Balance Control | Adjust L/R balance | Audio shifts between channels |
| P2-011 | Output Stream Tap | Access mixed output stream | Stream provides valid PCM data |
| P2-012 | Engine Error Recovery | Force error condition | Engine recovers or reports error state |

### Interactive Test Menu

```
═══════════════════════════════════════════════════════════════
            PHASE 2: CORE AUDIO ENGINE TESTS
═══════════════════════════════════════════════════════════════

  [1] Initialize Audio Engine
  [2] Start Audio Engine
  [3] Stop Audio Engine
  [4] List Output Devices
  [5] Select Output Device
  [6] Test Hot-Plug Detection (30 second window)
  [7] Master Volume Test (interactive slider)
  [8] Mute/Unmute Toggle Test
  [9] Balance Test (L/R sweep)
  [10] Verify Output Stream Tap
  [11] Run All Phase 2 Tests
  
  [R] View Test Results
  [M] Return to Main Menu
  [Q] Quit

═══════════════════════════════════════════════════════════════
```

---

## Phase 3: Primary Audio Sources Testing

**Objective:** Validate radio, vinyl, Spotify, and other primary audio source implementations.

### Test Cases

| ID | Test Case | Description | Expected Result |
|----|-----------|-------------|-----------------|
| P3-001 | Radio Source Creation | Create USB radio source | Source connects to USB audio device |
| P3-002 | Radio Playback | Play internet radio stream | Audio plays, metadata received |
| P3-003 | Radio Station Switch | Change between stations | Seamless transition, no artifacts |
| P3-004 | Radio Buffering | Test stream buffering under load | Smooth playback, buffer status reported |
| P3-005 | Vinyl Source Creation | Create USB turntable source | Source connects to USB audio device |
| P3-006 | Vinyl Playback | Capture vinyl audio | Live audio captured and played |
| P3-007 | Vinyl USB Port Conflict | Attempt duplicate USB port | Conflict error reported |
| P3-008 | Spotify Source Auth | Authenticate with Spotify | OAuth flow completes successfully |
| P3-009 | Spotify Playback | Play Spotify content | Audio plays with Spotify Connect |
| P3-010 | Spotify Controls | Play/Pause/Skip via API | Commands execute correctly |
| P3-011 | Source Volume Control | Per-source volume adjustment | Individual volumes work correctly |
| P3-012 | Source Mute | Mute individual source | Source mutes, others unaffected |
| P3-013 | Multiple Sources | Run multiple sources simultaneously | All sources mix correctly |
| P3-014 | Source Lifecycle | Create/Start/Stop/Dispose source | Clean state transitions |

### Interactive Test Menu

```
═══════════════════════════════════════════════════════════════
            PHASE 3: PRIMARY AUDIO SOURCES TESTS
═══════════════════════════════════════════════════════════════

  RADIO TESTS:
  [1] Create Radio Source (enter URL)
  [2] Play Radio Stream
  [3] Switch Radio Station
  [4] Test Radio Buffering
  
  VINYL TESTS:
  [5] List USB Audio Devices
  [6] Create Vinyl Source (select device)
  [7] Test Vinyl Capture
  [8] Test USB Port Conflict Detection
  
  SPOTIFY TESTS:
  [9] Authenticate Spotify
  [10] Play Spotify Track
  [11] Test Spotify Controls
  
  MULTI-SOURCE TESTS:
  [12] Test Per-Source Volume
  [13] Test Multiple Simultaneous Sources
  [14] Test Source Lifecycle
  [15] Run All Phase 3 Tests
  
  [R] View Test Results
  [M] Return to Main Menu

═══════════════════════════════════════════════════════════════
```

---

## Phase 4: Event Audio Sources Testing

**Objective:** Validate sound effects, notifications, and scheduled audio event playback.

### Test Cases

| ID | Test Case | Description | Expected Result |
|----|-----------|-------------|-----------------|
| P4-001 | Sound Effect Load | Load sound effect from file | Audio file parsed correctly |
| P4-002 | Sound Effect Play | Play one-shot sound effect | Plays once, cleans up |
| P4-003 | Sound Effect Overlap | Play same effect rapidly | Multiple instances play correctly |
| P4-004 | Sound Effect Volume | Adjust effect volume | Volume applies correctly |
| P4-005 | Notification Create | Create notification audio | Notification queued |
| P4-006 | Notification Priority | Test priority ordering | Higher priority plays first |
| P4-007 | Notification Queue | Queue multiple notifications | Plays in order, no overlap |
| P4-008 | Chime Playback | Play hourly chime | Chime plays at correct time |
| P4-009 | TTS Integration | Generate TTS announcement | Speech synthesized and plays |
| P4-010 | TTS Queue | Queue multiple TTS messages | Messages play in sequence |
| P4-011 | Event Scheduling | Schedule future audio event | Event triggers at scheduled time |
| P4-012 | Event Cancellation | Cancel scheduled event | Event does not play |
| P4-013 | Memory Cleanup | Play many effects, verify cleanup | Memory remains stable |

### Interactive Test Menu

```
═══════════════════════════════════════════════════════════════
            PHASE 4: EVENT AUDIO SOURCES TESTS
═══════════════════════════════════════════════════════════════

  SOUND EFFECTS:
  [1] Load Sound Effect File
  [2] Play Sound Effect
  [3] Rapid Fire Test (10x rapid plays)
  [4] Sound Effect Volume Test
  
  NOTIFICATIONS:
  [5] Create Notification
  [6] Test Priority Ordering
  [7] Queue Multiple Notifications
  
  CHIMES & SCHEDULED:
  [8] Play Test Chime
  [9] Schedule Future Event
  [10] Cancel Scheduled Event
  
  TEXT-TO-SPEECH:
  [11] Generate TTS Announcement
  [12] Queue Multiple TTS Messages
  [13] Test TTS with Background Audio
  
  STRESS TESTS:
  [14] Memory/Cleanup Stress Test
  [15] Run All Phase 4 Tests
  
  [R] View Test Results
  [M] Return to Main Menu

═══════════════════════════════════════════════════════════════
```

---

## Phase 5: Ducking & Priority System Testing

**Objective:** Validate audio ducking, priority-based mixing, and automatic volume management.

### Test Cases

| ID | Test Case | Description | Expected Result |
|----|-----------|-------------|-----------------|
| P5-001 | Priority Assignment | Assign priority to sources | Priorities stored correctly |
| P5-002 | Priority Ordering | Verify priority enumeration | Sources ordered by priority |
| P5-003 | Duck on Event | Trigger ducking via event | Background audio ducks |
| P5-004 | Duck Level | Verify duck attenuation level | Volume reduces by configured dB |
| P5-005 | Duck Ramp | Test fade-down smoothness | Smooth fade, no clicks |
| P5-006 | Duck Release | End ducking event | Volume returns smoothly |
| P5-007 | Duck Multiple Sources | Duck with multiple backgrounds | All background sources duck |
| P5-008 | Duck Nested | Trigger second event during duck | Handles nested ducks correctly |
| P5-009 | Priority Override | Higher priority source starts | Lower priority ducks/mutes |
| P5-010 | Priority Release | Higher priority ends | Lower priority resumes |
| P5-011 | Duck Configuration | Modify duck parameters | Changes apply to next duck |
| P5-012 | Announcement Duck | TTS announcement ducks music | Music ducks during speech |
| P5-013 | No Duck Sources | Some sources exempt from duck | Exempt sources unaffected |

### Interactive Test Menu

```
═══════════════════════════════════════════════════════════════
            PHASE 5: DUCKING & PRIORITY TESTS
═══════════════════════════════════════════════════════════════

  PRIORITY MANAGEMENT:
  [1] View Source Priorities
  [2] Assign Source Priority
  [3] Test Priority Ordering
  
  DUCKING TESTS:
  [4] Trigger Manual Duck
  [5] Test Duck Level (-6dB, -12dB, -18dB)
  [6] Test Duck Ramp Smoothness
  [7] Test Duck Release
  [8] Test Multi-Source Ducking
  [9] Test Nested Duck Events
  
  PRIORITY OVERRIDE:
  [10] Test Priority Override Scenario
  [11] Test Priority Release Scenario
  
  INTEGRATION:
  [12] Test Announcement Ducking (with music)
  [13] Test Duck Exempt Sources
  [14] Configure Duck Parameters
  [15] Run All Phase 5 Tests
  
  [R] View Test Results
  [M] Return to Main Menu

═══════════════════════════════════════════════════════════════
```

---

## Phase 6: Audio Outputs Testing

**Objective:** Validate multi-device output, Chromecast streaming, and HTTP audio streaming.

### Test Cases

| ID | Test Case | Description | Expected Result |
|----|-----------|-------------|-----------------|
| P6-001 | Multi-Device Output | Route to multiple devices | Audio plays on all devices |
| P6-002 | Device-Specific Volume | Per-device volume control | Volumes independent |
| P6-003 | Device Routing | Route specific source to device | Source only on target device |
| P6-004 | Chromecast Discovery | Discover Chromecast devices | Devices listed with names |
| P6-005 | Chromecast Connect | Connect to Chromecast | Connection established |
| P6-006 | Chromecast Stream | Stream audio to Chromecast | Audio plays on Chromecast |
| P6-007 | Chromecast Disconnect | Disconnect from Chromecast | Clean disconnection |
| P6-008 | HTTP Stream Start | Start HTTP audio stream server | Server listening on port |
| P6-009 | HTTP Stream Connect | Connect client to stream | Client receives audio data |
| P6-010 | HTTP Stream Multi-Client | Multiple clients connect | All clients receive audio |
| P6-011 | HTTP Stream Format | Verify stream format | Correct content-type, bitrate |
| P6-012 | Latency Measurement | Measure output latency | Latency within acceptable range |
| P6-013 | Sync Test | Test multi-output sync | Outputs synchronized |
| P6-014 | Failover | Primary output fails | Switches to backup output |

### Interactive Test Menu

```
═══════════════════════════════════════════════════════════════
            PHASE 6: AUDIO OUTPUTS TESTS
═══════════════════════════════════════════════════════════════

  MULTI-DEVICE:
  [1] List Active Output Devices
  [2] Enable Multi-Device Output
  [3] Set Per-Device Volume
  [4] Test Source Routing
  
  CHROMECAST:
  [5] Discover Chromecast Devices
  [6] Connect to Chromecast
  [7] Stream to Chromecast
  [8] Disconnect Chromecast
  
  HTTP STREAMING:
  [9] Start HTTP Stream Server
  [10] View Stream URL/Info
  [11] Simulate Client Connection
  [12] Multi-Client Test
  [13] Stop HTTP Stream Server
  
  DIAGNOSTICS:
  [14] Measure Output Latency
  [15] Test Multi-Output Sync
  [16] Test Output Failover
  [17] Run All Phase 6 Tests
  
  [R] View Test Results
  [M] Return to Main Menu

═══════════════════════════════════════════════════════════════
```

---

## Phase 7: Visualization & Monitoring Testing

**Objective:** Validate audio visualization components: spectrum analyzer (FFT), level meter (VU), and waveform display.

### Test Cases

| ID | Test Case | Description | Expected Result |
|----|-----------|-------------|-----------------|
| P7-001 | Spectrum Analyzer Init | Initialize spectrum analyzer with FFT size | Analyzer ready with configured bins |
| P7-002 | Spectrum FFT Processing | Process audio through FFT | Correct frequency detection |
| P7-003 | Spectrum Frequency Bins | Verify bin calculation | Bins cover full frequency range |
| P7-004 | Spectrum Smoothing | Test display smoothing | Stable, non-jittery display |
| P7-005 | Level Meter Init | Initialize VU meter | Meter ready for measurements |
| P7-006 | Level Peak Detection | Detect audio peaks | Peak values match input |
| P7-007 | Level RMS Calculation | Calculate RMS levels | RMS = amplitude / √2 for sine |
| P7-008 | Level Clipping Detection | Detect audio clipping | Clipping indicator at max level |
| P7-009 | Level Decibels | Linear to dBFS conversion | Correct dB values |
| P7-010 | Waveform Buffer | Buffer samples for display | Samples stored correctly |
| P7-011 | Waveform Stereo | Separate L/R channels | Independent channel buffers |
| P7-012 | Waveform Downsample | Downsample for display | Peaks preserved |
| P7-013 | Visualizer Service | Test IVisualizerService | All methods functional |
| P7-014 | Visualizer Processing | Real-time processing | Fast enough for real-time |
| P7-015 | Visualizer Reset | Reset visualization state | All data cleared |

### Interactive Test Menu

```
═══════════════════════════════════════════════════════════════
            PHASE 7: VISUALIZATION & MONITORING TESTS
═══════════════════════════════════════════════════════════════

  SPECTRUM ANALYZER:
  [1] Initialize Spectrum Analyzer
  [2] FFT Processing Test
  [3] Frequency Bins Verification
  [4] Smoothing Test
  
  LEVEL METER:
  [5] Initialize Level Meter
  [6] Peak Detection Test
  [7] RMS Calculation Test
  [8] Clipping Detection Test
  [9] dBFS Conversion Test
  
  WAVEFORM:
  [10] Waveform Buffer Test
  [11] Stereo Channels Test
  [12] Downsampling Test
  
  INTEGRATION:
  [13] Visualizer Service Test
  [14] Real-time Processing Test
  [15] Reset Test
  [16] Run All Phase 7 Tests
  
  [R] View Test Results
  [M] Return to Main Menu

═══════════════════════════════════════════════════════════════
```

---

## Development Prompts

The following prompts are designed for GitHub Copilot to implement each phase of the Audio UAT tool.

---

### Phase 2 Development Prompt

```markdown
## Task: Create Audio UAT Tool - Phase 2 (Core Audio Engine Testing)

Create the foundation for the Radio.Tools.AudioUAT console application in the `tools/` directory. This phase focuses on testing the Core Audio Engine functionality.

### Context
- Reference `/design/AUDIO_ARCHITECTURE.md` for audio engine specifications
- Reference `/PLAN.md` Phase 2 for Core Audio Engine details
- The tool should use the existing `Radio.Core` and `Radio.Infrastructure` projects

### Requirements

#### 1. Create Project Structure

Create `tools/Radio.Tools.AudioUAT/Radio.Tools.AudioUAT.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Radio.Tools.AudioUAT</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Radio.Core\Radio.Core.csproj" />
    <ProjectReference Include="..\..\src\Radio.Infrastructure\Radio.Infrastructure.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Spectre.Console" Version="0.49.*" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.*" />
  </ItemGroup>
</Project>
```

#### 2. Create Main Entry Point

Create `tools/Radio.Tools.AudioUAT/Program.cs`:
- Use Spectre.Console for rich console UI
- Initialize dependency injection with Radio.Infrastructure services
- Display main menu with phase selection
- Support command-line arguments for automated test runs

#### 3. Create Console UI Helper

Create `tools/Radio.Tools.AudioUAT/Utilities/ConsoleUI.cs`:
- Implement menu display with Spectre.Console
- Create progress bars for long-running tests
- Implement status panels showing current audio state
- Create table formatters for test results

#### 4. Create Test Runner Framework

Create `tools/Radio.Tools.AudioUAT/TestRunner.cs`:
- Define `IPhaseTest` interface for test implementations
- Create `TestResult` record for capturing test outcomes
- Implement test execution with timing and exception handling
- Support running individual tests or all tests in a phase

```csharp
public interface IPhaseTest
{
    string TestId { get; }
    string TestName { get; }
    string Description { get; }
    int Phase { get; }
    Task<TestResult> ExecuteAsync(CancellationToken ct = default);
}

public record TestResult
{
    public required string TestId { get; init; }
    public required bool Passed { get; init; }
    public string? Message { get; init; }
    public TimeSpan Duration { get; init; }
    public Exception? Exception { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}
```

#### 5. Create Test Results Manager

Create `tools/Radio.Tools.AudioUAT/TestResults/TestResultsManager.cs`:
- Store test results in memory during session
- Export results to JSON file
- Generate summary reports
- Track pass/fail statistics per phase

#### 6. Implement Phase 2 Tests

Create `tools/Radio.Tools.AudioUAT/Phases/Phase2/CoreAudioEngineTests.cs`:

Implement the following test cases:
- **P2-001 Engine Initialization**: Call `IAudioEngine.InitializeAsync()`, verify state becomes `Ready`
- **P2-002 Engine Start/Stop**: Call `StartAsync()` and `StopAsync()`, verify state transitions
- **P2-003 Device Enumeration**: Call `IAudioDeviceManager.GetOutputDevicesAsync()`, display devices
- **P2-004 Default Device Detection**: Call `GetDefaultOutputDeviceAsync()`, verify returns valid device
- **P2-005 Device Selection**: Interactive device selection, verify audio routes correctly
- **P2-006 USB Device Detection**: Filter devices by `IsUSBDevice`, display USB port info
- **P2-007 Hot-Plug Detection**: Subscribe to `DevicesChanged` event, wait for device changes
- **P2-008 Master Volume Control**: Interactive volume slider using Spectre.Console
- **P2-009 Master Mute Toggle**: Toggle mute, verify audio silences/resumes
- **P2-010 Balance Control**: Sweep balance from L to R, verify audio shift
- **P2-011 Output Stream Tap**: Get mixed output stream, verify data flows
- **P2-012 Engine Error Recovery**: Force invalid state, verify error handling

#### 7. Create Audio Test Helpers

Create `tools/Radio.Tools.AudioUAT/Utilities/AudioTestHelpers.cs`:
- Generate test tones (440Hz sine wave)
- Create silence detection utility
- Implement audio level meter display
- Create latency measurement helper

### Interactive Features

For each test, provide:
1. Clear instructions on what the tester should observe
2. Y/N confirmation prompts for subjective audio tests
3. Automatic validation where possible (e.g., state checks)
4. Detailed logging of all actions and results

### Success Criteria
- [ ] Project builds and runs without errors
- [ ] Main menu displays correctly with Spectre.Console
- [ ] All 12 Phase 2 tests are implemented
- [ ] Test results can be viewed and exported
- [ ] Interactive tests provide clear user guidance
- [ ] Console UI is intuitive and visually appealing
```

---

### Phase 3 Development Prompt

```markdown
## Task: Extend Audio UAT Tool - Phase 3 (Primary Audio Sources Testing)

Extend the Radio.Tools.AudioUAT application to include Phase 3 testing for Primary Audio Sources (Radio, Vinyl, Spotify).

### Context
- Reference `/PLAN.md` Phase 3 for Primary Audio Sources details
- Build upon the Phase 2 foundation already implemented
- Reference `/design/AUDIO.md` for source implementation specifications

### Requirements

#### 1. Implement Phase 3 Tests

Create `tools/Radio.Tools.AudioUAT/Phases/Phase3/PrimaryAudioSourceTests.cs`:

**Radio Source Tests:**
- **P3-001 Radio Source Creation**: Create `IRadioSource` with test stream URL, verify initialization
- **P3-002 Radio Playback**: Start playback, verify audio output and metadata events
- **P3-003 Radio Station Switch**: Switch between 3 test stations, verify seamless transition
- **P3-004 Radio Buffering**: Simulate network latency, verify buffering status and recovery

**Vinyl Source Tests:**
- **P3-005 Vinyl Source Creation**: Create `IVinylSource` with USB device selection
- **P3-006 Vinyl Playback**: Start vinyl capture, display input levels, verify audio passthrough
- **P3-007 Vinyl USB Port Conflict**: Attempt to create second source on same USB port, verify error

**Spotify Source Tests:**
- **P3-008 Spotify Source Auth**: Initiate OAuth flow, open browser, wait for callback
- **P3-009 Spotify Playback**: Play test track via Spotify Connect, verify audio
- **P3-010 Spotify Controls**: Test play/pause/skip/seek commands

**Multi-Source Tests:**
- **P3-011 Source Volume Control**: Adjust individual source volumes with real-time display
- **P3-012 Source Mute**: Mute one source while others play, verify isolation
- **P3-013 Multiple Sources**: Run radio + vinyl simultaneously, verify mixing
- **P3-014 Source Lifecycle**: Create, start, stop, dispose sources, verify clean transitions

#### 2. Create Source Test Helpers

Create `tools/Radio.Tools.AudioUAT/Utilities/SourceTestHelpers.cs`:
- Predefined test radio station URLs (public domain streams)
- USB device selection helper with port display
- Spotify test track selection
- Source state display panel

#### 3. Update Main Menu

Update `Program.cs` to include Phase 3 menu option:
```
[2] Phase 3: Primary Audio Sources
```

#### 4. Interactive Features for Phase 3

**Radio Testing:**
- Display live metadata (station name, current track)
- Show buffer level indicator
- Allow URL entry for custom streams

**Vinyl Testing:**
- Display real-time input level meters
- Show USB device details (manufacturer, serial)
- Highlight port conflict scenarios

**Spotify Testing:**
- Display current playback state
- Show track info (title, artist, album art URL)
- Interactive transport controls

### Test Configuration

Create `tools/Radio.Tools.AudioUAT/Assets/test-config.json`:
```json
{
  "testRadioStations": [
    { "name": "Jazz24", "url": "https://live.wostreaming.net/direct/ppm-jazz24aac-ibc1" },
    { "name": "Classical", "url": "https://stream.wqxr.org/wqxr" },
    { "name": "News", "url": "https://stream.wbur.org/wbur.mp3" }
  ],
  "spotifyTestTrack": "spotify:track:4uLU6hMCjMI75M1A2tKUQC",
  "vinylTestDurationSeconds": 30
}
```

### Success Criteria
- [ ] All 14 Phase 3 tests are implemented
- [ ] Radio streaming tests work with public streams
- [ ] Vinyl tests properly enumerate and capture USB audio
- [ ] Spotify OAuth flow works correctly
- [ ] Multi-source mixing tests demonstrate proper audio mixing
- [ ] Source lifecycle tests verify clean resource management
```

---

### Phase 4 Development Prompt

```markdown
## Task: Extend Audio UAT Tool - Phase 4 (Event Audio Sources Testing)

Extend the Radio.Tools.AudioUAT application to include Phase 4 testing for Event Audio Sources (Sound Effects, Notifications, TTS).

### Context
- Reference `/PLAN.md` Phase 4 for Event Audio Sources details
- Build upon Phases 2-3 foundation already implemented
- Event sources are typically short-duration, one-shot audio

### Requirements

#### 1. Create Test Audio Assets

Create directory `tools/Radio.Tools.AudioUAT/Assets/test-audio/` with:
- `tone-440hz.wav` - 1 second 440Hz sine wave (for effect tests)
- `chime.wav` - Short chime sound
- `notification.wav` - Notification sound
- `silence-1s.wav` - 1 second of silence (for timing tests)

> Note: Generate these programmatically during first run if not present using NAudio or similar

#### 2. Implement Phase 4 Tests

Create `tools/Radio.Tools.AudioUAT/Phases/Phase4/EventAudioSourceTests.cs`:

**Sound Effect Tests:**
- **P4-001 Sound Effect Load**: Load WAV file into sound effect player, verify no errors
- **P4-002 Sound Effect Play**: Play effect, confirm audio heard (Y/N prompt)
- **P4-003 Sound Effect Overlap**: Rapid-fire 10 plays of same effect, verify all instances play
- **P4-004 Sound Effect Volume**: Play at 25%, 50%, 75%, 100%, verify volume differences

**Notification Tests:**
- **P4-005 Notification Create**: Create notification with audio payload, verify queued
- **P4-006 Notification Priority**: Queue low/medium/high priority, verify high plays first
- **P4-007 Notification Queue**: Queue 5 notifications, verify sequential playback with no overlap

**Chime Tests:**
- **P4-008 Chime Playback**: Trigger chime manually, verify playback

**TTS Tests:**
- **P4-009 TTS Integration**: Generate "Testing audio system" speech, verify playback
- **P4-010 TTS Queue**: Queue 3 TTS messages, verify sequential playback

**Scheduling Tests:**
- **P4-011 Event Scheduling**: Schedule event 10 seconds in future, wait and verify trigger
- **P4-012 Event Cancellation**: Schedule event, cancel it, verify it doesn't play

**Stress Tests:**
- **P4-013 Memory Cleanup**: Play 100 sound effects, monitor memory, verify stable

#### 3. Create Event Source Helpers

Create `tools/Radio.Tools.AudioUAT/Utilities/EventSourceHelpers.cs`:
- WAV file generator for test tones
- Memory monitoring utility
- Event queue status display
- TTS provider helper (system TTS or Azure)

#### 4. Memory Monitoring

Implement memory tracking for stress tests:
```csharp
public class MemoryMonitor
{
    public long InitialMemoryMB { get; private set; }
    public long CurrentMemoryMB => GC.GetTotalMemory(false) / 1024 / 1024;
    public long DeltaMB => CurrentMemoryMB - InitialMemoryMB;
    
    public void Start() => InitialMemoryMB = CurrentMemoryMB;
    public bool IsStable(int thresholdMB = 50) => DeltaMB < thresholdMB;
}
```

#### 5. Update Main Menu

Update `Program.cs` to include Phase 4 menu option:
```
[3] Phase 4: Event Audio Sources
```

### Interactive Features for Phase 4

- Real-time notification queue display
- Memory usage graph during stress tests
- TTS preview before playback
- Countdown timer for scheduled events

### Success Criteria
- [ ] All 13 Phase 4 tests are implemented
- [ ] Sound effects play correctly with overlap support
- [ ] Notification priority system works correctly
- [ ] TTS generates and plays speech
- [ ] Scheduled events trigger at correct times
- [ ] Memory remains stable after stress tests
```

---

### Phase 5 Development Prompt

```markdown
## Task: Extend Audio UAT Tool - Phase 5 (Ducking & Priority System Testing)

Extend the Radio.Tools.AudioUAT application to include Phase 5 testing for Ducking & Priority System.

### Context
- Reference `/PLAN.md` Phase 5 for Ducking & Priority details
- Ducking reduces background audio volume when priority audio plays
- Priority system determines which sources can interrupt others
- Build upon Phases 2-4 foundation

### Requirements

#### 1. Implement Phase 5 Tests

Create `tools/Radio.Tools.AudioUAT/Phases/Phase5/DuckingPriorityTests.cs`:

**Priority Tests:**
- **P5-001 Priority Assignment**: Assign priorities (1-10) to test sources, verify storage
- **P5-002 Priority Ordering**: Create sources with different priorities, verify `GetActiveSources()` ordering
- **P5-003 Duck on Event**: Start background music, trigger event, verify music ducks

**Ducking Tests:**
- **P5-004 Duck Level**: Configure duck to -6dB, -12dB, -18dB, verify attenuation
- **P5-005 Duck Ramp**: Test fade-down with 100ms, 250ms, 500ms ramps, verify smoothness (no clicks)
- **P5-006 Duck Release**: Trigger duck, end event, verify volume returns to original
- **P5-007 Duck Multiple Sources**: Start 3 background sources, duck all, verify all reduce
- **P5-008 Duck Nested**: During active duck, trigger second event, verify proper handling

**Priority Override Tests:**
- **P5-009 Priority Override**: Start low priority, then high priority, verify low ducks/pauses
- **P5-010 Priority Release**: High priority ends, verify low priority resumes

**Configuration Tests:**
- **P5-011 Duck Configuration**: Modify duck dB, ramp time, verify next duck uses new values

**Integration Tests:**
- **P5-012 Announcement Duck**: Play music, trigger TTS announcement, verify music ducks during speech
- **P5-013 No Duck Sources**: Mark source as duck-exempt, verify it doesn't duck

#### 2. Create Ducking Visualization

Create `tools/Radio.Tools.AudioUAT/Utilities/DuckingVisualizer.cs`:
- Display real-time volume levels for all sources
- Show duck state (active/inactive)
- Visualize volume ramps
- Display priority hierarchy

Example visualization:
```
╔═══════════════════════════════════════════════════════════════════╗
║                    DUCKING STATUS                          ║
╠═══════════════════════════════════════════════════════════════════╣
║  Source          Priority   Volume    State                ║
╠═══════════════════════════════════════════════════════════════════╣
║  TTS Announcement   10       100%     ▶ PLAYING            ║
║  Background Music    3        25%     ▼ DUCKED (-12dB)     ║
║  Vinyl Input         5        25%     ▼ DUCKED (-12dB)     ║
╠═══════════════════════════════════════════════════════════════════╣
║  Duck Active: YES   Ramp: ████████░░ 80%                   ║
╚═══════════════════════════════════════════════════════════════════╝
```

#### 3. Create Test Scenarios

Implement predefined test scenarios:
```csharp
public class DuckingScenario
{
    public string Name { get; init; }
    public List<SourceConfig> BackgroundSources { get; init; }
    public List<TriggerEvent> TriggerEvents { get; init; }
    public DuckConfiguration DuckConfig { get; init; }
}

// Predefined scenarios
var scenarios = new[]
{
    new DuckingScenario 
    { 
        Name = "Simple Announcement",
        BackgroundSources = [new("Music", priority: 3)],
        TriggerEvents = [new(TriggerType.TTS, "Test announcement", delayMs: 2000)],
        DuckConfig = new(-12, rampMs: 250)
    },
    // More scenarios...
};
```

#### 4. Update Main Menu

Update `Program.cs` to include Phase 5 menu option:
```
[4] Phase 5: Ducking & Priority
```

### Audio Quality Checks

Implement audio quality verification:
- Click/pop detection during ramps
- Volume level accuracy verification
- Timing accuracy for ramps

### Success Criteria
- [ ] All 13 Phase 5 tests are implemented
- [ ] Priority ordering works correctly
- [ ] Ducking reduces volume smoothly without artifacts
- [ ] Duck levels are accurate (-6dB, -12dB, -18dB)
- [ ] Nested duck events handled properly
- [ ] Duck-exempt sources work correctly
- [ ] Real-time visualization displays correct states
```

---

### Phase 6 Development Prompt

```markdown
## Task: Extend Audio UAT Tool - Phase 6 (Audio Outputs Testing)

Extend the Radio.Tools.AudioUAT application to include Phase 6 testing for Audio Outputs (Multi-device, Chromecast, HTTP Streaming).

### Context
- Reference `/PLAN.md` Phase 6 for Audio Outputs details
- Build upon Phases 2-5 foundation
- This phase tests final output routing and external streaming

### Requirements

#### 1. Implement Phase 6 Tests

Create `tools/Radio.Tools.AudioUAT/Phases/Phase6/AudioOutputTests.cs`:

**Multi-Device Tests:**
- **P6-001 Multi-Device Output**: Enable output to 2+ devices simultaneously, verify all play
- **P6-002 Device-Specific Volume**: Set different volumes per device, verify independence
- **P6-003 Device Routing**: Route specific source to specific device, verify correct routing

**Chromecast Tests:**
- **P6-004 Chromecast Discovery**: Scan network for Chromecast devices, display found devices
- **P6-005 Chromecast Connect**: Select and connect to Chromecast, verify connection
- **P6-006 Chromecast Stream**: Stream audio to connected Chromecast, verify playback
- **P6-007 Chromecast Disconnect**: Disconnect cleanly, verify local audio resumes

**HTTP Streaming Tests:**
- **P6-008 HTTP Stream Start**: Start HTTP stream server on configurable port
- **P6-009 HTTP Stream Connect**: Connect with test client, verify audio data received
- **P6-010 HTTP Stream Multi-Client**: Connect 3 clients, verify all receive identical data
- **P6-011 HTTP Stream Format**: Verify content-type, bitrate, sample rate headers

**Diagnostics Tests:**
- **P6-012 Latency Measurement**: Measure end-to-end latency for each output
- **P6-013 Sync Test**: Play to multiple outputs, measure sync offset
- **P6-014 Failover**: Simulate primary output failure, verify backup takes over

#### 2. Create HTTP Stream Test Client

Create `tools/Radio.Tools.AudioUAT/Utilities/HttpStreamTestClient.cs`:
```csharp
public class HttpStreamTestClient
{
    public async Task<StreamTestResult> TestStreamAsync(string url, int durationSeconds = 10)
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        
        var result = new StreamTestResult
        {
            StatusCode = response.StatusCode,
            ContentType = response.Content.Headers.ContentType?.ToString(),
            BytesReceived = 0
        };
        
        using var stream = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[4096];
        var sw = Stopwatch.StartNew();
        
        while (sw.Elapsed.TotalSeconds < durationSeconds)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0) break;
            result.BytesReceived += read;
        }
        
        result.BitrateKbps = (result.BytesReceived * 8 / 1000) / durationSeconds;
        return result;
    }
}
```

#### 3. Create Chromecast Discovery

Create `tools/Radio.Tools.AudioUAT/Utilities/ChromecastHelper.cs`:
- Implement mDNS/DNS-SD discovery for Chromecast devices
- Display device name, IP, model
- Handle connection state changes

#### 4. Create Latency Measurement

Create `tools/Radio.Tools.AudioUAT/Utilities/LatencyMeter.cs`:
- Generate impulse/click at known time
- Detect impulse at output
- Calculate round-trip latency
- Display results in table format

#### 5. Create Output Visualization

```
╔═══════════════════════════════════════════════════════════════════╗
║                      AUDIO OUTPUT STATUS                           ║
╠═══════════════════════════════════════════════════════════════════╣
║  Output               Type        Status      Volume    Latency   ║
╠═══════════════════════════════════════════════════════════════════╣
║  Built-in Audio       Local       ● Active      80%      12ms    ║
║  USB DAC              Local       ● Active      65%      8ms     ║
║  Living Room          Chromecast  ● Streaming   70%      ~850ms  ║
║  HTTP Stream :8080    Network     ● 2 clients   N/A      varies  ║
╠═══════════════════════════════════════════════════════════════════╣
║  Total Active Outputs: 4                                           ║
╚═══════════════════════════════════════════════════════════════════╝
```

#### 6. Update Main Menu

Update `Program.cs` to include Phase 6 menu option:
```
[5] Phase 6: Audio Outputs
```

### Network Test Features

- Display stream URL with QR code for mobile testing
- Show connected client IPs and connection duration
- Network bandwidth usage display

### Success Criteria
- [ ] All 14 Phase 6 tests are implemented
- [ ] Multi-device output works with independent volumes
- [ ] Chromecast discovery and streaming functional
- [ ] HTTP stream server handles multiple clients
- [ ] Latency measurement provides accurate results
- [ ] Sync test identifies timing offsets
- [ ] Failover mechanism works correctly
```

---

### Complete Tool Finalization Prompt

```markdown
## Task: Finalize Audio UAT Tool - Complete Integration & Polish

Finalize the Radio.Tools.AudioUAT application with full integration, reporting, and polish.

### Context
- All phases (2-6) have been implemented
- This prompt focuses on integration, reporting, and user experience

### Requirements

#### 1. Create Comprehensive Test Report Generator

Create `tools/Radio.Tools.AudioUAT/Utilities/TestReportGenerator.cs`:
- Generate HTML report with all test results
- Include pass/fail statistics per phase
- Add timestamps and duration for each test
- Include system information (OS, audio devices, .NET version)
- Add screenshot/log attachments for failed tests

#### 2. Create Command-Line Interface

Support automated test runs:
```bash
# Run all tests
dotnet run -- --all

# Run specific phase
dotnet run -- --phase 3

# Run specific test
dotnet run -- --test P3-005

# Export results
dotnet run -- --all --output results.json --format json
dotnet run -- --all --output report.html --format html

# Continuous/regression mode
dotnet run -- --watch --phases 2,3,4
```

#### 3. Create Test Configuration System

Create `tools/Radio.Tools.AudioUAT/appsettings.json`:
```json
{
  "AudioUAT": {
    "TestTimeoutSeconds": 60,
    "AutoConfirmSubjectiveTests": false,
    "LogLevel": "Information",
    "ReportOutputPath": "./test-results",
    "TestAssets": {
      "AudioPath": "./Assets/test-audio",
      "ConfigPath": "./Assets/test-config.json"
    },
    "HttpStream": {
      "DefaultPort": 8080
    },
    "Chromecast": {
      "DiscoveryTimeoutSeconds": 10
    }
  }
}
```

#### 4. Create Welcome Screen & Help

Display on startup:
```
╔═══════════════════════════════════════════════════════════════════════════╗
║                                                                           ║
║   ██████╗  █████╗ ██████╗ ██╗ ██████╗      █████╗ ██╗   ██╗██████╗ ██╗ ██████╗ ║
║   ██╔══██╗██╔══██╗██╔══██╗██║██╔═══██╗    ██╔══██╗██║   ██║██╔══██╗██║██╔═══██╗║
║   ██████╔╝███████║██║  ██║██║██║   ██║    ███████║██║   ██║██║  ██║██║██║   ██║║
║   ██╔══██╗██╔══██║██║  ██║██║██║   ██║    ██╔══██║██║   ██║██║  ██║██║██║   ██║║
║   ██║  ██║██║  ██║██████╔╝██║╚██████╔╝    ██║  ██║╚██████╔╝██████╔╝██║╚██████╔╝║
║   ╚═╝  ╚═╝╚═╝  ╚═╝╚═════╝ ╚═╝ ╚═════╝     ╚═╝  ╚═╝ ╚═════╝ ╚═════╝ ╚═╝ ╚═════╝║
║                                                                           ║
║                        UAT TESTING TOOL v1.0                              ║
║                                                                           ║
║   Use this tool to validate audio functionality across all phases.        ║
║   For help, press [?] at any menu or run with --help                      ║
║                                                                           ║
╚═══════════════════════════════════════════════════════════════════════════╝
```

#### 5. Main Menu Integration

```
═══════════════════════════════════════════════════════════════════════════
                          MAIN MENU
═══════════════════════════════════════════════════════════════════════════

  SELECT TEST PHASE:
  
  [2] Phase 2: Core Audio Engine        ● 12 tests    ✓ 12 passed
  [3] Phase 3: Primary Audio Sources    ● 14 tests    ⚠ 13 passed, 1 skipped
  [4] Phase 4: Event Audio Sources      ● 13 tests    ○ Not run
  [5] Phase 5: Ducking & Priority       ● 13 tests    ○ Not run
  [6] Phase 6: Audio Outputs            ● 14 tests    ○ Not run
  
  ACTIONS:
  [A] Run All Tests
  [R] View Full Report
  [E] Export Results
  [S] System Information
  [C] Configuration
  
  [?] Help
  [Q] Quit

═══════════════════════════════════════════════════════════════════════════
  Session: 2024-01-15 14:32:05 | Tests Run: 26 | Pass: 25 | Fail: 0 | Skip: 1
═══════════════════════════════════════════════════════════════════════════
```

#### 6. Add System Information Display

Show audio system details:
```
╔═══════════════════════════════════════════════════════════════════╗
║                     SYSTEM INFORMATION                             ║
╠═══════════════════════════════════════════════════════════════════╣
║  Operating System:    Raspberry Pi OS (Debian 12)                  ║
║  .NET Version:        8.0.1                                        ║
║  SoundFlow Version:   1.2.3                                        ║
║  Audio Backend:       MiniAudio (ALSA)                             ║
╠═══════════════════════════════════════════════════════════════════╣
║  AUDIO DEVICES:                                                    ║
║  ┌─────────────────────────────────────────────────────────────┐  ║
║  │ [*] Built-in Audio (bcm2835)              Stereo   48kHz    │  ║
║  │ [ ] USB Audio Device (Focusrite)          Stereo   96kHz    │  ║
║  │ [ ] USB Audio Device (ION Turntable)      Stereo   44.1kHz  │  ║
║  └─────────────────────────────────────────────────────────────┘  ║
╚═══════════════════════════════════════════════════════════════════╝
```

### Success Criteria
- [ ] All phases accessible from unified main menu
- [ ] Test results persist across sessions
- [ ] HTML and JSON reports generated correctly
- [ ] Command-line automation works
- [ ] Configuration system functional
- [ ] Help available at all menus
- [ ] Professional, polished UI throughout
```

---

## Appendix: Test Asset Generation

The following code can be used to generate test audio files programmatically:

```csharp
public static class TestAudioGenerator
{
    public static void GenerateSineWave(string path, int frequency = 440, int durationMs = 1000, int sampleRate = 44100)
    {
        var samples = durationMs * sampleRate / 1000;
        var buffer = new short[samples];
        
        for (int i = 0; i < samples; i++)
        {
            var t = (double)i / sampleRate;
            buffer[i] = (short)(Math.Sin(2 * Math.PI * frequency * t) * short.MaxValue * 0.8);
        }
        
        WriteWavFile(path, buffer, sampleRate, 1, 16);
    }
    
    public static void GenerateSilence(string path, int durationMs = 1000, int sampleRate = 44100)
    {
        var samples = durationMs * sampleRate / 1000;
        var buffer = new short[samples];
        WriteWavFile(path, buffer, sampleRate, 1, 16);
    }
}
```

---

## Implementation Status

| Phase | Status | Tests | Notes |
|-------|--------|-------|-------|
| Phase 2 | ✅ Implemented | 12 tests | Core Audio Engine Tests |
| Phase 3 | ✅ Implemented | 14 tests | Primary Audio Sources Tests |
| Phase 4 | ✅ Implemented | 13 tests | Event Audio Sources Tests |
| Phase 5 | ✅ Implemented | 13 tests | Ducking & Priority Tests |
| Phase 6 | ✅ Implemented | 14 tests | Audio Outputs Tests |
| Phase 7 | ✅ Implemented | 15 tests | Visualization & Monitoring Tests |

### Phase 4 QA Verification Summary

The Phase 4 tests enable QA to verify:

1. **Sound Effect Playback** (P4-001 to P4-004)
   - Audio file loading from WAV, MP3, OGG, FLAC formats
   - One-shot playback with automatic cleanup
   - Rapid-fire overlapping instances
   - Per-event volume control

2. **Notification System** (P4-005 to P4-007)
   - Notification event creation and queuing
   - Priority-based ordering
   - Sequential playback without overlap

3. **Chime and Scheduling** (P4-008, P4-011, P4-012)
   - Scheduled audio event triggers
   - Event cancellation before trigger

4. **Text-to-Speech** (P4-009 to P4-010)
   - TTS engine availability detection (eSpeak, Google, Azure)
   - TTS audio generation and playback
   - TTS message queue processing

5. **Memory Stability** (P4-013)
   - Memory leak detection during heavy usage
   - Resource cleanup verification

### Running Phase 4 Tests

```bash
# Run all Phase 4 tests
cd tools/Radio.Tools.AudioUAT
dotnet run -- --phase 4

# Run specific test
dotnet run -- --test P4-009  # TTS Integration Test

# Interactive mode
dotnet run
# Then select "Phase 4: Event Audio Sources Tests" from the menu
```

### Phase 5 QA Verification Summary

The Phase 5 tests enable QA to verify:

1. **Priority Management** (P5-001, P5-002)
   - Source priority assignment (1-10 scale)
   - Priority ordering verification (highest first)
   - Default priorities for primary and event sources

2. **Ducking Behavior** (P5-003 to P5-008)
   - Automatic ducking when event audio triggers
   - Configurable duck levels (-6dB, -12dB, -18dB equivalents)
   - Smooth fade ramp transitions (FadeSmooth, FadeQuick, Instant)
   - Volume restoration after ducking completes
   - Multi-source ducking (all background sources duck simultaneously)
   - Nested ducking events (proper handling when multiple events overlap)

3. **Priority Override** (P5-009, P5-010)
   - Higher priority sources take precedence
   - Lower priority sources resume when higher priority completes
   - Clean handoffs between priority levels

4. **Configuration** (P5-011)
   - Dynamic configuration changes (DuckingPercentage, AttackMs, ReleaseMs)
   - Different ducking policies (FadeSmooth, FadeQuick, Instant)

5. **Integration** (P5-012, P5-013)
   - TTS announcement ducking with background music
   - Duck-exempt sources remain at original volume

### Running Phase 5 Tests

```bash
# Run all Phase 5 tests
cd tools/Radio.Tools.AudioUAT
dotnet run -- --phase 5

# Run specific test
dotnet run -- --test P5-003  # Duck on Event Test

# Interactive mode
dotnet run
# Then select "Phase 5: Ducking & Priority Tests" from the menu
```

### Phase 6 QA Verification Summary

The Phase 6 tests enable QA to verify:

1. **Multi-Device Output** (P6-001 to P6-003)
   - Simultaneous output to multiple audio devices
   - Per-device volume control with independence
   - Source-to-device routing configuration

2. **Chromecast Streaming** (P6-004 to P6-007)
   - Chromecast device discovery via mDNS
   - Connection establishment and authentication
   - Audio streaming to Chromecast devices
   - Clean disconnection with local audio resume

3. **HTTP Streaming** (P6-008 to P6-011)
   - HTTP audio stream server startup
   - Client connection and audio data reception
   - Multi-client support with identical streams
   - WAV format verification and headers

4. **Diagnostics** (P6-012 to P6-014)
   - End-to-end latency measurement per output
   - Multi-output synchronization testing
   - Output failover mechanism verification

### Running Phase 6 Tests

```bash
# Run all Phase 6 tests
cd tools/Radio.Tools.AudioUAT
dotnet run -- --phase 6

# Run specific test
dotnet run -- --test P6-004  # Chromecast Discovery Test

# Interactive mode
dotnet run
# Then select "Phase 6: Audio Outputs Tests" from the menu
```

### Phase 7 QA Verification Summary

The Phase 7 tests enable QA to verify:

1. **Spectrum Analyzer** (P7-001 to P7-004)
   - FFT initialization with configurable size (256-4096)
   - Accurate frequency detection via FFT processing
   - Correct frequency bin coverage (0 Hz to Nyquist)
   - Smooth spectrum display without jitter

2. **Level Meter (VU)** (P7-005 to P7-009)
   - Peak level detection for left/right channels
   - RMS calculation for average loudness
   - Clipping detection when signal exceeds threshold
   - Linear to dBFS conversion accuracy

3. **Waveform Display** (P7-010 to P7-012)
   - Circular buffer for time-domain samples
   - Separate stereo channel visualization
   - Peak-preserving downsampling for UI display

4. **Integration** (P7-013 to P7-015)
   - IVisualizerService full API verification
   - Real-time processing performance (< buffer duration)
   - State reset for source changes

### Running Phase 7 Tests

```bash
# Run all Phase 7 tests
cd tools/Radio.Tools.AudioUAT
dotnet run -- --phase 7

# Run specific test
dotnet run -- --test P7-002  # FFT Processing Test

# Interactive mode
dotnet run
# Then select "Phase 7: Visualization & Monitoring Tests" from the menu
```

---

## Phase 9: Audio Fingerprinting & Song Detection Testing

**Objective:** Validate audio fingerprinting, metadata lookup, play history, and unknown song detection.

### Test Cases

| ID | Test Case | Description | Expected Result |
|----|-----------|-------------|-----------------|
| P9-001 | Database Initialization | Initialize SQLite fingerprint database | All tables created successfully |
| P9-002 | Fingerprint Generation | Generate fingerprint from audio samples | Returns valid fingerprint with hash |
| P9-003 | Cache Store and Retrieve | Store and retrieve fingerprint from cache | Fingerprint stored and retrieved correctly |
| P9-004 | Metadata Storage | Store and retrieve track metadata | Metadata persists and retrieves correctly |
| P9-005 | Play History Recording | Record play history entry | Entry recorded with all fields |
| P9-006 | Play History Retrieval | Retrieve recent play history | Returns entries in descending order |
| P9-007 | Play Statistics | Calculate play statistics | Statistics calculated correctly |
| P9-008 | Audio Sample Capture | Capture audio samples from output stream | Samples captured with correct format |
| P9-009 | Duplicate Suppression | Test duplicate track suppression | Same hash stored only once |
| P9-010 | Unknown Track Handling | Handle unidentified tracks | Fingerprint stored for manual tagging |
| P9-011 | Metadata Search | Search track metadata | Search returns matching results |
| P9-012 | Cache Cleanup | Delete cached fingerprints | Fingerprint removed from cache |

### Interactive Test Menu

```
═══════════════════════════════════════════════════════════════
          PHASE 9: AUDIO FINGERPRINTING TESTS
═══════════════════════════════════════════════════════════════

  DATABASE:
  [1] Initialize Fingerprint Database
  [2] Generate Audio Fingerprint
  [3] Store and Retrieve Cache Entry
  
  METADATA:
  [4] Store Track Metadata
  [5] Search Metadata
  
  PLAY HISTORY:
  [6] Record Play History Entry
  [7] Retrieve Recent History
  [8] Calculate Statistics
  
  AUDIO CAPTURE:
  [9] Capture Audio Samples
  
  SPECIAL CASES:
  [10] Test Duplicate Suppression
  [11] Test Unknown Track Handling
  [12] Cache Cleanup Test
  [13] Run All Phase 9 Tests
  
  [R] View Test Results
  [M] Return to Main Menu

═══════════════════════════════════════════════════════════════
```

### Phase 9 QA Verification Summary

The Phase 9 tests enable QA to verify:

1. **Database Operations** (P9-001, P9-003, P9-012)
   - SQLite database initialization
   - Table creation (FingerprintCache, TrackMetadata, PlayHistory)
   - CRUD operations on fingerprint cache
   - Data persistence across sessions

2. **Fingerprint Generation** (P9-002, P9-009)
   - Audio fingerprint generation from samples
   - Hash uniqueness for different audio
   - Duplicate hash suppression

3. **Metadata Management** (P9-004, P9-011)
   - Track metadata storage (title, artist, album, etc.)
   - Metadata retrieval by ID and fingerprint
   - Text search across title, artist, album

4. **Play History** (P9-005, P9-006, P9-007)
   - Recording plays from various sources
   - Retrieving recent history
   - Statistics calculation (total, identified, by source)

5. **Audio Capture** (P9-008)
   - Capturing samples from SoundFlow output stream
   - Converting PCM bytes to float samples
   - Buffer duration accuracy

6. **Unknown Track Handling** (P9-010)
   - Storing unidentified fingerprints
   - Marking for manual tagging later

### Running Phase 9 Tests

```bash
# Run all Phase 9 tests
cd tools/Radio.Tools.AudioUAT
dotnet run -- --phase 9

# Run specific test
dotnet run -- --test P9-002  # Fingerprint Generation Test

# Interactive mode
dotnet run
# Then select "Phase 9: Audio Fingerprinting Tests" from the menu
```

### Configuration for Fingerprinting

Before running Phase 9 tests, ensure the following configuration is set in `appsettings.json`:

```json
{
  "Fingerprinting": {
    "Enabled": true,
    "SampleDurationSeconds": 15,
    "IdentificationIntervalSeconds": 30,
    "MinimumConfidenceThreshold": 0.5,
    "DuplicateSuppressionMinutes": 5,
    "DatabasePath": "./data/fingerprints.db",
    "AcoustId": {
      "ApiKey": "",
      "BaseUrl": "https://api.acoustid.org/v2",
      "MaxRequestsPerSecond": 3,
      "TimeoutSeconds": 10
    },
    "MusicBrainz": {
      "BaseUrl": "https://musicbrainz.org/ws/2",
      "ApplicationName": "RadioConsole",
      "ApplicationVersion": "1.0.0",
      "ContactEmail": "",
      "MaxRequestsPerSecond": 1,
      "TimeoutSeconds": 10
    }
  }
}
```

**Note:** To enable external API lookups (AcoustID/MusicBrainz), you need to:
1. Register for an AcoustID API key at https://acoustid.org/new-application
2. Add the key to the configuration or secrets

---

## Version History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-11-26 | GitHub Copilot | Initial phased plan |
| 1.1 | 2025-11-26 | GitHub Copilot | Phase 4 Event Audio Sources tests implemented |
| 1.2 | 2025-11-26 | GitHub Copilot | Phase 5 Ducking & Priority tests implemented |
| 1.3 | 2025-11-26 | GitHub Copilot | Phase 6 Audio Outputs tests implemented |
| 1.4 | 2025-12-02 | GitHub Copilot | Phase 7 Visualization & Monitoring tests implemented |
| 1.5 | 2025-12-02 | GitHub Copilot | Phase 9 Audio Fingerprinting & Song Detection tests implemented |

---

## Phase 10: Database Backup & Restore Testing

**Objective:** Validate unified database backup and restore functionality across all SQLite databases (configuration, metrics, fingerprinting).

### Configuration Requirements

Before running Phase 10 tests, ensure your `appsettings.json` includes the unified database configuration:

```json
{
  "Database": {
    "RootPath": "./data",
    "ConfigurationSubdirectory": "config",
    "ConfigurationFileName": "configuration.db",
    "MetricsSubdirectory": "metrics",
    "MetricsFileName": "metrics.db",
    "FingerprintingSubdirectory": "fingerprints",
    "FingerprintingFileName": "fingerprints.db",
    "BackupSubdirectory": "backups",
    "BackupRetentionDays": 30
  },
  "ManagedConfiguration": {
    "DefaultStoreType": "Sqlite",
    "BasePath": "./config",
    "SqliteFileName": "configuration.db",
    "AutoSave": true
  },
  "Metrics": {
    "Enabled": true,
    "DatabasePath": "./data/metrics.db"
  },
  "Fingerprinting": {
    "Enabled": true,
    "DatabasePath": "./data/fingerprints.db"
  }
}
```

### Test Cases

| ID | Test Case | Description | Expected Result |
|----|-----------|-------------|-----------------|
| P10-001 | Database Path Resolution | Verify all database paths resolve correctly | All paths use unified root directory |
| P10-002 | Create Full Backup | Create backup of all databases | Single archive with configuration, metrics, and fingerprinting databases |
| P10-003 | Restore from Backup | Restore databases from backup archive | All databases restored and data verified |
| P10-004 | Backup Cleanup | Test automatic cleanup of old backups | Old backups deleted based on retention policy |

### Interactive Test Menu

```
═══════════════════════════════════════════════════════════════
          PHASE 10: DATABASE BACKUP & RESTORE TESTS
═══════════════════════════════════════════════════════════════

  [1] Database Path Resolution
  [2] Create Full Backup
  [3] Restore from Backup
  [4] Backup Cleanup
  [5] Run All Phase 10 Tests
  
  [R] View Test Results
  [M] Return to Main Menu
  [Q] Quit
```

### Running Phase 10 Tests

**Automated Mode:**
```bash
# Run all Phase 10 tests
dotnet run -- --phase 10

# Run specific test
dotnet run -- --test P10-001
```

**Interactive Mode:**
```bash
# Start interactive mode
dotnet run

# Navigate to: Phase 10: Database Backup & Restore Tests
```

### Test Details

#### P10-001: Database Path Resolution
- **Purpose**: Verify DatabasePathResolver correctly resolves paths for all databases
- **Validates**: 
  - All paths use configured root directory
  - Configuration, metrics, and fingerprinting paths are consistent
  - Backup directory is correctly configured
- **Output**: Table showing resolved paths for all databases

#### P10-002: Create Full Backup
- **Purpose**: Test unified backup creation across all databases
- **Pre-conditions**: Initializes test data in all three databases
- **Validates**:
  - Backup archive created successfully
  - All database files included in backup
  - Manifest and README included
  - File size is reasonable
- **Output**: Backup metadata including ID, size, and included databases

#### P10-003: Restore from Backup
- **Purpose**: Verify backup restore functionality
- **Process**:
  1. Finds most recent backup
  2. Restores all databases with overwrite
  3. Verifies restored data integrity
- **Validates**:
  - All databases restored successfully
  - Data integrity maintained
  - Application can access restored databases
- **Output**: Restore confirmation and verification results

#### P10-004: Backup Cleanup
- **Purpose**: Test automatic cleanup of old backups
- **Validates**:
  - Cleanup respects retention policy (BackupRetentionDays)
  - Old backups are deleted
  - Recent backups are preserved
- **Output**: Count of deleted backups

### Backup File Format

Backups are stored as `.dbbackup` files (ZIP format) with the naming pattern:
```
unified_YYYYMMDD_HHMMSS_XXXXXX.dbbackup
```

Example: `unified_20231204_143022_a1b2c3.dbbackup`

**Archive Contents:**
```
unified_20231204_143022_a1b2c3.dbbackup
├── databases/
│   ├── configuration.db
│   ├── metrics.db
│   └── fingerprints.db
├── manifest.json
└── README.txt
```

### Expected Results

**Successful Test Run:**
- ✅ P10-001: All paths resolve under unified root directory
- ✅ P10-002: Backup created with all 3 databases (typically 100-500 KB)
- ✅ P10-003: Databases restored and test data verified
- ✅ P10-004: Old backups cleaned up per retention policy

### Troubleshooting

**Issue**: Database path resolution fails
- **Solution**: Verify `Database.RootPath` is set in appsettings.json

**Issue**: Backup creation fails
- **Solution**: Ensure backup directory is writable and has sufficient disk space

**Issue**: Restore fails with "file in use" error
- **Solution**: Stop any running Radio Console instances accessing the databases

**Issue**: No backups found for cleanup test
- **Solution**: Run P10-002 first to create a backup

### Notes

- Phase 10 tests create real database files in `./data/` directory
- Backups are retained in `./data/backups/` for manual inspection
- Tests use SQLite for secrets provider to test full configuration database
- Backup archives include encrypted secrets (encrypted at rest)
- For production use, store backups in secure, off-site location

