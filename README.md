# Nonuglon

My own personal grab-bag of FFXIV/Dalamud tweaks. Not trying to be a general-purpose plugin - just the handful of things I actually wanted, pulled out of other people's plugins and hand-adapted to live together in one place.

## Tweaks

- **Quick Return** - fires Return directly instead of clicking through the confirmation dialog. Can optionally leave/disband your party first. Ported from [ffxiv-bundleoftweaks](https://github.com/Jaksuhn/ffxiv-bundleoftweaks) (aka Automaton/CBT)'s `InstantReturn`.
- **Auto Pillion** - automatically hops onto a nearby mount with an open pillion seat, either from anyone in your party or one specific person. Also ported from ffxiv-bundleoftweaks.
- **Saddlebag Entrust Duplicates** - adds a button to the [AetherBags](https://github.com/Zeffuro/AetherBags) saddlebag window that entrusts duplicate items to your chocobo. Requires AetherBags to be installed. Ported from [PandorasBox](https://github.com/PunishXIV/PandorasBox)'s `EntrustChocoboDuplicatesAB` feature.

## Commands

```
/Nonuglon                                    open the settings window
/Nonuglon help                               list these commands in chat
/Nonuglon instantreturn <on|off>
/Nonuglon instantreturn leaveparty <on|off>
/Nonuglon autopillion <on|off>
/Nonuglon autopillion restrict <on|off>       restrict to one person instead of anyone in party
/Nonuglon autopillion target <name|clear>
/Nonuglon autopillion timeout <ms>            500-10000, how long to wait before retrying a failed mount attempt
/Nonuglon entrustchocobo <on|off>
```

Accepts `on/off`, `true/false`, `1/0`, `yes/no`, or `enable/disable` for any boolean.

## Building

1. Open `Nonuglon.sln` in Visual Studio or Rider and build (Debug or Release).
2. The built plugin lands at `Nonuglon/bin/x64/Debug/Nonuglon.dll` (or `Release`).
3. In-game: `/xlsettings` → Experimental → add the full path to `Nonuglon.dll` under Dev Plugin Locations.
4. `/xlplugins` → Dev Tools → Installed Dev Plugins → enable Nonuglon.

## Credit

Built on top of the [Dalamud SamplePlugin template](https://github.com/goatcorp/SamplePlugin), using [ECommons](https://github.com/NightmareXIV/ECommons) for the usual Dalamud plumbing. Tweak logic adapted from ffxiv-bundleoftweaks and PandorasBox as noted above - all credit for the original mechanics goes to their respective authors, this is just a personal repackaging of the bits I wanted.
