# TODO: tính năng FMRTE 27 editor chưa có

Quy tắc (bắt buộc, đọc trước):
1. Làm **dòng `[ ]` đầu tiên** trong phần "Làm tiếp", từ trên xuống. Không làm việc ngoài danh sách: không thử lại tính
   năng cũ, không viết công cụ chẩn đoán, không dọn dữ liệu save, không hoàn nguyên giá trị đã ghi.
2. **Xong** = tính năng mới hiện trên app (UI), ghi thật một lần trên save rác, đọc lại RAM khớp, **hành vi khớp FMRTE**
   (không tự đặt quy tắc), có 1–2 test cho logic ghi mới. Chuyển dòng xuống "Đã xong".
3. **Không giới hạn 45 phút nữa (người dùng bỏ 5/11).** Kẹt thật thì mới chuyển dòng xuống "Kẹt", ghi 2 dòng: kẹt ở đâu, cần gì. Ưu tiên FMRTE + Frida để đi tới cùng thay vì bỏ sớm.
4. Field nằm sâu: dùng Frida hook FMRTE (HANDOVER §3 Oracle mục 0). Không disassembly. **Cần người dùng thao tác FMRTE thì
   hỏi**, gom nhiều thao tác vào một lần hỏi.
5. Ghi có thêm/xoá dòng hoặc đổi header vector: thử lần đầu trên đối tượng ít quan trọng, đọc header trước và sau.
   Vector **inline** (header nằm trong object) thì ghi tại `object+offset`, không deref.
6. **UI: một nút Save chung** cho mọi chỉnh sửa trên màn (nháp → Save changes / Discard), không Apply từng trường.
7. Báo cáo: tên các dòng vừa đổi trạng thái, thời gian thật. Chi tiết offset ghi `re/FM27-findings.md`. Ghi chú trong file
   này tối đa 2 dòng mỗi mục. Không ghi nhật ký vào HANDOVER.

## Làm tiếp

### Lỗi cần ưu tiên
- [ ] Advanced Search: Core lọc nơi sinh, hợp đồng, clause theo mã, châu lục/quốc tịch, nơi đào tạo, quan hệ, Wonderkid, ranges chỉ số/vị trí và Preferred Moves; chưa đối chiếu đầy đủ FMRTE. Còn xác nhận mã clause, Region, EU national, regen, positional rating, contract status và ngữ nghĩa bộ lọc Relationships FMRTE.
- [ ] Kiểm UI và thử export/import chỉ số giữa hai cầu thủ trên save rác; bản build mới chưa thử do editor cũ đang mở cửa sổ filter. Export CSV/JSON chỉ gồm attributes và personality, import qua nháp Save chung; personality không rõ (=0) được bỏ qua và báo trạng thái.
- [ ] Club Income Statement: writer mới kiểm giá trị cũ nhưng chưa kiểm vtable CLB và chưa nhận snapshot con trỏ tài chính; cần guard xuyên suốt Core/CLI/UI trước khi coi ghi là an toàn.
- [x] App test tìm CLB: sửa hiển thị tên, bộ test hiện có đã chạy đạt.
- [x] UI mới: đã kiểm và sửa toàn bộ 8 module, sub-tab và dialog trên app thật ở `1100×820` và `1020×760` (`clipped=0, wrapped=0, overflowX=0`); xác nhận dải tab đối tượng, danh sách Clubs, chọn nhiều squad, `ColourPickerWindow`, sticky rail Player và bố cục Nation/Player.
- [ ] Competitions: trên Game Pass `27.0.0+2493426`, `ListRootRva` ném `MemoryAccessException`; UI đã fallback nạp `730` giải đang hoạt động từ `Player.Division` qua `Reload` guarded, còn cần tìm `ListRootRva` đầy đủ cho Game Pass.
- [ ] Destroy và Tactical Understanding: kiểm tra ghi và đọc lại RAM trên game thật; Destroy có nhiều bước guarded, lỗi giữa chừng có thể để lại một phần thay đổi.

### Nation
- [ ] Nation → Staff: backend/CLI đọc danh sách, chức vụ, đội phụ trách, quốc tịch, tuổi, CA/PA; England 22 người khớp FMRTE. UI chờ DeepSeek; thao tác hợp đồng tạm dừng bên dưới, Steam chưa kiểm.
- [ ] Nation → Squads: backend/CLI thêm Condition và Asking Price (England khớp FMRTE); còn số áo tuyển, quốc tịch phụ, rating, preset hàng loạt và UI.
- [ ] Nation → Tactics: rà các chức năng khác của đội tuyển; không làm các thanh Tactical Understanding theo quyết định của người dùng.

### Club (phần còn lại)
- [ ] Staff Renew / Expire: thêm test (gồm hợp đồng còn hạn xa hơn hôm nay) và đối chiếu nút FMRTE bằng Frida log ghi;
  quy tắc hiện tại là tự đặt (Renew = max(hạn, hôm nay) + số năm; Expire = hôm nay − 1 ngày).
- [ ] Affiliations: tạo quan hệ mới (Add) trên UI. Hiện chỉ Duplicate quan hệ có sẵn.
- [ ] Kits: đổi **Type** của item theo dropdown FMRTE: Shirt, Icon, Text, Shorts, Socks, Shirt (Square for number), Shirt (Plain).
- [ ] Kits: **Add / Remove / Duplicate** item (như FMRTE).
- [ ] Kits: Competition của item.
- [ ] UI Save chung: chuyển Club Vision, Affiliations, Sponsors, Debt và các màn còn nút Apply từng trường sang nháp +
  Save changes (Kits đã chuyển).

