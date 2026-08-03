# OSK viability on the Wayland kiosk — investigation report

**Date:** 2026-08-03
**Box:** `radio` (`radio.lan` → `192.168.86.50`), Intel N100, x86_64, Ubuntu + GNOME 46, GDM3 auto-login
**Session:** Wayland (`loginctl` session 1, seat0, tty2, `Type=wayland`)
**Question:** can the custom 676-line in-app JS keyboard be replaced by an OS-level on-screen keyboard?
**Authorization:** owner-authorized remote investigation; owner away from hardware for ~1 week.

> **Report provenance.** The investigating Tester was blocked from writing this file directly and returned the
> content in its result; the Coordinator committed it verbatim. Evidence artifacts under `evidence/` were
> written by the Tester.

---

## Verdict: an OS-level OSK is **NOT VIABLE** here

The reason is mechanical and was **observed**, not inferred from behavior:

> **Google Chrome 151 on Wayland never issues `zwp_text_input_v3.enable()` when a web-page input receives
> focus.** Across a full session log covering six input types, the count for `enable` is **zero** — including
> with `--enable-wayland-ime --wayland-text-input-version=3`.

Without `enable()`, the compositor is never told a text field is focused. GNOME's OSK is driven by exactly that
signal. Chrome also never sends `set_content_type`, so even if `enable()` were fixed upstream, the compositor
could not choose numeric vs. qwerty.

This is **not** a box misconfiguration: `screen-keyboard-enabled` is already `true`, the OSK demonstrably works,
and Mutter advertises `zwp_text_input_manager_v3`. The gap is on Chrome's side.

| Question | Answer | Basis |
|---|---|---|
| Compositor offers text-input-v3? | Yes | Observed (registry global) |
| Chrome binds it? | Yes | Observed (`bind` + `get_text_input`) |
| Chrome `enable()`s it for web inputs? | **No — 0 times** | Observed (full protocol log) |
| Does `--enable-wayland-ime` change it? | **No** | Observed (retested) |
| Does GNOME's OSK work on this box? | Yes | Observed (screen capture) |
| Can `onboard` type into Chrome? | **No** | Observed (no protocol path exists) |

**Recommendation:** keep the custom JS keyboard; close `design/plans/IAC-PRISTINE-INSTALL-AUDIT.md:307` as
*"system OSK removed; in-app keyboard is the only workable option"*; **remove** `onboard` from
`deploy/provision/packages.sh:88` and delete `~/.config/autostart/onboard-autostart.desktop`.

### The one real limitation, stated plainly

A GNOME Shell **keyring modal was already on screen** when the investigation started (finding F-1) and was
independently holding the OSK open. So the **visual** channel could not distinguish "OSK appeared for the web
input" from "OSK was already up." It could not be cleared — `Escape` is ignored by that dialog, and clicking
`Cancel` needed synthetic pointer input, which tooling policy blocked mid-session.

**The verdict rests on the protocol channel, not the visual one.** That is stronger evidence anyway (it observes
the mechanism, not the symptom), but what is *not* available is a clean capture of a focused web input with no
OSK beneath it.

### Method note (worth keeping)

Shell's screenshot API **and** `GetWindows` are both `AccessDenied` on GNOME 46; `gnome-screenshot` / `grim` /
`scrot` absent. Working route: `org.gnome.Mutter.ScreenCast` → `RecordMonitor("DP-1")` → PipeWire →
`gst-launch-1.0 pipewiresrc`. Two gotchas: Mutter only emits buffers **on damage** (a static screen starves the
stream — a pulsing element was injected), and the instrument was validated by changing the page and confirming
the capture changed, so "nothing appeared" is trustworthy rather than a dead pipeline.

---

## Findings

### F-1 · HIGH · A keyring modal has blocked the kiosk display for ~33 hours (pre-existing)

