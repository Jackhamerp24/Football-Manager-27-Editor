# Player — kiểm kê và tiến độ

## Other Nationality — 3/11/2026

Editor thêm Other Nationality cho Player và Staff, chọn Nation và Nationality Info; từ chối trùng và snapshot vector đã đổi. FMRTE `+` trên Mbappé (85139014) và Simeone (707720) chép dòng đã chọn vào cuối vector; bản ghi `nation* 00 00 08 09 64 <info> 00 FF`. UI editor đã thêm Portugal / Eligible For Nation trên cả hai; FMRTE mở tab mới đã hiện Portugal trên Mbappé. Đã hoàn nguyên header về buffer gốc và đọc lại toàn bộ byte gốc ở cả hai. Chưa đối chiếu FMRTE sau thao tác editor trên Simeone; chưa thử lúc game tự mở rộng/giải phóng buffer cấp ngoài.

Danh sách tab đã kiểm kê từ FMRTE: Information, Stats, Comparison, Contract, Career Plans, Swap Player, Clone Center,
Move to club, Swap National Call Up, Injury, Work Permit, Promises, Unavailability, Transfer Proposals, Positions,
Preferred Moves, Relationships, Languages, Scout Information. Chi tiết phần cũ xem handover §4.1; chưa coi Player hoàn thành.

## Future Contract, Comparison, Swap National Call Up, Clone Center

| Tab | Editor | Còn thiếu |
|---|---|---|
| Contract / Future Contract | Màn Contract đầy đủ trên hợp đồng tương lai; thử Dosso | — |
| Comparison | So sánh hai cầu thủ theo nhóm, tab Positions | — |
| Swap National Call Up | Đổi chỗ trong đội tuyển; thử Bruno ↔ Vieira | — |
| Clone Center | Chép theo nhóm sang nhiều cầu thủ; thử Mainoo → Micevski | Chép tên |
| Scout Information | Chưa làm (điểm do FMRTE tự tính) | Cần công thức |
| Swap Player | Bỏ qua theo người dùng (Move to club thay thế) | — |

## Information (FM27, save 15/8/2026, diff FMRTE 27 2/11)

| Field | Editor |
|---|---|
| Nationality, City Of Birth (tìm trong 58.475 city) | Có, đổi con trỏ |
| Nationality Info, Body Type, Hair Colour/Length, Skin Colour, Ethnicity | Có, dropdown theo thứ tự FMRTE |
| Days in Country | Có (ghi ngày đến = ngày game − số ngày) |
| Treated As Non-EU In Italy, Retiring After Spell, Retired From Int. Foot. | Có |
| Height, Born, CA/PA, Reputation, Left/Right Foot, Caps, Asking Price, Condition… | Có từ trước |
| First/Last/Common Name, Full/Shirt Name | Chưa: cần bảng tên toàn game |
| Make WonderKid | Không làm: FMRTE sinh ngẫu nhiên lại toàn bộ chỉ số, không phải cờ |
| Preset Register for UEFA, Trained at club/nation, Improve/Destroy Team Cohesion, All-in-one | Có (thêm dòng đúng định dạng game; FMRTE ghi đè dòng đầu tiên) |
| Preset Remove Int. Withdrawal, Tactical Understanding, Create history point | Chưa: chưa có mẫu / chưa tìm ra nơi lưu |

Đã thử UI trên Bruno: hoàn nguyên 10 field FMRTE đã đổi, object khớp từng byte với snapshot gốc; tick Retired + 2500 ngày, FMRTE hiện đúng, rồi hoàn nguyên.
Core 180 / App 115 pass; 8 đột biến (offset, mask, mã retirement, guard con trỏ) đều làm test fail.

## Career Plans, Transfer Proposals (FM27, save 15/8/2026)

