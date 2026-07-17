#!/usr/bin/env bash
# build-native.sh — Build + install the PipeWire native capture helper
# (libpw_helper.so). Radio.API's `radio-bt-stream` P/Invokes this SPA-pod
# builder to capture BT A2DP audio; without it there is NO BT music capture
# (IAC audit §3.7 / gap P0-2).
#
# Install target is /usr/local/lib + ldconfig — NOT /opt/radio-console/api,
# which every app deploy wipes. The ldconfig cache survives deploys.
#
# Requires build deps from packages.sh: build-essential pkg-config
# libpipewire-0.3-dev. Run as the login user with sudo, or as root.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
NATIVE_DIR="$REPO_ROOT/src/Radio.Infrastructure/Platform/Bluetooth/Native"
log() { echo "[build-native] $*"; }

# --- Preflight: build deps ---------------------------------------------------
for tool in gcc pkg-config; do
  command -v "$tool" >/dev/null 2>&1 \
    || { log "ERROR: '$tool' not found — run deploy/provision/packages.sh first"; exit 1; }
done
pkg-config --exists libpipewire-0.3 \
  || { log "ERROR: libpipewire-0.3-dev not found — run packages.sh first"; exit 1; }

if [[ ! -f "$NATIVE_DIR/pw_helper.c" ]]; then
  log "ERROR: $NATIVE_DIR/pw_helper.c not found"; exit 1
fi

# --- Build -------------------------------------------------------------------
log "Building libpw_helper.so from $NATIVE_DIR ..."
( cd "$NATIVE_DIR" && bash build-pw-helper.sh )

# --- Install to /usr/local/lib + refresh ldconfig cache ----------------------
log "Installing libpw_helper.so -> /usr/local/lib (survives app deploys)..."
sudo install -m 0755 "$NATIVE_DIR/libpw_helper.so" /usr/local/lib/libpw_helper.so
sudo ldconfig

if ldconfig -p 2>/dev/null | grep -q 'libpw_helper\.so'; then
  log "OK: libpw_helper.so present in ldconfig cache"
else
  log "WARNING: libpw_helper.so not visible in ldconfig cache — check the build"
fi
