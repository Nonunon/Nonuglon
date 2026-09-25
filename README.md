# Nonuglon

[![AI-DECLARATION: copilot](https://img.shields.io/badge/%E4%B7%BC%20AI--DECLARATION-copilot-fee2e2?labelColor=fee2e2)](./AI-DECLARATION.md)

My own personal grab-bag of tweaks. Just the handful of things I actually wanted.

## Tweaks

- **Quick Return** - fires Return directly. Can optionally leave/disband your party first. Ported from [ffxiv-bundleoftweaks](https://github.com/Jaksuhn/ffxiv-bundleoftweaks) (aka Automaton/CBT)'s `InstantReturn`.
- **Auto Pillion** - automatically hops onto a nearby mount with an open pillion seat. Can be restricted to a list of "favorite" people, via chat command, the config, or a "Add to Auto Pillion" entry (native context menu and/or Chat 2's). Also ported from [ffxiv-bundleoftweaks](https://github.com/Jaksuhn/ffxiv-bundleoftweaks).
- **Saddlebag Entrust Duplicates** - adds a button to the [AetherBags](https://github.com/Zeffuro/AetherBags) saddlebag window that entrusts duplicate items to your chocobo. Requires AetherBags to be installed. Ported from [PandorasBox](https://github.com/PunishXIV/PandorasBox)'s `EntrustChocoboDuplicates` feature.
- **Search Info Menu** - adds "View Search Info" to the right-click context menu on other players out in the open world, instead of being jailed to the party list or social menus addons.
- **Estate Teleportation** - adds "Estate Teleportation" to the right-click context menu on a friend sharing your current world, opening the game's own friend estate-teleport window for them directly.

Right-click (context menu) entries added by this plugin (Auto Pillion & Search Info Menu) have a green "N" badge.

## Commands

<details>
<summary>Full command reference</summary>

All toggles accept `on/off`, `true/false`, `1/0`, `yes/no`, `enable/disable`, or `toggle` (flips whatever it currently is).

```
/Nonuglon                                          open the settings window
/Nonuglon config | settings                        same as above
/Nonuglon help | ?                                 print this list to chat

/Nonuglon instantreturn <on|off|toggle>            alias: quickreturn
/Nonuglon instantreturn leaveparty <on|off|toggle>

/Nonuglon autopillion <on|off|toggle>              alias: pillion
/Nonuglon autopillion restrict <on|off|toggle>
/Nonuglon autopillion contextmenu <on|off|toggle>  native right-click "Add to Auto Pillion"
/Nonuglon autopillion chat2menu <on|off|toggle>    same, in Chat 2's own context menu
/Nonuglon autopillion timeout <ms>                 500-10000 ms
/Nonuglon autopillion target add <name>@<world>
/Nonuglon autopillion target remove <name>@<world>
/Nonuglon autopillion target enable <name>@<world>
/Nonuglon autopillion target disable <name>@<world>
/Nonuglon autopillion target list
/Nonuglon autopillion target clear

/Nonuglon entrustchocobo <on|off|toggle>           aliases: entrust, chocobo

/Nonuglon searchinfo <on|off|toggle>

/Nonuglon estateteleport <on|off|toggle>           alias: estate
```

</details>

## Installing

Add this as a custom plugin repository instead of building it yourself:

1. `/xlsettings` -> Experimental -> Custom Plugin Repositories -> paste `https://raw.githubusercontent.com/Nonunon/Nonuglon/master/repo.json` -> Add -> Save.
2. `/xlplugins` -> find Nonuglon under the custom-repo section -> Install.

Updates then show up the normal way, through the Plugin Installer, whenever a new version is released.

## Building

1. Open `Nonuglon.sln` in Visual Studio or Rider and build (Debug or Release).
2. The built plugin goes to `Nonuglon/bin/x64/Debug/Nonuglon.dll` (or `Release`).
3. In-game: `/xlsettings` -> Experimental -> add the full path to `Nonuglon.dll` under Dev Plugin Locations.
4. `/xlplugins` -> Dev Tools -> Installed Dev Plugins -> enable Nonuglon.

## Releasing

Run `Release_Nonuglon.bat` (patch bump by default; `Release_Nonuglon.bat minor`, `major`, or an explicit version like `Release_Nonuglon.bat 1.4.0` also work; `-DryRun` shows the next version and runs the build check without tagging/pushing anything). It refuses to run with uncommitted changes or a local `master` that's out of sync with `origin/master`, runs a Release build first so a broken build never gets tagged, then asks for confirmation before tagging and pushing.

Pushing that tag triggers [.github/workflows/release.yml](.github/workflows/release.yml), which builds Release, publishes it as a GitHub Release with `Nonuglon.zip` attached, and syncs `repo.json`'s `AssemblyVersion`/`DalamudApiLevel` from the build's own generated manifest. `repo.json`'s download links point at `.../releases/latest/download/Nonuglon.zip`, a stable URL that always resolves to the newest release, so nothing else needs to change per release. (Doing it by hand instead is just `git tag v1.0.0 && git push origin v1.0.0`.)

## Credit

Built on top of the [Dalamud SamplePlugin template](https://github.com/goatcorp/SamplePlugin), using [ECommons](https://github.com/NightmareXIV/ECommons) for the usual Dalamud plumbing, with [ffxiv-bundleoftweaks](https://github.com/Jaksuhn/ffxiv-bundleoftweaks) (aka Automaton/CBT) and [PandorasBox](https://github.com/PunishXIV/PandorasBox) as the main sources for the tweaks above - all credit for the original mechanics goes to their respective authors, this is just a personal repackaging of the bits I wanted, instead of pushing slop directly to them. ♥ Individual tweaks ported from elsewhere carry their own attribution in a doc comment at the top of their `.cs` file.
