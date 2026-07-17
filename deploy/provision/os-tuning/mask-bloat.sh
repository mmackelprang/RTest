#!/usr/bin/env bash
# mask-bloat.sh — Free RAM on the Intel N100 by masking desktop background daemons
# that the kiosk never uses, and disabling the print stack.
#
# Captured from the live `radio` box (2026-07-16 IAC audit §3.8):
#   * User-level MASK of GNOME's Evolution data-server factories and Tracker
#     file-indexer (they respawn on demand otherwise and eat ~50-150 MB).
#   * System-level DISABLE of CUPS (no printer on a radio console).
#
# Idempotent + check-before-apply. Safe to re-run.
#
# Run as the kiosk LOGIN user (mmack) with passwordless sudo. If run as root,
# the user-level masks are applied to SUDO_USER's systemd --user instance.
set -uo pipefail

# --- Resolve the target desktop user + their user-bus context ---------------
if [[ $(id -u) -eq 0 ]]; then
  TARGET_USER="${SUDO_USER:-${1:-mmack}}"
else
  TARGET_USER="${1:-$(id -un)}"
fi
TARGET_UID="$(id -u "$TARGET_USER" 2>/dev/null || echo 1000)"

log() { echo "[mask-bloat] $*"; }

# user_ctl: run `systemctl --user ...` in the target user's session, whether we
# are that user already or root invoking on their behalf.
user_ctl() {
  if [[ "$(id -un)" == "$TARGET_USER" ]]; then
    XDG_RUNTIME_DIR="/run/user/$TARGET_UID" systemctl --user "$@"
  else
    sudo -u "$TARGET_USER" XDG_RUNTIME_DIR="/run/user/$TARGET_UID" systemctl --user "$@"
  fi
}

# --- 1. Mask user-level desktop background daemons --------------------------
USER_BLOAT=(
  evolution-addressbook-factory.service
  evolution-calendar-factory.service
  evolution-source-registry.service
  tracker-extract-3.service
  tracker-miner-fs-3.service
  tracker-miner-rss-3.service
)

log "Masking desktop background daemons for user '$TARGET_USER'..."
for unit in "${USER_BLOAT[@]}"; do
  state="$(user_ctl is-enabled "$unit" 2>/dev/null || true)"
  if [[ "$state" == "masked" ]]; then
    log "  $unit already masked"
  else
    if user_ctl mask "$unit" >/dev/null 2>&1; then
      log "  masked $unit"
    else
      log "  WARNING: could not mask $unit (not present on this box?)"
    fi
  fi
done

# --- 2. Disable the print stack (no printer on the console) -----------------
for unit in cups.service cups-browsed.service cups.socket cups.path; do
  # Only act on units that exist; disable is a no-op if already disabled.
  if systemctl list-unit-files "$unit" >/dev/null 2>&1 \
     && systemctl list-unit-files "$unit" 2>/dev/null | grep -q "^$unit"; then
    if [[ "$(systemctl is-enabled "$unit" 2>/dev/null || true)" == "disabled" ]]; then
      log "  $unit already disabled"
    else
      sudo systemctl disable --now "$unit" >/dev/null 2>&1 || true
      log "  disabled $unit"
    fi
  fi
done

log "Done. (Re-login or reboot for user masks to fully release memory.)"
