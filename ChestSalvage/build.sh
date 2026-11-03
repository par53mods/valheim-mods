#!/bin/bash
# Tests the refund rules, then builds ChestSalvage.dll against the installed game
# and copies it into BepInEx/plugins.
set -euo pipefail
cd "$(dirname "$0")"

GAME_DIR="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
MANAGED="$GAME_DIR/valheim.app/Contents/Resources/Data/Managed"
CORE="$GAME_DIR/BepInEx/core"
PLUGIN_DIR="$GAME_DIR/BepInEx/plugins/ChestSalvage"

mkdir -p bin

csc -nologo -target:exe -out:bin/SalvageMathTests.exe SalvageMath.cs test/SalvageMathTests.cs
mono bin/SalvageMathTests.exe

csc -nologo -noconfig -nostdlib+ -target:library -optimize+ -out:bin/ChestSalvage.dll \
    -r:"$MANAGED/mscorlib.dll" \
    -r:"$MANAGED/netstandard.dll" \
    -r:"$MANAGED/System.dll" \
    -r:"$MANAGED/System.Core.dll" \
    -r:"$MANAGED/UnityEngine.dll" \
    -r:"$MANAGED/UnityEngine.CoreModule.dll" \
    -r:"$MANAGED/UnityEngine.UI.dll" \
    -r:"$MANAGED/UnityEngine.UIModule.dll" \
    -r:"$MANAGED/UnityEngine.PhysicsModule.dll" \
    -r:"$MANAGED/UnityEngine.AnimationModule.dll" \
    -r:"$MANAGED/Unity.TextMeshPro.dll" \
    -r:"$MANAGED/assembly_valheim.dll" \
    -r:"$MANAGED/assembly_guiutils.dll" \
    -r:"$MANAGED/assembly_utils.dll" \
    -r:"$CORE/BepInEx.dll" \
    -r:"$CORE/0Harmony.dll" \
    SalvageMath.cs ChestSalvage.cs

if pgrep -x Valheim >/dev/null; then
    echo "Valheim is running. Built, but not installed."
    exit 1
fi

mkdir -p "$PLUGIN_DIR"
cp bin/ChestSalvage.dll "$PLUGIN_DIR/"
echo "Installed to $PLUGIN_DIR"
