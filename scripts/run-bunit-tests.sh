#!/bin/bash
#
# Run bUnit tests only
# Usage: ./run-bunit-tests.sh
#

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "======================================"
echo "Radio Console - bUnit Tests"
echo "======================================"
echo ""

# Build the solution first
echo "Building solution..."
dotnet build --configuration Release

echo ""
echo "Running bUnit tests..."
echo ""

# Run bUnit tests (Radio.Web.Tests project)
dotnet test tests/Radio.Web.Tests/Radio.Web.Tests.csproj \
    --configuration Release \
    --logger "console;verbosity=normal" \
    --results-directory ./TestResults/bUnit \
    --collect:"XPlat Code Coverage"

echo ""
echo "======================================"
echo "bUnit Tests Complete"
echo "======================================"