### Player
- [ ] Move theo ngày như FMRTE (phí, ngày, Set transfer terms; game tự hoàn tất). Xem HANDOVER §5 mục Player.
- [ ] Move dạng Loan.
- [ ] Work Permit (trước kẹt vì không tìm ra bản ghi; thử lại bằng Frida).
- [ ] Promises (như trên).
- [ ] Đổi tên (First / Last / Common / Shirt name).
- [ ] Injury: đổi loại chấn thương.
- [ ] Remove Int. Withdrawal, Tactical Understanding (preset).
- [ ] Career Plans: Long-term Job / Outlook, Role preferences.

### Stadium, City, Award
- [ ] Stadium: đổi Owner / Location.
- [ ] City: Local Region, Language, Weather, Nation.

### Công cụ
- [ ] Shortlists.
- [ ] Favorites.
- [ ] Players Progress.
- [ ] Mass Edit: thêm field staff và CLB còn thiếu.

## Chờ người dùng
- **Kit for specific season:** chỉ làm khi người dùng xác nhận.

## Kẹt (quay lại khi có hướng mới)
- **Nation Staff — Renew/Expire/+1 Year:** FMRTE không ghi sau ba thao tác trên Tuchel/Carsley đã nạp lại; cần nguồn khác hoặc mẫu staff có cả hợp đồng CLB và quốc gia. Không suy quy tắc.
- **Kits — Alternative Kit Number:** chưa xác định chắc record của item tương ứng.

## Bỏ (người dùng quyết định)
- Các thanh Tactical Understanding: không tiếp tục làm; không ảnh hưởng Tactical Familiarity hoặc preset.
- Possible Signings: FMRTE tự chấm điểm, không rõ cách tính, không cần thiết.

## Đã xong (không làm lại)
- Nation Agreements: người dùng xác nhận xong; backend đã có thêm EU/Non-Foreign rỗng và Region 23–29 hiện số. UI trong mã hiện tại vẫn chưa nối thêm EU rỗng; không làm lại theo chỉ đạo người dùng.
- Stadium Training Ground và Seat Colour: đã có; Seat Colour đã ghi qua UI và đọc lại RAM.
- Award Name/Short Name/UID: FMRTE không cho sửa, người dùng đã xác nhận.
- Tactical familiarity (player, preset đội): `player+0x190` byte 0–100 (đo bằng FM Studio: Saliba 35→100, Havertz 81→100); ô **Tactical familiarity** ở header trang CLB + Save chung, ghi cho cả đội; CLI `fam <player> [0–100]` / `fam club <id> <0–100>`.
- Nation Kits and Colours: dùng chung cấu trúc holder của CLB — `nation+0x108 → +0xB0` là holder, kits ở `holder+0x78` (21 item England), màu ở `+0xA0/+0xA8`; card KITS (Kit/Type/Style/FG/BG) + Save chung; đã đổi màu áo bằng UI, FMRTE hiện đúng.
- Transfer Preferences: đổi nation/continent của một dòng + thêm/xoá dòng (cả bảng Nations và Continents); target nation phải là **preference team** (`nation+0x108`), không phải nation object; vector rỗng thì cấp buffer mới.
- Background / Foreground Colour đội tuyển: `nation+0x108 → +0xB0 → +0xA0` (fg) / `+0xA8` (bg), BGRA dword; card COLOURS trong trang Nation (draft + Save chung); đã ghi qua CLI và qua UI, FMRTE hiện đúng.
- Allow Licensed Kits (national team): bit `0x08` của byte `team+0x2E`, **xanh = bit 0**; UI có checkbox trong TEAMS REPUTATION (draft + Save chung); đã bật/tắt thật và FMRTE hiện đúng.
- EuroCoefficients: `team+0x190` (nation+0x108 → team) = vector 10 float, season cũ → mới; card EUROCOEFFICIENTS trong trang Nation, khớp FMRTE Spain (19.714 … 22.093, cuối 0).
- Continents: bảng bỏ cột Scouting cho khớp FMRTE (chỉ Rating / Min / Max).
- Continents (Transfer Preferences): reader/writer `NationPreferences.ReadContinents/SetContinent` (team+0x108, 16 B/dòng) + bảng CONTINENTS trong trang Nation dùng chung Save; đã ghi Spain 12→13 và đọc lại, 1 test.
- Kits and Colours: màu và Item Style, 6 tab Home/Away/Third + GK (UI Save chung và Steam chưa thử).
- Club Background / Foreground Colour: `[club+0xB0]+0xA8` / `+0xA0` (Steam chưa thử).
- Affiliations: Duplicate / Remove, cập nhật cả hai CLB (ghi tuần tự, lỗi giữa chừng có thể lệch hai phía).
- Relationships, Club Vision: thêm / xoá dòng (buffer mới cấp bằng `VirtualAllocEx`; nếu game crash khi lưu, nghi chỗ này).
- Club Staff Renew / Expire (còn việc ở "Làm tiếp").
- Sponsors Total Value (có từ trước).
