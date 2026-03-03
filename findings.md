# Findings

## Research & Discoveries

### Feature 1: RotaryUsb — Rotary Encoder Integration

**Repo**: `mmackelprang/RotaryUsb` (C++/CircuitPython firmware, C# example)
**Hardware**: 4x KY-040 rotary encoders on Raspberry Pi Pico (RP2040)
**Communication**: USB HID (Generic HID mode recommended for dedicated apps)

**Protocol (Generic HID mode)**:
- VID `0xCAFE`, PID `0x4005` (C++ firmware)
- Usage Page `0xFF00`, Usage `0x01`
- 8-byte reports: `[ReportID=0x01, Enc1(int8), Enc2(int8), Enc3(int8), Enc4(int8), Buttons(bitmask), Reserved, Reserved]`
- Encoder values are signed relative deltas: +1 CW, -1 CCW per detent
- Buttons: bit0=Btn1, bit1=Btn2, bit2=Btn3, bit3=Btn4 (1=pressed)
- Reports only sent on state change, 10ms minimum interval

**Linux integration**: Use `HidSharp` NuGet (cross-platform, works via hidraw) — NOT `HidLibrary` (Windows-only)

**Parsing (C#)**:
```csharp
sbyte enc1 = (sbyte)data[1]; // -127 to +127
byte buttons = data[5];
bool btn1 = (buttons & 0x01) != 0;
```

---

### Feature 2: RotaryPhone — Phone Call Announcements

**Repo**: `mmackelprang/RotaryPhone` (.NET 9, ASP.NET Core + SignalR)
**What it does**: Rotary phone-to-cell phone bridge via Bluetooth HFP

**Integration point**: SignalR hub at `http://<host>:5555/hub`
**Key events**:
- `CallStateChanged(string phoneId, string state)` — states: "Idle", "Dialing", "Ringing", "InCall"
- `IncomingCall(string phoneId, string phoneNumber)` — caller phone number

**Caller name resolution**: REST API `GET /api/contacts` to look up phone number → name

**Note**: The `IncomingCall` SignalR event may need enhancement in RotaryPhone's `SignalRNotifierService` — currently only `CallStateChanged` is broadcast. The phone number may need to be fetched from call history API when state transitions to "Ringing".

**Integration sequence**:
1. Connect SignalR client to RotaryPhone hub
2. On `Ringing` state → play ring audio sample (looped event source with ducking)
3. Resolve caller name via contacts API
4. TTS: "Incoming call from [Name]" or "Unknown caller from [Number]"
5. On state change away from `Ringing` → stop ring audio

---

### Feature 3: GoogleBroadcast — CRITICAL ISSUE

**Repo**: `mmackelprang/GoogleBroadcast` — **This is a simulation/scaffold. No actual functionality.**
- All "broadcast" methods are `SimulateBroadcastCall` that just `Task.Delay(1000)` and log
- Device discovery returns hardcoded fake devices
- Google auth is initialized but never used
- The code appears to be AI-generated boilerplate (single commit from Copilot)

**Direction mismatch**: The repo is about SENDING broadcasts TO Google Home devices. The user wants to RECEIVE broadcasts. Neither direction has a public Google API:
- **Sending**: Possible via Google Assistant gRPC API workaround (send "broadcast [message]" as assistant text command). Requires OAuth2 user credentials (not service accounts).
- **Receiving**: No public API exists. Google Home broadcasts are closed-ecosystem — only played on Google Home/Nest speakers.

**Possible alternatives**:
1. Build a custom broadcast system using the existing Cast infrastructure
2. Use MQTT or a webhook-based notification system
3. Integrate with Home Assistant which has Google Assistant SDK integration
4. Abandon Google broadcast, implement a custom "house intercom" via Cast devices

---

## Existing Codebase Integration Points

### Audio Sources
- `IEventAudioSource` for ephemeral audio (TTS, ring sounds) with auto-ducking
- `IDuckingService` reduces primary source volume (priority 1-10 scale, event default=8)
- `ITTSFactory.CreateAsync(text, params)` → returns ready-to-play event source
- Pattern: create event source → start ducking → play → stop ducking on completion

### Input Devices
- **NO existing HID/serial/GPIO abstraction** — entirely greenfield
- USB handling is audio-only (capture devices via MiniAudio)
- Would need new `IInputDevice` interface + implementations

### System Config UI
- MudBlazor Material 3 with MudTabs layout
- Cards for metrics, dialogs for configuration
- Pattern: Razor component + injected API service + SignalR for real-time updates

### SignalR Infrastructure
- `AudioStateHub` + `AudioVisualizationHub` already exist
- `AudioStateUpdateService` broadcasts state changes
- Well-established pattern for adding new real-time events

## Key Files
| File | Role |
|------|------|
| `src/Radio.Core/Interfaces/Audio/IAudioSource.cs` | Audio source interfaces |
| `src/Radio.Core/Interfaces/Audio/IEventAudioSource.cs` | Event source (TTS, sounds) |
| `src/Radio.Infrastructure/Audio/Sources/Event/TTSEventSource.cs` | TTS implementation |
| `src/Radio.Infrastructure/Audio/Services/DuckingService.cs` | Audio ducking |
| `src/Radio.Infrastructure/Audio/Sources/Event/AudioFileEventSource.cs` | Audio file playback |
| `src/Radio.Web/Components/Pages/SystemConfigPage.razor` | System config UI pattern |
| `src/Radio.API/Services/AudioStateUpdateService.cs` | SignalR state broadcasting |

## Open Questions
- What should we do about Google Broadcast? (see critical issue above)
- Where will the RotaryPhone server be running? Same Ubuntu box or separate machine?
- Which firmware mode for RotaryUsb? (Generic HID recommended)
