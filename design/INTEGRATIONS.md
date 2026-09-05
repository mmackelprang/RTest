# Integrations Setup Guide

This guide covers step-by-step setup for the three external integration systems: **Rotary Encoders**, **Phone Call Notifications**, and the **Announcement API**.

The Radio Console operates fully without any of these. **They do not all default the same way, and the
difference matters:** the **phone notification** and **announcement** integrations are opt-in and stay off until
configured, but the **rotary encoders default to on and are decided by presence** — `ENC-0` replaced the old gate
with detection, so plugging the device in is what enables it. `RotaryEncoder:Enabled` remains as an escape hatch
for turning a working device off, not as a switch you must find before the knobs will work.

---

## 1. Rotary Encoders (USB HID)

Physical rotary encoders connected via a Raspberry Pi Pico running custom firmware, exposed as a USB HID device. The cabinet engraves the four knobs, top to bottom, **VOLUME / SOURCE / PRESETS / TUNING**, and since `ENC-7` **all four are wired to that engraving**.

### Hardware Requirements

- **Raspberry Pi Pico** (or Pico W) with CircuitPython/C firmware
- **4x KY-040 rotary encoders** (with push buttons) wired to the Pico
- USB cable connecting the Pico to the Radio Console host

The Pico firmware must present itself as a USB HID device with:
- **Vendor ID:** `0xCAFE` (51966)
- **Product ID:** `0x4005` (16389)
**Report format** (rewritten by `ENC-1` against the live device's HID report descriptor; the source of truth is
`RotaryEncoderDecoder.cs` and `RotaryEncoderConfigCodec.cs`). All values are little-endian. Offsets below are
**payload** offsets — the buffer index is offset + 1, because byte 0 is the report ID.

- **Report `0x01` — positions, device to host.** 36-byte payload (37 bytes on the wire).
  - Payload 0-15: four `int32` clamped positions, one per encoder.
  - Payload 16: button bitmask, bit N = encoder N (1 = pressed).
  - Payload 17-19: reserved.
  - Payload 20-35: four `int32` **free-running movement accumulators**. The host takes the difference
    between consecutive reports; the accumulator wraps at 32 bits by design and two's-complement
    subtraction reads straight across the wrap.
  - Pre-accumulator firmware sends a 21-byte payload instead. Positions and buttons parse identically;
    movement is simply absent, and the host detects which form it has from the report length.
  - ⚠ **The first `0x01` report after every connect is a baseline and is discarded.** Accumulators are
    free-running, so without this a replug would deliver every detent turned since power-on to the
    volume knob at once.
- **Report `0x02` — configuration, both directions.** 106-byte payload: a 2-byte global header (format
  version, then a flags byte whose bit 0 selects 4 steps/detent when clear and 2 when set), followed by
  four 26-byte encoder blocks. The host pushes this on connect and reads it back to verify (`ENC-11`).
- **Report `0x03` — commands, host to device.** 2-byte payload.

### Encoder Mapping

> **The app serves this table from the router itself** (`ENC-8`), so the live version is always correct:
> **System Config → Integrations → Rotary Encoders → Encoder Mapping**. The copy below is a convenience
> snapshot and is the one that can go stale — prefer the UI. There is deliberately only one source of
> truth in code: `RotaryEncoderActionRouter.Mapping`, which is the same array dispatch runs through.

The knobs are a vertical column to the **left of the LCD**, so index 0 is the **topmost** and index 3 the
**bottommost**.

| Encoder | Engraved | Turn Action | Button Press | Long-press (600 ms) |
|---------|----------|-------------|--------------|---------------------|
| 0 | **VOLUME** | Volume up/down (configurable step %); a turn while muted **unmutes and applies the delta in the same frame** | Mute toggle | **Enter standby** |
| 1 | **SOURCE** | Opens the SOURCE overlay and moves the highlight one entry per detent. **Nothing switches** — preview only | Commits the highlighted entry. With the overlay closed it opens on the current entry, so a press commits what is already playing | *none* |
| 2 | **PRESETS** | Opens the PRESETS overlay and moves the highlight one entry per detent. **Nothing plays** — preview only | Recalls the highlighted station: switches source and band if needed, tunes, and plays. With the overlay closed it opens instead | **Save what is playing** |
| 3 | **TUNING** | Frequency step up/down when Radio is active (accelerated ×2/×4/×8, host-clamped to 8 steps per event); publishes a "no track control on this source" card otherwise | Start/stop frequency scan | *none* |

**There are exactly two long-presses on the whole panel, and there is deliberately no third:**
VOLUME → standby and PRESETS → save. SOURCE and TUNING have no long action at all, and they publish no
progress ring either — a ring that fills and then does nothing is a promise the code does not keep.

**The engraving mismatch is closed.** `ENC-5` remapped indices 1 and 3 onto SOURCE and TUNING and parked the
visualiser on 2 as a seat-warmer; `ENC-7` replaced it with PRESETS. Leaving the *old source cycler* on index 2
was rejected along the way: index 1 opens the SOURCE overlay, so a cycler beside it would have given two
adjacent knobs two divergent copies of the source selection — the defect the encoder handoff §4.4 spends a
paragraph forbidding. **The Settings page computes its "does not match the cabinet" warning per knob**, by
keyword-matching each row's turn description against its engraved name, so it now names nothing. ⚠ That is a
live coupling: rewording an entry in `RotaryEncoderActionRouter._mapping` so it no longer contains its
engraving's keyword relights the banner on a knob that is correct.

**PRESETS saves; it never overwrites.** A hold appends to the existing bank — the same bank the touchscreen
shows — and reports the per-band slot the bank then derives. Replacement and deletion stay on the touchscreen
behind the kebab, where they have a confirmation and an undo. Three boundaries are reported rather than
swallowed: `Only radio stations can be saved` on a non-radio source, `ALREADY SAVED · slot NN` for a station
already in the bank, and `PRESETS FULL` at the 50-preset cap.

**Visualization kept its capability and lost its only writer — both halves matter.** `ENC-7` removed
`VisualizationModeService` from the router. The **capability is unaffected**: Home's six-segment picker changes
the mode through `VisualizerPanel`'s own state and its saved preference, and never went through that service,
so choosing a visualisation is still something the touchscreen does (encoder handoff §11). (The System Config
"Visualizer" tab is FFT size / smoothing / peak-hold — it has never had a mode control.)

**The `VisualizationModeChanged` chain is gone — `ENC-9` deleted it (2026-09-03).** The encoder was the only
caller of `VisualizationModeService.CycleMode` / `ToggleEnabled`, so `ModeChanged` could no longer be raised,
the broadcast could never be sent, and the browser subscription behind it was inert. Removed together: the
service and its registration, `AudioStateUpdateService`'s subscription and broadcast, `AudioStateHubService`'s
client-side event and `_hubConnection.On` handler, `VisualizationModeDto`, and `VisualizerPanel`'s
subscription. **Visualiser mode is single-surface and local-only by decision.**

⚠ **What that costs, stated precisely:** there is no cross-circuit visualiser-mode sync — change the mode on
the kiosk and a phone browsing the same console will not follow. But *the picker never produced that sync
either*: the knob was the only writer, so no user ever had it from the picker. What is gone is the
**mechanism**, not a behaviour anyone was using, and rebuilding it means writing a **writer** rather than
re-adding a listener. `design/FUTURE-WORK.md` § 17 lists exactly what was removed.

**The device-side configuration table has always been in cabinet order** (`RotaryEncoderConfigDefaults.Create()`),
so acceleration-disabled lands on the two selector knobs and the tuning tiers `(150 ×2 / 80 ×4 / 40 ×8)` land on
TUNING. Before `ENC-5` those settings were applied to the wrong knobs; they are correct now — which is why tuning
acceleration became reachable for the first time in that row.

**A card always renders beside the knob that produced it**, because the HUD's geometry keys off the encoder index
the event arrived on rather than off this table, and handlers take that index as a parameter instead of
hard-coding it. A future remap moves the word without moving the card.

### On-screen feedback — the `EncoderHud` (ENC-4)

Every knob movement puts a transient card on screen **beside the knob that produced it, at the same height** —
anchored to the screen's left edge at `left: 24px`, banded down the 720 px axis at **90 / 270 / 450 / 630**.
Geometry keys off the encoder index; the words key off whatever the router's handler produced, so the card is in the
right place before it says the right word.

⚠ **`ENC-4` originally shipped this on the horizontal axis** — quarters of the 1920 px width, centres
240 / 720 / 1200 / 1680, bottom-anchored — which was correct for the knob row Rev 3 of the handoff described. The
as-built drawing puts the four knobs in a **vertical column to the LEFT of the LCD** on a uniform 29.63 mm pitch, so
the axis was wrong and all four constants with it. Rotated 90° in `ENC-4c`; the principle (the readout appears where
the knob is, so nobody has to be told which knob is which) is unchanged.

**The bands are facts about the panel, and they have one definition:** `Radio.Core.Configuration.FrontPanelGeometry`,
which also carries the engraved names, the index→knob mapping and the drawing's px→mm scale, citing
`design/hardware/front-panel-layout_4.svg`. Four surfaces need them — this HUD, the diagnostics card, the encoder
Settings table and the two selector overlays — so **a recut moves one line, not five.** The component's inline style
carries only `--encoder-band-y`; the left offset, the vertical centring on the band and the ≥ 8 px viewport clamp
are all in the `.encoder-hud` rule. `90 / 270 / 450 / 630` are the measured projections (93.05 / 271.02 / 448.98 /
626.95) **deliberately rounded** — 3.05 px of worst-case deviation is 0.508 mm on the panel, against a nearest wrong
band 178 px away; do not "restore" the measured values.

