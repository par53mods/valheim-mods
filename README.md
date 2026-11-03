# parallel53's Valheim mods

BepInEx mods for Valheim, published on Thunderstore under `parallel53`.

| Mod | What it does |
|---|---|
| [ChestSalvage](ChestSalvage) | Adds a Salvage button to chests that breaks the gear inside back into materials. |
| [BrightBlueDeer](BrightBlueDeer) | Recolours deer, deer hides and the deer rug, and turns crows gold, in colours you choose. |
| [QuickStackStore](QuickStackStore) | Adds an inventory button that moves your stackable items into nearby chests already holding them. |
| [CriticalPath](CriticalPath) | Suggests the next step towards better tools, food and what you need for the next boss. |
| [Ljosgjord](Ljosgjord) | Gives the Megingjord belt the Dverger Circlet's light while you wear it. |
| [MenuModList](MenuModList) | Lists the loaded mods and the BepInEx version on the main menu. |

## Building

Each mod has a `build.sh` that runs its tests (where it has them), compiles it with
Mono's `csc` against an installed copy of Valheim and BepInEx, and copies the DLL
into `BepInEx/plugins`. The scripts expect the Steam install on macOS; change
`GAME_DIR` at the top for another location.

`package-mods.py` builds the Thunderstore zips from the compiled DLLs.

## License

MIT, see [LICENSE](LICENSE).
