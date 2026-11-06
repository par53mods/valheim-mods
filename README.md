# parallel53's Valheim mods

BepInEx mods for Valheim, published on Thunderstore under `parallel53`.

| Mod | What it does |
|---|---|
| [ChestSalvage](ChestSalvage) | Regret that pile of old troll leather? A Salvage button on chests breaks the gear inside back into materials, shows the haul first and asks before anything is destroyed. |
| [BrightBlueDeer](BrightBlueDeer) | Odin's paint bucket: deer turn electric blue, crows gold, boars pink, bears bright green, wolves turquoise and necks violet. Only you see the makeover; every colour is yours to pick. |
| [QuickStackStore](QuickStackStore) | One button sends your stackables into nearby chests that already hold them, and tells you exactly what didn't fit. Hotbar and equipped gear stay put. |
| [CriticalPath](CriticalPath) | A pocket strategist for the inventory screen: the next tool, food, armor, mead, weapon or offering you need for the next boss, and nothing you don't. Then it points you at the boss. |
| [Ljosgjord](Ljosgjord) | Why carry a torch when your belt can glow? Wearing Megingjord gives you the Dverger Circlet's light, so your head is free for a proper helmet. |
| [MenuModList](MenuModList) | Shows every loaded mod and the BepInEx version on the main menu, so you know what's running before you sail off. |
| [SpeedBoat](SpeedBoat) | Full sail not full enough? Two extra sail speeds above Full, a fast reverse for docking in a hurry, and rowing speed you can tune. A label by the speed indicator shows the level. |

## Building

Each mod has a `build.sh` that runs its tests (where it has them), compiles it with
Mono's `csc` against an installed copy of Valheim and BepInEx, and copies the DLL
into `BepInEx/plugins`. The scripts expect the Steam install on macOS; change
`GAME_DIR` at the top for another location.

`package-mods.py` builds the Thunderstore zips from the compiled DLLs.

## License

MIT, see [LICENSE](LICENSE).
