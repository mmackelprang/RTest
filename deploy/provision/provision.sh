#!/usr/bin/env bash
# provision.sh — Idempotent provisioner for the box-only platform state that
# deploy/debian-x64/setup.sh, deploy/debian-x64/kiosk/setup-kiosk.sh, and
# deploy/Deploy-ToLinux.ps1 do NOT capture.
#
# This is the "save" half of the IAC audit (design/plans/IAC-PRISTINE-INSTALL-AUDIT.md).
# It captures the ~20 hand-applied artifacts that make Bluetooth/A2DP audio work,
# tune the OS, and run the ops automation — so a bare-Ubuntu box can be rebuilt
# to a working state instead of drifting.
#
# ORDER (per the runbook, audit §5): run AFTER deploy/debian-x64/setup.sh and the
# app deploy (Deploy-ToLinux.ps1), and BEFORE / around setup-kiosk.sh.
#
# SAFETY: every step is check-before-apply and safe to re-run. It installs system
# files via sudo and user-session state as the login user. NEVER run this against
# the live production box for a "test" — it is for a rebuild. It does not pair
# Bluetooth devices or enter secrets (those are manual — see the summary).
#
# Usage (run as the kiosk login user, e.g. mmack, with passwordless sudo):
#   deploy/provision/provision.sh [--user NAME] [--with-sdr] [--skip-packages]
#
#   --user NAME        Target desktop user for user-session steps (default:
#                      invoking user, or SUDO_USER if run via sudo, else mmack).
#   --with-sdr         Also install SDR dev headers (librtlsdr-dev).
#   --skip-packages    Skip the apt/PPA step (packages.sh) — useful for re-runs.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
COMMON_DIR="$REPO_ROOT/deploy/common"

# --- Args --------------------------------------------------------------------
WITH_SDR=false
SKIP_PACKAGES=false
CLI_USER=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --user) CLI_USER="${2:-}"; shift 2 ;;
    --with-sdr) WITH_SDR=true; shift ;;
    --skip-packages) SKIP_PACKAGES=true; shift ;;
    *) echo "[provision] unknown arg: $1" >&2; exit 2 ;;
  esac
done

# --- Resolve target desktop user + user-bus context --------------------------
if [[ -n "$CLI_USER" ]]; then
  TARGET_USER="$CLI_USER"
elif [[ $(id -u) -eq 0 ]]; then
  TARGET_USER="${SUDO_USER:-mmack}"
else
  TARGET_USER="$(id -un)"
fi
TARGET_UID="$(id -u "$TARGET_USER" 2>/dev/null || echo 1000)"
# `|| true` inside the substitution so a missing/typo'd user (getent exits non-zero)
# falls through to the /home/<user> default instead of aborting under set -e/pipefail.
TARGET_HOME="$(getent passwd "$TARGET_USER" 2>/dev/null | cut -d: -f6 || true)"
TARGET_HOME="${TARGET_HOME:-/home/$TARGET_USER}"

log()  { echo "[provision] $*"; }
step() { echo; echo "===== $* ====="; }

# run_user: execute in the target user's session (systemctl --user, pactl, gsettings).
run_user() {
  if [[ "$(id -un)" == "$TARGET_USER" ]]; then
    XDG_RUNTIME_DIR="/run/user/$TARGET_UID" \
      DBUS_SESSION_BUS_ADDRESS="unix:path=/run/user/$TARGET_UID/bus" "$@"
  else
    sudo -u "$TARGET_USER" XDG_RUNTIME_DIR="/run/user/$TARGET_UID" \
      DBUS_SESSION_BUS_ADDRESS="unix:path=/run/user/$TARGET_UID/bus" "$@"
  fi
}

# install_sys_file SRC DST [MODE] — returns 0 if it changed the file, 1 if not.
install_sys_file() {
  local src="$1" dst="$2" mode="${3:-0644}"
  if [[ ! -f "$src" ]]; then log "WARNING: source missing: $src"; return 1; fi
  if sudo cmp -s "$src" "$dst" 2>/dev/null; then
    return 1
  fi
  sudo install -D -m "$mode" -o root -g root "$src" "$dst"
  log "  installed $dst"
  return 0
}

log "Target user: $TARGET_USER (uid $TARGET_UID, home $TARGET_HOME)"
log "Repo root:   $REPO_ROOT"

# =============================================================================
step "1. Packages + APT sources (packages.sh)"
if $SKIP_PACKAGES; then
  log "Skipped (--skip-packages)."
else
  pkg_args=()
  $WITH_SDR && pkg_args+=(--with-sdr)
  bash "$SCRIPT_DIR/packages.sh" "${pkg_args[@]}"
fi

# =============================================================================
step "2. Native capture helper (build-native.sh) [P0-2]"
bash "$SCRIPT_DIR/build-native.sh"

