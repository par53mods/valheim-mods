#!/usr/bin/env python3
"""Packages the local mods as Thunderstore-style zips for Import local mod."""
import json
import struct
import zipfile
import zlib
from pathlib import Path

AUTHOR = "parallel53"
BEPINEX_PACK = "denikson-BepInExPack_Valheim-5.4.2360"
REPO = "https://github.com/par53mods/valheim-mods"
ROOT = Path(__file__).resolve().parent
OUTPUT = Path.home() / "Downloads"
ICON_SIZE = 256

# Each icon is a 16x16 drawing: "#" takes the mod's ink colour, "." its background.
MODS = [
    {
        "name": "ChestSalvage",
        "folder": "ChestSalvage",
        "version": "1.0.2",
        "description": "Regret that pile of old troll leather? A Salvage button on chests breaks the gear inside back into materials, shows the haul first and asks before anything is destroyed.",
        "readme": "Adds a **Salvage** button to the chest panel. It counts the gear that qualifies, lists the materials you'd get back and asks before anything is broken down. By default you get back half the crafting cost and all of the upgrade costs, rounded up, and an item is only salvaged while you're near the kind of crafting station its recipe uses.\n\nSettings are in `com.parallel53.chestsalvage.cfg`: `BaseReturnRate`, `UpgradeReturnRate` and `RequireStation`.",
        "background": (120, 72, 28),
        "ink": (250, 205, 110),
        "icon": [
            "................",
            "................",
            "..############..",
            ".##############.",
            ".##..........##.",
            ".##############.",
            ".##############.",
            "................",
            ".######..######.",
            ".######..######.",
            ".######..######.",
            ".##############.",
            ".##############.",
            ".##############.",
            "................",
            "................",
        ],
    },
    {
        "name": "BrightBlueDeer",
        "folder": "BrightBlueDeer",
        "version": "1.0.2",
        "description": "Odin's paint bucket: deer turn electric blue, crows gold, boars pink, bears bright green, wolves turquoise and necks violet. Only you see the makeover; every colour is yours to pick.",
        "readme": "Paints deer (alive or dead, plus deer hides and the deer rug), crows, boars, bears, wolves and necks, young and corpses included, along with leather scraps, wolf pelts, bear hides, neck tails and the wolf and bear rugs. Only your game changes; other players see them as usual.\n\nSettings are in `com.parallel53.brightbluedeer.cfg`: `Enabled`, `Color` for deer, `BirdColor` for crows, `BoarColor`, `BearColor`, `WolfColor` and `NeckColor`, each as red, green, blue from 0 to 255. Restart the game after changing them.",
        "background": (12, 60, 140),
        "ink": (110, 190, 255),
        "icon": [
            "................",
            "..#..#....#..#..",
            "..#..#....#..#..",
            "..####....####..",
            "....#......#....",
            "....##....##....",
            ".....######.....",
            "....########....",
            "....#.####.#....",
            "....########....",
            ".....######.....",
            ".....######.....",
            "......####......",
            "......####......",
            "................",
            "................",
        ],
    },
    {
        "name": "QuickStackStore",
        "folder": "QuickStackStore",
        "version": "1.0.4",
        "description": "One button sends your materials into nearby chests that already hold them, and names anything that didn't fit. Cooked food, meads, equipped gear and your hotbar stay with you by default.",
        "readme": "Click **Quick Stack** on the inventory screen to move stackable items into nearby chests that already hold them. It then says what went in and which items had no room.\n\nEquipped items and single items such as gear always stay with you. By default so do cooked food, meads and your hotbar.\n\nSettings are in `com.parallel53.quickstackstore.cfg`:\n\n- `Radius`: how far away a chest can be, in metres (default 10)\n- `KeepHotbar`: leave your hotbar alone (default on)\n- `StackMaterials`: stack materials such as wood, ore and hides (default on)\n- `StackRawFood`: stack food eaten as found, such as berries, mushrooms and honey (default on)\n- `StackCookedFood`: stack food made at a cooking station, oven or cauldron (default off)\n- `StackMeads`: stack meads and other drinks (default off)\n\nOther stackables, such as arrows and trophies, are always stacked.",
        "background": (70, 40, 110),
        "ink": (215, 180, 255),
        "icon": [
            "................",
            "......####......",
            "......####......",
            "......####......",
            "...##########...",
            "....########....",
            ".....######.....",
            "......####......",
            ".......##.......",
            "................",
            ".##############.",
            ".##############.",
            ".##..........##.",
            ".##############.",
            ".##############.",
            "................",
        ],
    },
    {
        "name": "CriticalPath",
        "folder": "CriticalPath",
        "version": "0.1.5",
        "description": "A pocket strategist for the inventory screen: suggests the next tool, food, armor, mead, weapon or offering to work towards for the next boss. Finding the boss is still up to you.",
        "readme": "Shows a panel under the inventory suggesting what to work on next:\n\n- Tools that open up new materials, such as the bronze axe for fine wood.\n- New food worth making, labelled health, stamina or eitr.\n- For the next boss: armor up to a level the mod sets for that boss, the resistance it calls for (mead or gear), weapons or arrows that hit its weaknesses, a shield where it helps, and the summoning offering once your armor is up to it.\n- Once you're ready, it reminds you to find the boss's altar and says which kind of Vegvisir to look for. It never marks the altar or reveals where it is.\n\nStations, upgrades and materials appear only as steps towards a goal. It reads your character's progress, so it works mid-game.\n\nSettings are in `com.parallel53.criticalpath.cfg`: `Suggestions` and `Notify`.",
        "background": (96, 30, 30),
        "ink": (255, 190, 90),
        "icon": [
            "................",
            ".##.............",
            ".##.............",
            ".######.........",
            ".######.........",
            ".....##.........",
            ".....##.........",
            ".....######.....",
            ".....######.....",
            ".........##.....",
            ".........##.....",
            ".........#####..",
            ".........######.",
            ".........#####..",
            "................",
            "................",
        ],
    },
    {
        "name": "Ljosgjord",
        "folder": "Ljosgjord",
        "version": "1.0.1",
        "description": "Why carry a torch when your belt can glow? Wearing Megingjord gives you the Dverger Circlet's light, so your head is free for a proper helmet.",
        "readme": "Ljósgjörð (\"light-belt\"): wearing the Megingjord gives you the Dverger Circlet's light, so you can wear a real helmet and still see in the dark. Only you see the light.",
        "background": (60, 50, 20),
        "ink": (255, 225, 120),
        "icon": [
            "................",
            ".......##.......",
            "...#...##...#...",
            "....#......#....",
            "......####......",
            ".....######.....",
            "##...######...##",
            ".....######.....",
            "......####......",
            "....#......#....",
            "...#...##...#...",
            ".......##.......",
            "................",
            ".##############.",
            ".##############.",
            "................",
        ],
    },
    {
        "name": "MenuModList",
        "folder": "MenuModList",
        "version": "1.0.1",
        "description": "Shows every loaded mod and the BepInEx version on the main menu, so you know what's running before you sail off.",
        "readme": "Shows a panel on the main menu listing every loaded mod, this one included, with its version and the BepInEx version. The panel is never taller than half the screen; a longer list scrolls.",
        "background": (32, 74, 58),
        "ink": (170, 235, 190),
        "icon": [
            "................",
            "................",
            "..##..########..",
            "..##..########..",
            "................",
            "................",
            "..##..########..",
            "..##..########..",
            "................",
            "................",
            "..##..########..",
            "..##..########..",
            "................",
            "................",
            "................",
            "................",
        ],
    },
    {
        "name": "SpeedBoat",
        "folder": "SpeedBoat",
        "version": "0.1.1",
        "description": "Full sail not full enough? Two extra sail speeds above Full, a fast reverse for docking in a hurry, and rowing speed you can tune. A label by the speed indicator shows the level.",
        "readme": "Press forward at Full sail for **+1** and again for **+2**: by default twice and three times Full's sail force. "
                  "Press back at reverse for **fast** reverse: by default twice the reverse rowing force. A label beside the ship's "
                  "speed indicator shows which level you're at, and stopping clears it. The extra speeds still "
                  "depend on the wind like normal sailing.\n\n"
                  "Rowing forward and in reverse stay at the game's speed unless you change them.\n\n"
                  "Settings are in `com.parallel53.speedboat.cfg`: `FullPlusOne`, `FullPlusTwo`, `FastReverse`, "
                  "`RowForward` and `RowBackward`, each a multiple of the game's force. "
                  "The person at the helm needs the mod; other players don't.",
        "background": (10, 50, 80),
        "ink": (120, 220, 255),
        "icon": [
            "................",
            ".......#........",
            ".......##.......",
            ".......###......",
            ".......####.....",
            ".......#####....",
            ".......######...",
            ".......#........",
            "..############..",
            "...##########...",
            "....########....",
            "................",
            ".##..##..##..##.",
            "#..##..##..##..#",
            "................",
            "................",
        ],
    },
    {
        "name": "DudeWheresMySpear",
        "folder": "DudeWheresMySpear",
        "version": "0.1.2",
        "description": "Dropped your spear in the tall grass again? Makes the sparkles on dropped items brighter, more plentiful and wider spread, so loot is easier to spot.",
        "readme": "Items lying on the ground give off a few faint sparkles. By default this mod doubles how many there are and "
                  "how long they last, makes them 1.5 times as big and as bright, spreads them 1.5 times as wide, and halves how fast "
                  "they drift, so they rise about as high as the game's. Only your "
                  "game changes; other players see the usual sparkles.\n\n"
                  "Settings are in `com.parallel53.dudewheresmyspear.cfg`: `Count` (2), `Size` (1.5), `Lifetime` (2), "
                  "`Intensity` (1.5), `Spread` (1.5) and `Drift` (0.5), each a multiple of the game's. Changes take effect as soon as "
                  "you save the file, even mid-game, including on items already on the ground.",
        "background": (60, 40, 10),
        "ink": (255, 220, 90),
        "icon": [
            "................",
            ".......#........",
            ".......#........",
            "......###.......",
            "..#.#######.#...",
            "......###.......",
            ".......#........",
            "...#...#....#...",
            "..###......###..",
            "...#........#...",
            "................",
            ".....######.....",
            "....########....",
            "...##########...",
            "................",
            "................",
        ],
    },
]


