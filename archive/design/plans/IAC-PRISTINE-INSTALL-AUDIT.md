# Infrastructure-as-Code (IAC) Audit — Pristine Rebuild of the Radio Console Box

> **Scope:** Audit + documentation only. Nothing on the live box or in the app was modified.
> **Target box:** `radio` (`ssh mmack@radio`) — Ubuntu 24.04.4 LTS (noble), kernel 6.17, Intel N100 x86_64.
> **Repo:** `D:\prj\RTest\RTest` (Radio Console). Shares the box with RotaryPhone.
> **Date:** 2026-07-16. **Method:** read every `deploy/` artifact + the BT/audio boundary doc, then batched read-only inventory over SSH and diffed each artifact against the repo.
> **Boundary contract:** `D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md` — several artifacts below are BT/audio-boundary-owned and any repo change to them must update that doc first.

---

## 1. Executive Summary

**How reproducible is a pristine install today? Roughly 55–60%.** `deploy/debian-x64/setup.sh` + `deploy/debian-x64/kiosk/setup-kiosk.sh` + `Deploy-ToLinux.ps1` get you *most* of a working app (users, dirs, `.NET`, base packages, both services, the two synced WirePlumber rules, kiosk browser, CPU/WiFi tuning). But a bare-Ubuntu run of those scripts **would not produce a working Bluetooth-audio box, and would silently drift on OS tuning and per-machine config.** The single biggest problem: the artifacts that make BT/A2DP actually work were hand-applied to the box over many sessions and were never round-tripped back into `deploy/`.

**Top gaps that would break a rebuild (detail in §4):**

| # | Gap | Impact |
|---|-----|--------|
| P0-1 | WirePlumber rules **`85-disable-hfp-hf.lua`, `87-bt-adapter-select.lua`, `89-bt-autoconnect.lua`** are box-only. Neither `setup.sh` nor `Deploy-ToLinux.ps1` installs them (they only handle `90-*` and `41-*`). | BT adapter isolation, HFP-HF handoff to RotaryPhone, and A2DP auto-connect all gone → no music BT audio + cross-service boundary violation. |
| P0-2 | **`libpw_helper.so`** build+install is wired into **no** script. Source (`pw_helper.c` + `build-pw-helper.sh`) is in the repo, but nothing builds it, and the real install target (`/usr/local/lib` + `ldconfig`) differs from the build script's comment (`/opt/radio-console/api/`). | BT native capture stream (`radio-bt-stream`) cannot load → no BT music capture at all. |
| P0-3 | `radio-api.service.d/pipewire.conf` drop-in (box-only) adds **`DBUS_SESSION_BUS_ADDRESS`**; the repo unit omits it. | PBAP contact sync (obexd) and GNOME sleep/DPMS D-Bus calls fail. |
| P0-4 | **`99-radio-quantum.conf`** (PipeWire `min-quantum=512`) box-only, installed by nothing. | ALSA xruns / audible glitches on the N100. |
| P1 | Box-only ops units + scripts (`radio-audio-verify`, `radio-weekly-maintenance` +timer, memory-limit drop-ins), OS tuning (`swappiness`, zram, masked bloat services, cups off), and **`appsettings.Production.json` drift** (hardware `Devices.*.USBPort` bindings not in the repo template). | Recovery-after-reboot loop, memory hardening, and per-machine hardware bindings lost. |
| P1 | Two **PPAs not installed by any script**: `marin-m/songrec` (Shazam recognition) and `pipewire-debian/pipewire-upstream` (PipeWire 1.0.7 — Ubuntu ships 1.0.5). | Song recognition missing; BT stability regressions on stock PipeWire. |

**Bottom line:** the app binaries deploy reproducibly today; the *platform* (BT/audio plumbing, OS tuning, ops automation) does not. The fix is a new idempotent `deploy/provision/provision.sh` that captures the ~20 box-only artifacts enumerated below.

---

## 2. Environment Baseline

