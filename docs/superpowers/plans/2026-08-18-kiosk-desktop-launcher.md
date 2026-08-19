# Plan — Kiosk desktop launcher: three icons, one repair action

**Spec / handoff:** [`docs/design-handoffs/HANDOFF-kiosk-desktop-launcher.md`](../../design-handoffs/HANDOFF-kiosk-desktop-launcher.md)
**Queue rows:** `KIOSK-1` (Part A) · `KIOSK-2` (Part B)
**Branches:** `fix/kiosk-launch-contract` (Part A) → `feat/kiosk-desktop-launcher` (Part B)
**Target:** Ubuntu 24.04 + GNOME 46 **Wayland**, 1920×720 touchscreen, `ssh mmack@radio`, passwordless sudo.

---

## Why this is two PRs and not one

**Part A is the foundation and the standalone win.** It gives the kiosk Chrome its own
`--user-data-dir`, makes that command line exist in exactly one file, and fixes
`Deploy-ToLinux.ps1` so a deploy stops leaving the screen dead. It changes no UX and adds no
dialog, so it can be verified entirely with objective probes and merged fast. It is also the
single riskiest change in the whole body of work (a new Chrome profile), which is precisely
why it should not be entangled with 600 lines of new dialog shell.

**Part B is the UX.** Every one of its pieces — the scoped exit, the repair-and-open launcher,
the single-instance guard — needs Part A's profile to already exist. Landing B first would mean
matching the kiosk on the *absence* of a flag, which §7.2 of the spec rejects as fragile.

**Ordering is load-bearing: `KIOSK-1` before `KIOSK-2`.** Not a preference.

---

## Owner decisions folded in (settled — do not re-litigate)

| Spec §11 | Decision | Where it lands |
|---|---|---|
| Q1 — rename `Exit Browser` → `Exit to Desktop` | **approved** | Task B4 |
| Q2 — replace GNOME's auto-proceeding shutdown confirm | **approved** (overrides §8's "not in scope") | Task B6 |
| Q3 — stateless `psidtsAgeSeconds > 1200` probe | **approved**, no Architect escalation | Task B2 |
| Q4 — in-app Blazor favicon (`App.razor:9`) | **DEFERRED** — see below | *(no task)* |
| Q5 — keep the four reported components | **approved** | Task B2 |

> **Q4 deferral, recorded so it is not silently lost.** The in-app favicon change is **out of
> scope for both PRs** and gets its own pass later. `--kiosk` hides the tab strip, address bar
> and title bar, so that favicon is never visible on the appliance — it would only ever show in
> a remote browser tab or a bookmark, which does not justify carrying the dark-surface and
> 16–32 px legibility risks inside a launcher cleanup. **`src/Radio.Web/Components/App.razor:9`
> is not touched by this plan.** The *desktop* icon (Task B1) is unaffected and fully in scope —
> that is the one the owner actually taps.

Plus three fixes the spec identified as prerequisites rather than polish (§10.5, §10.6, §6.8):
`chmod 755` not `chmod +x`; the missing repo→`~/Desktop` installer; 56 px GTK buttons.

---

## Cross-repo boundary — read before writing any GV bridge code

`~/bin/gv-bridge-ensure.sh`, its watchdog timer and its nightly restart timer are
**RotaryPhone-owned** (`D:\prj\RotaryPhone`), and a Builder is bringing them under version
control **right now** in that repo.

**This plan invokes and probes. It never owns, reimplements, edits or installs bridge startup.**
Concretely:

- The launcher **calls** `~/bin/gv-bridge-ensure.sh` and reads its exit code.
- The launcher **probes** the bridge Chrome by its profile dir and the `:5004` status body.
- The launcher **must not** write the script, install a unit, touch a timer, or `pkill` the
  bridge under any circumstance.
- Because the in-flight RotaryPhone PR may relocate the script, Task B2 **resolves it from a
  candidate list at runtime** rather than hard-coding one path.

The new-consumer note for `RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md`'s Change Log is a commit in the
*other* repo, so per the precedent set on 2026-08-10 it is **not a task here** — it is queued as
§ Cross-repo handoffs **#8** in `docs/BUILDER_QUEUE.md`.

---

## Verified environment facts (do not re-derive)

- Box is `ssh mmack@radio` (never the bare IP), Ubuntu 24.04 + GNOME 46 **Wayland**, auto-login,
  1920×720, **passwordless sudo**.
- `zenity` and `notify-send` are installed. **`yad` and `kdialog` are not.** Icon theme is **Yaru**.
- There is **no `audio-radio` icon in any installed theme** — that is why the current entry
  renders a generic document glyph.
- `~/.local/share/icons` **does not exist yet**.
- `--window-position` is a **no-op** under Wayland. Stacking is decided by launch order:
  **most recently launched wins.**
- Screen capture is largely unavailable: Shell screenshot API and `GetWindows` are
  `AccessDenied`; `gnome-screenshot` / `grim` / `scrot` are absent. **Prefer objective probes.**
- `Deploy-ToLinux.ps1` defaults to `-Runtime linux-arm64` and the box is **x86_64** — every
  invocation in this plan passes **`-Runtime linux-x64`**.
- Working liveness check for the kiosk: **established TCP connections to `:5002`**. During the
  Aug 2 outage that count was **0** while `radio-web` returned 200 the whole time. Process
  existence is not the check.
- The bridge watchdog timer is enabled and active, and `gv-bridge-ensure.sh` already carries
  `--remote-debugging-port=9224 --remote-allow-origins=*`. Both are handled; plan around them.

---

# PART A — `KIOSK-1` · one kiosk launch line, and it works under Wayland

Branch: `fix/kiosk-launch-contract`

## Task A0 — Probe the box and pin the four unknowns — ✅ **RESOLVED 2026-08-18**

> **This task is done and is no longer a gate.** The owner read all four unknowns off the live box
> on 2026-08-18 and the answers are recorded in
> [`docs/uat/2026-08-18-kiosk-launcher/PROBES.md`](../../uat/2026-08-18-kiosk-launcher/PROBES.md).
> **Do not re-run the probe block below** — re-checking costs a round trip to the box and risks
> disagreeing with what was verified. It is retained verbatim for the record.
>
> | Unknown | Answer | What it decides |
> |---|---|---|
> | zenity version / GTK major | **4.0.1, linked against libgtk-4** | `KIOSK-2` Task B3's 56 px CSS goes in **`gtk-4.0/gtk.css`**, not `gtk-3.0/` |
> | `--timeout` · `--default-cancel` · `--width` · `--height` | **all supported** | Variant A's 10 s auto-dismiss and Task B6's Cancel default are both available |
> | `--icon-name` | **NOT SUPPORTED — zenity 4 dropped it** | ⚠ **`KIOSK-2` Task B2 cannot use `--question --icon-name=…`.** Per-tier iconography must come from the built-in `--info` / `--warning` / `--error` glyphs, which take a single button. §10.2 forbids shipping a button that does nothing — but `Try again` / `Open anyway` are both live actions, so the **glyph** gives way, not the buttons. The exact substitution is a `KIOSK-2` decision; it just starts from this constraint instead of discovering it. |
> | `systemctl --user show-environment` | **carries `WAYLAND_DISPLAY=wayland-0`, `XDG_RUNTIME_DIR=/run/user/1000`, `DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus`, `DISPLAY=:0`** | **The entire Part A relaunch fix rests on this inheritance, and it holds.** Task A2 ships **without** the `--setenv=` fallback block. The owner relaunched the kiosk this way by hand on 2026-08-18 and it worked. |
> | rotary unit name | **`rotary-phone.service`** (a separate `rotary-phone-cookies.service` is a oneshot cookie refresh, **not** the API) | Task A4's discovery regex anchors on the full unit name so the cookies oneshot cannot win `head -1` |
>
> The remaining probes in the block below (DING schema, `flock` / `jq` / `rsvg-convert` presence,
> the `~/Desktop` mode baseline) gate **`KIOSK-2` tasks only**. They are deliberately left for that
> row to read at the point it needs them, rather than recorded now and going stale.

**No code ships in this task.** It produced one artifact, `docs/uat/2026-08-18-kiosk-launcher/PROBES.md`,
and its answers are substituted into later tasks. Do not guess any of these.

```bash
ssh mmack@radio 'bash -s' <<'PROBE'
echo "== zenity =="
zenity --version
ldd "$(command -v zenity)" | grep -E 'libgtk-(3|4)' || echo "NO GTK LINK FOUND"

echo "== zenity option support (exit code 0 = supported) =="
timeout 4 zenity --info --timeout=2 --text=probe >/dev/null 2>&1; echo "  --timeout: $?"
timeout 4 zenity --question --icon-name=dialog-error --timeout=2 --text=probe >/dev/null 2>&1; echo "  --icon-name: $?"
timeout 4 zenity --question --default-cancel --timeout=2 --text=probe >/dev/null 2>&1; echo "  --default-cancel: $?"
timeout 4 zenity --warning --ok-label=Continue --timeout=2 --text=probe >/dev/null 2>&1; echo "  --ok-label on --warning: $?"

echo "== systemd user manager environment (Part A depends on this) =="
systemctl --user show-environment | grep -E 'WAYLAND_DISPLAY|DISPLAY|XDG_SESSION_TYPE|XDG_CURRENT_DESKTOP' || echo "  NONE PRESENT"

echo "== rotary-phone unit name =="
systemctl list-units --type=service --all --no-legend | grep -iE 'rotary|phone' || echo "  NOT FOUND"

echo "== gv-bridge ensure script =="
ls -l ~/bin/gv-bridge-ensure.sh /usr/local/bin/gv-bridge-ensure.sh /opt/rotary-phone/bin/gv-bridge-ensure.sh 2>/dev/null

echo "== desktop icons extension =="
gsettings list-schemas | grep -i ding || echo "  NO DING SCHEMA"
gio info ~/Desktop/*.desktop 2>/dev/null | grep -i 'metadata::' | sort -u

echo "== tooling presence =="
for t in jq desktop-file-validate flock rsvg-convert inkscape convert; do
  printf '  %-22s %s\n' "$t" "$(command -v $t || echo ABSENT)"
done

echo "== current desktop state (the drift baseline) =="
stat -c '%a %U:%G %n' ~/Desktop/*.desktop
grep -l 'password-store=basic' ~/Desktop/*.desktop 2>/dev/null || echo "  NO desktop entry carries --password-store=basic (expected: the drift)"
PROBE
```

