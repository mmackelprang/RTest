# AUD-6 - choosing the stable output-device key

**Status:** `[INVESTIGATION - NOT IMPLEMENTED. Needs an owner decision before code.]`
**Date:** 2026-09-01 - **Context:** P0, blocks `AUD-7` hard (O2)

## Why this stopped short of a fix

`AUD-6` is the one P0 in the punch list whose fix **rewrites persisted user preferences**. The punch
list scopes it as *"a stable key **plus a migration** - every existing store holds ordinals."*
Choosing the key wrong does not fail loudly: it silently points a saved preference at the wrong
speaker, which is the exact failure `AUD-6` exists to remove. So the analysis is here and the
migration is not.

## The defect, confirmed against the code

- `SoundFlowDeviceManager.cs:517` mints `Id = $"playback-{i}"` straight off the enumeration index,
  and that string is what reaches storage.
- Resolution back to hardware is **pure string parsing, not a lookup**:
  `SoundFlowAudioEngine.GetDeviceIndexById:1030-1043` is an `int.TryParse` and nothing else.
- Measured previously across one deploy restart: on Aug 10 `playback-1` meant the soundbar; on
  Aug 11 it meant USB Audio Out. **Nothing in the store changed - the meaning did.**

## What identity SoundFlow actually offers

`SoundFlow.Structs.DeviceInfo` exposes exactly four members: `Id`, `Name`, `IsDefault`,
`SupportedDataFormats`. `Id` is documented only as *"The unique identifier for the device."*

**This repo has never read it.** Both `device.Id` references in `SoundFlowDeviceManager` (`:227`,
`:358`) are our own `AudioDeviceInfo.Id` - the ordinal string - not SoundFlow's. So the native id is
present, unused, and **unverified on this hardware**.

That matters because MiniAudio's underlying `ma_device_id` is a **union whose shape is
backend-specific**: a string-ish id on ALSA, a wide-char id on WASAPI, and on some backends a value
that is only meaningful within the current process. Whether SoundFlow surfaces something
serializable *and* stable across a restart is the open question, and it cannot be answered by
reading our own source - it needs a probe on the box.

## Options

| Option | Stable across restart? | Survives identical devices? | Risk |
|---|---|---|---|
| **A. Native `DeviceInfo.Id`** | **Unknown - must be probed** | Presumably yes | If it is a process-local handle, the store breaks worse than today |
| **B. `RawName`** | Yes - ALSA/WASAPI names are stable | **No** - two identical devices collide | Collision is real here; the box has multiple USB audio paths |
| **C. `RawName` + disambiguator** (USB port already extracted at `:526`) | Yes | Mostly | More moving parts, and the disambiguator is itself derived from a name |
| **D. Composite with fallback** - try native id, fall back to name, record both | Yes | Yes | Most code, but degrades rather than breaks |

## Recommendation

**Probe first, then D.** One check on the box answers whether Option A is viable, and the answer
changes the design rather than merely the implementation: enumerate twice across a service restart
and compare the native ids. If they match, A is on the table; if they are process-local handles, it
is not.

Storing **both** the native id and the raw name, resolving by native id and falling back to name,
means a future enumeration change degrades to "we found it by name" rather than "we selected the
wrong speaker". That is the direction to fail in, and it matches how the punch list treats the rest
of this workstream: *a screen left on is a nuisance, a screen that cannot be turned on is a service
call.*

## Migration note, for whoever implements it

Every existing store holds `playback-N`. The migration cannot resolve those correctly **after** an
enumeration change has already happened - the ordinal's meaning is already lost. So it must either
run once on a box whose enumeration still matches when the preference was saved, or accept that
pre-migration values resolve to the system default. The honest options:

1. Migrate on first boot after deploy, resolving the ordinal immediately and recording the stable
   key alongside it.
2. Treat any `playback-N` value as "unset" and fall back to the system default, forcing one
   re-selection by the owner.

**(2) is safer and should probably win**, because (1) silently bakes in whatever the enumeration
happens to be at migration time - and the whole defect is that that ordering is not trustworthy.
One re-selection in the UI is a smaller cost than a preference that is confidently wrong.

## Blocked on

An owner decision between A/B/C/D, and the probe that makes A answerable. `AUD-7` is blocked on this
landing (O2) - and shipping `AUD-7` first would convert today's **mis-report** into tomorrow's
**mis-route**.
