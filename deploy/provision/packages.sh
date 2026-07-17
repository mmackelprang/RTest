#!/usr/bin/env bash
# packages.sh — APT sources (PPAs + Google Chrome) and the package set that
# deploy/debian-x64/setup.sh does NOT install, captured from the live `radio`
# box (IAC audit §3.12).
#
# This is ADDITIVE to setup.sh (which already installs the audio base:
# libasound2-dev, libmp3lame-dev, avahi, bluez, curl/wget/unzip, fpcalc...).
# Run AFTER setup.sh, or standalone. Idempotent + check-before-apply.
#
# Run as the login user with passwordless sudo (or as root). apt operations are
# heavy — NEVER run this against the live production box; it is for a rebuild.
#
# Flags:
#   --with-sdr   also install librtlsdr-dev (rtl-sdr itself is in the base set)
set -euo pipefail

WITH_SDR=false
for arg in "$@"; do
  case "$arg" in
    --with-sdr) WITH_SDR=true ;;
    *) echo "[packages] unknown arg: $arg" >&2 ;;
  esac
done

log() { echo "[packages] $*"; }

# --- 0. Tooling to manage apt sources ---------------------------------------
if ! command -v add-apt-repository >/dev/null 2>&1; then
  log "Installing software-properties-common (for add-apt-repository)..."
  sudo apt-get update
  sudo apt-get install -y software-properties-common
fi

# --- 1. PPAs (idempotent — add-apt-repository skips duplicates) --------------
# PipeWire 1.0.7 upstream — Ubuntu 24.04 ships 1.0.5; the bluez.lua patch and BT
# stability fixes target the 1.0.7 quirk. Music BT audio depends on this.
if ! grep -rqs 'pipewire-debian/pipewire-upstream' /etc/apt/sources.list.d/ 2>/dev/null; then
  log "Adding PPA pipewire-debian/pipewire-upstream..."
  sudo add-apt-repository -y ppa:pipewire-debian/pipewire-upstream
else
  log "PPA pipewire-debian/pipewire-upstream already present"
fi

# SongRec — Shazam song recognition (marin-m/songrec).
if ! grep -rqs 'marin-m/songrec' /etc/apt/sources.list.d/ 2>/dev/null; then
  log "Adding PPA marin-m/songrec..."
  sudo add-apt-repository -y ppa:marin-m/songrec
else
  log "PPA marin-m/songrec already present"
fi

# --- 2. Google Chrome apt source (kiosk + GV bridge) ------------------------
if [[ ! -f /etc/apt/sources.list.d/google-chrome.sources && ! -f /etc/apt/sources.list.d/google-chrome.list ]]; then
  log "Adding Google Chrome apt source..."
  curl -fsSL https://dl.google.com/linux/linux_signing_key.pub \
    | sudo gpg --dearmor -o /usr/share/keyrings/google-chrome.gpg
  sudo tee /etc/apt/sources.list.d/google-chrome.sources >/dev/null <<'EOF'
X-Repolib-Name: Google Chrome
Types: deb
URIs: https://dl.google.com/linux/chrome-stable/deb/
Suites: stable
Components: main
Architectures: amd64
Signed-By: /usr/share/keyrings/google-chrome.gpg
EOF
else
  log "Google Chrome apt source already present"
fi

# --- 3. Refresh + install ----------------------------------------------------
log "apt-get update..."
sudo apt-get update

# PipeWire 1.0.7 suite + BT SPA plugin + dev headers (headers needed to build
# libpw_helper.so — see build-native.sh). Installing from the PPA upgrades the
# stock 1.0.5 to 1.0.7.
PIPEWIRE_PKGS=(pipewire wireplumber libspa-0.2-bluetooth libpipewire-0.3-dev)

# Native-helper build toolchain (P0-2).
BUILD_PKGS=(build-essential pkg-config)

# Feature + ops packages.
FEATURE_PKGS=(
  songrec           # Shazam recognition (PPA)
  bluez-obexd       # PBAP contact sync (obex.service)
  zram-tools        # compressed swap (zramswap.service)
  google-chrome-stable
  onboard           # on-screen keyboard (touchscreen kiosk)
  unclutter         # hide idle cursor
  xdotool           # kiosk browser refresh helper
  rtl-sdr           # RTL-SDR tuner tools
  python3           # BT/audio research harness scripts
)

log "Installing PipeWire 1.0.7 suite + build toolchain + feature packages..."
sudo apt-get install -y "${PIPEWIRE_PKGS[@]}" "${BUILD_PKGS[@]}" "${FEATURE_PKGS[@]}"

if $WITH_SDR; then
  log "Installing SDR dev headers (--with-sdr)..."
  sudo apt-get install -y librtlsdr-dev
fi

log "Package provisioning complete."
log "PipeWire version: $(dpkg-query -W -f='${Version}' pipewire 2>/dev/null || echo '?')"