**Answers that gated later tasks** — the "if it says" column is retained as the Planner wrote it;
the rows now resolved are marked in the summary block at the top of this task:

| Probe | If it says… | Then… |
|---|---|---|
| `ldd zenity` shows **libgtk-4** | GTK 4 | Task B3's CSS goes in `gtk-4.0/gtk.css`, not `gtk-3.0/` |
| `--timeout` unsupported | | Variant A loses auto-dismiss; use `--ok-label=OK` and say so in the queue row |
| `--icon-name` unsupported | | Variants B2/C use `--warning`/`--error` and lose the second button; per §10.2 **do not ship a button that does nothing** — reduce to one button plus the copy |
| `WAYLAND_DISPLAY` **absent** from `systemctl --user show-environment` | | Task A2 must pass `--setenv=` explicitly (fallback block is written into A2) |
| rotary unit name | e.g. `rotary-phone.service` | substituted as `@ROTARY_UNIT@` in Task B2 |
| DING schema absent | | skip `icon-size`; §10.1's fallback (alphabetical is what the names already produce) still holds |
| `flock` absent | | Task B2's single-instance guard falls back to a PID-file + `kill -0` check |

---

## Task A1 — Commit the design handoff (Planner has already done this)

`docs/design-handoffs/HANDOFF-kiosk-desktop-launcher.md` lands with the **planning** PR, together
with an owner-decisions block appended to §11 and a pointer at §8 recording that Q2 was approved.
**Builder does not need to touch it.** Listed here only so nobody re-commits it.

---

## Task A2 — `radio-kiosk-launch`: the only place the kiosk command line lives

> **Part A ships two scripts, not one.** `radio-kiosk-exit` lands here too, because
> `Deploy-ToLinux.ps1` (Task A5) calls it in its stop phase. Its full content and rationale are
> specified once, in **Task B5** — Part B only wires the desktop entry to it. Do not write a
> second copy.

**New file:** `deploy/debian-x64/kiosk/bin/radio-kiosk-launch`
**Installs to:** `/usr/local/bin/radio-kiosk-launch`, mode `755` (survives deploys — `Deploy-ToLinux.ps1`
wipes `/opt/radio-console/{api,web}` only).

Three callers converge on this file: the autostart entry (Task A3), `Deploy-ToLinux.ps1`
(Task A5) and the repair-and-open launcher (Task B2). Today those three carry three *different*
flag sets, which is how the box drifted in the first place.

```bash
#!/usr/bin/env bash
# radio-kiosk-launch — the single definition of the Radio Console kiosk browser command line.
#
# Callers: radio-kiosk-autostart.desktop (boot), Deploy-ToLinux.ps1 (post-deploy relaunch),
# radio-console-open (the desktop icon). Every flag change belongs here and nowhere else.
set -euo pipefail

KIOSK_PROFILE="${RADIO_KIOSK_PROFILE:-$HOME/.config/radio-kiosk-chrome}"
KIOSK_URL="${RADIO_KIOSK_URL:-http://localhost:5002}"
CDP_PORT="${RADIO_KIOSK_CDP_PORT:-9223}"

# --user-data-dir is load-bearing three times over, not tidiness:
#   1. It is what lets "Exit to Desktop" kill the kiosk WITHOUT killing the Google Voice
#      bridge Chrome, which runs on ~/.config/gv-bridge-chrome and which nothing restarts
#      on demand (its watchdog timer is a 2-minute cadence).
#   2. Chrome >= 136 silently ignores --remote-debugging-port on the DEFAULT user-data-dir.
#      A non-default profile is the documented fix, so CDP on :9223 becomes real again.
#   3. CLAUDE.md records that --password-store=basic DESTROYS v11 cookies on a profile that
#      already holds them (measured live: 45 v11 -> 16 v10, taking the Google Voice session
#      with it). It is safe only on a profile that was `basic` from first run. This profile
#      is created by this script and only ever visits localhost:5002, so it holds no session
#      to lose. Do not point these flags back at the default profile.
CHROME_FLAGS=(
  --kiosk
  --user-data-dir="$KIOSK_PROFILE"
  --ozone-platform=wayland
  --password-store=basic
  --no-first-run
  --no-default-browser-check
  --noerrdialogs
  --disable-infobars
  --disable-session-crashed-bubble
  --disable-background-timer-throttling
  --disable-renderer-backgrounding
  --disable-backgrounding-occluded-windows
  --force-renderer-accessibility
  --remote-debugging-port="$CDP_PORT"
)

kiosk_pids() { pgrep -f -- "--user-data-dir=$KIOSK_PROFILE" 2>/dev/null || true; }

case "${1:-}" in
  --is-running) [ -n "$(kiosk_pids)" ] ;;
  --print-profile) printf '%s\n' "$KIOSK_PROFILE" ;;
  --print-command) printf '%s ' google-chrome "${CHROME_FLAGS[@]}" "$KIOSK_URL"; echo ;;
  "")
    if [ -n "$(kiosk_pids)" ]; then
      echo "radio-kiosk-launch: kiosk already running ($(kiosk_pids | tr '\n' ' '))" >&2
      exit 0
    fi

    # XDG_RUNTIME_DIR is how `systemd-run --user` finds the user service manager over a
    # non-login SSH connection, where it is otherwise unset. Inside the graphical session
    # it is already correct and this assignment is a no-op.
    export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
    export DBUS_SESSION_BUS_ADDRESS="${DBUS_SESSION_BUS_ADDRESS:-unix:path=$XDG_RUNTIME_DIR/bus}"

    # The transient unit inherits the USER SERVICE MANAGER's environment, not this shell's, and
    # that inheritance is the entire fix. Task A0 CONFIRMED WAYLAND_DISPLAY is present there
    # (see PROBES.md §3), so the --setenv= fallback this plan originally carried is not shipped.
    # If some future box ever lacks it, pass --setenv= explicitly rather than falling back to
    # DISPLAY=:0 — DISPLAY=:0 is the defect this script exists to remove (it lands the kiosk
    # under XWayland with the wrong flag set).
    systemctl --user reset-failed radio-kiosk.service 2>/dev/null || true
    exec systemd-run --user --collect --unit=radio-kiosk \
      --description="Radio Console kiosk browser" \
      google-chrome "${CHROME_FLAGS[@]}" "$KIOSK_URL"
    ;;
  *) echo "usage: radio-kiosk-launch [--is-running|--print-profile|--print-command]" >&2; exit 2 ;;
esac
```

**Why `systemd-run --user --collect` and not `nohup … &`:** the transient unit is started *by
the graphical session's own service manager*, so it runs with that session's environment
(`WAYLAND_DISPLAY`, `DBUS_SESSION_BUS_ADDRESS`, `XDG_CURRENT_DESKTOP`) instead of whatever a
detached SSH shell happens to hold. This is the invocation the owner verified by hand today, and
the resulting kiosk showed **2 established connections to `:5002`**. `--collect` reaps the unit
when Chrome exits so a later relaunch is not blocked by a lingering failed unit.

**Verify (objective):**

```bash
ssh mmack@radio '/usr/local/bin/radio-kiosk-launch --print-command'   # flag set is what you expect
ssh mmack@radio '/usr/local/bin/radio-kiosk-exit 2>/dev/null; sleep 2; /usr/local/bin/radio-kiosk-launch'
sleep 8
ssh mmack@radio "ss -tn state established | grep -c ':5002' || true"  # expect >= 1
ssh mmack@radio 'systemctl --user is-active radio-kiosk.service'      # expect: active
ssh mmack@radio 'curl -sf http://localhost:9223/json/version | head -c 200'  # CDP is alive again
ssh mmack@radio 'test -d ~/.config/radio-kiosk-chrome && echo profile-created'
```

---

## Task A3 — Point the autostart entry at the launch script

**Edit:** `deploy/debian-x64/kiosk/radio-kiosk-autostart.desktop`

```ini
[Desktop Entry]
Name=Radio Console Kiosk
Comment=Auto-launch Radio Console in kiosk mode
Exec=/usr/local/bin/radio-kiosk-launch
Icon=audio-radio
Terminal=false
Type=Application
X-GNOME-Autostart-enabled=true
X-GNOME-Autostart-Delay=5
```

**`Exec=` is the only line Part A changes.** `Icon=audio-radio` stays broken for now on purpose:
the icon asset arrives in Task B1, and shipping `Icon=@ICON_DIR@/radio-console.svg` before the
file exists would trade a generic glyph for a missing one. Task B4 switches it.

> **The autostart path must NOT run the repair action** (`radio-console-open`, Task B2). A slow
> boot would then fire the repair dialog every morning, which is the exact noise §6.7 exists to
> prevent. Boot already starts `radio-api`/`radio-web` via systemd; autostart just opens the screen.

**Verify:** `ssh mmack@radio 'grep -c "radio-kiosk-launch" ~/.config/autostart/radio-kiosk-autostart.desktop'` → `1`.

---

## Task A4 — `setup-kiosk.sh`: make the repo the source of truth for `~/Desktop`

This is the root cause of the whole drift problem. Three separate instances of it turned up in
one day: the live `radio-console.desktop` lacks `--password-store=basic` that the repo has had
since Aug 11; `GV-Bridge.desktop` was mode 775 and therefore unlaunchable; nothing ever copied
the repo's entries onto the box. **Closing this loop is a first-class goal, not a side effect.**

**Edits to `deploy/debian-x64/kiosk/setup-kiosk.sh`:**

Replace the whole `[1/5] Install desktop shortcuts` block:

