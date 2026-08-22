# Nonuglon

[![AI-DECLARATION: copilot](https://img.shields.io/badge/%E4%B7%BC%20AI--DECLARATION-copilot-fee2e2?labelColor=fee2e2)](./AI-DECLARATION.md)

My own personal grab-bag of tweaks. Just the handful of things I actually wanted.

## Tweaks

- **Quick Return** - fires Return directly. Can optionally leave/disband your party first. Ported from [ffxiv-bundleoftweaks](https://github.com/Jaksuhn/ffxiv-bundleoftweaks) (aka Automaton/CBT)'s `InstantReturn`.
- **Auto Pillion** - automatically hops onto a nearby mount with an open pillion seat, Can optionally mount one specific person's. Also ported from [ffxiv-bundleoftweaks](https://github.com/Jaksuhn/ffxiv-bundleoftweaks).
- **Saddlebag Entrust Duplicates** - adds a button to the [AetherBags](https://github.com/Zeffuro/AetherBags) saddlebag window that entrusts duplicate items to your chocobo. Requires AetherBags to be installed. Ported from [PandorasBox](https://github.com/PunishXIV/PandorasBox)'s `EntrustChocoboDuplicates` feature.

## Commands

```
/Nonuglon                                    
/Nonuglon help                               
/Nonuglon instantreturn <on|off>
/Nonuglon instantreturn leaveparty <on|off>
/Nonuglon autopillion <on|off>
/Nonuglon autopillion restrict <on|off>      
/Nonuglon autopillion target <name|clear>
/Nonuglon autopillion timeout <ms>            500-10000 ms
/Nonuglon entrustchocobo <on|off>
```

Accepts `on/off`, `true/false`, `1/0`, `yes/no`, or `enable/disable` for any boolean.

## Building

1. Open `Nonuglon.sln` in Visual Studio or Rider and build (Debug or Release).
2. The built plugin goes to `Nonuglon/bin/x64/Debug/Nonuglon.dll` (or `Release`).
3. In-game: `/xlsettings` -> Experimental -> add the full path to `Nonuglon.dll` under Dev Plugin Locations.
4. `/xlplugins` -> Dev Tools -> Installed Dev Plugins -> enable Nonuglon.

## Credit

Built on top of the [Dalamud SamplePlugin template](https://github.com/goatcorp/SamplePlugin), using [ECommons](https://github.com/NightmareXIV/ECommons) for the usual Dalamud plumbing. Tweak logic adapted from [ffxiv-bundleoftweaks](https://github.com/Jaksuhn/ffxiv-bundleoftweaks) (aka Automaton/CBT) and [PandorasBox](https://github.com/PunishXIV/PandorasBox) as noted above - all credit for the original mechanics goes to their respective authors, this is just a personal repackaging of the bits I wanted, instead of pushing slop directly to them. ♥
