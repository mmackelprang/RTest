#!/usr/bin/env bash
# Build the PipeWire helper shared library.
# Run on the target Linux host (Ubuntu/Pi) where libpipewire-0.3-dev is installed.
#
# This script only BUILDS libpw_helper.so. Install it to /usr/local/lib +
# ldconfig (NOT /opt/radio-console/api, which every app deploy wipes) — the
# ldconfig cache survives deploys. deploy/provision/build-native.sh does the
# build + install in one step; prefer that on a rebuild.
#
# Usage:
#   ./build-pw-helper.sh
#   sudo install -m755 libpw_helper.so /usr/local/lib/ && sudo ldconfig

set -euo pipefail
cd "$(dirname "$0")"

echo "Building libpw_helper.so..."
gcc -shared -fPIC -O2 -o libpw_helper.so pw_helper.c \
    $(pkg-config --cflags --libs libpipewire-0.3)

echo "Built: $(ls -la libpw_helper.so)"
echo "Install (survives deploys): sudo install -m755 libpw_helper.so /usr/local/lib/ && sudo ldconfig"
