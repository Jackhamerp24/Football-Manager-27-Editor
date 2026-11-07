# Bàn giao Argon: rà soát và cải thiện UX/UI toàn app

## Mục tiêu

Người dùng muốn bạn **xem từng trang và mọi tab con của Football Manager 27 Editor**, không bỏ sót chỉ vì trang đó ít dùng. Dùng một đối tượng đại diện cho mỗi loại, không cần lặp trên mọi cầu thủ/CLB. Đánh giá lỗi bố cục, căn chỉnh, nhãn, điều hướng, tốc độ và thao tác; sau đó cải thiện mạnh nhưng giữ nguyên và nối đúng các chức năng thực sự đã có. Không đánh dấu một trang đã được kiểm nếu chưa mở trang đó; ghi `chưa kiểm` cùng lý do.

Đọc `docs/HANDOVER.md`, `AGENTS.md`, `docs/TODO.md` và skill `C:/Users/Duong/.agents/skills/test-discipline/SKILL.md` trước. HANDOVER có nhiều dòng theo thời điểm: ưu tiên trạng thái mới nhất và kiểm code thực tế, không mặc định tất cả dòng cũ còn đúng. Người dùng đã cho phép dùng computer use; khi họ nói đang tự kiểm hoặc yêu cầu dừng thì dừng thao tác. Không commit/push.

## Môi trường và đối tượng mẫu

- Workspace `C:\Users\Duong\Desktop\FMRTE clone 27`; C#/.NET 10, Avalonia code-behind, không XAML.
- Bản app gần nhất: `C:\Users\Duong\Desktop\FM27 Editor\App-PlayerLayout\Fm27Editor.exe`. Kiểm đúng process/path trước khi xem; trước khi mở bản build mới, đóng bản editor cũ. Không mở nhiều instance editor.
- Mở FM27 Game Pass, vào save rồi bấm **Load Game** trong editor. Không giả định PID cố định. Save là save rác, người dùng đã cho phép ghi thử qua writer có guard; không ghi mò. Nếu không cần ghi để đánh giá bố cục thì chỉ xem/nhập nháp, không bấm Save.
- Mẫu đề xuất: **Bruno Fernandes ID 43124203** hoặc Mbappé ID 85139014; **Manchester United ID 680**; **England ID 765**; **Premier League ID 11**; **Old Trafford**; **Manchester**; **World Footballer of the Year**; một staff nam bất kỳ xác định bằng ID. Tra cứu ID thực tế trong app/CLI khi chưa biết, không đoán. Future Transfer chỉ hiện nếu mẫu có; chọn thêm một người đúng điều kiện hoặc đánh dấu chưa kiểm, không giả vờ mục đã được xem.
- FMRTE 27 chỉ để tham khảo, tối đa **một tab cho mỗi đối tượng**, tái sử dụng tab đang có. Không mở hàng loạt tab trùng. Đóng tab khi không còn dùng; không đóng/mở liên tục trong cùng context. Không dùng nữ, không làm Swap Player/Swap Staff, không bấm Install update. Load Game đã được người dùng cho phép nếu cần.
- Graphics tùy chỉnh của người dùng: `D:\Football Manager 27\graphics`. Dùng nút Graphics để chọn; app tự ghi nhớ. `config.xml` ánh xạ ID sang file có thể mang tên khác ID. Ảnh/license đóng gói trong game chưa được hỗ trợ; không nhầm logo liên đoàn với cờ. Tính năng graphics vừa sửa chưa được kiểm UI.

## Yêu cầu UX người dùng đã chốt

