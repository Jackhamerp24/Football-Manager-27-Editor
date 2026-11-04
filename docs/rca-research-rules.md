# Rule: đo và suy ra Recommended Current Ability (RCA)

Ngày lập: 2026-10-31. Đọc file này trước khi tiếp tục nghiên cứu RCA.
Đây là chiến lược thí nghiệm được người dùng yêu cầu, không phải công thức đã xác minh.

## Mục tiêu và quyền thực hiện

- Dùng RCA của **FM26 In-Game Editor chính thức** làm số đối chiếu độc lập.
- Ghi đủ vị trí, thuộc tính, hai chân; đo tăng/giảm từng thuộc tính 1 điểm tại từng vị trí, rồi suy ra quy tắc nhiều vị trí.
- Được thử trên nhiều cầu thủ. Người dùng đã xác nhận save FM26 hiện tại là save rác và cho phép Computer Use thay đổi mọi chỉ số của bất kỳ cầu thủ nào để nghiên cứu. Không hỏi lại trước từng lần chỉnh trong phạm vi này.
- Quyền trên thay thế yêu cầu hỏi từng lần ghi trong AGENTS.md đối với thí nghiệm này; không tự mở rộng sang save khác chưa được xác định là save thử nghiệm.
- Đọc skill Computer Use trước thao tác UI. Mọi ghi từ app/CLI phải qua writer có guard theo quy tắc dự án; không dùng offset FM27 để ghi FM26.
- Không copy code, bảng trọng số, asset hoặc chuỗi của FMRTE. Không lấy calculator bên ngoài làm ground truth.
- Chưa có bằng chứng rằng RCA là một field lưu sẵn hoặc không tồn tại trong RAM. Những câu khẳng định tuyệt đối cũ trong tài liệu không phải kết luận đã kiểm chứng.

## Mốc bàn giao: phải xác nhận lại khi tiếp tục

Lucas Herrington, Hull City; ngày game trên ảnh 20/12/2025; CA 154, PA 160.

| Trạng thái | RCA | Nguồn |
|---|---:|---|
| Ban đầu DC 20, DL 1, DR 1 | 154 | IGE; chưa ghi đủ mọi vị trí |
| Thêm DL 20 | 154 | Người dùng báo cáo |
| Giữ DL 20, thêm DR 20 | 154 | Người dùng báo cáo |
| Giữ DL/DR 20, thêm DM 20 | 162 | Người dùng báo cáo; IGE xác nhận kết quả cuối |
| D(RLC), DM; Crossing 7 → 8 | 162 | Agent quan sát trong IGE |

Chuỗi DL → DR → DM **cộng dồn**, không phải ba phép thử độc lập. DM trước chỉnh chưa biết.
Trạng thái cuối được ghi nhận là Crossing **8**, chưa hoàn nguyên. Không mặc định trạng thái này còn đúng khi mở lại.

Thuộc tính hiển thị trên ảnh trước lần Crossing 7 → 8 (chưa phải snapshot raw đầy đủ):

- Technical: Corners 5; Crossing 7; Dribbling 14; Finishing 6; First Touch 15; Free Kick Taking 2; Heading 17; Long Shots 8; Long Throws 8; Marking 15; Passing 16; Penalty Taking 3; Tackling 16; Technique 15.
- Mental: Aggression 16; Anticipation 14; Bravery 15; Composure 16; Concentration 16; Decisions 14; Determination 16; Flair 15; Leadership 11; Off The Ball 9; Positioning 16; Teamwork 12; Vision 15; Work Rate 12.
- Physical: Acceleration 12; Agility 10; Balance 11; Jumping Reach 18; Natural Fitness 15; Pace 16; Stamina 15; Strength 12.
- Chân trái Reasonable, chân phải Very Strong; **chưa biết điểm số chính xác**. GK, hidden/personality và vị trí còn thiếu cần đọc trước đo.

## Nguyên tắc đo bắt buộc

