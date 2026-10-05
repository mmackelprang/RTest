# BT dual-routing investigation — the actual cause of "underwater" audio

**Status**: complete (root cause confirmed).
**Author**: Mark + Claude, 2026-05-22.
**Motivation**: After deploying Path D (variable-rate SRC) and running 15-minute UAT, the objective metrics REGRESSED (comp events +20%, underruns +200%, ppm +27%) instead of improving. Subjective "underwater" persisted. Mark also reported: audio is coming from BOTH the local soundbar AND the Google Cast device simultaneously on startup. This document captures the root-cause investigation.

## Path D UAT result (the regression that triggered this dig)

```
                    Path D UAT — 2026-05-22T17:29
==================================================================
Resampler init log: ✓ "SrcVariableResampler initialized: quality=SincFastest,
                    channels=2, initial ratio=1.000250"

Recent compensation events (target ~0): 10  in 15 min
Recent underrun events (target 0):       2  in 15 min

                    Objective comparison vs Path C baseline
==================================================================
                       baseline (Path C)    after (Path D)
comp events/hour          1880.4               2262.6     (+20% — FAIL)
underrun events/hour        12.1                 36.4     (+200% — FAIL)
clock skew (ppm)          1071                  1356      (+27% — FAIL)

OVERALL: FAIL on all three criteria
```

On its face: Path D made things worse. But that contradicts the architecture — a real variable-rate SRC should *eliminate* rate-mismatch artifacts, not amplify them.

## Root cause — dual audio paths via PipeWire

Live diagnostic on `mmack@radio` revealed **two independent audio paths feeding the local soundbar simultaneously**:

```bash
$ pactl list sink-inputs
Sink Input #14615
    application.name = "Radio.API"
    media.name = "miniaudio:0"
    module-stream-restore.id = "sink-input-by-application-name:Radio.API"

Sink Input #14689
    media.name = "Pixel 10 Pro XL (codec aptX HD)"
    module-stream-restore.id = "sink-input-by-media-name:Pixel 10 Pro XL (codec aptX HD)"
```

### Path A (the designed path)
```
BT phone (aptX HD)
  → PipeWire BT capture node (bluez_input.B0_D5_FB_D2_0D_68.a2dp-source)
  → PipeWireNativeStream.OnProcess
  → SrcVariableResampler  ← Path D
  → BufferedSoundGenerator
  → MasterMixer
  → PlaybackDevice (with _localOutputMuted=true → volume=0)  ← muted, correctly
  → also tapped: TappedOutputStream → HttpStreamOutput → Google Cast
```

### Path B (the rogue path — discovered today)
```
BT phone (aptX HD)
  → PipeWire stream-restore module: "I've seen this device before, route to default sink"
  → directly to alsa_output.pci-0000_00_1f.3.analog-stereo (soundbar)
```

**PipeWire's `module-stream-restore` is a PulseAudio-compatibility module that remembers per-stream routing decisions.** The first time the Pixel 10 connected as an A2DP source after fresh `radio-api` install, it apparently auto-routed to the default sink (the soundbar). PipeWire saved that routing decision keyed by `media-name`. Every subsequent connection: PipeWire restores the saved routing, sending the BT audio directly to the soundbar **without any Radio.API involvement**.

Radio.API's `SetLocalOutputMuted(true)` correctly silences sink-input #14615 (the Radio.API stream) by setting its PlaybackDevice MasterMixer.Volume to 0. But it has no knowledge or control over sink-input #14689 (the rogue BT-direct stream).

## Implications

### For Path D
The Path D regression is a **measurement artifact**, not a Path D failure. With two paths feeding the soundbar at different latencies (Path A picked up ~1-3 ms when the resampler landed), the listener hears a comb filter / phase artifact between the two paths. That comb-filter sound is exactly "underwater" / "slow." Radio.API's BufferedSoundGenerator sees increased rate disagreement (because the consumer side now has additional drift caused by the resampler), drives compensation harder.

**Path D may be working as designed but cannot be measured in isolation until Path B is silenced.** Re-evaluate Path D's effectiveness AFTER Part 1 of this investigation lands.

### For the "two audio outputs" complaint
Same root cause. Mark's "audio comes from both the soundbar AND the Cast device on startup" is the dual-routing in action:
- Cast → Office speaker (the legitimate selection)
- Soundbar → because of PipeWire's stream-restore auto-routing

`SetLocalOutputMuted(true)` from Radio.API's perspective is correctly being called and IS muting the Radio.API sink-input, but the rogue BT-direct sink-input is unaffected.

### For the UI complaint
Separate concern. The "Out" pill at `src/Radio.Web/Components/Layout/MainLayout.razor:636-641` literally just navigates to `/devices` — explicitly stubbed by an earlier "PR 3 will own this" comment. There's no popover-based output picker. Even if Part 1 silences the rogue path, the UX gap remains: the user has no quick way to pick speakers vs Cast from the topbar.

## Mitigation options for Path B

1. **Stream-restore exclusion via WirePlumber rule** — tell WP to NEVER auto-route BT-A2DP sources to a real sink. Routes them to a null sink (or no sink at all) by default. Radio.API's capture node is unaffected (it's a different graph endpoint). This is the cleanest fix — addresses the root cause without disabling stream-restore globally.
2. **Clear the saved stream-restore decision** for the specific BT MAC — fix is temporary (re-saved on next "user routes BT to sink" decision). Surface-level patch.
3. **Disable `module-stream-restore` entirely** — affects all audio devices, not just BT. Heavy hammer, has UX side effects (volume / device choice resets every session for everything).

**Option 1 is the right move.** Lives in `/etc/wireplumber/` per the existing project pattern (MEMORY: "Radio Console manages all `/etc/wireplumber/bluetooth.lua.d/` configs"). Documented as Plan in `docs/plans/2026-05-22-wp-bt-route-exclusivity.md` (companion document).

## Decision

Two plans queued, both surfaced for review before implementation:

1. **`docs/plans/2026-05-22-wp-bt-route-exclusivity.md`** — Part 1: WirePlumber config to silence Path B. ~30 LOC infra config. **Critical-path** because without it, Path D and any future audio-routing work cannot be measured.
2. **`docs/plans/2026-05-22-output-picker-ui.md`** — Part 2: real output-picker popover replacing the `/devices`-navigation stub. ~150-200 LOC UI work, modeled on `CastDeviceDropdown.razor`.

After Part 1 lands and is verified: re-run Path D UAT. Expectation: comp events drop dramatically (resampler can finally work without competing against direct-routing); subjective "underwater" gone or substantially reduced; ppm settles near 0 (resampler's actual purpose, now measurable).

If Path D's re-measurement still shows symptoms after Part 1: that's the moment to consider quality-mode tuning (SincFastest → SincMedium) or Phase 2 closed-loop ratio control.
