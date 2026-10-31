# AGENTS.md — Football Manager 27 Editor

Đọc hết file này trước khi sửa code. Nó mô tả app làm gì, chạy thế nào, thiết kế ra sao, cái gì đã kiểm chứng, và quy tắc làm việc.
Chi tiết RE thô (offset, ảnh chụp bộ nhớ, giả thuyết) nằm ở `re/FMRTE26-findings.md` (chỉ có trên máy, không lên git).
Nghiên cứu RCA có quy tắc riêng: [docs/rca-research-rules.md](docs/rca-research-rules.md).

## 1. App là gì

Editor dùng **trong lúc đang chơi Football Manager 27**, chỉ cho save offline / một người chơi. Giống FMRTE 26 với FM26:
mở process `fm.exe`, đọc và ghi bộ nhớ của module `game_plugin.dll` (engine C++ của SI, không phải IL2CPP).
Không hook, không inject code, không sửa file game. Tên app: **Football Manager 27 Editor**.

- FMRTE 26 đã được RE **chỉ để hiểu cơ chế**. Không copy code, asset hay chuỗi của FMRTE. Mọi offset FM27 tự đo trên game thật.
- IGE (In-Game Editor chính thức của SI, chạy trên FM26) được dùng làm "đáp án": chụp RAM trước/sau một thao tác IGE
  để biết thao tác hợp lệ phải đổi những gì.
- Ưu tiên của người dùng: **mọi thao tác nhanh nhất có thể**. App không được phình tới vài trăm MB.
- Repo public: https://github.com/Jackhamerp24/Football-Manager-27-Editor. Release do GitHub Actions build từ source
  (`.github/workflows/build.yml`, tag `v*`), có SHA256 và build attestation. Hai exe chạy độc lập: `Fm27Editor.exe` (app),
  `Fm27Editor.Tools.exe` (CLI). Profile offset được nhúng vào exe.

## 2. Môi trường

| | |
|---|---|
| Dự án | `C:\Users\Duong\Desktop\FMRTE clone 27` (git, nhánh `main`) |
| FM27 | `C:\Program Files (x86)\Steam\steamapps\common\Football Manager 27`, build **`27.0.0+2486695`** |
| FM26 | `F:\SteamLibrary\steamapps\common\Football Manager 26`, build `26.3.2+2329565`, có IGE |
| FMRTE 26 | `C:\BraCa Soft\FMRTE 26` (dùng làm đáp án trên FM26) |
| Stack | C# / .NET 10, Avalonia 11.3 |
| Shell | PowerShell 5.1 và Git Bash. Không có Python. File CRLF: dùng tool edit, `.Replace()` có thể không khớp mà không báo lỗi |

**Sandbox:** exe và testhost chạy từ trong workspace có thể **không thấy `fm.exe`** (OpenProcess bị chặn).
Copy thư mục `bin/Release/net10.0` ra `%TEMP%` rồi chạy (`dotnet vstest <dll>` cho test). FM26 và FM27 có thể chạy cùng lúc
(cùng tên `fm.exe`): `GameProcess.Locate` ưu tiên process có `game_plugin` 27.x; `memscan` nhận biến môi trường `FM_PID`.

## 3. Cấu trúc

| Path | Nội dung |
|---|---|
| `src/Fm27Editor.Core` | Toàn bộ backend: bộ nhớ, kiểm build, profile offset, đọc/ghi Player, Contract, Club/Team, Transfer, offset finder |
| `src/Fm27Editor.App` | App Avalonia (code-behind, không XAML) |
| `src/Fm27Editor.Tools` | CLI: `players`, `verify`, `set`, `contract`, `move`, `clauses <club id>`, `transfer peek/dump/offers/diff`, `find-offsets` |
| `offsets/<build>.json` | Mọi RVA/offset phụ thuộc build, nhúng vào Core lúc build |
| `tests/Fm27Editor.Core.Tests` | Unit test trên bộ nhớ giả (`FakeMemory`, `TestWorld`) |
| `tests/Fm27Editor.App.Tests` | Test view model / nhãn |
| `tests/golden/<build>.players.json` | Giá trị chép từ UI game, đáp án cho `verify` và `find-offsets` |
| `re/tools/memscan` | Công cụ dò bộ nhớ (chỉ đọc, trừ lệnh `write`) |
| `docs/club-plan.md` | Kế hoạch mục Club: số liệu FMRTE, công cụ `club`/`probe`, chia lane cho 5 agent |

## 4. Core hoạt động thế nào