1. Xác nhận đúng phiên bản/build, save, cầu thủ, ngày game và baseline. Lưu snapshot đầy đủ, không chỉ field đang sửa.
2. Kiểm tra cơ chế commit/refresh của IGE. Phân biệt giá trị đang nhập, giá trị sau commit và RCA đã refresh. Dùng một thay đổi đủ lớn để phát hiện RCA stale nếu cần, rồi hoàn nguyên.
3. Giữ nguyên ngày trong loạt chính, **không Continue**. Người dùng lưu ý chỉ số ghi đè trong game có thể chỉ cập nhật sau Continue; tách thí nghiệm qua ngày riêng, không trộn với phép đo tức thời trong IGE.
4. Mỗi phép đo xuất phát từ cùng baseline, đổi một biến, commit, đọc lại thuộc tính và RCA, khôi phục baseline, đọc lại RCA. Nếu baseline không trở lại thì đánh dấu phép đo bị nhiễu và điều tra trước khi dùng.
5. Chụp/đọc lại trạng thái sau từng thao tác theo skill; không chạy chuỗi click mù. Ghi nhận nếu game tự thay đổi field khác.
6. Số nguyên hiển thị có thể che giá trị thô/phần lẻ. Nhập lại cùng số hiển thị không chứng minh raw đã hoàn nguyên. Ưu tiên đọc raw nếu đã có cách xác minh; nếu chưa có, tạo baseline số nguyên chuẩn hóa qua IGE và lưu riêng với snapshot gốc, ghi rõ giới hạn.
7. RCA không đổi khi +1 **không chứng minh trọng số bằng 0**. Không đồng nhất RCA với phong độ. Không chặn RCA ở PA chỉ vì PA thấp hơn.
8. Lưu nhật ký sau từng nhóm nhỏ; checkpoint phải nêu field nào đang khác baseline và thao tác kế tiếp. Không báo hoàn tất từ vài phép thử.

## Thứ tự thực hiện

### A. Snapshot và thử độ ổn định

- Đọc đủ General, Positions, hai chân, Technical, Mental, Physical, Goalkeeping và các field liên quan có thể chỉnh.
- Đọc RCA lặp lại khi mở/đóng tab mà không sửa để kiểm tra refresh.
- Xác nhận Crossing hiện tại; thiết lập baseline có ID riêng. Thử Crossing -1/+1, hoàn nguyên sau mỗi nhánh; thêm bước lớn nếu cần kiểm tra refresh.

### B. Vị trí đơn

- Cùng cầu thủ, cùng thuộc tính/chân/CA/PA: đặt một vị trí 20, mọi vị trí khác ở mức thấp nhất hợp lệ của editor.
- Đọc lại **toàn bộ** vị trí sau commit, vì game có thể tự chuẩn hóa. Nếu không tạo được trạng thái vị trí đơn thật, ghi cấu hình thực tế; không gán nhãn sai.
- Lặp từng vị trí mà editor thực sự hỗ trợ. GK là nhóm riêng, gồm thuộc tính thủ môn.
- Không nhầm vị trí sân với vai trò chiến thuật.

### C. Ma trận thuộc tính × vị trí

- Ở từng baseline vị trí đơn: với mỗi thuộc tính, đo x-1, baseline x, x+1; mỗi nhánh phải độc lập và hoàn nguyên.
- Nếu x ở biên thì nhánh vượt giới hạn được ghi `boundary`, không tạo số đo giả.
- Thêm x±2, x±4 trong giới hạn để tách ảnh hưởng nhỏ khỏi làm tròn.
- Quét các mức thấp/trung/cao và dày hơn khi nghi phi tuyến (đặc biệt vùng 16–20 là giả thuyết cần kiểm tra, không phải quy luật đã biết).
- Dùng nhiều baseline mức thuộc tính để tránh nhầm ngưỡng làm tròn với thay đổi trọng số. Tránh vùng RCA bão hòa khi ước lượng độ dốc.
- Không loại thuộc tính chỉ vì tài liệu FM21 nói không tính CA; kiểm chứng trên FM26.

### D. Độ thành thạo và tổ hợp vị trí

