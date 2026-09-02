# ENC-15 — the touch-wake gate: investigation report

**Date:** 2026-09-02
**Box:** `radio` (`radio.lan` -> `192.168.86.50`), Intel N100, x86_64, Ubuntu + GNOME 46, GDM3 auto-login
**Session:** Wayland (`loginctl` session 1, seat0)
**Row:** `ENC-15` (P0) in [`docs/HANDOFF-GA-PUNCH-LIST.md`](../../HANDOFF-GA-PUNCH-LIST.md) §3.5;
Designer [Rev 3 §8.5](../../design-handoffs/HANDOFF-rotary-encoder-mapping.md)
**Question:** can touch independently wake a blanked panel? If not, blanking (`ENC-6`'s second half) must
not ship, because losing the encoder USB while dark would leave a screen that cannot be turned on from
inside sealed furniture.
**Authorization:** owner-authorized autonomous execution against the GA punch list.

---

## Verdict: **GATE FAILED**

Not *"needs a human."* The mechanism is settled without anybody touching the glass:

> **The touchscreen is powered by the panel. When the panel powers down, the touch controller leaves the
> USB bus.** There is no device left to ignore a touch — the event cannot be generated in the first place.

This is a stronger and more disappointing result than the row anticipated. The row asked whether the
compositor *ignores* touch while blanked. The answer is that there is nothing to ignore.

| Question | Answer | Basis |
|---|---|---|
| Is the touchscreen a properly seated input device when lit? | Yes | Observed — ILITEK `222a:0335`, `hid-multitouch`, `event13`/`event14`, `ID_INPUT_TOUCHSCREEN=1` |
| Does it remain on the USB bus while the panel is blanked? | **No** | Observed — kernel `USB disconnect` on blank, re-enumeration on unblank |
| Can touch therefore wake a blanked panel? | **No, by construction** | Inferred from the above — see *the one real limitation* |
| Is the encoder a compositor input device? | **No** | Observed — zero evdev nodes, `hidraw` only |
| Does the punch list documented recovery line work? | **Only in one of the two dark states** | Observed — see §3 |
| Was a real state reached that the documented line could not recover? | **Yes** | Observed — see §4 |

**Recommendation: `ENC-6` blanking half does not ship.** The punch list already pre-committed to this
outcome — *"If touch cannot wake it, blanking does not ship until it has two wake paths"* — so this is the
recording of an agreed consequence, not a new decision to make. The **non-blanking half of `ENC-6` is
unaffected** and remains P0.

---

## 1. The touch controller leaves the bus with the panel

Lit, the touchscreen is exactly what you would want: a properly seated, seat-bound input device — ILITEK
`222a:0335`, driver `hid-multitouch`, nodes `event13` and `event14`, `ID_INPUT=1` and
`ID_INPUT_TOUCHSCREEN=1`.

Blanked, it is gone. Across one blank/unblank cycle:

```
Sep 02 11:50:16 kernel: usb 3-1: USB disconnect, device number 12    <- SetActive true issued at 11:50:15
Sep 02 11:50:22 kernel: usb 3-1: new full-speed USB device number 14 <- ~1s after SetActive false
Sep 02 11:50:35 kernel: usb 3-1: USB disconnect, device number 14
```

The disconnect follows the blank by about a second, and the re-enumeration follows the unblank by about a
second. With no USB device there is no evdev node, no libinput device, and no HID report — so no touch
event reaches any layer of the stack, compositor included.

**This is a property of this panel power design, not of GNOME input handling.** No amount of compositor
configuration recovers a device that is not on the bus.

## 2. The encoder is not a compositor input device either — and this invalidates a premise

This was not what `ENC-15` set out to test, and it matters more than the thing that was.

`cafe:4005 RotaryUsb` is present on USB and has **zero evdev nodes**. It exposes `/dev/hidraw3` only
(`root:plugdev`, mode `0660`):

```
$ ls /sys/bus/hid/devices/0003:CAFE:4005.000B/input/
ls: cannot access ...: No such file or directory

$ grep -ci rotary /proc/bus/input/devices
0
```

Consequences, in order of how much they hurt:

1. **A knob generates no compositor input at all.** It cannot reset the GNOME idle timer and it cannot wake
   a blanked panel by itself.
2. **A knob wake can only ever work if `radio-api` reads `hidraw` and itself calls the D-Bus unblank.**
3. **That makes `radio-api` a single point of failure in the only remaining wake path.** If the service is
   wedged — which is the exact condition under which somebody most wants the screen — the knobs cannot
   bring the panel back either.

Designer Rev 3 §8.5 treats the encoders as *the* hardware wake source that makes blanking survivable. On
this hardware they are not a hardware wake source at all; they are an application-mediated one.

## 3. The documented recovery line is insufficient as written

The punch list asks that this line be recorded in `INTEGRATIONS.md` so it is findable at 2 a.m.:

```
gdbus call --session --dest org.gnome.ScreenSaver --object-path /org/gnome/ScreenSaver \
  --method org.gnome.ScreenSaver.SetActive false
```

It works — but only when the **screensaver** is what is holding the panel down. Both directions were
confirmed, and the blank is a real panel power-down rather than a black shield:

```
$ gdbus call ... SetActive true
()
T1 11:50:17 GetActive=(true,)  ActiveTime=(uint32 2,)
card1-DP-1: status=connected dpms=Off enabled=disabled

$ gdbus call ... SetActive false
()  exit=0
T2 11:50:21 GetActive=(false,)
card1-DP-1: status=connected dpms=On  enabled=enabled
```

**Then the other dark state appeared: panel down while the screensaver reports inactive.**

```
11:52:48  GetActive=(false,)   dpms=Off  enabled=disabled   touch=ABSENT
```

Here `SetActive false` is a **no-op** — the screensaver is already inactive, so there is nothing for it to
deactivate. DPMS-off is a separate control, and it disagreed with the screensaver:

```
$ gdbus call --session --dest org.gnome.Mutter.DisplayConfig ... Get PowerSaveMode
(<3>,)                      # 3 = DRM DPMS Off, while the screensaver reported false
      readwrite i PowerSaveMode = 3;
```

Setting it directly is what recovered the panel, and unlike the screensaver cycle it **held**:

```
$ gdbus call --session --dest org.gnome.Mutter.DisplayConfig --object-path \
    /org/gnome/Mutter/DisplayConfig --method org.freedesktop.DBus.Properties.Set \
    org.gnome.Mutter.DisplayConfig PowerSaveMode "<0>"
()  set exit=0
11:57:38 psm=0 dpms=On en=enabled touch=P
...
12:02:17 dpms=On touch=P        # stable 4.5 min
```

**`INTEGRATIONS.md` therefore carries the Mutter `PowerSaveMode` line as primary and the ScreenSaver line
as secondary**, because the screensaver route does not reach DPMS-off.

## 4. The failure this row exists to prevent was reproduced on real hardware

After the first blank the panel entered a roughly 13-second on/off oscillation that the screensaver route
could not break — each `SetActive false` bought about 13 seconds before the panel went down again.
`PowerSaveMode=0` broke it.

That is the failure mode `ENC-15` was written to catch, reproduced on the actual appliance, with the
documented recovery line proving insufficient against it. It is the strongest available argument for the
gate having been worth an hour.

**The box was left verified stable** — `dpms=On`, `PowerSaveMode=0`, touch `PRESENT`, kiosk connected
(2 established connections to `:5002`), `radio-api` and `radio-web` both active, zero leftover transient
units, and **no `gsettings` value changed**. Three USB disconnects appear in the kernel log for the day;
all three are from this investigation.

## 5. As-found blanking configuration — off at three layers, and unchanged

| Setting | As-found |
|---|---|
| `org.gnome.desktop.session idle-delay` | `uint32 0` (never) |
| `org.gnome.desktop.screensaver idle-activation-enabled` | `false` |
| `org.gnome.desktop.screensaver lock-enabled` | `false` — so there is no password-brick risk on top of the blanking one |
| `org.gnome.settings-daemon.plugins.power sleep-inactive-ac-timeout` | `0` |
| `org.gnome.settings-daemon.plugins.power sleep-inactive-ac-type` | `nothing` |
| `SleepService.cs:84-87` and `:114-115` | both `SetDisplayPowerAsync` calls commented out |

**No `gsettings set` was run.** All values were re-read at exit and were identical.

---

## The one real limitation, stated plainly

**Nobody touched the glass.** The verdict rests on the *mechanism* — a USB device that is not present
cannot emit an event — rather than on an observed failure to wake.

The single thing that could overturn it is **undocumented panel-firmware wake-on-touch**: a panel that
watches its own digitizer while powered down and re-asserts power on contact, without the host ever seeing
an input event. That behaviour exists on some integrated panels. It was not tested and cannot be tested
remotely.

Observing the mechanism is stronger evidence than observing the symptom would have been, for the same
reason the OSK investigation protocol-channel result was stronger than its visual one. But it is not the
same as a finger on the glass.

A confirmatory procedure is staged on the box at **`/tmp/enc15-touchtest.sh`**. It arms a 45-second rescue
timer *before* blanking, blanks, gives 30 seconds for the owner to touch the glass repeatedly, restores the
panel automatically, and prints `PASS - TOUCH WOKE THE PANEL` or `FAIL - NO TOUCH DEVICE WHILE DARK`. The
rescue timer was verified to actually fire rather than assumed — `Started enc15-rescuetest.service`,
`gdbus[363351]: ()`.

**It is optional.** Given the mechanism, a `PASS` would mean panel-firmware wake-on-touch exists, which
would be a genuine surprise worth knowing about; a `FAIL` confirms what is already established. If the
screen is still dark after the script finishes, the `PowerSaveMode` line in §3 is the recovery, and a
power-cycle at the wall is the backstop.

> Note: `/tmp` does not survive a reboot. If the box has been restarted since 2026-09-02 the script is
> gone and the procedure has to be re-staged.

---

## Method note (worth keeping)

DPMS state was read from `/sys/class/drm/card1-DP-1/`, which reports `status`, `dpms` and `enabled`
independently — the three do not always agree, and it was the disagreement between `dpms=Off` and the
screensaver `GetActive=false` that located the second dark state. Touch presence was tracked by
enumerating the USB device rather than by asking libinput, which is what made the disconnect visible
instead of merely inferring it from silence.

Every D-Bus call in this report was run with the graphical session environment imported, per `CLAUDE.md`.
Without it the session bus is not reachable from an SSH shell and every call fails in a way that looks like
the interface is missing.