**Transient occlusion is accepted on every band** and nothing about position, width or z-order changes for it. The
card is up `EncoderInteractionTimings.HudHoldMs` — 2500 ms since `ENC-20`, raised from 1500 — after the last
detent, carries `pointer-events: none`, and appears only while a hand is on a knob. That is a tail, not a lifetime:
the timer re-arms on every detent, so the card is really up for the turn plus the hold.
The VOLUME band lands on the fixed topbar and covers `ENC-4a`'s `MUTED` chip; that is safe **only because every card
in the VOLUME band carries the console's mute state**, which is an invariant rather than a coincidence — a future
card at index 0 that omitted `IsMuted` would hide the reason the room is silent while saying nothing about it.

**One entrance animation is a declared exception, not drift.** A left-anchored card cannot enter on
`snackbarSlideIn`'s `translateY(100%)`, so handoff §6.1 authorises a mirrored horizontal pair — same duration, same
easing, no new token — scoped to `.encoder-hud` in the Normal variant. **Do not "correct" `.encoder-hud-enter` back
to `.snackbar-enter`.** The Sleep variant is placed by the anti-burn-in drift wrapper rather than by an edge and
deliberately still uses the original.

One component, two hosts: `EncoderHud.razor` mounts in `MainLayout` for every normal route, and again inside
`Sleep.razor` with `Variant="Sleep"` (centred, dim amber, inside the anti-burn-in drift wrapper), because `/sleep` is
on `EmptyLayout` and `MainLayout` is not in that tree.

**Two behaviour changes worth knowing at the cabinet:**

- **The short button press now fires on RELEASE, not on press.** Firing on press would fire it on the way into every
  long-press hold.
- **A long press (600 ms) fires AT the threshold while the button is still held**, and the release afterwards does
  nothing. Only encoder 0 has a long action wired (→ Standby); a >600 ms hold on encoders 1–3 now performs **no
  action at all**, where it previously acted on press.

**Turning the volume knob while muted unmutes and applies the delta in the same frame** (`ENC-4b`).

The 600 ms threshold has **one definition**, `Radio.Core.Configuration.EncoderInteractionTimings`, shared by the
host-side synthesis in `Radio.Infrastructure` and by `RadioControlPanel` in `Radio.Web` — the two run in different
processes, so the value is promoted to Core rather than referenced across the boundary.

HUD broadcasts are coalesced to ≥ 50 ms (20 Hz), trailing-edge, always emitting the final value. **The audio action
is not throttled** — volume applies per event at full rate. The ear leads; the screen catches up.

### When something is wrong — what the owner sees (ENC-12)

The console pushes the encoder configuration to the device at every connection and verifies it by
read-back. When that does not fully succeed the response is **silent by design** in the audio path but
**visible** in the UI, and this is what to look for.

**A badge on the Settings pill, from any route.** Bottom-right of the topbar **Settings** pill, so it is
findable without already being on the page that explains it. Three states, three distinct glyphs — the
shape carries the state, not just the colour, so it survives for someone who cannot distinguish amber
from red:

| Glyph | Colour | Means | Accessible name |
|---|---|---|---|
| `warning` | amber | **Degraded** — read-back arrived and its safety fields were right; a non-safety field did not apply | *"Settings — knob settings not applied"* |
| `error` | red | **Hard fault** — a safety field did not read back, **or the device never answered at all** | *"Settings — knob safety settings not applied, volume limited"* |
| `link_off` | amber | the knobs are **not connected** | *"Settings — knobs not connected"* |

Nothing at all is shown when the configuration verified, when the device is merely retrying
(`Transient` — attempts 1-3 are silent on purpose), or when `RotaryEncoder:Enabled` is false. A healthy
boot is completely silent: no toast, no banner, no badge.

**⚠ The volume clamp tracks the *safety* fields, not "did everything apply" (`ENC-16`).**
`RotaryEncoderConfigVerifier.VolumeClampFor` returns the full **4 units** per event for `Configured`
**and for `Degraded`**, and the tight **2** for `Unknown`, `Transient` and `HardFault` — exactly the
tiers where `wrap` on VOLUME or `reverse` on any knob is unverified or disagreeing. A `Degraded`
console's volume knob behaves normally: read-back confirmed the safety fields and only a feel field
(an acceleration tier, `step_size`) disagreed, so the knob may accelerate differently from the design
but it is not limited.

**The clamp is counted in device units, and since `ENC-20` a unit is a point.** `VolumeStepPercent = 1`,
so 4 units per event is **4 volume points** and the tight 2 is 2 points — the same number read either
way. That is not how it used to be: at `VolumeStepPercent = 2` this paragraph's *"6 units"* meant
**12 points**, and reading the clamp as points understated what a single event could actually do by
half. `ENC-20` pinned `step_size` and `VolumeStepPercent` both to `1` precisely so the two quantities
coincide on VOLUME; the tightened value itself did not change, but it now bounds 2 points instead of 4
and so is strictly tighter than before. The derivation lives in encoder handoff §5.4 (Rev 8).

`Transient` keeps the tight clamp because it means *"not confirmed yet"*, not *"confirmed fine"*, and
the boot window is exactly when a fresh or factory-reset device is running acceleration at ×50. An
unplugged console gets it too, because a disconnect resets the tier to `Unknown`.

**A hard fault is the one tier that limits the volume knob, and it is also the one the owner is told
about** — its toast says so, *"Volume is limited until this is fixed."* It covers both ways the safety
fields end up unconfirmed: a read-back that disagreed on one, and a device that never answered within
the three attempts. In either case the knob feels sluggish until the configuration is re-applied from
**System Config → Integrations → Rotary Encoders**. Touch volume is unaffected.

*Until 2026-09-03 the tight clamp applied to every tier except `Configured`, so a `Degraded` console
was limited exactly as hard as a hard-faulted one while being told only that its knobs "may feel
wrong". That is the defect `ENC-16` closed; this paragraph used to describe it as shipped behaviour.*

**Two independent latches, and neither repeats or resets.** A fault also raises a Radzen toast that
navigates to `/system` when clicked. Presence and configuration are latched **separately**, because a
knob that is missing and a knob that is misconfigured are not the same news:

- **Configuration** — at most **one notification per severity, and only on escalation**. A tier that
  flaps between Degraded and Configured fifty times produces exactly one toast; a Degraded that later
  becomes a hard fault produces one more, because the situation got strictly worse. Every
  de-escalation and every repeat is silent.
- **Presence** — at most **one *"Knobs disconnected"*** and at most **one *"Knobs connected"*** per
  session. A lead that bounces inside the furniture ten times still says each of those exactly once.

The two never consult each other, so the worst a single session can produce is a small, bounded
handful — one disconnect, one reconnect, one Degraded, one hard fault — in any order, and never more.
Nothing is ever reset, so a fault that clears and returns an hour later is silent the second time. If
you dismissed a toast and want it back, **reload the page**: a reload is a new browser session, and a
fault that is still live is announced once to it. The badge needs no reload — it is stateless and
tracks the live state for as long as the fault exists, which is the half of the pair meant to still be
there when you come back.

Booting with the knobs **already** unplugged raises no toast at all — just the badge — on the
assumption that whoever unplugged them knows. Booting *into* a configuration fault does raise its
toast, once, as soon as a browser is there to receive it.

### Setup Steps

**Step 1: Connect the Pico**

Plug the Pico into a USB port on the Radio Console host. Verify it appears as a HID device:

```bash
# Linux — check dmesg for the HID device
dmesg | tail -20

# List HID devices (look for VID:PID cafe:4005)
ls -la /dev/hidraw*
```

**Step 2: Set up HID permissions (Linux only)**

By default, `/dev/hidraw*` devices require root access. Create a udev rule so the `mmack` user (or whichever user runs `radio-api`) can access the device:

```bash
sudo tee /etc/udev/rules.d/99-radio-encoders.rules << 'EOF'
# Radio Console — Pico rotary encoder HID access
SUBSYSTEM=="hidraw", ATTRS{idVendor}=="cafe", ATTRS{idProduct}=="4005", MODE="0666"
EOF

sudo udevadm control --reload-rules
sudo udevadm trigger
```

Unplug and re-plug the Pico for the rule to take effect.

**Step 3: Enable in configuration**

Edit `appsettings.json` (or `appsettings.Production.json` for per-machine overrides):

```json
{
  "RotaryEncoder": {
    "Enabled": true,
    "VendorId": 51966,
    "ProductId": 16389,
    "DevicePath": "",
    "PollIntervalMs": 10,
    "VolumeStepPercent": 1,
    "ReconnectDelayMs": 2000
  }
}
```

Configuration fields:
| Field | Description | Default |
|-------|-------------|---------|
| `Enabled` | Escape hatch to force the encoder service off. Presence decides normally (`ENC-0`) | `true` |
| `VendorId` | USB HID Vendor ID (decimal) | `51966` (0xCAFE) |
| `ProductId` | USB HID Product ID (decimal) | `16389` (0x4005) |
| `DevicePath` | Explicit `/dev/hidrawN` path (empty = auto-detect by VID/PID) | `""` |
| `PollIntervalMs` | Delay between HID report reads in ms | `10` |
| `VolumeStepPercent` | Volume points applied per **device unit** of movement (0-100) — not per detent, which is `step_size × tier multiplier` units. Shown **read-only** on the Settings page as VOLUME's step size; the editable duplicate was removed by `ENC-8`. `ENC-20` set it to `1` alongside `step_size = 1`, so one unit is one point and one slow detent is one point | `1` |
| `ReconnectDelayMs` | Delay before retrying after device disconnect | `2000` |

