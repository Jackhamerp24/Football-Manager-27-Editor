# Bàn giao (4/11/2026)

File bàn giao duy nhất. Đọc file này, rồi `AGENTS.md`. Chỉ mở file khác khi việc đang làm cần tới.
Bằng chứng offset từng mục: `re/FM27-findings.md` (chỉ trên máy). **Không đọc cả file**: xem mục lục đầu file, grep tên mục.
Giữ file này ngắn: mỗi việc xong chỉ sửa dòng trạng thái tương ứng, không thêm nhật ký. Chi tiết đưa vào findings.

## 1. Trạng thái

- Build 0 lỗi; Core 365 + App 146 test pass (5/11).
- Profile: Steam `offsets/27.0.0+2486695.json` (cũ), `offsets/27.0.0+2493426.json` (Steam hiện tại),
  `offsets/27.0.0+2493426-gamepass.json` (Game Pass, gói `0.9.2409.0`). Game Pass và Steam trùng chuỗi build; phân biệt bằng
  image size và PE timestamp.
- **Game Pass:** offset trong object dùng chung với Steam; vtable map theo tên lớp RTTI. Đã ghi thử bằng CLI: chỉ số, lương,
  Move to club (Mbeumo → PSG), CA/PA và lương staff. Khác Steam: vector staff `module+0x5683E08` trỏ thẳng staff part
  (`directStaffVector`), ngày game đọc trực tiếp `directGameDateRva`.
- **Move:** đã bổ sung ghi đội chính `contract+0x18` và thử Ronaldo Al Nassr → PSG qua CLI, đọc lại ba con trỏ cùng đội PSG; người dùng xác nhận Ronaldo ở PSG trong game. Mbeumo được Move bằng bản cũ vẫn có `+0x18` trỏ Man Utd; Move mới từ chối hợp đồng hai đội khác nhau, chưa sửa Mbeumo. Các side effect khác và UI editor chưa kiểm chứng; xem findings trước khi tiếp tục Release.
- **Game update:** `pwsh -NoProfile -File re/tools/gamepass-rva-map.ps1 -OutputPath <log> -ProfileOutput <profile.json>` dựng
  profile từ RTTI + RAM (~7 s). Root direct trong script đo cho build hiện tại; build mới cần kiểm lại root.

| Mảng | Đã có | Còn thiếu |
|---|---|---|
| Player | Hầu hết tab (`docs/player-tabs.md`) | Promises, Work Permit, Scout Information, đổi tên, preset |
| Staff | Information, Contract, Career Plans, Relationships, Languages, Knowledge, Formations, Tendencies | Training Rating (chưa xác nhận có trong FMRTE 27) |
| Club | Nhiều field, Squads, Transfer Center (đọc, sửa phí), Relationships, Staff, Debt, Affiliations, Vision (sửa Importance) | Kits, Tactics, Possible Signings, Sponsors (`docs/club-tabs.md`) |
| Nation | Phần lớn field (`docs/nation-tabs.md`) | Agreements, EU, Non-Foreign, Continents, EuroCoefficients, Licensed Kits, màu |
| Stadium | 25 field, 7 cờ, 8 khán đài | Training Ground, Owner/Location, sửa Seat Colour |
| Cities | Inhabitants, Attraction, toạ độ, Altitude | Region, Language, Weather, Nation |
| Competitions | Reputation, xem màu và Winter Ball | Rules, Prize Money, VAR, Substitution, sửa màu/ngày |
| Awards | Reputation, Winner reputation | Tên |
| Công cụ | Lịch sử + Undo; Export CSV/JSON; Freezer player/CLB; lọc chỉ số Min–Max, staff theo tên/CLB/chức vụ; Mass Edit player/staff nhiều field và lương, field số nguyên CLB; preset Heal/Fitness/Morale/Lift bans/Remove Unhappiness/Max attributes/Prevent retirement cả đội có preview/Undo; Captain/vice captain cho cầu thủ (CLI Game Pass) | Thử UI; mở rộng Mass Edit field khác/staff/club; các preset còn lại, Shortlists, Favorites, Players Progress |