# =============================================================================
step "3. WirePlumber BT rules + bluez.lua patch [P0-1]"
# Canonical copies live in deploy/common/ (next to 90/41), so Deploy-ToLinux.ps1
# and setup.sh sync the same files. Install any that changed, then restart WP once.
wp_changed=false
# main.lua.d rules
if install_sys_file "$COMMON_DIR/41-disable-bt-input-restore-target.lua" \
      /etc/wireplumber/main.lua.d/41-disable-bt-input-restore-target.lua; then wp_changed=true; fi
# bluetooth.lua.d rules (85/87/89 are boundary-owned — see boundary doc)
for r in 85-disable-hfp-hf.lua 87-bt-adapter-select.lua 89-bt-autoconnect.lua 90-disable-bt-input-autolink.lua; do
  if install_sys_file "$COMMON_DIR/$r" "/etc/wireplumber/bluetooth.lua.d/$r"; then wp_changed=true; fi
done

# Verify/apply the bluez.lua PipeWire-1.0.7 patch via the existing boot script.
if [[ -x /opt/radio-console/radio-bt-setup.sh ]]; then
  log "Verifying bluez.lua patch (radio-bt-setup.sh --patch-only)..."
  sudo /opt/radio-console/radio-bt-setup.sh --patch-only || log "WARNING: patch verify returned non-zero"
else
  log "NOTE: /opt/radio-console/radio-bt-setup.sh not installed yet — run setup.sh first"
fi

if $wp_changed; then
  log "WirePlumber rules changed — restarting wireplumber..."
  run_user systemctl --user restart wireplumber 2>/dev/null || log "WARNING: could not restart wireplumber"
else
  log "WirePlumber rules already up to date."
fi

# =============================================================================
step "4. PipeWire quantum config [P0-4]"
PW_CONF_DIR="$TARGET_HOME/.config/pipewire/pipewire.conf.d"
PW_CONF="$PW_CONF_DIR/99-radio-quantum.conf"
if run_user cmp -s "$SCRIPT_DIR/pipewire/99-radio-quantum.conf" "$PW_CONF" 2>/dev/null; then
  log "99-radio-quantum.conf already up to date."
else
  run_user mkdir -p "$PW_CONF_DIR"
  # Copy via a temp the target user can read, then place it as the user.
  tmp="$(mktemp)"; cp "$SCRIPT_DIR/pipewire/99-radio-quantum.conf" "$tmp"; chmod 0644 "$tmp"
  if [[ "$(id -un)" == "$TARGET_USER" ]]; then
    cp "$tmp" "$PW_CONF"
  else
    sudo install -m 0644 -o "$TARGET_USER" -g "$TARGET_USER" "$tmp" "$PW_CONF"
  fi
  rm -f "$tmp"
  log "  installed $PW_CONF — restarting pipewire..."
  run_user systemctl --user restart pipewire 2>/dev/null || log "WARNING: could not restart pipewire"
fi

# =============================================================================
step "5. systemd — env reconcile + ops units [P0-3, P1]"
# 5a. Main-unit env reconcile. The canonical radio-api/web units in deploy/common
# now FOLD in DBUS_SESSION_BUS_ADDRESS (P0-3) + DOTNET_GCHeapHardLimit, so the
# box-only drop-ins are normally unnecessary. As a safety net for a box whose
# deployed main unit predates the fold, install the fallback drop-ins ONLY when
# the running unit is missing the values.
reconcile_dropin() {  # unit_name  needle  dropin_relpath
  local unit="$1" needle="$2" rel="$3"
  local main="/etc/systemd/system/$unit"
  if [[ ! -f "$main" ]]; then
    log "  $unit not installed yet (run setup.sh) — skipping env reconcile"
    return
  fi
  if sudo grep -q "$needle" "$main" 2>/dev/null; then
    log "  $unit already carries $needle (folded) — no drop-in needed"
  else
    log "  $unit missing $needle — installing fallback drop-in $rel"
    install_sys_file "$SCRIPT_DIR/systemd/$rel" "/etc/systemd/system/$rel" || true
  fi
}
reconcile_dropin radio-api.service DBUS_SESSION_BUS_ADDRESS radio-api.service.d/pipewire.conf
reconcile_dropin radio-api.service DOTNET_GCHeapHardLimit   radio-api.service.d/memory-limit.conf
reconcile_dropin radio-web.service DOTNET_GCHeapHardLimit   radio-web.service.d/memory-limit.conf
# radio-web only: HOME must land on a writable path inside the ProtectHome=true
# sandbox. Needle is the full value, not just "HOME=", so a unit that sets no HOME
# (systemd then derives /home/mmack from User=) or sets a different one still gets
# the drop-in.
reconcile_dropin radio-web.service "HOME=/opt/radio-console/data" radio-web.service.d/10-dataprotection-home.conf

