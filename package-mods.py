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
        "version": "1.0.0",
        "description": "Adds a Salvage button to chests that breaks the gear inside back into materials.",
        "readme": "Adds a Salvage button to the chest panel. It shows how many items qualify, "
                  "lists what you get back and asks before anything is destroyed.\n\n"
                  "Settings are in `com.parallel53.chestsalvage.cfg`: `BaseReturnRate`, "
                  "`UpgradeReturnRate` and `RequireStation`.",
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
        "version": "1.0.0",
        "description": "Recolours deer, deer hides and the deer rug, and turns crows gold, in colours you choose.",
        "readme": "Paints deer, their corpses, deer hides, the deer rug and crows. Only your own game "
                  "changes; other players see them as normal.\n\n"
                  "Settings are in `com.parallel53.brightbluedeer.cfg`: `Enabled`, `Color` for "
                  "deer and `BirdColor` for crows (red, green, blue, each from 0 to 255). Restart the game after changing them.",
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
        "version": "1.0.1",
        "description": "Adds an inventory button that moves your stackable items into nearby chests already holding them.",
        "readme": "Click Quick Stack on the inventory screen to move stackable items into nearby chests that "
                  "already hold them. Equipped items, single items such as gear and, by default, "
                  "your hotbar stay with you.\n\n"
                  "Settings are in `com.parallel53.quickstackstore.cfg`: `Radius` and "
                  "`KeepHotbar`.",
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
        "version": "0.1.1",
        "description": "Suggests the next step towards better tools, food and just enough armor for the next boss.",
        "readme": "Shows a panel on the inventory screen with the next step towards the most survivability "
                  "for the least time: tools that open up new materials, food you can cook, and only the "
                  "armor and resistance meads needed for the next boss. Stations, upgrades and materials such as bronze appear "
                  "as steps when a goal needs them. Once you have the armor it tells you to go fight. "
                  "It reads your character's progress, so it works mid-game.\n\n"
                  "Settings are in `com.parallel53.criticalpath.cfg`: `Suggestions` and `Notify`.",
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
        "version": "1.0.0",
        "description": "Gives the Megingjord belt the Dverger Circlet's light while you wear it.",
        "readme": "Ljósgjörð: wearing the Megingjord gives you the Dverger Circlet's light. Only you see it.",
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
        "version": "1.0.0",
        "description": "Lists the loaded mods and the BepInEx version on the main menu.",
        "readme": "Shows a panel on the main menu listing every loaded mod, including this one, "
                  "with its version and the BepInEx version.",
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
