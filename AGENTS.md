# AGENTS.md — Football Manager 27 Editor

File này cho agent (GPT/Codex/Claude) biết dự án đang ở đâu, cái gì đã được kiểm chứng, và đi tiếp thế nào.
Đọc hết trước khi sửa code. Việc cụ thể đang giao: [docs/handover-app.md](docs/handover-app.md).

## Mục tiêu

Một editor dùng khi đang chơi **Football Manager 27**, chỉ cho chế độ offline/một người chơi. Cách làm giống FMRTE 26
với FM26: đọc/ghi bộ nhớ của `fm.exe` đang chạy, module `game_plugin.dll` (engine C++ của SI, không phải IL2CPP).
Tên app: **Football Manager 27 Editor**.

- FMRTE 26 đã được reverse engineering, chỉ để hiểu cơ chế (xem `re/FMRTE26-findings.md`).
  **Không copy code, asset hay chuỗi của FMRTE.** Mọi offset FM27 đều tự đo trên game thật.
- Ưu tiên của người dùng: **quét và mọi thao tác nhanh nhất có thể**. App chỉ cần không phình tới vài trăm MB.
- Stack: C# / .NET 10, UI bằng Avalonia 11.3 (cùng stack với FMRTE).

## Môi trường

| | |
|---|---|
| Dự án | `C:\Users\Duong\Desktop\FMRTE clone 27` (chưa có git) |
| FM27 | `C:\Program Files (x86)\Steam\steamapps\common\Football Manager 27`, build **`27.0.0+2486695`** |
| FM26 | `F:\SteamLibrary\steamapps\common\Football Manager 26` (build `26.3.2+2329565`) |
| FMRTE 26 | `C:\BraCa Soft\FMRTE 26` |
| Shell | PowerShell 5.1 (và Git Bash). Với file CRLF, `.Replace()` có thể không khớp mà không báo lỗi; hãy dùng tool edit |

## Cấu trúc

| Path | Nội dung | Trạng thái |
|---|---|---|
| `Fm27Editor.slnx` | Solution | Có Core, Tools, Tests. **App chưa được add** |
| `src/Fm27Editor.Core` | Truy cập bộ nhớ, kiểm build, profile offset, đọc/ghi Player, offset finder | Xong phần Player |
| `src/Fm27Editor.Tools` | CLI `players`, `verify`, `set`, `find-offsets` | Xong |
| `src/Fm27Editor.App` | Ứng dụng Avalonia | **Mới có csproj + app.manifest**, chưa có code |
| `offsets/<build>.json` | Mọi RVA/offset phụ thuộc build | Có `27.0.0+2486695` |
| `tests/Fm27Editor.Core.Tests` | 26 unit test trên bộ nhớ giả | Pass |
| `tests/golden/<build>.players.json` | Giá trị chép từ UI game, làm nguồn đối chiếu | 8 cầu thủ |
| `re/` | Ghi chép RE, dump offset FM26, scanner `memscan` | Tham khảo |
| `README.md` | Build, test, chạy, quy trình khi game có patch | |

## Kiến trúc Core (đã có)

- `GameSession.Attach(profiles, writable)`: tìm `fm.exe` và `game_plugin.dll`, chọn profile có chuỗi build đúng tại
  `BuildStringRva`. Sai build thì ném `UnsupportedBuildException`, game chưa chạy thì ném `GameNotRunningException`.
- `PlayerRepository.LoadAll()`: đọc 50.043 cầu thủ, kèm tên, trong khoảng 0,65 giây. `Reload(address)` đọc lại một cầu thủ.
- `PlayerWriter`: `SetAttribute`, `SetPosition`, `SetPersonality`, `SetCurrentAbility`, `SetPotentialAbility`.
  Mỗi lần ghi: kiểm vtable vẫn là Player → kiểm giá trị trong RAM vẫn bằng snapshot → ghi → đọc lại.
  Lỗi trả về `StaleObjectException`, `ArgumentOutOfRangeException` hoặc `MemoryAccessException`.
