# `deploy/provision/` — bare-Ubuntu platform provisioner

This tree captures the **box-only platform state** that the existing deploy
scripts (`deploy/debian-x64/setup.sh`, `deploy/debian-x64/kiosk/setup-kiosk.sh`,
`deploy/Deploy-ToLinux.ps1`) do **not** — the hand-applied artifacts that make
Bluetooth/A2DP audio work, tune the Intel N100 OS, and run the ops automation.
Without it, a rebuild produces an app that boots but has **no working BT music
audio** and drifts on OS tuning.

**Read first:** the audit that motivated this tree —
[`design/plans/IAC-PRISTINE-INSTALL-AUDIT.md`](../../design/plans/IAC-PRISTINE-INSTALL-AUDIT.md).
Every file here maps to a section (§3.x) of that audit; the ordered bare-Ubuntu
runbook is §5.

> **Boundary-owned artifacts:** the WirePlumber `85/87/89` rules and the
> `bluez.lua` patch are BT/audio-boundary artifacts shared with RotaryPhone.
> Their canonical repo home is `deploy/common/` (next to `90`/`41`). See
> `D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md` (Change
> Log entry `2026-07-16`).

---

## Quick start (rebuild)

Run in the order the runbook (audit §5) prescribes:

```bash
# 1. Base packages, users, dirs, services, WP 90/41+85/87/89, APT hook, tuning
sudo deploy/debian-x64/setup.sh

# 2. App binaries (from the Windows dev box)
#    ./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64

# 3. The platform layer this tree owns — run as the login user (mmack) w/ sudo
deploy/provision/provision.sh            # add --with-sdr for librtlsdr-dev

# 4. Kiosk (autologin, browser, gsettings)
deploy/debian-x64/kiosk/setup-kiosk.sh
```

`provision.sh` is **idempotent** and **check-before-apply** — safe to re-run.
It installs system files via `sudo` and user-session state as the login user.
**Never run it against the live production box for a "test"** — it is for a
rebuild.

---

## What `provision.sh` orchestrates

| Step | Script / files | Audit § | Gap |
|------|----------------|:-------:|:---:|
| 1. Packages + PPAs | `packages.sh` | §3.12 | P1 |
| 2. Native BT capture helper | `build-native.sh` → `/usr/local/lib/libpw_helper.so` + `ldconfig` | §3.7 | **P0-2** |
| 3. WirePlumber BT rules + `bluez.lua` patch | `deploy/common/{41,85,87,89,90}-*.lua` + `radio-bt-setup.sh --patch-only` | §3.4/§3.5 | **P0-1** |
| 4. PipeWire quantum | `pipewire/99-radio-quantum.conf` → `~/.config/pipewire/pipewire.conf.d/` | §3.6 | **P0-4** |
| 5. systemd env reconcile + ops units | `systemd/` + `scripts/` | §3.1/§3.3 | P0-3 / P1 |
| 6. OS tuning | `os-tuning/` (swappiness, zram, mask-bloat, cups off) | §3.8 | P1 |
| 7. PBAP | `systemctl --user enable --now obex` | §3.2 | P1 |

### Directory map

```
deploy/provision/
  provision.sh          # idempotent entrypoint (--user/--with-sdr/--skip-packages)
  packages.sh           # APT sources (chrome + songrec/pipewire PPAs) + package set
  build-native.sh       # build + install libpw_helper.so -> /usr/local/lib [P0-2]
  pipewire/
    99-radio-quantum.conf              # default.clock.min-quantum=512 [P0-4]
  systemd/
    radio-audio-verify.service         # post-boot audio health verifier
    radio-weekly-maintenance.service   # apt upgrade + reboot (Sun 03:00)
    radio-weekly-maintenance.timer
    radio-api-restart.{service,timer}  # LEGACY — captured, NOT installed (see below)
    radio-api.service.d/pipewire.conf      # FALLBACK drop-in (DBus) — see note
    radio-api.service.d/memory-limit.conf  # FALLBACK drop-in (GC cap)
    radio-web.service.d/memory-limit.conf  # FALLBACK drop-in (GC cap)
    radio-web.service.d/10-dataprotection-home.conf # FALLBACK drop-in (writable HOME)
  scripts/
    radio-audio-verify.sh
    radio-weekly-maintenance.sh
  os-tuning/
    99-radio-swappiness.conf           # vm.swappiness=10
    zramswap                           # /etc/default/zramswap (zstd, 50%)
    mask-bloat.sh                      # mask evolution/tracker (user) + disable cups
  README.md
```