```bash
# ---- 1. Install desktop shortcuts ----
echo "[1/8] Installing desktop shortcuts..."

APPS_DIR="$HOME/.local/share/applications"
DESKTOP_DIR="$HOME/Desktop"
BIN_DIR="/usr/local/bin"
ICON_DIR="$HOME/.local/share/icons/radio-console"
GTK_DIR="/usr/local/share/radio-console/gtk-touch"

mkdir -p "$APPS_DIR" "$DESKTOP_DIR" "$ICON_DIR"

# The rotary-phone unit name is DISCOVERED, not assumed. Task A0 read it off the box, and a
# wrong name here would make KIOSK-2's launcher report PHONE as a hard failure forever — which
# is exactly the cry-wolf failure the Google Voice rule exists to prevent. Defined in Part A
# because install_entry() substitutes it and the script runs under `set -u`.
ROTARY_UNIT="${ROTARY_UNIT:-$(systemctl list-units --type=service --all --no-legend \
  | awk '{print $1}' | grep -iE '^(rotary-?phone|rotaryphone)' | head -1)}"
if [ -z "$ROTARY_UNIT" ]; then
  echo "  WARNING: no rotary-phone service unit found; PHONE repair will be a no-op."
  ROTARY_UNIT="rotary-phone.service"
fi

# Mode 755, NOT `chmod +x`. With the default umask `chmod +x` yields 775, and GNOME
# REFUSES to launch a group-writable .desktop file — silently, with no error anywhere.
# That mode bit is why the box's GV-Bridge entry never worked. `install -m 755` states
# the mode instead of incrementing it, so the umask cannot leak in.
install_entry() {
  local src="$1" name; name="$(basename "$src")"
  sed -e "s|@ICON_DIR@|$ICON_DIR|g" \
      -e "s|@KIOSK_USER@|$KIOSK_USER|g" \
      -e "s|@ROTARY_UNIT@|$ROTARY_UNIT|g" \
      "$src" > "$DESKTOP_DIR/$name.tmp"
  install -m 755 "$DESKTOP_DIR/$name.tmp" "$DESKTOP_DIR/$name"
  install -m 644 "$DESKTOP_DIR/$name.tmp" "$APPS_DIR/$name"
  rm -f "$DESKTOP_DIR/$name.tmp"
  gio set "$DESKTOP_DIR/$name" metadata::trusted true 2>/dev/null || true
  if command -v desktop-file-validate >/dev/null 2>&1; then
    desktop-file-validate "$DESKTOP_DIR/$name" || echo "  WARNING: $name failed validation"
  fi
  echo "  Installed: $name (mode $(stat -c '%a' "$DESKTOP_DIR/$name"))"
}

for file in radio-console.desktop radio-exit-browser.desktop radio-shutdown.desktop; do
  install_entry "$SCRIPT_DIR/$file"
done
```

Add a new block after it:

```bash
# ---- 1b. Install kiosk helper scripts ----
echo "[2/8] Installing kiosk helper scripts..."
for s in radio-kiosk-launch radio-kiosk-exit; do
  sudo install -m 755 "$SCRIPT_DIR/bin/$s" "$BIN_DIR/$s"
  echo "  Installed: $BIN_DIR/$s"
done
```

Add a cleanup block (idempotent; safe to re-run):

```bash
# ---- 1c. Remove entries this setup no longer owns ----
echo "[3/8] Removing superseded desktop entries..."

# `onboard` is dropped: docs/uat/2026-08-03-osk-wayland-viability/REPORT.md measured Chrome 151
# on Wayland issuing ZERO zwp_text_input_v3.enable() calls, so the OS keyboard cannot type into
# a web page here at all. The Web UI's built-in virtual keyboard is the only working text input.
# The package is dropped from deploy/provision/packages.sh; this disables the autostart entry a
# hand-provisioned box may still carry. Renamed rather than deleted so it is recoverable.
ONBOARD_AUTOSTART="$HOME/.config/autostart/onboard-autostart.desktop"
if [ -f "$ONBOARD_AUTOSTART" ]; then
  mv "$ONBOARD_AUTOSTART" "$ONBOARD_AUTOSTART.disabled"
  echo "  Disabled: onboard-autostart.desktop"
fi
pkill -x onboard 2>/dev/null || true
```

Renumber the remaining `[n/8]` step banners (the file currently mixes `[1/5]` with `[3/7]`).

**Verify (objective):**

```bash
ssh mmack@radio 'cd /tmp/kiosk-src && ./setup-kiosk.sh mmack'
ssh mmack@radio "stat -c '%a %n' ~/Desktop/*.desktop"                 # every line starts 755
ssh mmack@radio 'grep -c "password-store=basic" ~/Desktop/radio-console.desktop'  # 1 — drift closed
ssh mmack@radio 'test ! -f ~/.config/autostart/onboard-autostart.desktop && echo onboard-gone'
ssh mmack@radio 'for f in ~/Desktop/*.desktop; do desktop-file-validate "$f" && echo "OK $f"; done'
```

> Note the deploy scripts do not currently ship `deploy/debian-x64/kiosk/` to the box.
> `setup-kiosk.sh` is run from a checkout — `scp -r deploy/debian-x64/kiosk mmack@radio:/tmp/kiosk-src`
> then run it there. Record the exact invocation in the PR body so the next person does not guess.

---

## Task A5 — `Deploy-ToLinux.ps1`: stop leaving the screen dead

Two hunks. Both currently reference the **default** Chrome profile, which after Task A2 is no
longer the kiosk's profile — so leaving them alone would silently clear the wrong cache and leave
the kiosk's own `Singleton*` lock in place.

**Hunk 1 — the stop block (currently line ~145–155).** Replace the `ssh` line and correct the
comment, which after this change would otherwise describe paths the script no longer touches:

```powershell
  Write-Host "[2/4] Stopping services and kiosk browser..." -ForegroundColor Yellow
  # On stop: (a) wipe Chrome's HTTP disk cache so the relaunch can't serve a stale HTML/CSS
  # bundle that pre-dates the deploy — we hit that during the Radzen theme migration, where
  # Chrome served the old MudBlazor markup despite radio-web returning the new HTML. (b) remove
  # Chrome's Singleton lock files; the kill sends SIGTERM/SIGKILL without the orderly shutdown
  # that cleans those up, so on the next relaunch Chrome would see "another instance" and refuse
  # to start.
  #
  # Both paths live INSIDE the kiosk profile now. A profile started with --user-data-dir keeps
  # its cache at <profile>/Default/Cache, not in ~/.cache/google-chrome — that directory belongs
  # to the DEFAULT profile, which the kiosk no longer uses.
  #
  # radio-kiosk-exit matches on the kiosk profile path, so the Google Voice bridge Chrome
  # (~/.config/gv-bridge-chrome) is left running. Never widen this to `pkill -f chrome`.
  ssh $SshTarget "sudo systemctl stop radio-web 2>/dev/null; sudo systemctl stop radio-api 2>/dev/null; /usr/local/bin/radio-kiosk-exit 2>/dev/null; rm -rf ~/.config/radio-kiosk-chrome/Default/Cache ~/.config/radio-kiosk-chrome/Default/Code\ Cache 2>/dev/null; rm -f ~/.config/radio-kiosk-chrome/Singleton* 2>/dev/null; true"
```

**Hunk 2 — the relaunch block (currently line ~361–376).** Delete the `KNOWN DEFECT` paragraph
(it is fixed here) and the `DISPLAY=:0 nohup …` line:

```powershell
    # Relaunch kiosk browser.
    #
    # This used to be `DISPLAY=:0 nohup google-chrome …`, which assumed X11 on a box that runs
    # Wayland. The relaunch landed under XWayland with a flag set that did not match the boot
    # path, and in practice left the panel dead after every deploy. systemd-run --user starts
    # the browser from the graphical session's own service manager, so it inherits that
    # session's WAYLAND_DISPLAY / DBUS_SESSION_BUS_ADDRESS instead of an SSH shell's.
    #
    # The flag set itself is no longer duplicated here: radio-kiosk-launch owns it.
    Write-Host "  Relaunching kiosk browser..." -ForegroundColor DarkGray
    ssh $SshTarget "/usr/local/bin/radio-kiosk-launch"

    # Liveness, not process existence. During the 2026-08-02 outage Chrome was running and
    # radio-web returned 200 for 33 hours while the panel showed an auth dialog and made ZERO
    # connections to :5002. Established connections are the check that would have caught it.
    Write-Host "  Verifying the kiosk reached the UI..." -ForegroundColor DarkGray
    $kioskConns = 0
    for ($i = 0; $i -lt 10; $i++) {
      Start-Sleep -Seconds 2
      $kioskConns = [int](ssh $SshTarget "ss -tn state established | grep -c ':5002' || true").Trim()
      if ($kioskConns -ge 1) { break }
    }
    if ($kioskConns -ge 1) {
      Write-Host "  Kiosk is live ($kioskConns established connections to :5002)" -ForegroundColor Green
    } else {
      # Deliberately a warning, not exit 1: the binaries deployed and verified successfully.
      # What failed is the browser relaunch, and saying so loudly is the whole point — the old
      # code said nothing at all and the owner found a dead screen.
      Write-Host "  WARNING: 0 established connections to :5002 — the kiosk did not reach the UI." -ForegroundColor Red
      Write-Host "    Check: ssh $SshTarget 'systemctl --user status radio-kiosk.service'"
      Write-Host "    Retry: ssh $SshTarget '/usr/local/bin/radio-kiosk-launch'"
    }
```

**Verify (objective, end to end):**

```powershell
./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64
# expect the new green line: "Kiosk is live (N established connections to :5002)"
```
```bash
ssh mmack@radio 'pgrep -fa gv-bridge-chrome | head -1'   # the bridge SURVIVED the deploy
ssh mmack@radio 'curl -sf http://localhost:9223/json/version >/dev/null && echo cdp-ok'
```

---

## Task A6 — Drop `onboard` from provisioning

**Edit `deploy/provision/packages.sh`** — delete line 88 from `FEATURE_PKGS`:

```bash
FEATURE_PKGS=(
  songrec           # Shazam recognition (PPA)
  bluez-obexd       # PBAP contact sync (obex.service)
  zram-tools        # compressed swap (zramswap.service)
  google-chrome-stable
  unclutter         # hide idle cursor
  xdotool           # kiosk browser refresh helper
  rtl-sdr           # RTL-SDR tuner tools
  python3           # BT/audio research harness scripts
)
```

Add above the array:

```bash
# `onboard` was removed 2026-08-18. docs/uat/2026-08-03-osk-wayland-viability/REPORT.md measured
# Google Chrome 151 on Wayland issuing ZERO zwp_text_input_v3.enable() calls when a web-page
# input takes focus, so the OS keyboard cannot type into the kiosk at all. Text entry is the Web
# UI's own virtual keyboard. setup-kiosk.sh disables a leftover autostart entry on already-
# provisioned boxes; the package itself is left installed there rather than removed by script.
```

