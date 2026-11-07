# Football Manager 27 Editor

English | [Tiếng Việt](README.vi.md)

An in-game editor for Football Manager 27. It edits players, staff, clubs, nations and more while the game is running,
by reading and writing the memory of `fm.exe` (module `game_plugin.dll`). Works with the Steam and the Xbox / Game Pass
versions.

For single-player, offline saves only. Do not use it online or in shared saves.

## What it does

| Area | What you can edit |
|---|---|
| Players | Attributes, positions, personality, CA/PA, reputation, height, date of birth, nationality and other details, contract and future contract, bonuses and transfer clauses, career plans, relationships, languages, preferred moves, injuries, bans, unhappiness, national call-ups |
| Player actions | Move to club (with fee and sell-on), release, captain / vice captain, clone attributes to other players, comparison |
| Staff | Attributes, CA/PA, personality, contract, career plans, relationships, languages, knowledge, formations, tendencies, move to another club |
| Clubs | Information, finances, debt, affiliations, club vision, relationships, staff, squads, transfer center |
| Nations, stadiums, cities, competitions, awards | Details, reputation and settings shown in their own screens |
| Tools | Mass edit with preview and Undo, team presets (heal, fitness, morale, lift bans, remove unhappiness, max attributes, prevent retirement), freezer, export to CSV / JSON, filters |

Every change goes into a history with Undo. See [CHANGELOG.md](CHANGELOG.md) for what is new.

Loading every player in the database (about 50,000) takes under a second.

Every write is checked first:
- the object at that address must still be a player;
- the value in memory must still equal the value the editor read.

The editor reads the value back after writing it. If anything does not match, it writes nothing and reports the error.

## Requirements

- Windows 10/11 x64
- Football Manager 27 `27.0.0+2493426` on Steam or Xbox / Game Pass (`27.0.0+2486695` on Steam also works). Other builds
  need a new offset profile; see [After a game patch](#after-a-game-patch).
- [.NET 10 SDK](https://dotnet.microsoft.com/download), only if you build it yourself

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
edit. It makes no network connections and does not ask for administrator rights. The only files it writes are its own
settings in `%LOCALAPPDATA%m27editor` (frozen values, saved filters) and the exports you ask for.

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

Save your game before editing. Some screens only show the new values after you press **Continue**.

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

Many more commands (staff, clubs, nations, mass edit, presets, release, captain, staff move) are listed at the top of
`src/Fm27Editor.Tools/Program.cs`.

## How FM ability works

CA is the value the game treats as the source of truth. Over a few in-game days, attributes drift toward a level that
fits the player's CA, in both directions. So raising attributes without raising CA may not last; set CA as well.

## Layout

| Path | What |
|---|---|
| `src/Fm27Editor.Core` | Process memory, build check, offset profiles, player reading, guarded writes, offset finder |
| `src/Fm27Editor.App` | Desktop app (Avalonia) |
| `src/Fm27Editor.Tools` | Command-line tools |
| `offsets/<build>.json` | Every build-specific address and field offset, one file per game build. Built into the exe |
| `tests/Fm27Editor.Core.Tests`, `tests/Fm27Editor.App.Tests` | Unit tests against a fake address space |
| `tests/golden/<build>.players.json` | Player values copied from the game UI: the reference for `verify` and `find-offsets` |
| `re/tools/memscan` | Read-only memory scanner used to find the FM27 layout |
| `AGENTS.md` | Project state, verified facts, open questions, roadmap |

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

For Xbox / Game Pass, `re/tools/gamepass-rva-map.ps1` builds the profile from the Steam one by matching class names
(RTTI), in a few seconds.

## Roadmap

Done:

- Steam and Xbox / Game Pass, one profile per build.
- Players, staff, clubs, nations, stadiums, cities, competitions and awards in their own screens.
- Mass edit, team presets, freezer, export, advanced search, attribute export / import.
- Club kits and colours, finances and sponsors; competition VAR, prize money, squad rules and standings; nation
  agreements, transfer preferences and EuroCoefficients.

Next:

- Move a player on a set date and on loan, with a new career history row, as FMRTE does.
- Club and national team tactics; national team squads and staff in the app.
- Kit items: change type, add, remove, duplicate.
- Work permits, promises, renaming players, injury type.
- City region, language and weather; stadium owner and location.
- Shortlists, favourites, player progress.
- Rebuild the profile for a new game build with one command.
- A smaller, faster-starting exe.

## Disclaimer

This project is not affiliated with or endorsed by Sports Interactive or SEGA. Football Manager is their trademark.
Editing the game's memory can corrupt a save, so keep backups. Use at your own risk.

## Support

If the editor helps you, you can support development:

- [Ko-fi](https://ko-fi.com/quyduongvu) (card or PayPal)
- Vietnamese bank transfer (VietQR, Vietcombank): scan with any Vietnamese banking or e-wallet app

<img src="docs/images/donate-vietqr.png" alt="VietQR, Vietcombank" width="220">

## License

GPL-3.0-or-later. See [LICENSE](LICENSE).
