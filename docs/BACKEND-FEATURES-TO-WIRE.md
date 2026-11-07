# Backend đã xong — app chưa có (bàn giao cho agent UI)

Tài liệu này chỉ liệt kê **những gì đã làm xong ở Core/CLI nhưng giao diện chưa dùng tới**, kèm API chính xác,
file cần sửa, test đã có và bằng chứng trên game thật. Đọc xong là nối được, không cần đọc lại lịch sử.

Quy ước chung của mọi writer trong repo (đừng bỏ qua khi nối UI):

- Mọi lần ghi đi qua `backend.Edits.Execute(...)` để có lịch sử/undo.
- Mỗi writer tự kiểm: object còn đúng vtable → giá trị trong RAM còn bằng snapshot → ghi → đọc lại.
  Sai thì ném `StaleObjectException` / `MemoryAccessException` và không ghi gì thêm.
- **Game chỉ đổi màn hình sau khi bấm Continue** — thanh trạng thái phải báo.
- `ClubVisionSnapshot.Bytes` là ảnh chụp **nguyên bản ghi**: sau mỗi lần ghi phải **đọc lại entry** trước khi ghi
  trường tiếp theo, nếu không guard báo snapshot cũ.

---

## Bảng tổng hợp

| # | Tính năng | Backend | Trong app hiện tại | Việc UI phải làm |
|---|---|---|---|---|
| 1 | Club Vision: **tên loại mục tiêu** | `Core/Clubs/ClubVisionTypes.cs` | App có bảng riêng `ClubListLabels.VisionTypes` (App/ClubLists.cs) **thiếu 15 mã**, tên khác game, mã lạ hiện `Unknown (n)` | Thay `ClubListLabels.VisionType(code)` bằng `ClubVisionTypes.DisplayName(code)` — **không hiện số** |
| 2 | Club Vision: **target** | `ClubVisionWriter.SetTarget` | Không có | Thêm ô Target + Apply vào panel chi tiết Club Vision |
| 3 | Club Vision: **status** | `ClubVisionWriter.SetStatus` | Không có | **Không nên hiện** — xem mục 1.3 (sửa không đổi gì trên UI game) |
| 4 | Income Statement ghi 1 dòng | `FinancialStatements.Set` | Có hàng Period/Field/Value/Write value trong ClubView | Giữ lại khi redesign; nếu đổi ID/thứ tự field thì sửa `IncomeValue/ExpenditureValue` |
| 5 | Sponsors thêm/xoá | `ClubSponsorWriter.Add/Remove` | Có thanh "Add sponsor"/"Remove selected" | Giữ; nhớ cảnh báo cấp phát ngoài heap |
| 6 | Debts xoá tất cả | `GameSession.RemoveAllDebts` | Có nút "Clear all debts" | Giữ |
| 7 | Tactical understanding max cả đội | vòng lặp `PlayerFamiliarity.SetTactical(…, 100)` | **Đã bị gỡ** (nút "Max" cũ nằm ở header CLB) | Đặt 1 nút trong trang **Squad** (yêu cầu người dùng: chỉ ở Squad) |
| 8 | Danh sách Stadium | profile `stadium.listOffset = 0x540` | Đọc đúng 29.460 stadium | Không cần gì, trừ khi redesign đổi cách nạp |
| 9 | Stadium/City/Competition/Award thành entity tab | App đã sửa (bỏ split) | Đang chạy | Giữ khi redesign |

---

## 1. Club Vision

### 1.1 Đọc

```csharp
IReadOnlyList<ClubVisionEntry> rows = session.ReadClubVision(teamAddress, loadedClubs);
// ClubVisionEntry:
//   Address, byte TypeCode, byte Importance, DateOnly? StartDate, DateOnly? EndDate,
//   short? ItemIndex, ClubListClub? ClubItem, ClubVisionSnapshot? Snapshot,
//   int? Target, byte? Status        // null khi profile không có offset
```

### 1.2 Tên loại — dùng bảng đã giải mã

```csharp
string text = ClubVisionTypes.DisplayName(row.TypeCode);    // luôn có chữ, không bao giờ chứa số
ClubVisionTargetKind kind = ClubVisionTypes.TargetKind(row.TypeCode);
```

