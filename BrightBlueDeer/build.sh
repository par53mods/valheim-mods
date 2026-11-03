#!/bin/bash
# Tests the colour setting, then builds BrightBlueDeer.dll against the installed
# game and copies it into BepInEx/plugins.
set -euo pipefail
cd "$(dirname "$0")"

GAME_DIR="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
MANAGED="$GAME_DIR/valheim.app/Contents/Resources/Data/Managed"
CORE="$GAME_DIR/BepInEx/core"
PLUGIN_DIR="$GAME_DIR/BepInEx/plugins/BrightBlueDeer"

mkdir -p bin

csc -nologo -target:exe -out:bin/ColorSettingTests.exe ColorSetting.cs test/ColorSettingTests.cs
mono bin/ColorSettingTests.exe

csc -nologo -noconfig -nostdlib+ -target:library -optimize+ -out:bin/BrightBlueDeer.dll \
    -r:"$MANAGED/mscorlib.dll" \
    -r:"$MANAGED/netstandard.dll" \
    -r:"$MANAGED/System.dll" \
    -r:"$MANAGED/System.Core.dll" \
    -r:"$MANAGED/UnityEngine.dll" \
    -r:"$MANAGED/UnityEngine.CoreModule.dll" \
    -r:"$MANAGED/UnityEngine.ParticleSystemModule.dll" \
    -r:"$MANAGED/assembly_valheim.dll" \
    -r:"$CORE/BepInEx.dll" \
    -r:"$CORE/0Harmony.dll" \
    ColorSetting.cs BrightBlueDeer.cs

if pgrep -x Valheim >/dev/null; then
    echo "Valheim is running. Built, but not installed."
    exit 1
fi

mkdir -p "$PLUGIN_DIR"
cp bin/BrightBlueDeer.dll "$PLUGIN_DIR/"
echo "Installed to $PLUGIN_DIR"
