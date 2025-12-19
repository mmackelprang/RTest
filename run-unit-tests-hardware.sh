#!/bin/bash
#
# Run xUnit tests with real radio hardware
# Usage: ./run-unit-tests-hardware.sh
#
# IMPORTANT: This requires real radio hardware to be connected.
# Tests will interact with actual USB radio devices (RTL-SDR, RF320).
#

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "======================================"
echo "Radio Console - xUnit Tests with Hardware"
echo "======================================"
echo ""
echo "⚠️  WARNING: This will interact with real radio hardware!"
echo "   Make sure your USB radio devices are connected."
echo ""
read -p "Press Enter to continue or Ctrl+C to cancel..."
echo ""

# Build the solution first
echo "Building solution..."
dotnet build --configuration Release

echo ""
echo "Running xUnit tests (excluding E2E and bUnit tests)..."
echo ""

# Run all xUnit test projects except E2E and bUnit (Web.Tests)
dotnet test \
    --configuration Release \
    --logger "console;verbosity=normal" \
    --results-directory ./TestResults/xUnit \
    --collect:"XPlat Code Coverage" \
    --filter "FullyQualifiedName!~Radio.Web.E2ETests&FullyQualifiedName!~Radio.Web.Tests"

echo ""
echo "======================================"
echo "xUnit Tests Complete"
echo "======================================"