The WirePlumber `85/87/89` **canonical copies live in `deploy/common/`** (not
here) so `Deploy-ToLinux.ps1` and `setup.sh` sync the same files
`radio-bt-setup.sh` verifies. `provision.sh` installs them from there.

### systemd drop-ins are a fallback, not the primary mechanism

The box had three drop-ins adding `DBUS_SESSION_BUS_ADDRESS` (P0-3) and
`DOTNET_GCHeapHardLimit`, plus a 2026-08-18 hotfix drop-in giving `radio-web` a
writable `HOME` (`10-dataprotection-home.conf`). Those values are now **folded
into the canonical main units** (`deploy/common/radio-api.service`,
`radio-web.service`), so the drop-ins are usually redundant. `provision.sh`
installs the fallback drop-ins in
`systemd/` **only if** a deployed main unit predates the fold (missing the value).

### Legacy units (captured, not installed)

`radio-api-restart.{service,timer}` (daily 04:00 restart) are **superseded** by
`radio-weekly-maintenance` (which reboots weekly, and boot recovery is
automatic: `radio-bt-setup` → `radio-api` → `radio-audio-verify`). They are
captured for completeness but `provision.sh` does **not** install them. On the
live box the timer is disabled/inactive.

---

## Manual / runtime steps (NOT automatable)

`provision.sh` prints these on completion:

1. **Bluetooth pairing** — pairing keys are per-adapter runtime state and are
   never copied. On `hci0` (music) only:
   ```bash
   bluetoothctl -- select 78:20:51:F5:FB:A7
   # put phone in pairing mode
   pair <MAC> ; trust <MAC> ; connect <MAC>
   ```
   **Never pair the same device on `hci1`** (RotaryPhone voice) — boundary
   rule #8; duplicate BT devices break WirePlumber profile resolution.

2. **Secrets / keyring** — `data/secrets/secrets.db` is machine-key-encrypted
   and **not portable**. Re-enter AcoustID / Spotify / Google TTS keys via the
   **System Config** page after rebuild. `${secret:...}` tags in config resolve
   against this store.

3. **`appsettings.Production.json` hardware bindings** — the repo template
   (`deploy/debian-x64/appsettings.Production.json`) ships the full **key
   structure** with **empty placeholders** for the per-machine bindings. Fill
   them for the target box:

   | Key | Live `radio` value | How to find it |
   |-----|-------------------|----------------|
   | `Devices.Radio.USBPort` | `AB13X` | Devices page → the RTL-SDR/USB radio's port id |
   | `Devices.Vinyl.USBPort` | *(empty)* | Devices page → phono/vinyl capture device |
   | `Devices.Cast.DefaultDevice` | *(empty)* | Cast dropdown → preferred default receiver |

   `Deploy-ToLinux.ps1` only writes this template when the box has no
   `appsettings.Production.json` yet, so it never clobbers a filled-in live copy.
   **No secrets live in this file** (the Cast `ApplicationId 567E3DBA` is a public
   receiver id).

4. **Kiosk** — `deploy/debian-x64/kiosk/setup-kiosk.sh` (autologin, gsettings,
   unclutter, refresh helper). See "Kiosk reconciliation" below for the box-only
   extras that setup-kiosk.sh does not yet cover.

5. **Reboot & verify** the auto-recovery chain and check
   `journalctl -t radio-audio-verify`.