FMRTE 27 không có trang Teams, Officials, Media Sources. Không làm Swap Player / Swap Staff (Move to club thay thế). FMRTE 27 có Move riêng, xem §5 mục 6.
Staff Promises: bỏ qua (người dùng xác nhận). Kiểm kê Staff/Stadium/City/Competition/Award chưa đầy đủ, nên cột "còn thiếu" có thể sót.

## 2. Quy tắc (người dùng đã chốt)

1. **Tốc độ trước.** Làm xong việc rồi test một lần theo mức rủi ro. Port, migrate, sửa cơ học: build + chạy test sẵn có +
   một lần thử thật. Chỉ thêm test (kèm đột biến) cho logic ghi mới. Không viết test cho có.
2. Save đang mở là save rác: ghi thử không cần hỏi, **không hoàn nguyên**. Đọc lại RAM một lần để xác nhận.
3. Mọi lần ghi qua writer có guard (vtable, snapshot, ghi, đọc lại).
4. Offset mới xác nhận bằng FMRTE 27 hoặc UI game trên 2 đối tượng khác giá trị. Không đoán FM có gì. Tìm theo ID.
5. Không skip test, không làm yếu assertion; đổi assertion cũ thì ghi lý do.
6. UI không AI-slop: không tagline, helper text, nhãn phụ, emoji, gradient. Theme tối, một màu nhấn tím, mật độ cao.
7. Ngày thi đấu chỉ Instant Result; mờ thì dừng. Không bấm Load Game. Không cài update thay người dùng.
8. Kẹt quá 3 vòng một chỗ thì ghi 3 dòng vào findings rồi chuyển việc. Làm liên tục, không chờ.
9. Không nhắc usage, không báo commit, commit chỉ khi được bảo, không chạy Jev. Bỏ qua bóng đá nữ.
10. Chỉ dùng computer use khi người dùng cho phép trong phiên đó. FMRTE: tối đa 2 tab cùng đối tượng, đóng tab xong việc.
    Không mở nhiều bản editor cùng lúc.
11. Báo cáo tiếng Việt, ngắn, tách "đã chạy" với "suy luận".

## 3. Cách chạy

```bash
dotnet build Fm27Editor.slnx -c Release
```

Testhost và exe trong workspace không thấy `fm.exe`. Chạy test từ `%TEMP%`:

```bash
cd "$TEMP"; rm -rf fm27tCore fm27tApp; cp -r "/c/Users/Duong/Desktop/FMRTE clone 27/tests/Fm27Editor.Core.Tests/bin/Release/net10.0" fm27tCore; cp -r "/c/Users/Duong/Desktop/FMRTE clone 27/tests/Fm27Editor.App.Tests/bin/Release/net10.0" fm27tApp; dotnet vstest fm27tCore/Fm27Editor.Core.Tests.dll fm27tApp/Fm27Editor.App.Tests.dll | tail -5
```

