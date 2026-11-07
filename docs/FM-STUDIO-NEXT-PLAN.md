# Kế hoạch tính năng tiếp theo (từ FM Studio 27 v1.1.2)

## 0. Trạng thái bản v1.1.2 (đã kiểm)

- Bản trên máy: `C:\Users\Duong\Desktop\FM_Studio_27_Portable_v1.1.2\FM27Studio.exe`, 72.456.722 byte.
- Khác v1.1.1: đóng gói **single-file** (một exe, deps.json nhúng, xác nhận `"FM27Studio/1.1.2"`), kèm thư mục mới
  `Stadiums\` — **278 ảnh sân** (`<stadiumId>.jpg`, có biến thể `130000A.jpg`) và `Stadiums\Flags\`.
  v1.1.1 để rời `FM27Studio.dll` + thư mục ngôn ngữ.
- **Không trích xuất được DLL từ exe trong phiên này**: file là bundle nén kiểu riêng, header bundle không theo
  layout chuẩn (đã thử đọc footer, quét chữ ký, parse ngược từ EOF — đều thất bại). Vì vậy danh sách API mới của
  v1.1.2 **chưa đối chiếu được**; các mục dưới đây dựa trên bản đồ service của v1.1.1 (đã decompile đầy đủ) + thay đổi
  thấy được của v1.1.2.
- Thay đổi chức năng của v1.1.2 theo mô tả phát hành: hỗ trợ Game Pass, sửa đồng bộ squad rules / standings /
  lịch sử transfer — phần lớn là **sửa lỗi**, không phải tính năng RAM mới. Editor của ta đã chạy Game Pass.
- Nếu cần đúng API v1.1.2: chạy app một lần rồi dump tab/panel qua UI Automation, hoặc xin bản có `FM27Studio.dll` rời.

## 1. Nguyên tắc cho mọi mục (lấy từ TODO.md)

1. Xong = **có trong app** + ghi thật trên save rác + đọc lại RAM khớp + **hành vi khớp FMRTE** + 1–2 test cho logic ghi mới.
2. Offset mới phải đo trên RAM build đang chạy, ghi vào `re/FM27-findings.md`, không suy từ CIL.
3. Không bao giờ hiện mã enum thô trên UI (dùng nhãn đã giải mã, hoặc nhãn trung tính).
4. Mỗi writer: guard vtable + snapshot + đọc lại; sai thì ném `StaleObjectException` / `MemoryAccessException`.

## 2. Việc sẽ làm — theo thứ tự

### Giai đoạn A — Đóng nốt Club Vision (1–2 ngày)

| # | Việc | Cách làm | Xong khi |
|---|---|---|---|
| A1 | **Source Type** (game hiện Board / Unknown (194)) | Man Utd có đúng 1 dòng Unknown(194) → so byte/word giữa dòng đó và các dòng Board; kiểm lại trên ≥2 CLB khác có dòng Unknown | Đọc được source type; nếu game cho sửa thì có writer; FMRTE khớp |
| A2 | **Tạo dòng Club Vision từ đầu** (type/importance/target/2 ngày/competition) | Đã có `Duplicate` (copy bản ghi + cấp phát). Mở rộng: dựng bản ghi mới từ template nhưng ghi đè 5 trường, thêm vào vector | Tạo dòng mới → Continue → màn Club Culture của game hiện dòng đó |
| A3 | **Chốt evidence các trường còn lại** | Sửa type/importance/ngày/target trên 1 dòng → Continue → đối chiếu | Game đổi đúng cả 4; ghi vào tài liệu bàn giao |

### Giai đoạn B — Đăng ký thi đấu & ban lãnh đạo (3–5 ngày)

| # | Việc | Nguồn | Cách làm |
|---|---|---|---|
| B1 | **Register players for domestic division** | `SquadRegistrationHookService` (7 method), `SquadEligibilityHookService` (23) | So cầu thủ đã/chưa đăng ký trong squad; tìm cờ (bit/byte) trên player hoặc team; đo trên 2 CLB |
| B2 | **Homegrown (club/nation)** | FM Studio có grant HGC/HGN | Tìm cờ homegrown của cầu thủ; đối chiếu màn hình FMRTE |
| B3 | **Board request** | `BoardRequestService` (12) | Decompile v1.1.1 để lấy danh sách request + trường ghi; nếu chỉ là UI gọi hàm có sẵn thì tìm object board |
| B4 | **Staff allowance** | `StaffAllowanceService` (8) | Xác định "allowance" là gì trong RAM (có thể là staff cap); đo 2 CLB |

### Giai đoạn C — Chuyển nhượng & tuyển quốc gia (5–8 ngày)

| # | Việc | Nguồn | Cách làm |
|---|---|---|---|
| C1 | **Transfer deal**: đọc/sửa deal hiện có, force accept/confirm | `TransferDealService` (27), `TransferOverrideService` (4), `NegotiationBypassService` (16) | Tìm object deal từ club/player; so deal thật trong save; chỉ làm phần đọc trước |
| C2 | **Nation transfer values** (ma trận 20 mức danh tiếng) | `NationMemoryService.SaveNationTransferValues` + `ReputationOffsetChain` | Đi theo chain từ nation; xác định mảng 20 phần tử; so với FMRTE |
| C3 | **Nation rivals** | `NationMemoryService.SaveNationRivals` | Khác bảng Relationships đã có; đo trên England/Vietnam |

### Giai đoạn D — Quyết định có làm hay bỏ

| # | Việc | Đề xuất |
|---|---|---|
| D1 | **Stadium images** (mới trong v1.1.2: 278 ảnh + Flags) | **Bỏ** — chỉ là ảnh minh hoạ, không phải dữ liệu game; trái quy tắc "không trang trí" trong AGENTS §6 |
| D2 | **Referee / Award memory service** | Chỉ làm nếu người dùng cần; chưa có màn nào trong FMRTE chứng minh |
| D3 | **Tactical advisor (gợi ý chiến thuật)** | **Bỏ phần tính điểm** (không ghi RAM); chỉ giữ phần đã có |
| D4 | **Nation agreements** | **Không làm** — hàm trong FM Studio là stub (trả rỗng/true) |

## 3. Còn thiếu dữ liệu để làm tiếp

- Bản **Steam** của FM27 (đo lại `stadium.listOffset`; hiện chỉ có Game Pass).
- Chụp màn FMRTE cho: Source Type, Register domestic, Board request, Nation transfer values (để làm oracle).
- Một lần Continue cho A3 (sửa type/importance/ngày/target).

## 4. Đề xuất objective cho goal mode

> Hoàn tất các tính năng FM Studio còn thiếu trong editor, theo thứ tự A → B → C:
> A1 Source Type, A2 tạo dòng Club Vision, A3 chốt evidence; B1 register domestic, B2 homegrown, B3 board request,
> B4 staff allowance; C1 transfer deal (đọc trước), C2 nation transfer values, C3 nation rivals.
> Mỗi mục phải: đo offset trên RAM build 2493426, có writer guard, test mới, ghi thật trên save rác, đối chiếu FMRTE,
> và cập nhật `docs/BACKEND-FEATURES-TO-WIRE.md` để agent UI nối. Bỏ D1–D4 (ảnh, referee/award, advisor, agreements).
