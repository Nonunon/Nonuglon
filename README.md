# Nonuglon

[![AI-DECLARATION: copilot](https://img.shields.io/badge/%E4%B7%BC%20AI--DECLARATION-copilot-fee2e2?labelColor=fee2e2)](./AI-DECLARATION.md)

```
https://raw.githubusercontent.com/Nonunon/Nonuglon/master/repo.json
```

My own personal grab-bag of tweaks. Just the handful of things I actually wanted.

## Tweaks

- **Quick Return** - fires Return directly. Can optionally leave/disband your party first. Ported from [ffxiv-bundleoftweaks](https://github.com/Jaksuhn/ffxiv-bundleoftweaks) (aka Automaton/CBT)'s `InstantReturn`.
- **Auto Pillion** - automatically hops onto a nearby mount with an open pillion seat. Can be restricted to a list of "favorite" people, via chat command, the config, or a "Add to Auto Pillion" entry (native context menu and/or Chat 2's). Also ported from [ffxiv-bundleoftweaks](https://github.com/Jaksuhn/ffxiv-bundleoftweaks).
- **Saddlebag Entrust Duplicates** - adds a button to the [AetherBags](https://github.com/Zeffuro/AetherBags) saddlebag window that entrusts duplicate items to your chocobo. Requires AetherBags to be installed. Ported from [PandorasBox](https://github.com/PunishXIV/PandorasBox)'s `EntrustChocoboDuplicates` feature.
- **Search Info Menu** - adds "View Search Info" to the right-click context menu on other players out in the open world, instead of being jailed to the party list or social menus addons.
- **Estate Teleportation** - adds "Estate Teleportation" to the right-click context menu on a friend in your party list who shares your current world, opening estate-teleport window for them directly.
- **Commands** - a grab-bag of tiny single-setting "mini-tweaks".
  - **Inactive Window FPS Throttle** - a toggle for the game's own "Limit frame rate when client is inactive." System Configuration setting.
  - **Render Toggle** - "Disable 3D world rendering". Shows a "Render Off" entry in the server info bar (click to re-enable) whenever active.

Right-click (context menu) entries added by this plugin have a green "N" badge.

## Instructions

1. In-game, open `/xlsettings` and go to the Experimental tab.
2. Under Custom Plugin Repositories, paste `https://raw.githubusercontent.com/Nonunon/Nonuglon/master/repo.json`, click Add, then Save.
3. Open `/xlplugins` and find `Nonuglon` listed under the `All Plugins` section.
4. Click Install.

## Commands

<details>
<summary>Full command reference</summary>

All toggles accept `on/off`, `true/false`, `1/0`, `yes/no`, `enable/disable`, or `toggle` (flips whatever it currently is).

Each tweak's own on/off/toggle command always works, but any subcommand beyond that (`leaveparty`, `target`, `contextmenu`, `limit`, etc.) only exists once the tweak itself is turned on.

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

/Nonuglon commands <on|off|toggle>                 master switch for every mini-tweak below
/Nonuglon commands inactivefps <on|off|toggle>      same as /Nonuglon inactivefps below
/Nonuglon commands inactivefps limit <on|off|toggle>

/Nonuglon inactivefps <on|off|toggle>              enables the mini-tweak itself; also a top-level alias for the above
/Nonuglon inactivefps limit <on|off|toggle>        changes the actual game setting; needs Commands AND this on
/inactivefps <on|off|toggle>                       same as /Nonuglon inactivefps - a shorter standalone alias
/inactivefps limit <on|off|toggle>

/Nonuglon commands rendertoggle <on|off|toggle>
/Nonuglon commands rendertoggle now <on|off|toggle>

/Nonuglon rendertoggle <on|off|toggle>             enables the mini-tweak itself; also a top-level alias for the above
/Nonuglon rendertoggle now <on|off|toggle>         flips 3D rendering; needs Commands AND this on
/rendertoggle <on|off|toggle>                      same as /Nonuglon rendertoggle - a shorter standalone alias
/rendertoggle now <on|off|toggle>
/rendertoggle                                      no arguments: flips 3D rendering directly (mini-tweak must already be on)
```

</details>

## Building

<details>
<summary>Build from source</summary>

**Manual, via IDE:**

1. Open `Nonuglon.sln` in Visual Studio or Rider.
2. Build the solution (Debug or Release).
3. The built plugin lands at `Nonuglon/bin/x64/Debug/Nonuglon.dll` (or `Release`).
4. In-game, open `/xlsettings`, go to Experimental, and add the full path to `Nonuglon.dll` under Dev Plugin Locations.
5. Open `/xlplugins`, go to Dev Tools -> Installed Dev Plugins, and enable Nonuglon.

</details>

## Credit

- Built on the [Dalamud SamplePlugin template](https://github.com/goatcorp/SamplePlugin).
- Uses [ECommons](https://github.com/NightmareXIV/ECommons) for the usual Dalamud plumbing.
- Most tweak logic is adapted from [ffxiv-bundleoftweaks](https://github.com/Jaksuhn/ffxiv-bundleoftweaks) (aka Automaton/CBT) and [PandorasBox](https://github.com/PunishXIV/PandorasBox); all credit for the original mechanics goes to their respective authors, this is a personal repackaging of the bits I wanted.
- Individual tweaks ported from elsewhere carry their own attribution in a doc comment at the top of their `.cs` file, rather than an exhaustive list here.