**UI tuyệt đối không hiện mã số** (1, 2, 33, 182…). Dùng `DisplayName`:
mã đã giải mã → đúng chuỗi game; mã chưa thấy → `"Other objective"` (hằng `ClubVisionTypes.UnnamedLabel`).
App hiện có `ClubListLabels.VisionType` trả `Unknown (182)` — **phải thay**, đó là chỗ đang lộ số ra màn hình.
Test `DisplayName_never_shows_the_numeric_code` chặn việc này (kiểm cả không có chữ số trong chuỗi).

Bảng lấy bằng cách đối chiếu **22 dòng màn Club Culture của Man Utd** với **25 bản ghi RAM** (type + importance + ngày
khớp từng dòng), ngày 7/11/2026:

| Code | Game hiển thị |
|---|---|
| 1 | Win the Competition |
| 2 | Qualify For Competition |
| 5 | Challenge for the Competition title |
| 10 | Reach the latter stages of the Competition |
| 33 | Be competitive in the competition |
| 44 | Work within wage budget |
| 45 | Sign players to sell for a profit |
| 60 | Become the most reputable team in Nation |
| 61 | Become the most reputable team in continent |
| 62 | Become the most reputable team in the world |
| 64 | Grow the club's reputation |
| 81 | Maximum one-year contracts for players over the age of X |
| 82 | Maximum two-year contracts for players over the age of X |
| 84 | Minimum X-year contracts for first-team players |
| 104 | Sign players under the age of X for the first team |
| 105 | Sign players under the age of X for the future |
| 120 | Develop players using the club's youth system |
| 121 | Develop the best youth system in the country |
| 122 | Develop division best youth system |
| 150 | Play attacking football |
| 155 | Play entertaining football |

Mã 182/184 (Man Utd có, nằm dưới màn hình) **chưa thấy tên** → `DisplayName` trả `"Other objective"`, không hiện số.

### 1.3 Target và Status

- `Target` = int32 tại `+0x28` (profile `club.lists.visionTarget`). Nghĩa đã đo:
  - 104/105/81/82 → **tuổi** (Man Utd: 23 / 32 / 30)
  - 84 → **số năm hợp đồng** (4)
  - 1/2/5/10/33 → **vòng đấu hoặc thứ hạng mục tiêu** (4, 3, 6, 2, 1)
- `Status` = byte tại `+0x38` (profile `club.lists.visionStatus`), chỉ thấy giá trị 1 (1.922 dòng) và 2 (942 dòng)
  trên 5.251 CLB. **Đổi 1 → 2 trên Man Utd rồi Continue: màn Club Culture không đổi gì.** Vì vậy đây không phải
  "Source Type" mà người dùng thấy (cột đó hiện Board / Unknown (194), nguồn dữ liệu chưa xác định).
  → UI nên **bỏ** field này, hoặc hiện "chưa đặt tên" nếu cần debug.

### 1.4 Ghi

```csharp
var writer = session.ClubVisionWriter;
writer.SetType(teamAddress, row, byte type);             // +0x2C
writer.SetImportance(teamAddress, row, int value);       // +0x2D
writer.SetDates(teamAddress, row, start, end);           // +0x20 / +0x24, giữ bit cờ 0xFE00
writer.SetTarget(teamAddress, row, int target);          // +0x28
writer.SetStatus(teamAddress, row, byte status);         // +0x38
writer.Remove(teamAddress, row);
writer.Duplicate(teamAddress, row, allocator);           // allocator = ((WindowsProcessMemory)session.Memory).Allocate
```

### 1.5 Kiểm chứng

- Test: `ClubVisionTypesTests` (34 case), `ClubListReaderTests.Reads_and_writes_vision_target_and_status` (byte-exact).
- Chạy thật trên save rác (Man Utd): `target 6 → 7 → 6`, `status 1 → 2 → 1`, mỗi lần đọc lại khớp.
- CLI để tự kiểm: `vision <clubId>`, `vision <clubId> set <index> target|status <value> [--write]`,
  `visiondump <clubId> [n]`, `visionscan`, `visionstatus`.

---

## 2. Income Statement — ghi một dòng

```csharp
var statements = session.FinancialStatements;
IReadOnlyList<ClubFinancialPeriod> periods = statements.Read(clubAddress);
long current = periods[periodIndex].GateReceipts;                 // expected
statements.Set(clubAddress, periodIndex, income: true, key: "gateReceipts", expected: current, value: newValue);
```