**Edit `deploy/provision/README.md` lines 192–194** — the open question is now answered:

```markdown
- ~~`onboard` on-screen keyboard~~ — **removed 2026-08-18.** The contradiction is resolved in
  favour of `setup-kiosk.sh`'s note: onboard cannot type into Chrome on Wayland
  (`docs/uat/2026-08-03-osk-wayland-viability/REPORT.md`). Dropped from `packages.sh`; the
  autostart entry is disabled by `setup-kiosk.sh`. Text entry is the Web UI's virtual keyboard.
```

**Verify:** `grep -c onboard deploy/provision/packages.sh` → `1` (the comment only).

---

## Task A7 — Correct the docs Part A invalidates

`CLAUDE.md` currently asserts things Part A makes untrue. Per this repo's `## Pre-Merge Review`
rule, a doc that survives the behaviour it described is worse than no doc.

1. **§ "Remote UI driving is currently unavailable"** — CDP on `:9223` is restored for the kiosk.
   Rewrite the first paragraph to say the kiosk now runs on a non-default `--user-data-dir`
   (`~/.config/radio-kiosk-chrome`) so `--remote-debugging-port=9223` is honoured, and that
   `radio-refresh-browser` remains broken because it drives `xdotool` under Wayland — a separate
   fix. **Do not claim `radio-refresh-browser` works; nothing in this plan touches it.**
2. **§ "Verifying a deploy actually landed"** — add that the deploy now verifies the kiosk itself
   by established connections to `:5002`, and that a `WARNING: 0 established connections` line
   means the binaries landed but the browser did not come back.
3. **§ Deployment / Services** — record `radio-kiosk.service` as a **transient user unit**
   (`systemctl --user status radio-kiosk`) so nobody hunts for a unit file that does not exist.

**Verify:** re-read each edited paragraph against the shipped code and confirm every claim is
one the diff actually makes true. Specifically confirm the `--password-store=basic` warning
elsewhere in `CLAUDE.md` still reads correctly — it now has a live example of the safe case.

---

# PART B — `KIOSK-2` · three icons, one repair-and-open action

Branch: `feat/kiosk-desktop-launcher`. **Depends on `KIOSK-1` being merged.**

## Task B1 — Ship the Anderson Console mark as the desktop icon

**New files under `deploy/debian-x64/kiosk/icons/`:**

| File | Source |
|---|---|
| `radio-console.svg` | copy of `branding/favicon.svg` (853 bytes, 3 colours, no gradients) |
| `radio-console-512.png` | copy of `branding/icon-512.png` |
| `radio-console-256.png` | render from the SVG |
| `radio-console-128.png` | render from the SVG |

Rendering (Task A0 reports which of these exists; do it on the box or on the dev machine):

```bash
rsvg-convert -w 256 -h 256 radio-console.svg -o radio-console-256.png
rsvg-convert -w 128 -h 128 radio-console.svg -o radio-console-128.png
# or: inkscape -w 256 -h 256 radio-console.svg -o radio-console-256.png
```

**Install** (add to `setup-kiosk.sh`, in the block added by Task A4):

```bash
# ---- 1d. Install the Radio Console icon ----
echo "[4/8] Installing icon assets..."
install -m 644 "$SCRIPT_DIR"/icons/radio-console*.svg "$SCRIPT_DIR"/icons/radio-console*.png "$ICON_DIR/"
echo "  Installed: $ICON_DIR/"
```

`~/.local/share/icons/` is user-owned and outside `/opt/radio-console/`, so it **survives
deploys** — `Deploy-ToLinux.ps1` wipes `api/` and `web/` only.

**Referenced by absolute path, deliberately** (§4.4): `Icon=audio-radio` was a *theme name*, it
silently resolved to nothing, and the owner got a document glyph with no error anywhere. On a
one-user appliance an absolute path cannot fail to resolve and cannot be invalidated by a stale
icon cache. The cost is that it hard-codes the user's home, which is why `setup-kiosk.sh`
substitutes `@ICON_DIR@` instead of the repo carrying a literal path.

**Verify (objective):**

```bash
ssh mmack@radio 'ls -l ~/.local/share/icons/radio-console/'
ssh mmack@radio 'grep "^Icon=" ~/Desktop/radio-console.desktop'          # absolute path
ssh mmack@radio 'test -f "$(grep "^Icon=" ~/Desktop/radio-console.desktop | cut -d= -f2)" && echo icon-resolves'
```

**Visual acceptance is the owner's, at the panel** (§4.5) — the three grille bars and two knobs
must not merge at the shipping size. Screen capture is unavailable; this is one of the two checks
in this plan that genuinely needs a human at the glass. If the mark does not hold up, **route back
to the Designer for a simplified variant rather than shipping something muddy** — do not
hand-edit the brand mark in this PR.

**Desktop icon size** (§2.2), guarded because Task A0 may report no DING schema:

```bash
if gsettings list-schemas | grep -q '^org.gnome.shell.extensions.ding$'; then
  gsettings set org.gnome.shell.extensions.ding icon-size 'large'
fi
```

If `large` overflows 720 px of height with the icon rows present, fall back to `'standard'`.
Also clear any hand-dragged positions so alphabetical auto-arrange applies (§10.1) — Task A0's
`gio info` output names the actual metadata key on this box; unset it on all three entries.
**If neither pinning nor clearing proves reliable, do nothing: the alphabetical order is what
the names already produce, and that is the safety property.**

---

## Task B2 — `radio-console-open`: probe, repair, then open

**New file:** `deploy/debian-x64/kiosk/bin/radio-console-open` → `/usr/local/bin/radio-console-open`, mode 755.

This is the largest single piece of the work. It is written with two **testability affordances**
that exist because the box cannot be screenshotted: `--print-status` emits the computed state of
all four components as machine-readable lines, and `--dialog-demo` renders any dialog variant on
demand. Every tier decision in §6.1 is therefore verifiable over SSH without seeing the screen.

### B2.1 — Header, guard, and configuration

```bash
#!/usr/bin/env bash
# radio-console-open — the "Radio Console" desktop icon: repair what's down, then open the kiosk.
#
# Design: docs/design-handoffs/HANDOFF-kiosk-desktop-launcher.md
#
# TWO RULES THAT INVERT THE WHOLE DESIGN IF GOT WRONG:
#  1. The kiosk browser is the SUBJECT of this action, not a reported component (§5.1). The
#     desktop is only visible when the kiosk is closed, so the kiosk is ALWAYS down when this
#     runs. If starting it counted as a repair, the dialog would fire on every single tap.
#  2. Google Voice auth is dead roughly 9 minutes in every 20 by design (§5.3). This script
#     checks LIVENESS, never auth. Reporting the routine blackout would fire the dialog on
#     ~45% of taps for a condition that fixes itself and that this script cannot act on.
#
# Not `set -e`: a failed probe is DATA, not a crash.
set -uo pipefail

# Absorb a second tap silently while a run is in flight (§5.6). With `unclutter -idle 3` and
# touch input there is no cursor feedback at all, so a second tap during a 1-3s cold start is
# likely, not hypothetical.
LOCKFILE="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}/radio-console-open.lock"
exec 9>"$LOCKFILE"
flock -n 9 || exit 0

KIOSK_LAUNCH=/usr/local/bin/radio-kiosk-launch
GTK_TOUCH_CONFIG=/usr/local/share/radio-console/gtk-touch
ROTARY_UNIT="@ROTARY_UNIT@"          # substituted by setup-kiosk.sh — see Task A0
GV_PROFILE="$HOME/.config/gv-bridge-chrome"

API_URL=http://localhost:5000/api/health/version
WEB_URL=http://localhost:5002/
PHONE_URL=http://localhost:5004/api/gvbridge/status

# The GV bridge ensure script is ROTARYPHONE-OWNED (D:\prj\RotaryPhone). This script CALLS it
# and reads its exit code. It must never write it, install a unit for it, or kill the bridge.
# Resolved from a candidate list because RotaryPhone is currently moving it under version
# control and the canonical path may change.
resolve_gv_ensure() {
  local c
  for c in "$HOME/bin/gv-bridge-ensure.sh" /usr/local/bin/gv-bridge-ensure.sh \
           /opt/rotary-phone/bin/gv-bridge-ensure.sh; do
    [ -x "$c" ] && { printf '%s\n' "$c"; return 0; }
  done
  return 1
}

declare -A STATE VALUE       # STATE: online|started|starting|failed|needsignin
LABELS=(AUDIO CONSOLE PHONE VOICE)   # dependency order = start order = display order (§5.1)
declare -A UNITNAME=( [AUDIO]="radio-api" [CONSOLE]="radio-web" \
                      [PHONE]="$ROTARY_UNIT" [VOICE]="Google Voice bridge" )
```

### B2.2 — Probes (§5.2: liveness, and evidence, not a probe field)

