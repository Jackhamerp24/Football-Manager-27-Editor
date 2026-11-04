# Nation: kiểm kê tab FMRTE 27 và độ phủ của editor

Đáp án: FMRTE 27 trên save 8/11/2026, Anh (765) và Việt Nam (145). FMRTE ở máy này chưa kích hoạt: Save và nút `+` mở
wizard kích hoạt, nên FMRTE chỉ dùng để đọc. Cách map: ghi bằng editor rồi mở lại tab FMRTE.

Trạng thái: **Có** = đọc + ghi, đã so FMRTE · **Đọc** = chỉ đọc · **Chưa** = chưa map.

Phạm vi hiện tại theo yêu cầu người dùng: bỏ qua đội nữ; tiếp tục các phần còn lại.

## 1. Information

| Mục | Nội dung trên FMRTE | Trạng thái |
|---|---|---|
| Overview | Name, Unique ID | Đọc |
| | Capital (thành phố) | Đọc: `nation+0xE8`, FMRTE hiện chữ thường; Vietnam Ha Noi, Brazil Brasília (DF) khớp |
| | Continent (dropdown) | Có: `nation+0xF0`; Brazil đổi qua FMRTE (Asia) và qua editor (South America), `+0x2F0` của đội nữ không đổi. Chưa có "World" |
| | Development, Economic Factor, Years To Gain Nationality | Có |
| | Gain Nationality Type (dropdown) | Có: `+0x1EB`, 1 Continuous / 2 Accumulative; England 765 và Vietnam 145 đã ghi thử 1 → 2, đối chiếu FMRTE và khôi phục 1 |
| | Does Not Allow Dual Nationality (checkbox) | Bit `0x02` tại `+0x1E9`: China PR và Japan đã tick, Spain và France không tick. Spain 00 → 02 hiện tick; China PR 02 → 00 hiện bỏ tick; đã hoàn nguyên cả hai. |
| Agreements | Danh sách: thoả thuận / Type / From / Until / Active, nút `+` thêm, `−` thu gọn | Chưa: đã thử một lượt (3/11), xem findings; `nation+0x90` là vector Non-Foreign |
| Nations Treated as EU | Như trên (Anh: EFTA, EU) | Chưa |
| Nations Treated As Non-Foreign | Như trên (Anh: UK And Ireland, Overseas Territories Act) | Chưa |

Nút thêm Nations Treated as EU đã mở wizard kích hoạt trên FMRTE 27. Các thao tác Agreements và Non-Foreign chưa xác nhận
đầy đủ trạng thái khoá; cần quan sát riêng và dùng FMRTE 26 để xác định hành vi trước khi triển khai, không đoán cách cấp phát.

## 2. National Teams (Men's Football / Women's Football)

| Mục | Nội dung | Trạng thái |
|---|---|---|
| Overview | FA Financial Power, FIFA Position, FIFA Ranking Points, 3 năm, Foreign Manager Likelihood, FA Patience, Youth Rating, Max. Youth Age, 3 loại khán giả | Có (nam `nation+0x108`, nữ `nation+0x110`) |
| | Importance (dropdown) | Claude đã thêm writer ghi cả `[nation+0x108]+0x87C/+0x87D`, từ chối khi hai bản sao khác nhau; mã 1–4. Đã có test hai bản sao. Các phép hiển thị Spain 1 → 3 và France 1 → 2 đã xác nhận trước đó và hoàn nguyên. |
| | Allow Multiple Assistant Managers | Có cho đội nam: `[nation+0x108]+0x87E`; Spain tắt → bật → tắt và France bật → tắt → bật, đã đối chiếu FMRTE 27 và hoàn nguyên. Đã thử Save và hoàn nguyên từ UI editor trên Spain. Đội nữ chưa xác nhận. |
| | Background / Foreground Colour | Chưa: đã thử một lượt (3/11), xem findings |
| EuroCoefficients | Hệ số theo mùa (chỉ châu Âu) | Chưa; RAM Spain/France build mới có vector 10 float tại holder `+0x190`, chưa đối chiếu FMRTE hoặc xác nhận thứ tự mùa, xem findings |
| Teams | Chọn đội (England, U21…), Allow Licensed Kits, Reputation (nam 9600, nữ 9900), Stadium | Stadium: đọc (`nation+0x118`, Vietnam My Dinh, Brazil trống khớp FMRTE). Licensed Kits: chưa xác nhận trên FM27; mã FMRTE 26 gợi ý bit đảo `0x08` của TeamFlags, offset cấu hình phụ thuộc nhánh `0x22/0x2E`, xem findings. Reputation đội nam: có (`[nation+0x108]+0x18` → team, int32 `+0xA8`; Vietnam 3000, U23 2400, U19 1500, Brazil 9200, England 9600 khớp FMRTE; chưa có tên từng đội); guard nguồn danh sách và giữ chỉ số gốc khi bỏ phần tử không hợp lệ đã bổ sung, chưa thử lại UI/game; editor giới hạn 30000, chưa xác định giới hạn FMRTE. Đổi Stadium, đội nữ: chưa |