- `key` ∈ `ClubStatementOffsets.IncomeKeys` / `ExpenditureKeys`.
- Period 0 = tháng này, 1 = tháng trước, 2 = mùa này; block 0x118 byte, income ở `block+0x9C`.
- App đã có UI (Period / Field / Value / Write value) — chỉ cần giữ khi redesign và map đúng 21 key.
- CLI: `income <clubId>`, `income <clubId> set <period> <income|expenditure> <key> <value> [--write]`.
- Đã chứng minh: RAM `2.428.554 → 3.428.554 → 2.428.554`; **game hiện đúng 3.428.554 sau Continue**.
- Lưu ý khi hiển thị: "Total income / profit" của app là tổng các dòng; cột Profit của game là lợi nhuận tháng đã trừ lương
  → không so trực tiếp được, phải so từng dòng.

---

## 3. Sponsors — thêm / xoá

```csharp
var sponsors = session.ReadSponsors(clubAddress);
session.SponsorWriter.Add(clubAddress, byte incomeType, int total, DateOnly start, DateOnly end);
session.SponsorWriter.Remove(clubAddress, entry);
```

- Bản ghi 0x18 byte; vector ở `club+0x130` (con trỏ tới mảng con trỏ), tự grow bằng `VirtualAllocEx`.
- `incomeType` phải qua `ClubSponsorWriter.SupportedIncomeType`; nhãn ở `ClubListLabels.SponsorIncomeTypes`.
- Cảnh báo cho người dùng: bản ghi/mảng nằm **ngoài allocator của game**.
- Test: `ClubSponsorListTests` (5). CLI: `clubsponsor <clubId> add|remove …`.
- **Đã chứng minh qua Continue**: Sheffield 28/11 → 14 sponsor, game không crash, màn Finance → Sponsors có dòng mới.

---

## 4. Debts — xoá tất cả

```csharp
session.RemoveAllDebts(clubAddress);   // thu vector nợ về rỗng, không giải phóng bản ghi
```

- Test: `ClubDebtClearTests`. CLI: `clubdebt <clubId> clear [--write]`.
- Đã chứng minh qua Continue: Man Utd 3 khoản → 0, màn Debt and Loans trống, không crash.
- **Không có hàm thêm nợ** — xoá là không khôi phục được trong phiên.

---

## 5. Tactical understanding — max cả đội

```csharp
var access = session.PlayerFamiliarity;
foreach (var team in club.Teams)
    foreach (var p in session.ReadSquad(team.Address))
        if (access.Read(p.Address) is { } snapshot)
            access.SetTactical(snapshot, 100);
```

- Đây là toàn bộ nội dung nút "Max" trước nằm ở header CLB (đã gỡ theo yêu cầu "chỉ để 1 nút ở Squad").
- Đã chạy thật: Bruno Fernandes 77 → 100, Kobbie Mainoo → 100 (CLI `fam` đọc lại).

---

## 6. Offset profile đã sửa trong đợt này

`offsets/27.0.0+2493426-gamepass.json` (và `…+2493426.json` cho các key dùng chung):

| Key | Giá trị | Vì sao |
|---|---|---|
| `stadium.listOffset` | `0x780` → **`0x540`** | vector stadium thật (29.460 mục, vtable 0x4D970D8); 0x780 rỗng |
| `club.financeLists.statements.*` | block 0x118, income `+0x9C` | Income Statement |
| `club.lists.visionHolderVtables` | `0x4D6AEE8 0x4D6AF68 0x4DBCC98` | team CLB dùng class khác team tuyển |
| `club.lists.cultureVtables` | `0x4ACE098 0x4D79198 0x4B0E5C8` | như trên; thiếu là Club Vision trả 0 dòng |
| `club.lists.visionTarget` / `visionStatus` | `0x28` / `0x38` | target/status |

Profile Steam (`27.0.0+2493426.json`) **chưa đo lại** `stadium.listOffset` (vẫn `0x560`).

---

## 7. Việc backend còn lại (chưa ai làm)

1. **Register players for domestic division** — chưa xác minh offset.
2. **Nation transfer values** (ma trận 20 mức danh tiếng) và **nation rivals** — chưa có số offset.
3. **Tổng thu sponsor** `finance+0x560` sau add/remove — chưa rõ game tự tính lại hay phải ghi.
4. **Source Type** của Club Vision (Board / Unknown 194) — chưa tìm ra field.
5. **Nation/Continental registration (homegrown)** — FM Studio có `RegisterPlayerForDomesticDivision`/homegrown, editor chưa có.