| Property | Value |
|---|---|
| OS | Ubuntu 24.04.4 LTS (noble), kernel 6.17.0-40-generic |
| Arch / CPU | x86_64, Intel N100 (4 cores) |
| Login/kiosk user | `mmack` (uid 1000), groups: `adm cdrom sudo dip plugdev users lpadmin` |
| App system user | `radio` (uid 997), groups: `audio plugdev bluetooth pulse-access` — **created but services do NOT run as it** (they run as `mmack` for PipeWire access) |
| Display stack | GNOME on **Wayland** (`--ozone-platform=wayland`); GDM3 autologin |
| Audio | PipeWire **1.0.7** (PPA) + WirePlumber **0.4.17**, default sink `alsa_output.pci-0000_00_1f.3.analog-stereo` |
| BT | BlueZ 5.72; TP-Link UB500 `hci0` (music) + Intel AX201 `hci1` (voice, RotaryPhone) |
| .NET | System runtimes 8.0.28 + 10.0.9 present; **app deploys self-contained** (system runtime not required) |
| App root | `/opt/radio-console/{api,web,data,logs,config,media,tools,scripts}` |
| Services | `radio-api` :5000, `radio-web` :5002 (both run as `mmack`) |
| Remote-debug ports | kiosk Chrome `9223`, GV-bridge Chrome `9224` |

---

## 3. Full Inventory by Category

Legend for "In repo?": ✅ = captured & installed by a script; ⚠️ = source/logic in repo but **install not wired** or **drifted**; ❌ = box-only, uncaptured.

### 3.1 systemd SYSTEM units (`/etc/systemd/system/`)

| Artifact | Path | Purpose | In repo? | Action to capture |
|---|---|---|---|---|
| radio-api.service | `/etc/systemd/system/` | API service (audio engine, REST, SignalR). Deployed unit **matches** repo (`MemoryHigh=350M`, `CPUAffinity=2 3`, `Nice=-5`, `AmbientCapabilities=CAP_NET_ADMIN`). | ✅ `deploy/common/radio-api.service` | none |
| radio-web.service | same | Blazor UI, `Requires=radio-api`. | ✅ `deploy/common/radio-web.service` | none |
| radio-bt-setup.service | same | Oneshot boot: adapter aliases, stale-pairing cleanup, default sink, bluez.lua+WP verify. | ✅ `deploy/common/radio-bt-setup.service` | none |
| radio-performance.service | same | CPU governor → performance. | ✅ `deploy/common/radio-performance.service` | none |
| radio-pipewire-access.service | same | `setfacl -m u:radio:x /run/user/1000`. **Now vestigial** (services run as `mmack`, not `radio`). | ✅ `deploy/common/radio-pipewire-access.service` | keep, but note it's a no-op for the mmack-run topology |
| **radio-audio-verify.service** | same | Oneshot post-boot audio health verifier (diagnostic v1; logs pipeline state, reads hci0 only). `ExecStart=/opt/radio-console/radio-audio-verify.sh`. | ❌ box-only | add unit + script to repo |
| **radio-weekly-maintenance.service** | same | Oneshot `apt update/upgrade/autoremove` → `systemctl reboot`. Boot recovery is automatic (bt-setup → api → verify). | ❌ box-only | add unit + script to repo |
| **radio-weekly-maintenance.timer** | same | `OnCalendar=Sun *-*-* 03:00`. **Enabled.** | ❌ box-only | add timer to repo |
| radio-api-restart.service | same | Oneshot restart radio-api + re-set default sink. | ❌ box-only | add (superseded — see below) |
| radio-api-restart.timer | same | `OnCalendar=*-*-* 04:00`. **Disabled + inactive** — superseded by weekly-maintenance reboot. | ❌ box-only | capture but mark disabled/legacy |
| **radio-api.service.d/memory-limit.conf** | drop-in dir | `Environment=DOTNET_GCHeapHardLimit=0x30000000` (768 MiB). | ❌ box-only | add drop-in (or fold into unit) |
| **radio-api.service.d/pipewire.conf** | drop-in dir | Adds `XDG_RUNTIME_DIR`, `ProtectSystem=false`, `ProtectHome=false`, **`DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus`**. The DBus var is **not** in the repo unit. | ❌ box-only (P0-3) | add `DBUS_SESSION_BUS_ADDRESS` to repo unit or ship drop-in |
| **radio-web.service.d/memory-limit.conf** | drop-in dir | `DOTNET_GCHeapHardLimit=0x30000000`. | ❌ box-only | add drop-in |
| zramswap.service | package unit | Provided by `zram-tools` (not a custom file). Enabled. | ⚠️ package install not in setup.sh | `apt install zram-tools` in provision |
| rotary-phone.service / rotary-phone-cookies.service | same | **RotaryPhone-owned** — out of scope for this repo. | n/a | RotaryPhone's provisioning |

### 3.2 systemd USER units (`~/.config/systemd/user/`)