You can also configure these from the Web UI: **System Config → Integrations → Rotary Encoders**.

**Step 4: Restart the API service**

```bash
sudo systemctl restart radio-api
```

**Step 5: Verify**

Check the service logs for connection status:

```bash
journalctl -u radio-api --no-pager --since "1 min ago" | grep -i encoder
```

You should see:
```
Rotary encoder service started
Encoder device connected: <device name>
```

Or open **System Config → Integrations → Rotary Encoders** in the Web UI and check the status chip shows "Connected".

### Troubleshooting

- **"Disconnected" in UI but Pico is plugged in:** Check udev permissions, verify VID/PID match with `lsusb | grep -i cafe`
- **Encoder turns in wrong direction:** open **System Config → Integrations → Rotary Encoders** and toggle **Direction** for that knob. It is sent to the device immediately and verified. ⛔ **Do not swap the A/B pins on the Pico or negate the delta in firmware** — that was the pre-`ENC-2` remedy, and doing it as well as setting `reverse` reverses the knob twice, leaving it exactly as wrong as before
- **No response on button press:** check the button bitmask at **payload offset 16** of report `0x01` (buffer index 17) in the Pico firmware. The service uses bit N for encoder N (bit 0 = encoder 0)
- **Service starts but can't find device:** Try setting `DevicePath` explicitly to `/dev/hidraw0` (or whichever device the Pico is)

### Sleep, wake, and the three states (`ENC-6`)

The console has **three** states, derived from two facts rather than stored as a state machine.
`ISleepService.WakeState` is the derivation and `Radio.Core.Interfaces.ConsoleWakeState` is the enum.

| State | `IsSleeping` | `/sleep` on screen | Audio | Reached by |
|---|---|---|---|---|
| **Awake** | false | no | playing | any wake |
| **Ambient** | false | yes | **playing** | 30 min idle (`idle-dimmer.js`), or navigating to `/sleep` |
| **Standby** | **true** | yes | **paused + muted** | the topbar Sleep pill · a VOLUME long-press · `POST /api/system/sleep` |

**Designer Rev 5 §8 describes five states. The two dark ones are withdrawn** with the blanking half —
see the recovery section below for why the panel must never blank.

**What each input does** (handoff §8.3, minus the two dark columns):

| Input | Awake | Ambient | Standby |
|---|---|---|---|
| VOLUME turn / press / hold | acts | **acts in place** — the readout renders on the sleep screen's own HUD host | consumed; the press **resumes** |
| SOURCE / PRESETS / TUNING turn | acts | consumed, and **wakes** to the full UI | consumed; **does not resume audio** (D22) |
| SOURCE / PRESETS / TUNING press | acts | consumed, and **wakes** | **resumes** |
| Screen tap | acts | wakes | **resumes** |

**Two things that are easy to get wrong here, both of which shipped as bugs:**

- **The idle timer must NOT call `SetSleepAsync(true)`.** Ambient is defined by playback continuing.
  The server learns about Ambient from **`Sleep.razor` reporting itself** via
  `POST /api/system/sleep-screen`, on first render and on dispose — which is also what makes all
  three ways of reaching `/sleep` one state rather than two. `MainLayout` reports the opposite on its
  own first render, which is what corrects the flag after a hard browser navigation kills the page's
  circuit before its dispose can report. ⚠ **That correction happens on the next `MainLayout`
  render, not immediately, and it does not cover an API restart** — the flag lives in memory on
  `radio-api`, so a restart while the kiosk sits on `/sleep` leaves the server reading `Awake` until
  something re-reports. See `design/FUTURE-WORK.md` §7 for the open follow-up.
- **A wake spends exactly one input.** `ISleepService.TryClaimWake()` is a synchronous latch, and
  `WakeState` reads `Awake` from the instant a claim is taken — earlier than either `IsSleeping`
  flipping or the browser leaving the route. Without it a fast spin would lose every detent for the
  length of a page navigation instead of one. A consumed input still publishes that knob's current
  value, so the first detent answers *where am I* and the second one moves it.

### ⚠ The screen is dark and will not come back — recovery

Read this first if you are at the cabinet and the panel is black. **You need SSH; the panel itself cannot
help you.** `ssh mmack@radio` (never the bare IP — the SSH identity is hostname-bound).

**Primary — set the display power mode directly.** This is the one that works in both dark states and
holds:

```bash
gdbus call --session --dest org.gnome.Mutter.DisplayConfig \
  --object-path /org/gnome/Mutter/DisplayConfig \
  --method org.freedesktop.DBus.Properties.Set \
  org.gnome.Mutter.DisplayConfig PowerSaveMode "<0>"
```

**Secondary — deactivate the screensaver.** Only use this if the primary is unavailable:

```bash
gdbus call --session --dest org.gnome.ScreenSaver --object-path /org/gnome/ScreenSaver \
  --method org.gnome.ScreenSaver.SetActive false
```

**The order matters, and it was measured.** The screensaver route does **not** reach DPMS-off. On
2026-09-02 the panel was found dark with `dpms=Off` while the screensaver reported `GetActive=(false,)` —
`SetActive false` is a no-op there, because the screensaver is already inactive. `PowerSaveMode` read `3`
(DRM DPMS Off) at the same moment, and setting it to `0` is what actually restored the panel. Where the
screensaver cycle did work it bought about 13 seconds before the panel went down again.

Both commands need the graphical session environment. From a plain SSH shell, import it first or every
call fails in a way that looks like the interface is missing — the incantation is in `CLAUDE.md` under
"Remote UI driving".

Check state with `cat /sys/class/drm/card1-DP-1/dpms` and the `PowerSaveMode` property — `status`, `dpms`
and `enabled` are reported independently and do not always agree.

> **Why screen blanking is switched off, and must stay off.** `ENC-15` established on the box that **the
> touchscreen is powered by the panel and leaves the USB bus when the panel blanks** — so touch cannot wake
> it, because no input device exists to generate the event. The rotary encoder cannot wake it either: it
> exposes `/dev/hidraw3` and **no evdev node at all**, so it is invisible to the compositor and cannot even
> reset the idle timer. A knob wake would work only through `radio-api` reading hidraw and calling the
> unblank itself, which makes that service a single point of failure in the only remaining wake path.
> Full write-up: [`docs/uat/2026-09-02-enc15-touch-wake-gate/REPORT.md`](../docs/uat/2026-09-02-enc15-touch-wake-gate/REPORT.md).

---

### Driving the knobs from software — the ENC-17 harness

**`tools/encoder-harness/virtual_encoder.py` turns "a human must turn each knob" into "a script
can."** It creates a real USB HID device carrying the RotaryUsb identity and report descriptor and
injects genuine Input Report `0x01` frames, so the shipped `HidRotaryEncoderService` opens it,
decodes it and acts on it exactly as it does the physical device — same udev rule, same HidSharp
enumeration, same decoder, same `ENC-1` accumulator semantics. It is not a mock, and nothing in the
console is modified, reconfigured or restarted to accommodate it.

```bash
scp tools/encoder-harness/virtual_encoder.py mmack@radio:/tmp/
ssh mmack@radio "sudo python3 /tmp/virtual_encoder.py -c 'turn 0 3' -c 'hold 0 900'"
```

`turn` / `offline-turn` / `press` / `release` / `tap` / `hold` / `idle` / `detach` / `attach`.
Encoders are `0 = VOLUME, 1 = SOURCE, 2 = PRESETS, 3 = TUNING`. Full command table, design
rationale and the recovery procedure: **`tools/encoder-harness/README.md`**.

**It takes the real device's identity (`cafe:4005`) and unbinds the real encoder for the duration**,
rebinding it on exit. A distinct VID/PID via `RotaryEncoder:VendorId`/`:ProductId` was rejected
because it moves UAT off the shipped configuration at exactly the layer under test, and because an
override left behind points the console at a device that does not exist — the real knobs then stay
dead across reboots with nothing on screen to say why.

**It cannot be left running by accident.** stdin EOF exits, `--max-seconds` (default 300) is a hard
watchdog, `SIGINT`/`SIGTERM`/`SIGHUP` exit cleanly, and teardown runs in a `finally:` on every exit
the process can observe. `usbipd` is a child with `PR_SET_PDEATHSIG=SIGKILL` so the kernel reaps it
even when the harness cannot clean up. There is no unit file and no autostart, deliberately.

⚠ **`SIGKILL` is the exception and leaks.** Measured 2026-09-03: usbipd died as designed, but the
configfs gadget and the vhci attachment both survived, leaving a virtual `cafe:4005` enumerated and
the real encoder unbound. What survives is **inert** — every report originates from a typed command,
so an undriven harness sends nothing and the `ENC-3` synthetic-volume hazard is not reachable from a
leak; the real cost is that the physical knobs stay dead. **Recover with
`sudo python3 /tmp/virtual_encoder.py --cleanup`** — or just start the harness again, which tears
down leftovers on the way in and rebinds the real encoder on the way out. A reboot also clears it.

⚠ **`/dev/uhid` does not work for this, despite being present on the appliance.** A uhid device is
parented to `/sys/devices/virtual/...` and so has no USB ancestor. The shipped udev rule matches
`ATTRS{idVendor}`, which resolves by walking up to a USB parent, so it never fires — workaroundable.
**HidSharp is not**: measured 2026-09-03, with a uhid `cafe:4005` device present and readable,
`DeviceList.Local.GetHidDevices()` did not list it at all, so `GetHidDevices(0xCAFE, 0x4005)`
returned 0 and even the `DevicePath` override could not select it. HidSharp's Linux backend resolves
vendor and product from the USB parent's sysfs attributes. Hence the usbip loopback gadget, which
needs `usbip-vudc`, `vhci-hcd`, `libcomposite`, `usb_f_hid` and the `usbip`/`usbipd` binaries —
**all already installed on this box**. `dummy_hcd`, the more usual virtual UDC, is *not* available
for this kernel.