- `PlayerRecord`: snapshot bất biến của một cầu thủ.
- `OffsetProfile`: nạp/ghi JSON. Thiếu bất kỳ field nào thì báo lỗi ngay lúc nạp.
- `OffsetFinder`: dựng lại profile cho build mới, từ RAM và file golden, mất khoảng 25 giây.
- `IProcessMemory`: interface để test bằng `FakeMemory`. Bản thật là `WindowsProcessMemory`.

## Đã kiểm chứng trên game thật (build `27.0.0+2486695`)

- Gốc danh sách cầu thủ: `game_plugin+0x57037C0` (`[+0x10]` là đầu mảng, `[+0x18]` là cuối mảng). Vtable Player
  `+0x4D27228`, vtable phụ `+0x4F75A08`. Object dài `0x3C8`.
- Chỉ số = `object + 0xB8 + offset FM26`, lưu dưới dạng hiển thị × 5. CA/PA ở `+0x264/+0x266`. Tên ở
  `+0x2C8/+0x2D8/+0x2E0/+0x2E8`. Chiều cao `+0x22E`, ngày sinh `+0x310`. Bảng đầy đủ nằm trong `offsets/` và `re/FMRTE26-findings.md` mục 6.
- `verify` khớp 8/8 cầu thủ golden. Hai lần ghi thử (Šeško, Mainoo) hiện đúng trong game và còn nguyên sau khi lưu rồi tải lại save.
- **CA là giá trị gốc của FM.** Theo thời gian, game kéo các chỉ số về mức hợp với CA (đây là cơ chế của FM, người dùng
  cung cấp). Sửa chỉ số mà không sửa CA thì có thể bị kéo về dần. Ghi CA thì CA không bị đổi ngay.
  **RCA không có trong RAM:** FMRTE tự tính từ chỉ số. Nếu app cần RCA thì phải có công thức riêng, không chép bảng trọng số của FMRTE.
  (Kết luận cũ "game tự tính lại CA từ chỉ số" đã bị rút lại.)
- **Hợp đồng/CLB** (chỉ đọc): `PlayerRecord.Contract` (`Wage` theo tuần, `Expires`, `Joined`) và `PlayerRecord.Club`
  (`Name`, `ShortName`, `DisplayName` = tên hiện ở đầu hồ sơ). Hai section `contract`/`club` trong JSON là tuỳ chọn;
  khi thiếu thì `Contract` = null. Khớp 8/8 golden. Cầu thủ tự do thì `Contract` = null.
- **Ngày trong game:** `GameSession.ReadGameDate()` trả về `DateOnly?`, đọc mới mỗi lần gọi, và chỉ trả về khi hai chuỗi
  con trỏ độc lập khớp nhau. Dùng `PlayerRecord.AgeOn(date)` để ra tuổi như trong game. Hàm trả về null khi chưa tải save.

## Chưa biết / còn mở

- Personality mới kiểm ở mức hợp lý, chưa so với nguồn có số chính xác.
- Byte vị trí `+0x151` chưa biết. Có 14 Player không nằm trong mảng danh sách.
- Ngày trong game mới kiểm qua 1 lần sang ngày, chưa kiểm sau khi khởi động lại hẳn game.
- **Trạng thái save (31/10/2026):** người dùng đã thoát game mà **không lưu** phần lớn các thay đổi thử nghiệm.
  Vì vậy golden của Mainoo (Crossing 12) và Šeško (Crossing 7, Finishing 16, Natural Fitness 20, CA 165) có thể không còn
  khớp với save. Lần mở game tới: chạy `verify`, rồi hỏi người dùng đang dùng save nào trước khi sửa golden.
  Lần mở đó cũng là dịp kiểm xem con trỏ gốc và chuỗi ngày game có còn đúng sau khi khởi động lại hẳn game không.
- Nếu `verify` báo lệch golden, **không được sửa golden cho xanh**. Phải hỏi người dùng xem có phải họ đã ghi
  (ví dụ khi test app) hay không. Ví dụ: lần lệch của Šeško ngày 5/11 đã được người dùng xác nhận rồi mới cập nhật golden.
