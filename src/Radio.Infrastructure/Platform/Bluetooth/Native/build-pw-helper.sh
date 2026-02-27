#!/usr/bin/env bash
# Build the PipeWire helper shared library.
# Run on the target Linux host (Ubuntu/Pi) where libpipewire-dev is installed.
#
# Usage:
#   ./build-pw-helper.sh
#   cp libpw_helper.so /opt/radio-console/api/

set -euo pipefail
cd "$(dirname "$0")"

echo "Building libpw_helper.so..."
gcc -shared -fPIC -O2 -o libpw_helper.so pw_helper.c \
    $(pkg-config --cflags --libs libpipewire-0.3)

echo "Built: $(ls -la libpw_helper.so)"
echo "Copy to deployment: cp libpw_helper.so /opt/radio-console/api/"
