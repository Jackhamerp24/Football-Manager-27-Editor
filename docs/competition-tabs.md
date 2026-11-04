# Trang Competition của FMRTE 27 — bảng kiểm kê

Nguồn: nhãn field lấy từ `Assets\Languages\english.json` của FMRTE 27 (bản build 3, `E:\FMRTE 27.0.0-build3`),
cấu trúc tab theo quan sát ngày 3/11/2026 trong `re/FM27-findings.md` mục "Competitions" và ảnh người dùng gửi.
Cột "Editor": **Có** = đọc/sửa được qua writer có guard; **Chưa** = chưa làm; **Không làm** = ngoài phạm vi.
Cột "Đo": **x2** = đã đối chiếu ít nhất 2 giải khác giá trị; **1** = mới một giải; **—** = chưa đo.

## Tab Information

| Field | Nhãn FMRTE | Loại | Editor | Đo |
|---|---|---|---|---|
| League Name | `Attributes.Competition.Name` | text (đọc) | — | — |
| Unique ID | (cột ID trong danh sách) | int32, chỉ đọc | Có (đọc) | x2 |
| Reputation | `Common.Reputation` | int16, 0–200 | Có | x2 |
| Substitution Rules | `Common.SubstitutionRules` | dropdown 60+ giá trị | Chưa | — |
| Foreground Colour | `Common.ForegroundColour` | 4 byte B,G,R,A | Chưa | — |
| Background Colour | `Common.BackgroundColour` | 4 byte | Chưa | — |
| Trim Colour | `Attributes.Competition.TrimColor` | 4 byte | Chưa | — |
| Winter Ball Colour | `Attributes.Competition.WinterBallColor` | 4 byte | Chưa | — |
| Winter Ball Start Date | `Attributes.Competition.WinterBallStartDate` | ngày | Chưa | — |
| Winter Ball End Date | `Attributes.Competition.WinterBallEndDate` | ngày | Chưa | — |
| Enable custom VAR settings | `Attributes.Competition.VarSettings.EnableCustomVarSettings` | check | Chưa | — |
| VAR | `Attributes.Competition.VarSettings.Var` (No VAR / Referee Review / Remote Review / Referee And/Or Remote Review / Unknown VAR Type) | dropdown | Chưa | — |
| VAR - TV Matches Only | `Attributes.Competition.VarSettings.TVMatchesOnly` | check | Chưa | — |
| From date | `Attributes.Competition.VarSettings.FromDate` | ngày | Chưa | — |
| Standings | `Common.Standings` + `Pos/Team/PTS/GS/GA/W/D/L/Prize` | bảng, chủ yếu chỉ đọc | Không làm | — |

## Tab Prize Money

| Field | Nhãn FMRTE | Loại | Editor | Đo |
|---|---|---|---|---|
| Prize (theo vị trí) | `Attributes.Competition.Prize` | bảng position → tiền | Chưa | — |
| Classification Prize Money | `Common.ClassificationPrizeMoney` | tiền | Chưa | — |
| Winner / Loser Prize Money | `Common.WinnerPrizeMoney`, `Common.LoserPrizeMoney` | tiền | Chưa | — |
| Prize Money Per Win / Draw / Defeat | `Common.PrizeMoneyPerWin/"Draw"/"Defeat"` | tiền | Chưa | — |
| nút "Add Prize Money To All Positions" | `Common.AddPrizeMoneyToAllPositions` | nút | Không làm (thao tác UI) | — |

## Tab Rules

| Field | Nhãn FMRTE | Loại | Editor | Đo |
|---|---|---|---|---|
| Maximum squad size | `Attributes.Competition.MaxSquadSize` | số | Chưa | — |
| Minimum squad size | `Attributes.Competition.MinSquadSize` | số | Chưa | — |
| Goalkeepers in the squad | `Attributes.Competition.GoalkeepersInSquad` | số | Chưa | — |
| Outfield players in the squad | `Attributes.Competition.OutfieldPlayersInSquad` | số | Chưa | — |
| Homegrown At Club / in Nation | `Attributes.Competition.HomegrownFromClub/"Nation"` | số | Chưa | — |
| Non-EU players | `Attributes.Competition.NonEuPlayers` | số | Chưa | — |
| First-Year Senior Players In Squad | `Attributes.Competition.FirstYearSeniorPlayers` | số | Chưa | — |
| Reserve players, not homegrown | `Attributes.Competition.NonHomegrownReservePlayers` | số | Chưa | — |
| Squad salary | `Attributes.Competition.SquadSalary` | tiền (ghi chú: giới hạn theo CLB nằm ở finances) | Chưa | — |
| Squad Registration Periods | `Attributes.Competition.RegistrationPeriods` | danh sách ngày mở/đóng | Chưa | — |

## Tab Stages / Fixtures

| Field | Nhãn FMRTE | Loại | Editor | Đo |
|---|---|---|---|---|
| Stages, Ties, Fixtures (đổi đội, ngày, kết quả, swap teams) | `Attributes.Competition.StagesAndFixtures`, `...Stages.*` | cây stage/trận | Không làm (FMRTE cảnh báo có thể hỏng save; ngoài phạm vi phiên này) | — |

## Ghi chú

- FMRTE cảnh báo khi sửa Stages/Fixtures có thể làm hỏng competition/save; mục này không làm.
- Field chỉ đọc trong FMRTE (Name, ID, Standings) thì editor chỉ đọc hoặc bỏ.
- Cột "Đo" sẽ được điền sau khi diff RAM của một lần Save trong FMRTE và sau khi ghi thử trên 2 giải.
