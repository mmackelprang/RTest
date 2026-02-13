#!/bin/bash
# publish.sh — Cross-compile helper for Radio Console
# Builds self-contained deployments of both Radio.API and Radio.Web for target platforms.
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

  # Publish Radio.API
  echo ""
  echo "--- Radio.API ---"
  dotnet publish "$REPO_ROOT/src/Radio.API/Radio.API.csproj" \
    --configuration Release \
    --runtime "$RID" \
    -f net8.0 \
    --self-contained true \
    --output "$OUTPUT/api" \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -p:IncludeNativeLibrariesForSelfExtract=true

  # Publish Radio.Web
  echo ""
  echo "--- Radio.Web ---"
  dotnet publish "$REPO_ROOT/src/Radio.Web/Radio.Web.csproj" \
    --configuration Release \
    --runtime "$RID" \
    -f net8.0 \
    --self-contained true \
    --output "$OUTPUT/web" \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -p:IncludeNativeLibrariesForSelfExtract=true

  # Copy tools
  if [ -d "$REPO_ROOT/tools/fpcalc" ]; then
    mkdir -p "$OUTPUT/api/tools/fpcalc"
    cp -r "$REPO_ROOT/tools/fpcalc/"* "$OUTPUT/api/tools/fpcalc/" 2>/dev/null || true
  fi

  # Copy deploy scripts
  mkdir -p "$OUTPUT/deploy"
  cp "$SCRIPT_DIR/radio-api.service" "$OUTPUT/deploy/"
  cp "$SCRIPT_DIR/radio-web.service" "$OUTPUT/deploy/"
  cp "$SCRIPT_DIR/../DEPLOYMENT.md" "$OUTPUT/deploy/" 2>/dev/null || true

  # Create shared data directories
  mkdir -p "$OUTPUT/data/config" "$OUTPUT/data/metrics" "$OUTPUT/data/fingerprints" \
           "$OUTPUT/data/secrets" "$OUTPUT/data/albumart" "$OUTPUT/data/backups" \
           "$OUTPUT/logs"

  # Create default appsettings.Production.json for API
  if [ ! -f "$OUTPUT/api/appsettings.Production.json" ]; then
    cat > "$OUTPUT/api/appsettings.Production.json" << 'APPSETTINGS'
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

  # Create default appsettings.Production.json for Web
  if [ ! -f "$OUTPUT/web/appsettings.Production.json" ]; then
    cat > "$OUTPUT/web/appsettings.Production.json" << 'APPSETTINGS'
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
        "Url": "http://0.0.0.0:5002"
      }
    }
  },
  "ApiBaseUrl": "http://localhost:5000"
}
APPSETTINGS
  fi

  local API_SIZE=$(du -sh "$OUTPUT/api" | cut -f1)
  local WEB_SIZE=$(du -sh "$OUTPUT/web" | cut -f1)
  echo ""
  echo "Published $RID — API: $API_SIZE, Web: $WEB_SIZE"
  echo "  Output: $OUTPUT"
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
