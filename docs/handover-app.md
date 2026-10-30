# Handover: dựng app Football Manager 27 Editor (Avalonia)

Đọc [AGENTS.md](../AGENTS.md) trước. File này mô tả việc cần làm: dựng app desktop trên `Fm27Editor.Core` hiện có,
sao cho về sau thêm được Club, Contract, Staff, freezer, presets... mà **không phải sửa lại khung app**.

## 1. Phạm vi

**Làm:**
- Bộ khung app (`src/Fm27Editor.App`) có điểm mở rộng ở mục 4.
- Module đầu tiên: **Players**, gồm tìm kiếm, xem và sửa chỉ số / CA / PA / vị trí / personality.

**Không làm:**
- Không đổi offset, không đổi ngữ nghĩa của Core, không dò thêm dữ liệu game. Phần dò dữ liệu sẽ được làm và bổ sung sau.
- Nếu UI cần thêm gì từ Core, ưu tiên thêm hàm **chỉ đọc**, kèm unit test có giá trị kỳ vọng độc lập.

Hiện trạng `src/Fm27Editor.App`:
- Đã có `Fm27Editor.App.csproj`: `WinExe`, `net10.0`, AssemblyName `Fm27Editor`, Avalonia 11.3.10, DataGrid,
  Fonts.Inter, CommunityToolkit.Mvvm 8.4.0, compiled bindings bật sẵn, `offsets/*.json` được copy vào output.
- Đã có `app.manifest`.
- **Chưa có code nào, và chưa được add vào `Fm27Editor.slnx`.**

## 2. Triết lý thiết kế (yêu cầu của người dùng, bắt buộc)

- Tham khảo **độ trực quan** của FMRTE (ảnh FMRTE ở mục 7), nhưng được phép tự thiết kế.
- **Không ai-slop**: không gradient trang trí, không thẻ bo tròn lồng nhau, không icon minh hoạ vô nghĩa,
  không emoji, không hero banner, không "dashboard" thống kê cho đẹp.
- **Cắt tagline, helper text và nhãn phụ.** Nếu màn hình cần chữ mô tả mới hiểu được thì phải sửa bố cục, không thêm chữ.
  Không có placeholder dài. Không có câu như "Select a player to start editing". Trạng thái rỗng thì để trống hoặc bỏ panel đó đi.
- **Được** dày nhãn như FMRTE: mỗi field có nhãn tên field (Crossing, Pace, CA...). Đó là dữ liệu, không phải chữ giải thích.
- Bố cục tự giải thích:
  - các nhóm chỉ số xếp thành cột theo đúng nhóm của game;
  - vị trí vẽ thành sơ đồ sân;
  - giá trị đổi màu theo dải như trong FM.
- **Tốc độ là tính năng:**
  - mở app → attach → nạp danh sách → gõ tìm phải phản hồi tức thì;
  - không spinner che màn hình;
  - nạp dữ liệu ở background, UI vẫn dùng được trong lúc nạp.
- Một màu nhấn duy nhất. Theme tối mặc định, Fluent. Mật độ thông tin cao: hàng thấp, font 13–14px, ưu tiên số canh phải.

## 3. Màn hình Players (đề xuất, được điều chỉnh)

```
┌───────────────────────────────────────────────────────────────────────────────────┐
│ [Players] [Clubs]…   🔍 [__________]              27.0.0+2486695 · 50,043   ⟳     │  thanh trên
├──────────────────────────────┬────────────────────────────────────────────────────┤
│ Name           Born  CA  PA  │ Kobbie Mainoo          19/04/2005 · 184 cm         │
│ Lamine Yamal   2007 181 195  │ CA [154]  PA [177]   Home 7382 Current 7551 World 6501 │
│ Kobbie Mainoo  2005 154 177  │ Technical   Mental      Physical    Goalkeeping    │
│ …  (DataGrid ảo hoá)         │ Crossing 12 Aggression… Acceleration… Aerial Reach…│
│                              │ …                                                  │
│                              │ Hidden      Personality  [sơ đồ sân vị trí]        │
├──────────────────────────────┴────────────────────────────────────────────────────┤
│ Crossing 9 → 12                                                                   │  thanh trạng thái
└───────────────────────────────────────────────────────────────────────────────────┘
```

- **Tìm kiếm:**
  - không phân biệt hoa/thường và dấu, dùng `CompareInfo.IndexOf` với `IgnoreCase | IgnoreNonSpace`, để "sesko" ra "Šeško";
  - khớp `DisplayName` và `FullName`;
  - lọc trong RAM trên mỗi phím gõ; 50k dòng thì không cần debounce lâu.
- **Lưới kết quả:**
  - mặc định sắp theo CA giảm dần, sắp lại được theo từng cột;
  - cột Born (năm sinh). **Chưa hiện tuổi**, vì chưa có ngày trong game (xem AGENTS.md).
- **Ô chỉ số:**
  - nhãn + `NumericUpDown` (1–20, bước 1, không có nút spin rườm rà, cuộn chuột và phím ↑↓ vẫn dùng được);
  - màu chữ theo dải: 1–5 / 6–10 / 11–15 / 16–20.