```bash
# `systemctl is-active` is not health. The repo's own lesson: a health field derived from a
# probe rather than from "did the last real call return data" reports healthy straight through
# an outage. Every check below includes a real request that returns real data.
probe_audio()   { systemctl is-active --quiet radio-api && curl -fsS --max-time 2 -o /dev/null "$API_URL"; }
probe_console() { systemctl is-active --quiet radio-web && curl -fsS --max-time 2 -o /dev/null "$WEB_URL"; }
probe_voice_process() { pgrep -f -- "--user-data-dir=$GV_PROFILE" >/dev/null 2>&1; }

# PHONE and VOICE share ONE request to :5004 so the parallel probe budget stays at 2s (§5.2b).
GV_BODY=""; GV_RC=1
fetch_gv_status() { GV_BODY="$(curl -fsS --max-time 2 "$PHONE_URL" 2>/dev/null)"; GV_RC=$?; }

# jq is not guaranteed present (Task A0), so parse with grep rather than depending on it.
gv_psidts_age() {
  printf '%s' "$GV_BODY" \
    | grep -oE '"psidtsAgeSeconds"[[:space:]]*:[[:space:]]*[0-9]+' \
    | grep -oE '[0-9]+$' | head -1
}

# available / degraded / cookiesValid on this endpoint ARE LIARS — captured reading
# {"available":true,"degraded":false,"cookiesValid":true} while both SMS endpoints returned
# hard 502s. psidtsAgeSeconds is the only honest field, and it is a live blackout clock:
#   <660 healthy · 660-1200 normal blackout trough · resets at ~1200.
# A value BEYOND 1200 means the refresh cycle itself never fired — that is the only reading
# that is a genuine dead session, and it is what makes a single stateless probe sufficient.
classify_voice() {
  probe_voice_process || { echo offline; return; }
  local age; age="$(gv_psidts_age)"
  [ -z "$age" ] && { echo needsignin; return; }   # field absent/null -> refresh cycle stopped
  [ "$age" -gt 1200 ] && { echo needsignin; return; }
  echo online                                     # <660 AND 660-1200 are BOTH silent
}

run_probes() {
  local tmp; tmp="$(mktemp -d)"
  ( probe_audio   && echo online > "$tmp/AUDIO"   || echo down > "$tmp/AUDIO" ) &
  ( probe_console && echo online > "$tmp/CONSOLE" || echo down > "$tmp/CONSOLE" ) &
  ( fetch_gv_status; printf '%s' "$GV_BODY" > "$tmp/body"; echo "$GV_RC" > "$tmp/rc" ) &
  wait
  STATE[AUDIO]="$(cat "$tmp/AUDIO")"
  STATE[CONSOLE]="$(cat "$tmp/CONSOLE")"
  GV_BODY="$(cat "$tmp/body")"; GV_RC="$(cat "$tmp/rc")"
  if [ "$GV_RC" = 0 ]; then STATE[PHONE]=online; else STATE[PHONE]=down; fi
  # An unreachable :5004 is the PHONE row's problem, not VOICE's (§5.3, last table row).
  if [ "${STATE[PHONE]}" = online ]; then STATE[VOICE]="$(classify_voice)"
  elif probe_voice_process; then STATE[VOICE]=online
  else STATE[VOICE]=offline; fi
  rm -rf "$tmp"
}
```

### B2.3 — Repair, with per-component budgets (§5.5)

```bash
probe_phone()   { curl -fsS --max-time 2 -o /dev/null "$PHONE_URL"; }

start_audio()   { sudo systemctl start radio-api; }
start_console() { sudo systemctl start radio-web; }
start_phone()   { sudo systemctl start "$ROTARY_UNIT"; }
start_voice()   { local s; s="$(resolve_gv_ensure)" || return 1; "$s"; }

wait_for() {   # $1 = probe fn, $2 = budget seconds
  local deadline=$(( SECONDS + $2 ))
  while [ "$SECONDS" -lt "$deadline" ]; do "$1" && return 0; sleep 1; done
  return 1
}

RUN_DEADLINE=$(( SECONDS + 60 ))   # whole-run ceiling (§5.5): stop starting, report what's true

repair() {   # $1 = component
  [ "$SECONDS" -ge "$RUN_DEADLINE" ] && { STATE[$1]=starting; return; }
  case "$1" in
    AUDIO)   progress_text "Starting the audio engine…"
             start_audio   && { wait_for probe_audio   20 && STATE[AUDIO]=started   || STATE[AUDIO]=starting; }   || STATE[AUDIO]=failed ;;
    CONSOLE) progress_text "Starting the console…"
             start_console && { wait_for probe_console 15 && STATE[CONSOLE]=started || STATE[CONSOLE]=starting; } || STATE[CONSOLE]=failed ;;
    PHONE)   progress_text "Starting the phone service…"
             start_phone   && { wait_for probe_phone   15 && STATE[PHONE]=started   || STATE[PHONE]=starting; }   || STATE[PHONE]=failed ;;
    VOICE)   progress_text "Starting the Google Voice link…"
             start_voice   && { wait_for probe_voice_process 20 && STATE[VOICE]=started || STATE[VOICE]=starting; } || STATE[VOICE]=failed ;;
  esac
}
```

> **Amber, never red, for a component that started but has not answered inside its budget**
> (§5.5). Red is reserved for *the start command itself failed*. This is the corpus's two-tier
> rule and it is the difference between a dialog that is worth reading and one that gets
> dismissed unread.

### B2.4 — Dialogs

```bash
zen() { XDG_CONFIG_HOME="$GTK_TOUCH_CONFIG" GTK_THEME=Yaru-dark zenity "$@"; }

GREEN='#4ADE80'; AMBER='#F0A830'; RED='#F87171'; LOW='#4B5563'

pill_for() {   # echoes "<colour>|<pill>|<value suffix>"
  case "$1" in
    online)     echo "$GREEN|Online|" ;;
    started)    echo "$GREEN|Online| · started just now" ;;
    starting)   echo "$AMBER|Starting| · not answering yet" ;;
    failed|offline) echo "$RED|Offline| · didn't start" ;;
    needsignin) echo "$AMBER|Needs sign-in| · session expired" ;;
  esac
}

esc() { printf '%s' "$1" | sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g'; }

status_block() {   # the [label][value][pill] row grammar (§6.3), monospaced so columns align
  local c colour pill suffix out=""
  for c in "${LABELS[@]}"; do
    IFS='|' read -r colour pill suffix <<< "$(pill_for "${STATE[$c]}")"
    out+="$(printf '<tt>%-9s <span foreground="%s">%-34s</span> <span foreground="%s">[ %s ]</span></tt>\n' \
      "$c" "$LOW" "$(esc "${UNITNAME[$c]}")$suffix" "$colour" "$pill")"
  done
  printf '%s' "$out"
}

headline() { printf '<span size="x-large" weight="bold">%s</span>' "$(esc "$1")"; }
subline()  { printf '<span foreground="%s">%s</span>' "$LOW" "$(esc "$1")"; }
```

Progress dialog (§6.2) — repair path only, never the healthy path:

```bash
PROGRESS_FIFO=""; PROGRESS_PID=""
progress_open() {
  PROGRESS_FIFO="$(mktemp -u)"; mkfifo "$PROGRESS_FIFO"
  zen --progress --pulsate --auto-close --no-cancel --width=760 \
      --title="Radio Console" --text="Starting…" < "$PROGRESS_FIFO" &
  PROGRESS_PID=$!
  exec 8>"$PROGRESS_FIFO"
}
progress_text()  { [ -n "$PROGRESS_PID" ] && printf '#%s\n' "$1" >&8; }
progress_close() { [ -n "$PROGRESS_PID" ] || return 0
                   exec 8>&-; wait "$PROGRESS_PID" 2>/dev/null; rm -f "$PROGRESS_FIFO"; PROGRESS_PID=""; }
```

Report dialog dispatch — zenity's three stock types mapped onto the corpus's existing severity
tiers; **no new severity vocabulary is invented** (§6.1):

```bash
report_dialog() {
  local text; text="$(compose_report)"
  if any_state failed offline; then                     # hard failure -> red WITH an action
    zen --question --icon-name=dialog-error --width=760 --title="Radio Console" \
        --ok-label="Try again" --cancel-label="Open anyway" --text="$text"
    return $?                                           # 0 = retry, 1 = open anyway
  elif any_state needsignin; then                       # degraded -> calm amber, NOT red
    if [ "$GV_RAISE_SUPPORTED" = 1 ]; then
      zen --question --icon-name=dialog-warning --width=760 --title="Radio Console" \
          --ok-label="Show the sign-in" --cancel-label="Not now" --text="$text" && raise_gv_bridge
    else
      # §10.2: DO NOT SHIP A BUTTON THAT DOES NOTHING. If the bridge window cannot be raised
      # under Wayland, the copy stands on its own and the dialog gets one dismiss button.
      zen --warning --width=760 --title="Radio Console" --ok-label="OK" --text="$text"
    fi
  elif any_state starting; then
    zen --warning --width=760 --title="Radio Console" --ok-label="Continue" --text="$text"
  elif any_state started; then                          # informational success
    zen --info --timeout=10 --width=760 --title="Radio Console" --ok-label="OK" --text="$text"
  fi
  return 1
}
```

**Copy is verbatim from §6.4 / §6.5 / §6.6 — do not paraphrase.** House voice: plain, calm,
sentence case, no exclamation marks, never blame the user, one sentence, single `…` never three
dots. Per-component failure strings must stay distinct; the corpus is explicit that they must
not be collapsed into one generic string.

| Case | Headline | Sub-line |
|---|---|---|
| one started (audio) | `The audio engine wasn't running — it's started now.` | — |
| one started (console) | `The console wasn't running — it's started now.` | — |
| one started (phone) | `The phone service wasn't running — it's started now.` | — |
| one started (voice) | `The Google Voice link wasn't running — it's started now.` | — |
| two or more started | `Two services weren't running — they're started now.` | — |
| something slow | `The audio engine is still starting — give it a minute.` | `If sound is still missing in a minute, open the console again.` |
| GV dead session | `Google Voice needs you to sign in again.` | `You'll need a keyboard — the on-screen one can't type into Google's page.` |
| `radio-api` failed | `The audio engine didn't start.` | `The console will open, but there'll be no sound.` |
| `radio-web` failed | `The console didn't start.` | `The screen will show an error page instead.` |
| `rotary-phone` failed | `The phone service didn't start.` | `Calls and texts won't reach the screen.` |
| GV bridge failed | `The Google Voice link didn't start.` | `Voicemail and texts won't load. Calls still work.` |
| kiosk Chrome failed | `The console screen didn't open.` | `Try again. If it keeps failing, shut down and power back on.` |
| two or more failed | `Two services didn't start.` | `Some things won't work until they're running.` |

Count words are spelled (`Two services…`); use a small number-word map for 2–4 and fall back to
`Some services…` beyond that.

**No raw exit codes, `systemctl` output or curl errors in any dialog.** The unit name in the dim
value column is an identifier, not a diagnostic dump, and it is the only place jargon is allowed.

### B2.4b — The helpers the dispatch above calls

Written out so nothing in this plan is a name without a body.

