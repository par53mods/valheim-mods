#!/bin/bash
# Builds DudeWheresMySpear.dll against the installed game and copies it into
# BepInEx/plugins.
set -euo pipefail
cd "$(dirname "$0")"

GAME_DIR="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
MANAGED="$GAME_DIR/valheim.app/Contents/Resources/Data/Managed"
CORE="$GAME_DIR/BepInEx/core"
PLUGIN_DIR="$GAME_DIR/BepInEx/plugins/DudeWheresMySpear"

mkdir -p bin

csc -nologo -noconfig -nostdlib+ -target:library -optimize+ -out:bin/DudeWheresMySpear.dll \
    -r:"$MANAGED/mscorlib.dll" \
    -r:"$MANAGED/netstandard.dll" \
    -r:"$MANAGED/System.dll" \
    -r:"$MANAGED/System.Core.dll" \
    -r:"$MANAGED/UnityEngine.dll" \
    -r:"$MANAGED/UnityEngine.CoreModule.dll" \
    -r:"$MANAGED/UnityEngine.ParticleSystemModule.dll" \
    -r:"$MANAGED/assembly_valheim.dll" \
    -r:"$MANAGED/assembly_utils.dll" \
    -r:"$CORE/BepInEx.dll" \
    -r:"$CORE/0Harmony.dll" \
    DudeWheresMySpear.cs

if pgrep -x Valheim >/dev/null; then
    echo "Valheim is running. Built, but not installed."
    exit 1
fi

mkdir -p "$PLUGIN_DIR"
cp bin/DudeWheresMySpear.dll "$PLUGIN_DIR/"
echo "Installed to $PLUGIN_DIR"