## 3. Squads

Giống màn Squads của CLB: nam/nữ × England, U21, U20, U19, U18; lưới cầu thủ; Player Presets, Other actions, Apply to all.
Trạng thái: **Chưa**. Có thể dùng lại SquadView + preset của CLB khi tìm được squad vector của từng đội tuyển.

## 4. Kits and Colours

6 bộ (Home, Away, Third, GK Home/Away/Third) × nam/nữ. Mỗi bộ là danh sách item (Shirt, Text, Icon, Shorts, Socks) có
Competition; item có Type, Foreground, Background, Outline, Number Colour, Number Outline Colour, Competition, Kit for
specific season, Alternative Kit Number, Item Style. Nút **Add / Duplicate / Remove** item.
Trạng thái: **Chưa** (CLB cũng chưa có; nên làm chung một lần).

## 5. Tactics

Last Match / Suggested: sơ đồ In / Out of Possession (vai trò + điểm), Starting eleven, Substitutes. Không có ô sửa.
Trạng thái: **Chưa** (chỉ đọc, ưu tiên thấp).

## 6. Staff

Danh sách staff đội tuyển (National Job, tuổi, đội, quốc tịch, CA, PA) + Staff Presets, Renew / Expire / ±1 Year,
Apply to all. Trạng thái: **Đọc một phần**: màn Staff đọc được từng người; chưa có bộ lọc theo đội tuyển, chưa có hợp
đồng staff.

## 7. Relationships

Danh sách quan hệ (Type, Name, Value), nút `+` / `−`; panel bên phải: Type (Rival Nation…), Nation, Value, Reason.
Anh: Scotland 100, Germany 100, France 75, Wales 55, Northern Ireland 50, Republic of Ireland 60, Argentina 90.
Trạng thái: **Một phần**: đọc và sửa Value của Rival Nation cho đội nam qua nháp Save/Discard; Spain–Italy 60 → 61 và France–Germany 70 → 71 đã đối chiếu FMRTE và hoàn nguyên, UI editor đã thử Save/Discard và hoàn nguyên Spain. Sau đó bổ sung guard capacity vector, chưa thử lại UI/game sau sửa. Chưa hỗ trợ các loại khác, Reason, chọn Nation hoặc thêm/xoá.

## 8. Transfer Preferences

Nations: mỗi dòng một nước (Rating, Scouting Knowledge, Minimum Age, Maximum Age); Continents (Anh: trống).
Anh: Australia −2 / 0 / 14 / 45, Germany −2 / 0 / 23 / 28, Northern Ireland −2 / 0 / 14 / 45. Trạng thái: **Một phần**: các dòng Nations được đọc và sửa bốn giá trị qua Save/Discard; cả bốn field đã ghi thử và đối chiếu FMRTE trên Spain–Austria và France–Belgium rồi hoàn nguyên. Writer qua UI đã thử Rating trên hai cặp và hoàn nguyên; ba field còn lại chưa thử riêng qua UI. Sau đó đã bổ sung guard capacity vector và test đột biến, chưa thử lại UI/game sau sửa guard. Continents chưa map: RAM build mới có vector ứng viên tại holder `+0x108` với record Spain/Portugal/Brazil/Argentina trỏ continent, England/France rỗng; Brazil có hai record cùng đích nhưng byte khác. Chưa đối chiếu FMRTE build mới (xem findings). Chưa hỗ trợ thêm/xoá dòng.

## Thứ tự đề xuất

1. Importance: đối chiếu đầy đủ các byte được ghi trên FM26 rồi xác nhận FM27.
2. Relationships và Transfer Preferences: hoàn thiện loại quan hệ, thêm/xoá và Continents.
3. Agreements / EU / Non-Foreign, kèm thêm và xoá dòng.
4. Reputation đội tuyển, Licensed Kits, Stadium của đội tuyển.
5. Squads đội tuyển (dùng lại màn Squads).
6. Kits and Colours (làm chung với CLB).
7. EuroCoefficients, Tactics (chỉ đọc).