```bash
any_state() {   # any_state failed offline  -> true if ANY component is in one of the given states
  local c want
  for c in "${LABELS[@]}"; do
    for want in "$@"; do [ "${STATE[$c]}" = "$want" ] && return 0; done
  done
  return 1
}

compute_tier() {
  if   any_state failed offline; then echo error
  elif any_state needsignin;     then echo warning
  elif any_state starting;       then echo warning
  elif any_state started;        then echo info
  else echo silent; fi
}

count_in_state() { local c n=0; for c in "${LABELS[@]}"; do
  for w in "$@"; do [ "${STATE[$c]}" = "$w" ] && n=$((n+1)); done; done; echo "$n"; }

numword() { case "$1" in 2) echo Two;; 3) echo Three;; 4) echo Four;; *) echo Some;; esac; }

# Friendly names, used in headlines only. The technical unit names stay in the dim value column.
friendly() { case "$1" in
  AUDIO) echo "The audio engine";; CONSOLE) echo "The console";;
  PHONE) echo "The phone service";; VOICE) echo "The Google Voice link";; esac; }

compose_report() {
  local n c head sub=""
  if any_state failed offline; then
    n="$(count_in_state failed offline)"
    if [ "$n" = 1 ]; then
      for c in "${LABELS[@]}"; do case "${STATE[$c]}" in failed|offline)
        head="$(friendly "$c") didn't start."
        case "$c" in
          AUDIO)   sub="The console will open, but there'll be no sound.";;
          CONSOLE) sub="The screen will show an error page instead.";;
          PHONE)   sub="Calls and texts won't reach the screen.";;
          VOICE)   sub="Voicemail and texts won't load. Calls still work.";;
        esac ;; esac; done
    else
      head="$(numword "$n") services didn't start."
      sub="Some things won't work until they're running."
    fi
  elif any_state needsignin; then
    head="Google Voice needs you to sign in again."
    sub="You'll need a keyboard — the on-screen one can't type into Google's page."
  elif any_state starting; then
    for c in "${LABELS[@]}"; do [ "${STATE[$c]}" = starting ] && \
      head="$(friendly "$c") is still starting — give it a minute."; done
    sub="If sound is still missing in a minute, open the console again."
  else
    n="$(count_in_state started)"
    if [ "$n" = 1 ]; then
      for c in "${LABELS[@]}"; do [ "${STATE[$c]}" = started ] && \
        head="$(friendly "$c") wasn't running — it's started now."; done
    else
      head="$(numword "$n") services weren't running — they're started now."
    fi
  fi
  printf '%s\n\n%s\n' "$(headline "$head")" "$(status_block)"
  [ -n "$sub" ] && printf '\n%s\n' "$(subline "$sub")"
}

# The kiosk is never a reported row (§5.1), but its FAILURE is still worth saying (§6.6).
kiosk_failed() {
  zen --error --width=760 --title="Radio Console" --text="$(headline "The console screen didn't open.")

$(subline "Try again. If it keeps failing, shut down and power back on.")"
}

# Set to 1 only if the B2.7 measurement shows the bridge window can actually be raised.
# Left at 0 means the "Show the sign-in" button is not offered at all — §10.2: do not ship a
# button that does nothing.
GV_RAISE_SUPPORTED=0
raise_gv_bridge() { google-chrome --user-data-dir="$GV_PROFILE" https://voice.google.com & }

# Canned states for one-shot visual sign-off on a box that cannot be screenshotted.
load_demo_states() {
  STATE=( [AUDIO]=online [CONSOLE]=online [PHONE]=online [VOICE]=online )
  case "$1" in
    A)  STATE[CONSOLE]=started; STATE[PHONE]=started ;;
    B1) STATE[AUDIO]=starting ;;
    B2) STATE[VOICE]=needsignin ;;
    C)  STATE[AUDIO]=failed ;;
  esac
}
```

### B2.5 — Sequence and the two ordering rules that are easy to get backwards

```bash
main() {
  run_probes
  local needs_repair=0 c
  for c in "${LABELS[@]}"; do [ "${STATE[$c]}" != online ] && needs_repair=1; done

  if [ "$needs_repair" = 0 ]; then
    "$KIOSK_LAUNCH" || kiosk_failed        # Variant D: no dialog, no toast, no sound (§6.7)
    exit 0
  fi

  progress_open
  for c in "${LABELS[@]}"; do [ "${STATE[$c]}" != online ] && repair "$c"; done
  progress_close

  if any_state failed offline; then
    # ORDERING RULE 2: on the error path the kiosk is HELD BACK. This is the only place the
    # design deliberately adds friction — the error dialog carries the only explanation plus
    # the retry, and a fullscreen kiosk racing it risks burying it.
    if report_dialog; then exec "$0"; else "$KIOSK_LAUNCH" || kiosk_failed; fi
  else
    # ORDERING RULE 1: on the success path the kiosk launches BEFORE the dialog.
    # --window-position is a no-op under Wayland and stacking is decided by launch order, so a
    # dialog raised first would be swallowed by the fullscreen kiosk and the owner sees nothing.
    "$KIOSK_LAUNCH" || kiosk_failed
    sleep "${RADIO_DIALOG_DELAY:-2}"       # §10.4 — verify on the box; raise if it gets buried
    report_dialog || true
  fi
}
```

`exec "$0"` on `Try again` re-runs the whole action; the `flock` fd is closed by `exec`'s
replacement of the process image — **verify this explicitly** (Test T14), because a retained
lock would make the retry a silent no-op, which is the worst possible failure for a retry button.

### B2.6 — Test affordances (these exist because the box cannot be screenshotted)

```bash
case "${1:-}" in
  --print-status)
    run_probes
    for c in "${LABELS[@]}"; do printf '%s=%s\n' "$c" "${STATE[$c]}"; done
    printf 'PSIDTS=%s\n' "$(gv_psidts_age)"
    printf 'TIER=%s\n' "$(compute_tier)"     # error | warning | info | silent
    exit 0 ;;
  --dialog-demo)                              # --dialog-demo A|B1|B2|C
    load_demo_states "${2:-A}"; report_dialog; exit 0 ;;
  --dry-run)                                  # probe + print what WOULD be repaired, start nothing
    run_probes; for c in "${LABELS[@]}"; do
      [ "${STATE[$c]}" != online ] && echo "WOULD-START $c (${UNITNAME[$c]})"; done
    exit 0 ;;
esac
main "$@"
```

### B2.7 — Single-instance / raise under Wayland (§5.6, §10.2)

`--window-position` is a no-op and `GetWindows` is `AccessDenied`, so "raise the existing window"
has no clean API. **Two candidate mechanisms, one measurement, and a documented fallback.**

```bash
# Candidate 1 — let Chrome do it. A second `google-chrome --user-data-dir=<same profile> <url>`
# is forwarded to the running instance, which requests activation. Under GNOME 46 the launching
# .desktop supplies XDG_ACTIVATION_TOKEN, which is what a Wayland compositor needs to honour it.
raise_kiosk_via_chrome() {
  google-chrome --user-data-dir="$($KIOSK_LAUNCH --print-profile)" http://localhost:5002 &
}
# Candidate 2 — decline. `radio-kiosk-launch` already exits 0 without spawning a second Chrome
# when one is running, which satisfies the design requirement even if raising proves impossible.
```

**Selection criterion, measured on the box:** with the kiosk running and the desktop showing,
run candidate 1 and check (a) does the kiosk come forward, and (b) does the kiosk end up with an
extra tab (`curl -s localhost:9223/json | grep -c '"type": "page"'` before and after). Ship
candidate 1 **only if it raises and the page count is unchanged.** Otherwise ship candidate 2.
Record the measurement in the PR body either way.

The same question decides `Show the sign-in` in §6.5 — the bridge Chrome is on a different
profile with CDP on `:9224`. Set `GV_RAISE_SUPPORTED=1` only if the equivalent measurement
passes; otherwise the button is dropped and the copy stands alone. **Do not ship a button that
does nothing.**

---

## Task B3 — 56 px GTK buttons, or say so

GTK's ~34 px default button is well under `--touch-min` (48 px), and the spec treats the touch
floor as non-negotiable. There is no zenity flag for it; the route is a GTK CSS override scoped
to this process alone via `XDG_CONFIG_HOME`.