- **Ghi ngay khi đổi giá trị**, như FMRTE: commit khi Enter, khi rời ô, hoặc mỗi bước tăng giảm.
  1. Gọi writer.
  2. `Reload` cầu thủ, vì CA có thể bị game tính lại.
  3. Cập nhật mọi ô.
  4. Ghi một dòng trạng thái: `Crossing 9 → 12`.
- **Khi ghi lỗi:**
  - `StaleObjectException` → tải lại cầu thủ, thanh trạng thái báo giá trị đã đổi trong game;
  - không tìm thấy cầu thủ (đã tải save khác) → nạp lại danh sách.
- **Kết nối:**
  - khởi động thì attach. Game chưa chạy hoặc sai build thì thanh trên hiện đúng một dòng trạng thái, kèm nút thử lại;
  - không dùng dialog chặn màn hình.
- **Mở rộng sau:** vùng tên/thông tin ở đầu panel phải đủ chỗ để thêm Club, quốc tịch, hợp đồng khi Core có dữ liệu.

## 4. Kiến trúc mở rộng (quan trọng nhất)

Mục tiêu: về sau thêm một loại dữ liệu hay một tính năng thì chỉ **thêm file và đăng ký**, không phải sửa MainWindow hay các module khác.

### 4.1 Lớp nối Core: `IEditorBackend`
Tách UI khỏi `GameSession` để view model test được bằng `FakeMemory`:
```csharp
public interface IEditorBackend
{
    ConnectionState State { get; }              // Disconnected / UnsupportedBuild / Connected(build)
    event EventHandler? StateChanged;
    Task ConnectAsync(CancellationToken ct);
    PlayerRepository Players { get; }            // về sau: Clubs, Staff…
    IEditService Edits { get; }
}
```

### 4.2 Một đường ghi duy nhất: `IEditService`
**Mọi** thao tác ghi đều đi qua đây: sửa tay, mass edit, preset, freezer.
- Tuần tự hoá các lần ghi, vì freezer chạy nền và có thể ghi cùng lúc với sửa tay.
- Mỗi lần ghi tạo một bản ghi `EditRecord`: entity, field, giá trị cũ, giá trị mới, thời điểm, kết quả.
  Dùng cho thanh trạng thái, một panel lịch sử về sau, và **Undo** (ghi lại giá trị cũ, vẫn qua guard).
- Bên trong gọi `PlayerWriter` (và các writer tương lai). Không bao giờ gọi `TryWrite` trực tiếp.

### 4.3 Field mô tả bằng dữ liệu: `FieldDescriptor`
Panel sửa **render từ danh sách descriptor**, không hard-code từng ô trong XAML. Thêm một field = thêm một descriptor.
```csharp
public sealed record FieldDescriptor<TEntity>(
    string Id, string Label, string Group, FieldKind Kind,   // Kind: Rating20, Int (min,max), Text, Date, ReadOnly
    int Min, int Max,
    Func<TEntity, int> Read,
    Action<IEditService, TEntity, int>? Write);               // null = chỉ đọc
```
- Players: sinh descriptor từ các enum `PlayerAttribute` (group lấy qua `PlayerAttributeInfo.GroupOf`),
  `PlayerPosition`, `PersonalityAttribute`, cộng thêm CA/PA/danh tiếng.
- Nhãn hiển thị ("Free Kick Taking", "Off The Ball", "Jumping Reach", "Punching (Tendency)"...) để trong một bảng
  map riêng của App, sau này dùng làm chỗ bản địa hoá.
- Cột lưới kết quả cũng là descriptor (`ColumnDescriptor`). Về sau người dùng chọn được cột, giống FMRTE.

### 4.4 Module theo loại entity: `IEditorModule`
```csharp
public interface IEditorModule
{
    string Title { get; }                                     // "Players", sau này "Clubs", "Staff"…
    IReadOnlyList<ColumnDescriptor> Columns { get; }
    Task<IReadOnlyList<object>> LoadAsync(IEditorBackend b, CancellationToken ct);
    bool Matches(object row, string query);
    EditorViewModel CreateEditor(object row);                 // panel phải
}
```
- Thanh trên hiện tab cho **các module đã đăng ký và đang dùng được**. **Không hiện tab "coming soon" hay tab bị xám.**
- Đăng ký trong một chỗ duy nhất (`Modules.cs` hoặc DI). Thêm Clubs về sau = thêm `ClubsModule` và một dòng đăng ký.

### 4.5 Hành động trên entity: `IEntityAction`
Cho presets kiểu FMRTE (Heal, Inspire, RemoveBans, Max attributes...) và mass edit:
```csharp
public interface IEntityAction
{
    string Title { get; }
    Type EntityType { get; }
    bool CanRun(object entity);
    Task RunAsync(IEditService edits, IReadOnlyList<object> entities, CancellationToken ct);
}
```
- Menu "Actions" trên panel sửa và trên các dòng đang chọn của lưới chỉ hiện khi có ít nhất một action dùng được.
- Giai đoạn này **chưa cần** action nào. Chỉ cần interface và chỗ cắm, không làm action giả.

