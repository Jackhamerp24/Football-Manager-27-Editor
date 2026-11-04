# Club — kiểm kê ban đầu

Nguồn kiểm kê hiện có: FMRTE 27 với Sheffield United (ID 708) trong `docs/club-plan.md`, và mã editor hiện tại. Đây là bản đối chiếu ban đầu, **chưa phải kiểm kê hoàn chỉnh hai CLB**; các tab chưa có ảnh hoặc chưa kiểm tra nút được đánh dấu chưa kiểm.

| Tab FMRTE | Đã ghi nhận | Editor / còn kiểm |
|---|---|---|
| Information | Overview, Teams, Facilities; số liệu Sheffield United trong `club-plan.md` | Có nhiều field; cần đối chiếu từng field và thao tác với mẫu thứ hai |
| Squads | Danh sách theo đội, presets và thao tác hàng loạt | Có màn Squads; cần kiểm Team Cohesion và International Withdrawal |
| Finances | Overview, Transfer embargo, Sponsors and Other, Debt and Loans | Có nhiều field; Debt đã đọc/sửa Original Debt, Start/End, Source (4 mã đã đo), Monthly Repayment, Monthly Interest Repayment và Interest-only payment trên build 2493426, thử UI và đọc lại RAM/FMRTE. Sponsors Total Value đã đọc hai CLB; **tạm dừng sửa** vì chưa đo side effect thao tác lưu FMRTE 27, xem findings. Các field/thao tác khác cần kiểm tiếp |
| Club Vision | Type, Source, Importance, Item, For Competition, Start, End | Reader chỉ nhận các trường đã xác nhận; writer/UI Importance đã chuẩn bị và test bytes hai CLB trên cả build cũ và mới. Capability ghi tắt trên cả hai profile vì chưa đối chiếu thao tác ghi FMRTE 27 hoặc thử UI/RAM sau ghi. Các field khác và thêm/xóa cần kiểm |
| Possible Signings | 11 dòng tại Man Utd, Sheffield United trống; xem phần đối chiếu dưới | **Tạm dừng:** chưa tìm được nguồn bảng sau quét tham chiếu hai ID, dò đồ thị club nông và kiểm PE FMRTE bằng REA; chi tiết trong findings |
| Player Clauses | Điều khoản cầu thủ | Đã có reader/writer một phần; cần đối chiếu màn CLB và các loại còn thiếu |
| Transfer Center | Sheffield United có các đề nghị, CLB đến/đi, phí, trạng thái | Có mô hình giao diện trong `ClubLists.cs`; chưa xác nhận reader và thao tác hoàn chỉnh |
| Kits and Colours | Đã ghi Home/shirt: các màu và Item Style 3 | Có mô hình giao diện trong `ClubLists.cs`; chưa xác nhận reader và thao tác hoàn chỉnh |
| Tactics | Last Match và Suggested, sơ đồ In/Out of Possession, Starting eleven; Suggested có lựa chọn xếp đội | **Tạm dừng:** các cụm tham chiếu hai cầu thủ chưa xác định được dữ liệu 11 vị trí Last Match; xem findings |
| Staff | Sheffield United có danh sách gồm tên, chức vụ, tuổi, Team, CA/PA | **Một phần:** tab Staff (Job, Name, Age, Team, Nation, CA, PA); số người khớp FMRTE (Man Utd 94, Sheffield United 48). Team đội 1/U21/U18 đã đối chiếu hai CLB trên build 2493426; loại chưa biết và build cũ để trống. Open Selected đã nối tới màn Staff và thử Jim O'Brien; FMRTE mở tab nền. Chưa có Renew/Expire, presets |
| Affiliations | Parent, Feeder, Start, End, Type, Annual Fee và các quyền lợi | Có Parent, Feeder, Type; Annual Fee không âm, Start/End Date, % Chance Of Friendly, % Loan Wage Paid By Parent đang bật, Play Friendlies, First Option To Buy, Players Go On Loan và Scouting Knowledge Shared đọc/sửa trên build 2493426, đã thử UI/RAM/FMRTE. Maximum No. Players Loaned tạm dừng vì chưa có mẫu bật, ngày sentinel, bật/tắt tỷ lệ và các field khác, thêm/xóa cần xác nhận |
| Regional Divisions | Division, Level, Year | Reader hiện chỉ xác nhận Level; tên/ID giải, năm và thêm/xóa cần xác nhận |
| Relationships | Type/Name/Value; panel phải: Rival (Club, Value, Reason), Likes/Dislikes Person (Person, Value, Reason chỉ cho Likes, Permanent) | **Một phần:** đọc đủ 37 + 16 dòng và sửa Value/Reason/Permanent (Core + tab trong app); guard capacity vector đã bổ sung, chưa thử lại UI/game sau sửa. Chưa có `+`/`−`, đổi người/CLB đích, ý nghĩa Unknown (29/68) |
| Tactical Understanding | Các giá trị theo từng đội; Sheffield United đội 1 và U21 có số khác nhau | Có field theo team; cần đối chiếu đầy đủ và các thao tác liên quan |
| Supporters | Distribution và Profile; giá trị Sheffield United trong `club-plan.md` | Có nhiều field; cần đối chiếu đầy đủ |

