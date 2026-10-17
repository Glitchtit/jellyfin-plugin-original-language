#!/usr/bin/env bash
# Build/test/package inside the official .NET SDK image (no SDK on the host).
set -euo pipefail
cd "$(dirname "$0")"

NAME="Original Language Metadata"
VERSION="1.0.0.0"
IMAGE="mcr.microsoft.com/dotnet/sdk:10.0"

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
        (cd dist && zip -qr "jellyfin-plugin-original-language_${VERSION}.zip" "${NAME}_${VERSION}")
        rm -rf dist/publish
        echo "Packaged: $out"
        ;;
    *)
        echo "usage: $0 {test|package}" >&2
        exit 1
        ;;
esac