def png_chunk(kind, data):
    return (struct.pack(">I", len(data)) + kind + data +
            struct.pack(">I", zlib.crc32(kind + data)))


def icon_png(mod):
    scale = ICON_SIZE // len(mod["icon"])
    rows = bytearray()
    for line in mod["icon"]:
        row = bytearray([0])
        for cell in line:
            row += bytes(mod["ink"] if cell == "#" else mod["background"]) * scale
        rows += bytes(row) * scale

    header = struct.pack(">IIBBBBB", ICON_SIZE, ICON_SIZE, 8, 2, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + png_chunk(b"IHDR", header) +
            png_chunk(b"IDAT", zlib.compress(bytes(rows), 9)) + png_chunk(b"IEND", b""))


def changelog(mod):
    """The mod's CHANGELOG.md, which must have an entry for the version being packaged."""
    path = ROOT / mod["folder"] / "CHANGELOG.md"
    if not path.is_file():
        raise SystemExit("{} has no CHANGELOG.md".format(mod["name"]))
    text = path.read_text(encoding="utf-8")
    heading = "## {} (".format(mod["version"])
    if not any(line.startswith(heading) for line in text.splitlines()):
        raise SystemExit("{}'s CHANGELOG.md has no entry for {}".format(mod["name"], mod["version"]))
    return text


def package(mod):
    dll = ROOT / mod["folder"] / "bin" / (mod["name"] + ".dll")
    changes = changelog(mod)
    manifest = {
        "name": mod["name"],
        "version_number": mod["version"],
        "website_url": REPO + "/tree/main/" + mod["folder"],
        "description": mod["description"],
        "dependencies": [BEPINEX_PACK],
    }

    path = OUTPUT / "{}-{}-{}.zip".format(AUTHOR, mod["name"], mod["version"])
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("manifest.json", json.dumps(manifest, indent=2) + "\n")
        archive.writestr("README.md", "# {}\n\n{}\n".format(mod["name"], mod["readme"]))
        archive.writestr("CHANGELOG.md", changes)
        archive.writestr("icon.png", icon_png(mod))
        archive.write(dll, dll.name)

    print(path)


if __name__ == "__main__":
    for mod in MODS:
        package(mod)