### 4.6 Dịch vụ nền: `IBackgroundService`
Chỗ cắm cho freezer (định kỳ ghi lại các giá trị đã khoá, qua `IEditService`) và cho việc phát hiện save bị tải lại
(vtable của object hiện tại không còn hợp lệ → nạp lại danh sách). Giai đoạn này chỉ cần phần **phát hiện tải lại save**.

### 4.7 Thư mục gợi ý
```
src/Fm27Editor.App/
  Program.cs, App.axaml(.cs)
  Shell/        MainWindow, thanh trên, thanh trạng thái, chuyển module
  Backend/      IEditorBackend, EditorBackend (GameSession), IEditService, EditService, EditRecord
  Fields/       FieldDescriptor, ColumnDescriptor, FieldKind, DisplayNames, RatingBrushes
  Modules/      IEditorModule, IEntityAction, IBackgroundService, Modules.cs (đăng ký)
  Modules/Players/  PlayersModule, PlayerEditorViewModel, PlayerEditorView.axaml, PlayerFields.cs, PitchPositions.axaml
  Controls/     RatingBox (ô 1–20 có màu), FieldGroup (cột nhóm sinh từ descriptor)
```

## 5. Hiệu năng (đo, đừng đoán)

- **Ngân sách:**
  - mở app → danh sách đầy đủ hiện ra < 1,5 s sau khi attach (`LoadAll` hiện mất khoảng 0,65 s);
  - mỗi phím gõ khi tìm < 50 ms với 50k dòng;
  - sửa một ô → thấy giá trị mới < 100 ms.
- **Cách làm:**
  - `LoadAll` chạy trên thread pool;
  - lưới dùng DataGrid ảo hoá;
  - lọc trên mảng đã nạp, không tạo lại view model cho cả 50k dòng mỗi lần gõ;
  - chỉ tạo `PlayerEditorViewModel` cho cầu thủ đang chọn.
- Ghi thời gian đo thật trong báo cáo, phân biệt số đã đo với số ước lượng.
- Kiểm thử publish: `dotnet publish -c Release -r win-x64` (self-contained; NativeAOT nếu Avalonia + trimming chạy ổn).
  Ghi lại kích thước và thời gian khởi động. Không được vượt vài trăm MB.

## 6. Kiểm thử và nghiệm thu

- **Test project mới** `tests/Fm27Editor.App.Tests`:
  - test view model và `EditService` qua `IEditorBackend` giả, dùng lại cách dựng `FakeMemory`/`TestWorld` của Core tests
    (có thể chuyển 2 file này sang một project test dùng chung);
  - giá trị kỳ vọng lấy độc lập (ví dụ tính tay), theo đúng test-discipline.
- **Rủi ro cần có test:**
  - tìm kiếm bỏ dấu ("sesko" → "Benjamin Šeško");
  - sửa một ô chỉ ghi đúng một field, với đúng giá trị × 5;
  - sau khi ghi, CA trên UI lấy từ lần đọc lại chứ không phải giá trị cũ;
  - `StaleObjectException` → không ghi, panel nạp lại giá trị thật;
  - `EditRecord` lưu đúng giá trị cũ/mới; Undo khôi phục đúng giá trị cũ;
  - các module/action chưa đăng ký thì không hiện.
- Mỗi test mới phải chứng minh được là fail với một đột biến có chủ đích, làm trên bản sao.
- **Chạy thật với FM27:**
  - mở app, tìm "Yamal", đối chiếu với `tests/golden` (chỉ đọc);
  - chỉ thử ghi khi **người dùng đồng ý** và đã lưu save.
- **Nghiệm thu:**
  - `dotnet build` và `dotnet test` sạch;
  - `Fm27Editor.Tools verify` vẫn pass;
  - chụp màn hình app chạy với game thật;
  - báo cáo số đo hiệu năng;
  - liệt kê những gì chưa làm.

## 7. Tham khảo FMRTE (chỉ để hiểu độ trực quan, không copy)

Màn hình cầu thủ của FMRTE 21: panel trái là Personal Details / Ability (CA/PA, Recommended CA) / Reputation /
Personality / Hidden; giữa là các cột Technical, Mental, Physical với mỗi dòng là nhãn + ô số; phải là Options và Presets
(Heal, Inspire, Destroy, Remove bans...). Thanh dưới hiện savegame, số cầu thủ, số đội.
Dùng thứ tự nhóm và mật độ đó làm điểm xuất phát. Không lặp lại giao diện kiểu Windows Forms cũ của FMRTE.

## 8. Báo lại khi xong

- Đã làm gì, file nào.
- Số đo hiệu năng (đo hay ước lượng).
- Test đã chạy và kết quả đột biến.
- Đã thử trên game thật chưa, có ghi chưa, có được người dùng đồng ý không.
- Điểm mở rộng nào đã có nhưng chưa dùng (Actions, background services ngoài phát hiện tải lại save).
- Câu hỏi còn mở cho người dùng.
