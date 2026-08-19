# PROBES — kiosk desktop launcher (plan Task A0)

**Box:** `ssh mmack@radio` — Intel N100, Ubuntu 24.04, GNOME 46 **Wayland**, 1920×720, passwordless sudo.
**Read:** 2026-08-18, by the owner, off the live box.
**Plan:** [`docs/superpowers/plans/2026-08-18-kiosk-desktop-launcher.md`](../../superpowers/plans/2026-08-18-kiosk-desktop-launcher.md)

Task A0 exists so that no later task guesses these. It is **resolved** — the four unknowns below
are answers, not open questions. `KIOSK-2` should read this file rather than re-probe: two of the
four cost a round trip to the box, and one of them (`--icon-name`) changes what `KIOSK-2` can build.

---

## 1. zenity — version and GTK major

| Field | Value |
|---|---|
| Version | **4.0.1** |
| Linked against | **libgtk-4** |

**Consequence for `KIOSK-2` Task B3:** the 56 px touch-target CSS goes in **`gtk-4.0/gtk.css`**,
not `gtk-3.0/gtk.css`. A `gtk-3.0/` file is read by nothing here and would fail silently — the
dialog would simply ship with GTK's ~34 px default buttons, which is under the 48 px touch floor.
The plan's §10.3 route (point `XDG_CONFIG_HOME` at a private config dir) is unchanged; only the
subdirectory name differs.

## 2. zenity — option support

| Option | Supported | Notes |
|---|---|---|
| `--timeout` | ✅ | Variant A's 10 s auto-dismiss (§6.4) is available. |
| `--default-cancel` | ✅ | The shutdown confirm can default to Cancel (§8 / Task B6). |
| `--width` | ✅ | 760 px dialogs as specced. |
| `--height` | ✅ | |
| `--icon-name` | ❌ **NOT SUPPORTED** | **zenity 4 dropped it.** |

**Consequence for `KIOSK-2` Task B2 — this one changes the build, not just a flag.** The plan's
`report_dialog()` uses `zen --question --icon-name=dialog-error …` and
`zen --question --icon-name=dialog-warning …` to get a red or amber glyph on a two-button dialog.
That combination is no longer available: per-tier iconography must come from zenity's **built-in
`--info` / `--warning` / `--error`** dialog types, which carry their own glyphs but take a single
OK button.

So `KIOSK-2` has to choose per tier between *the right icon* and *two buttons*, and the plan's
§10.2 rule decides it: **do not ship a button that does nothing** — but equally, do not drop a
button that does something real. `Try again` / `Open anyway` (Variant C) are both live actions and
must survive; the red glyph is what gives way. Options are a `--question` with no stock icon plus
explicit red in the Pango headline, or `--forms`/`--list`. **This is a `KIOSK-2` decision and is
deliberately not made here.** It is recorded so that row starts from the constraint instead of
discovering it mid-implementation.

## 3. `systemctl --user show-environment` — does it carry the graphical session env?

**Yes. This is the single most load-bearing answer in this file — the entire Part A relaunch fix
rests on it.**

```
WAYLAND_DISPLAY=wayland-0
XDG_RUNTIME_DIR=/run/user/1000
DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus
DISPLAY=:0
```

A transient unit started with `systemd-run --user` inherits the **user service manager's**
environment, not the calling shell's. Because the user manager carries `WAYLAND_DISPLAY`, a kiosk
launched over SSH lands on Wayland with the same environment as one launched at boot — which is
exactly what `DISPLAY=:0 nohup google-chrome …` failed to do.

**Verified in practice, not only by inspection:** the owner relaunched the kiosk this way by hand
on 2026-08-18 during the incident, and it worked.

**Consequence for Task A2:** the `--setenv=` fallback block the plan pre-wrote is **not needed** and
is not shipped. `radio-kiosk-launch` records why in a comment, and says what to do instead if some
future box ever lacks `WAYLAND_DISPLAY` there — pass `--setenv=` explicitly, never fall back to
`DISPLAY=:0`, which is the defect the script exists to remove.

## 4. Rotary phone unit name

**`rotary-phone.service`.**

A second unit, **`rotary-phone-cookies.service`**, also exists — it is a **oneshot cookie refresh,
not the API**. Starting it would not bring the phone service up.

**Consequence for Task A4 / `KIOSK-2` Task B2:** `setup-kiosk.sh` discovers the unit rather than
hard-coding it, and its `grep -iE '^(rotary-?phone|rotaryphone)\.service$'` anchors on a full unit
name so the cookies oneshot cannot win the `head -1`. A wrong name here would make `KIOSK-2`'s
launcher report `PHONE` as a permanent hard failure — precisely the cry-wolf outcome §5.3 exists to
prevent.

---

## Not probed, and why

The plan's A0 block also probed for DING schema presence, `flock` / `jq` / `desktop-file-validate` /
`rsvg-convert` availability, and the current `~/Desktop` mode/drift baseline. **Every one of those
gates a `KIOSK-2` task only** (icon sizing, the single-instance guard, icon rasterisation, the
dialog). None of them gates anything in Part A, so they are left for `KIOSK-2` to read at the point
it needs them rather than recorded here as stale facts. Part A's own installer guards the two it
touches: `desktop-file-validate` is called only behind a `command -v` test, and the icon directory
is created with `mkdir -p`.

## Drift baseline (the thing Part A closes)

Recorded because it is the evidence for the change, not because anything branches on it:

- Nothing had ever copied `deploy/debian-x64/kiosk/*.desktop` onto the box. `~/Desktop` was
  hand-maintained.
- The in-tree `radio-console.desktop` has carried `--password-store=basic` since 2026-08-11; the
  live copy did not.
- `GV-Bridge.desktop` was mode **775**, and GNOME silently refuses to launch a group-writable
  `.desktop` file — that mode bit, not a "permission problem", is why it never worked. It is why
  `setup-kiosk.sh` now uses `install -m 755` and never `chmod +x`.
- `~/.config/autostart/onboard-autostart.desktop` was already renamed to `.disabled` by hand on the
  box before this row; Task A6 is what makes that stick across a re-provision.
