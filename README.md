# RCG AI Companion (River City Girls 1)

*[Leia em português](README.pt-BR.md)*

A mod that turns **Player 2 into an AI-controlled partner** in River City Girls 1 (Steam/PC).
Built with [BepInEx 5](https://github.com/BepInEx/BepInEx) + Harmony (the game runs on Unity 2018.2 / Mono).
No original game files are modified.

Current version: **v2.10** — see the [CHANGELOG](CHANGELOG.md).

## What she does
- **Joins the game on her own** and **follows you** (including through doors, instantly).
- **Fights alongside you**: prioritizes whoever is hitting you, does combos, attacks downed enemies, uses specials and **air attacks** (juggles, jump-in attacks).
- **Positioning**: attacks from the side opposite to yours (pincer), avoids the middle of the crowd, goes around enemies, escapes being surrounded and backs off between combos.
- **Learning parry**: measures the timing of each attack of each enemy and blocks at the right moment; what she learns is saved in `BepInEx\config\rcg.aicompanion.parry.txt`.
- **Dodges** unblockable attacks and boss attacks; **bosses**: hit and back off, then goes all in when the boss is stunned.
- **Weapons** from the ground (only in combat), **recruits**, **revives you**, **heals herself** and picks up food from the ground.
- **Shopping**: at the shop she gets her own (visible) turn: dojo moves, food with permanent bonuses, reserve food and accessories (scores all 35 effects; equips the best 2).
- **Platforming by imitation**: records your path (jumps, wall jumps, ladders) and replays it when you are at a height she can't reach.
- **Personality**: lines about victories, revives, leveling up, parries, bosses...
- **Progress saved** along with your save (the game saves everything per character).

## Keys
| Key | Action |
|---|---|
| **F7** | Switch partner (cycles through the available ones; the choice is saved) |
| **F8** | Toggle the AI on/off (when off, a friend can use controller 2) |
| **F9** | Call the partner to your side |
| **F10** | Orders: Normal → Aggressive → Defensive → Stay here |

## Repository structure
```
src/                      mod source code (C# 5, compiled with the .NET Framework csc)
  CompanionPlugin.cs      BepInEx plugin: config, keys, auto-join, healing, teleport, partner switching, shop watcher
  CompanionBrain.cs       combat AI: targeting, combos, defense/parry, positioning, reviving, weapons, air attacks
  CompanionNavigator.cs   platforming by imitation (records and replays the player's path)
  CompanionShopper.cs     shopping and accessories
  CompanionPatches.cs     Harmony patches (P2 join, shop, doors, Game Over, damage events)
  AttackLearner.cs        attack timing learning (parry)
  CompanionSpeech.cs      dialogue lines
  CompanionTelemetry.cs   action log + efficiency summaries
  CompanionTestHarness.cs test tools (only with ModoTeste)
docs/                     tester guides (English and Portuguese)
build.ps1                 builds the DLL and installs it into the game
```

## Building
Requirements: River City Girls installed via Steam + BepInEx 5.4.23.5 (x64) in the game folder.
```
powershell -ExecutionPolicy Bypass -File build.ps1
```
Close the game first (the DLL stays locked while it is running). The script compiles against the game's own DLLs and copies the result to `BepInEx\plugins`.
If the game is installed elsewhere: `build.ps1 -GameDir "D:\...\River City Girls"`.

## Testing
- Logs: `BepInEx\LogOutput.log` and `BepInEx\RCG_AICompanion_acoes.log` (timeline + efficiency summaries every 60s).
- Test mode (`[Debug] ModoTeste = true`): commands in `BepInEx\teste_comando.txt` — `status`, `gameover_auto`, `ui:confirmar`/`ui:baixo`/..., `parceira:<name>`, `parceira_ciclo`. **Turn it off afterwards** (F11 triggers a Game Over).
- Back up your save before testing: `%USERPROFILE%\AppData\LocalLow\WayForward Technologies\River City Girls\_savedata`.

## Distribution to testers
The package includes BepInEx + the DLL + the guides from `docs/`. Generated packages go in `dist/` (not tracked by git).

## Uninstalling
Delete from the game folder: `BepInEx\`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt`.

## License
[MIT](LICENSE): anyone may freely use, copy, modify and redistribute the mod's code,
as long as the license notice is kept. The license covers only the code in this repository (not the game).

## Disclaimer
Fan-made, **unofficial** mod, not affiliated with WayForward or Arc System Works.
*River City Girls* and its characters belong to their respective owners. This repository contains
only the mod's code — no game files, assets or code. You need the original game (Steam).
