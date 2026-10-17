#!/usr/bin/env bash
# Build/test/package inside the official .NET SDK image (no SDK on the host).
set -euo pipefail
cd "$(dirname "$0")"

NAME="Original Language Metadata"
VERSION="1.0.0.0"
IMAGE="mcr.microsoft.com/dotnet/sdk:10.0"
ZIP="jellyfin-plugin-original-language_${VERSION}.zip"
REPO_URL="https://github.com/Glitchtit/jellyfin-plugin-original-language"

dotnet() {
    mkdir -p "$HOME/.nuget-docker"
    docker run --rm \
        --user "$(id -u):$(id -g)" \
        -e HOME=/tmp -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
        -e NUGET_PACKAGES=/nuget \
        -v "$HOME/.nuget-docker:/nuget" \
        -v "$PWD:/src" -w /src \
        "$IMAGE" dotnet "$@"
}

case "${1:-}" in
    test)
        shift
        dotnet test Jellyfin.Plugin.OriginalLanguage.sln "$@"
        ;;
    package)
        rm -rf dist
        dotnet publish Jellyfin.Plugin.OriginalLanguage/Jellyfin.Plugin.OriginalLanguage.csproj \
            -c Release -o "dist/publish"
        out="dist/${NAME}_${VERSION}"
        mkdir -p "$out"
        cp dist/publish/Jellyfin.Plugin.OriginalLanguage.dll "$out/"
        cp meta.json "$out/"
        cp assets/logo.png "$out/"
        # Repository installs expect the plugin files at the zip root.
        (cd "$out" && zip -qr "../${ZIP}" .)
        rm -rf dist/publish
        echo "Packaged: $out and dist/${ZIP}"
        ;;
    release)
        # Adds/updates this version in manifest.json (the plugin repository index).
        # Then: commit manifest.json, and upload dist/$ZIP to the GitHub release v$VERSION.
        "$0" package
        checksum=$(md5sum "dist/${ZIP}" | cut -d' ' -f1)
        python3 - "$VERSION" "$checksum" "${REPO_URL}/releases/download/v${VERSION}/${ZIP}" <<'PY'
import json, sys
from datetime import datetime, timezone

version, checksum, source_url = sys.argv[1:4]
meta = json.load(open("meta.json"))
try:
    manifest = json.load(open("manifest.json"))
except FileNotFoundError:
    manifest = []

plugin = next((p for p in manifest if p["guid"] == meta["guid"]), None)
if plugin is None:
    plugin = {"guid": meta["guid"], "versions": []}
    manifest.append(plugin)

plugin.update({
    "name": meta["name"],
    "description": meta["description"],
    "overview": meta["overview"],
    "owner": meta["owner"],
    "category": meta["category"],
    "imageUrl": "https://raw.githubusercontent.com/Glitchtit/jellyfin-plugin-original-language/main/assets/logo.png",
})
plugin["versions"] = [v for v in plugin["versions"] if v["version"] != version]
plugin["versions"].insert(0, {
    "version": version,
    "changelog": meta["changelog"],
    "targetAbi": meta["targetAbi"],
    "sourceUrl": source_url,
    "checksum": checksum,
    "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
})

with open("manifest.json", "w") as f:
    json.dump(manifest, f, indent=2)
    f.write("\n")
print(f"manifest.json: {meta['name']} {version} (md5 {checksum})")
PY
        ;;
    *)
        echo "usage: $0 {test|package|release}" >&2
        exit 1
        ;;
esac
