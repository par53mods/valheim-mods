# parallel53's Valheim mods

BepInEx mods for Valheim, published on Thunderstore under `parallel53`.

| Mod | What it does |
|---|---|
| [ChestSalvage](ChestSalvage) | Regret that pile of old troll leather? A Salvage button on chests breaks the gear inside back into materials, shows the haul first and asks before anything is destroyed. |
| [BrightBlueDeer](BrightBlueDeer) | Odin's paint bucket: deer turn electric blue, crows gold, boars pink, bears bright green, wolves turquoise and necks violet. Only you see the makeover; every colour is yours to pick. |
| [QuickStackStore](QuickStackStore) | One button sends your materials into nearby chests that already hold them, and names anything that didn't fit. Cooked food, meads, equipped gear and your hotbar stay with you by default. |
| [CriticalPath](CriticalPath) | A pocket strategist for the inventory screen: suggests the next tool, food, armor, mead, weapon or offering to work towards for the next boss. Finding the boss is still up to you. |
| [Ljosgjord](Ljosgjord) | Why carry a torch when your belt can glow? Wearing Megingjord gives you the Dverger Circlet's light, so your head is free for a proper helmet. |
| [MenuModList](MenuModList) | Shows every loaded mod and the BepInEx version on the main menu, so you know what's running before you sail off. |
| [SpeedBoat](SpeedBoat) | Full sail not full enough? Two extra sail speeds above Full, a fast reverse for docking in a hurry, and rowing speed you can tune. A label by the speed indicator shows the level. |
| [DudeWheresMySpear](DudeWheresMySpear) | Dropped your spear in the tall grass again? Makes the sparkles on dropped items brighter, more plentiful and wider spread, so loot is easier to spot. |
| [ShutupPortal](ShutupPortal) | Silences portals by default: the sound when one activates and the hum while it's active. Both volumes are settings. |
| [BlueFlame](BlueFlame) | Campfires, hearths, torches, braziers, portals and crafting stations burn blue like the Standing Blue-burning Iron Torch by default, blue light included. |

## Building

Each mod has a `build.sh` that runs its tests (where it has them), compiles it with
Mono's `csc` against an installed copy of Valheim and BepInEx, and copies the DLL
into `BepInEx/plugins`. The scripts expect the Steam install on macOS; change
`GAME_DIR` at the top for another location.

`package-mods.py` builds the Thunderstore zips from the compiled DLLs.

## License

MIT, see [LICENSE](LICENSE).
