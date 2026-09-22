# ADOFAI Note Highway

A [UnityModManager](https://www.nexusmods.com/site/mods/21) mod for *A Dance of Fire and Ice*
that draws a scrolling note highway in sync with the song.

This fork adds **1 / 4 / 6 / 8 lanes**, density-based inward rolls or alternating hands,
hold-aware allocation, real multitaps, and optional visual grouping of nearby notes.
Timing comes directly from the game's input conductor clock. It never sends key input.

**[简体中文使用与分轨说明](docs/分轨说明.md)**

## Multi-lane controls

Open **Ctrl+F10**, enable **ADOFAI Note Highway - Multi-lane**, and click its settings icon.
Choose **简体中文** or **English** at the top. The three settings pages cover:

- **Lanes:** 1K/4K/6K/8K, inward roll or alternate hands, primary hand, single-finger
  KPS, follow playback speed, optional nearby-note visual alignment.
- **Layout:** scroll speed **10–10000 px/s** with a logarithmic slider and numeric entry,
  note thickness **2–80 px**, total width, spacing, opacity, lane labels and beat grid.
  Position the hit line by bottom distance **0–4320 px** or **0–100%** of highway height;
  its thickness is adjustable from **1–20 px**. Positions are clipped to the visible highway.
- **Color & Sync:** hand/chord colors, the game's input-versus-visual clock correction,
  and a manual timing nudge. Positive nudge makes notes arrive later.

Settings take effect immediately; use UMM **Save** to persist them. Existing XML settings
are retained, and missing new fields receive defaults. The default remains single lane.
Multiple lanes are visual fingering guidance, not a new input-judgment mode; assign your
preferred physical keys yourself. Red **+N** warnings explicitly report demands that cannot
fit into the available lanes. Holds are never cut off to hide a capacity conflict.

Midspins do not create extra presses. Ordinary hold releases do not create duplicate taps;
consecutive holds and remaining multitap requirements are retained. The game's **Hit Once**
multitap option is respected. Nearby-note visual alignment is disabled by default because it
aligns drawing positions while keeping actual hit and release times unchanged.

The allocation is a new C# implementation informed by the timing-density and inward-roll
rules in [Adofai-Macro](https://github.com/helloLZR/Adofai-Macro/tree/c6f2d20e2b508285709eb90d2e6de78bf4845d20).
It allocates directly to the requested lane count instead of folding a 32-key macro onto
fewer columns. This mod does not export charts for other games.

## Build and test

Install .NET SDK 6 or newer, ADOFAI, and UnityModManager, then run:

```powershell
./scripts/build.ps1 -GameDir 'D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice'
```

The default game path is Steam's standard Program Files (x86) library. The script runs four
dependency-free console test suites, builds the .NET Framework 4.8 mod against your local game,
and writes the ZIP and SHA256 under `dist`. It packages only the mod DLL, Info.json and docs;
game/Unity/UMM assemblies and user settings are never included.

To run a test suite without the game installed:

```powershell
dotnet run --project tests/LaneAllocator.Tests -c Release
dotnet run --project tests/Settings.Tests -c Release
dotnet run --project tests/ChartEventBuilder.Tests -c Release
dotnet run --project tests/HighwayGeometry.Tests -c Release
```

## Install

Download the latest `.zip` from [Releases](https://github.com/novahanser/adofai-highway/releases) and drag it into the UnityModManager
installer (or unzip it into the game's `Mods` folder). Then launch the game, open the UMM menu
(**Ctrl+F10**), and enable **ADOFAI Note Highway**. Colours, opacity, scroll speed, and the beat
grid are all adjustable in the mod's settings.

## Disclaimer

This is purely a for-fun project. The highway is a visual assist, and using it on ranked or
leaderboard maps would count as cheating under the rules of those communities.
Check the rules of any ranking system before playing with the mod enabled, and keep it off where assists aren't allowed.
Use common sense; I take no responsibility if you ignore this and get yourself banned.

## Preview

[Preview video](https://www.youtube.com/watch?v=TrtfApyTOkE)
