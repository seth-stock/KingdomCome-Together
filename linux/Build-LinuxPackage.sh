#!/usr/bin/env bash
# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#
# Builds release/KingdomComeTogether-Linux-<version>.tar.gz (docs/LINUX.md).
#
#   linux/Build-LinuxPackage.sh [REPO_DIR]
#
# Run it on Linux (or WSL) with the .NET 8 SDK. The agent and the relay are published self-contained for linux-x64, so the player
# needs no .NET. The plugin (KCDMP.dll) and its injector are WINDOWS programs -- they run inside the game's Proton prefix -- and
# cannot be built on Linux: they are taken, already built, from native/build (build them on Windows with native\Build-Native.ps1)
# or from PLUGIN_DIR=/path/with/KCDMP.dll-and-KCDMP_LauncherInjector.exe.
set -euo pipefail
REPO="$(cd "${1:-$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")/..}" && pwd)"
VERSION="$(tr -d '[:space:]' <"$REPO/VERSION")"
NAME="KingdomComeTogether-Linux-$VERSION"
OUT="${OUT_DIR:-$REPO/release}"
WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
PKG="$WORK/$NAME"
mkdir -p "$PKG"/{agent,relay,plugin,mod,docs} "$OUT"

echo "== agent + relay (linux-x64, self-contained)"
dotnet publish "$REPO/dotnet/KcdMp.Client/KcdMp.Client.csproj" -c Release -r linux-x64 --self-contained -o "$PKG/agent" -v q -nologo
dotnet publish "$REPO/dotnet/KcdMp.Server/KcdMp.Server.csproj" -c Release -r linux-x64 --self-contained -o "$PKG/relay" -v q -nologo
chmod +x "$PKG/agent/KcdMpClient" "$PKG/relay/KcdMpServer"

echo "== plugin + injector (Windows binaries, run inside Proton)"
PLUGIN_DIR="${PLUGIN_DIR:-$REPO/native/build}"
DLL="$(find "$PLUGIN_DIR" -name KCDMP.dll | head -n1)"; INJ="$(find "$PLUGIN_DIR" -name KCDMP_LauncherInjector.exe | head -n1)"
[[ -f "$DLL" && -f "$INJ" ]] || { echo "KCDMP.dll / KCDMP_LauncherInjector.exe not found under $PLUGIN_DIR (build them on Windows: native\\Build-Native.ps1)" >&2; exit 1; }
cp "$DLL" "$INJ" "$PKG/plugin/"

echo "== mod + launcher + docs"
cp -r "$REPO/kdcmp" "$PKG/mod/kdcmp"
rm -rf "$PKG/mod/kdcmp/ConfigPatch"      # embedded in the agent; not a mod file
cp "$REPO/linux/kcdmp" "$PKG/kcdmp"; chmod +x "$PKG/kcdmp"
cp "$REPO/VERSION" "$REPO/LICENSE" "$REPO/NOTICE" "$REPO/AUTHORS" "$PKG/"
cp "$REPO/docs/LINUX.md" "$PKG/docs/" 2>/dev/null || true
cat >"$PKG/READ-ME-FIRST.txt" <<EOF
Kingdom Come: Together $VERSION -- Linux build (unofficial, community)

1. Needs: Steam, the retail "Kingdom Come: Deliverance II" and the "Kingdom Come: Deliverance II Modding tools" (Steam > Tools),
   started once from Steam so Proton has made its prefix.
2. ./kcdmp doctor          shows what is missing
3. ./kcdmp link-data       once (and after game updates)
4. ./kcdmp install         (game closed)
5. ./kcdmp play            starts the game under Proton; load a save
6. ./kcdmp inject          then, once you are in the world
7. ./kcdmp host   or   ./kcdmp join HOST[:PORT]

This is NEW and has not been run under a real Proton by its authors: read docs/LINUX.md ("What is and is not tested") first.
Not affiliated with or endorsed by Warhorse Studios or PLAION. GPL-3.0-only; see LICENSE and NOTICE.
EOF
( cd "$PKG" && find . -type f ! -name SHA256SUMS -print0 | sort -z | xargs -0 sha256sum >SHA256SUMS )

echo "== tar"
tar -C "$WORK" --owner=0 --group=0 --sort=name -czf "$OUT/$NAME.tar.gz" "$NAME"
( cd "$OUT" && sha256sum "$NAME.tar.gz" | tee "$NAME.tar.gz.sha256" )
ls -l "$OUT/$NAME.tar.gz"
