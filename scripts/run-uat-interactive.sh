#!/bin/bash
#
# Run UAT Test Tool interactively with real radio hardware
# Usage: ./run-uat-interactive.sh
#
# This runs the Audio User Acceptance Testing (UAT) tool which provides
# an interactive menu for testing various audio subsystems with real hardware.
#

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=========================================="
echo "Radio Console - UAT Test Runner"
echo "=========================================="
echo ""
echo "Interactive User Acceptance Testing Tool"
echo ""
echo "⚠️  IMPORTANT:"
echo "   - This requires real radio hardware (RTL-SDR, RF320, etc.)"
echo "   - Audio output devices must be configured"
echo "   - Tests will produce audible audio output"
echo ""
read -p "Press Enter to continue or Ctrl+C to cancel..."
echo ""

# Build the UAT tool
echo "Building UAT test tool..."
dotnet build tools/Radio.Tools.AudioUAT/Radio.Tools.AudioUAT.csproj --configuration Release

echo ""
echo "Starting UAT Test Runner..."
echo ""

# Run the UAT tool
dotnet run --project tools/Radio.Tools.AudioUAT/Radio.Tools.AudioUAT.csproj --configuration Release
