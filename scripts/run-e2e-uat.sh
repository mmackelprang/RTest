#!/bin/bash
#
# Run E2E UAT Tests
# Usage: ./run-e2e-uat.sh [--phase <number>] [--interactive] [--output <file>] [--no-shutdown]
#

# Note: Do not use `set -e`; this script handles errors explicitly so that
# non-critical failures (e.g., best-effort shutdown requests) do not override
# the actual test exit code.

PHASE=0
INTERACTIVE=false
NO_SHUTDOWN=false
OUTPUT_FILE=""

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
    --output)
      OUTPUT_FILE="$2"
      shift 2
      ;;
    --no-shutdown)
      NO_SHUTDOWN=true
      shift
      ;;
    *)
      echo "Unknown option: $1"
      echo "Usage: ./run-e2e-uat.sh [--phase <number>] [--interactive] [--output <file>] [--no-shutdown]"
      exit 3
      ;;
  esac
done

echo "======================================"
echo "Radio Console - E2E UAT Tests"
echo "======================================"
echo ""

# Check if API is running
echo "Checking if Radio API is running..."
if curl -f -s -o /dev/null -m 5 "http://localhost:5000/api/sources" 2>/dev/null; then
    echo "✓ API is running"
else
    echo "✗ API is not running!"
    echo "Please start the Radio API first:"
    echo "  cd src/Radio.API"
    echo "  dotnet run"
    exit 2
fi

# Check if Web UI is running
echo "Checking if Radio Web UI is running..."
if curl -f -s -o /dev/null -m 5 "http://localhost:5001" 2>/dev/null; then
    echo "✓ Web UI is running"
else
    echo "✗ Web UI is not running!"
    echo "Please start the Radio Web UI first:"
    echo "  cd src/Radio.Web"
    echo "  dotnet run"
    exit 2
fi

echo ""

# Build UAT tool
echo "Building E2E UAT tool..."
dotnet build tools/Radio.Tools.AudioUAT --configuration Release --nologo -v q

if [ $? -ne 0 ]; then
    echo "✗ Build failed!"
    exit 3
fi

# Run tests
echo ""
if [ -n "$OUTPUT_FILE" ]; then
    echo "Running E2E UAT tests (output to $OUTPUT_FILE)..."
else
    echo "Running E2E UAT tests..."
fi
echo ""

UAT_ARGS=()

if [ $PHASE -gt 0 ]; then
    UAT_ARGS+=(--phase $PHASE)
else
    # Default to all phases if none specified
    UAT_ARGS+=(--all)
fi

if [ "$INTERACTIVE" = true ]; then
    UAT_ARGS+=(--interactive)
fi

if [ -n "$OUTPUT_FILE" ]; then
    UAT_ARGS+=(--output "$OUTPUT_FILE")
fi

dotnet run --project tools/Radio.Tools.AudioUAT --configuration Release --no-build -- "${UAT_ARGS[@]}"

TEST_EXIT_CODE=$?

echo ""
echo "======================================"
if [ $TEST_EXIT_CODE -eq 0 ]; then
    echo "E2E UAT Tests Complete - ALL PASSED"
else
    echo "E2E UAT Tests Complete - SOME FAILED"
fi
echo "======================================"

# Optionally shutdown application
if [ "$NO_SHUTDOWN" = false ]; then
    echo ""
    echo "Shutting down application..."
    if curl -f -s -o /dev/null -X POST -m 5 "http://localhost:5000/api/system/shutdown" 2>/dev/null; then
        echo "✓ Shutdown initiated"
    else
        echo "⚠ Could not shutdown application (may already be stopped)"
    fi
fi

exit $TEST_EXIT_CODE