---

## GV bridge (cross-service — RotaryPhone-owned, NOT provisioned here)

The box runs a **second Chrome** at `voice.google.com` that bridges Google Voice
into the phone UI. **This is RotaryPhone's, not Radio Console's** — do not script
it in this tree. Documented here only so a rebuild knows the dependency exists.

- **Canonical launcher** (enabled path): the **google-chrome watchdog**, user
  units `gv-bridge-watchdog.{service,timer}` (every 2 min) + `gv-bridge-restart.{service,timer}`
  (nightly 04:00) running `~/bin/gv-bridge-ensure.sh` / `gv-bridge-restart.sh`,
  plus autostart `~/.config/autostart/gv-bridge-chrome.desktop`. `ensure.sh`
  launches:
  ```
  google-chrome --mute-audio --load-extension=/opt/rotary-phone/ChromeExtension \
    --user-data-dir=/home/mmack/.config/gv-bridge-chrome --ozone-platform=wayland \
    --window-position=10000,10000 https://voice.google.com
  ```
- The Chrome **extension** (`/opt/rotary-phone/ChromeExtension`) and the
  `gv-bridge-profile` are **RotaryPhone repo** artifacts (boundary doc Change Log
  2026-03-21). The old snap-Chromium `gv-bridge-chrome.service` is **disabled**
  (superseded by the google-chrome watchdog).
- **To provision on a rebuild:** coordinate with the RotaryPhone session — it
  owns the extension/profile install and the `gv-bridge-*` user units/scripts.
  Radio Console only consumes the GV REST/SignalR feed (`radio:5004`).

---

## Kiosk reconciliation (tracked for a follow-up to `setup-kiosk.sh`)

`setup-kiosk.sh` covers autologin, three gsettings, unclutter, the refresh helper,
and — since 2026-08-18 — **the three `~/Desktop` entries and the two kiosk helper
scripts** (`/usr/local/bin/radio-kiosk-launch`, `/usr/local/bin/radio-kiosk-exit`).
That last part closes the drift loop: before it, nothing had ever copied the repo's
`.desktop` files onto the box, so `~/Desktop` was hand-maintained and diverged.

**The repo is now the source of truth for `~/Desktop`. Do not hand-edit those entries.**
The deploy scripts do not ship `deploy/debian-x64/kiosk/`, so re-run the installer from
a checkout after changing anything in it:

```bash
scp -r deploy/debian-x64/kiosk mmack@radio:/tmp/kiosk-src
ssh mmack@radio 'cd /tmp/kiosk-src && ./setup-kiosk.sh mmack'
```

The live box additionally has (audit §3.8/§3.9) — apply manually or fold into
`setup-kiosk.sh` later:

- gsettings `org.gnome.settings-daemon.plugins.power sleep-inactive-ac-type 'nothing'`
  (the three screensaver/idle keys are already in `setup-kiosk.sh`).
- `~/.config/autostart/update-notifier.desktop` with `Hidden=true` +
  `X-GNOME-Autostart-enabled=false` (suppress update popups in kiosk).
- ~~`onboard` on-screen keyboard~~ — **removed 2026-08-18.** The contradiction is resolved in
  favour of `setup-kiosk.sh`'s note: onboard cannot type into Chrome on Wayland
  (`docs/uat/2026-08-03-osk-wayland-viability/REPORT.md` measured **zero**
  `zwp_text_input_v3.enable()` calls from Chrome 151). Dropped from `packages.sh`; the autostart
  entry is renamed to `.disabled` by `setup-kiosk.sh`. Text entry is the Web UI's virtual
  keyboard.

---

## Verified vs. the live box

All captured configs were pulled read-only from `radio` on 2026-07-16 and match
this tree byte-for-byte (`packages.sh`/`build-native.sh`/`provision.sh`/`mask-bloat.sh`
are new orchestrators, not captures). See the PR description for the
capture/verification table.
