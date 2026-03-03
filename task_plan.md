# Task Plan

## Objective
Integrate rotary encoders (Volume, Tuning, Source, Viz), phone call announcements (ring + TTS), and a generic notification receiver into the Radio Console.

## Phases

### Phase 1: Core interfaces & configuration
- [ ] RotaryEncoderOptions, PhoneIntegrationOptions config classes
- [ ] IRotaryEncoderService, IPhoneIntegrationService, IAnnouncementService interfaces
- [ ] Event args classes

### Phase 2: Announcement Service (shared foundation)
- [ ] AnnouncementService: TTS + ducking orchestration
- [ ] First real consumer of IDuckingService

### Phase 3: Notification Controller
- [ ] POST /api/notifications/announce endpoint
- [ ] Validates + calls AnnouncementService

### Phase 4: Rotary Encoder Service
- [ ] HidRotaryEncoderService: HidSharp USB HID reader
- [ ] RotaryEncoderActionRouter: encoder events → audio actions
- [ ] VisualizationModeService: tracks viz mode
- [ ] RotaryEncoderHostedService: BackgroundService

### Phase 5: Phone Call Integration
- [ ] PhoneCallClient: SignalR client to RotaryPhone hub
- [ ] PhoneContactLookupService: REST client for contacts
- [ ] PhoneCallIntegrationService: ring + TTS on incoming calls

### Phase 6: Configuration & Wiring
- [ ] NuGet packages (HidSharp, SignalR.Client)
- [ ] DI registration in AudioServiceExtensions
- [ ] Program.cs hosted services
- [ ] appsettings.json config sections

### Phase 7: Web UI Updates
- [ ] SignalR handlers for new events
- [ ] System config page tabs for encoder/phone status

### Phase 8: Testing & Deployment
- [ ] Build: 0 warnings
- [ ] Tests: all pass
- [ ] Deploy + functional testing

## Decisions Log
| # | Decision | Rationale | Date |
|---|----------|-----------|------|
| 1 | GoogleBroadcast → Generic notification receiver | GoogleBroadcast repo is simulated, no receive API exists | 2026-03-03 |
| 2 | Encoder Layout A: Vol/Tune/Source/Viz | Most intuitive vintage radio UX | 2026-03-03 |
| 3 | RotaryPhone on same Ubuntu box (localhost:5555) | Simplifies networking, single machine | 2026-03-03 |
| 4 | AnnouncementService shared by phone + notifications | DRY: same TTS+ducking pattern | 2026-03-03 |

## Errors Encountered
| # | Error | Resolution | Date |
|---|-------|------------|------|

## Current Status
**Phase:** 0 (planning)
**Blocked:** No