- Giữ vị trí chính 20; quét vị trí phụ từ mức thấp nhất hợp lệ đến 20 để tìm ngưỡng/tính liên tục.
- Đo các cặp rồi bộ ba: DC+DL, DC+DR, DC+DM, ST+AML, ST+AMR, ST+AML+AMR; mở rộng theo sai số.
- Với Herrington: đối chiếu DC+DM với DC+DL+DR+DM; tái tạo đúng chuỗi người dùng đã làm.
- Đảo thứ tự thêm vị trí để kiểm tra tính phụ thuộc thứ tự/cập nhật.
- So sánh các giả thuyết: chọn một vị trí, lấy trọng số lớn nhất từng thuộc tính, trung bình có trọng số, khoản cộng, hoặc tương tác khác. Không mặc định nhiều vị trí luôn tăng RCA.

### E. Hai chân, CA/PA và tương tác

- Giữ các yếu tố khác cố định, thay từng chân riêng; lặp ở vài vị trí và thử hoán đổi trái/phải.
- Thay riêng CA và PA để kiểm tra RCA có phụ thuộc trực tiếp hoặc bị giới hạn không.
- Khi mô hình một biến có sai số có cấu trúc, dùng phép thử cặp thuộc tính có kiểm soát để phát hiện tương tác; không ép mô hình tuyến tính cho khớp.
- Thí nghiệm Continue chỉ làm sau khi đã lưu snapshot và số đo trước Continue; sau Continue chụp lại toàn bộ thay đổi, không quy mọi thay đổi cho một nguyên nhân.

### F. Ước lượng và kiểm chứng

- Dự kiến 6–10 cầu thủ gồm GK, hậu vệ, tiền vệ, tiền đạo; chọn đa dạng chỉ số/chân/vị trí. Đây là điểm xuất phát, không phải cỡ mẫu đảm bảo chính xác.
- Chia nhóm xây mô hình và nhóm kiểm chứng **theo cầu thủ trước khi fit**. Nếu dùng nhóm kiểm chứng để chỉnh mô hình, phải dành nhóm độc lập mới để đánh giá cuối.
- RCA nguyên tạo ràng buộc làm tròn; không lấy một delta nguyên làm trọng số chính xác. Kiểm tra quy tắc round/floor/ceil và hằng số cùng các giả thuyết phi tuyến.
- Báo số mẫu, tỷ lệ khớp chính xác, sai số tuyệt đối trung bình/lớn nhất, sai số theo vị trí và các trường hợp thất bại. Không tuyên bố tìm được công thức engine chỉ vì khớp tập nhỏ.
- Kết quả FM26 không tự động đúng cho FM27. Chỉ tích hợp dưới nhãn `RCA ước tính` nếu vẫn là mô hình gần đúng; công bố phạm vi và kiểm chứng phiên bản.

## Nhật ký và bằng chứng

Lưu trong `re/rca-experiments/`; tách quy tắc (file này) khỏi dữ liệu quan sát.
Mỗi baseline có đủ snapshot và nguồn; mỗi phép thử cần các trường:

```text
experiment_id, timestamp, game_build, save_session, game_date,
player_id_or_name, baseline_id, positions, left_foot, right_foot,
field, before_display, after_display, before_raw?, after_raw?,
ca_before, pa_before, rca_before, rca_after,
commit_refresh_method, other_changes, restore_value, restore_rca,
evidence_reference, status, notes
```

- Giá trị chưa biết là null/unknown, không điền đoán. `status` phân biệt observed, user_reported, invalid, pending, boundary.
- Không lưu địa chỉ object làm danh tính bền vững qua phiên. Không sửa golden FM27 để phản ánh phép thử FM26.
- Phân rõ **Quan sát / Người dùng báo cáo / Suy luận / Chưa biết** khi cập nhật `re/FM27-findings.md`.
- Nếu sửa phần mềm/test để hỗ trợ nghiên cứu: đọc skill test-discipline và áp dụng AGENTS.md; oracle phải từ IGE độc lập, không từ công thức đang test.

## Checkpoint bàn giao bắt buộc

Ghi: đã đo những baseline/field nào; đang mở cầu thủ/tab nào; giá trị nào chưa khôi phục; game có Continue chưa; file dữ liệu; lỗi/nhiễu; phép đo tiếp theo. Khi tiếp tục phải xác nhận UI hiện tại trước thao tác.

Tham khảo lịch sử: [FMRTE26 findings, mục 8](../re/FM27-findings.md). Các nguồn FM Scout được ghi ở đó chỉ là giả thuyết tham khảo, không thay số đo IGE.