| Artifact | Purpose | In repo? | Action |
|---|---|---|---|
| **gv-bridge-chrome.service** (disabled) | Snap Chromium at `voice.google.com`, off-screen, CDP `:9224`, loads `.../gv-bridge-profile/Extension`. | ❌ box-only | RotaryPhone GV-bridge domain (see note) |
| **gv-bridge-restart.service + .timer** (`04:00`) | Nightly recycle of GV Chrome to shed leaked heap. `ExecStart=%h/bin/gv-bridge-restart.sh`. | ❌ box-only | capture under GV integration |
| **gv-bridge-watchdog.service + .timer** (every 2 min) | Relaunch GV Chrome if down. `ExecStart=%h/bin/gv-bridge-ensure.sh`. | ❌ box-only | capture under GV integration |
| obex.service (enabled) | `bluez-obexd` user service — **required for PBAP** contact sync. | ⚠️ documented in INTEGRATIONS.md, `systemctl --user enable --now obex` not scripted | add to provision |
| tracker-*.service, evolution-*-factory.service | Symlinked to `/dev/null` (user-level **mask**) to save RAM. | ❌ box-only | add to provision |

> **GV-bridge ownership note:** The GV-bridge Chrome units/scripts load `/opt/rotary-phone/ChromeExtension` and a snap-Chromium `gv-bridge-profile` — both **RotaryPhone-owned** (boundary doc Change Log 2026-03-21). They live in `mmack`'s session on *this* box and the Radio Console kiosk consumes the GV feed, but the extension source is in the RotaryPhone repo. Capture the *unit + script skeletons* here for rebuild completeness, but the extension/profile provisioning belongs to RotaryPhone. Coordinate via the boundary doc.

### 3.3 Scripts in `/opt/radio-console/` and `~/bin/`

| Script | Purpose | In repo? | Action |
|---|---|---|---|
| `/opt/radio-console/radio-bt-setup.sh` | Boot BT setup + bluez.lua/WP patch verify (installed by setup.sh). | ✅ `deploy/common/radio-bt-setup.sh` | none |
| `/opt/radio-console/radio-audio-verify.sh` | Post-boot audio health verifier (diagnostic). | ❌ box-only | add to `deploy/common/` |
| `/opt/radio-console/radio-weekly-maintenance.sh` | apt upgrade + reboot. | ❌ box-only | add to `deploy/common/` |
| `~/bin/gv-bridge-ensure.sh` | Launch GV Chrome if down (watchdog + autostart). | ❌ box-only | GV integration (RotaryPhone-adjacent) |
| `~/bin/gv-bridge-restart.sh` | Kill+relaunch GV Chrome (nightly). | ❌ box-only | GV integration |
| `/opt/radio-console/scripts/research/*` | 16 BT/audio research harness scripts (py/sh). | ✅ tracked under repo `scripts/research/` | none (deployed copy) |

### 3.4 WirePlumber configs — **BT/audio-boundary-owned**

`/etc/wireplumber/bluetooth.lua.d/`:

| File | Purpose | In repo? | Action |
|---|---|---|---|
| **85-disable-hfp-hf.lua** | Sets `bluez5.roles = [ a2dp_sink a2dp_source bap_sink bap_source hsp_hs hsp_ag hfp_ag ]` — removes `hfp_hf` so RotaryPhone can own HFP-HF. | ❌ box-only (P0-1) | add to `deploy/common/`, install in provision, sync in `Deploy-ToLinux.ps1` |
| **87-bt-adapter-select.lua** | `bluez5.default.adapter = "78:20:51:F5:FB:A7"` — WP manages **only** the TP-Link (music) adapter. | ❌ box-only (P0-1) | add + install + sync |
| **89-bt-autoconnect.lua** | `bluez5.auto-connect = [ a2dp_sink a2dp_source hfp_ag hsp_ag ]` on `bluez_card.*`. | ❌ box-only (P0-1) | add + install + sync |
| 90-disable-bt-input-autolink.lua | `node.autoconnect=false` on `bluez_input.*`. | ✅ `deploy/common/90-...lua` (synced by Deploy-ToLinux + setup.sh) | none |

`/etc/wireplumber/main.lua.d/`:

| File | Purpose | In repo? | Action |
|---|---|---|---|
| 41-disable-bt-input-restore-target.lua | Stops restore-stream re-routing BT input to default sink (dual-path fix). | ✅ `deploy/common/41-...lua` | none |

> **Note:** the box uses **system-wide** `/etc/wireplumber/*` only — there is **no** `~/.config/wireplumber/` (the Pi-style `50-bluez-a2dp-sink.conf` / `51-bt-capture-routing.conf` null-sink approach from `DEPLOYMENT.md` is **not** used on x64; A2DP-sink role comes from `85-*` + the bluez.lua patch, and capture is via the native `radio-bt-stream`, not a null sink).

