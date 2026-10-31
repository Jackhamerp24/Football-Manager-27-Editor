# Club module: plan and lanes

Read [AGENTS.md](../AGENTS.md) first (sandbox, test discipline, UI rules, hard rules). This file is the shared plan for the
Club module, done by 5 agents at once. **Each lane edits only the files it owns.** Shared state goes into your own findings
file; Claude merges offsets into the profile.

## Goal

A Club section in the app like FMRTE 27's: pick a club, see and edit Information, Finances, Supporters, Tactical
Understanding, then lists (sponsors, debts, club vision, affiliations, regional divisions, kits, staff). Every value that
FMRTE can edit must be readable **and writable**.

## Live game state (do not change)

- FM27 `27.0.0+2486695` is running with a save the user has already loaded. **Do not load another save** and do not
  click Continue in FM: addresses and values below belong to this exact state.
- The game date is 22/7/2026. Test club: **Sheffield United, ID 708** (England, Sky Bet Championship).
- FMRTE 27 is open on Sheffield United. It runs as admin, so other programs cannot click it. When you need FMRTE to
  change a value (to tell two candidate offsets apart), **ask the user** to change it and say what to set it to.
- **Writing to the game is only done by Claude or the user.** Your tools are read-only.

## Tools (read-only)

Build once, then copy to `%TEMP%` (a copy in the workspace cannot open `fm.exe`):

```bash
dotnet build src/Fm27Editor.Tools -c Release
```

```bash
cp -r src/Fm27Editor.Tools/bin/Release/net10.0 "$TEMP/fm27tools-<yourname>"
```

- `Fm27Editor.Tools.exe club 708` prints the club address, finance object, clause holder, teams and module base.
- `Fm27Editor.Tools.exe probe <hexAddr> <hexSize> <depth> <value>...` searches int32/int16 (and a single byte when only
  one value is given) in `[addr, addr+size)`, and with depth ≥ 1 in the first 0x1000 bytes of every object a qword
  there points to. Output `+0x100->+0xD8 int32 = 1889` means: read the pointer at club+0x100, the field is at +0xD8.
- `Fm27Editor.Tools.exe clauses 708` lists transfer clauses (already done, a worked example of a list).
- `re/tools/memscan` (`dump`, `refs`, `findval`, ...) for harder cases. Read its `Program.cs` header.

Sheffield United in this session: club `0x1AB691E24E0`, finance `0x1AB0ED6C750`, first team `0x1AB312D8A08`,
U21 `0x1AB5A0A4538`, U18 `0x1AB5A0A4480`, module `0x7FFAF3E20000`. Re-check with `club 708` before you start.

Example, already measured with probe (use these as a pattern, then confirm them):

| Field | Value | Candidate |
|---|---|---|
| Year founded | 1889 | club `+0x100->+0xD8` int32 |
| Chairman status | 6916 | club `+0x100->+0xDE` int16 |
| Season ticket holders | 19725 | club `+0x100->+0xC0` int32 |
| Social media followers | 574287 | club `+0x100->+0xF8` int32 |
| Average attendance | 27676 | club `+0xB0->+0x68` int32 |
| Maximum attendance | 34595 | club `+0xB0->+0x70` int32 |
| Wage budget | 496589 | finance `+0x808` and `+0x834` (ambiguous) |
| Max wage | 25000 | finance `+0x80C` |
| Wage used | 490710 | finance `+0x814` (already in the profile as `wageSpend`) |
| Transfer budget | 6000000 | finance `+0x7C4`, `+0x7C8`, `+0x7D4` (ambiguous; season vs remaining) |

## Values on screen (FMRTE 27, Sheffield United, unedited)

**Information / Overview:** Name Sheffield United, Unique ID 708, Nation England, Year founded 1889, Status Professional,
Chairman Status 6,916, Morale 8, Average Attendance 27,676, Minimum Attendance 10,000, Maximum Attendance 34,595,
Season ticket holders 19,725, Social Media Followers 574,287, Background colour dark red, Foreground colour white,
Allow Custom Logo ticked, Ownership Type Limited Liability (dropdown), Chairperson Title Chairperson (dropdown).
**Teams:** Team Sheffield United, Allow Licensed Kits ticked, Reputation 6,200, Stadium Bramall Lane.
**Facilities:** Training 13, Youth Coaching 13, Youth Facilities 12, Youth Recruitment 15, Youth Importance 14,
Corporate Facilities 12, Training Ground Shirecliffe.

