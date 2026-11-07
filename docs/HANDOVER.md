# Bàn giao (4/11/2026)

File bàn giao duy nhất. Đọc file này, rồi `AGENTS.md`. **Việc cần làm nằm ở `docs/TODO.md`**, làm từ dòng `[ ]` đầu tiên.
Bằng chứng offset từng mục: `re/FM27-findings.md` (chỉ trên máy). **Không đọc cả file**: xem mục lục đầu file, grep tên mục.
Giữ file này ngắn: mỗi việc xong chỉ sửa dòng trạng thái tương ứng, không thêm nhật ký. Chi tiết đưa vào findings.

## 0. Backend xong nhưng app chưa có

Các tính năng đã làm xong ở Core/CLI (Club Vision target + bảng tên loại, Income Statement, Sponsors thêm/xoá,
Debts clear, Tactical max, offset Stadium) mà giao diện **chưa nối**: xem
[BACKEND-FEATURES-TO-WIRE.md](BACKEND-FEATURES-TO-WIRE.md). Đọc file đó trước khi sửa UI để không làm lại.

## 1. Trạng thái

- 7/11: hoàn thiện toàn diện giao diện và layout app Football Manager 27 Editor theo tiêu chuẩn AGENTS.md §6: hệ thống thiết kế UiKit tối giản (#0C0E14/#131620, accent tím #7C3AED, mật độ cao, không AI-slop/gradient/tagline rác), sticky entity banner & left rail chuẩn FMRTE cho Player/Club/Staff/Nation/Competition, chia lưới 2 cột cân đối cho các form/move, sơ đồ sân + rating pill trực quan, live vector kit preview (Club & Nation). Live UiAudit trên game thật (50.039 cầu thủ) đạt 100% (121/121 màn hình ở cả 1100×820 và 1020×760 đạt clipped=0, wrapped=0, overflowX=0). Build 0 lỗi, App 152/152 tests passed. Publish Desktop\FM27 Editor\App-PlayerLayout\Fm27Editor.exe.

- 7/11: `PlayerSearchIntersection.Find` ghép các stage AND, giữ trạng thái unavailable xuyên các điều kiện và loại khi một stage chắc chắn không khớp; `Cities.FindPlayersBornInCityDetailed` báo riêng City sai vtable/chưa đọc. Build 0 lỗi, Core 434/App 152 pass; mutation xoá trạng thái unavailable fail. Các backend cần adapter stage khi UI tích hợp.
- 7/11: `PlayerSnapshotSearch` lọc thêm Caps, Goals, PA−CA, asking price, wages, ethnicity, Non-EU Italy; khoảng dùng 64-bit. Test kết hợp có giá 3000000000 và caps/goals; build 0 lỗi, Core 433/App 152 pass.
- 7/11: `ContractSearchCriteria.Clauses` hỗ trợ nhiều điều khoản đồng thời, mỗi điều kiện có/không và min/max riêng, đọc vector một lần. Test fixture FMRTE types 32/39/38 và trường hợp điều kiện không khớp; mutation All→Any fail. Build 0 lỗi, Core 433/App 152 pass; mã clause 0/1/2/4 chưa được xác minh.
- 7/11: Core `PlayerSnapshotSearch.Find` kết hợp khoảng cho age/CA/PA/height/reputation, từng vị trí (all/any), attributes/personality, preferred-move mask và Wonderkid; báo snapshot thiếu. Test M=14/AM=16, passing 17, personality 16, moves và unknown; mutation AND→OR fail. Solution build 0 lỗi, Core 432/App 152 pass; chưa nối UI.
- 7/11: User định nghĩa Wonderkid là tuổi ≤20, PA >150. Core `WonderkidSearch.IsWonderkid` trả null nếu thiếu tuổi và PA >150; CLI `playerswonderkids`, preset mặc định đổi thành MaxAge 20/MinPa 151 (preset đã lưu riêng không tự đổi). Game Pass 276 người, 1 thiếu tuổi; build 0 lỗi, Core 431/App 152 pass, mutation PA 150 fail.
- 7/11: Core `PlayerRelationSearch` lọc trained-at club/nation và quan hệ theo loại, target, value; báo riêng dữ liệu không đọc được. CLI `playerstrained <club|nation> <ID>` đã viết nhưng chưa build trong workspace đang sửa Club Vision. Bản sao Core 429 test đạt, mutation so CLB fail; probe Game Pass Sheffield ID 708 trả 93 và 1314 thiếu, chưa so đầy đủ FMRTE.
- 7/11: `ContractSearchCriteria` lọc clause theo mã, có/không và khoảng giá trị; `playersclause <mã> <present|absent> [min max]`. Chỉ type 3 Yearly Wage Rise có bằng chứng FMRTE trực tiếp; vector lỗi được báo thiếu. Bản sao Core/CLI build, Core 427 test đạt, mutation mã fail; CLI type 3 value 7 trả 0, chưa đối chiếu FMRTE. Workspace hiện lỗi build `ClubVisionSnapshot` đang được DeepSeek sửa và `TemplatedControl` trong UI Argon; chưa chạy App test mới.
- 7/11: `ContractSearchCriteria` thêm min/max lương tuần và asking price, bao gồm hai biên, báo thiếu dữ liệu; CLI `playerscontract <type|any> <playing|any> [min-wage max-wage min-price max-price]` dùng `any` cho biên mở. Game Pass khoảng 40000–50000/1000000–10000000 trả 80, 1666 thiếu; 426 Core test pass, mutation biên lương fail đúng assertion. Solution vẫn lỗi `TemplatedControl` trong `SectionHost.cs` đang sửa UI; App test chưa chạy lại.
- 7/11: Core `PlayerNationalitySearch.Find` + CLI `playersnation <nation-id> [--include-other]` lọc quốc tịch chính và tùy chọn phụ; báo riêng dữ liệu không đọc được. Game Pass Portugal có 3298 kết quả, 1312 thiếu; chưa đối chiếu danh sách FMRTE, chưa nối UI. Core 425 test đạt, mutation đảo so sánh fail; build solution hiện bị lỗi `TemplatedControl` trong `SectionHost.cs` đang được Argon sửa, chưa chạy lại test App.
- 7/11: Core `PlayerContinentSearch.Find` + CLI `playerscontinent <tên châu lục>` lọc theo châu lục của **quốc tịch chính**, báo dữ liệu thiếu. Mẫu Nation England/Brazil/Vietnam đối chiếu FMRTE; mutation đảo điều kiện làm test fail. Game Pass Europe 25.438 kết quả, 140 thiếu; chưa đối chiếu bộ lọc FMRTE và chưa nối UI vì Argon sửa giao diện. Build 0 lỗi, Core 424/App 152 test đạt.
- 7/11: Core `ContractSearch.Find(players, criteria, ct)` và CLI `playerscontract <type|any> <playing-time|any>` lọc type/playing time, trả riêng snapshot hợp đồng stale/không đủ field. Test byte mẫu type + playing time, mutation đảo so sánh fail; Game Pass `FullTime Star` có 3503 kết quả, chưa đối chiếu danh sách FMRTE. Build 0 lỗi, Core 423/App 152 test đạt; UI chưa nối vì Argon sửa giao diện.
- 7/11: Core `Cities.FindPlayersBornInCity(players, cityId, ct)` + CLI `playersborn <city-id>` đã lọc theo ID; Maia 855 trên Game Pass trả 17 người gồm Bruno 43124203, mẫu byte FMRTE kiểm tra đúng/sai ID và vtable; mutation đảo phép so sánh làm test fail. Build 0 lỗi, Core 422/App 152 test đạt. Chưa nối UI: Argon đang sửa UX/UI; xem TODO.
- 7/11: kiểm toán và sửa UX/UI toàn bộ 8 module, sub-tab và dialog trên save Game Pass thật ở `1100×820` và `1020×760` (`119/119` màn `clipped=0, wrapped=0, overflowX=0`): bỏ `Viewbox`/`Width=970` trên Player, sửa lịch `Born`, gắn lại thanh tìm/lọc bị thiếu và khớp `ID` cho mọi danh sách, đồng bộ double-click/`Enter`, cuộn ngang tab thực thể, sửa `Home`/`UnifiedSearch`/top `Save`/`Mass`/`Freezer`, sửa cắt cột `DataGrid` + `Club Squads` double-click, nâng cấp `ColourPickerWindow`, fallback nạp `730` giải từ `Player.Division` khi `Competitions.LoadAll()` lỗi trên Game Pass. Build 0 lỗi, Core 421 + App 152 test đạt (mutation 3/3 đạt); một số panel con vẫn dùng `Save`/`Apply` riêng và ghi nhiều bước không atomic. Publish `Desktop\FM27 Editor\App-PlayerLayout`.
- 7/11: graphics mặc định rỗng giờ kích hoạt hộp chọn thay vì coi là pack; file Mbappé ID 85139014 và logo Man City ID 679 có trong `D:\Football Manager 27\graphics`. Cờ quốc gia trong header Player có chỗ hiển thị khi pack có mapping; chưa kiểm UI và ảnh mặc định game chưa hỗ trợ. Handover review UI cho Argon: `docs/ARGON-UX-REVIEW.md`.
- 7/11: Graphics tự chọn mặc định Documents hoặc hỏi thư mục và ghi nhớ; nút Graphics cho đổi thư mục. Đọc `config.xml` trên nền, nối ảnh person/club/comp/nation vào header theo ID; có một test parser, App 150 test đạt. Đã thấy file thật cho Bruno/Man Utd/Premier League; chưa kiểm giao diện hoặc ảnh đóng gói trong game, cờ chỉ có nếu pack cung cấp mapping flag. Bản Desktop `FM27 Editor/App-PlayerLayout`.
- 7/11: tách thanh mục Player khỏi Viewbox/vùng cuộn; header, thanh mục và hàng thể lực độc lập nội dung tab. Build 0 lỗi, App 149 test đạt; chưa kiểm tương tác GUI. Publish `Desktop\FM27 Editor\App-PlayerLayout`.
- 7/11: thanh mục Player căn theo chiều cao danh sách thay vì kéo giãn theo nội dung từng tab; build 0 lỗi, App 149 test đạt, chưa kiểm GUI. Publish `Desktop\FM27 Editor\App-PlayerLayout`.
- 7/11: đặt khoảng cách chung 12px giữa thanh mục và nội dung mọi tab Player; Career Plans chọn đúng đối tượng từ catalogue và nới ô hiển thị tên. Build 0 lỗi, App 149 test đạt ngoài sandbox; GUI chưa kiểm. Đã cập nhật bản Desktop `FM27 Editor/App-PlayerLayout` sau khi được cấp quyền.
- 7/11: chuyển Morale/Asking Price/Caps/Goals/Youth Caps/Goals vào Personal Details; Condition/Sharpness/Jadedness đặt ngoài vùng cuộn của tab Player; căn giữa ô chỉ số và cho Planned Retirement chiếm một hàng. Build 0 lỗi, Core 421/App 149 test đạt từ bản sao test ngoài workspace; đã xem GUI Mbappé và xác nhận ba ô còn hiện khi cuộn, Planned Retirement đọc đủ. Bản chạy `Desktop\FM27 Editor\App-PlayerLayout`.
- 7/11: ô Rating20 đặt MinWidth=0, MaxWidth=30 để không chiếm chỗ của nhãn; build 0 lỗi, App 149 test đạt, chưa xác nhận kích thước bằng GUI. Publish lại `Desktop\FM27 Editor\App-PlayerLayout`.
- 7/11: ô Rating20 trong Player thu gọn, nhập ngoài 1–20 phục hồi giá trị trước khi sửa lúc rời ô; căn đều ô Personal Details. Build 0 lỗi, App 149 test đạt; chưa kiểm thao tác nhập/lưu trên GUI. Publish `Desktop\FM27 Editor\App-PlayerLayout`.
- 7/11: bỏ cuộn ngang Player, thu Personal Details và các cột; nội dung tự co theo chiều rộng bằng Viewbox. Build 0 lỗi, App 149 test đạt; chưa kiểm GUI, cần kiểm độ đọc và giá trị dài trong combobox tại cửa sổ nhỏ. Publish `Desktop\FM27 Editor\App-PlayerLayout`.
- 7/11: thu nhỏ font và control dùng chung, chỉnh một số cột Player và căn giữa badge tuổi. Build 0 lỗi, App 149 test đạt; chưa kiểm UI. Review dở: Club/contract/advanced search còn nhãn Wrap và cột cố định, phải căn lại để chữ một dòng mà không bị cắt. Publish `Desktop\FM27 Editor\App-PlayerLayout`.
- 7/11: nới các cột thuộc tính lên 200 và Tools lên 205, đồng bộ chiều cao tiêu đề, tăng cỡ/màu CA/PA. Build 0 lỗi, App 149 test đạt; chưa kiểm lại giao diện thực tế. Publish `Desktop\FM27 Editor\App-PlayerLayout`.
- 7/11: Player Information dùng hàng ngang có cuộn ngang khi hẹp; thu gọn Personal Details/Tools, Personality dưới Mental và Hidden dưới Physical. Enter bỏ focus các NumericUpDown và TextBox số canh phải; TextBox số khác chưa bao phủ hết. Build 0 lỗi, App 149 test đạt; chưa kiểm bố cục/Enter qua UI. Publish `Desktop\FM27 Editor\App-PlayerLayout`.
- 7/11: CA/PA nằm trực tiếp ở góc phải thanh tên player, Freeze trong cột Tools; Born, Height và 3 Reputation ở Personal Details, Born dùng lịch `dd-MM-yyyy`, Technical/GK nằm trên tiêu đề bảng, bỏ Discard đầu trang. Build 0 lỗi, 149 test App đạt; chưa kiểm GUI/lưu ngày qua UI. Publish lại `Desktop\FM27 Editor\App-PlayerLayout`, không tạo app mới.
- 7/11: Player Information dùng bố cục tự xuống dòng, bỏ nút Save riêng và nối Save trên thanh chính; liên kết CLB/giải/quốc gia mở tab tương ứng, tái sử dụng tab cầu thủ. Đã bỏ các `CharacterEllipsis` khai báo trong app; chưa xác nhận toàn bộ trường hợp bị cắt hoặc UI ở màn hình nhỏ. Save chung mới định tuyến Player và Club; các mục khác vẫn cần hợp nhất, giữ nút Save riêng hiện có. Build 0 lỗi, Core 421/App 149 test đạt; chưa thử GUI theo yêu cầu user. Bản `Desktop\FM27 Editor\App-PlayerLayout`.
- 7/11: đã bỏ thanh sort và các nút CSV/JSON/Mass Edit trùng trong danh sách; thêm export/import attributes + personality trên Information của player (nhập vào nháp). Lọc có ngưỡng riêng mỗi vị trí, ranges cơ bản, Personality, Ethnicity, Non-EU Italy và Preferred Moves. Build 0 lỗi; Core 421/App 149 test đạt; mutation ngưỡng vị trí và file lỗi bị test bắt. Bản `Desktop\FM27 Editor\App-Advanced`; mục còn thiếu và bước kiểm UI/import thật ở TODO.
- 7/11: danh sách Players thêm Position và Asking Price; user quy định chỉ vị trí ≥16. RAM/FMRTE khớp các mẫu Bruno, Zapelli, Guimarães, Tabata, Varela; UI Bruno hiện `M/AM C` và `46,876,060`. Build 0 lỗi, Core 421 + App 146 test đạt; test vị trí bắt đột biến ngưỡng 15 trên bản sao. Chi tiết/xung đột mẫu Xadas ở findings.
- 7/11: sửa luồng Players ↔ Clubs: bảng CLB có chiều cao hữu hạn, không tự mở CLB đầu, giữ thanh tab trống, tránh nạp lại khi chuyển; Clubs về danh sách đầy đủ. Đã thử UI thật với Man City, chuyển sang Players và mở lại tab; App 146/146 test đạt.
- 7/11: app nay chờ bấm Load Game mới kết nối; bỏ lần đọc dữ liệu thừa sau kết nối thất bại. Bản chạy mới tại `C:\Users\Duong\Desktop\FM27 Editor\App-Codex`; CLI cùng build ngoài workspace kết nối Game Pass và đọc 50.039 cầu thủ, chưa thử nút UI. Chạy exe/testhost trực tiếp trong workspace vẫn bị hạn chế truy cập process; không có PID cố định trong app.
- User yêu cầu bỏ thanh Tactical Familiarity, dùng nút Tactical Understanding cho cầu thủ được chọn hoặc cả đội; cần kiểm tra trên game sau thay đổi giao diện.
- Ưu tiên sau thay đổi của DeepSeek: sửa lỗi đã phát hiện trước khi thêm tính năng; Income Statement thiếu kiểm vtable CLB và snapshot con trỏ tài chính, cần phối hợp phần UI đang thay đổi. Xem TODO.
- User đã cho phép sửa UI: ưu tiên cấu trúc tab, chọn nhiều cầu thủ, màu sắc và các nút squad; xem TODO để tiếp tục kiểm tra những phần chưa xác nhận.
- UI đang sửa: tab theo từng đối tượng; danh sách CLB nạp chỉ số nền; Nation/Club có menu trái; squad chọn nhiều; Tec/GK chung cột; picker màu Hex/RGB/HSV/CMYK. Build Release 0 lỗi; Core 420 và App 146 test đạt. Chưa xác nhận thao tác trên cửa sổ thật; shell không thấy `fm` trong phiên kiểm tra.
- Nút Tactical Understanding hiện ghi guarded byte familiarity cầu thủ `+0x190`; chưa chứng minh tương đương đầy đủ preset FMRTE. Destroy gồm nhiều lần ghi có guard và có thể dừng giữa chừng; chưa thử RAM sau thay đổi.

- Nation Staff: backend `NationStaff.ReadDetails(nation)` và CLI `nationstaff <nation>` đã publish ở `Desktop\FM27 Editor\Tools`; đọc danh sách, quốc tịch, tuổi, CA/PA, chức vụ và đội phụ trách. Game Pass England 22 người khớp FMRTE, Brazil đọc 15; còn thao tác hàng loạt, UI và Steam. Xem findings.

- Nation Squads: backend/CLI đọc thêm Condition và Asking Price, hai mẫu England khớp ảnh FMRTE. Xem TODO cho các trường và thao tác còn thiếu.

- Nation UI: 6 tab Information / National Teams / Squads / Kits and Colours / Relationships / Transfer Preferences, Save/Discard cố định trên hàng tab; đã build/test và publish, chưa thử UI theo yêu cầu.

- Nation root: tự tìm vector lân cận khi root profile lệch; CLI đọc 251 Nation trên Game Pass phiên 6/11. Region có 28 object và writer guarded; tên mã 1–22 lấy trực tiếp enum FMRTE, riêng 11 không có object trong phiên này, 23–29 hiện số. User xác nhận Agreements xong và yêu cầu bỏ qua; backend thêm EU/Non-Foreign rỗng đã ghi/đọc, nhưng mã UI hiện tại chưa nối EU rỗng. World đã có, CLI ghi/đọc Spain. Xem TODO/findings.

- Club Kits: đã có 6 tab, nháp màu/Item Style và Save chung; reader/writer guarded đã ghi/đọc RAM Game Pass trên nhiều CLB. Danh sách nay hiện riêng các item trùng tên; chưa kiểm UI thay đổi này theo yêu cầu người dùng. Steam, Alternative Kit Number và các thao tác thêm/xoá chưa xác nhận. Chi tiết và phạm vi còn lại xem TODO và findings.
- Club Background / Foreground Colour: Game Pass `[club+0xB0]+0xA8/+0xA0`, đã nối profile/UI sẵn có và guarded writer ghi/đọc thật Bayern; Steam chưa thử. Xem TODO và findings.
- Affiliations: CLI Duplicate/Remove kiểm và sửa vector hai CLB; đã đọc lại cả hai trên Game Pass. UI có Remove dạng nháp qua Save chung, chưa thử UI theo chỉ đạo. Chưa có tạo quan hệ mới hoặc Add UI; lỗi giữa các lần ghi có thể để hai phía lệch. Xem TODO/findings.

- **Bàn giao DeepSeek 5/11:** Champions League có 3 stage; reader nay chọn stage league hợp lệ duy nhất, CLI đọc 36 đội/36 mức thưởng khớp FMRTE. Người dùng sửa Win 1803968 và Pos 1 10222481 trong FMRTE, Frida + RAM xác nhận. Build 0 lỗi; Core 372 + App 146 pass; mutation stage mơ hồ bị test bắt. Chưa thử UI hoặc ghi bằng editor trên Champions League. Rules Champions League chưa đọc được; FMRTE hiện Maximum 25, Homegrown Nation 8, Club 4, Goalkeepers 2. Xem findings; hiện tạm dừng theo yêu cầu người dùng.

- Competition 5/11: FMRTE đã nạp game; người dùng cho phép bấm Load Game khi cần. Computer-use và Frida quyền thường hoạt động. Prize Money đã đọc/ghi Game Pass; bảng thưởng theo thứ hạng đã đối chiếu và thử UI, xem findings. Tìm kiếm editor nhận tên hoặc ID.
- Rules: Maximum squad size có reader/writer, CLI và UI; Non-EU và Outfield có UI/CLI, người dùng lưu đồng thời Spanish 5/25, CLI đọc lại. Homegrown UI đọc theo mã 0x26/0x27/0x92/0xFD thay vì ID giải; dùng chung reader/writer guarded; UI Bundesliga ghi 0x26=9 và 0x27=13, CLI đọc lại. League One FMRTE Homegrown at club or in nation 8→9, rồi UI editor 9→10; CLI mã 0xFD đọc 10 với ba bản khớp; League Two 0xFD=8 qua CLI. Frida ready nhưng không bắt được write League One; xem findings. CLI/FMRTE xác nhận hai giải Đức, UI lưu đồng thời hai field Bundesliga và CLI đọc lại. Chưa thử Steam hoặc giải khác. Xem findings.
- VAR: `competition+0xD0` trỏ object chứa ngày `+0x10`, loại `+0x14`, TV Matches Only `+0x15`, Enable custom VAR `+0x16`; đã đối chiếu PL và Bundesliga, UI/CLI có writer guarded. Chưa tạo object null, chưa thử Steam/Continue. Mỗi đối tượng chỉ một tab FMRTE, giữ tab khi còn làm việc và điều hướng trong tab đó.
- Standings: Core/CLI/UI đọc và sửa Games Played, W, D, Losses, GS, GA, Points; bảng theo thứ tự cột FMRTE. Game Pass CLI ghi/đọc các cột trên Man Utd và Arsenal; UI lưu hai cột và CLI đọc lại. Chưa thử giải khác, Steam hoặc Continue. FMRTE Refresh hiện lỗi VAR `InvalidCastException`; xem findings.
- Build 0 lỗi; Core 370 + App 146 test pass từ bản sao TEMP (5/11). Chạy trực tiếp trong workspace bị testhost từ chối quyền mở tiến trình.
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
| Player | Captain: **đã thử UI 5/11** (Set captain → CLI xác nhận, Undo → trả đúng người cũ; đã sửa lỗi nút Undo không bật do thiếu RenderEditor); Hầu hết tab (`docs/player-tabs.md`); **Contract đã thử UI 5/11**: đổi Wages 14000 → 15000 rồi **Save contract**, CLI xác nhận £15000, trả lại 14000 (lưu ý: nút Save changes ở góc phải không lưu contract); **Bonuses & Clauses đã thử UI 5/11**: Add "Yearly Wage Rise" 7% → CLI thấy `term type 3 value 7`, Add "Optional Contract Extension By Club" Years 1 → `type 22 param 1`, **Add ghi thẳng RAM**, đã xoá lại bằng CLI mới `contractrm <tên> <index>` | Promises, Work Permit, Scout Information, đổi tên, preset |
| Staff | Information, Contract, Career Plans, Relationships, Languages, Knowledge, Formations, Tendencies; **City: đã sửa Attraction qua UI 5/11** (Manchester 18 → 19, Save, RAM city+0x54 = 0x13, trả lại 18); **ACTIONS 'Trained at club': đã chạy 5/11** (thêm dòng `trained at club Sheffield United period 1`, xác nhận bằng `statuses`; object cầu thủ không đổi byte nào vì là vector relations); **đã bổ sung Remove**: `RelationshipWriter.RemoveTrainedRow` + `RemoveTrainedAtClub/AtNation` + CLI `untrained <tên>` — đã dùng gỡ dòng dư của Burrows và kiểm bằng `statuses`; **Move to club: bảng xem trước đã thử UI 5/11** (Hull 665, đội 1 38/38 → hiện cảnh báo cấp phát squad ngoài allocator; số before→after và Wage bill hiển thị đúng; **chưa Confirm** nên không ghi); **Swap National Call Up: đã thử UI 5/11** (Litmanen Finland ↔ Ashfaq Maldives, CLI `callups` xác nhận đổi chỗ, swap lại hoàn nguyên; cùng đội thì guard báo 'Both players are already in that squad'); **Future Transfer: đã sửa cả Transfer Date và Value qua UI 5/11** (Date 07-01↔07-02, Value 429.515↔430.000, CLI `future` xác nhận từng bước rồi trả lại); **Future Transfer: đã sửa Transfer Date qua UI 5/11** (Bellaarouch: 2027-07-01 → 2027-07-02, CLI `future` xác nhận `on 7/2/2027`, trả lại 7/1/2027); **Future Transfer: UI khớp CLI 5/11** (Bellaarouch: Braga → Dinamo 1948 ngày 2027-07-01, fee 429.515, wage 2.965, 2027-07-01..2030-06-30; tab chỉ hiện khi có future transfer); **Transfer Proposals: UI khớp CLI 5/11** (Bellaarouch: Braga → Dinamo 1948, fee 429.515, Confirmed — app và `proposalsurvey` trùng khít); **Clone Center: đã thử UI 5/11** (Bruno → Burrows, chỉ Technical: Cro 14→18, CA/PA/tuổi không đổi; hoàn nguyên byte-exact bằng snapshot + CLI `poke`); **Preferred Moves: đã sửa trait qua UI 5/11** (tick 'Runs With Ball Down Left' + Save của panel → RAM bitfield `40`→`41`, bit 0 = move đó; bỏ tick trả về `40`); **Player: quét 5/11 cả 13 tab (Information/Attributes/Comparison/Positions/Personality/Career Plans/Clone Center/Move to club/Injury/Unavailability/Preferred Moves/Relationships/Languages) — tab nào cũng render**; **Award: đã sửa Reputation qua UI 5/11** (World Footballer of the Year 20 → 19, CLI `awards` xác nhận, trả lại 20); **Competition: đã sửa Trim Colour qua UI 5/11** (Premier League #FF000000 → #FF123456, CLI `compinfo` xác nhận, trả lại bằng `compset`); **Stadium: đã sửa Seat Colour qua UI 5/11** (Old Trafford #FFE10000 → #FF00BEEF, Save changes, CLI `stadiumcolour` xác nhận rồi trả lại); **Nation: đã sửa Gain Nationality Type qua UI 5/11** (Continuous → Accumulative, Save changes, CLI `nation` đọc 2, trả lại 1); **smoke test 5/11**: 5 module phụ (Nations/Stadiums/Cities/Competitions/Awards) nạp sạch, Awards 263 giải, Nations 251 nước; đã sửa status bar giữ câu cũ khi đổi module | Training Rating (chưa xác nhận có trong FMRTE 27) |
| Club | Nhiều field, Squads, Transfer Center (đọc, sửa phí), Relationships, Staff, Debt, Affiliations, Vision (sửa Importance — **đã thử UI 5/11** trên Sheffield United: 5 → 7 → Apply, CLI `clublists` đọc 7, trả lại 5); **Sponsors: sửa annual value + 2 ngày, cả CLI và UI** (CLI `clubsponsor <id> <index> total|dates <value> [--write]`; UI Finances → Sponsors and Other có ô Total Value / Start / End + Apply, đã thử trên app: 900,000,123 và 2025-07-02..2035-07-01, CLI đọc lại đúng rồi khôi phục; FMRTE xác nhận 901,234,567 / 8-1-2025) | Kits (đã chốt item 0x28 + 5 màu BGRA; **5/11 tìm được chain** bằng `memscan climb`: item 0x2723146E758 <- 0x2723BD96F68 (vector<Item*>) <- 0x27230BE28B8 (kit set: màu + vectors) <- 0x272057A2650; còn thiếu field khoá club: club không trỏ tới kit set (climb 0 goal), bảng `0x27230BE2xxx` là các record ~0xB0 byte chưa thấy club id; đã so kit CLB thứ hai (Sheffield United): item ở 0x27231468xxx, màu = B,G,R,A tại item+0x8, nhiều CLB chung một allocation; màu kit khôi phục theo Background Colour của CLB (#FF9E2530); **club Background Colour đã tìm được chỗ lưu**: `0x27230BE19B8` = dword BGRA `30 25 9E FF` (#FF9E2530), cùng vùng `0x27230BExxxx` với kit set; đã thử nối: club/team **không** trỏ tới object graphics (`0x272057A0C70` giữ màu, `0x272057A25A0` giữ kit), bảng `0x27102B89D60` cũng **không** có con trỏ nào tham chiếu (0 hit) ⇒ cần disassembly hoặc mẫu đối chứng thứ ba; quan sát mới: cụm màu kit lặp với bước **0x50** chứ không phải 0x28 — cần đo lại kích thước bản ghi kit), Tactical Understanding (**đã giải mã 5/11**: khối ở 0x2723F4F5B00, nhóm 3 dword cách 0x10 từ +0xC0; `dword & 0x3FFF` = giá trị×100, cờ ở bit 14+; đã map Build Up/Chance Creation/Passing Style/Pressing Trap/Line of Engagement/Pressing Intensity, Position-Role **chưa map** (+0x120 là mảng con trỏ); đã có `TacticalBlock` + CLI `tactics <0xđịa-chỉ>` đọc đúng 6 slider (30/76/91/13/61/57 khớp FMRTE) và test `TacticalBlockTests`; còn thiếu đường team→khối: club/team **không** có con trỏ tới khối; block của U21 (`0x2723C1E5580`) lại có bố cục khác block đội 1 nên mỗi đội có thể là allocation riêng — `TacticalBlock` hiện chỉ đúng cho block đã đo, xem findings), Tactics, Possible Signings, Sponsors add/remove (`docs/club-tabs.md`) |
| Nation | Phần lớn field (`docs/nation-tabs.md`) | Agreements, EU, Non-Foreign, Continents, EuroCoefficients, Licensed Kits, màu |
| Stadium | 25 field, 7 cờ, 8 khán đài, **Seat Colour sửa được** (CLI `stadiumcolour`, UI textbox + swatch), Is Training Ground đã có | Owner/Location |
| Cities | Inhabitants, Attraction, toạ độ, Altitude | Region, Language, Weather, Nation |
| Competitions | **Xong phần FMRTE cho phép sửa** (Game Pass): Reputation, Substitution Rules, 4 màu, 2 ngày Winter Ball; VAR; Standings; Prize Money + Classification; Rules theo mã luật (Maximum cả fallback `group+0xF0`, Homegrown, Goalkeepers `0x57`, Non-EU, Outfield); reader chọn stage league hợp lệ duy nhất (CLI đọc CL 36 đội); CL một bản; "Also Applies To" hiện trong UI (so common+index) và trong CLI; UI CL đã ghi 26→27, FMRTE xác nhận (`docs/competition-tabs.md`) | Tạo object VAR khi null (cần cấp phát RAM), Stages (không làm — FMRTE cảnh báo hỏng save), Steam chưa thử, `0xD4` chưa rõ nhưng FMRTE không hiện |
| Awards | Reputation, Winner reputation | Tên/Short Name/UID: FMRTE không cho sửa (5/11, người dùng xác nhận) |
| Công cụ | Lịch sử + Undo; Export CSV/JSON; Freezer player/CLB; lọc chỉ số Min–Max, staff theo tên/CLB/chức vụ; Mass Edit player/staff nhiều field và lương, field số nguyên CLB; preset Heal/Fitness/Morale/Lift bans/Remove Unhappiness/Max attributes/Prevent retirement cả đội có preview/Undo; Captain/vice captain cho cầu thủ (CLI Game Pass) | Mở rộng Mass Edit field khác/staff/club; các preset còn lại, Shortlists, Favorites, Players Progress |

FMRTE 27 không có trang Teams, Officials, Media Sources. Không làm Swap Player / Swap Staff (Move to club thay thế). FMRTE 27 có Move riêng, xem §5 mục 6.
Competition Name và Unique ID không thể chỉnh (người dùng xác nhận); không đưa vào danh sách tính năng cần sửa. Staff Promises: bỏ qua (người dùng xác nhận). Kiểm kê Staff/Stadium/City/Competition/Award chưa đầy đủ, nên cột "còn thiếu" có thể sót.

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
12. Workflow mới: khi cần thao tác chỉnh trong FMRTE/game để thu thập bằng chứng, nói rõ giải, field và giá trị cần chỉnh để người dùng tự thao tác; chuẩn bị Frida/đọc RAM trước khi yêu cầu.

## 3. Cách chạy

```bash
dotnet build Fm27Editor.slnx -c Release
```

Testhost và exe trong workspace không thấy `fm.exe`. Chạy test từ `%TEMP%`:

```bash
cd "$TEMP"; rm -rf fm27tCore fm27tApp; cp -r "/c/Users/Duong/Desktop/FMRTE clone 27/tests/Fm27Editor.Core.Tests/bin/Release/net10.0" fm27tCore; cp -r "/c/Users/Duong/Desktop/FMRTE clone 27/tests/Fm27Editor.App.Tests/bin/Release/net10.0" fm27tApp; dotnet vstest fm27tCore/Fm27Editor.Core.Tests.dll fm27tApp/Fm27Editor.App.Tests.dll | tail -5
```

- CLI và app: copy `bin/Release/net10.0` ra `%TEMP%`, chạy từ đó. Lấy PID bằng `Get-Process fm`, đặt `FM_PID`.
- Bản chạy cho người dùng (5/11): `dotnet publish` ra **`C:\Users\Duong\Desktop\FM27 Editor\App`** và `...\Tools` — chạy từ ngoài workspace thì nhận game; chạy trực tiếp trong `src\...\bin\Release` bị sandbox workspace nên báo "Football Manager (fm.exe) is not running".
- Đột biến (chỉ khi cần): offset sửa JSON trong bản copy `%TEMP%\fm27tCore\offsets\`; code copy repo sang `%TEMP%\mutsrc`.
- Cắt output: chỉ lấy dòng tổng kết của test, dump RAM chỉ vùng cần xem.
- FMRTE 27: thư mục cài trên `E:\` (bản build 5 sau update). Mở trong cmd không admin: `set __COMPAT_LAYER=RunAsInvoker`.
  Tên hiển thị trong `Assets\Languages\english.json` (chỉ tên, không có mã).
- Computer use: `re/tools/focus.ps1 <FMRTE|Fm27Editor|fm>` đưa cửa sổ lên. FMRTE giữ dữ liệu cũ trong tab đang mở: mở lại qua ô
  tìm. Hộp lỗi "Changes cannot be saved" là modal chặn mọi thao tác, bấm Ok trước.

### Oracle

0. **Frida hook FMRTE (dùng trước tiên cho field nằm sâu).** Hook `ReadProcessMemory` và `WriteProcessMemory` của tiến trình
   FMRTE, ghi log địa chỉ, size, dữ liệu theo thứ tự.
   - Mở một tab FMRTE (không cần sửa): log đọc cho mọi field của tab. Khớp giá trị log với màn hình để biết field nào ở đâu;
     lần ngược các lần đọc trước đó (địa chỉ lần sau = con trỏ trả về lần trước) tới object đã biết hoặc RVA tĩnh trong
     `game_plugin.dll` để ra chuỗi con trỏ.
   - Sửa + Save: log ghi cho biết mọi chỗ FMRTE ghi (có field FMRTE ghi nhiều bản sao, ví dụ Competition Rules 3 bảng);
     writer của ta ghi đúng các chỗ đó.
   - Kiểm chuỗi trên 2 đối tượng khác nhau. Người dùng (hoặc computer use) bấm tab trong FMRTE; agent đọc log.
   - Đã chạy được 5/11 (DeepSeek, Competition Rules/VAR); FMRTE không chặn hook.
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

# Người tiếp nhận phần Nation: đọc thêm [HANDOVER-TERRA.md](HANDOVER-TERRA.md) (trạng thái từng mục, offset đã đo, việc còn lại).

## 5. Việc còn lại (theo thứ tự)

**Agent bắt đầu từ mục đầu tiên chưa xong; không làm lại việc đã xong, không đi thử UI tính năng cũ.**
Thử bằng cách **ghi → đọc lại RAM/CLI → đi tiếp, KHÔNG hoàn nguyên** (§2.2).
Không disassembly `game_plugin.dll`: field sâu thì hook Frida `re/tools/fmrte-rpm-trace.py`, mở tab FMRTE, đọc log `ReadProcessMemory`, lần ngược chuỗi con trỏ (như đã làm ở Competitions).

### 5.0a Trạng thái RE của ba mục đang chặn (cập nhật 5/11 — đọc trước khi thử lại)

| Mục | Đã biết chắc | Đã loại trừ | Bước tiếp theo |
|---|---|---|---|
| **Kits** | Item 0x28/0x50 byte (màu BGRA ở item+0x8), item Man Utd `0x2723146Exxx`, Sheffield `0x27231468xxx`; vector item `0x2723BD96F68`; kit-set record `0x27230BE28B8` (màu + vector) trong bảng record ~0xB0 ở `0x27230BE2xxx`; `memscan climb` cho chain item ← vector ← kit-set ← `0x272057A2650` | Club object **không** trỏ tới kit set (`climb` 0 goal); bảng `0x27102B89D60` **không** có con trỏ tĩnh nào (`scanval` 0 hit); 8 KB quanh `0x27230BE2000` không chứa con trỏ tới vùng item | Frida: mở tab Kits của 2 CLB trong FMRTE, đọc log `ReadProcessMemory`, lần ngược từ màu/item ra object CLB |
| **Club colours** (Background/Foreground) | Màu là **dword BGRA** trong object graphics: Sheffield `#FF9E2530` ở `0x27230BE19B8`; object đó là `0x272057A0C70`; chain: màu ← `0x272057A0C70` ← `0x27102B89D68` | Club object (16 KB) **0** con trỏ vào vùng `0x272057A0000..0x272057B0000`; team object `0x2722E9FBC08` cũng không; `scanval` cho dword màu = 0 hit (vì object nằm ngoài vùng quét hoặc bị đổi tạm) | Frida: hook `ReadProcessMemory` khi mở tab Information, lần ngược từ dword màu ra object CLB |
| **Tactical Understanding** | Mã hoá `dword & 0x3FFF = value×100`, cờ ở bit 14+; nhóm 3 dword cách 0x10 từ `+0xC0`; đã map Build Up/Chance Creation/Passing Style/Pressing Trap/Line of Engagement/Pressing Intensity; `TacticalBlock` + CLI `tactics <0xđịa-chỉ>` đọc đúng 6 slider | `+0x120` **không** phải Position/Role (là mảng con trỏ); khối của U21 có bố cục khác khối đội 1; club/team không trỏ tới khối | Frida: mở tab Tactics trong FMRTE, lần ngược từ 6 dword slider ra object team |

**Không dùng disassembly `game_plugin.dll`.** Dùng Frida hook FMRTE (§3 Oracle mục 0) để lần chuỗi con trỏ.

**Công cụ hỗ trợ đã có:** `memscan climb|refs|scanval|dump`, Frida logger (`%TEMP%\fmw3.log`, job ghi mọi write của FMRTE), `healthprofile`/`healthodd` (kiểm profile sau khi game update), `tactics` (đọc khối chiến thuật theo địa chỉ).

1. **Cập nhật editor theo bản game trong một lần chạy (`update-profile`) — VIỆC ĐANG LÀM (người dùng chốt 5/11: làm xong mục này trước).** Đã dựng sẵn trong Core: `ModuleRtti.cs` (chỉ mục MSVC RTTI tên lớp → vtable), `RootFinder.cs` (tìm root theo công thức + tìm build string), `ProfileHealth.cs` (PASS/FAIL từng module), `ProfileUpdater.cs` (`--label` và rebuild từ tên/công thức). Còn thiếu: nối lệnh CLI, thêm mục `rtti`/`roots` vào 2 profile, và kiểm bằng cách xoá RVA rồi dựng lại. **Chưa có bản Steam để kiểm.** Ta tự build bản cập nhật rồi phát hành cho người
   dùng; mục tiêu là agent làm xong trong ~15 phút, không mất nhiều phiên.
   - Xây một lần lệnh `Fm27Editor.Tools.exe update-profile`:
     1. đọc dấu vân tay build (`size_of_image` + `link_timestamp` của `game_plugin.dll`) và bản cài (Steam/Game Pass theo
        đường dẫn `fm.exe`);
     2. lấy profile gần nhất làm gốc, giữ nguyên offset trong object;
     3. tìm lại mọi vtable theo tên lớp RTTI (profile lưu thêm tên lớp cạnh mỗi RVA; gộp `re/tools/gamepass-rva-map.ps1` vào C#);
     4. tìm lại mọi root (mảng player/staff/club, nation, stadium, city, competition, award, offers, ngày game) bằng công thức
        tìm ghi sẵn cho từng root, không dùng địa chỉ cứng;
     5. health check từng module (đọc vài đối tượng, kiểm điều offset sai không giả được, như FMLibre `health.rs`);
     6. ghi `offsets/<build>.json` và in bảng module ĐẠT / LỖI.
   - Tiêu chí xong: xoá hết RVA trong bản sao của profile hiện tại, chạy lệnh ra lại profile giống bản gốc, mọi module ĐẠT,
     trên cả Steam và Game Pass. Cố tình làm sai một offset trong object thì đúng module đó báo LỖI.

**Danh sách việc: `docs/TODO.md`. Agent làm theo file đó, không lấy việc từ HANDOVER; phần dưới chỉ là ghi chú tham khảo.**

2. **Club bằng Frida** (oracle §3 mục 0): Kits and Colours, Tactics, Possible Signings, Sponsors. Bảng 5.0a ở trên là manh mối cũ.
3. **Nation bằng Frida:** Continents, EuroCoefficients, Agreements/EU/Non-Foreign, Licensed Kits, màu.

_Đã xong, không làm lại:_ Export, Freezer, Mass Edit, preset cả đội, Release, Captain, Staff Move, Competitions, thử UI các module (5/11). Health check: `healthprofile`, `healthplayers`, `healthclubs` đã có.

**Work Permit và Promises: làm lại được bằng Frida** (mở tab tương ứng trong FMRTE, đọc log `ReadProcessMemory`, lần chuỗi con trỏ).

3. **Stadium, City, Award:** phần còn thiếu ở §1. Competition đã xong phần FMRTE cho phép sửa; còn lại là tạo object VAR khi null (cấp phát RAM) và Stages (không làm vì FMRTE cảnh báo hỏng save).
4. **Player nhỏ:** Clone Center chép tên, đổi tên, Career Plans tạo container mới, loại điều khoản còn thiếu. **Move — làm sau:** nghiên cứu tạo hợp đồng mới và ghi lịch sử chuyển nhượng kèm phí; hiện Move giữ hợp đồng cũ, không tạo dòng lịch sử (người dùng đã kiểm tra Mbeumo → PSG trong game).
   **Move của FMRTE 27 (người dùng ghi nhận 5/11, đáp án để làm theo).** Hộp thoại: Move, From, To, Type (Transfer | Loan), ô
   **Move now**.
   - Tick Move now: chuyển ngay, **không có phí** ("Moves the player straight away, without a fee").
   - Bỏ tick: nhập Transfer Fee, Transfer Date, ô "Set transfer terms"; FMRTE ghi chú "Keep FM paused while saving. FM completes
     the transfer on the selected date" → game tự hoàn tất vào ngày đó.
   Kết quả thấy trong game: thêm dòng Career mới (ví dụ 2026/27 PSG £100M), giữ lịch sử cũ; cầu thủ được báo "đã được sắp xếp
   chuyển nhượng", có tin News; hợp đồng mới có vẻ được game tính theo CLB mới. Ta hơn ở chỗ chuyển ngay vẫn nhập được phí.
   Hướng làm: bản theo ngày gần như chắc là tạo một arranged transfer để game tự xử lý. Oracle: snapshot RAM trước/sau cho từng
   lựa chọn (danh sách offer `[module+offers]+0x8`, career history, contract, news), cả Loan.
5. **Đã dừng (Frida có thể mở lại Work Permit, Promises):** Player Promises, Work Permit, Scout Information, Training Rating, Make WonderKid, Injury Add, RCA.

## Khi game update (sau khi có `update-profile`)

1. Mở game, vào save, chạy `update-profile`.
2. Mọi module ĐẠT: build, chạy test, ghi thử một lần (chỉ số, hợp đồng, move), thêm profile vào repo, cập nhật CHANGELOG,
   commit, tag `v*` để CI build và phát hành.
3. Có module LỖI: chỉ sửa offset trong object của module đó (`find-offsets`, so RAM với FMRTE), chạy lại bước 1.
   Không đụng module khác. Phát hành khi mọi module ĐẠT, hoặc phát hành với module lỗi để chỉ đọc nếu người dùng đồng ý.

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

### Competition VAR — đối chiếu 5/11
Bundesliga ID 22: FMRTE Save ghi Remote Review = 02, TV Matches Only = 01, Enable custom VAR = 01 ở ba byte `[[competition+0xD0]+0x14..0x16]`, khớp bố cục Premier League.

### Competition VAR — đã thêm 5/11
Editor và CLI `compvar` đã đọc/sửa Type, TV Matches Only, Enable custom VAR có guard. FMRTE đã xác nhận hai mẫu PL/Bundesliga; CLI ghi và đọc lại hai giải, UI ghi TV của Bundesliga và CLI xác nhận. Build 0 lỗi; test 371 core + 146 app pass ở TEMP; mutation đổi offset TV làm test fail. Chưa tạo VAR khi con trỏ null, chưa kiểm Steam hoặc Continue. Xem chi tiết `re/FM27-findings.md`.

### Competition VAR From date — đã thêm 5/11
Đã bổ sung From date vào editor/CLI, nhập yyyy-MM-dd, xóa bằng ô trống; writer kiểm tra object/con trỏ/snapshot và đọc lại. FMRTE ghi/đọc xác nhận mẫu Bundesliga và Premier League; UI đổi ngày Bundesliga và CLI đọc lại. Build 0 lỗi, 371 core + 146 app test pass; mutation +1 ngày bị test bắt. Còn thiếu: tạo VAR khi null, kiểm Steam và tác động sau Continue. Chi tiết `re/FM27-findings.md`.

### Rules Bundesliga
FMRTE ghi Homegrown At Club mã 0x26 và Homegrown in Nation mã 0x27 vào ba bản sao; hai giải Đức dùng tên `FIRST`/`SECOND` thay cho ID. Cả hai trường có UI/CLI cho Bundesliga và Bundesliga 2; UI Bundesliga đã lưu hai trường cùng lúc, CLI và FMRTE đối chiếu. Maximum của hai giải Đức chưa hỗ trợ. Xem `re/FM27-findings.md`.

### Rules tổng quát — ưu tiên hiện tại
Người dùng yêu cầu tự liệt kê luật đúng theo giải, không hardcode từng giải. Spanish First Division (67) UI FMRTE hiện 25 Maximum / 23 Outfield / 3 Non-EU; survey primary trả 0x09=25 / 0xA9=23 / 0x12=3, mapping chỉ là suy luận do trùng giá trị, chưa xác nhận ghi. Reader bản sao hiện không tìm được Spanish. REA báo FMRTE.exe không có CLI metadata; cần nghiên cứu NativeAOT/runtime để tìm descriptor/nhãn và liên kết bản sao tổng quát. Không mở rộng bảng ID giải thủ công. Xem `re/FM27-findings.md`.

### Rules tổng quát — Frida game 5/11
Đã attach Frida trực tiếp fm.exe, module game_plugin không có export chứa rule/squad/registr; chỉ probe và detach, chưa bắt được hàm xử lý. Bước kế: watchpoint có giới hạn khi mở Rules trong game để truy instruction/call stack và descriptor; chưa khẳng định biết mapping nhãn. Xem findings.

Rules tổng quát: đã thử Frida MemoryAccessMonitor 45 giây trên trang chứa Maximum Squad Spanish, không hit; cửa sổ FM không hiển thị qua computer use nên chưa kích hoạt trang Rules. Script đã unload/detach. Đây chưa phải bằng chứng game không đọc; tiếp tục nghiên cứu descriptor hoặc watchpoint lúc điều hướng được game. Không hardcode thêm từng giải.

Rules tổng quát: đã tìm được trong binary game nhóm tên nội bộ nhiều biến thể squad/homegrown quanh file offset 0x4EAF064 (`first_year_senior_players_in_squad`, `homegrown_players_12_19_from_nation_4_years`, ...). Chưa chứng minh mã record ứng với tên nào; tiếp tục xác định PE RVA và bảng con trỏ/xref, không suy từ thứ tự chuỗi. Xem findings.

### Rules tổng quát — bảng mã trong game_plugin
Đã tìm trong `game_plugin.dll` bảng entry 16 byte `{pointer tên, mã}`; kiểm `module+0x4EA9D70` trong RAM Game Pass là `max_squad_size` mã 9. Các entry cùng bảng xác nhận `0x12 non_eec_players_in_squad`, `0xA9 outfield_players_in_squad`, `0x26/0x27` homegrown club/nation 15–21, `0x92 homegrown_players_from_nation`, `0xFD homegrown_players_from_club_or_nation`. Không suy mã từ thứ tự; có alias. Đây là bước hướng đến reader chung, chưa xác định biên đầy đủ/bản sao để mở ghi tổng quát. Xem findings.

Rules tổng quát: thêm survey chỉ đọc `re/tools/competition-rule-signatures.py` so toàn bộ cặp mã/giá trị primary với các nhóm bản sao; ba giải PL, Spanish, Bundesliga đều ra đúng một nhóm trong mỗi nguồn. Đây là tương quan, chưa chứng minh sở hữu và chưa được dùng làm guard ghi; cần tìm quan hệ danh tính độc lập và xử lý collision. Xem findings.

### Rules index chung — 5/11
Holder+0x31 trùng vị trí group trong hai nguồn `lrxf` ở sáu giải PL, Championship, Spanish, Bundesliga, Bundesliga 2, Ligue 1. Reader chuyển sang dùng index chung và kiểm snapshot/ba bản; CLI `comprules <id> code <hex>` đọc Ligue 1 mã 0x12 = 4, Spanish A9=23. Ligue 1 không có record max mã 9 dù ảnh FMRTE hiện 99, nguồn 99 chưa rõ. Build 0 lỗi; 372+146 test pass; mutation index cố định sai làm test fail. Chưa thử UI hoặc ghi sau đổi. Xem findings.

Ligue 1 Maximum Squad: user lưu 36, Frida bắt group+0xF0 byte 24 và 2 int32 bản sao qua `lrxf[index]→prns→dnss→smns`; đã thấy trước đó group+0xF0=125, sau =36. Spanish cùng byte =25. Cần nối guarded writer cho group và cả bản rule mã 9 khi tồn tại; không ghi 126, user cho biết 125 tối đa. Xem findings.

Maximum Squad fallback: đã có writer guarded cho giải không có rule 9 (`group+F0` 1 byte + hai `smns` int32), CLI đọc/ghi Ligue 1 36→37 và Bundesliga 99→100; ưu tiên rule 9 nếu có, không fallback khi guard rule 9 fail. Build 0 lỗi, 372+146 test pass, mutation F0→F1 fail. Chưa thử UI/FMRTE sau writer; FMRTE đã đóng. Không khẳng định Rules đầy đủ mọi giải. Xem findings.

Rules phổ biến: UI đã nối Non-EU (mã 0x12) và Outfield (0xA9) khi đủ ba bản, không theo ID giải. CLI guarded ghi/đọc Ligue 1 Non-EU 4→5, Spanish Non-EU 3→4, Outfield 23→24; build 0 lỗi, 372+146 test pass. UI chưa thao tác được (computer use thiếu geometry); FMRTE đang đóng. Chưa chứng minh outfield trên giải thứ hai, chưa thử Steam/Continue. Xem findings.

Competition Name / Classification Enabled: Người dùng xác nhận Name và Unique ID không thể chỉnh. Classification disable trên PL ghi vector end=begin, enable lại trong cùng tab ghi lại 80 byte rồi end=begin+80. Championship bật bảng trống tạo 24 dòng 0; ngày 5/11 CLI đọc lại hạng 1 = 12345678, hạng 2 = 23456789 sau khi người dùng lưu. Frida ghi end = begin khi tắt; bật lại trên cùng tab ghi 96 byte và end, RAM giữ hai số trên. Log không có nội dung buffer vì vượt 64 byte; sau đóng/mở tab rồi bật, người dùng thấy 0 và CLI xác nhận 24 dòng 0. Đã nối writer guarded, CLI và UI; CLI Game Pass tắt rồi bật Championship, đọc lại 0 rồi 24 dòng 0. UI tắt rồi bật Championship đã được CLI xác nhận riêng: 0 rồi 24 dòng 0; FMRTE xác nhận bảng trống sau tắt. Steam và cấp phát buffer mới trên game chưa thử; xem findings.
