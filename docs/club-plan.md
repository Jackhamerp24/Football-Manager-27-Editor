# Club: số liệu FMRTE 27 cũ (save 22/7/2026)

Chỉ là dữ liệu đáp án mà code và test trích dẫn. Không phải hướng dẫn làm việc; quy trình hiện tại ở `docs/HANDOVER.md`.
Địa chỉ RAM trong file chỉ đúng cho phiên save cũ đó.

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