# 5b. Ops scripts + units (box-only: audio-verify + weekly-maintenance).
log "Installing ops scripts to /opt/radio-console/..."
sudo mkdir -p /opt/radio-console
install_sys_file "$SCRIPT_DIR/scripts/radio-audio-verify.sh"       /opt/radio-console/radio-audio-verify.sh       0755 || true
install_sys_file "$SCRIPT_DIR/scripts/radio-weekly-maintenance.sh" /opt/radio-console/radio-weekly-maintenance.sh 0755 || true

log "Installing ops units..."
install_sys_file "$SCRIPT_DIR/systemd/radio-audio-verify.service"       /etc/systemd/system/radio-audio-verify.service       || true
install_sys_file "$SCRIPT_DIR/systemd/radio-weekly-maintenance.service" /etc/systemd/system/radio-weekly-maintenance.service || true
install_sys_file "$SCRIPT_DIR/systemd/radio-weekly-maintenance.timer"   /etc/systemd/system/radio-weekly-maintenance.timer   || true

sudo systemctl daemon-reload
sudo systemctl enable radio-audio-verify.service   >/dev/null 2>&1 || true
sudo systemctl enable radio-weekly-maintenance.timer >/dev/null 2>&1 || true
log "  enabled radio-audio-verify.service + radio-weekly-maintenance.timer"
# NOTE: radio-api-restart.{service,timer} are captured under systemd/ but are
# LEGACY (superseded by the weekly-maintenance reboot). Left uninstalled on
# purpose. See README.

# =============================================================================
step "6. OS tuning [P1]"
# swappiness
if install_sys_file "$SCRIPT_DIR/os-tuning/99-radio-swappiness.conf" /etc/sysctl.d/99-radio-swappiness.conf; then
  sudo sysctl --system >/dev/null 2>&1 || true
  log "  applied vm.swappiness"
fi
# zram
zram_changed=false
if install_sys_file "$SCRIPT_DIR/os-tuning/zramswap" /etc/default/zramswap; then
  log "  installed /etc/default/zramswap"
  zram_changed=true
fi
if systemctl list-unit-files zramswap.service >/dev/null 2>&1; then
  sudo systemctl enable --now zramswap.service >/dev/null 2>&1 || true
  # Only restart when the config actually changed — a restart does swapoff+mkswap+
  # swapon, which forces swapped pages back into RAM (a memory spike this N100 box
  # is tight on). enable --now above is idempotent and safe on every run.
  if $zram_changed; then
    sudo systemctl restart zramswap.service >/dev/null 2>&1 || true
    log "  zramswap.service restarted (config changed)"
  else
    log "  zramswap.service enabled (config unchanged — no restart)"
  fi
else
  log "  NOTE: zramswap.service not found — is zram-tools installed? (packages.sh)"
fi
# mask desktop bloat + disable cups
bash "$SCRIPT_DIR/os-tuning/mask-bloat.sh" "$TARGET_USER"

# =============================================================================
step "7. PBAP contact sync (obex.service)"
if run_user systemctl --user enable --now obex.service >/dev/null 2>&1; then
  log "  obex.service enabled (PBAP contact sync)"
else
  log "  WARNING: could not enable obex.service — is bluez-obexd installed? (packages.sh)"
fi

# =============================================================================
step "Provisioning complete — remaining MANUAL / RUNTIME steps"
cat <<EOF

Automated by this script: PipeWire 1.0.7 + PPAs, native libpw_helper.so,
WirePlumber BT rules (85/87/89/90/41) + bluez.lua patch, PipeWire quantum,
systemd env reconcile + ops units, OS tuning (swappiness/zram/masks/cups),
and obex (PBAP).

Still MANUAL (not automatable — see deploy/provision/README.md):
  * Bluetooth pairing (hci0 music adapter only):
      bluetoothctl -- select 78:20:51:F5:FB:A7
      (put phone in pairing mode) pair / trust / connect
      NEVER pair the same device on hci1 (RotaryPhone voice) — boundary rule #8.
  * Secrets (machine-key-encrypted, not portable): re-enter AcoustID / Spotify /
      Google TTS keys via the System Config page.
  * appsettings.Production.json hardware bindings (Devices.Radio.USBPort etc.) —
      reconcile per README; discover the USB port id from the Devices page.
  * GV bridge (voice.google.com Chrome) is RotaryPhone-owned — provisioned by the
      RotaryPhone repo, NOT here. See README "GV bridge (cross-service)".
  * Kiosk: run deploy/debian-x64/kiosk/setup-kiosk.sh for autologin + browser.
  * Reboot and confirm the recovery chain:
      radio-bt-setup -> radio-api -> radio-audio-verify
EOF
