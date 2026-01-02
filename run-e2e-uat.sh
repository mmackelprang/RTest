#!/bin/bash
#
# Run E2E UAT Tests
# Usage: ./run-e2e-uat.sh [--phase <number>] [--interactive] [--no-shutdown]
#

set -e

PHASE=0
INTERACTIVE=false
NO_SHUTDOWN=false

# Parse arguments
while [[ $# -gt 0 ]]; do
  case $1 in
    --phase)
      PHASE="$2"
      shift 2
      ;;
    --interactive)
      INTERACTIVE=true
      shift
      ;;
    --no-shutdown)
      NO_SHUTDOWN=true
      shift
      ;;
    *)
      echo "Unknown option: $1"
      exit 1
      ;;
  esac
done

echo "======================================"
echo "Radio Console - E2E UAT Tests"
echo "======================================"
echo ""

# Check if API is running
echo "Checking if Radio API is running..."
if curl -f -s -o /dev/null -m 5 "http://localhost:5000/api/sources"; then
    echo "✓ API is running"
else
    echo "✗ API is not running!"
    echo "Please start the Radio API first:"
    echo "  cd src/Radio.API"
    echo "  dotnet run"
    exit 1
fi

# Check if Web UI is running
echo "Checking if Radio Web UI is running..."
if curl -f -s -o /dev/null -m 5 "http://localhost:5001"; then
    echo "✓ Web UI is running"
else
    echo "✗ Web UI is not running!"
    echo "Please start the Radio Web UI first:"
    echo "  cd src/Radio.Web"
    echo "  dotnet run"
    exit 1
fi

echo ""

# Build UAT tool
echo "Building E2E UAT tool..."
dotnet build tools/Radio.Tools.AudioUAT --configuration Release

# Run tests
echo ""
echo "Running E2E UAT tests..."
echo ""

UAT_ARGS=()

if [ $PHASE -gt 0 ]; then
    UAT_ARGS+=(--phase $PHASE)
fi

if [ "$INTERACTIVE" = true ]; then
    UAT_ARGS+=(--interactive)
fi

dotnet run --project tools/Radio.Tools.AudioUAT --configuration Release -- "${UAT_ARGS[@]}"

TEST_EXIT_CODE=$?

echo ""
echo "======================================"
echo "E2E UAT Tests Complete"
echo "======================================"

# Optionally shutdown application
if [ "$NO_SHUTDOWN" = false ]; then
    echo ""
    echo "Shutting down application..."
    if curl -f -s -o /dev/null -X POST -m 5 "http://localhost:5000/api/system/shutdown"; then
        echo "✓ Shutdown initiated"
    else
        echo "⚠ Could not shutdown application (may already be stopped)"
    fi
fi

exit $TEST_EXIT_CODE