**The harness answers the configuration handshake**, so the console reaches `Configured` rather than
sitting on the tightened clamp — otherwise a clamp measurement would silently measure the wrong
thing.

**What it proved on the appliance, 2026-09-03** (against `5e571b8`): the service reports
`Encoder report length 107 bytes (movement accumulators: true)` and verifies its configuration on
attempt 1; a turn moves volume at 2% per unit; the `ENC-3` per-event clamp holds at ±6 units
(±12 points) against single events of 20 and 50 detents — ⚠ **those two figures are pre-`ENC-20`
and are kept as dated history, not as current behaviour; the live values are 1% per unit and ±4
units, which is also ±4 points** — the `ENC-4` HUD renders left-anchored at
its band; a 900 ms hold on encoder 0 synthesises a long press with the progress ring while a 200 ms
hold does not; and **`ENC-1`'s re-baseline rule holds across a real USB disconnect — 50 detents
accrued while unplugged produced a 0-point jump on replug.**

⚠ **It does not replace the owner's hand on the panel.** Feel, acceleration and whether a spin
*sounds* right are still his. It also does not exercise the firmware: acceleration tiers,
`step_size` and detent density are the device's, and the harness asserts a movement value where the
real device would compute one.

## 2. Phone Call Integration (RotaryPhone)

Connects to an external **RotaryPhone** server via SignalR to receive incoming call notifications. When a call comes in, the Radio Console:
1. Ducks the current audio
2. Plays a ring sound (if configured)
3. Announces the caller name/number via TTS

### Prerequisites

- A running **RotaryPhone** server instance with:
  - A SignalR hub at `/hubs/phone`
  - (Optional) A contacts REST API for caller name lookup

### Setup Steps

**Step 1: Note the RotaryPhone server address**

The RotaryPhone server exposes two endpoints:
- **SignalR Hub:** `http://<phone-server>:5555/hubs/phone`
- **Contacts API:** `http://<phone-server>:5555`

Replace `<phone-server>` with the actual hostname or IP.

**Step 2: (Optional) Place a ring sound file**

If you want a ring sound before the TTS announcement, place a WAV or MP3 file on the Radio Console host:

```bash
# Example: copy a ring sound to the media directory
cp phone-ring.wav /opt/radio-console/media/sounds/phone-ring.wav
```

**Step 3: Enable in configuration**

Edit `appsettings.json` (or `appsettings.Production.json`):

```json
{
  "PhoneIntegration": {
    "Enabled": true,
    "HubUrl": "http://<phone-server>:5555/hubs/phone",
    "ContactsApiBaseUrl": "http://<phone-server>:5555",
    "RingSoundPath": "media/sounds/phone-ring.wav",
    "RingPriority": 9,
    "AnnouncementPriority": 8,
    "ReconnectBaseDelayMs": 2000,
    "ReconnectMaxDelayMs": 30000
  }
}
```

Configuration fields:
| Field | Description | Default |
|-------|-------------|---------|
| `Enabled` | Master switch for phone integration | `false` |
| `HubUrl` | RotaryPhone SignalR hub URL | `http://radio:5004/hub` |
| `ContactsApiBaseUrl` | RotaryPhone contacts REST API base URL | `http://radio:5004` |
| `PlayRingSound` | Play ring sound through radio speakers (disable when physical phone rings) | `false` |
| `RingSoundPath` | Path to ring sound file (relative to app root, or absolute) | `media/sounds/phone-ring.wav` |
| `RingPriority` | Audio ducking priority for the ring sound (1-10) | `9` |
| `AnnouncementPriority` | Audio ducking priority for TTS caller announcement (1-10) | `8` |
| `ReconnectBaseDelayMs` | Initial reconnection delay in ms (doubles on each retry) | `2000` |
| `ReconnectMaxDelayMs` | Maximum reconnection delay cap in ms | `30000` |

You can also configure these from the Web UI: **System Config → Integrations → Phone Integration**.

**Step 4: Restart the API service**

```bash
sudo systemctl restart radio-api
```

**Step 5: Verify connection**

```bash
journalctl -u radio-api --no-pager --since "1 min ago" | grep -i phone
```

Success:
```
Connecting to RotaryPhone hub at http://<phone-server>:5555/hubs/phone
Connected to RotaryPhone hub
```

If the RotaryPhone server isn't running yet:
```
Could not connect to RotaryPhone hub at http://... Will retry.
```

This is safe — the client retries with exponential backoff and will connect automatically when the server becomes available.

### SignalR Hub Protocol

The Radio Console expects the RotaryPhone hub to invoke the following client method:

```
CallStateChanged(string state, string phoneNumber)
CallStateChanged(string state, string phoneNumber, string callerName)
```

Both overloads are registered. The `state` parameter is parsed leniently:

| State Values | Maps To |
|-------------|---------|
| `"ringing"`, `"ring"`, `"incoming"` | Ringing |
| `"incall"`, `"in_call"`, `"active"`, `"answered"` | InCall |
| `"ended"`, `"hangup"`, `"idle"` | Ended |
| Anything else | Idle |

### Contacts REST API Protocol

The optional contacts API is called when a caller name isn't provided in the SignalR event:

```
GET {ContactsApiBaseUrl}/api/contacts/lookup?phone={phoneNumber}
```

Expected response (200 OK):
```json
{
  "Name": "John Smith",
  "PhoneNumber": "+15551234567"
}
```

If the API is unavailable or returns no match, the raw phone number is used in the announcement.

### PBAP Contact Sync

Radio.API can download contacts from the connected phone's phonebook via Bluetooth PBAP (Phone Book Access Profile). These contacts are used for caller ID resolution during incoming calls.

**How it works:**
1. A Python helper script (`pbap_download.py`) manages the D-Bus session bus connection to BlueZ's obexd
2. It creates an OBEX PBAP session, selects the internal phonebook, and downloads all contacts as a VCF file
3. The VCF is parsed (vCard 2.1/3.0 with quoted-printable decoding) and stored in SQLite
4. Phone number lookup uses exact match first, then last-7-digit suffix matching for fuzzy resolution
5. After sync, the BT connection is automatically restored (OBEX teardown causes a temporary disconnect)

**Prerequisites:**
- `bluez-obexd` installed and enabled as a user service: `systemctl --user enable --now obex`
- `python3` with `dbus-python` and `PyGObject` packages (standard on Ubuntu)
- `DBUS_SESSION_BUS_ADDRESS` environment variable set in the radio-api systemd service
- Phone must be paired and have granted PBAP access (Android prompts on first access)

**Configuration** (`appsettings.json` → `Bluetooth:Pbap`):

| Field | Description | Default |
|-------|-------------|---------|
| `AutoSyncOnConnect` | Automatically sync contacts when a phone connects | `true` |
| `SyncStaleThresholdHours` | Hours before contacts are considered stale and re-synced | `24` |
| `TransferTimeoutSeconds` | Max seconds to wait for PBAP transfer to complete | `30` |

