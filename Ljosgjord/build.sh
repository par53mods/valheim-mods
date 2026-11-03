#!/bin/bash
# Builds Ljosgjord.dll against the installed game and copies it into BepInEx/plugins.
set -euo pipefail
cd "$(dirname "$0")"

GAME_DIR="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
MANAGED="$GAME_DIR/valheim.app/Contents/Resources/Data/Managed"
CORE="$GAME_DIR/BepInEx/core"
PLUGIN_DIR="$GAME_DIR/BepInEx/plugins/Ljosgjord"

mkdir -p bin

csc -nologo -noconfig -nostdlib+ -target:library -optimize+ -out:bin/Ljosgjord.dll \
    -r:"$MANAGED/mscorlib.dll" \
    -r:"$MANAGED/netstandard.dll" \
    -r:"$MANAGED/System.dll" \
    -r:"$MANAGED/System.Core.dll" \
    -r:"$MANAGED/UnityEngine.dll" \
    -r:"$MANAGED/UnityEngine.CoreModule.dll" \
    -r:"$MANAGED/assembly_valheim.dll" \
    -r:"$CORE/BepInEx.dll" \
    Ljosgjord.cs

if pgrep -x Valheim >/dev/null; then
    echo "Valheim is running. Built, but not installed."
    exit 1
fi

mkdir -p "$PLUGIN_DIR"
cp bin/Ljosgjord.dll "$PLUGIN_DIR/"
echo "Installed to $PLUGIN_DIR"