- CLI và app: copy `bin/Release/net10.0` ra `%TEMP%`, chạy từ đó. Lấy PID bằng `Get-Process fm`, đặt `FM_PID`.
- Đột biến (chỉ khi cần): offset sửa JSON trong bản copy `%TEMP%\fm27tCore\offsets\`; code copy repo sang `%TEMP%\mutsrc`.
- Cắt output: chỉ lấy dòng tổng kết của test, dump RAM chỉ vùng cần xem.
- FMRTE 27: thư mục cài trên `E:\` (bản build 5 sau update). Mở trong cmd không admin: `set __COMPAT_LAYER=RunAsInvoker`.
  Tên hiển thị trong `Assets\Languages\english.json` (chỉ tên, không có mã).
- Computer use: `re/tools/focus.ps1 <FMRTE|Fm27Editor|fm>` đưa cửa sổ lên. FMRTE giữ dữ liệu cũ trong tab đang mở: mở lại qua ô
  tìm. Hộp lỗi "Changes cannot be saved" là modal chặn mọi thao tác, bấm Ok trước.

### Oracle

1. Diff một thao tác: `hex <addr> <len>`, làm một thao tác trong FMRTE, Ctrl+S, `hex` lại.
2. Dropdown dài: chạy nền `bash re/tools/watch.sh <pid> <addr> <len> <log>`, lặp Down + Ctrl+S trong FMRTE.
3. Thêm/xoá dòng: đọc holder/vector, bấm `+`/`−`, Ctrl+S, đọc lại. FMRTE cấp buffer mới và chép dòng đang chọn.
4. Khảo sát toàn game bằng lệnh CLI chỉ đọc (mẫu `tendencysurvey`).

CLI: danh sách lệnh ở đầu `src/Fm27Editor.Tools/Program.cs`. `re/tools/memscan` cho việc khó hơn.

## 4. Bố cục bộ nhớ hay dùng

- Player `0x3C8`, ID `+0x294`; person ở `player+0x288`. Person (chung cầu thủ/staff): personality +0x70, relations +0x78,
  hợp đồng/chức vụ +0xA8, career plans +0xC8, languages +0xF0. Code: `PersonOwner`.
- Staff part: `Person − 0x100` (thường), `Person − 0x440` (HLV người chơi). Tendencies +0xA8, knowledge +0xB0, formations +0xDF.
- Hợp đồng: Type byte +0xC3. Ngày: 9 bit thấp là ngày trong năm, bit trên là cờ, phải giữ.
- Relation 16 byte: target +0, reason +8, target kind +0xA, type +0xB, value +0xC, cờ +0xD.

## 5. Việc còn lại (theo thứ tự)

1. **Ưu tiên phần có thể học FMLibreEditor** (dự án theo GPL-3.0 nên được chép code FMLibre/fm-editor, giữ ghi công nguồn trong comment; hỗ trợ Game Pass và Steam): Export CSV/JSON đã có, UI chưa thử; Freezer
   (đã nối UI, lưu và tick; player hỗ trợ giữ nguyên/cho tăng/cho giảm, CLI đã thử reconnect, chưa thử UI; balance giữ nguyên/cho tăng); Mass Edit (nhiều field player/staff có UI/bộ lọc/Undo, preview nền; Wage báo số player/staff không có hợp đồng đọc được, lương staff đã thử ghi hai người bằng CLI Game Pass; chưa thử UI; còn mở rộng); Presets cả đội (đã nối Heal/Fitness/Morale/Lift bans/Remove Unhappiness/Max attributes/Prevent retirement có preview/Undo; Max báo unavailable cho raw ngoài 1–100, giữ nguyên Dirtiness/Injury Proneness; Prevent retirement chỉ Game Pass, đã thử CLI batch hai player, Steam chưa đo manager; các UI chưa thử; Captain `team+0x88/+0x90` có writer/CLI/UI/Undo, đã ghi thử hai cầu thủ và Undo một lần trên Game Pass, UI/Steam chưa thử); Release Player và chuyển staff xem dòng dưới.
   Thứ tự có thể điều chỉnh theo phần dùng chung và kết quả thực tế; làm các phần đã có trong app bằng backend hiện có.
   FMLibre có Captain/chuyển staff và Export danh sách; chưa kết luận chúng tương đương chính xác tính năng FMRTE. Move mới đã sửa đội chính nhưng chưa xử lý đủ mọi squad/captain theo Libre. Release có lõi, Undo, CLI `release <id> [--write]` và nút UI; đã thử Game Pass một cầu thủ qua CLI, UI và trạng thái sau Continue chưa thử. Staff Move có lõi, Undo, CLI `staffmove <id> <club-id> [--write]` và tab UI; đã thử coach và physio trên Game Pass, ngày hợp đồng cập nhật theo ngày game; UI và Steam chưa thử; xem findings.
2. **Health check khi gặp build lạ** (như FMLibre): CLI `healthplayers` chỉ đọc, lấy tối đa 2.000 player và kiểm vị trí,
   personality, khoảng + tương quan chỉ số theo vị trí. `healthstructure` kiểm contract owner/vtable và team/club/squad;
   `healthstaff` kiểm chỉ số theo vai trò và liên kết job, hiện báo check vì 131 job có owner đúng nhưng vtable khác contract;
   gate staff **tạm dừng** sau ba vòng kiểm: cần bằng chứng loại job/vtable độc lập, xem findings;
   `healthclubs` kiểm đội chính trỏ về CLB và reputation của CLB lấy được từ player; đã đo Game Pass và profile offset sai
   trên bản sao. Mới là chẩn đoán profile đã biết, chưa attach build lạ hay tự khóa ghi; không coi staff check là lỗi offset
   đã xác nhận.
3. **Club:** Kits, Tactics, Possible Signings, Sponsors, thêm/xoá Vision/Affiliations/Regional, Relationships `+`/`−`.
4. **Nation:** Continents, EuroCoefficients (đã có vector ứng viên `holder+0x108`, `+0x190`), Agreements/EU/Non-Foreign, Licensed Kits.
5. **Stadium, City, Competition, Award:** phần còn thiếu ở §1.
6. **Player nhỏ:** Clone Center chép tên, đổi tên, Career Plans tạo container mới, loại điều khoản còn thiếu. **Move — làm sau:** nghiên cứu tạo hợp đồng mới và ghi lịch sử chuyển nhượng kèm phí; hiện Move giữ hợp đồng cũ, không tạo dòng lịch sử (người dùng đã kiểm tra Mbeumo → PSG trong game).
   **Move của FMRTE 27 (người dùng ghi nhận 5/11, đáp án để làm theo).** Hộp thoại: Move, From, To, Type (Transfer | Loan), ô
   **Move now**.
   - Tick Move now: chuyển ngay, **không có phí** ("Moves the player straight away, without a fee").
   - Bỏ tick: nhập Transfer Fee, Transfer Date, ô "Set transfer terms"; FMRTE ghi chú "Keep FM paused while saving. FM completes
     the transfer on the selected date" → game tự hoàn tất vào ngày đó.
   Kết quả thấy trong game: thêm dòng Career mới (ví dụ 2026/27 PSG £100M), giữ lịch sử cũ; cầu thủ được báo "đã được sắp xếp
   chuyển nhượng", có tin News; hợp đồng mới có vẻ được game tính theo CLB mới. Ta hơn ở chỗ chuyển ngay vẫn nhập được phí.
   Hướng làm: bản theo ngày gần như chắc là tạo một arranged transfer để game tự xử lý. Oracle: snapshot RAM trước/sau cho từng
   lựa chọn (danh sách offer `[module+offers]+0x8`, career history, contract, news), cả Loan.
7. **Đã dừng:** Player Promises, Work Permit, Scout Information, Training Rating, Make WonderKid, Injury Add, RCA.

## 6. Bẫy

- Offset đúng trên một đối tượng có thể sai: luôn 2 mẫu khác giá trị. Happiness là byte có dấu.
- FMRTE 27 tự có lỗi: ghi đè dòng relation đầu; ghi 0 thay vì 2 cho +0xC của career plan.
- Giới hạn FMRTE chỉ dựa vào giá trị nó từ chối. Mã suy từ thứ tự dropdown chưa phải đã xác nhận.
- Màu lưu B, G, R, A. FMRTE ghi lại float khi Save (lệch 1 ulp là bình thường).
- File nguồn CRLF: dùng tool edit; `sed` có thể không khớp mà không báo.
- Guard "không phải X" trong test: đặt giá trị object giả bằng snapshot để chỉ guard vtable còn tác dụng.

## 7. File tham chiếu (chỉ mở khi cần)

| File | Dùng khi |
|---|---|
| `re/FM27-findings.md` | Bằng chứng/offset một tính năng (grep tên mục) |
| `re/archive/early-findings.md` | FMRTE 26/FM26/IGE, gốc của Move to club và hợp đồng |
| `docs/player-tabs.md`, `docs/nation-tabs.md`, `docs/club-tabs.md` | Kiểm kê tab FMRTE |
| `docs/club-plan.md`, `docs/squad-plan.md`, `re/club/*`, `re/squad/*` | Số liệu cũ mà test trích dẫn |
| `docs/rca-research-rules.md`, `re/rca-experiments/` | Chỉ khi người dùng bảo nghiên cứu RCA |
