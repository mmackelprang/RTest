#!/bin/bash
#
# Run all test suites
# Usage: ./run-all-tests.sh
#
# This runs:
# 1. bUnit tests (UI component tests)
# 2. xUnit tests (unit tests - may require hardware)
# 3. E2E tests (end-to-end browser tests)
#

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=========================================="
echo "Radio Console - All Test Suites"
echo "=========================================="
echo ""
echo "This will run all test suites:"
echo "  1. bUnit tests (Blazor component tests)"
echo "  2. xUnit tests (unit tests with hardware)"
echo "  3. E2E tests (browser-based tests)"
echo ""
echo "⚠️  NOTE: xUnit tests may require real radio hardware!"
echo ""
read -p "Press Enter to continue or Ctrl+C to cancel..."
echo ""

# Build the solution first
echo "Building solution..."
dotnet build --configuration Release

echo ""
echo "=========================================="
echo "Step 1/3: Running bUnit Tests"
echo "=========================================="
echo ""

dotnet test tests/Radio.Web.Tests/Radio.Web.Tests.csproj \
    --configuration Release \
    --logger "console;verbosity=normal" \
    --results-directory ./TestResults/bUnit \
    --collect:"XPlat Code Coverage"

echo ""
echo "=========================================="
echo "Step 2/3: Running xUnit Tests"
echo "=========================================="
echo ""

dotnet test \
    --configuration Release \
    --logger "console;verbosity=normal" \
    --results-directory ./TestResults/xUnit \
    --collect:"XPlat Code Coverage" \
    --filter "FullyQualifiedName!~Radio.Web.E2ETests&FullyQualifiedName!~Radio.Web.Tests"

echo ""
echo "=========================================="
echo "Step 3/3: Running E2E Tests"
echo "=========================================="
echo ""

dotnet test tests/Radio.Web.E2ETests/Radio.Web.E2ETests.csproj \
    --configuration Release \
    --logger "console;verbosity=normal" \
    --results-directory ./TestResults/E2E \
    --collect:"XPlat Code Coverage"

echo ""
echo "=========================================="
echo "All Test Suites Complete"
echo "=========================================="
echo ""
echo "Test results saved to ./TestResults/"