Không dùng trạng thái mô hình UI làm bằng chứng rằng dữ liệu thật đã đọc hoặc ghi được. Bước tiếp theo là kiểm từng tab trong FMRTE trên hai CLB nam, ghi rõ danh sách field và nút, rồi đối chiếu với reader/writer thực tế.

## Đọc RAM hiện tại — 3/11/2026

Lệnh `clublists` chỉ đọc trả về Sheffield United (708): 9 mục vision loại culture, 1 affiliation, 3 regional level 6/7/8; Manchester United (680): 0 mục vision loại culture, 4 affiliations, 7 regional level 1/2/3/4/1/2/3. Đây là kết quả của reader hiện tại, **chưa phải** xác nhận số lượng đầy đủ trong FMRTE: reader có bộ lọc loại bản ghi, và tên đội nữ trong affiliation có thể không được phân biệt đúng. Không dùng 0 mục culture để kết luận CLB không có vision.

## Đối chiếu FMRTE — 3/11/2026

- **Possible Signings:** bảng có Inf, Name, Age, CA, PA, Nationalities, Club, Asking Price, Positions, Actual Rating, Potential Rating, Avg Match Rating. Sheffield United (708) trống; Manchester United (680) có 11 người, gồm Martin Baturina (CA 146 / PA 159, Como 1907) và Yeremay (140 / 159, Deportivo La Coruña). Chưa xác định nguồn danh sách hay công thức rating.
- **Transfer Center:** Type, Name, From, To, Transfer Fee, Status; phần Information của dòng chọn có To và Value. Sheffield United có Alex Shipstone đến với phí £0, Player accepted; Manchester United có bốn dòng, gồm Alejandro Balde dạng Loan từ Barcelona, £150.000, Ready to sign. Phạm vi lọc chưa kiểm đầy đủ.
- **Kits and Colours:** sáu tab Home, Away, Third và ba bộ GK; item Shirt, Text, Icon, Shorts, Socks. Chọn item hiện Type, năm màu, Competition, Kit for specific season, Alternative Kit Number, Item Style; có Add/Duplicate/Remove. Sheffield United Home Shirt style 3, Manchester United Home Shirt style 25.
- **Tactics:** Sheffield United hiện màn gợi ý trống và chọn Formation; Manchester United có Last Match / Suggested, hai sơ đồ In/Out of Possession, Starting eleven và Substitutes. Chưa xác định tính toán hoặc khả năng ghi của màn.
- **Relationships:** có danh sách Type/Name/Value, nút `+`/`−`; dòng Rival có Club, Value, Reason. Sheffield United → Sheffield Wednesday 100 / Local; Manchester United → Liverpool 100 / Local, Leeds United 95. Còn Likes/Dislikes Person và các loại Unknown; chưa map bộ nhớ hoặc xác nhận hành vi ghi.

### Relationships — phần đã triển khai (3/11/2026)

Vector 16 byte inline ở `club+0x48` (profile `club.relationships`); bố cục và mã reason trong `re/FM27-findings.md`. Đọc khớp FMRTE trên Man Utd (37 dòng) và Sheffield United (16 dòng). Ghi qua `ClubRelationshipWriter` (guard: vtable CLB, vector, nguyên bản ghi 16 byte), đã ghi Sheffield Wednesday, McGoldrick, Arsenal rồi mở lại FMRTE: hiển thị đúng. Test: `ClubRelationshipTests` (Core, 6), `ClubRelationshipLabelsTests` (App, 2), đã kiểm đột biến. Đã thử UI editor một lần: Sheffield United → Relationships → Barnsley 75/Local sửa thành 77/Historic bằng Save changes, đọc lại RAM bằng `clubrel 708` ra value 77, reason 3.

### Transfer Center — phần đã triển khai

Reader quét vector offer toàn game và chọn theo CLB bán/mua; tra tên từ cả Player và Staff, lọc các trạng thái 2/3/15/20 như reader Transfer Proposals hiện có. Trên save này đọc và UI khớp 1 dòng Sheffield United và 4 dòng Manchester United như trên, gồm Figueroa và Rocha là staff. UI có ô Value dùng `TransferProposalWriter`: thử Balde 150.000 → 150.001 → 150.000, đọc lại toàn bộ 0x98 byte khớp ảnh gốc. Giá trị 150.001 đã từng được FMRTE xác nhận ở tab Transfer Proposals trước đó; lượt thử UI Club này chưa mở lại FMRTE khi giá trị đổi. Chưa xác nhận mọi trạng thái, loại offer, nguồn đích hoặc hành vi khi người trong offer không có trong danh mục đã nạp.