> **Status: partially remediated 2026-08-03 14:09 EDT — see [F-1 remediation](#f-1-remediation--2026-08-03) at the
> end of this report.** The Radio Console now renders; the modal survives and is **not** the kiosk's.

The screen is **not showing the Radio Console**. It shows GNOME Shell's *"Authentication required / The login
keyring did not get unlocked when you logged into your computer."*

Chain, each link observed: GDM auto-login never unlocks the keyring → kiosk Chrome carries **no
`--password-store` flag** (`/proc/2808/cmdline`) → it asks gnome-keyring → prompt raised via
`org.gnome.keyring.SystemPrompter`, implemented by gnome-shell itself (hence no `gcr-prompter` process). Boot was
`2026-08-02 03:03:17`, Chrome started `03:03:43` → up ~33h.

Proof of the fix: RotaryPhone's snap-Chromium (pid 2328) **does** pass `--password-store=basic` and prompts
nothing.

**Fix:** add `--password-store=basic` to `deploy/debian-x64/kiosk/radio-kiosk-autostart.desktop:4`. RotaryPhone's
pid 3268 also lacks it — latent second trigger, theirs to fix.

*Not proven:* whether the kiosk window is mapped-but-occluded (`GetWindows` denied). Observed: `radio-web`
active, `:5002` → 200, Radio Console UI not visible anywhere.

### F-2 · GNOME's OSK works — and eats a third of the screen

`screen-keyboard-enabled` was **already `true`**; it was never written by the investigation. The OSK renders for
the modal's password entry (a Shell-internal Clutter widget, not a Wayland client, not a web page — a positive
control only).

Measured: **top edge y=480, height 240px = 33.3% of 720**. Chrome is **not** resized — `window.innerHeight`
asserted in-page as **720** while the OSK occupied y=480–719. Any web input below y=480 would be **flatly
occluded with no compensation**.

### F-3 · Core result — Chrome never activates text-input-v3

```
wl_registry#2.global(24, "zwp_text_input_manager_v3", 1)
-> wl_registry#2.bind(24, "zwp_text_input_manager_v3", 1, new id [unknown]#27)
-> zwp_text_input_manager_v3#27.get_text_input(new id zwp_text_input_v3#36, wl_seat#17)
```

A real click focuses `#i-text`, DOM focus asserted `{"active":"i-text","hasFocus":true}`, and the entire traffic
that follows is:

```
-> zwp_text_input_v3#36.set_surrounding_text("", 0, 0)
-> zwp_text_input_v3#36.commit()
```

Histogram (default flags **and** IME build, after clicking all six input types): `enable` **0**,
`set_content_type` **0**, `set_cursor_rectangle` **0**, `set_surrounding_text` 1, `commit` 1. Per spec,
`commit()` without `enable()` leaves the input **disabled**. **Not one flag away.**

Corollary: the `type` / `inputmode` hygiene question is **moot for an OS OSK** — no content type is forwarded at
all.

*Scope caveat:* measured on a probe Chrome, not kiosk pid 2808 — same binary, version, flags, geometry and
compositor; the only deltas were `--user-data-dir` and `--password-store=basic`, neither of which touches
text-input.

### F-4 · The `onboard` contradiction, settled — repo is right by accident, wrong in its reason

`deploy/debian-x64/kiosk/setup-kiosk.sh:116-117` says *"onboard doesn't work on Wayland."* **False as written**,
right conclusion.

Observed: onboard installed (`1.4.1-5ubuntu6`), **running** (pid 2354, up 1d09h), `WAYLAND_DISPLAY=wayland-0`,
`auto-show enabled=true`, and **visibly rendering** on screen.

But it cannot deliver a keystroke: `/proc/2354/fd` holds a **wayland** socket and **zero** X11 sockets (so no
XTEST), and Mutter advertises **neither** `zwp_virtual_keyboard_manager_v1` **nor** `zwp_input_method_manager_v2`.
No route exists. It is a decorative keyboard eating space on a 720px panel.

### F-5 · HIGH · Kiosk CDP on :9223 is dead — Chrome ≥136 ignores it on the default profile

