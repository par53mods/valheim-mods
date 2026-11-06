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
        "version": "1.0.1",
        "description": "Regret that pile of old troll leather? A Salvage button on chests breaks the gear inside back into materials, shows the haul first and asks before anything is destroyed.",
        "readme": "Adds a **Salvage** button to the chest panel. It counts the gear that qualifies, lists the materials you'd get back and asks before anything is broken down. Half the crafting cost and all of the upgrade costs come back by default, rounded up, and only near the station the item was made at.\n\nSettings are in `com.parallel53.chestsalvage.cfg`: `BaseReturnRate`, `UpgradeReturnRate` and `RequireStation`.",
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
        "version": "1.0.2",
        "description": "One button sends your stackables into nearby chests that already hold them, and tells you exactly what didn't fit. Hotbar and equipped gear stay put.",
        "readme": "Click **Quick Stack** on the inventory screen to move stackable items into nearby chests that already hold them. Equipped items, single items such as gear and, by default, your hotbar stay with you. When a chest is full it tells you which items had no room.\n\nSettings are in `com.parallel53.quickstackstore.cfg`: `Radius` and `KeepHotbar`.",
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
        "version": "0.1.2",
        "description": "A pocket strategist for the inventory screen: the next tool, food, armor, mead, weapon or offering you need for the next boss, and nothing you don't. Then it points you at the boss.",
        "readme": "Shows a panel under the inventory with the next steps towards the most survivability for the least time:\n\n- Tools that open up new materials, such as the bronze axe for fine wood.\n- The best new food you can make, labelled health, stamina or eitr.\n- For the next boss: just enough armor, the resistance it calls for (mead or gear), weapons or arrows that hit its weaknesses, a shield where it helps, and the summoning offering once your armor is up to it.\n- Finding the boss's altar comes last, once everything else is ready.\n\nStations, upgrades and materials appear only as steps towards a goal. It reads your character's progress, so it works mid-game.\n\nSettings are in `com.parallel53.criticalpath.cfg`: `Suggestions` and `Notify`.",
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
        "readme": "Shows a panel on the main menu listing every loaded mod, this one included, with its version and the BepInEx version.",
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
        "version": "0.1.0",
        "description": "Full sail not full enough? Two extra sail speeds above Full, a fast reverse for docking in a hurry, and rowing speed you can tune. A label by the speed indicator shows the level.",
        "readme": "Press forward at Full sail for **+1** (twice the sail force) and again for **+2** (three times). "
                  "Press back at reverse for **fast** reverse (twice the rowing force). A label beside the ship's "
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
        "version": "0.1.0",
        "description": "Dropped your spear in the tall grass again? Makes the sparkles on dropped items brighter, more plentiful and wider spread, so loot catches your eye from further away.",
        "readme": "Items lying on the ground give off a few faint sparkles. This triples how many there are, how "
                  "big they are and how long they last, and makes them brighter and spread wider around the item. Only your "
                  "game changes; other players see the usual sparkles.\n\n"
                  "Settings are in `com.parallel53.dudewheresmyspear.cfg`: `Count` (3), `Size` (3), `Lifetime` (3), "
                  "`Intensity` (2) and `Spread` (2), each a multiple of the game's. Changes take effect as soon as "
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


def package(mod):
    dll = ROOT / mod["folder"] / "bin" / (mod["name"] + ".dll")
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
        archive.writestr("icon.png", icon_png(mod))
        archive.write(dll, dll.name)

    print(path)


if __name__ == "__main__":
    for mod in MODS:
        package(mod)