- Dùng Player làm mẫu điều hướng: các nút trên cùng trở lại danh sách tương ứng, tab đối tượng duy nhất, tab đang mở không biến mất; không tự chọn CLB đầu. Chuyển Players ↔ Clubs không được treo.
- Thanh mục bên trái của Player **sticky**, giữ nguyên vị trí, chiều rộng và chiều cao khi đổi mục hoặc cuộn. Cột này hiện được tách khỏi nội dung trong `AppShell.cs`/`SectionHost.cs`; kiểm lại, không làm tái phát co giãn do `Viewbox`. Khoảng cách rõ giữa thanh mục và nội dung. Không kéo ngang toàn trang Player để thấy thông tin.
- Tham khảo FMRTE về **mật độ, pacing, căn cột, hàng và phân cấp**: cửa sổ khoảng 1000–1100 px cũng phải dùng được. Cho phép tự thiết kế màu; mặc định hiện là nền tối với tím. Không tagline, helper text vô ích, emoji, gradient trang trí hay thẻ lồng nhau. Không cần giải thích thao tác máy tính thông thường.
- Chữ và giá trị không được bị che, cắt bằng `...` hoặc bẻ xuống nhiều dòng. Chú ý tên dài, dropdown, tên nút Tools, Free Kick Taking, Natural Fitness, Personality, Mental và Planned Retirement. Không xử lý bằng cách chỉ thu nhỏ font tới mức khó đọc. Số và chữ phải được căn giữa dọc trong ô.
- Player header: ảnh cạnh tên khi có graphics, CLB/giải/quốc gia bấm mở tab tương ứng; cờ quốc gia nhỏ. CA/PA đặt bên phải header, không bọc card; Freeze trong cột Tools. Personal Details chứa Born (`dd-MM-yyyy`, có picker), Height, Reputation, Morale, Asking Price, Caps/Int Goals/Youth Caps/Youth Goals. Condition/Sharpness/Jadedness ở hàng sticky gọn, không bọc mỗi mục trong card. Technical/GK chuyển ngay trên tiêu đề cột, GK mặc định với thủ môn. Cột attribute cùng chiều rộng, text một dòng, ô số nhỏ vừa 1–20; 21 phải bị từ chối và về giá trị trước khi sửa.
- Không có nút Apply cho từng trường: mục tiêu là **một Save chung**, các thay đổi vào nháp. Hiện thực chưa hoàn thành: top Save mới định tuyến Player/Club, một số panel còn Save/Apply riêng hoặc ghi ngay. Khi review phải nêu chính xác; không xóa nút đang là đường ghi duy nhất nếu chưa nối chức năng tương đương. Enter xác nhận/rời ô số, không tự ghi game; hiện chưa đảm bảo cho mọi TextBox số.
- Squad chọn nhiều bằng Ctrl+click và thao tác chuột thông thường; thanh Tools chỉ ở Player Information hoặc Club Squad. Destroy, Heal, Inspire, Tactical Understanding phải áp dụng đúng tập cầu thủ được chọn hoặc tất cả theo lựa chọn; không tự suy giá trị hay đổi guard. Không thêm thanh Tactical Understanding, không làm promise staff.
- Bất kỳ color picker nào: chọn trực quan, nhập Hex và các kiểu màu hữu ích; xem cả CLB, quốc gia, sân vận động và giải đấu. `Kit for specific season` tạm dừng cho tới khi người dùng xác nhận.

## Checklist bắt buộc: mở từng trang và ghi kết quả

Mỗi dòng phải có **đã xem / không có ở mẫu / bị chặn**, kích thước cửa sổ, lỗi quan sát, và ảnh hoặc bước tái hiện khi cần. Kiểm một lần ở cửa sổ mặc định và một cửa sổ hẹp hơn; đổi tab, cuộn và quay lại danh sách. Danh sách dưới là điểm xuất phát: đối chiếu thanh menu thực tế và code để thêm mọi trang/dialog còn thiếu.

1. **Khung chung:** Home, Load Game/Retry, thanh module, dải tab, tìm kiếm chung, Save, History, Mass, Freezer, Graphics, status, đóng/mở lại tab; không khẳng định nút có chức năng nếu code đang báo chưa hỗ trợ.
2. **Players:** danh sách, tìm/lọc/sort bằng cột, Scout/Advanced Search (General, Attributes gồm Technical/GK/Mental/Physical/Hidden, Positions, Preferred Moves, preset), import/export trong Player Information. Sau đó từng mục: Information, Positions, Career Plans, Move to club, Work Permit, Promises, Unavailability, Future Transfer nếu có, Transfer Proposals, Injury, Contract và tab con, Preferred Moves, Relationships, Languages, Scout Information, Comparison, Clone Center, Swap National Call Up. Với mục trống/chưa triển khai, ghi rõ, không tự bịa tính năng.
3. **Clubs:** danh sách, Information, Squads và chọn nhiều, Finances và các trang con Overview/Sponsors/Debt/Income Statement, Supporters, Club Vision, Affiliations, Regional Divisions, Kits and Colours (6 bộ + item), Transfer Center, Relationships, Staff. Rà các mục thực tế theo `ClubFieldInfo.Sections` và `ClubView.cs`, không bỏ trang con vì là tab lồng.
4. **Staff:** danh sách, Information, Contract, Career Plans, Relationships, Languages, Knowledge, Formations, Tendencies, Move to club và dialog liên quan.
5. **Nations:** danh sách, Information (gồm Agreements), National Teams, Squads, Kits and Colours, Relationships, Transfer Preferences; kiểm các trang thực sự xuất hiện, ghi riêng Nation Staff nếu chưa có UI.
6. **Competitions:** danh sách và chi tiết, stage/standings, Games Played/W/D/L/GS/GA/Points, prize money, classification, squad registration/rules và VAR nếu có với mẫu. Nếu giải mẫu không có một phần, đánh dấu theo dữ liệu và chọn mẫu bổ sung chỉ khi cần để thấy phần đó.
7. **Stadiums, Cities, Awards:** lần lượt danh sách, mở một đối tượng, xem toàn bộ mục, dropdown, picker màu, Save và các trạng thái không hỗ trợ. Tên/ID Award là read-only; không đề xuất bật sửa vô căn cứ.
8. **Dialog/luồng dùng chung:** chọn màu, chọn ngày, chọn đối tượng, tìm kiếm, xác thực nhập số, thông báo lỗi, trạng thái loading/empty, resize, keyboard/Enter, popup bị che, navigation trở lại tab cũ, bảo toàn nháp. Test chức năng có tác động chỉ trong phạm vi đã được phép; đối chiếu RAM qua writer/CLI khi cần, không mặc định UI đúng vì build/test xanh.