**New file:** `deploy/debian-x64/kiosk/gtk-touch/gtk-3.0/gtk.css`
(→ `gtk-4.0/gtk.css` instead if Task A0's `ldd` shows zenity links libgtk-4)

```css
/* Touch sizing for the Radio Console zenity dialogs only.
 * Scoped by pointing XDG_CONFIG_HOME at /usr/local/share/radio-console/gtk-touch for the
 * zenity process, so no global GTK setting is touched and no other app on the box is affected.
 * 56px = --touch-preferred; 24px between buttons stops a fat finger hitting "Open anyway"
 * when aiming at "Try again". */
button {
  min-height: 56px;
  min-width: 200px;
  padding-left: 24px;
  padding-right: 24px;
}
.dialog-action-box button,
.dialog-vbox button {
  margin-left: 12px;
  margin-right: 12px;
}
```

**Install** (`setup-kiosk.sh`):

```bash
echo "[5/8] Installing touch GTK overrides..."
sudo install -d -m 755 "$GTK_DIR/gtk-3.0"
sudo install -m 644 "$SCRIPT_DIR/gtk-touch/gtk-3.0/gtk.css" "$GTK_DIR/gtk-3.0/gtk.css"
```

**Verify.** The honest position: button *height in pixels* cannot be measured on this box —
`xdotool` is X11, `GetWindows` is `AccessDenied`, and no screenshot tool is present. What can be
verified objectively is that the CSS is found and parsed:

```bash
ssh mmack@radio 'XDG_CONFIG_HOME=/usr/local/share/radio-console/gtk-touch GTK_DEBUG=css \
  timeout 5 zenity --info --timeout=2 --text=probe 2>&1 | grep -iE "gtk.css|parsing|error" | head'
```
A parse error prints here; silence plus a rendered dialog means the file was accepted. The size
itself is the owner's fingertip, via `radio-console-open --dialog-demo C`.

**If the override cannot be made to work, say so in the PR rather than shipping 34 px buttons.**
The documented fallback is a wider dialog with fewer, larger buttons (§10.3).

---

## Task B4 — The three desktop entries

**`deploy/debian-x64/kiosk/radio-console.desktop`** (centre, primary):

```ini
[Desktop Entry]
Name=Radio Console
Comment=Open the Radio Console. Starts anything that isn't running.
Exec=/usr/local/bin/radio-console-open
Icon=@ICON_DIR@/radio-console.svg
Terminal=false
Type=Application
Categories=AudioVideo;Audio;
StartupNotify=true
```

**`deploy/debian-x64/kiosk/radio-exit-browser.desktop`** (left) — **filename unchanged**, only
`Name`/`Comment`/`Exec` change, so GNOME keeps any existing desktop metadata for it:

```ini
[Desktop Entry]
Name=Exit to Desktop
Comment=Close the Radio Console screen. Leaves everything else running.
Exec=/usr/local/bin/radio-kiosk-exit
Icon=application-exit
Terminal=false
Type=Application
Categories=Utility;
```

**`deploy/debian-x64/kiosk/radio-shutdown.desktop`** (right):

```ini
[Desktop Entry]
Name=Shutdown System
Comment=Shut down the Radio Console system.
Exec=/usr/local/bin/radio-shutdown-confirm
Icon=system-shutdown
Terminal=false
Type=Application
Categories=System;
```

Also switch `radio-kiosk-autostart.desktop`'s `Icon=audio-radio` to `Icon=@ICON_DIR@/radio-console.svg`
now that Task B1 ships the asset.

> **The alphabetical ordering is the accidental-shutdown mitigation, not cosmetics.** DING
> auto-arranges alphabetically: `Exit to Desktop` · `Radio Console` · `Shutdown System`. That puts
> the safe, most-tapped action physically **between** the two "leaving" actions, so Exit and
> Shutdown are never neighbours and a mis-aimed fingertip lands on something harmless. **Any
> future rename must preserve `E… < R… < S…`.** Write this as a comment in `setup-kiosk.sh` next
> to the install loop, where a future renamer will actually see it.

**Delete the GV Bridge entry** — from `~/Desktop` **and** `~/.local/share/applications` (§2.3).
Its function moves inside Radio Console; no replacement, no hidden entry. Add to `setup-kiosk.sh`'s
cleanup block:

```bash
# The GV Bridge entry pointed at `systemctl --user start gv-bridge-chrome`, an ABANDONED
# snap-Chromium unit (different browser, different profile, different extension) rather than the
# live path. It was also mode 775, which GNOME refuses to launch. Its job now lives inside
# radio-console-open, which calls the canonical ensure script. Canonical is
# ~/bin/gv-bridge-ensure.sh (google-chrome + ~/.config/gv-bridge-chrome +
# /opt/rotary-phone/ChromeExtension) — this also closes the ambiguity flagged in
# design/plans/IAC-PRISTINE-INSTALL-AUDIT.md §7.
for stale in "$DESKTOP_DIR/GV-Bridge.desktop" "$APPS_DIR/GV-Bridge.desktop" \
             "$DESKTOP_DIR/gv-bridge.desktop" "$APPS_DIR/gv-bridge.desktop"; do
  [ -f "$stale" ] && { rm -f "$stale"; echo "  Removed: $stale"; }
done
```

Task A0's `stat` output names the exact filename on the box; use it rather than the guesses above
if it differs.

**Verify:** `ssh mmack@radio 'ls ~/Desktop'` → exactly three `.desktop` files, all mode 755;
`grep '^Name=' ~/Desktop/*.desktop` sorts as `Exit to Desktop` / `Radio Console` / `Shutdown System`.

---

## Task B5 — `radio-kiosk-exit`: kill the kiosk, spare the bridge

**New file:** `deploy/debian-x64/kiosk/bin/radio-kiosk-exit` → `/usr/local/bin/radio-kiosk-exit`, mode 755.
(Part A already installs this file — it is referenced by `Deploy-ToLinux.ps1` Task A5. **Ship it
in Part A**, and Part B only wires the desktop entry to it. Listed here so the behaviour is
specified in one place.)

```bash
#!/usr/bin/env bash
# radio-kiosk-exit — close the Radio Console kiosk screen. Leaves everything else running.
#
# This replaced `pkill -f chrome`, which also killed the Google Voice bridge Chrome — and
# nothing restarts that on demand (its watchdog timer runs on a 2-minute cadence, so voicemail
# and texts went dark for up to two minutes every time the owner tapped Exit).
#
# The two Chromes are distinguished by profile: the bridge runs
# --user-data-dir=~/.config/gv-bridge-chrome, the kiosk runs --user-data-dir=~/.config/radio-kiosk-chrome.
# NEVER widen this match to `chrome` or `--kiosk`.
set -uo pipefail

KIOSK_PROFILE="$(/usr/local/bin/radio-kiosk-launch --print-profile)"

systemctl --user stop radio-kiosk.service 2>/dev/null
pkill -f -- "--user-data-dir=$KIOSK_PROFILE" 2>/dev/null

# Post-exit there is no dialog, no toast, no confirmation. The desktop appearing is the feedback.
exit 0
```

**No confirmation dialog, and the reason is asymmetry, not laziness** (§7.1): an accidental Exit
costs ~3 seconds and self-repairs via the neighbouring icon; an accidental Shutdown costs a full
boot and a physical power button. The risk is addressed by layout (§2.1) instead.

**Verify (objective — this is the whole point of the change):**

```bash
ssh mmack@radio '/usr/local/bin/radio-kiosk-launch; sleep 6; pgrep -c -f gv-bridge-chrome'  # note N
ssh mmack@radio '/usr/local/bin/radio-kiosk-exit; sleep 2; \
  echo "kiosk: $(pgrep -c -f radio-kiosk-chrome || echo 0)  bridge: $(pgrep -c -f gv-bridge-chrome || echo 0)"'
# expect kiosk: 0   bridge: N  (unchanged)
```

---

## Task B6 — `radio-shutdown-confirm` (owner Q2: approved)

**New file:** `deploy/debian-x64/kiosk/bin/radio-shutdown-confirm` → `/usr/local/bin/radio-shutdown-confirm`, mode 755.

```bash
#!/usr/bin/env bash
# radio-shutdown-confirm — replaces `gnome-session-quit --power-off`.
#
# GNOME's own confirmation carries a 60-SECOND COUNTDOWN AND THEN POWERS OFF BY ITSELF. On a wall
# panel that means an accidental tap, followed by the owner walking away, powers the box off
# anyway. That is not a confirm; it is a delay. A confirm that proceeds on inaction is the wrong
# shape for a touchscreen, so this one waits indefinitely for an explicit tap.
set -uo pipefail
GTK_TOUCH_CONFIG=/usr/local/share/radio-console/gtk-touch

if XDG_CONFIG_HOME="$GTK_TOUCH_CONFIG" GTK_THEME=Yaru-dark zenity \
     --question --icon-name=dialog-warning --width=760 --title="Radio Console" \
     --ok-label="Shut down" --cancel-label="Cancel" --default-cancel \
     --text='<span size="x-large" weight="bold">Shut down the radio?</span>

<span foreground="#4B5563">Music stops and the screen goes dark. You'"'"'ll need the power button to start it again.</span>'
then
  systemctl poweroff
fi
exit 0
```

**No `--timeout` on this dialog, deliberately** — a timeout would reintroduce exactly the
auto-proceeding behaviour it replaces. `Cancel` is focused by default.

**Verify:** `--default-cancel` support comes from Task A0. Objective check that nothing powers off
on dismissal: run it, tap `Cancel`, then `ssh mmack@radio 'uptime -s'` — the boot time must be
unchanged. Test the affirmative path **last**, once everything else has passed.

---

## Task B7 — Wire the installer to the new artifacts

`setup-kiosk.sh` gains: icon install (B1), GTK override install (B3), and `radio-console-open` +
`radio-shutdown-confirm` added to the `bin/` install loop from Task A4:

```bash
for s in radio-kiosk-launch radio-kiosk-exit radio-console-open radio-shutdown-confirm; do
  sudo install -m 755 "$SCRIPT_DIR/bin/$s" "$BIN_DIR/$s"
  echo "  Installed: $BIN_DIR/$s"
done
```

`radio-console-open` carries the `@ROTARY_UNIT@` placeholder, so it must go through the same
substitution the desktop entries get rather than a plain `install`:

```bash
sed -e "s|@ROTARY_UNIT@|$ROTARY_UNIT|g" "$SCRIPT_DIR/bin/radio-console-open" > /tmp/rco.$$
sudo install -m 755 /tmp/rco.$$ "$BIN_DIR/radio-console-open"; rm -f /tmp/rco.$$
```

`ROTARY_UNIT` is already derived in Task A4 — **do not add a second derivation.** The GV Bridge
entry cleanup is specified in Task B4 and lands in the same cleanup block Task A4 created.

**Verify:** `ssh mmack@radio 'grep -c "@" ~/Desktop/*.desktop /usr/local/bin/radio-console-open'`
→ **zero unsubstituted `@…@` placeholders** anywhere on the box.

---

## Task B8 — Docs for Part B

- `deploy/provision/README.md` — the desktop entries are now installed by `setup-kiosk.sh` from
  the repo; `~/Desktop` is no longer hand-maintained. State the exact re-run invocation.
- `design/INTEGRATIONS.md` — record that the Radio Console launcher is a **second consumer** of
  `gv-bridge-ensure.sh` (invoke-and-probe only; RotaryPhone owns the script), and that
  `psidtsAgeSeconds > 1200` is the launcher's dead-session test while `660–1200` is a routine
  trough that is deliberately never reported.
- `docs/uat/2026-08-18-kiosk-launcher/REPORT.md` — the UAT record for the Test Plan below.
- `CLAUDE.md` — one line under the kiosk section: the desktop entries and helper scripts live in
  `deploy/debian-x64/kiosk/` and are installed by `setup-kiosk.sh`; **do not hand-edit `~/Desktop`.**

---

# Test Plan

Screen capture is largely unavailable on this box (Shell screenshot API and `GetWindows` are
`AccessDenied` on GNOME 46; `gnome-screenshot` / `grim` / `scrot` are absent). Every test below is
an **objective probe** except T18 and T19, which are explicitly the owner's eyes and are marked
as such. Where a visual check is unavoidable, `--dialog-demo` puts the exact dialog on the glass
on demand so the owner can sign off in one pass rather than by reproducing failure conditions.

Record every result in `docs/uat/2026-08-18-kiosk-launcher/REPORT.md`.

### Part A

| # | What | Command | Pass |
|---|---|---|---|
| T1 | Kiosk launches under Wayland | `ssh mmack@radio '/usr/local/bin/radio-kiosk-launch'; sleep 8; ssh mmack@radio "ss -tn state established \| grep -c ':5002' \|\| true"` | ≥ 1 |
| T2 | It is a systemd user unit, not a detached shell | `ssh mmack@radio 'systemctl --user is-active radio-kiosk.service'` | `active` |
| T3 | It really is Wayland, not XWayland | `ssh mmack@radio 'systemctl --user show-environment \| grep WAYLAND_DISPLAY'` and `pgrep -fa radio-kiosk-chrome \| grep -c ozone-platform=wayland` | both non-empty |
| T4 | CDP is restored by the dedicated profile | `ssh mmack@radio 'curl -sf http://localhost:9223/json/version'` | JSON with `Browser:` |
| T5 | Second launch does not spawn a second Chrome | run `radio-kiosk-launch` twice; `pgrep -c -f radio-kiosk-chrome` before/after | count unchanged |
| T6 | Deploy leaves the screen alive | `./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64` | green `Kiosk is live (N …)` line |
| T7 | Deploy does not kill the GV bridge | `pgrep -c -f gv-bridge-chrome` before and after T6 | unchanged, non-zero |
| T8 | Deploy clears the *kiosk* profile's cache, not the default one | `ssh mmack@radio 'ls ~/.config/radio-kiosk-chrome/Singleton* 2>/dev/null \| wc -l'` after T6's stop phase | `0` |
| T9 | Every desktop entry is mode 755 | `ssh mmack@radio "stat -c '%a %n' ~/Desktop/*.desktop"` | every line starts `755` |
| T10 | Repo→box drift is closed | `ssh mmack@radio 'grep -c password-store=basic ~/Desktop/radio-console.desktop'` | `1` |
| T11 | `onboard` is gone from provisioning and the session | `grep -c onboard deploy/provision/packages.sh` (→ 1, the comment) and `ssh mmack@radio 'test ! -f ~/.config/autostart/onboard-autostart.desktop && echo ok'` | both pass |
| T12 | Re-running the installer is idempotent | run `setup-kiosk.sh` twice; `stat`/`ls ~/Desktop` after each | identical output, no errors |

### Part B — the silent path (the common case, and the one most easily broken)

| # | What | Command | Pass |
|---|---|---|---|
| T13 | Everything healthy → **no dialog at all** | `ssh mmack@radio '/usr/local/bin/radio-console-open & sleep 12; pgrep -c zenity \|\| echo 0'` | `0` — and `:5002` connections ≥ 1 |
| T14 | Double-tap is absorbed | fire `radio-console-open` twice 200 ms apart; `pgrep -c -f radio-kiosk-chrome` | one kiosk, no second dialog |
| T15 | GV blackout trough is **never** reported | poll `--print-status` every 60 s for 25 min, logging `PSIDTS=` and `VOICE=` | every sample with `660 ≤ PSIDTS ≤ 1200` shows `VOICE=online` and `TIER=silent` |
| T16 | A dead GV session **is** reported | `PSIDTS` observed `> 1200`, or force it by stopping the bridge Chrome | `VOICE=needsignin`, `TIER=warning` |
| T17 | Bridge down → repairable | `pkill -f gv-bridge-chrome`; `radio-console-open --dry-run` | prints `WOULD-START VOICE`; the ensure script is *called*, never edited |

### Part B — the repair and failure paths

| # | What | Command | Pass |
|---|---|---|---|
| T18 | Variant A (info) | `sudo systemctl stop radio-web`; run the launcher | progress dialog appears, `radio-web` active afterwards, dialog auto-closes ≈10 s, kiosk already up behind it. **Owner's eyes for the 10 s dismissal and the four rows.** |
| T19 | Variant C (error) | `radio-console-open --dialog-demo C` | red icon, two buttons, `Try again` retries and `Open anyway` launches. **Owner's eyes for the 56 px buttons and 24 px gap.** |
| T20 | Retry is not a silent no-op | tap `Try again` in T19 | the run repeats — confirms `exec "$0"` released the `flock` fd |
| T21 | Hard failure holds the kiosk back | make `radio-api` fail to start (e.g. a deliberately bad `ExecStart` override), run the launcher | dialog is visible, kiosk **not** launched until a button is tapped |
| T22 | Dialog is not buried on the success path | T18, with `RADIO_DIALOG_DELAY` at 0 then 2 | if 0 buries it, ship 2 and record it; if it can be buried at any delay, invert to dialog-first per §10.4 |
| T23 | Exit spares the bridge | `radio-kiosk-exit`; count both Chromes | kiosk `0`, bridge unchanged |
| T24 | Shutdown confirm never auto-proceeds | run `radio-shutdown-confirm`, leave it untouched 3 min, then Cancel; `uptime -s` | boot time unchanged |
| T25 | Icon resolves (no generic document glyph) | `test -f "$(grep '^Icon=' ~/Desktop/radio-console.desktop \| cut -d= -f2)"` | pass; **owner confirms the mark renders at the panel** |
| T26 | Alphabetical order holds | `grep -h '^Name=' ~/Desktop/*.desktop \| sort` | `Exit to Desktop` < `Radio Console` < `Shutdown System` |
| T27 | No unsubstituted placeholders | `grep -rn '@[A-Z_]*@' ~/Desktop/*.desktop /usr/local/bin/radio-console-*` | no matches |
| T28 | Entries validate | `for f in ~/Desktop/*.desktop; do desktop-file-validate "$f"; done` | silent |

**Two constraints on how the UAT itself is run**, both learned the hard way here:

- **Heavy `journalctl` reads correlate with audio distortion** on this N100. Bound every query
  (`--since '-10min'`) and never tail while audio is playing.
- **T15 must record wall-clock time against the 20-minute GV blackout cycle**, or the results look
  random. The whole point of that test is that the cycle is predictable.

---

# Docs Impact

| File | Change | Task |
|---|---|---|
| `docs/design-handoffs/HANDOFF-kiosk-desktop-launcher.md` | committed + owner decisions recorded | planning PR |
| `docs/superpowers/plans/2026-08-18-kiosk-desktop-launcher.md` | this plan | planning PR |
| `docs/BUILDER_QUEUE.md` | `KIOSK-1` + `KIOSK-2` rows, cross-repo handoff #8 | planning PR |
| `docs/ROADMAP.md` | planning-queue entry | planning PR |
| `CLAUDE.md` | CDP restored; deploy kiosk verification; `radio-kiosk.service`; don't hand-edit `~/Desktop` | A7, B8 |
| `deploy/provision/README.md` | onboard resolved; desktop entries now installer-owned | A6, B8 |
| `design/INTEGRATIONS.md` | second consumer of `gv-bridge-ensure.sh`; the `>1200` rule | B8 |
| `docs/uat/2026-08-18-kiosk-launcher/PROBES.md` + `REPORT.md` | probe answers + UAT record | A0, Test Plan |

---

# Carried risks (each already has a task and a fallback)

| Risk | Fallback | Where |
|---|---|---|
| ~~zenity is GTK 4 → `gtk-3.0/gtk.css` ignored~~ **CONFIRMED: zenity 4.0.1 / libgtk-4** | use `gtk-4.0/gtk.css` — not a risk any more, a requirement | A0 ✅ → B3 |
| ~~`--timeout` / `--default-cancel` unsupported~~ **both CONFIRMED supported**; **`--icon-name` CONFIRMED ABSENT** | `--icon-name` is gone in zenity 4, so per-tier glyphs must come from `--info`/`--warning`/`--error`. **The glyph gives way, not the buttons** — `Try again` / `Open anyway` are live actions | A0 ✅ → B2, B6 |
| ~~`WAYLAND_DISPLAY` absent from the user manager env~~ **CONFIRMED PRESENT** (`wayland-0`, plus `XDG_RUNTIME_DIR`, `DBUS_SESSION_BUS_ADDRESS`, `DISPLAY=:0`) | risk retired; the `--setenv=` fallback is **not shipped** | A0 ✅ → A2 |
| Window raise impossible under Wayland | decline-to-launch guard; drop `Show the sign-in` | B2.7 |
| 56 px override unachievable | wider dialog, fewer larger buttons — and **say so** rather than shipping 34 px | B3 |
| Dialog buried by the fullscreen kiosk | tune `RADIO_DIALOG_DELAY`; if still buried, invert to dialog-first | T22 |
| DING positions pinned rather than alphabetical | clear saved positions; if unreliable, the names already produce the right order | B1 |
| `gv-bridge-ensure.sh` relocated by RotaryPhone's in-flight PR | resolved from a candidate list at runtime | B2.1 |
| Fresh kiosk profile loses nothing — but **verify** | the profile only ever visits `localhost:5002`; the GV session lives on a different profile | A2, T7 |

---

# Scope additions beyond the literal handoff (each droppable)

1. **`--print-status` / `--dry-run` / `--dialog-demo`** on `radio-console-open`. *Required in
   practice*, not cosmetic: without them no tier decision in §6.1 is verifiable on a box that
   cannot be screenshotted. Droppable only if the team accepts visual-only verification.
2. **The established-connection check in `Deploy-ToLinux.ps1`** (A5). The handoff asks for the
   relaunch to work; this makes its failure *visible*. Droppable, but it is the only thing that
   would have caught the Aug 2 outage.
3. **`ROTARY_UNIT` discovery** rather than a hard-coded unit name (B7). Droppable once A0 pins
   the name — but a wrong name makes `PHONE` a permanent false red, which is precisely the
   cry-wolf failure §5.3 exists to prevent.
4. **`--no-first-run --no-default-browser-check`** on the kiosk flags (A2). Not in the handoff;
   required because the profile is new and would otherwise show first-run UI on the panel.

**Deliberately not in scope:** the in-app Blazor favicon (owner deferred — see the top of this
plan); `radio-refresh-browser`, which is still broken because it drives `xdotool` under Wayland
(now *fixable* via the restored CDP on `:9223`, and worth a fast-follow row); and any change to
`gv-bridge-ensure.sh`, its watchdog, or its nightly timer, which are RotaryPhone's.