**Finances / Overview:** Balance £5,790,291, Transfer Budget (Season) £6,000,000, Transfer Budget Remaining £6,000,000,
% of transfer revenue made available 100, Wage Budget £496,589, Wage Used £490,710, Max Wage £25,000, Rich Benefactor
Underwriter (dropdown), Average Match Ticket Price £35, Stadium Rental Per Year £0.
**Transfer embargo:** Reason None, Start/End/Appeal date 1/1/1900, Age restriction 0.
**Sponsors and Other** (Income type, total value, start, end): Stadium 125000 19/1/2024–30/6/2027; Stadium 50000
1/7/2026–30/6/2027; Stadium 100000 1/7/2026–30/6/2027; Total Commercial Income 8316273 1/7/2024–30/6/2025; Kit Sponsor
500000 1/6/2026–30/6/2027; Stadium 100000; Stadium 50000; Stadium 100000; Stadium 35000 (all 1/7/2026–30/6/2027);
Back of Shirt 250000 1/7/2026–30/6/2027; Kit Manufacturer 4500000 1/7/2026–30/6/2029; Shirt Sleeve 100000
1/7/2026–30/6/2027; General Sponsor 10591455 30/11/2024–1/12/2027.
**Debt and Loans** (source, original debt, start, end): Loan Repayable On Promotion To Top Division 11000000
1/7/2025–30/6/2028; Bank 23954758 1/7/2025–30/6/2035; Bank 21503936 1/7/2025–30/6/2030.

**Club Vision** (type, source, importance, item, for competition, start, end): Reach the playoffs, Board, 10, Sky Bet
Championship, True, 15/8/2026–3/5/2027; Be competitive in the competition, Board, 9, The Emirates FA Cup, True,
19/9/2026–3/5/2027; Be competitive, Board, 4, Carabao Cup, True, 1/8/2026–3/5/2027; Reach the playoffs, Board, 10, Sky
Bet Championship, True, 15/8/2026–3/5/2027; Win promotion, Board, 2, Premier League, True, 13/7/2026–4/5/2029; Play
attacking football, Board, 5; Develop players using the youth system, Board, 5; Sign players under the age of X, Board,
4; Work within wage budget, Board, 10, start 13/7/2026; Unknown (181), Board, 7, Sheffield Wednesday; Unknown (183),
Board, 8, Leeds United; Unknown (183), Board, 1, Nottingham Forest (last three start 13/7/2026).

**Affiliations:** Sheffield United Women's Club; parent and feeder Sheffield United, start 17/4/2024, end 1/1/1900,
type Women's Club, annual fee £0, all friendship benefits unticked.
**Regional Divisions:** Enterprise National League North level 6 year 0; Northern Premier League Premier Division 7 / 0;
Northern Premier League Division One East 8 / 0.
**Transfer Center:** Steve Brown → Sheffield United £0; Liam McLaughlin Sheffield United → Burnley £130,050; same → Ipswich
£130,050; Jack Patchett → Sheffield United £0; Liam McLaughlin → West Ham £130,050; Ryan Giggs → Sheffield United £0;
all "Player accepted".
**Kits (Home, shirt):** foreground red, background white, outline black, number white, number outline red, item style 3.
**Staff:** 44 staff, for example Managing Director Stephen Bettis 52 CA 120 PA 133; Manager Chris Wilder 58 CA 129 PA 140.

**Supporters:** distribution Hardcore 12 %, Core 40 %, Family 15 %, Fair Weather 17 %, Corporate 2 %, Casual 14 %
(total 100 %); profile Loyalty 15, Passion 15, Patience 6, Affluence 7, Temperament 7, Expectations 10.

**Tactical Understanding** (per team; Passing Style, Build Up, Chance Creation, Line of Engagement, Pressing Intensity,
Pressing Trap, Position/Role): first team 60, 60, 62, 60, 60, 62, 47; U21 62, 62, 62, 62, 62, 63, 52.

## How a field is recorded

Single-value fields are named by the `ClubField` enum in `src/Fm27Editor.Core/Clubs/ClubFields.cs` (do not rename
members; ask Claude to add one). A mapping is one JSON entry, exactly this shape:

```json
"YearFounded": { "root": "club", "path": "0x100->0xD8", "type": "int32" }
```

- `root`: `club` or `team` (team fields such as reputation and tactical understanding start from a team object).
- `path`: every offset but the last is a pointer to follow; the last is the field.
- `type`: `byte`, `sbyte`, `int16`, `int32`, `bool`, `date` (int16 day of year + int16 year), `colour`.
- `scale` (optional): when the stored number is the shown number times N.

A mapping counts as **confirmed** only when (a) the value matches on Sheffield United **and** one more club with
different values (pick any club from `club <id>`; get its FMRTE values from the user), or (b) the user changed it in
FMRTE and the probe saw that address change. Ambiguous candidates (two addresses, same value) stay **candidate** until
one of these checks separates them. Write Observed / Inferred / Unknown separately.