| Tab | Editor | Còn thiếu |
|---|---|---|
| Career Plans | Đọc, sửa Condition/Job/Item/Scope, thêm, xoá (thử UI trên Bruno, FMRTE hiện đúng) | Long-term Job/Outlook (suy ra từ bảng ưu tiên vai trò), Role preferences, tạo container cho cầu thủ chưa có |
| Transfer Proposals | Danh sách như FMRTE (cùng bộ lọc status), sửa Fee và hợp đồng đề nghị (Type, Wages, Started, Expires, Rolling) | Đổi CLB mua / cầu thủ |
| Future Transfer | Chỉ hiện khi có chuyển nhượng hẹn trước; sửa Transfer Date, Value, hợp đồng. Thử UI trên Marmoush, FMRTE hiện đúng | Đổi Player / To |
| Contract | Loại hợp đồng giờ ghi đúng byte `+0xC3` (trước ghi nhầm `+0x26`); thử Briffa Part ↔ Full Time | Tab con Future Contract |

## Injury (FM27, game date 8/11/2026)

FMRTE có danh sách Injuries; mỗi mục có Name (tìm loại chấn thương), Minimum Days, Extra Days, By Physio,
By Specialist, By Injection, By Isolation; thanh danh sách có +/−. Cần kiểm riêng ý nghĩa +/−, không suy diễn thao tác xoá.