- Không thử ghi lên game thật khi chưa hỏi người dùng (bao gồm cả khi chạy thử app).
- Gốc danh sách chưa kiểm sau khi khởi động lại hẳn game, mới kiểm sau khi tải lại save.
- Chưa đo trên FM27 tốc độ game kéo chỉ số về mức hợp với CA sau khi sửa. Để làm freezer và RCA thì cần đo việc này.
- Lúc restore App có cảnh báo NU1903 (`Tmds.DBus.Protocol`, phụ thuộc Linux kéo theo Avalonia). Chưa xử lý.

## Lộ trình

1. **Giao diện** (đang giao, xem `docs/handover-app.md`): tìm và sửa cầu thủ.
2. **Mở rộng dữ liệu** (làm theo quy trình RE ở dưới). Đã có lương, hạn hợp đồng và CLB của cầu thủ (chỉ đọc).
   Đã có ngày trong game và tuổi. Còn: ghi lương/hạn hợp đồng, hợp đồng cho mượn, điều khoản, Club (tài chính, danh tiếng,
   sân), Staff, Nation, Competition, Injury.
3. **Tính năng kiểu FMRTE:** tìm kiếm nâng cao và lọc theo chỉ số, mass edit, freezer (giữ cố định giá trị),
   presets (Heal, Inspire, RemoveBans...), so sánh cầu thủ, xuất dữ liệu. Cuối cùng là các thao tác nhiều bước
   (chuyển cầu thủ, clone), là loại rủi ro nhất.
4. **Phát hành:** publish NativeAOT/self-contained, kiểm kích thước và tốc độ khởi động.

## Quy trình thêm một loại dữ liệu mới (Phase 2)

1. Lấy bảng offset FM26 tương ứng trong `re/fm26-dump/offsets.txt`.
2. Trên FM27 đang chạy, dùng `re/tools/memscan` để tìm vtable của loại object đó và đo delta so với FM26.
3. Đối chiếu ít nhất 3 giá trị nhìn thấy được trên UI game. Ghi chúng vào file golden.
4. Thêm offset vào `offsets/<build>.json` và `OffsetProfile` (thiếu field thì phải báo lỗi lúc nạp), thêm reader/writer trong Core.
5. Thêm unit test có giá trị kỳ vọng độc lập, và mở rộng `verify` cùng `OffsetFinder` cho loại dữ liệu mới.
6. Ghi kết quả vào `re/FMRTE26-findings.md`, phân rõ Quan sát / Suy luận / Chưa biết.

## Quy tắc bắt buộc

- **Test discipline** (`C:/Users/Duong/.Codex/skills/test-discipline/SKILL.md`, bản Claude nằm ở `C:/Users/Duong/.claude/skills/test-discipline/SKILL.md`):
  - không skip test, không làm yếu assertion, không sinh lại golden mà không giải thích từng chỗ khác;
  - giá trị kỳ vọng lấy từ nguồn độc lập (UI game, ví dụ tính tay), không suy ra từ code;
  - test mới phải chứng minh được là có thể fail bằng một đột biến có chủ đích, làm trên bản sao, không sửa code thật.
- **Ghi vào game đang chạy là hành động thật.** Hỏi người dùng trước mỗi lần ghi khi chạy thử, và khuyên lưu save trước.
  Mọi thao tác ghi của app phải đi qua `PlayerWriter` (hoặc writer tương đương có cùng các bước kiểm tra).
- **Golden:** chỉ chứa giá trị đã thấy trong game. Khoảng do scout (vd `9-12`) thì bỏ. Tên/họ chỉ ghi khi đã đọc
  được trong RAM. Nếu editor sửa một giá trị thì cập nhật golden kèm `note`.
- **Phân biệt** kết quả đã chạy với dự đoán khi báo cáo. Không gọi một lỗi là "có sẵn từ trước" nếu chưa có bằng chứng.
- Không lưu địa chỉ object tuyệt đối giữa các phiên: mỗi lần tải save, địa chỉ đều đổi.
- Không chạy Jev router.

## Lệnh

```bash
dotnet build Fm27Editor.slnx -c Release
```

```bash
dotnet test Fm27Editor.slnx -c Release
```

```bash
src/Fm27Editor.Tools/bin/Release/net10.0/Fm27Editor.Tools.exe verify tests/golden/27.0.0+2486695.players.json
```