Lists (sponsors, debts, vision, affiliations, divisions, kit items, transfer center): record the container path
(club → … → vector begin/end/capacity or linked list), the element size and vtable, and each element's field offsets,
in the same Observed / Inferred / Unknown style. `TransferClauseReader.cs` is the worked example.

## Lanes

| Lane | Agent | Owns (only these files) | Output |
|---|---|---|---|
| A. Core + merge | Claude | `src/Fm27Editor.Core/**`, `offsets/*.json`, `tests/Fm27Editor.Core.Tests/**`, `src/Fm27Editor.Tools/**`, `AGENTS.md`, this file | profile, readers/writers, tests, live writes |
| B. App UI | Gemini 4 Argon | `src/Fm27Editor.App/Club*.cs` (new files), `tests/Fm27Editor.App.Tests/Club*.cs`; one registration line in `Modules.cs`/`MainWindow.cs` | Clubs screen |
| C1. Scalars: finance | DeepSeek V4.1 Flash | `re/club/scalars-finance.md`, `re/club/scalars-finance.json` | Finances overview, transfer embargo, Information overview (incl. colours, status/ownership/chairperson dropdowns) |
| C2. Scalars: other | Gemini 3.8 Flash | `re/club/scalars-other.md`, `re/club/scalars-other.json` | Facilities, Supporters, team reputation + Allow Licensed Kits, Tactical Understanding |
| D. Lists + visual checks | GPT-6.1 Sol | `re/club/lists.md`, `re/club/ui-review.md` | layouts for sponsors, debts, club vision, affiliations, regional divisions, kits, transfer center; screenshot review of lane B |

Shared rules for every lane:

- **Report progress in `re/club/STATUS.md`** (one file for all lanes; its own rules at the top say how to edit it
  without overwriting others). Update your section whenever your status changes, and read the other sections.

- **Batch questions to the user.** When you need FMRTE changed to separate candidates, collect every request first and
  ask once, as a short list: field, what to set it to, and that it should be set back afterwards.
- Never write to the game. Never rename `ClubField` members; ask Claude in your findings file to add one.
- `re/` is local only (not in git). Keep findings files short: a table of field → path → type → status, then notes.

Lane notes:

- **B (Argon):** build against `backend.Session.ClubFields` (`ReadAll(club, firstTeam)`, `Read`, `Set`) and the
  `ClubField` enum; clubs come from `backend.Session.LoadClubs(players)`. Show a field only when `IsMapped`; edit through
  a draft + Save like the player Attributes tab; write through `backend.Edits.Execute(() => access.Set(root, field,
  expected, value))`. Layout like FMRTE: left section list with Information, Finances, Supporters, Tactical Understanding
  first; club picker by name or ID like Move to club. Labels and min/max per field live in your own `ClubFieldInfo.cs`.
  Follow AGENTS.md section 6 (no taglines, no helper text, dense, one accent colour). The FMRTE screenshots the user sent
  are described in "Values on screen"; ask the user for the images if you want to see them. Until fields are mapped the
  screen is mostly empty; Claude adds mappings as lanes C1/C2/D confirm them, so check that a new mapping appears without
  code changes. Run tests from a `%TEMP%` copy (`dotnet vstest`).
- **C1 (DeepSeek, no image input):** every number you need is written above. Start with the ambiguous finance
  candidates (transfer budget season vs remaining, wage budget at two addresses). Dropdowns (Status, Ownership Type,
  Chairperson Title, Rich Benefactor, Embargo Reason) are small ints; record the raw value and that the name mapping is
  unknown. Colours: Sheffield background is dark red, foreground white.
- **C2 (Gemini Flash):** Supporters percentages may be stored ×10 or ×100; Tactical Understanding and Team Reputation are
  per team (root `team`): first team and U21 have different values, which tells the right team apart. Facilities values
  repeat (13, 13, 12, 15, 14, 12), so confirm the order on a second club.
- **D (GPT Sol):** use the distinctive numbers (10591455, 8316273, 23954758, 21503936, 4500000) to find list elements with
  `probe`/`memscan`, then `memscan refs` to walk up to the container and from there to the club. Report as in the worked
  example; do not write code. When lane B has a build, run the app from a `%TEMP%` copy, screenshot the Clubs screen and
  write problems into `ui-review.md` (clipped controls, wrong values against "Values on screen", rule breaks).

## Done means

- Every confirmed field reads correctly in the app for Sheffield United and one other club, and a write made in the app
  shows in FMRTE after refresh (Claude or the user does the write).
- Core tests for each field with literal offsets and the FMRTE value as the oracle; one mutation per new reader proven
  to fail (see AGENTS.md).
