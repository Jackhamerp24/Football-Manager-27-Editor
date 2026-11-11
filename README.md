<p align="center"><img src="docs/images/icon.png" alt="Football Manager 27 Editor" width="220"></p>

# Football Manager 27 Editor

English | [Tiếng Việt](README.vi.md)

An in-game editor for Football Manager 27. It edits players, staff, clubs, nations and more while the game is running,
by reading and writing the memory of `fm.exe` (module `game_plugin.dll`). Built for the Xbox / Game Pass and Steam
versions; see [Requirements](#requirements) for what has been checked on each.

For single-player, offline saves only. Do not use it online or in shared saves.

## What it does

| Area | What you can edit |
|---|---|
| Players | Attributes, positions, personality, CA/PA, reputation, height, date of birth, nationality and other details; contract and future contract, bonuses and transfer clauses; career plans, relationships, languages, preferred moves, development speed; injuries, bans, unhappiness, national call-ups; work permit and promises |
| Moving players | Move to club as a transfer with fee and sell-on, as a loan, or as a **future transfer** with a date and all the transfer terms (instalments, appearance and goal payments, buy-back fee, sell-on, wage contribution, friendly), which can also be removed again; swap two players; release; captain / vice captain; clone attributes to other players; comparison |
| Staff | Attributes, CA/PA, personality, contract, career plans, relationships, languages, knowledge, formations, tendencies, move to another club with a new role and wage |
| Clubs | Information, finances, debt, affiliations, club vision, kits and colours, relationships, staff, squads, transfer center |
| Nations, stadiums, cities, competitions, awards, referees, injury types | Details, reputation and settings, each in its own screen. Stadiums: owner, location, nearby stadium and coordinates. Cities: nation, region, language and weather |
| Tools | Squad presets with preview and Undo (grant work permit for the club's country, Golden Generation, heal, fitness, morale, lift bans, remove unhappiness, max attributes, prevent retirement, tactical understanding, team cohesion), freezer, export to CSV / JSON, advanced filters, favourites, reload of the current screen (Ctrl+R), settings (date format) |

Every change goes into a history with Undo. See [CHANGELOG.md](CHANGELOG.md) for what is new.

Loading every player in the database (about 50,000) takes under a second.

Every write is checked first:
- the object at that address must still be what the editor read;
- the value in memory must still equal the value the editor read.

The editor reads the value back after writing it. If anything does not match, it writes nothing and reports the error.

A few actions add rows to the game's own lists (a new promise, a future transfer, a language). When a list is full, the
editor allocates a new one outside the game's allocator. Keep a backup of the save before using them.

## Requirements

- Windows 10/11 x64
- Football Manager 27 `27.0.1+2494687` (or `27.0.0+2493426`), Xbox / Game Pass or Steam. Other builds need a new offset profile; see
  [After a game patch](#after-a-game-patch).
- [.NET 10 SDK](https://dotnet.microsoft.com/download), only if you build it yourself

What has been checked where:

| Build | Status |
|---|---|
| Xbox / Game Pass `27.0.1+2494687` | Current build. Every reader (71 features) and every kind of write (24 round trips) run against the live game; 88 values checked against a calibration save and FMRTE |
| Steam `27.0.1+2494687` | Current build. Every reader (71 features) and every kind of write (24 round trips) run against the live game |
| Xbox / Game Pass and Steam `27.0.0+2493426` | Previous build, still supported |
| Steam `27.0.0+2486695` | Older build, used by the tests |

## Download

Download from [Releases](../../releases):

| File | For |
|---|---|
| **`Fm27Editor.exe`** | The editor app. Most people only need this one. |
| `Fm27Editor.Tools.exe` | Command-line tools (see below), including `find-offsets` for a new game build |

Each exe runs on its own from any folder. Nothing needs installing, not even .NET.

All the source is in this repository, and every release is built from it by the [Build workflow](.github/workflows/build.yml)
on GitHub's servers. Nothing is built on a personal machine or uploaded by hand. Each release lists the commit it was
built from, and you can check this two ways:

- `SHA256SUMS.txt` in the release has the hash of each file. Compare with `Get-FileHash Fm27Editor.exe`.
- Every file has a signed build attestation that links it to the workflow run and the commit. With the
  [GitHub CLI](https://cli.github.com):

  ```bash
  gh attestation verify Fm27Editor.exe --repo Jackhamerp24/Football-Manager-27-Editor
  ```

What the editor does to your system: it opens the `fm.exe` process, reads its memory, and writes the specific values you
edit. Its only network request is one check at start for a newer release on GitHub (nothing is sent but the request itself; it fails silently offline). It does not ask for administrator rights. The only files it writes are its own
settings (`%LOCALAPPDATA%\Fm27Editor`: frozen values, saved filters, graphics folder; `favorites.json` next to the exe)
and the exports you ask for.

## Build and run

```bash
dotnet build Fm27Editor.slnx -c Release
```

```bash
dotnet test Fm27Editor.slnx -c Release
```

Start FM27, load a save, then run the app:

```bash
src/Fm27Editor.App/bin/Release/net10.0/Fm27Editor.exe
```

Save your game before editing. The game only redraws a screen after you press **Continue**, even though the change is
already in memory.

## Command-line tools

Run these with the game open and a save loaded.

```bash
src/Fm27Editor.Tools/bin/Release/net10.0/Fm27Editor.Tools.exe players Yamal
```

```bash
src/Fm27Editor.Tools/bin/Release/net10.0/Fm27Editor.Tools.exe set "Kobbie Mainoo" Crossing 12
```

```bash
src/Fm27Editor.Tools/bin/Release/net10.0/Fm27Editor.Tools.exe verify tests/golden/27.0.0+2486695.players.json
```

| Command | What it does |
|---|---|
| `players [name]` | Lists matching players: in-game date, age, club, wage, contract expiry, CA/PA |
| `set <name> <field> <value>` | Writes one value. The field is an attribute (`Pace`), a position (`Striker`), a personality value (`Ambition`), `CA` or `PA` |
| `verify <golden.json>` | Compares live memory with values copied from the game UI |
| `find-offsets <golden.json> <out.json> [--date yyyy-MM-dd]` | Rebuilds the offset profile for a new game build |

Many more commands (staff, clubs, nations, presets, promises, transfers, future transfers) are listed at the top of
`src/Fm27Editor.Tools/Program.cs`. Commands that write need `--write`; without it they only print the plan.

## How FM ability works

CA is the value the game treats as the source of truth. Over a few in-game days, attributes drift toward a level that
fits the player's CA, in both directions. So raising attributes without raising CA may not last; set CA as well.

## Layout

| Path | What |
|---|---|
| `src/Fm27Editor.Core` | Process memory, build check, offset profiles, reading, guarded writes, offset finder |
| `src/Fm27Editor.App` | Desktop app (Avalonia) |
| `src/Fm27Editor.Tools` | Command-line tools |
| `offsets/<build>.json` | Every build-specific address and field offset, one file per game build. Built into the exe |
| `tests/Fm27Editor.Core.Tests`, `tests/Fm27Editor.App.Tests` | Unit tests against a fake address space |
| `tests/golden/<build>.players.json` | Player values copied from the game UI: the reference for `verify` and `find-offsets` |
| `re/tools/memscan` | Read-only memory scanner used to find the FM27 layout |

## After a game patch

The editor will not attach to a build it has no profile for. This keeps it from writing to the wrong addresses.

To make a profile for a new build:

1. Load a save. Copy the exact attributes, height and date of birth of a few players into a new golden file.
   - At least one player must have a common name, and at least one must have none.
   - Leave out scouted ranges such as `9-12`.
2. Run `find-offsets <golden.json> offsets/<build>.json --date <in-game date>`. It takes about 25 seconds.
   Profiles in an `offsets` folder next to the exe are used in addition to the built-in ones, and replace a
   built-in profile for the same build.
3. Run `verify` with the same golden file.

`find-offsets` finds the player layout only. The other screens need their own offsets; rebuilding the whole profile with
one command is on the roadmap. For Xbox / Game Pass, `re/tools/gamepass-rva-map.ps1` builds a profile from the Steam one
by matching class names (RTTI), in a few seconds.

## Not supported: women's football

The editor is built and checked for men's football only. Women's teams, players and competitions are not supported:
they can be missing from lists, can show up unlabelled or with wrong data, and edits to them are untested, so avoid
writing to them. Support for women's football may be added later; it is not planned for a specific release.

## Roadmap

Done:

- Xbox / Game Pass and Steam, one profile per build.
- Players, staff, clubs, nations, stadiums, cities, competitions, awards, referees and injury types in their own screens.
- Move to club: transfer, loan and future transfer with all the transfer terms; swap player.
- Promises (all types), work permit, tactical understanding preset for players and squads, favourites, reload of the
  current screen.
- Squad presets, freezer, export, advanced search, attribute export / import.
- Light (white and blue) theme next to the dark one, switched with one button and remembered.
- Mass edit for players, staff and clubs in FMRTE's layout, plus a Nation scope (youth rating and the other nation
  fields) that FMRTE does not have; opened with the toolbar button or Ctrl+M.
- Club kits and colours, finances and sponsors; competition VAR, prize money, squad rules and standings; nation
  agreements, transfer preferences and EuroCoefficients; stadium owner, location, nearby stadium and coordinates; city
  nation, region, language and weather.

Next:

- A macOS build.
- Prices shown in the game's currency, or in a currency the user picks.
- Deeper analysis: the player stats radar tab and player visualisation.
- Shortlist.
- Transfer terms for a move that happens now (today only the sell-on is supported).
- Scout Information and Stats tabs; Possible Signings.
- Kit items: change type, add, remove, duplicate.
- Training calendar.
- Coach allowances.
- A smaller, faster-starting exe.
- Women's football (teams, players, competitions), possibly later.

## Disclaimer

This project is not affiliated with or endorsed by Sports Interactive or SEGA. Football Manager is their trademark.
Editing the game's memory can corrupt a save, so keep backups. Use at your own risk.

**No warranty, no liability.** The editor changes a running game in ways its makers did not design for. It is provided "as is":
the author is not responsible for a damaged or lost save, a crash, or any other loss caused by using it. Back up your save
before every session, and use a throwaway save for anything you are not sure about.

**If a save breaks, tell the author what caused it.** You are not alone in this: a report helps fix the cause for everyone.
Click the bug icon at the bottom right of the editor, describe what you did, and create the report. It is a zip saved on
your Desktop with your description, the state of the editor and game, and a log of every write the editor made to the game
(address, old and new bytes) for the last ten runs. It works while the editor is open and also after you have closed the
editor or the game, because the logs are kept on disk (`%LOCALAPPDATA%\Fm27Editor\logs`). Nothing is uploaded and no save
file is included; send the zip yourself, either by opening an [issue](https://github.com/Jackhamerp24/Football-Manager-27-Editor/issues)
(drag the zip and any screenshots into the text box; issues are public) or by email to vuquyduong2004@gmail.com
(attach the zip and screenshots). Say which build you use (Game Pass or Steam), what you did, and whether the save still opens.
Folder paths in the logs are cut to the last name (`D:\Pictures\Me\FM Graphics` becomes `…\FM Graphics`), but read the zip and screenshots before sending them.

## Support

If the editor helps you, you can support development:

- [Ko-fi](https://ko-fi.com/quyduongvu) (card or PayPal)
- Vietnamese bank transfer (VietQR, Vietcombank): scan with any Vietnamese banking or e-wallet app

<img src="docs/images/donate-vietqr.png" alt="VietQR, Vietcombank" width="220">

## License

GPL-3.0-or-later. See [LICENSE](LICENSE).
