#!/usr/bin/env bash
# radio-weekly-maintenance.sh — Weekly OS update + reboot (root, via systemd timer, Sun 03:00).
# Post-reboot audio/BT recovery is automatic: radio-bt-setup.service (adapters, sink, bluez.lua
# patch) -> radio-api.service -> radio-audio-verify.service; GV bridge + kiosk relaunch via autostart.
# Validated on 2026-07-16 (kernel 6.17.0-35 -> 6.17.0-40 reboot: audio + services + GV bridge recovered).
set -uo pipefail
LOG=/var/log/radio-weekly-maintenance.log
log() { echo "$(date '+%Y-%m-%d %H:%M:%S') $1" | tee -a "$LOG"; logger -t radio-weekly-maintenance "$1"; }

export DEBIAN_FRONTEND=noninteractive
APT_OPTS=(-y -o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold)

log "=== weekly maintenance start ==="
log "apt-get update"
apt-get update >>"$LOG" 2>&1 || log "WARNING: apt-get update failed (continuing to reboot)"
log "apt-get upgrade (keep existing configs; no package removals)"
apt-get "${APT_OPTS[@]}" upgrade >>"$LOG" 2>&1 || log "WARNING: apt-get upgrade failed (continuing to reboot)"
log "apt-get autoremove"
apt-get -y autoremove --purge >>"$LOG" 2>&1 || true
log "=== maintenance complete — rebooting (recovery is automatic on boot) ==="
sync
systemctl reboot