**Kết nối:** `GameSession.Attach(GameSession.LoadProfiles(dir), writable)`.
- Tìm `fm.exe` + `game_plugin.dll`, chọn profile có chuỗi build khớp tại `BuildStringRva`.
- Sai build → `UnsupportedBuildException`. Game không chạy → `GameNotRunningException`.
- Profile nhúng sẵn; file JSON trong thư mục `offsets/` cạnh exe sẽ thay profile nhúng cùng build.

**Mô hình đọc:** mọi dữ liệu là snapshot bất biến.
- `PlayerRepository.LoadAll()` đi theo mảng con trỏ cầu thủ ở `game_plugin+0x57037C0`, kiểm vtable từng object.
  Đọc ~23–50 nghìn cầu thủ kèm tên trong 0,4–0,7 s.
- `PlayerRecord` gồm chỉ số, vị trí, personality, CA/PA, danh tiếng, tên, chiều cao, ngày sinh, hợp đồng, CLB.
- **Địa chỉ đổi sau mỗi lần tải save.** Không bao giờ lưu địa chỉ giữa các phiên.

**Mô hình ghi:** mọi lần ghi đều qua writer có guard. App không gọi `TryWrite` trực tiếp. Mỗi lần ghi:
1. object vẫn đúng vtable;
2. giá trị trong RAM vẫn bằng snapshot;
3. ghi;
4. đọc lại.

Sai ở bước nào thì ném `StaleObjectException` / `MemoryAccessException`, không ghi gì thêm.
- `PlayerWriter`: chỉ số (lưu ×5), vị trí, personality, CA/PA, chiều cao, ngày sinh.
- `ContractWriter` (`session.Contracts`):
  - `Read(player)` → `ContractDetails` (mọi field + `Terms`);
  - setter theo snapshot: lương, ngày bắt đầu/ký/hết hạn/vào CLB, loại hợp đồng, thưởng trung thành, số áo,
    playing time hiện tại + tương lai + năm, độ hài lòng;
  - `SetTerm` / `AddTerm` / `RemoveTerm` cho thưởng/điều khoản (vector 8 byte/mục: i32 value, i16 parameter, i16 type).
  - **Ngày trong hợp đồng: 9 bit thấp là ngày trong năm, bit trên là cờ, phải giữ nguyên khi ghi.** 1/1/1900 nghĩa là "không có".
- `TransferService` (`session.Transfers`):
  - `Plan(player, clubs, buyer, fee, team?)` tính sẵn mọi lần ghi;
  - `session.ApplyTransfer(plan)` kiểm lại từng giá trị rồi mới ghi.
  - Làm lại đúng các thay đổi của IGE "Move": tiền 2 CLB (số dư, chi/thu chuyển nhượng, ngân sách, tổng lương),
    team trong hợp đồng + team thi đấu, gỡ khỏi squad vector cũ, thêm vào squad vector mới.
  - **Không** tạo hợp đồng mới (sửa sau bằng `ContractWriter`), không trừ phí đoàn kết / % bán lại cho CLB thứ ba,
    không ghi lịch sử chuyển nhượng.
- `ClubDirectory` / `session.LoadClubs(players)`: CLB và đội lấy từ cầu thủ đã nạp (~0,1 s).
  `ScanAllClubs()` quét cả heap (~15 s), lấy được cả CLB không có cầu thủ.
- `TransferClauseReader` / `session.ReadTransferClauses(club)`: chỉ đọc. Club `+0xB0` → holder, `holder+0` là
  `vector<Clause*>` (lẫn object loại khác, lọc theo vtable). Trả về sell-on, appearance fee, competition bonus của
  cầu thủ CLB đó (`TransferClause.PlayerId` so với `PlayerRecord.Id`).
- **Cấp phát:** khi squad vector hoặc vector điều khoản đầy, Core cấp buffer mới bằng `VirtualAllocEx`
  (`WindowsProcessMemory.Allocate`). Buffer này nằm ngoài allocator của game. Đã chạy ổn qua một trận trên FM26, nhưng vẫn
  có rủi ro khi game tự giải phóng hoặc mở rộng vector đó. UI phải cảnh báo.
- `GameSession.ReadGameDate()`: `game_plugin+0x56F1A18` → object có vtable `+0x4C8C858` → ngày ở `+0x7F4`. Null nếu sai vtable.
- `OffsetFinder` + `find-offsets`: dựng lại profile cho build mới từ RAM + file golden (~25 s).

## 5. App hoạt động thế nào

