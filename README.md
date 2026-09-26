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
- **Estate Teleportation** - adds "Estate Teleportation" to the right-click context menu on a friend in your party list (and similar list-style menus) who shares your current world, opening the game's own friend estate-teleport window for them directly. Doesn't duplicate it on their nameplate/model out in the world, since the game already shows it there natively.
- **Commands** - a grab-bag of tiny single-setting "mini-tweaks", too small to need a full tweak of their own. Turning Commands off disables every mini-tweak's actual effect at once, without losing which ones you'd individually checked.
  - **Inactive Window FPS Throttle** - a chat command and config-window checkbox for the game's own "Limit frame rate when client is inactive." System Configuration setting. Needs both Commands and this mini-tweak's own checkbox on before it does anything.

All tweaks are off by default on a fresh install; nothing activates until you turn it on yourself.

Right-click (context menu) entries added by this plugin (Auto Pillion & Search Info Menu) have a green "N" badge.

## Installing

1. In-game, open `/xlsettings` and go to the Experimental tab.
2. Under Custom Plugin Repositories, paste `https://raw.githubusercontent.com/Nonunon/Nonuglon/master/repo.json`, click Add, then Save.
3. Open `/xlplugins` and find Nonuglon listed under the custom-repository section.
4. Click Install.

Updates then show up the normal way, through the Plugin Installer, whenever a new version is released.

## Commands

<details>
<summary>Full command reference</summary>

All toggles accept `on/off`, `true/false`, `1/0`, `yes/no`, `enable/disable`, or `toggle` (flips whatever it currently is).

Each tweak's own on/off/toggle command always works, but any subcommand beyond that (`leaveparty`, `target`, `contextmenu`, `limit`, etc.) only exists once the tweak itself is on, both to run and in this list. Try one while its tweak is off and it's rejected exactly as if it were never a valid word, not told to turn the tweak on first.

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

**Automated, via script:**

Run `build/Build_Nonuglon.bat`. It builds Release and drops the output directly into the live Dev Plugin Locations folder (`F:\FFXIV\Plugins\DevPlugins\Nonuglon`), so a build plus an in-game reload picks up changes with no manual copying.

`build/` is gitignored - these are local dev-convenience scripts, not part of what ships.

</details>

## Releasing

<details>
<summary>Cut a new release</summary>

**Steps:**

1. Commit and push code changes to `master` as normal.
2. Run `build/Release_Nonuglon.bat` from a clean, up-to-date working tree.
   - Patch version bump by default; `Release_Nonuglon.bat minor`, `major`, or an explicit version such as `Release_Nonuglon.bat 1.4.0` also work.
   - `-DryRun` shows the next version and runs the build check without tagging or pushing anything.
3. Confirm the `y/N` prompt once the local build check passes.

**What happens under the hood:**

The script refuses to run with uncommitted changes, or with a local `master` out of sync with `origin/master`. It runs a Release build first, so a broken build never gets tagged. On confirmation, it tags and pushes, which triggers [.github/workflows/release.yml](.github/workflows/release.yml): that workflow builds Release, publishes a GitHub Release with `Nonuglon.zip` attached, syncs `repo.json`'s `AssemblyVersion` and `DalamudApiLevel` from the build's own generated manifest, and updates `Nonuglon.csproj`'s checked-in `<Version>` to match the tag (purely cosmetic, keeps a local dev build from showing a stale version number). `repo.json`'s download links point at `.../releases/latest/download/Nonuglon.zip`, a stable URL that always resolves to the newest release, so nothing else needs to change per release. The manual equivalent of the tag/push step is `git tag v1.0.0 && git push origin v1.0.0`.

</details>

## Credit

- Built on the [Dalamud SamplePlugin template](https://github.com/goatcorp/SamplePlugin).
- Uses [ECommons](https://github.com/NightmareXIV/ECommons) for the usual Dalamud plumbing.
- Most tweak logic is adapted from [ffxiv-bundleoftweaks](https://github.com/Jaksuhn/ffxiv-bundleoftweaks) (aka Automaton/CBT) and [PandorasBox](https://github.com/PunishXIV/PandorasBox); all credit for the original mechanics goes to their respective authors, this is a personal repackaging of the bits I wanted.
- Individual tweaks ported from elsewhere carry their own attribution in a doc comment at the top of their `.cs` file, rather than an exhaustive list here.
