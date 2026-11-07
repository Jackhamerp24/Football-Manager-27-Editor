# Trang Competition của FMRTE 27 — bảng kiểm kê

Nguồn: nhãn field lấy từ `Assets\Languages\english.json` của FMRTE 27 (bản build 3/5, `E:\FMRTE 27.0.0-build3`),
ảnh người dùng gửi cho Premier League (ID 11) và Sky Bet Championship (ID 12) ngày 5/11/2026, và diff RAM sau một lần
Save trong FMRTE (`re/FM27-findings.md`, mục "Competition").
Cột "Editor": **Có** = đọc và ghi qua writer có guard; **Chưa** = chưa làm; **Không làm** = ngoài phạm vi.
Cột "Đo": **x2** = đã đối chiếu/ghi trên ít nhất 2 giải khác giá trị; **1** = mới một giải; **—** = chưa đo.

## Tab Information

| Field | Nhãn FMRTE | Dạng | Editor | Đo |
|---|---|---|---|---|
| Name | `Attributes.Competition.Name` | text | Không chỉnh được (người dùng xác nhận 5/11) | — |
| Unique ID | (cột ID) | int32 `+0xC` | Không chỉnh được (người dùng xác nhận 5/11) | — |
| Reputation | `Common.Reputation` | int16 + bản sao `+0x18A`, 0–200 | Có | x2 (11: 190, 12: 136, 130286: 155) |
| Substitution Rules | `Common.SubstitutionRules` | byte `+0xFD`, 0xFF = "Do Not Override Sub Rules" | Có (nhãn mới xác nhận 0xFF và 0x11 = "5 from 9"; giá trị khác hiện `Value 0xNN`) | x2 (0xFF và 0x11; đã ghi 0x22 lên Süper Lig) |
| Foreground Colour | `Common.ForegroundColour` | 4 byte B,G,R,A `+0x130` | Có | x2+ (trắng trên cả ba giải) |
| Background Colour | `Common.BackgroundColour` | `+0x134` | Có | x2 (tím đậm / vàng đồng / đỏ) |
| Trim Colour | `Attributes.Competition.TrimColor` | `+0x138` | Có | x2 (FMRTE ghi vàng trên PL; Championship đen, đã ghi #FF00FF00) |
| Winter Ball Colour | `Attributes.Competition.WinterBallColor` | `+0x15C` | Có | x2 (FMRTE ghi đỏ trên PL; Championship vàng, đã ghi #FF00FFFF) |
| Winter Ball Start Date | `Attributes.Competition.WinterBallStartDate` | int16 ngày trong năm + int16 năm `+0x150`, giữ bit cờ | Có | x2 (2026-11-11 / 2026-10-25 / 2015-11-15; đã ghi 2026-10-20 và 2015-11-01) |
| Winter Ball End Date | `Attributes.Competition.WinterBallEndDate` | `+0x154` | Có | x2 (2027-03-28 / 2016-03-15; đã ghi 2016-03-01) |
| Enable custom VAR settings | `Attributes.Competition.VarSettings.EnableCustomVarSettings` | byte VAR+0x16 | Có nếu object VAR tồn tại | PL và Bundesliga; FMRTE, UI và CLI |
| VAR | `Attributes.Competition.VarSettings.Var` | byte VAR+0x14 | Có | PL và Bundesliga; các mã 1–5 đã ghi riêng |
| VAR - TV Matches Only | `Attributes.Competition.VarSettings.TVMatchesOnly` | byte VAR+0x15 | Có | PL và Bundesliga; UI, CLI, FMRTE |
| From date | `Attributes.Competition.VarSettings.FromDate` | ngày VAR+0x10 | Có | PL 05/05/2028, Bundesliga 01/07/2027; UI và CLI đã ghi |
| Standings | `Common.Standings` (Pos/Team/Games Played/W/D/Losses/GS/GA/Points) | bảng | Có với giải một stage đã đo | FMRTE PL 20 đội; CLI ghi 2 đội, UI và RAM đã xác nhận |

Standings (người dùng, kinh nghiệm FM26 + FMRTE): game lưu điểm riêng, **không tính lại** từ thắng/hoà/thua. Sửa điểm thì
game giữ số mới, cộng tiếp sau mỗi trận và xếp hạng lại theo điểm (ví dụ 3T-2H-2B mà 15 điểm). Editor ghi thẳng từng ô,
không ép điểm khớp với thắng/hoà/thua. Chưa kiểm lại trên FM27.

VAR ở object `[competition+0xD0]`; chưa tạo object khi null, chưa thử Steam hay Continue.

## Tab Prize Money

| Field | Nhãn FMRTE | Dạng | Editor | Đo |
|---|---|---|---|---|
| Prize Money Per Win / Draw / Defeat | `Common.PrizeMoneyPerWin/"Draw"/"Defeat"` | int32 (£) | Có profile Game Pass/Steam; Steam chưa thử | x2: CLI ghi/đọc lại ba field cho PL và Championship; FMRTE Refresh PL hiện 1/2/3 |
| Classification Prize Money (Enabled + Pos 1..N) | `Common.ClassificationPrizeMoney` / `Attributes.Competition.Prize` | check + vector int32 | Có Enabled và sửa vị trí; UI/CLI Game Pass đã thử; Steam chưa thử | FMRTE và CLI khớp PL 20 dòng/Bundesliga 18 dòng; CLI ghi hai giải, UI ghi PL và FMRTE Refresh đọc lại |
| Winner / Loser Prize Money | `Common.WinnerPrizeMoney`, `Common.LoserPrizeMoney` | tiền | Chưa | — |
| nút "Add Prize Money To All Positions" | `Common.AddPrizeMoneyToAllPositions` | nút | Không làm (thao tác UI) | — |

Đo được: FMRTE ghi Per Win/Draw/Defeat = 1.111.111 / 2.222.222 / 3.333.333 và Pos 1/2 = 12.345.678 / 12.345.679.
Các giá trị nằm trong object riêng (`vtable module+0x4D7EA18`, Game Pass): `+0x14C/+0x150/+0x154` là
per win/draw/defeat, `+0x1E0` là vector int32 của prize theo vị trí. Frida đối chiếu hai giải cho đường
`competition+0xB0 → holder+0x1C0 (vector một stage) → stage+0xF0 → group`; writer từ chối trường hợp nhiều stage.

## Tab Rules

| Field | Nhãn FMRTE | Dạng | Editor | Đo |
|---|---|---|---|---|
| Maximum squad size / Minimum squad size | `Attributes.Competition.MaxSquadSize/MinSquadSize` | số | Maximum có UI/CLI, Minimum chưa. Giải không có dòng luật `0x9` thì đọc/ghi byte `group+0xF0` (Bundesliga = 100, 3 bản) | Game Pass CLI ghi và đọc PL 33→34, Championship 26→27, Bundesliga 100→101; UI PL 34→35 và Bundesliga 100, RAM đọc lại; Steam chưa thử |
| Goalkeepers / Outfield players in the squad | `Attributes.Competition.GoalkeepersInSquad/OutfieldPlayersInSquad` | số | Outfield có UI/CLI; Goalkeepers: mã `0x57` (UI đọc/ghi) — mới xác nhận trên Champions League | CL ảnh FMRTE = 2; Spanish UI/CLI cho Outfield |
| Homegrown At Club / Homegrown in Nation | `Attributes.Competition.HomegrownFromClub/HomegrownFromNation` | số | Nation: PL, Championship và hai giải Đức; At Club: hai giải Đức | Game Pass CLI/FMRTE xác nhận cả hai giải Đức; UI Bundesliga lưu đồng thời hai trường, CLI đọc lại; Steam chưa thử |
| Non-EU players | `Attributes.Competition.NonEuPlayers` | số | Có UI/CLI | Spanish UI/CLI, Ligue 1 CLI |
| Also Applies To (FMRTE) | — | danh sách giải dùng chung value của luật (so `common` + index group) | Có UI/CLI | CL → Europa League + Conference League, đúng ảnh FMRTE; PL/League One/Bundesliga không hiện |
| First-Year Senior Players In Squad | `Attributes.Competition.FirstYearSeniorPlayers` | số | Chưa | — |
| Reserve players, not homegrown | `Attributes.Competition.NonHomegrownReservePlayers` | số | Chưa | — |
| Squad salary | `Attributes.Competition.SquadSalary` | tiền | Chưa | — |
| Squad Registration Periods | `Attributes.Competition.RegistrationPeriods` | danh sách ngày | Chưa | — |

FMRTE cho sửa "Maximum squad size" 25 → 26 và "Homegrown in Nation" 8 → 9 trên Premier League; các giá trị này
không nằm trong object competition nên cũng chờ đường dẫn như Prize Money.

## Tab Stages / Fixtures

| Field | Nhãn FMRTE | Dạng | Editor | Đo |
|---|---|---|---|---|
| Stages, Ties, Fixtures (đổi đội, ngày, kết quả, swap teams) | `Attributes.Competition.StagesAndFixtures`, `...Stages.*` | cây stage/trận | Không làm (FMRTE cảnh báo có thể hỏng save; ngoài phạm vi) | — |

## Giá trị đọc từ ảnh FMRTE (5/11/2026)

| Mục | Premier League (11) | Sky Bet Championship (12) | Süper Lig (130286) |
|---|---|---|---|
| Reputation | 189 → 190 (FMRTE ghi) | 136 | 155 |
| Substitution Rules | Do Not Override → "5 from 9" (FMRTE ghi) | Do Not Override | Do Not Override |
| Foreground / Background | trắng / tím đậm | trắng / vàng đồng | trắng / đỏ |
| Trim | đen → vàng (FMRTE ghi) | đen | đỏ |
| Winter Ball Colour | vàng → đỏ (FMRTE ghi) | vàng | vàng |
| Winter Ball Start / End | 25/10/2026 → 11/11/2026 (FMRTE ghi) | 25/10/2026 – 28/3/2027 | 15/11/2015 – 15/3/2016 |
| Var Settings | có panel, bỏ tick | không có panel | chưa xem |
| Prize Money Per Win/Draw/Defeat | £0 / £0 / £0 → 1.111.111 / 2.222.222 / 3.333.333 (FMRTE ghi) | £0 / £0 / £0 | chưa xem |
| Classification Prize Money | Enabled, pos 1 53.100.000 … 20 2.600.000 → pos 1/2 đổi | không tick, bảng rỗng | chưa xem |
| Squad Registration | 25 (Max.) / 8 (Min.) → 26 / 9 (FMRTE ghi) | 25 / 8 | chưa xem |