- `EditorBackend` bọc `GameSession`. `IEditService` là **đường ghi duy nhất**: tuần tự hoá, có lịch sử `EditRecord`
  và Undo. Field cầu thủ mô tả bằng `FieldDescriptor`, panel render từ danh sách descriptor.
  Module đăng ký qua `IEditorModule` (hiện có Players).
- Màn chính:
  - trái: lưới cầu thủ ảo hoá, tìm theo tên không phân biệt hoa/thường và dấu;
  - phải: panel cầu thủ với các tab Attributes / Positions (sơ đồ sân) / Personality / **Contract** / **Move to club**.
- Sửa chỉ số: thay đổi được giữ dạng nháp → Save changes / Discard.
- **Contract** (`PlayerContractView.cs`, nhãn ở `ContractLabels.cs`):
  - bố cục 2 cột như FMRTE, loại hợp đồng và playing time chọn từ danh sách;
  - Bonuses & Clauses hiện tên thật, mỗi mục có Remove, hàng Add với 27 loại điều khoản;
  - Save sẽ parse hết trước khi ghi;
  - Transfer Clauses: sửa %, Signed For, số tiền, số trận, ngày hết hạn (Apply từng dòng); competition bonus hiện tên giải (map theo index `+0x8` của competition, chỉ giải VĐQG của cầu thủ đã nạp); xoá và thêm sell-on (CLB hưởng theo ID).
- Dải ô trên tab: CA, PA, Reputation (Home/Current/World), Height, Born — đều sửa được, lưu bằng Save changes.
- **Move to club** có ô Sell-On %: sau khi chuyển, thêm sell-on CLB mua nợ CLB bán, Signed For = phí chuyển nhượng.
- **Move to club**:
  - CLB tự nạp; tìm theo tên hoặc ID; đội mặc định là đội 1; nhập phí;
  - bảng xem trước hai cột CLB bán | CLB mua (mỗi khoản trước → sau), cảnh báo khi phải cấp phát;
  - Confirm → mở tab Contract.
- **Game chỉ cập nhật màn hình (tài chính, chỉ số) sau khi bấm Continue**, dù RAM đã đổi ngay. Thanh trạng thái phải báo điều này.

## 6. Thiết kế UI (yêu cầu của người dùng, bắt buộc)

- Học độ trực quan của FMRTE, được tự thiết kế.
- **Không AI-slop:** không gradient trang trí, không thẻ bo tròn lồng nhau, không icon minh hoạ vô nghĩa, không emoji,
  không hero banner, không dashboard thống kê cho đẹp.
- **Không tagline, helper text, nhãn phụ.** Cần chữ giải thích mới hiểu thì sửa bố cục. Trạng thái rỗng thì để trống.
  Nhãn tên field kiểu FMRTE (Wages, Expires, Crossing...) thì được, vì đó là dữ liệu.
- Bố cục tự giải thích: chỉ số theo đúng nhóm của game, vị trí vẽ thành sơ đồ sân, màu theo dải như FM.
- **Tốc độ là tính năng:** không spinner che màn hình, nạp ở background, gõ tìm phản hồi tức thì.
- Một màu nhấn (tím), theme tối, mật độ cao (font 13px, hàng thấp), số canh phải.

## 7. Đã kiểm chứng trên game thật

- **FM27 `27.0.0+2486695`:**
  - Player: vtable `+0x4D27228`/`+0x4F75A08`, object `0x3C8`; chỉ số = `object + 0xB8 + offset FM26`; ID ở `+0x294`.
  - Hợp đồng `+0x330`, team thi đấu `+0x130`, CLB cũ `+0x390`, phí lần chuyển gần nhất `+0x244`.
  - Team: vtable `+0x4CE1108`, club `+0x30`, squad vector `+0x38/+0x40/+0x48`.
  - Club: vtable `+0x4CDA1F8`, ID `+0xC`, tài chính `+0x150`. Trong object tài chính: số dư `+0xC`,
    chi chuyển nhượng tháng `+0x29C`, thu bán cầu thủ `+0x32C`, đã chi mùa `+0x4CC`, ngân sách `+0x7C4`, tổng lương `+0x810`.
  - Điều khoản chuyển nhượng: club `+0xB0` → holder → `vector<Clause*>`. Sell-on vtable `+0x4C8F1E8`,
    appearance fee `+0x4DC41E8`, competition bonus `+0x4DC4088` (bố cục trong `TransferClauseReader`). 7 điều khoản của
    Almería (ID 1661) khớp màn FMRTE 27. Mã điều kiện competition bonus (`+0x24`: 52, 51) chưa map được tên.
  - Đã thử trong game:
    - ghi chỉ số;
    - đổi lương + playing time (Acerbi);
    - chuyển Mainoo sang PSG, ngân sách và số dư khớp sau khi Continue;
    - xác nhận chuyển nhượng từ UI.
