# `ENC-22` — power the panel off after a period in sleep; any knob wakes it

[← Builder Queue index](../../docs/BUILDER_QUEUE.md)

🟠 **P1 — owner request 2026-09-28, GA scope.** *"After some period of time in sleep mode, the LCD should
go into 'low/no' power mode and wake up with any knob touch. We should test whether this is possible
and add a task for it if it is."*

⚠ **This reinstates, on new evidence, the blanking half of `ENC-6` that `ENC-15` withdrew.** ENC-15
(2026-09-02, [report](../uat/2026-09-02-enc15-touch-wake-gate/REPORT.md)) failed its gate because
*touch* cannot wake a dark panel, and it required two wake paths. The owner's request is the
knob-only design ENC-15 identified as the one remaining path (*"a knob wake can only ever work if
`radio-api` reads `hidraw` and itself calls the D-Bus unblank"*) and never measured. It has now been
measured, and it works. **The single-wake-path risk is real and is what the safety rules below are for.**

## ✅🔬 Shipped 2026-09-28 — [#707](https://github.com/mmackelprang/RTest/pull/707), **off by default**

`Radio.Infrastructure.Platform.Display.PanelPowerService` (Mutter `PowerSaveMode` via `gdbus`, keyed on
`IsSleepScreenVisible`), asked first by `RotaryEncoderActionRouter`; `ExecStopPost=` on
`radio-api.service` (canonical unit + `deploy/provision` fallback drop-in, installed on `radio`). The
retained ScreenSaver-route `SleepService.SetDisplayPowerAsync` was removed. Config and rules:
`design/INTEGRATIONS.md` §1, *Panel power-off in sleep*. **Enable only after the owner's physical check —
[`uat/OWNER-REVIEW.md`](../uat/OWNER-REVIEW.md), Phase 2g.**

**Box UAT, 2026-09-28, build `47b55ae`, `Sleep:PanelOffAfterMinutes = 1` set temporarily in
`/opt/radio-console/api/appsettings.Production.json` (restored afterwards), sampled at 1 Hz:**

| Step | Result |
|---|---|
| (a) kiosk to `/sleep` by `window.location.href` (the idle path, `IsSleeping` false) → timer | ✅ `Ambient` 16:10:43 → power-off logged 16:11:43.485 → `dpms=Off`; encoder connected throughout |
| (b) harness `turn 0 1` while dark | ✅ consumed 16:13:25.040 → `dpms=On` next sample; volume 0.21 and mute unchanged |
| (c) harness `tap 1`, and `press 0`/`release 0`, each while dark | ✅ `On` at 16:14:33 and 16:15:39; mute not toggled by the VOLUME press/release |
| (d) harness `detach` while dark | ✅ disconnect seen 16:16:47.6 → `On` 16:16:48. Detached while lit with the timer due: *"not powering off — the encoder is not connected"* 16:18:37, panel stayed on |
| (e) `sudo systemctl restart radio-api` while dark | ✅ `On` 1.3 s after the command. **Plus** `systemctl kill -s SIGKILL` while dark: `On` 0.5 s later while the unit was still `activating` — `ExecStopPost=` alone, since `RestartSec=10` rules out start-up |
| (f) `Deploy-ToLinux.ps1` while dark | ✅ `On` at the service stop (16:23:01); deploy ended `Kiosk is live (14 …)` |
| Disabled again | ✅ 73 s on `/sleep` (`Ambient`) with the setting removed: panel stayed on |

Also observed: starting the harness while dark (it unbinds the real encoder) lit the panel through the
encoder-lost rule, and after every reconnect the 30 s stability period was honoured before a power-off.
**Not measured:** a physical knob by a hand while dark (owner, OWNER-REVIEW), and an overnight cycle.

## Feasibility — measured on `radio` 2026-09-28, 12:41

A 20-second blank driven through `org.gnome.Mutter.DisplayConfig.PowerSaveMode`, set and cleared from a
**service-like environment** (`env -i` with only `DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus`
— what `radio-api` has; it runs as `mmack`, the session user), sampled once a second:

| Question | Result |
|---|---|
| Can a service-context process power the panel off? | ✅ `PowerSaveMode` 0 → 3; `/sys/class/drm/card1-DP-1/dpms` = `Off` |
| Does it stay off? | ✅ **20 of 20 samples Off — no oscillation.** (ENC-15's 13-second on/off cycle came from the ScreenSaver route; GNOME idle blanking is off at three layers, so nothing fights the Mutter setting.) |
| Does the encoder stay on USB while dark? | ✅ **Yes.** `3-2.3` (`cafe:4005`) present and `hidraw4` present in every sample; `/api/integrations/encoder/status` `isConnected: true` throughout. It hangs off the Genesys hub on root port 2, which is **not** panel-powered. |
| Does touch survive? | ⛔ No — `usb 3-1: USB disconnect` at the blank, re-enumerated ~2 s after unblank, exactly as ENC-15 found. **Knobs are the only wake source.** |
| Can a service-context process power it back on? | ✅ `PowerSaveMode` 3 → 0, `dpms=On`, kiosk reconnected (2 established connections to `:5002`). |

**Not yet measured — the first task of this row:** that a *physical* knob turn is delivered to
`radio-api` while the panel is dark. The device stays connected and its hidraw node stays open, so
it is expected to work; confirm it with the owner's hand (and with `tools/encoder-harness` for the
repeatable part) before building on it.

## The shape

1. **When:** after `Sleep:PanelOffAfterMinutes` (default suggestion: 10) of continuous sleep —
   ⚠ on **both** sleep entries: the Sleep pill (`SleepService.EnterSleepAsync`, `IsSleeping = true`)
   **and** the idle path (`idle-dimmer.js` → `SetSleepScreenVisibleAsync(true)`, where `IsSleeping` stays
   false — gotcha #9 in `HANDOFF-NEXT-SESSION.md`; key the timer off `IsSleepScreenVisible`, not
   `IsSleeping`).
2. **How:** `org.gnome.Mutter.DisplayConfig` `PowerSaveMode` via the session bus
   (`/run/user/1000/bus`). **Not** `SleepService.SetDisplayPowerAsync` as it stands — that uses the
   GNOME ScreenSaver `SetActive` route, which ENC-15 showed does not reach DPMS-off and produced the
   oscillation, and it shells out through `sudo -u`, which is unnecessary now that the service runs as
   the session user. Replace it rather than call it.
3. **Wake:** any encoder turn **or** press while dark → `PowerSaveMode 0` **first**, and the event is
   consumed (not acted on) — the router's existing `SleepGateOutcome.ConsumeAndWake` semantics. Whether
   waking the panel also leaves `/sleep` or only lights it is a Designer/owner call; the default should be
   "light the sleep screen", matching today's first-touch behaviour.

## ⛔ Safety rules — not optional (Designer Rev 3 §8.5; punch list §10)

With touch gone, **a dark panel plus a lost encoder is a screen nobody can turn on inside a sealed
cabinet.** The encoder USB *does* drop on this box — the reconnect loop exists because it happens.

- **Never power off when the encoder is not `Connected`.**
- **If the encoder disappears while the panel is off, power it on immediately** and do not power off
  again until the encoder has been back for a debounce period.
- **On `radio-api` start, set `PowerSaveMode 0` unconditionally** — a crash while dark must not leave
  the panel dark across the restart. Add an `ExecStopPost=` unblank to `radio-api.service` for the same
  reason on a clean stop (⛔ remember `OPS-10`'s note that systemd silently drops an `Exec` line it
  cannot parse — verify it on the box).
- **Deploys:** `Deploy-ToLinux.ps1` stops `radio-api`; the `ExecStopPost=` covers it. Verify a deploy
  from a dark panel ends with the panel on and the kiosk live.
- **Recovery line** stays in `design/INTEGRATIONS.md` and on the cabinet card, Mutter route first:
  `gdbus call --session --dest org.gnome.Mutter.DisplayConfig --object-path /org/gnome/Mutter/DisplayConfig --method org.freedesktop.DBus.Properties.Set org.gnome.Mutter.DisplayConfig PowerSaveMode "<0>"`
  (with `DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus` from SSH).

## Verify

- Unit: the timer arms on both sleep entries and disarms on wake; power-off is refused while the
  encoder is absent; an encoder-lost event while dark powers on; startup powers on.
- Box, harness: `virtual_encoder.py` — sleep, wait out the timer, `turn 0 1` → panel on, event consumed;
  `detach` while dark → panel on within one poll.
- Box, owner: a real knob turn and press each wake a dark panel; watch one overnight cycle.

## Relationship to other rows

Supersedes the "withdrawn permanently — do not reinstate" note on `ENC-6`'s blanking half **for the
knob-only design, with the rules above**; the ENC-15 finding about touch stands unchanged.

## Owner observation of the 2026-09-28 test blank

- *"The panel came back in sleep mode after you started it back up."* — the kiosk page survives a
  panel power cycle: the browser is untouched, so whatever screen was showing when the panel went dark
  is what comes back. The wake therefore needs no navigation of its own.
- *"There is a hardware splash dialog that appears for a couple of seconds in the middle of the screen,
  but that's acceptable."* — the panel's own firmware splash on power-up. **Accepted by the owner; not a
  defect, do not try to suppress it.** It does mean the first ~2 s after a knob wake show the splash
  over the sleep screen — factor that into any "wake feels slow" report.
