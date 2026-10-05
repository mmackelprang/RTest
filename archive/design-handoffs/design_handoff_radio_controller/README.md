# Handoff: Radio Controller — Tuner & Recognition

## What this is

A companion package to `design_handoff_radio_console/`. That earlier pass audited the whole Radio.Web shell (top bar, sources, queue, metrics, etc.) but explicitly **did not touch the radio source surfaces** — the tuner, the song-recognition table, and the gain popover only exist on real hardware (the device with the RTL-SDR plugged in), so they weren't part of the original screenshot set.

The user re-ran the device, captured those screens, and this package picks up where the earlier handoff stopped.

## Scope

Four screens, seven proposed changes:

1. **Radio Main Page** — the now-playing column when the active source is a tuner. Status pills are scattered across three corners; match confidence is invisible.
2. **Radio Control Page** — the centred tuner (band selector, frequency, signal meter, scan controls, AGC, memory bank).
3. **Radio Song Recognition** — the fingerprint match table that replaces the album art when "Searching" is the now-playing state.
4. **Radio Gain Control popover** — opens from the `0dB` pill; sets RTL-SDR analog gain.

## About the design files

- `Design Analysis.html` — long-form audit. Reads end-to-end like a design review; one section per finding, with current-state notes, severity, and proposed direction.
- `Handoff Canvas.html` (+ `design-canvas.jsx`, `mocks.jsx`, `app.jsx`) — pan/zoom canvas of seven before/after artboards plus one composed "Tuner page after" view. This is the visual spec.
- `IMPLEMENTATION.md` — developer script. Each finding mapped to the Razor file(s) it touches with concrete numbered steps.

The canvas mocks are styled in the same broadcast-console language as the live UI (DSEG amber LED, cyan accent, dark warm-black surface) — same token names where they exist in `design-system.css`.

## How to read the canvas

The first artboard ("Tuner page · 1920 × 720") is the **composed after-state** — everything below it explained as one screen. Use it as the reference image; use the per-finding artboards to see what changed and why.

## Status

All findings are `[PENDING REVIEW]` — drafted, but not approved. Mark each one `[APPROVED]` / `[NEEDS ITERATION]` / `[PARKED]` directly in `IMPLEMENTATION.md` before you ship anything.

## Files in this folder

- `README.md` — this file.
- `Design Analysis.html` — long-form audit.
- `Handoff Canvas.html` + `design-canvas.jsx` + `mocks.jsx` + `app.jsx` — visual specs.
- `IMPLEMENTATION.md` — per-finding developer script.

## Tokens

All values reference `src/Radio.Web/wwwroot/css/design-system.css`. The mocks use the same hex codes (`--surface-base #0D0D0F`, `--accent-primary #5CD4E8`, `--signal-amber #F0A830`, `--font-led DSEG14Classic-Bold`). No new tokens are introduced; if one is needed, add it to design-system.css §2 first and reference from there.
