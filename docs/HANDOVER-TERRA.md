# Handover → Terra: nation section (FMRTE 27 editor)

Viết 6/11, sau khi mở lại dòng Agreements. Đọc `docs/TODO.md` (quy tắc ở đầu file) và `docs/HANDOVER.md` (§2, §3 Oracle mục 0)
trước; file này chỉ chứa trạng thái riêng của phần **Nation** để người tiếp nhận vào việc ngay.

## 0. Môi trường đang chạy

| | |
|---|---|
| Repo | `C:\Users\Duong\Desktop\FMRTE clone 27` (nhánh `main`) |
| Build / test | `dotnet build Fm27Editor.slnx -c Release` · `dotnet test Fm27Editor.slnx -c Release` |
| Publish | `dotnet publish src\Fm27Editor.App -c Release -o "C:\Users\Duong\Desktop\FM27 Editor\App"` và tương tự `src\Fm27Editor.Tools` → `...\Tools` |
| Chạy exe | Copy thư mục publish ra `%TEMP%` rồi chạy (exe trong workspace có thể không thấy `fm.exe`) |
| `memscan` | `%TEMP%\fm27ms\memscan.exe`; **luôn truyền địa chỉ dạng chuỗi `"0x..."`** (`("0x{0:X}" -f $addr)`), truyền số nguyên sẽ đọc ra 0 mà không báo lỗi |
| Frida | `python3` = `C:\Users\Duong\AppData\Local\Python\bin\python3.exe`, frida 17.25.2. Trace đọc: `re/tools/fmrte-rpm-trace.py --fmrte-pid <pid> --fm-pid <pid> --log <jsonl> [--target 0xADDR] --ring 800 --seconds 60`; logger ghi: `%TEMP%\fmw2.py <fmrtePid> <log>` (READY khi sẵn sàng) |
| Lúc viết | FMRTE 27 PID **5848**, `fm.exe` PID **17368**, module `game_plugin.dll` 0x7FFAD8B60000. PID đổi sau mỗi lần mở lại app/game — luôn kiểm lại |
| Refresh sau khi ghi | FMRTE **cache** dữ liệu lúc Load Game: muốn thấy giá trị mới phải bấm **Load Game** rồi mở lại tab (địa chỉ RAM đổi sau Load Game) |

## 1. Trạng thái Nation (theo `docs/TODO.md`)

### Đã xong, đã kiểm bằng FMRTE (không làm lại)
- **Kits and Colours**: `nation+0x108` → team (vtable `0x4C9B0E8`) `+0xB0` = holder; kits ở `holder+0x78` (England 21 item),
  record 0x28 byte, con trỏ trỏ vào byte màu đầu tiên (−8 = record): `+2` kit, `+3` type, `+4` flags, `+0x1C` style,
  `+8` bốn màu BGRA. Core `NationKits`, CLI `nationkititem`, card **KITS** (Kit/Type/Style/FG/BG).
- **Background / Foreground Colour**: holder `+0xA0` fg, `+0xA8` bg (BGRA dword). Core `NationColourAccess`, CLI `nationcolours`, card **COLOURS**.
- **Allow Licensed Kits**: bit `0x08` của byte `team+0x2E`, **xanh = bit 0**. Core `NationTeamReader.AllowLicensedKits/SetAllowLicensedKits`, checkbox trong card TEAMS REPUTATION.
- **EuroCoefficients**: `team+0x190` = vector 10 float (mùa cũ → mới), card **EUROCOEFFICIENTS**, CLI `nationcoeff`.
- **Transfer Preferences (nations + continents)**: vector 16 byte/dòng ở `team+0xF0` (nations) và `team+0x108` (continents);
  `+0` rating `sbyte`, `+1` maxAge, `+2` minAge, `+4` knowledge, `+8` target pointer.
  **Target của dòng nations phải là "preference team" = `nation+0x108` của nation đích**, không phải nation object.
  Core `NationPreferences` (`SetTarget`/`Add`/`Remove`/`TargetFor`), CLI `natpref`, hai bảng trong trang Nation (dropdown target + “−”, “+” mỗi bảng).

### Đang mở lại — **Agreements** (dòng `[ ]` đầu tiên trong Nation)
Hiện có **2/5 loại** và **chưa có picker target**:
- EU vector `nation+0xA8`, Non-Foreign `nation+0x90` (inline begin/end/cap), mỗi phần tử là con trỏ tới record **0x30 byte**.
- Đo được: **Region slot `+0x18`**, **Agreement slot `+0x20`**, **From/Until `+0x28`/`+0x2C`** (4 byte/mốc: 9 bit thấp = ngày trong năm,
  2 byte sau = năm; `01 00 6C 07` = 1/1/1900 = *No limit*).
- FMRTE **đổi Type** = ghi 8 byte 0 vào slot cũ rồi ghi con trỏ vào slot của loại mới ⇒ loại = slot đang có con trỏ (đây là suy luận,
  cần đo thêm — xem việc 1).
- **Chưa biết slot của Nation / ExcludedNation / Continent.** Khi làm xong phải có đủ **5 catalogue**:
  organisation (European Economic Area, European Free Trade Association, European Union, Organisation of African Caribbean and
  Pacific States, Organization of Turkic States, Overseas Territories Act), **region** (gồm cả các mục số **23…29** lẫn tên:
  Asia, Caribbean, Central Africa, Central America, Central Asia, Central Europe, East Africa, East Asia, Eastern Europe,
  Middle Eastern, North Africa, North America, Oceania, South America, South Asia, South Europe, Southeast Asia, Southern Africa,
  Uk And Ireland, Western Africa), continent (7), nation (251), excluded nation (251).
