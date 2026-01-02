#!/bin/bash
#
# Run E2E tests only
# Usage: ./run-e2e-tests.sh
#

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "======================================"
echo "Radio Console - E2E Tests"
echo "======================================"
echo ""

# Build the solution first
echo "Building solution..."
dotnet build --configuration Release

echo ""
echo "Running E2E tests..."
echo ""

# Run E2E tests
dotnet test tests/Radio.Web.E2ETests/Radio.Web.E2ETests.csproj \
    --configuration Release \
    --logger "console;verbosity=normal" \
    --results-directory ./TestResults/E2E \
    --collect:"XPlat Code Coverage"

echo ""
echo "======================================"
echo "E2E Tests Complete"
echo "======================================"
