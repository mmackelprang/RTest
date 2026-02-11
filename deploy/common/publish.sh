#!/bin/bash
# publish.sh — Cross-compile helper for Radio Console
# Builds self-contained deployments for target platforms from any dev box.
#
# Usage:
#   ./publish.sh arm64    # Raspberry Pi (linux-arm64)
#   ./publish.sh x64      # Debian x64 (linux-x64)
#   ./publish.sh all      # Both platforms

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
OUTPUT_BASE="$REPO_ROOT/publish"

publish_for_rid() {
  local RID="$1"
  local OUTPUT="$OUTPUT_BASE/$RID"

  echo "========================================="
  echo "Publishing for $RID..."
  echo "========================================="

  rm -rf "$OUTPUT"

  dotnet publish "$REPO_ROOT/src/Radio.API/Radio.API.csproj" \
    --configuration Release \
    --runtime "$RID" \
    --self-contained true \
    --output "$OUTPUT" \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -p:IncludeNativeLibrariesForSelfExtract=true

  # Copy tools
  if [ -d "$REPO_ROOT/tools/fpcalc" ]; then
    mkdir -p "$OUTPUT/tools/fpcalc"
    cp -r "$REPO_ROOT/tools/fpcalc/"* "$OUTPUT/tools/fpcalc/" 2>/dev/null || true
  fi

  # Copy deploy scripts
  mkdir -p "$OUTPUT/deploy"
  cp "$SCRIPT_DIR/radio-console.service" "$OUTPUT/deploy/"
  cp "$SCRIPT_DIR/../DEPLOYMENT.md" "$OUTPUT/deploy/" 2>/dev/null || true

  # Create data directories
  mkdir -p "$OUTPUT/data/config" "$OUTPUT/data/metrics" "$OUTPUT/data/fingerprints" \
           "$OUTPUT/data/secrets" "$OUTPUT/data/albumart" "$OUTPUT/data/backups" \
           "$OUTPUT/logs"

  # Copy default appsettings
  if [ ! -f "$OUTPUT/appsettings.Production.json" ]; then
    cat > "$OUTPUT/appsettings.Production.json" << 'APPSETTINGS'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5000"
      }
    }
  }
}
APPSETTINGS
  fi

  local SIZE=$(du -sh "$OUTPUT" | cut -f1)
  echo ""
  echo "Published $RID to $OUTPUT ($SIZE)"
  echo ""
}

case "${1:-}" in
  arm64|linux-arm64|rpi|pi)
    publish_for_rid "linux-arm64"
    ;;
  x64|linux-x64|amd64)
    publish_for_rid "linux-x64"
    ;;
  all)
    publish_for_rid "linux-arm64"
    publish_for_rid "linux-x64"
    ;;
  *)
    echo "Usage: $0 {arm64|x64|all}"
    echo ""
    echo "  arm64  - Raspberry Pi (linux-arm64)"
    echo "  x64    - Debian/Ubuntu x64 (linux-x64)"
    echo "  all    - Both platforms"
    exit 1
    ;;
esac

echo "Done."