- **Lỗi dữ liệu do thí nghiệm trước để lại trên save rác**: dòng Region trong “Nations Treated as EU” của **England (765)** đang trỏ
  tới object `0x2717E9FAC90` (cặp id 21/23) — object cùng class region (`vtable 0x7FFADD8D8508`) nhưng không có trong catalogue nên
  FMRTE in ra **“23”**; dòng Uk And Ireland gốc trỏ `0x2717E9FB080` (id 12/14). Ngoài ra có **2 dòng EFTA lặp** do bấm “+” của FMRTE.
  Không cần sửa nếu người dùng không yêu cầu.

### Chưa làm
- **Squads**: xem §2 (gần xong).
- **Staff (ban huấn luyện đội tuyển)**: chưa bắt đầu.
- **Tactics (đội tuyển)**: cùng chỗ kẹt với Tactics CLB (xem mục “Kẹt” của TODO).

## 2. Squads — dở dang, làm tiếp ngay sau Agreements

Đã xong:
- `src/Fm27Editor.Core/Nations/NationSquads.cs`: `Read(team)` đọc squad vector **`team+0x38`** (profile `team.squad`) nhưng validate
  **vtable national team** = `nation.teams.teamVtable` (khác vtable CLB → `SquadReader` của CLB không dùng được);
  `ReadNation(nation)` trả 7 đội (senior, U21, U20, U19, U18…).
- CLI `nationsquad <nation> [teamIndex]`: England senior = **26 cầu thủ**, đúng thứ tự + CA/PA như FMRTE
  (Pickford 157, James 173, Guéhi 165/168, Konsa 154/155, Shaw 149/162, Rice 183/184, Anderson 160/171, Saka 182/188…).
- UI: card **SQUADS** trong `NationView.cs` — dropdown chọn đội (England/U21/…) + bảng Name/Positions/CA/PA/Age.
- Test `tests/Fm27Editor.Core.Tests/NationSquadsTests.cs` (đọc vector + từ chối vtable CLB). **Core 398/398.**
  Một test `ReadNation` đã bị bỏ vì profile mẫu của test là build cũ (`27.0.0+2486695`) không khớp `nation.teams`;
  phần này chỉ kiểm được trên game thật bằng CLI.

Còn lại để đánh `[x]`:
1. Chạy chứng minh mutation (đột biến vtable national → club trong `NationSquads.Read` phải làm test fail, rồi hoàn nguyên).
2. Publish App, mở app → Nations → England → chụp ảnh card SQUADS (đối chiếu tên/CA/PA với FMRTE), lưu ý UIA: click vào **giữa
   DataItem của lưới** (không phải toạ độ cố định) mới chọn được dòng; app có thể đứng sau cửa sổ khác.
3. Cập nhật `docs/TODO.md` (dòng Squads → Đã xong) + ghi offset vào `re/FM27-findings.md`.

## 3. Việc 1 khi làm Agreements: đo 3 slot còn lại

Cách nhanh nhất (đã dùng cho Region/Agreement):
1. Chạy logger ghi `%TEMP%\fmw2.py <FMRTE_PID> %TEMP%\fmw6.log`.
2. Trong FMRTE: England → Information → Agreements → đổi **Type** của một dòng sang **Nation**, `Ctrl+S`; đọc log → offset + giá trị.
   Lặp lại cho **ExcludedNation** và **Continent** (mỗi lần đổi một loại, ghi lại offset).
3. Ghi kết quả vào `re/FM27-findings.md` (mục Quan sát), rồi:
   - Core `NationAgreements`: mở rộng enum `AgreementKind` đủ 5 loại + map slot, `SetTarget(row, kind, pointer)`,
     `SetKind(row, kind, pointer)` nếu FMRTE cho đổi loại.
   - Catalogue: continent lấy từ `NationLinks.Continents(nations)`; nation lấy từ `Nations.LoadAll()`;
     **organisation + region** cần nguồn: tìm trong RAM (vector object cùng class như `0x2717E9FB080`) hoặc bảng cố định của FMRTE
     (đối chiếu ảnh người dùng đã gửi trong hội thoại).
   - UI (NationView, card AGREEMENTS): mỗi dòng có Type picker + Target picker đổi theo Type, “+”/“−”, Save chung; đọc/ghi qua
     `NationAgreements`.
   - Test: thêm case cho từng loại (đọc đúng slot, ghi đúng slot, guard stale), mutation chứng minh.

## 4. Quy ước bắt buộc (tóm tắt từ TODO/HANDOVER)

- Chỉ làm dòng `[ ]` đầu tiên trong “Làm tiếp”; xong = UI + ghi thật trên save rác + đọc lại RAM + hành vi khớp FMRTE + 1–2 test.
- Không disassembly `game_plugin.dll` (ACL chặn file trên disk); dùng Frida + `memscan`/`findval`/`refs` (BFS `findval` rất hiệu quả để
  tìm đường dẫn: `memscan findval <obj> 8 i32:<giá trị>`).
- Vector **inline** thì ghi header tại `object+offset`, không deref (đã từng gây mất dữ liệu Affiliation vì lỗi này).
- Mỗi màn một nút **Save changes** chung (nháp → Save/Discard), không Apply từng trường.
- Không hoàn nguyên giá trị đã ghi, không dọn save, không viết công cụ chẩn đoán.
- Báo cáo: tên dòng vừa đổi trạng thái + thời gian thực; offset chi tiết ghi `re/FM27-findings.md`, **không** ghi nhật ký vào HANDOVER.