| Field | Mẫu FMRTE | Editor / kiểm chứng |
|---|---|---|
| Name | Saka 28122642: Tight hamstring; Ramos 7458272: Twisted ankle | Đọc; chưa đổi loại |
| Minimum / Extra Days | Saka 3/0; Ramos 12/7 | Sửa qua Save/Discard; writer giữ snapshot toàn bản ghi và kiểm membership. Saka 4/1, Ramos 13/8 đã hiện đúng trong FMRTE sau đóng/mở tab; hoàn nguyên 3/0, 12/7. UI Saka đã thử nháp, Discard, Save và hoàn nguyên bằng writer CLI. |
| Treatment | Byte cờ `+0x30`: 01 Physio (Bašić), 02 Specialist (Davies), 08 Isolation (Eriksen); ghi 04 hiện Injection, 03 hiện Physio + Specialist. 0x10/0x20 (N'Landu, Abraham) không có ô | 4 checkbox, lưu bằng Save changes; writer giữ 0x10/0x20. Đã thử UI trên Bašić (Specialist, rồi Physio + Minimum 6) và hoàn nguyên byte gốc |
| Thêm/xoá | FMRTE 27 `+` (2/11): chép bản ghi cùng loại của cầu thủ khác, cấp holder/vector/bản ghi mới; game không coi là chấn thương (Kane vẫn đá, không hồi phục, không có tag). `−` = end = begin | Không làm Add; xoá đã có (Heal) |

Ngày bắt đầu đọc được trong Core, FMRTE Injury không hiện field này trong hai hồ sơ trên. Chưa thêm setter ngày.

Kiểm trong game FM27 (Claude):
- Heal: Ugarte 78084541 (torn ACL) hết tag Inj, Fitness "Peak" sau một lần Continue; chấn thương không quay lại.
- Minimum/Extra Days: Onana 91207274, 237+207 → 20+5, sang ngày sau hết chấn thương trong game. Hai số là tổng tính từ ngày
  bắt đầu, không giảm theo ngày (đối chứng Diallo giữ 15+15). Game chỉ vẽ lại màn hình sau Continue.
Test mới kiểm ghi đúng bốn byte thời lượng, giữ nguyên mọi byte khác, từ chối snapshot cũ và bản ghi tách khỏi danh sách.
Đột biến đảo Minimum/Extra làm assertion thất bại; bản gốc Core 146/App 72 pass.

## Unavailability

FMRTE có Scope, Item (tìm đối tượng), Reason, Number of points, Start Date, Number of Days, Num. matches,
Num. matches fulfilled và +/−. Scope dropdown đã thấy Competition, Domestic Cup, Domestic League, All nation competitions,
All continent competitions, All international matches, Global Ban, Unavailable to play club football,
Suffering from undisclosed injury, Cannot play football due to medical reasons, Cannot play football due to personal reasons,
Holidays, Isn't going to be registered, Cannot play club football, Cannot play club football in official matches. Chưa map mọi mã.

- Oxlade-Chamberlain 28046572: Scotland, All nation competitions, None, points 0, 13/7/2026, Days −1, 3 trận/đã chấp hành 0.
- Boateng 29087451: England, All nation competitions, Sending off, points 0, 13/7/2026, Days −1, 7 trận/đã chấp hành 2.
- Đã thử writer Oxlade 4/1, Boateng 8/3 và mở lại FMRTE xác nhận cả hai, hoàn nguyên 3/0 và 7/2. Writer kiểm toàn snapshot, holder/vector và membership, chỉ ghi +0x1C/+0x1D.
- Có tab UI Save/Discard cho hai số đếm án theo trận; các án khác chỉ hiển thị ngày/số đếm. UI Boateng đã thử nháp không ghi RAM và Save 7/2 → 7/3, xác nhận rồi hoàn nguyên 7/2 bằng CLI. Core 147/App 72 pass; đột biến đảo số trận/đã chấp hành làm test fail.
- Kiểm trong game FM27 (Claude): Perry Ng 29111384 ghi 0/1 → 0/3; sau vòng Championship 21/11 thành 1/3 và án vẫn còn,
  nên game dùng số đã ghi. Đối chứng Álvarez 0/2 → 1/2; Hamer 2/3 → án bị xoá. Tooltip Sus luôn "next match", không dùng làm đáp án.
- Đổi Scope/Item/Reason/points/ngày/days và thêm/xoá: làm như FMRTE 27 (diff 2/11, mã Scope/Reason trong findings). Thêm cần holder sẵn có. Án thêm mới có hiệu lực: Bruno Global 3 trận bị loại khỏi trận Feyenoord 25/11, game tăng đã chấp hành lên 1/3. Item hiện dạng số.

## Work Permit

Tyrese Campbell 28108499: khối Work Permit trống với +/−; Work Permit Denied = None. Cần kiểm mẫu có dữ liệu.
Haaland 29179241 và Bruno Fernandes 43124203 cũng trống/None. Sau bốn hướng kiểm tra (UI, bảng FM26,
metadata REA, quét RAM toàn game) vẫn chưa xác định được record: dừng theo quy tắc quá ba vòng. Probe đọc 3,83 GB
RAM dùng keys toàn bộ 50.043 Player và 251 Nation, trả 1.250 ứng viên chưa xác nhận; không suy ra game không có permit.
Chưa có reader/writer Work Permit. Chi tiết và giới hạn ở handover.
Các tab Unavailability, Languages, Relationships, Preferred Moves và Information còn thiếu tiếp tục theo thứ tự handover.

## Languages, Preferred Moves, Relationships (Claude, FM27, 22/11/2026)

| Tab | Bố cục FM27 | Editor | Còn thiếu |
|---|---|---|---|
| Languages | `player+0x378` vector con trỏ → bản ghi 16 byte (Language* +0, Value +8). Mbappé Spanish 8 / English 5 / Arabic 6, Kane German 10 | Sửa Value 1–10 (khoảng giá trị của toàn game). Ghi thử Spanish 9, German 11, đối chiếu FMRTE; thử UI English 6, đã hoàn nguyên. Thêm/xoá như FMRTE 27 (diff 2/11): bản ghi + vector mới bằng VirtualAllocEx, xoá lùi end; thử UI Kane Albanian/Arabic, qua một trận, đã xoá lại | Rủi ro game free vùng cấp ngoài |
| Preferred Moves | `player+0x348` 8 byte bit, ô FMRTE theo hàng (bit 0–60) | 61 checkbox, Select All/Clear, giữ bit 61–63. Đối chiếu 9 ô trên Kane/Mbappé, ghi bit 0, 58–60; thử UI Crosses Early | "Plays Ball With Feet" không nằm trong 8 byte này |
| Relationships | Vector inline 16 byte ở holder `player+0x300`: Type +0xB, Value +0xC, Reason +0x8, cờ +0xD | Likes/Dislikes Person, Likes Team: sửa Value, Reason, Permanent; thêm (tìm cầu thủ hoặc CLB) và xoá như FMRTE 27 (diff 2/11). 19 dòng Mbappé khớp FMRTE; thử UI trên Kane (Saka, Rice, Tottenham, Arsenal), đã hoàn nguyên | Other Nationality: đọc, sửa Nationality Info (+0xD), xoá; chưa thêm. Các loại khác (Games Played…) |