**REST API** (`/api/bluetooth/pbap`):

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/sync?deviceAddress=XX:XX:XX:XX:XX:XX` | Trigger manual sync (uses connected device if omitted) |
| `GET` | `/contacts?deviceAddress=XX:XX:XX:XX:XX:XX` | List synced contacts for a device |
| `GET` | `/lookup?phoneNumber=5551234567` | Look up a contact by phone number |
| `GET` | `/status` | Sync status for all devices |

**Operational notes:**
- PBAP sync causes a brief BT audio interruption (~5 seconds) as OBEX uses a separate RFCOMM channel
- The service uses `PrivateTmp=true` in systemd, so temp files are written to `/opt/radio-console/data/` (not `/tmp`) to avoid mount namespace mismatch with obexd
- A `SemaphoreSlim` prevents concurrent sync attempts from auto-sync and manual API calls
- On first connect from a new phone, Android will prompt to allow phonebook access — the user must approve on the phone

### How It Works

When an incoming call is detected (`Ringing` state):

1. The `PhoneCallIntegrationService` looks up the caller name:
   - First checks PBAP contacts (local SQLite, synced from phone's phonebook)
   - Falls back to the RotaryPhone contacts REST API
2. If a ring sound file exists at `RingSoundPath`, it plays the sound followed by a TTS announcement (e.g., "Incoming call from John Smith")
3. If no ring sound, just the TTS announcement plays
4. Audio ducking lowers the main audio during the announcement
5. The resolved caller name is reported back to RotaryPhone via SignalR (`ReportCallerResolved`)
6. When the call ends (`Ended` or `Idle`), the announcement stops and audio returns to normal
7. Call state changes are broadcast to the Web UI in real-time via SignalR

### Troubleshooting

- **"Disconnected" but server is running:** Check firewall rules, verify the hub URL is reachable from the Radio Console host with `curl http://<phone-server>:5555/hubs/phone/negotiate`
- **No announcement on ring:** Check TTS engine availability in **System Config → Event Sources**. Ensure a TTS engine (Google or Azure) is configured, with its API key present. Both engines are cloud services and there is no offline fallback, so announcements also go silent whenever the network is down - see the `TTS-9` note in [SYSTEMCONFIGURATION.md](SYSTEMCONFIGURATION.md#text-to-speech-tts-setup)
- **Wrong caller name:** Verify the contacts API response shape matches the expected schema above
- **Ring sound doesn't play:** Verify the file exists at the configured path and is a valid WAV or MP3

### Google Voice (gvbridge) Messages

The same RotaryPhone service exposes a Google Voice bridge that the Web UI consumes for the unified **Messages** feed on `/phone` (voicemail + texts). Like the rest of the phone integration, Radio Console consumes it over REST/SignalR **only** — no GV backend runs in-process.

**Base host:** `radio:5004` (same RotaryPhone service as the call integration above).

**REST (read), under `/api/gvbridge/*`:**

| Method | Route | Returns |
|--------|-------|---------|
| GET | `/api/gvbridge/status` | `{ available, activeMode, sipRegistered?, cookiesValid? }` — drives the reconnecting banner + Send gate |
| GET | `/api/gvbridge/voicemail?count=&pageToken=` | `VoicemailListDto` (paged) |
| GET | `/api/gvbridge/voicemail/{id}` | `VoicemailItemDto` |
| GET | `/api/gvbridge/voicemail/{id}/audio` | voicemail recording (bind an **absolute** URL — see gotcha) |
| GET | `/api/gvbridge/sms/threads?count=` | `SmsThreadListDto` |
| GET | `/api/gvbridge/sms/threads/{threadId}?count=` | `SmsThreadMessagesDto` |
| POST | `/api/gvbridge/sms/send` | `SendSmsResponse` — **shipped** on RotaryPhone, dark behind their `GVBridge:EnableSmsSend=false` (returns `409` + `Code:"send_disabled"`). Gated on our side by `RotaryPhone:Gv:SendEnabled` (PR3). **Response shapes have diverged — reconcile before flipping** (see FUTURE-WORK §12 item 2) |

**REST (mark-read / durable read-state, PR4 — ADR-024):** gated by `RotaryPhone:Gv:MarkReadEnabled` (consumer flag); body `{ "isRead": true }`; returns the updated DTO; `200`→DTO, `404`→gone (null), `502`/non-2xx→null but the UI keeps its optimistic flip and reconciles on the next list/poll/push; **no client-side auto-retry**.

| Method | Route | Returns |
|--------|-------|---------|
| POST | `/api/gvbridge/voicemail/{id}/read` | updated `VoicemailItemDto` (`isRead` authoritative) |
| POST | `/api/gvbridge/sms/threads/{threadId}/read` | updated `SmsThreadDto` (`hasUnread` authoritative) |

**SignalR push:** rides the existing `/hub` RotaryHub connection (consumed by `PhoneHubService`):
- `SmsReceived` → `PhoneHubService.GvSmsReceived` (`SmsMessageDto`)
- `VoicemailReceived` → `PhoneHubService.GvVoicemailReceived` (`VoicemailItemDto`)
- `ReadStateChanged` → `PhoneHubService.ReadStateChanged` (`ReadStateChangedDto { Kind, Id, ThreadId, IsRead, ChangedAtUtc }`, PR4) — unified read-state change. RotaryPhone broadcasts **unconditionally, including back to the originator**, so consumers MUST de-dupe by `(id-or-threadId + isRead)` (see the `ReadStateReconciler` gotcha below). Unknown `Kind` is ignored defensively.

> **GV SMS is NOT the same product as VoIP.ms trunk SMS.** Trunk SMS rides `GvTrunkHubService.SmsReceived` on `/hubs/gvtrunk` with a different payload. The C# event for GV SMS is deliberately named `GvSmsReceived` so the two can't be confused. Do not merge or rename them. (ADR-022 §4.3.)

**Config keys** (`appsettings.json`; per-machine overrides in `appsettings.Production.json`):

```json
"RotaryPhone": {
  "ApiBaseUrl": "http://radio:5004",
  "HubUrl": "http://radio:5004/hub",
  "Gv": {
    "SendEnabled": false,        // flip true when POST /sms/send ships (PR3)
    "MarkReadEnabled": false,    // flip true when the two POST .../read routes ship (PR4)
    "StatusPollSeconds": 10,     // GvBridgeStatusService poll interval
    "AuthKey": ""                // set to enable X-RotaryPhone-Auth header (OFF today)
  }
}
```

**Gotchas:**
- **Voicemail audio URL must be absolute.** The DTO's relative `AudioUrl` resolves against the Web origin (`:5002`) and 404s — always rebuild it against the API base via `GvBridgeApiService.GetVoicemailAudioUrl(id)` (→ `http://radio:5004/...`). Never bind the relative `AudioUrl` (ADR-022 D4).
- **A failed conversation load is NOT an empty conversation (GV-8 / UAT F-1).** `GetSmsThreadMessagesAsync` returns `GvResult<SmsThreadMessagesDto>` — `Success` / `HttpError` (with `StatusCode` + RotaryPhone's `error`/`code` discriminator) / `Timeout` / `Transport` / `Malformed` — precisely because it used to map all of them to `null`, which `PhonePage` then coalesced with `?? new()` into an empty list. A 502 and a genuinely empty thread became byte-identical, and the pane rendered **"Start the conversation below."** with no error, no spinner and no retry. The conversation pane now branches **skeleton → error → empty → list** in that order, so the empty copy is reachable only for a real zero-message result. Two consequences worth holding: **(a)** a group thread (id containing `/`) returns a genuine **HTTP 200 with `messages: []`** because RotaryPhone never decodes the `%2F` — from our side that IS an empty, and rendering it as one is correct, not a bug (their fix is tracked as a cross-repo item); **(b)** the failure is **invisible to browser instrumentation** — Blazor Server fetches server-side over SignalR, so the UAT saw 0 console errors and 0 failed requests. The only probe is `journalctl -u radio-web --since '-30min' | grep 'Failed to get GV SMS thread'` — keep it bounded with `--since`, do not tail; the box is an Intel N100 and heavy journald reads compete with the audio pipeline. **`GvResult<T>` is the reusable shape** — GV-6 adopts it for the mark-read methods rather than inventing a second mechanism.
- **`psidtsAgeSeconds` is the ONLY trustworthy field on `GET :5004/api/gvbridge/status` — and it is a live blackout clock.** Twice-confirmed (2026-07-31 root-cause pass and the GV-8 UAT). Google's PSIDTS cookie is good for ~11 minutes; RotaryPhone's CDP refresh only fires every ~20, with no reactive refresh on 401 — so GV auth is dead for roughly **9 minutes out of every 20**, on a wall clock that is independent of anything we do. Read it as: **`< 660` healthy · `660–1200` blackout (expect HTTP 502 on every GV read) · resets at ~1200.** The sibling fields **lie** — `{"available": true, "degraded": false, "cookiesValid": true, "psidtsAgeSeconds": 707}` was captured while both SMS endpoints were returning hard 502s, which is exactly why our "Google Voice is reconnecting" banner (`PhoneMessagesPanel.razor:14-20`) never fires in the window it exists for. **Practical consequence: any test of this surface that does not record `psidtsAgeSeconds` (or wall-clock time) produces results that look random.** That is how a previous pass came to hypothesise throttling — a hypothesis the logs then falsified three ways (401 never 429; failure tracks wall-clock not request volume; recovery lands on fixed boundaries). Prefer this one-shot probe over reading journals; the box is an Intel N100 and heavy journald reads compete with the audio pipeline. _Both the refresh interval and the dishonest health fields are RotaryPhone-side items, tracked in `docs/BUILDER_QUEUE.md` § Cross-repo handoffs #6._

  ```bash
  curl -s http://192.168.86.50:5004/api/gvbridge/status
  ```

- **Read-state is durable via GV write-through (PR4 / ADR-024), behind `RotaryPhone:Gv:MarkReadEnabled`.** When the flag is ON, heard/read persists to Google Voice (the single source of truth); RadioConsole keeps **no local read-state store** — list endpoints' `isRead`/`hasUnread` are authoritative on every (re)load. When the flag is OFF (today), read-state is per-circuit optimistic only and any list reload — hard reload, the Refresh button, or error-retry — re-derives from `isRead`/`hasUnread`. Missed calls count toward the topbar `/phone` badge (owner decision 2).
- **The de-dupe invariant (ADR-024 §9) — the one correctness rule.** Every mark yields ≥2 signals: the mark route's returned DTO **and** the echoed `ReadStateChanged` broadcast (RotaryPhone re-broadcasts unconditionally, including back to the originator); a 502 adds a third "keep optimistic, reconcile later" path. All collapse to one badge state via `ReadStateReconciler`, keyed by `(id-or-threadId + isRead)` — applying an already-applied mark is a no-op (no re-render, no flicker).
- **Two-flag distinction.** `RotaryPhone:Gv:MarkReadEnabled` is **our consumer flag** (gates whether our seam calls the route). RotaryPhone's server-side `GVBridge:EnableMarkRead` is **their config flag** (gates whether their already-shipped route acts or returns the dark rejection). Both default `false` and are independent — **flip theirs first**, confirm the route no longer rejects, then flip ours. Their `GVBridge:AllowMarkUnread` (also `false`) separately gates `isRead:false`, which we never send.
- **Mark-read auth is auto-covered.** The two `POST .../read` routes ride the `/api/gvbridge/*` prefix gate and reuse `RotaryPhone:Gv:AuthKey` via the existing `RotaryPhoneAuthHandler` — no new auth key (ADR-024 §7).
- **`RotaryPhoneAuthHandler` is OFF** until `Gv:AuthKey` is set. ~~A native `<audio>` element can't send that header, so an auth-gated audio endpoint would break the direct-`<audio>` approach (ADR-022 §8.1).~~ **Superseded by [ADR-029](decisions/2026-08-03-gv-audio-through-engine.md) (2026-08-03):** voicemail audio no longer goes to the browser at all — Radio.API fetches it server-side through `GvMediaClient`, which *can* attach the header. **The "keep the audio endpoint unauthenticated" constraint is dissolved and the standing cross-repo ask should be withdrawn** (`docs/BUILDER_QUEUE.md` § Carried risks #3). Note the closure depends on Radio.API holding its own `GvMedia:AuthKey` — it would *not* hold under an opaque-URL design (ADR-029 §10.1).

#### Second consumer: the kiosk desktop launcher (`radio-console-open`)

Since 2026-08-18 the Web UI is **not** the only thing in this repo that leans on the GV bridge.
`deploy/debian-x64/kiosk/bin/radio-console-open` — the "Radio Console" desktop icon, installed to
`/usr/local/bin` by `setup-kiosk.sh` — probes the bridge and, when it is down, **invokes**
`gv-bridge-ensure.sh` to bring it back.

- **`gv-bridge-ensure.sh` is RotaryPhone-owned, and this is invoke-and-probe only.** The launcher
  calls it and reads its exit code, and does nothing else with it: it never writes, installs,
  edits or owns bridge startup, never touches the watchdog or nightly timers, and never `pkill`s
  the bridge. **The entire contract is two things — a path and an exit code.** The path is
  resolved from a candidate list at runtime (`~/bin/` → `/usr/local/bin/` →
  `/opt/rotary-phone/bin/`) because RotaryPhone is bringing the script under version control and
  may relocate it; a relocation then degrades to a *reported* failure rather than a wrong one.
- **The launcher checks liveness, never auth**, and it reads `psidtsAgeSeconds` by exactly the
  rule above plus one thing this doc had not needed to spell out before: **`> 1200`, or the field
  absent or null, is the launcher's dead-session test.** The counter resets at ~1200 in every
  healthy cycle, so a value beyond it means the refresh cycle itself never fired — which is what
  makes a single stateless probe sufficient, with no timestamp file and no history.
- **The `660–1200` trough is deliberately never reported.** It is the appliance working as
  designed, it clears itself inside ten minutes, and the launcher can do nothing about it.
  Surfacing it would fire a dialog on roughly 45% of taps, and a status surface that cries wolf
  is worse than no status surface: by the time something is genuinely broken it is already being
  dismissed unread. The in-app `/phone` banner owns transient auth decay; the launcher does not.
- The bridge process is identified by its profile directory
  (`--user-data-dir=~/.config/gv-bridge-chrome`), never by matching `chrome`. **Never widen any
  kill or match to `chrome`** — that is what used to take the bridge down.

---

### Server-side GV media fetch and cache (`GvMedia`) — ADR-029 D3/D8

Since `PHN-1b` the API can fetch a voicemail recording **itself**, into a bounded on-disk cache,
rather than handing the browser an absolute URL to fetch. Since `PHN-1c` there is a route family that
uses it — `/api/audio/events`, below — so the fetch, the cache and the playback seam are all in place.
⚠ **`GvMedia:Enabled` still ships `false`**, so no fetch happens on a stock box until it is turned on;
`Radio.Web` also still plays voicemail through its own `<audio>` element until `PHN-2` retires it.

**Why it exists, and why the cache is not an optimisation.** GV auth on the bridge is dead roughly
**9 minutes in every 20** (punch list `XR-3`), and `/api/gvbridge/status` reports
`available:true, degraded:false` *during* the blackout. A user who replays a voicemail thirty
seconds later therefore has roughly a **45% chance** the second fetch would 502 if it went back to
the network. A cache hit never touches the network, which is what makes replay reliable on a wall
clock nobody can see. The accepted cost: private voicemail audio now sits **at rest on disk**.

**Why the API fetches rather than the browser.** `GvBridgeApiService.GetVoicemailAudioUrl` only ever
*builds* a string that the `<audio>` element then fetches, so no `DelegatingHandler` can touch it —
which is why browser-side playback would break the moment RotaryPhone's `/api/gvbridge/*` auth gate
flips on. `GvMediaAuthHandler` attaches `X-RotaryPhone-Auth` to the server-side fetch, which is the
thing the browser could never do.

**Configuration — `GvMedia`, in `src/Radio.API/appsettings.json`:**

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Master gate. `GvMediaClient` refuses to fetch when false. |
| `BaseUrl` | `http://radio:5004` | The gvbridge host. Deliberately **not** `PhoneIntegration:ContactsApiBaseUrl` — same host today, different feature, independently disableable. |
| `AuthKey` | `""` | Value for `X-RotaryPhone-Auth`. Empty means **no header is sent**, which matches the current LAN-only posture. |
| `CacheDirectory` | `./data/gvmedia` | Resolves to `/opt/radio-console/data/gvmedia` on the appliance — `radio-api.service` sets `WorkingDirectory=/opt/radio-console` and runs as `mmack`, who owns `data/` (its sibling `albumart/` is already there). |
| `CacheMaxMegabytes` | `50` | Cap in MB. **`0` is a supported escape hatch and means NO CACHE** — see below. |
| `MaxPlaybackSeconds` | `300` | Bounds the download (≈9.6 MB at an assumed 32 000 B/s) and the no-cache sweep window. Also the hard cap on one attended playback (ADR-029 §7.1): `EventPlaybackService` arms a one-shot timer when audio starts and stops the playback when it fires, with no client cooperation and no poll. **There is no "off"** — a value below 1 clamps to 1. |
| `MaxSpeechChars` | `1000` | Cap on event-playback speech text. ⚠ The behaviour is **rejection, not truncation** — `Radio.Web` truncates visibly before posting. |
| `PreemptAtPriority` | `8` | Priority at or above which a starting event source stops attended playback (ADR-029 D5). Read by `EventPlaybackService.OnDuckingStateChanged`. ⚠ **Safe to lower, a trap to raise — and the reason is not the one it looks like.** Every source that reaches this rule had its priority set explicitly: all four `StartDuckingAsync` call sites in the tree call `SetPriority` first, so `GetPriority`'s category default never answers a start raise. What makes `9` a trap is `NotificationsController.Announce`'s `request.Priority ?? 8` — **every external notification that does not name a priority arrives at exactly 8**, so raising this to `9` silently stops the doorbell preempting while the dormant `PhoneIntegration:RingPriority` (9) still would, leaving the feature looking intact after it has stopped happening for everything that can make a sound on this box. `7` widens it to the documented "high importance" band. Keep it at or below `DuckingService.DefaultEventPriority` — the number ADR-029 §6.1 anchored on. A test pins those two compile-time defaults; it can see neither a per-machine override nor the controller's `?? 8`, which is what this line is for. |
| `FetchTimeoutSeconds` | `15` | HTTP timeout for one media fetch. |

**Cache behaviour.** Recency is `LastWriteTimeUtc`, touched on every hit — **not** access time,
because Linux mounts default to `relatime` and would silently degrade the LRU to something between
LRU and FIFO. Eviction really deletes, oldest first, until the directory fits the cap; the entry the
caller is about to play is exempt, so a cap smaller than one recording is left violated by exactly
that one file and corrected on the next write. With `CacheMaxMegabytes = 0` recordings are still
written — playback needs a local path — but **never served back**, and a short-TTL sweep
(`max(60s, MaxPlaybackSeconds × 2)`) reclaims them; at most one recording lingers until the next
fetch. Choosing `0` re-exposes replay to the blackout above.

**Failure taxonomy.** `GvMediaUnavailableException.Reason` distinguishes `Disabled`, `NotFound`
(404 — ⚠ **retryable, not permanent**; see the diagnosis table below), `Unauthorized` (401/403 —
most likely an `AuthKey` divergence), `Upstream` (5xx, usually the blackout, retryable), `Timeout`,
`Transport` and `TooLarge`. Collapsing these is the shared root of the open `GV-6` and `GV-8` rows;
do not. `IsPermanent` is true for **`Disabled` only** — it is the one reason that is permanent by
construction on this side, and no clock changes it.

**Logging.** A raw voicemail id reaches **no** log message, log argument or exception message. Ids
are masked as a hash prefix (`gvm:1a2b3c4d`) — the first 8 hex of the same SHA-256 that names the
cache file, so a log line and a file on disk correlate while leaking nothing. A `***1234` suffix
mask is right for a phone number and wrong here: nobody recognises a voicemail id by its last four
characters, so a suffix would leak for zero operator benefit.

#### The attended-playback route family (`PHN-1c`, ADR-029 §3.3)

```
POST   /api/audio/events             → 202 EventPlaybackSnapshot | 400 | 409
GET    /api/audio/events/current     → 200 EventPlaybackSnapshot | 204
DELETE /api/audio/events/{id}        → 204 | 404 | 409
POST   /api/audio/events/{id}/seek   → 200 | 400 | 404 | 409   { positionSeconds }
POST   /api/audio/events/{id}/pause  → 200 | 404 | 409
POST   /api/audio/events/{id}/resume → 200 | 404 | 409
```

⚠ **`404` and `409` mean different things and every route uses the same rule.** `404` is reserved for an
id `GET /current` has **never** described. An id that has just completed or failed is still an id
`/current` describes — it simply cannot pause, scrub or stop any more — so it gets `409` with a named
reason (`NotPlaying`, `NotPaused`, `NotSeekable`, `NotStoppable`). `seek` additionally answers `400`
(`BadPosition`) for a negative, NaN or infinite `positionSeconds`; a *finite* position past the end of
the content is a `409 NotSeekable`, not a `400` and not a `500`.

Two arms, one mechanism: `kind: "Speech"` carries the literal utterance, `kind: "RemoteMedia"` carries a
`(mediaKind, mediaId, durationSeconds)` **reference**. ⚠ **There is no URL field and there must never be
one** — the server builds the fetch URL from its own configuration, which is what keeps this from being an
SSRF primitive.

⚠ **`POST` answers `202`, not `200`, and that changes where failures appear.** Both arms have an
acquisition phase — an HTTP fetch, or a TTS synthesis — before any audio exists, so the response describes
an *accepted* playback in `Preparing`. **An acquisition failure is therefore not a status code**: it arrives
later as `state: "Failed"` with a named `failureReason` on the snapshot, and `GET /current` is where a
caller reads it. The one exception is `GvMedia:Enabled` being false, which is knowable without touching the
network and is answered synchronously as `409`.

⚠ **`GET /current` retains the last snapshot after a playback ends**, until a new one replaces it. `204`
means *nothing has been started since boot*, not *nothing is playing* — read `state` to tell those apart.
That is required by the `202` shape: an acquisition failure has no response left to carry it, so this is
the surface that carries it instead.

**State reaches the UI by push, and by one pull.** Every transition is broadcast on `/hubs/audio` as
**`EventPlaybackChanged`**, carrying the same snapshot `GET /api/audio/events/current` returns —
with `state` and `kind` as **strings** on both, so the same client field can be filled from either.
There is deliberately **no position tick**: the snapshot is an anchor
(`positionAtBroadcast` + `broadcastAtUtc` + `state`) and clients interpolate locally (ADR-029 §8.2).
`Radio.Web` seeds its cache from the REST call **once per process**, because broadcasts fire on
transitions and a client connecting between two of them would otherwise render silence over a
talking room.

**Three things stop an attended playback without anyone pressing Stop.** ⚠ They used to be listed
here "in descending order of trustworthiness", with the circuit rule last. **ADR-029 Amendment 2
reordered that**, and the ordering is now by *how much of the room's behaviour each one explains*
rather than by how much each is trusted:

1. **The last Blazor circuit closing** — on this appliance, the one that actually fires. Owner
   decision **`D30`** (2026-09-04): *"If the page reloads mid-voicemail, the audio should fail. If the
   user wants to hear it they can replay."* A graceful close (reload, navigate away, tab close)
   disposes the circuit at once — the 10-minute `DisconnectedCircuitRetentionPeriod` covers only
   *unexpected* disconnects. Measured on the box: reloading the only browser goes 1 → 0 → 1 and this
   path stops the playback at the zero, ~0.4 s before the replacement circuit arrives.
   ⚠ **That is intended behaviour, not a defect.** An earlier revision of this list marked it *"Known
   broken — do not rely on this"*; `D30` makes that wrong in the other direction, and there is to be
   no grace period, no circuit-identity matching and no survival mechanism. ⚠ **One acknowledged
   limit:** with a **second browser** open a reload is 2 → 1 → 2 and nothing stops, so the rule
   delivers `D30` only in the single-browser case — which is this box's resting state. Closing that
   gap needs to know which circuit started the playback, i.e. the ownership model ADR-029 ⟨A1·4⟩
   deleted. The divergence is benign: a second client still holds the transport.
2. **Entering `/sleep`**, because that route runs under `EmptyLayout` and offers no transport
   (ADR-029 §7.5 on §16.5's corrected trigger). ⚠ **TWO server-side edges, and the reason there are
   two is a real defect that shipped in the design and was caught on the box:**
   - `SleepService.SetSleepScreenVisibleAsync(true)` — the `/sleep` page reporting itself. Covers the
     **30-minute idle timer**, the Sleep pill, the server push and a direct navigation.
   - `SleepService.EnterSleepAsync` — the room being parked. Covers the entries with **no browser at
     all**: `POST /api/system/sleep` and the encoder long-press.

   ⚠ **This list used to say "Enforced server-side in `SleepService.EnterSleepAsync`, so all three
   client entry points … are covered by one rule."** That was false, and it was false for the exact
   case §7.5 was written about: `idle-dimmer.js`'s `navigateToSleep('idle')` reaches `/sleep` by
   `window.location.href` and deliberately calls nothing server-side, so `IsSleeping` stays **false**
   on the idle path and the rule never ran there. The rule is *"stop when `ConsoleWakeState` leaves
   `Awake`"*, applied as an edge at both write sites — never polled, because `WakeState` reads `Awake`
   while a wake claim is outstanding (`ENC-6`'s fast spin).

   It is a **stop**, not a mute: `WakeAsync` restores the pre-sleep mute state, so a merely-muted
   voicemail would become audible again mid-word on the next touch.
3. **The `GvMedia:MaxPlaybackSeconds` cap** — the only one with no client in the loop at all, and now
   the backstop behind the other two rather than the thing they back up. Ships at 300 s. Built as a
   one-shot `TimeProvider` timer whose callback *dispatches* a stop — ⚠ **not** `CancelAfter` on the
   playback's token, which would leave the audio sounding while suppressing the completion that would
   otherwise have ended it.
   ⚠ **The 300 is coupled to the 600 in one direction, and nothing enforces it.** A network drop that
   never reconnects evicts its circuit ~10 minutes in; that firing is harmless only *because* the cap
   has already stopped the audio five minutes earlier. Raise `MaxPlaybackSeconds` past ten minutes —
   plausible, for a long voicemail — and a behaviour nobody has ever seen appears silently. The cap
   has no maximum, and the retention period lives in `Radio.Web` where nothing in `Radio.API` can see
   it.

Navigating between routes does **not** stop playback (`NavigationManager.NavigateTo` without
`forceLoad` keeps the circuit), and closing one of two open browsers does not either.

**`failureReason` — the operator diagnosis table.** Nine reasons reach a snapshot, deliberately not
collapsed. ⚠ **Not all nine are acquisition failures**, and the last two are the ones that send an
operator to a different place entirely — `MediaAcquisitionFailed` is the generic arm on the media path,
and `PlaybackError` means the audio *was* acquired and the player failed:

| `failureReason` | Means | Retry? | What to do |
|---|---|---|---|
| `MediaNotFound` | gvbridge answered 404 | **Yes** | ⚠ **Does NOT mean the recording is gone.** RotaryPhone's `GetAudio` resolves through `FindNodeAsync`, which does not check its voicemail list's `Succeeded` flag — so a failed authenticated list returns an *empty* set and the route answers 404. That is what a **Google Voice auth blackout** looks like from here (~9 min in every 20). Wait a few minutes and retry before concluding anything. |
| `MediaUnauthorized` | gvbridge answered 401/403 | No | **`GvMedia:AuthKey` and RotaryPhone's `InterServiceAuthKey` differ.** There are **two** `appsettings.Production.json` files to edit — one under `api/`, one under `web/` — and **neither is re-seeded by the deploy**. See the runbook below. This is the only signal for that mismatch; the boot check cannot detect it. |
| `MediaUpstream` | any other non-2xx, 5xx included | Yes | Usually the GV auth blackout. Retry. |
| `MediaTimeout` | exceeded `GvMedia:FetchTimeoutSeconds` | Yes | Check the bridge is up and the LAN is healthy. |
| `MediaTransport` | DNS/connection/TLS failure below HTTP | Yes | `GvMedia:BaseUrl` wrong, or gvbridge down. |
| `MediaTooLarge` | body exceeded the size bound | No | Bound is `MaxPlaybackSeconds` × 32 000 B/s. A real voicemail should never hit it. |
| `SpeechSynthesisFailed` | TTS did not produce audio | Depends | No engine/voice configured (`TTS:DefaultEngine` and `DefaultVoice` ship **empty** since `TTS-9` and throw naming the valid set), a missing API key, or synthesis exceeded `TTS:GenerationTimeoutSeconds`. |
| `MediaAcquisitionFailed` | the RemoteMedia arm threw something that was **not** a `GvMediaUnavailableException` | Maybe | The generic arm, so the log line is the diagnosis: `journalctl -u radio-api` carries a Warning naming the `evp-` id. Most likely a local problem rather than a bridge one — `GvMedia:CacheDirectory` unwritable or not owned by `mmack`, a full disk, or the cached file failing to open. |
| `PlaybackError` | the source reported `PlaybackCompletionReason.Error` | Maybe | ⚠ **The audio was acquired — `GvMedia` is not where to look.** This is SoundFlow failing to start or continue playback, and it is what an operator sees when the output device is gone or `radio-api` has lost PipeWire (the service must run as `mmack` with `XDG_RUNTIME_DIR=/run/user/1000`). ⚠ It can also arrive **after minutes of audio**, so `state: "Failed"` does not imply nothing was heard. |

⚠ **A tenth string, `MediaDisabled`, exists and is normally unreachable.** `GvMedia:Enabled` being false
is knowable without the network and is answered synchronously as a `409` on `POST`; the only way it
reaches a snapshot is if the flag is turned off in the window between accepting a playback and fetching
its media.

**Before turning `GvMedia:Enabled` on**, check whether RotaryPhone's `InterServiceAuthKey` is set. Its gate
ships **default-off**, and while it is off `GvMedia:AuthKey` may stay empty — but if it *has* been set, every
fetch returns 401 and surfaces as `MediaUnauthorized` until the two match, across the two files below.

#### ⚠ Runbook: setting `AuthKey` on a live box is a hand edit, twice

Nothing in the deploy will do this for you, and the two services do **not** share one file.

```bash
# Radio.API — the GvMedia key
ssh mmack@radio 'sudo nano /opt/radio-console/api/appsettings.Production.json'   # add GvMedia:AuthKey
ssh mmack@radio 'sudo systemctl restart radio-api'

# Radio.Web — its own RotaryPhone:Gv:AuthKey, the same secret under a different key
ssh mmack@radio 'sudo nano /opt/radio-console/web/appsettings.Production.json'
ssh mmack@radio 'sudo systemctl restart radio-web'
```

*Why the deploy cannot do it:* `Deploy-ToLinux.ps1` rsyncs with
`--exclude='appsettings.Production.json'` and seeds that file only when it is **absent**
(`test -f` guard), deliberately, so per-machine settings survive a deploy. Editing the repo's
`deploy/*/appsettings.Production.json` seed therefore never reaches a box that already has one.
And the two overlays are genuinely separate files that diverged months ago — measured on `radio`
on 2026-09-02, the API's is **1057 bytes, mtime 2026-03-05** (AudioOutput / Devices / FilePlayer /
Diagnostics / Fingerprinting) while the Web's is **75 bytes, mtime 2026-07-31** (only
`RotaryPhone:Gv:MarkReadEnabled`). Neither carries an auth key today.

`src/Radio.API/appsettings.json` **is** overwritten on every deploy, which is why the non-secret
`GvMedia` defaults live there and the secret does not.

`GvMediaStartupCheck` logs one Warning at boot when `Enabled` is true and `AuthKey` is empty. It
also has a narrower branch for when `RotaryPhone:Gv:AuthKey` is visible to Radio.API and `GvMedia`'s
is not — but note that Radio.API cannot normally see Radio.Web's overlay, for exactly the reason
above, so that branch fires only when the key has also been placed in Radio.API's own configuration
or environment (`RotaryPhone__Gv__AuthKey`).

---

## 3. Notification / Announcement API

A REST endpoint that any external service can call to trigger a TTS announcement with audio ducking. No setup required beyond having the Radio Console API running.

### Endpoint

```
POST http://<radio-console>:5000/api/notifications/announce
Content-Type: application/json

{
  "Message": "Dinner is ready!",
  "Priority": 8
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Message` | string | Yes | Text to announce via TTS |
| `Priority` | int | No | Audio ducking priority 1-10 (default: 8). Higher = more important |

### Response

**Success (200):**
```json
{ "message": "Announcement played" }
```

**Validation error (400):**
```json
{ "error": "Message is required" }
```

### Usage Examples

**curl:**
```bash
curl -X POST http://radio:5000/api/notifications/announce \
  -H "Content-Type: application/json" \
  -d '{"Message": "The washing machine is done", "Priority": 7}'
```

**Home Assistant automation:**
```yaml
action:
  - service: rest_command.radio_announce
    data:
      message: "Motion detected at the front door"
      priority: 9
```

With a REST command configured as:
```yaml
rest_command:
  radio_announce:
    url: "http://radio:5000/api/notifications/announce"
    method: POST
    content_type: "application/json"
    payload: '{"Message": "{{ message }}", "Priority": {{ priority | default(8) }}}'
```

**Python:**
```python
import requests

requests.post("http://radio:5000/api/notifications/announce", json={
    "Message": "Package delivered",
    "Priority": 6
})
```

### Testing from the Web UI

Open **System Config → Integrations → Notifications**, type a message, set the priority, and click **Send Test**. You'll hear the TTS announcement through the Radio Console speakers.

### How Audio Ducking Works

When an announcement is triggered:
1. The current audio (music, radio, etc.) is smoothly lowered in volume
2. The TTS announcement plays at the specified priority level
3. Once the announcement finishes, the original audio volume is restored
4. **Higher-priority events interrupt attended GV playback — and nothing else.** ⚠ Be precise about the scope, because the claim here was wrong in both directions before. Since `PHN-1d` (ADR-029 D5), an event source that starts at or above `GvMedia:PreemptAtPriority` (**8**) **stops** an in-flight voicemail or spoken message outright — it does not pause it, and the recording is replayable at zero cost. That is `EventPlaybackService.OnDuckingStateChanged`, and it is the first load-bearing read of `DuckingService.GetPriority` in this system's life. ⚠ **Only that direction.** Pressing play while such a source is *already* sounding still **mixes** today; the owner's decision of 2026-09-04 (punch list `D28`) is that it should **wait and then play**, and that queue ships with the console-playback chip that can show it is waiting. ⚠ **Also still NOT true is announcement-versus-announcement.** Ducking itself remains **binary and reference-counted**, not priority-weighted: the first event fades the primary source to the fixed global `Audio:DuckingPercentage` (20), every subsequent concurrent event leaves the *level* alone, and full volume returns only when the last event leaves. An announcement at 9 does not interrupt one at 3 — they **mix**. ADR-029 §6.2 rule 3 declines to fix that on purpose: the fix is a queue across every caller of `IAnnouncementService`, which is separate work. `StopAllDuckingAsync` also still has **zero non-test callers**. ⚠ **The live consequence, which is intended (ADR-029 §6.1):** with `PhoneIntegration:Enabled` false, the only thing on this box that can preempt attended playback is a notification posted to `/api/notifications/announce` at its default priority 8 — **a doorbell will stop a voicemail mid-play.** Outside that one rule the `Priority` field below is still accepted, validated, stored and used for nothing else, so the guidance table remains intent rather than behavior.

Priority guidelines:
| Priority | Use Case |
|----------|----------|
| 1-3 | Low importance (informational, timers) |
| 4-6 | Medium importance (smart home events, reminders) |
| 7-8 | High importance (doorbell, notifications) |
| 9-10 | Critical (phone calls, alarms, security alerts) |

---

## Web UI Management

All three integrations can be monitored and configured from the Web UI at:

**System Config → Integrations** (8th tab)

The Integrations tab has three sub-tabs:

1. **Rotary Encoders** — Connection status, VID/PID, encoder mapping reference, full configuration editor
2. **Phone Integration** — Hub connection status, current call state, caller info, full configuration editor
3. **Notifications** — Test announcement form for sending TTS announcements

Status indicators update in real-time via SignalR — no page refresh needed.

> **Note:** Configuration changes made in the Web UI are saved to the configuration store but require a service restart to take effect (the `Enabled` flag and connection parameters are read at startup).

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        Radio.API                                │
│                                                                 │
│  RotaryEncoderHostedService ← IRotaryEncoderService (HID)       │
│    └→ RotaryEncoderActionRouter → IAudioManager                 │
│                                                                 │
│  PhoneCallIntegrationService ← IPhoneIntegrationService (SignalR)│
│    └→ PhoneContactLookupService (PBAP SQLite + REST fallback)   │
│                                                                 │
│  PbapSyncService ← IPbapSyncService (D-Bus OBEX via Python)     │
│    └→ PbapContactRepository (SQLite)                            │
│    └→ VCardParser                                               │
│    └→ IAnnouncementService → ITTSFactory + IDuckingService      │
│                                                                 │
│  NotificationsController                                        │
│    └→ IAnnouncementService → ITTSFactory + IDuckingService      │
│                                                                 │
│  IntegrationsController (status endpoints for Web UI)           │
│    └→ IRotaryEncoderService.IsConnected                         │
│    └→ IPhoneIntegrationService.IsConnected/CurrentState         │
│                                                                 │
│  AudioStateUpdateService                                        │
│    └→ Broadcasts EncoderConnectionChanged via SignalR            │
│    └→ Broadcasts PhoneCallStateChanged via SignalR               │
└─────────────────────────────────────────────────────────────────┘
         ↕ SignalR                    ↕ HTTP
┌─────────────────────────────────────────────────────────────────┐
│                        Radio.Web                                │
│                                                                 │
│  SystemConfigPage → Integrations tab (3 sub-tabs)               │
│    └→ IntegrationsApiService (status polling)                   │
│    └→ ConfigurationApiService (config load/save)                │
│    └→ AudioStateHubService (real-time events)                   │
└─────────────────────────────────────────────────────────────────┘
```

### Key Files

| Layer | File | Purpose |
|-------|------|---------|
| Core | `Interfaces/Input/IRotaryEncoderService.cs` | Encoder service contract |
| Core | `Interfaces/External/IPhoneIntegrationService.cs` | Phone service contract |
| Core | `Interfaces/Audio/IAnnouncementService.cs` | TTS announcement contract |
| Core | `Configuration/RotaryEncoderOptions.cs` | Encoder config options |
| Core | `Configuration/PhoneIntegrationOptions.cs` | Phone config options |
| Infrastructure | `Platform/Input/HidRotaryEncoderService.cs` | USB HID reader + event firing |
| Infrastructure | `Platform/Input/RotaryEncoderActionRouter.cs` | Maps encoder events → audio actions |
| Infrastructure | `External/PhoneCallClient.cs` | SignalR client for RotaryPhone hub |
| Infrastructure | `External/PhoneContactLookupService.cs` | PBAP + REST contact lookup |
| Infrastructure | `Bluetooth/PbapSyncService.cs` | PBAP sync service (D-Bus OBEX) |
| Infrastructure | `Bluetooth/PbapContactRepository.cs` | SQLite contact storage |
| Infrastructure | `Bluetooth/VCardParser.cs` | vCard 2.1/3.0 parser |
| Infrastructure | `Bluetooth/pbap_download.py` | Python D-Bus OBEX helper script |
| Infrastructure | `Audio/Services/AnnouncementService.cs` | TTS + ducking orchestration |
| API | `Controllers/NotificationsController.cs` | `POST /api/notifications/announce` |
| API | `Controllers/IntegrationsController.cs` | `GET /api/integrations/{encoder,phone}/status` |
| API | `Services/RotaryEncoderHostedService.cs` | Encoder lifecycle management |
| API | `Services/PhoneCallIntegrationService.cs` | Phone call event handling + caller ID |
| API | `Controllers/PbapController.cs` | PBAP sync/contacts/lookup REST endpoints |
| Web | `Services/ApiClients/IntegrationsApiService.cs` | HTTP client for status endpoints |
| Web | `Components/Pages/SystemConfigPage.razor` | Integrations tab UI |