The flag is on the live process; nothing listens. `ss` shows only 9224 (RotaryPhone's);
`~/.config/google-chrome/DevToolsActivePort` does not exist. Since Chrome 136 the flag is **silently ignored** on
the default user-data-dir; this box runs **151**. Every browser here that *does* expose CDP passes an explicit
`--user-data-dir`. Confirmed positively — an identical probe with `--user-data-dir` brought CDP straight up.

Consequence: `/usr/local/bin/radio-refresh-browser` → *"Could not refresh browser…"*, rc=1.

**Fix:** add a **non-default** `--user-data-dir` to the kiosk launch line (creates a fresh profile on first run).

### F-6 · MEDIUM · `--window-position` is a no-op under Wayland

Wayland clients cannot position their own toplevels. Both RotaryPhone browsers are ignored — 2328
(`100,60`) and 3268 (`10000,10000`) are **both on screen**, overlapping the kiosk. Prior sessions described 3268
as "the second, off-screen Chrome"; on this session it is not off-screen. Untouched; route via the boundary doc.

### F-7 · MEDIUM · X11 display helpers confirmed no-ops; DPMS not actually disabled

Queried with correct auth (`XAUTHORITY=/run/user/1000/.mutter-Xwaylandauth.YM3AT3`):

```
DPMS (Display Power Management Signaling):
  Server does not have the DPMS Extension
```

So `xset -dpms` / `s off` / `s noblank` (`setup-kiosk.sh:106-108`) are **hard no-ops**. (A naive query without
`XAUTHORITY` fails with "Authorization required" — a different, misleading failure.) The `disable-dpms.desktop`
autostart entry that `setup-kiosk.sh:139-152` creates **does not exist** on the box.

What actually holds the screen on is the gsettings, which are correct: `idle-delay=0`, `lock-enabled=false`,
`idle-activation-enabled=false`, `sleep-inactive-ac-type='nothing'`. **But `power idle-dim` is `true`** — the
screen still dims on idle. Should be `false` for a wall-mounted kiosk.

The `xdotool` path is equally dead: with correct auth, `search --class chrome` and `--name "Radio Console"` both
return nothing; `xlsclients` shows only `gsd-xsettings`, `ibus-x11`, `mutter-x11-frames`. Also `~/.Xauthority`
does not exist.

### F-8 · LOW · Repo/box drift on `radio-refresh-browser`

`setup-kiosk.sh:170-176` still writes the **xdotool** version; the deployed script is a newer **CDP-based** one.
Both paths are broken (F-5, F-7). Sync the repo to the deployed version.

### F-9 · LOW · PhoneDevTray dialer gets a QWERTY keyboard for digits

The input has no `type`, no `inputmode`, no `data-keyboard`. Traced through `virtual-keyboard.js:326-347`:
`detectInputMode` matches nothing and returns `'qwerty'`; meanwhile `virtual-keyboard.js:37` reads
`input.type || 'text'` so the keyboard *does* open — just in the wrong mode. One-line fix:
`inputmode="numeric"`. Worth sweeping other digit fields.

---

## Explicitly NOT proven

1. No clean visual capture of "web input focused, no OSK" — modal confound (above).
2. Kiosk pid 2808 itself was never driven (CDP dead; restarting it was declined).
3. Whether the kiosk window is mapped behind the modal — `GetWindows` denied.
4. Whether `onboard` would type into an X11 client (academic — it has no X11 connection).
5. No real touch-hardware testing; all input synthetic. (Moot — Chrome never sends `enable()`.)
6. Only Chrome 151 tested; no older Chrome, no Firefox.

---

## Every change reverted

`gsettings` was **never written** — `screen-keyboard-enabled` was already `true`, so the planned mutation was
unnecessary.

| Change | Reverted? | Verification |
|---|---|---|
| 2 probe Chrome instances (own profile, ports 9225/9226) | ✅ | Both ports refuse connections; processes gone |
| `/tmp/osk-probe/` | ✅ | `No such file or directory`; no residue |
| Transient `uinput` devices | ✅ | Device count **15 → 15**, name-list md5 **identical** (`b9346afa…`) |
| `Escape` into modal | n/a — no effect | Screen byte-identical (`md5 607ac20c…`) |
| `a` then `BackSpace` in password field | ✅ net zero | Field observed **empty**, as found |
| Click on modal `Cancel` | n/a — no effect | Modal still present; hover highlight only |
| Mutter ScreenCast sessions | ✅ | Each `Session.Stop()`ed; none left |
| Mouse pointer moved | ✅ self-healing | `unclutter -idle 3` |

**Not touched:** `radio-api`, `radio-web`, `rotary-phone` — all still `active`, never restarted. Kiosk Chrome
**2808**, RotaryPhone **2328** / **3268** — all with unbroken uptime since the 2026-08-02 boot. The teardown
script hard-refuses those three PIDs by number.

Final state: `screen-keyboard-enabled=true` (unchanged), onboard pid 2354 running, 15 input devices, all three
services `active`, `:5002` → 200. `evidence/08-final-state.png` matches `evidence/00-baseline-screen.png`.

### ⚠ One thing that could not be reverted — because it was not ours

> **Superseded in part on 2026-08-03 — see [F-1 remediation](#f-1-remediation--2026-08-03).** The claim below
> that the box "will keep doing so" no longer holds: the Radio Console is now rendering. The modal itself does
> persist, but it is now attributable to RotaryPhone's pid 3268, not to the kiosk.

**F-1's keyring modal is still on screen.** It could not be cleared (Escape ignored; `Cancel` needs pointer
injection, which was blocked). It was not made worse — password field empty exactly as found, final capture
matches the first — but **the box has been showing this dialog instead of the Radio Console for ~33 hours and
will keep doing so.** Remediation without physical access, least to most intrusive:

1. **Preferred, permanent:** add `--password-store=basic` to `radio-kiosk-autostart.desktop:4`, restart kiosk
   Chrome. The prompt never returns.
2. Restart kiosk Chrome alone — *may* clear it if 2808 is the requester. Unverified: 3268 also lacks the flag and
   could be the requester.
3. Someone with input permission clicks `Cancel` at **(856, 468)**.

---

## Appendix — `PhoneIntegration:Enabled` (side question from the Coordinator)

**It is `false` on the running box, and that is the shipped default, not drift.**

| Source | `PhoneIntegration` |
|---|---|
| `/opt/radio-console/api/appsettings.json` | `"Enabled": false`, `RingPriority: 9`, `AnnouncementPriority: 8` |
| `/opt/radio-console/api/appsettings.Production.json` | **key absent** — no override |
| `radio-api` unit `Environment=` | no `PhoneIntegration__*` override |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

The Production layer contributes nothing → effective value **`false`**. The repo's
`src/Radio.API/appsettings.json` also has `false`, and `git log -S'"PhoneIntegration"'` returns exactly **one**
commit — `8d2a2ab` *"feat: Add rotary encoder, phone integration, and notification systems with UI"* — the one
that introduced the block. Never flipped since. So: **"never enabled,"** not "was on and drifted off."

**Implication for ADR-029:** ring **9** and announcement **8** have **no live occupant on this box** —
unexercised constants in a disabled service's section. Anchoring new ducking thresholds to them anchors to
something that has never run in production here. Worth deciding explicitly whether `PhoneIntegration` is meant to
be switched on (note `HubUrl` → `http://radio:5004/hub`, and RotaryPhone *is* serving on 5004), or whether the
priority scale should be re-anchored to sources that actually play.

---

## F-1 remediation — 2026-08-03

**When:** 2026-08-03, 14:07–14:18 EDT · **Authorization:** owner-authorized live patch + kiosk-Chrome restart,
owner away from hardware ~1 week · **Scope:** one flag, one process.

### Outcome in one line

**The Radio Console is rendering on the panel again** (observed via screen capture, not inferred). **The keyring
modal is still on screen**, dimming and overlaying it — and it is now demonstrably **not** the kiosk's prompt.
Clearing it requires a change to RotaryPhone's pid 3268, which was **not** made: cross-repo boundary, escalated
instead.

### What was changed

Exactly one line of `~/.config/autostart/radio-kiosk-autostart.desktop`. Backup taken first —
`radio-kiosk-autostart.desktop.bak-20260803-140727`, sha256 verified identical to the original
(`e8d42c018ef9…`).

**Original `Exec=` (verbatim, as found live — identical to the repo copy):**

```
Exec=google-chrome --kiosk --noerrdialogs --disable-infobars --disable-session-crashed-bubble --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --force-renderer-accessibility --ozone-platform=wayland --remote-debugging-port=9223 http://localhost:5002
```

**Patched `Exec=` (verbatim):**

```
Exec=google-chrome --kiosk --noerrdialogs --disable-infobars --disable-session-crashed-bubble --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --force-renderer-accessibility --ozone-platform=wayland --remote-debugging-port=9223 --password-store=basic http://localhost:5002
```

`diff` against the backup is a single changed line. Kiosk Chrome **2808** was then SIGTERM'd and relaunched from
that patched line. **New PID: 156618.**

### How much worse the pre-state was than F-1 recorded

F-1 said the modal was displayed *instead of* the Radio Console but flagged as *not proven* whether the kiosk
window was "mapped-but-occluded." It was neither — **2808 had never loaded the page at all**:

| Probe | pid 2808 (before) | pid 156618 (after) |
|---|---|---|
| ESTABLISHED conns to `:5002` | **0** | **14** at first check, 4–6 steady-state |
| `--type=renderer` processes | **0** | **3** |
| Radio Console visible | no | **yes** |

Zero renderers and zero connections to a service that was answering `200` the whole time. Chrome 2808 was blocked
in profile initialisation on the keyring prompt and never reached the navigation. So the fix did not merely
un-occlude a window — it let the kiosk start for the first time since the 2026-08-02 boot.

*(Method note for future sessions: `ps -eo … --ppid N` silently ignores the filter because `-e` overrides it —
use `pgrep -P N`. And Chrome renderers are forked from the **zygote**, so they are not children of the browser
process; count them across the whole process table.)*

### Verification — observed, not inferred

Screen capture via the route this report documents (`org.gnome.Mutter.ScreenCast` → `RecordMonitor("DP-1")` →
PipeWire → `gst-launch-1.0 pipewiresrc`).

**Instrument validated three independent ways**, so "it worked" cannot be a dead pipeline replaying a stale frame:

1. Every run returned **all frames distinct** (12/12, 12/12, 10/10, 9/10) — the stream is live per-frame.
2. Two captures 20 s apart read the Radio Console's **own top-bar clock** as `2:15:38 PM` and `2:15:59 PM` — a
   21 s delta matching the sleep (`evidence/10-capture-instrument-validation.png`).
3. Page content advanced between captures on its own — now-playing went *The Zombies · "Time of the Season"* →
   *The Clash · "Should I Stay or Should I Go"*, TOTAL PLAYS 35486 → 35488.

Damage starvation never materialised — the modal's blinking caret pre-fix, and the live UI post-fix, both supply
continuous damage. No damage injection was needed.

| Artifact | Content |
|---|---|
| `evidence/00-baseline-screen.png` | prior session's baseline — modal, no Radio Console |
| `evidence/08b-pre-remediation-screen.png` | this session, pre-patch — **modal + onboard keyboard + RotaryPhone's Google Voice windows; no Radio Console anywhere** |
| `evidence/09-post-fix-screen.png` | this session, post-patch — **Radio Console rendering fullscreen** (top bar, source tabs, SDR 97.75 MHz, now-playing + album art, history list, visualiser tabs) **with the keyring modal still overlaid** |
| `evidence/10-capture-instrument-validation.png` | the two clock crops, stacked |

### The modal that remains is not ours

| pid | Owner | `--password-store` |
|---|---|---|
| **156618** | Radio Console kiosk (new) | **`basic` ✅** |
| 2328 | RotaryPhone snap-chromium | `basic` ✅ |
| **3268** | **RotaryPhone google-chrome** | **absent ❌** |

3268 is the only browser left on the box without the flag. Two live prompt objects —
`/org/freedesktop/secrets/prompt/u1` and `/org/freedesktop/secrets/prompt/u2` — were still present after the
restart, and a `Locked` property read on the `login` collection **hung**, i.e. `gnome-keyring-daemon` is still
serialised behind an active prompt. `org.gnome.keyring.SystemPrompter` is owned by **gnome-shell (1866)**,
confirming F-1's mechanism.

**Not proven:** which of `u1`/`u2` belongs to which client, and whether one is an orphan left by the killed 2808.
The prompt objects expose no requester identity, and establishing it would mean probing 3268. What *is* proven is
that neither belongs to 156618 — it carries the flag and never contacts the secret service.

**Escalation:** fixing this means adding `--password-store=basic` to RotaryPhone's launcher for 3268
(`gv-bridge-chrome.service` / `~/.config/autostart/gv-bridge-chrome.desktop`). That is RotaryPhone's file and
RotaryPhone's process — **not changed here**. Route via
`D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md`. Until then the panel shows a **dimmed,
input-grabbed** Radio Console: it renders and updates live, but touch input goes to the modal.

### Deliberately not done

- **F-5's `--user-data-dir` was NOT added.** One change per restart so a misbehaving kiosk has an unambiguous
  cause. Confirmed still outstanding: `:9223` has no listener (only 2328's `:9224` is bound), so
  `radio-refresh-browser` remains broken. Second pass.
- `radio-api`, `radio-web`, `rotary-phone` — **not restarted**; all `active` throughout.
- RotaryPhone's **2328 / 3268** and onboard **2354** — untouched, unbroken uptime from the 2026-08-02 boot
  (`1-11:14` at final check). The restart script hard-refused those PIDs by number and re-verified the target's
  cmdline before signalling.
- The repo's `deploy/debian-x64/kiosk/radio-kiosk-autostart.desktop` still lacks the flag — **the live box and
  the repo have now diverged.** The repo copy needs the same one-flag change or the next provision run will
  regress this.

### Rollback

```bash
cp ~/.config/autostart/radio-kiosk-autostart.desktop.bak-20260803-140727 \
   ~/.config/autostart/radio-kiosk-autostart.desktop
```

Then restart Chrome from that line. The change is autostart-only — a reboot re-reads the file, so the patch is
also what makes the fix survive the next boot.