- **FM26 (qua IGE / FMRTE):**
  - cấu trúc Player/Contract giống FM27;
  - tự ghi chuyển JJ Gabriel sang Real Madrid, game nhận và chơi qua một trận không crash;
  - bản ghi **sell-on** (0x28 byte: player ID, CLB hưởng, giá lúc ký, %);
  - bản ghi **phí có điều kiện** (0x38 byte, vtable `+0x459AF48`);
  - chi tiết ở `re/FMRTE26-findings.md`.
- **Cơ chế FM** (người dùng cung cấp): CA là gốc, chỉ số dần bị kéo về mức hợp với CA. RCA không lưu trong RAM.
- Phí đoàn kết: CLB đào tạo cầu thủ (tuổi 12–23) nhận % phí. Đây là lý do số dư CLB bán hụt so với phí.

## 8. Còn mở (theo thứ tự đề xuất)

1. **Điều khoản chuyển nhượng** (đọc đã xong cho 3 loại):
   - map mã điều kiện competition bonus ↔ tên (52 cho Horta/Bonini, 51 cho Morcillo; cần ảnh FMRTE/IGE);
   - các loại còn lại: sell-on lợi nhuận, mua lại, trả góp (tìm vtable trong cùng vector);
   - đã có: sửa giá trị cả 3 loại, thêm/xoá sell-on (`TransferClauseWriter`). Thêm/xoá appearance fee / competition bonus cần giải mã danh sách toàn game thứ hai (bản ghi appearance được trỏ từ một buffer ở `0x1AD72B24BA0` của save này, không phải std::vector);
   - chưa thử trong game: sell-on cấp bằng VirtualAllocEx khi game trả tiền và giải phóng bản ghi (bán cầu thủ lần sau).
2. Tạo hợp đồng mới khi chuyển (IGE làm vậy), cho mượn, lịch sử chuyển nhượng, phí đoàn kết.
3. Quốc tịch (object Nation), Staff, chấn thương, án treo giò, preset (Heal, Remove bans...), freezer, mass edit, so sánh, xuất dữ liệu.
4. Chưa thử trên game: ghi chiều cao/ngày sinh, Add/Remove điều khoản từ UI.

## 9. Quy tắc bắt buộc

- **Ghi vào game là hành động thật.** Chỉ ghi trên save rác, hỏi người dùng trước khi thử ghi (trừ khi người dùng đã bảo làm),
  khuyên lưu save trước. Mọi ghi qua writer có guard.
- **Test discipline** (`C:/Users/Duong/.claude/skills/test-discipline/SKILL.md`, bản Codex ở `C:/Users/Duong/.Codex/skills/test-discipline/SKILL.md`):
  - không skip test, không làm yếu assertion;
  - giá trị kỳ vọng lấy từ nguồn độc lập (UI game, FMRTE, IGE, tính tay);
  - test mới phải được chứng minh fail bằng một đột biến có chủ đích trên bản sao.
- **Golden:** chỉ giá trị đã thấy trong game, bỏ khoảng do scout. Nếu `verify` lệch thì coi là người dùng đã sửa: cập nhật kèm `note`.
  Chỉ báo người dùng khi lệch kiểu offset sai (nhiều cầu thủ cùng lệch đều).
- **RE:** không tự đoán FM có những gì; hỏi người dùng hoặc tìm trên mạng. Tìm cầu thủ theo **ID** (có trùng tên).
  Ghi kết quả vào `re/FMRTE26-findings.md`, tách Quan sát / Suy luận / Chưa biết.
- Báo cáo phân biệt kết quả đã chạy với dự đoán.
- Không nhắc tới usage hay trạng thái commit trừ khi người dùng hỏi. Không chạy Jev router.

## 10. Lệnh

```bash
dotnet build Fm27Editor.slnx -c Release
```

```bash
dotnet test Fm27Editor.slnx -c Release
```

```bash
src/Fm27Editor.Tools/bin/Release/net10.0/Fm27Editor.Tools.exe move "Kobbie Mainoo" 868 12345678
```

(Chạy từ bản copy trong `%TEMP%` nếu exe không thấy game. Lệnh `move` không có `--write` chỉ in kế hoạch.)