### 3.5 bluez.lua patch + protection

| Artifact | Purpose | In repo? | Action |
|---|---|---|---|
| `/usr/share/wireplumber/scripts/monitors/bluez.lua` line 384 `if true or ...` patch | PipeWire 1.0.7 quirk workaround (force-activate BT device). | ⚠️ patch **logic** is in `radio-bt-setup.sh` (`verify_bluez_patch` applies via `sed`) | wire `radio-bt-setup.sh --patch-only` into provision |
| `/usr/share/wireplumber/scripts/monitors/bluez.lua.bak` | Pristine backup (pkg original, 2023-12-03). | n/a (created on patch) | auto-created by script |
| `/etc/apt/apt.conf.d/99-protect-bluez-lua` | APT `DPkg::Post-Invoke` hook re-applies patch after any `wireplumber` upgrade. | ✅ `deploy/common/99-protect-bluez-lua` (installed by setup.sh) | none |

### 3.6 PipeWire config

| Artifact | Purpose | In repo? | Action |
|---|---|---|---|
| `~/.config/pipewire/pipewire.conf.d/99-radio-quantum.conf` | `default.clock.min-quantum = 512` — kills ALSA xruns. | ❌ box-only (P0-4) — documented in `DEPLOYMENT.md` but the file is not in the repo and no script installs it | add file to repo + install in provision |

### 3.7 Native lib — **BT capture critical**

| Artifact | Purpose | In repo? | Action |
|---|---|---|---|
| `/usr/local/lib/libpw_helper.so` (28 KB, in `ldconfig` cache) | SPA-pod builder for the PipeWire native capture stream (`radio-bt-stream`). | ⚠️ **source in repo**, **build/install unwired** (P0-2) | see below |
| `src/Radio.Infrastructure/Platform/Bluetooth/Native/pw_helper.c` | C source. | ✅ tracked | — |
| `src/.../Native/build-pw-helper.sh` | `gcc -shared -fPIC ... $(pkg-config --cflags --libs libpipewire-0.3)`. **Comment says `cp libpw_helper.so /opt/radio-console/api/`** but the box actually uses `/usr/local/lib` + `ldconfig`. | ✅ tracked, but **referenced by no deploy/setup script** and install path is inconsistent | provision must: `apt install build-essential pkg-config libpipewire-0.3-dev` → run build → `install -m755 libpw_helper.so /usr/local/lib/ && ldconfig` |

Build toolchain currently present on the box (so a rebuild *can* compile it): `gcc`, `pkg-config`, `libpipewire-0.3-dev` — **none installed by `setup.sh`.**

### 3.8 OS tuning (all box-only)

| Artifact | Value / Purpose | In repo? | Action |
|---|---|---|---|
| `/etc/sysctl.d/99-radio-swappiness.conf` | `vm.swappiness=10` (verified live = 10). | ❌ box-only | add to provision |
| `/etc/default/zramswap` | `ALGO=zstd`, `PERCENT=50`, `PRIORITY=100` (1.8 GB zram swap active). Needs `zram-tools` pkg. | ❌ box-only | add config + `apt install zram-tools` |
| Masked system services | `evolution-{addressbook,calendar,source-registry}-factory`, `tracker-{extract,miner-fs,miner-rss}-3` (system) + user-level `/dev/null` masks. | ❌ box-only | `systemctl mask ...` list in provision |
| `cups` + `cups-browsed` | **disabled** (not masked). | ❌ box-only | `systemctl disable` in provision |
| `/etc/gdm3/custom.conf` | `AutomaticLoginEnable=true` / `AutomaticLogin=mmack`. | ✅ set by `setup-kiosk.sh` | none |
| gsettings | `session idle-delay=0`, `screensaver lock-enabled=false`, `screensaver idle-activation-enabled=false`, `power sleep-inactive-ac-type='nothing'`, `power idle-dim=true`. | ⚠️ `setup-kiosk.sh` sets the first three; `sleep-inactive-ac-type` is extra | add remaining gsettings to kiosk setup |
| `update-notifier.desktop` (autostart) | `Hidden=true` — suppress update popups in kiosk. | ❌ box-only | add to kiosk setup |
| `/etc/modprobe.d/blacklist-rtl.conf` | DEPLOYMENT.md says setup blacklists `dvb_usb_rtl28xxu` — **absent on box** (rtl-sdr tools still installed). | ⚠️ documented, not present | add to provision only if SDR radio is used |

