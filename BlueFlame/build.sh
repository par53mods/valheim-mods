#!/bin/bash
# Builds BlueFlame.dll against the installed game and copies it into
# BepInEx/plugins.
set -euo pipefail
cd "$(dirname "$0")"

GAME_DIR="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
MANAGED="$GAME_DIR/valheim.app/Contents/Resources/Data/Managed"
CORE="$GAME_DIR/BepInEx/core"
PLUGIN_DIR="$GAME_DIR/BepInEx/plugins/BlueFlame"

mkdir -p bin

csc -nologo -noconfig -nostdlib+ -target:library -optimize+ -out:bin/BlueFlame.dll \
    -r:"$MANAGED/mscorlib.dll" \
    -r:"$MANAGED/netstandard.dll" \
    -r:"$MANAGED/System.dll" \
    -r:"$MANAGED/System.Core.dll" \
    -r:"$MANAGED/UnityEngine.dll" \
    -r:"$MANAGED/UnityEngine.CoreModule.dll" \
    -r:"$MANAGED/UnityEngine.ParticleSystemModule.dll" \
    -r:"$MANAGED/UnityEngine.UI.dll" \
    -r:"$MANAGED/UnityEngine.UIModule.dll" \
    -r:"$MANAGED/Unity.TextMeshPro.dll" \
    -r:"$MANAGED/assembly_valheim.dll" \
    -r:"$MANAGED/assembly_guiutils.dll" \
    -r:"$MANAGED/assembly_utils.dll" \
    -r:"$CORE/BepInEx.dll" \
    -r:"$CORE/0Harmony.dll" \
    BlueFlame.cs

if pgrep -x Valheim >/dev/null; then
    echo "Valheim is running. Built, but not installed."
    exit 1
fi

mkdir -p "$PLUGIN_DIR"
cp bin/BlueFlame.dll "$PLUGIN_DIR/"
echo "Installed to $PLUGIN_DIR"
