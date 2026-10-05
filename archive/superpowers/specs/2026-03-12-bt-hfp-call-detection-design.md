# BT HFP Call Detection & Phone Ring Integration

**Date:** 2026-03-12
**Status:** Approved

## Summary

When a BT-connected phone receives an incoming call, detect it via HFP call indicators and:
1. Ring the physical rotary phone (SIP INVITE to HT801)
2. Broadcast `CallStateChanged` / `IncomingCall` via SignalR (same format as SIP calls)
3. Radio Console handles TTS announcement and UI update automatically

## Architecture

The RotaryPhone server owns all call detection and ringing. Radio Console consumes events via the existing SignalR pipeline with zero new message types.

### Current Flow (SIP calls — working)
```
HT801 ATA detects call → SIP signaling → RotaryPhone.Server detects
  → SignalR "CallStateChanged"("Ringing", phoneNumber)
  → Radio.API PhoneCallIntegrationService receives
    → TTS "Incoming call from X"
    → Broadcasts to Web UI → PhonePage shows "Ringing"
```

### New Flow (BT HFP calls)
```
Cell phone receives call → HFP indicator to BlueZ → RotaryPhone.Server detects
  → Sends SIP INVITE to HT801 → Physical rotary phone rings
  → SignalR "CallStateChanged"("Ringing", phoneNumber)
  → Radio.API PhoneCallIntegrationService receives
    → TTS "Incoming call from X"
    → Broadcasts to Web UI → PhonePage shows "Ringing"
```

## Radio Console Changes (this repo)

### 1. Disable Ring Sound in PhoneCallIntegrationService

The ring sound file (`media/sounds/phone-ring.wav`) should NOT play through the radio speakers — the physical rotary phone handles ringing. Add config flag `PlayRingSound` (default: `false`) to `PhoneIntegrationOptions`.

**Files:**
- `src/Radio.Core/Configuration/PhoneIntegrationOptions.cs` — add `PlayRingSound` property
- `src/Radio.API/Services/PhoneCallIntegrationService.cs` — conditionally skip ring sound

### 2. No Other Changes Needed

The existing pipeline handles everything:
- `PhoneCallClient` receives `CallStateChanged` from RotaryPhone hub
- `PhoneCallIntegrationService` does TTS + broadcasts to Web UI
- `PhonePage` updates call state badge

## RotaryPhone Server Changes (separate repo)

See prompt file: `docs/superpowers/prompts/2026-03-12-rotaryphone-hfp-detection.md`

## Testing

- Deploy both services, connect phone via BT, call the phone
- Verify: rotary phone rings, TTS announces caller, PhonePage shows "Ringing"
- Verify: hanging up resets PhonePage to "Idle"
- Verify: existing SIP calls still work