### 3.9 Autostart `.desktop` (`~/.config/autostart/`)

| File | Purpose | In repo? | Action |
|---|---|---|---|
| radio-kiosk-autostart.desktop | Launch kiosk Chrome (`--kiosk ... --ozone-platform=wayland --remote-debugging-port=9223`). **Matches repo.** | ✅ `deploy/debian-x64/kiosk/` | none |
| unclutter.desktop | Hide cursor (`unclutter -idle 3`). | ✅ generated by `setup-kiosk.sh` | none |
| **onboard-autostart.desktop** | On-screen keyboard. **Contradicts** `setup-kiosk.sh` (which says onboard "doesn't work on Wayland" and does NOT install it) yet it's installed + autostarted. | ❌ box-only | reconcile: either drop, or add `onboard` install + autostart to kiosk setup |
| **update-notifier.desktop** | Hidden=true (see §3.8). | ❌ box-only | add to kiosk setup |
| **gv-bridge-chrome.desktop** | `Exec=~/bin/gv-bridge-ensure.sh`, 15 s delay. | ❌ box-only | GV integration (RotaryPhone-adjacent) |
| (setup-kiosk also creates `disable-dpms.desktop`) | X11 `xset` DPMS off — **not present** (box is Wayland; gsettings covers it). | n/a | harmless; xset is a no-op on Wayland |

### 3.10 Config / data

| Artifact | Purpose | In repo? | Action |
|---|---|---|---|
| `/opt/radio-console/config/bt-music-devices.conf` | MACs that must live only on hci0 (music). **Matches repo.** | ✅ `deploy/common/bt-music-devices.conf` | none |
| `/opt/radio-console/api/appsettings.Production.json` | **DRIFTED from repo template.** Box has extra keys: `AudioOutput.GoogleCast.{StreamingMode,ApplicationId}`, **`Devices.Radio.USBPort`, `Devices.Vinyl.USBPort`, `Devices.Cast.DefaultDevice`** (hardware bindings), `Diagnostics.AudioValidation.*`, `Fingerprinting.UseShazamForAllSources`. No secrets in the file. | ⚠️ `deploy/debian-x64/appsettings.Production.json` only has `AudioOutput.DeviceDisplay` + `FilePlayer` | reconcile the repo template with the live hardware bindings (esp. `Devices.*.USBPort`) |
| Web `appsettings.Production.json` | **Absent on box** (web reads `ApiBaseUrl` from unit env). | ⚠️ | harmless; document |
| `/opt/radio-console/.asoundrc` | Direct-ALSA + `bt_capture` pulse TCP hint. | ✅ created by `setup.sh` | none |
| `/opt/radio-console/data/{config,secrets,fingerprints,metrics,albumart,backups}` | SQLite runtime state. `secrets.db` is **machine-key-encrypted — not portable**. | runtime | back up config.db; re-enter secrets |
| `/opt/radio-console/media/audio/**` (incl. `notify/ alarm/ alerts/ ring/`) | Test tracks + event sounds (phone-ring etc.). | runtime | seed event sounds; test tracks optional |

### 3.11 BlueZ pairings (runtime state)

| Adapter | Paired | Note |
|---|---|---|
| `78:20:51:F5:FB:A7` (hci0 music) | 4 devices (Tab A7 Lite, Pixel 7 Pro, + 2 others) | re-pair on rebuild; do **not** copy link keys |
| `10:91:D1:FE:00:46` (hci1 voice) | none | RotaryPhone-owned; do not pair music devices here |