## Cách ghép tính năng và nơi sửa

- `MainWindow.cs`/`AppShell.cs`: khung chính, điều hướng module/tab, Player header, Save và sticky. `SectionHost.cs`: menu mục và điều hướng nội dung; UI drag order trong `UiOrderStore.cs`.
- `PlayerInformationView.cs`, `PlayerActionsView.cs`, `PlayerCareerPlansView.cs`, `PlayerContractView.cs`, `Player*View.cs`: các trang Player. `Modules.cs`/`PlayerFields` mô tả field; `PlayerDraft` và `playerScalarDrafts` là nháp. Numeric 1–20 đang commit nháp khi rời ô; kiểm Enter, invalid và chuyển tab.
- `ClubView.cs`, `ClubFieldInfo.cs`, `ClubLists.cs`, `ClubKitsView.cs`, `SquadView.cs`: Club, squad, finance, kit. `NationView.cs`, `StaffView.cs`, `CompetitionView.cs`, `CityView.cs`, `StadiumView.cs`, `AwardView.cs`: các module tương ứng. `AttributeFilterDialog.cs`: Scout. `GraphicsCatalog.cs`: index config, cache ảnh, folder picker.
- `EditorBackend`/`IEditService` và Core là đường đọc/ghi; giữ vtable/snapshot/readback guard. Không gọi ghi RAM trực tiếp, không đổi offset để sửa bố cục, không biến nút trống thành nút giả. Một số Save thực hiện nhiều bước: không tuyên bố atomic nếu code không bảo đảm.
- Dữ liệu live phải lấy theo ID và phiên hiện tại; không gắn địa chỉ RAM cũ vào UI hoặc test. Không tự đoán cấu trúc game. Các phần chưa có backend/guard cần ghi riêng, không tự làm giả chỉ để hình trông đủ.

## Bàn giao kết quả

Đầu tiên lập bảng kiểm đủ trang và phân loại lỗi: tên trang, trigger, hiện trạng, đề xuất, mức ảnh hưởng, đã sửa/chưa sửa. Ưu tiên các lỗi làm che dữ liệu, treo, điều hướng sai, ghi nhầm hoặc mất nháp; sau đó mới tinh chỉnh visual. Thực hiện các sửa đổi trong phạm vi người dùng giao, không dừng chỉ ở đề xuất. Kiểm sau sửa ngay trên app khi quyền/hoàn cảnh cho phép, ghi rõ đã thấy và chưa thấy; không yêu cầu người dùng chỉ hộ từng lỗi. Không viết test để đủ số; dùng test có sẵn và thêm test có ý nghĩa nếu có rủi ro logic mới.

Build `dotnet build Fm27Editor.slnx -c Release`. Khi testhost trong workspace không có quyền process, dùng quy trình bản sao test ngoài workspace trong HANDOVER, không bỏ qua lỗi. Ghi lại bản chạy, test thực thi và các mục chưa kiểm; cập nhật `docs/HANDOVER.md` và `docs/TODO.md` ngắn gọn. Không hứa hoàn tất tất cả chỉ vì build xanh.
