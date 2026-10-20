#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
package_dir="${1:?Usage: prepare.sh OUTPUT_DIRECTORY [linux-x64|linux-arm64]}"
package_rid="${2:-linux-x64}"
case "$package_rid" in linux-x64|linux-arm64) ;; *) echo 'Unsupported Linux architecture' >&2; exit 2;; esac
mkdir -p "$package_dir"
package_dir="$(cd "$package_dir" && pwd)"
# Fail instead of merging stale binaries from a previous architecture/version.
if [ -e "$package_dir/publish" ]; then echo 'Use a fresh output directory' >&2; exit 2; fi
cd "$repo_root"
dotnet publish BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop -c Release -r "$package_rid" \
  --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o "$package_dir/publish"
cp packaging/flatpak/*.desktop packaging/flatpak/*.xml "$package_dir/"
cp BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui/Resources/AppIcon/appicon.svg "$package_dir/icon.svg"
cat > "$package_dir/bru-avtopark" <<'LAUNCHER'
#!/bin/sh
exec /app/lib/bru-avtopark/BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop "$@"
LAUNCHER
chmod +x "$package_dir/bru-avtopark"
cat > "$package_dir/com.bru.avtopark.ticketsales.json" <<'MANIFEST'
{
  "app-id": "com.bru.avtopark.ticketsales",
  "runtime": "org.gnome.Platform",
  "runtime-version": "49",
  "sdk": "org.gnome.Sdk",
  "command": "bru-avtopark",
  "finish-args": ["--share=network", "--socket=wayland", "--socket=fallback-x11", "--share=ipc", "--device=dri"],
  "modules": [{
    "name": "bru-avtopark", "buildsystem": "simple",
    "build-commands": [
      "mkdir -p /app/lib/bru-avtopark",
      "cp -a publish/. /app/lib/bru-avtopark/",
      "install -Dm755 bru-avtopark /app/bin/bru-avtopark",
      "install -Dm644 com.bru.avtopark.ticketsales.desktop /app/share/applications/com.bru.avtopark.ticketsales.desktop",
      "install -Dm644 com.bru.avtopark.ticketsales.metainfo.xml /app/share/metainfo/com.bru.avtopark.ticketsales.metainfo.xml",
      "install -Dm644 icon.svg /app/share/icons/hicolor/scalable/apps/com.bru.avtopark.ticketsales.svg"
    ],
    "sources": [{"type": "dir", "path": "publish", "dest": "publish"},
      {"type": "file", "path": "bru-avtopark"},
      {"type": "file", "path": "icon.svg"},
      {"type": "file", "path": "com.bru.avtopark.ticketsales.desktop"},
      {"type": "file", "path": "com.bru.avtopark.ticketsales.metainfo.xml"}]
  }]
}
MANIFEST