Pairing keys live in `/var/lib/bluetooth/<adapter>/<device>/`. **Document the pairing procedure, not the keys** — put each phone in pairing mode, `bluetoothctl -- select 78:20:51:F5:FB:A7`, `pair`/`trust`/`connect`. Ensure a device is **never** paired on both adapters (boundary rule #8).

### 3.12 Packages / external tools

| Category | Package / source | In setup script? | Action |
|---|---|---|---|
| PipeWire 1.0.7 suite + `libspa-0.2-bluetooth` + `libpipewire-0.3-dev` | **PPA `pipewire-debian/pipewire-upstream`** (Ubuntu ships 1.0.5) | ❌ PPA not added by setup.sh | add PPA + upgrade in provision |
| WirePlumber 0.4.17 | Ubuntu repo | partial | ensure installed |
| Song recognition: `songrec 0.7.4` | **PPA `marin-m/songrec`** | ❌ | add PPA + install |
| Fingerprinting: `fpcalc` (`libchromaprint-tools`) + bundled `/opt/radio-console/tools/fpcalc/fpcalc` | Ubuntu repo | ✅ setup.sh | none |
| BT: `bluez`, `bluez-obexd` (PBAP) | Ubuntu repo | ⚠️ obexd not explicit | add `bluez-obexd` + `systemctl --user enable --now obex` |
| SDR: `rtl-sdr`, `librtlsdr-dev` | Ubuntu repo | DEPLOYMENT.md documents (Pi setup) | add to x64 provision if SDR used |
| Kiosk: `google-chrome` (Google apt repo), `unclutter`, `xdotool`, `onboard` | mixed | ⚠️ chrome repo + onboard not scripted | add chrome apt source + installs |
| GV bridge: `chromium` (snap) | snap | ❌ | RotaryPhone-adjacent |
| zram: `zram-tools` | Ubuntu repo | ❌ | add |
| Discovery: `avahi-daemon`, `avahi-utils` | Ubuntu repo | ✅ setup.sh | none |
| Build: `build-essential`/`gcc`, `pkg-config` | Ubuntu repo | ❌ | add (needed for `libpw_helper.so`) |
| .NET | System 8+10 present; app self-contained | setup.sh installs .NET 8 (stale — app is net10) | bump to `--channel 10.0` or rely on self-contained |

### 3.13 Other rebuild-fragile items

- **`radio` user + group** (uid 997) — created by `setup.sh`; keep (owns the pipewire-access ACL), even though services run as `mmack`.
- **ACL** `user:radio:--x` on `/run/user/1000` — set by `radio-pipewire-access.service` (now effectively a no-op for the mmack topology, but harmless).
- **`mmack` group membership** — services get `audio`/`bluetooth`/`pulse-access` via the unit's `Group=`/`SupplementaryGroups=`, **not** from `mmack`'s own groups (mmack is not in those groups). Reproducible from the unit files.
- **XDG/DBus wiring** — `XDG_RUNTIME_DIR=/run/user/1000` (unit) + `DBUS_SESSION_BUS_ADDRESS` (drop-in, P0-3).
- **Remote-debug ports** — kiosk `9223`, GV `9224` (baked into `.desktop`/unit exec lines).

---

## 4. Prioritized Gap List (box-only, ranked by rebuild impact)

**P0 — audio/BT will not work without these:**
1. **WP `85/87/89-*.lua`** (§3.4) — adapter isolation + HFP-HF handoff + A2DP autoconnect. *Also update `Deploy-ToLinux.ps1`'s `$wpBluetoothRules` list and `radio-bt-setup.sh`'s `verify_wp_configs` to include them (it already checks for them but nothing installs them).* Boundary-owned.
2. **`libpw_helper.so` build+install** (§3.7) — plus its build deps (`build-essential`, `pkg-config`, `libpipewire-0.3-dev`) and the correct install path (`/usr/local/lib` + `ldconfig`).
3. **`DBUS_SESSION_BUS_ADDRESS`** in radio-api (§3.1 pipewire.conf drop-in) — PBAP + sleep D-Bus.
4. **`99-radio-quantum.conf`** (§3.6) — xrun elimination.
5. **PipeWire 1.0.7 PPA** (§3.12) — bluez stability; the bluez.lua patch targets the 1.0.7 quirk.

**P1 — degraded/undefended box, wrong behavior:**
6. `appsettings.Production.json` drift — hardware `Devices.*.USBPort`/`Cast.DefaultDevice` bindings (§3.10).
7. Memory hardening drop-ins (`DOTNET_GCHeapHardLimit` ×2) (§3.1).
8. OS tuning: `swappiness=10`, zram (+`zram-tools`), masked bloat services, cups off (§3.8).
9. Ops automation: `radio-audio-verify` + `radio-weekly-maintenance` (+timer) + scripts (§3.1/§3.3) — the auto-update+reboot recovery loop.
10. `songrec` PPA + package (§3.12).
11. `obex.service` enablement + `bluez-obexd` (PBAP) (§3.2/§3.12).

**P2 — convenience / cross-service / legacy:**
12. GV-bridge user units + `~/bin/gv-bridge-*.sh` + autostart (§3.2/§3.9) — RotaryPhone-adjacent; coordinate.
13. Kiosk extras: `onboard` install+autostart, `update-notifier` hide, extra gsettings (§3.8/§3.9).
14. `radio-api-restart.timer` (disabled/legacy) (§3.1).
15. BlueZ re-pair procedure (§3.11).

---

## 5. Proposed Pristine-Install Runbook (bare Ubuntu 24.04 → working box)

> Ordered. Items marked **[new]** need artifacts that don't yet exist in `deploy/` (see §6).

1. **Base OS.** Install Ubuntu 24.04 Desktop, create user `mmack` (uid 1000). Enable passwordless sudo. Set hostname `radio`.
2. **Repo + APT sources.** Clone repo. **[new]** Add apt sources: Google Chrome, PPA `marin-m/songrec`, PPA `pipewire-debian/pipewire-upstream`. `apt update`.
3. **Packages.** Run `deploy/debian-x64/setup.sh` (creates `radio` user, dirs, `.asoundrc`, fpcalc, base services, `90-*`/`41-*` WP rules, APT hook, performance/wifi tuning). **[new]** additionally install: `pipewire* wireplumber libspa-0.2-bluetooth libpipewire-0.3-dev build-essential pkg-config songrec bluez-obexd zram-tools onboard unclutter xdotool google-chrome-stable rtl-sdr librtlsdr-dev`; upgrade PipeWire to 1.0.7 from the PPA.
4. **Native helper [new, P0-2].** `bash src/Radio.Infrastructure/Platform/Bluetooth/Native/build-pw-helper.sh` → `sudo install -m755 libpw_helper.so /usr/local/lib/ && sudo ldconfig`.
5. **WirePlumber BT rules [new, P0-1].** Install `85/87/89-*.lua` (boundary-owned) alongside the already-handled `90-*`/`41-*`. Verify bluez.lua patch: `sudo /opt/radio-console/radio-bt-setup.sh --patch-only`.
6. **PipeWire quantum [new, P0-4].** Install `~/.config/pipewire/pipewire.conf.d/99-radio-quantum.conf`; `systemctl --user restart pipewire wireplumber`.
7. **Service overrides [new, P0-3].** Install `radio-api.service.d/{pipewire,memory-limit}.conf` + `radio-web.service.d/memory-limit.conf` (or fold `DBUS_SESSION_BUS_ADDRESS` + GC limit into the units). `systemctl daemon-reload`.
8. **OS tuning [new, P1].** Install `/etc/sysctl.d/99-radio-swappiness.conf` (`sysctl --system`), `/etc/default/zramswap` (enable `zramswap`), mask `tracker-*`/`evolution-*-factory` (system + user), `systemctl disable cups cups-browsed`.
9. **PBAP.** `systemctl --user enable --now obex`.
10. **Ops units [new, P1].** Install `radio-audio-verify.{sh,service}`, `radio-weekly-maintenance.{sh,service,timer}`; enable the verify service + weekly timer.
11. **App deploy.** From Windows: `./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64` (self-contained; also syncs WP rules + Production config). Reconcile `appsettings.Production.json` with live `Devices.*.USBPort` bindings (§3.10).
12. **Kiosk.** `deploy/debian-x64/kiosk/setup-kiosk.sh` (autologin, gsettings, unclutter, desktop shortcuts, refresh helper). **[new]** add `onboard` + `update-notifier` hide + extra gsettings if desired.
13. **Bluetooth pairing.** `bluetoothctl -- select 78:20:51:F5:FB:A7`; put each phone in pairing mode; `pair`/`trust`/`connect`. Never pair the same device on hci1.
14. **Secrets.** Re-enter AcoustID / Spotify / Google TTS keys via System Config (machine-key-encrypted, not portable).
15. **GV bridge (optional, cross-service).** Coordinate with RotaryPhone to provision the Chrome extension/profile + install the `gv-bridge-*` user units/scripts/autostart.
16. **Reboot & verify.** Confirm auto-recovery chain: `radio-bt-setup` → `radio-api` → `radio-audio-verify`; GV + kiosk relaunch via autostart; check `journalctl -t radio-audio-verify`.

---

## 6. Recommended Repo Changes

Create a single idempotent provisioner that owns everything `setup.sh`/`setup-kiosk.sh`/`Deploy-ToLinux.ps1` don't. Suggested layout:

```
deploy/provision/
  provision.sh                     # idempotent entrypoint; --with-sdr / --with-gv flags
  packages.sh                      # apt sources (chrome, songrec PPA, pipewire PPA) + installs (incl. build-essential, pkg-config, libpipewire-0.3-dev, zram-tools, bluez-obexd, onboard, unclutter, xdotool)
  build-native.sh                  # build + install libpw_helper.so → /usr/local/lib + ldconfig   [P0-2]
  wireplumber/                     # boundary-owned — coordinate via BT/audio boundary doc
    85-disable-hfp-hf.lua          [P0-1]
    87-bt-adapter-select.lua       [P0-1]
    89-bt-autoconnect.lua          [P0-1]
  pipewire/
    99-radio-quantum.conf          [P0-4]
  systemd/
    radio-api.service.d/pipewire.conf      # incl. DBUS_SESSION_BUS_ADDRESS  [P0-3]
    radio-api.service.d/memory-limit.conf
    radio-web.service.d/memory-limit.conf
    radio-audio-verify.service
    radio-weekly-maintenance.service
    radio-weekly-maintenance.timer
    radio-api-restart.{service,timer}       # legacy/disabled — capture, leave disabled
  scripts/
    radio-audio-verify.sh
    radio-weekly-maintenance.sh
  os-tuning/
    99-radio-swappiness.conf
    zramswap                        # /etc/default/zramswap
    mask-bloat.sh                   # mask tracker-*/evolution-*; disable cups*
  gv-bridge/                        # RotaryPhone-adjacent; guarded behind --with-gv
    (skeletons for gv-bridge-{chrome,restart,watchdog} units + ensure/restart scripts + autostart)
  README.md                        # maps every artifact to §3 of this audit
```

**Also fix these existing files:**
- **`deploy/Deploy-ToLinux.ps1`** — add `85/87/89-*.lua` to `$wpBluetoothRules` so deploys keep them in sync (they're already checked by `radio-bt-setup.sh` but installed by nothing).
- **`deploy/common/radio-api.service`** — add `Environment=DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus` and `Environment=DOTNET_GCHeapHardLimit=0x30000000` (or ship the drop-ins) so the repo unit is self-sufficient.
- **`deploy/debian-x64/setup.sh`** — bump `.NET` install to `--channel 10.0`; add the PPAs + build deps; call `deploy/provision/build-native.sh`.
- **`deploy/debian-x64/appsettings.Production.json`** — reconcile with the live hardware bindings (`Devices.*.USBPort`, `Cast.DefaultDevice`, GoogleCast, Diagnostics, Fingerprinting).
- **`deploy/debian-x64/kiosk/setup-kiosk.sh`** — resolve the `onboard` contradiction (install+autostart it, or remove it from the box); add `update-notifier` hide + the extra gsettings.

**Boundary coordination (do first):** `85/87/89-*.lua`, the bluez.lua patch, and the adapter-select MAC are **BT/audio-boundary artifacts owned by Radio Console**. Adding them to `deploy/` is in-lane, but per `RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md` you must record the move in that doc's Change Log so the RotaryPhone session knows where the canonical copies now live. The `zramswap`/masks/sysctl OS-tuning items are shared-system changes — note them in the boundary doc too, since a rebuild affects both services.

---

## 7. Flags / Risks / Ambiguities

- **`libpw_helper.so` install-path inconsistency** — `build-pw-helper.sh` says `cp → /opt/radio-console/api/`, but the working box uses `/usr/local/lib` (survives deploys via `ldconfig`, per project memory). A deploy wipes `/opt/radio-console/api/`, so the `/opt` path would be lost on every deploy. **Standardize on `/usr/local/lib` + `ldconfig`** and fix the script comment.
- **`.NET` version drift in `setup.sh`** — installs `aspnetcore 8.0`; the app is `net10.0`. Self-contained deploys hide this, but a `-Quick` (framework-dependent) deploy on a freshly-setup box would fail without the 10.0 runtime.
- **`onboard` contradiction** — installed + autostarted on the box, but `setup-kiosk.sh` explicitly declines to install it (Wayland). The virtual keyboard is also built into the Web UI (FUTURE-WORK §12). Decide whether system `onboard` is still wanted.
- **GV-bridge ownership** — two different Chrome setups coexist: the (disabled) `gv-bridge-chrome.service` uses **snap Chromium** + a snap profile, while the **enabled** watchdog/restart path uses **google-chrome** + `~/.config/gv-bridge-chrome` + `/opt/rotary-phone/ChromeExtension`. The extension/profile are RotaryPhone-owned. Clarify which path is canonical before scripting it.
- **`radio-pipewire-access.service` is vestigial** for the current mmack-run topology (the ACL grants the unused `radio` user traverse rights). Harmless, but confusing on rebuild — document intent or drop.
- **Secrets are machine-key-encrypted** (`data/secrets/secrets.db`) — never portable; always re-entered. Not an IAC artifact, but a rebuild step people forget.
- **Kept live queries light** per the box's SSH-load/audio-distortion caveat: all inventory was read-only, no `journalctl` tailing or DB reads. No box or app state was modified.
