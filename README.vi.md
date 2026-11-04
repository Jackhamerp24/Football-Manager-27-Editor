# Football Manager 27 Editor

[English](README.md) | Tiếng Việt

Editor dùng khi đang chơi Football Manager 27. App sửa cầu thủ ngay lúc game đang chạy, bằng cách đọc và ghi
bộ nhớ của `fm.exe` (module `game_plugin.dll`).

Chỉ dùng cho save chơi một mình, offline. Không dùng khi chơi online hay với save chung.

## App làm được gì

| Mục | Đọc | Ghi |
|---|---|---|
| Chỉ số cầu thủ (kỹ thuật, tinh thần, thể chất, thủ môn) | ✓ | ✓ |
| Vị trí (1–20) | ✓ | ✓ |
| Tính cách: thích nghi, tham vọng, trung thành, chịu áp lực, chuyên nghiệp, tinh thần thể thao, tính khí, gây tranh cãi | ✓ | ✓ |
| CA / PA | ✓ | ✓ |
| Tên, chiều cao, ngày sinh, tuổi theo ngày trong game | ✓ | |
| Danh tiếng (trong nước, hiện tại, thế giới) | ✓ | |
| CLB, lương tuần, ngày bắt đầu và hết hạn hợp đồng | ✓ | |

Đọc toàn bộ cầu thủ trong dữ liệu game (khoảng 50.000 người) mất chưa tới 1 giây.

Trước mỗi lần ghi, app kiểm tra:
- object ở địa chỉ đó vẫn còn là cầu thủ;
- giá trị trong bộ nhớ vẫn bằng giá trị app đã đọc.

Sau khi ghi, app đọc lại để xác nhận. Nếu có gì không khớp thì app không ghi gì cả và báo lỗi.

## Yêu cầu

- Windows 10/11 x64
- Football Manager 27, build `27.0.0+2486695`. Bản game khác cần profile offset mới, xem [Khi game có patch](#khi-game-có-patch).
- [.NET 10 SDK](https://dotnet.microsoft.com/download), chỉ khi bạn tự build

## Tải về

Tải ở mục [Releases](../../releases):

| File | Dùng để |
|---|---|
| **`Fm27Editor.exe`** | App editor. Đa số người dùng chỉ cần file này. |
| `Fm27Editor.Tools.exe` | Công cụ dòng lệnh (xem bên dưới), có cả `find-offsets` để hỗ trợ bản game mới |

Mỗi file exe chạy độc lập, để ở thư mục nào cũng được. Không cần cài đặt gì, kể cả .NET.

Toàn bộ mã nguồn nằm trong repo này. Mọi bản phát hành đều do [workflow Build](.github/workflows/build.yml) build từ mã
nguồn đó trên máy chủ của GitHub, không build trên máy cá nhân và không upload tay. Mỗi bản phát hành ghi rõ commit
dùng để build. Bạn có hai cách để tự kiểm:

- File `SHA256SUMS.txt` trong bản phát hành có hash của từng file. So với kết quả của `Get-FileHash Fm27Editor.exe`.
- Mỗi file có một chứng thực build có chữ ký, nối file đó với lần chạy workflow và commit. Kiểm bằng
  [GitHub CLI](https://cli.github.com):

  ```bash
  gh attestation verify Fm27Editor.exe --repo Jackhamerp24/Football-Manager-27-Editor
  ```

App làm gì với máy của bạn: mở process `fm.exe`, đọc bộ nhớ của nó, và chỉ ghi đúng những giá trị bạn sửa. App không
kết nối mạng, không ghi file nào (chỉ lệnh `find-offsets` ghi profile ra đường dẫn bạn chỉ định), và không đòi quyền
administrator.

## Build và chạy

```bash
dotnet build Fm27Editor.slnx -c Release
```

```bash
dotnet test Fm27Editor.slnx -c Release
```

Mở FM27, tải một save, rồi chạy app:

```bash
src/Fm27Editor.App/bin/Release/net10.0/Fm27Editor.exe
```

Nên lưu game trước khi sửa. Có màn hình chỉ hiện giá trị mới sau khi bạn bấm **Continue**.

## Công cụ dòng lệnh

Chạy khi game đang mở và đã tải save.

```bash
src/Fm27Editor.Tools/bin/Release/net10.0/Fm27Editor.Tools.exe players Yamal
```

```bash
src/Fm27Editor.Tools/bin/Release/net10.0/Fm27Editor.Tools.exe set "Kobbie Mainoo" Crossing 12
```

```bash
src/Fm27Editor.Tools/bin/Release/net10.0/Fm27Editor.Tools.exe verify tests/golden/27.0.0+2486695.players.json
```

| Lệnh | Làm gì |
|---|---|
| `players [tên]` | Liệt kê cầu thủ khớp tên: ngày trong game, tuổi, CLB, lương, hạn hợp đồng, CA/PA |
| `set <tên> <field> <giá trị>` | Ghi một giá trị. Field là chỉ số (`Pace`), vị trí (`Striker`), tính cách (`Ambition`), `CA` hoặc `PA` |
| `verify <golden.json>` | So bộ nhớ game với các giá trị chép từ giao diện game |
| `find-offsets <golden.json> <out.json> [--date yyyy-MM-dd]` | Dựng lại profile offset cho bản game mới |

## Cơ chế CA trong FM

Game coi CA là giá trị gốc. Sau vài ngày trong game, các chỉ số sẽ dần bị kéo về mức hợp với CA của cầu thủ, theo cả
hai chiều. Vì vậy tăng chỉ số mà không tăng CA thì có thể không giữ được lâu. Nên sửa cả CA.

## Cấu trúc

| Đường dẫn | Nội dung |
|---|---|
| `src/Fm27Editor.Core` | Truy cập bộ nhớ, kiểm build, profile offset, đọc cầu thủ, ghi có kiểm tra, tìm offset |
| `src/Fm27Editor.App` | App desktop (Avalonia) |
| `src/Fm27Editor.Tools` | Công cụ dòng lệnh |
| `offsets/<build>.json` | Mọi địa chỉ và offset phụ thuộc bản game, mỗi bản một file. Được nhúng sẵn vào exe |
| `tests/Fm27Editor.Core.Tests`, `tests/Fm27Editor.App.Tests` | Unit test chạy trên bộ nhớ giả |
| `tests/golden/<build>.players.json` | Giá trị cầu thủ chép từ giao diện game, dùng làm chuẩn cho `verify` và `find-offsets` |
| `re/tools/memscan` | Công cụ quét bộ nhớ (chỉ đọc), dùng để tìm cấu trúc dữ liệu của FM27 |
| `AGENTS.md` | Trạng thái dự án, những gì đã kiểm chứng, câu hỏi còn mở, lộ trình |

## Khi game có patch

App sẽ không gắn vào bản game chưa có profile. Như vậy app không bao giờ ghi nhầm địa chỉ.

Để tạo profile cho bản game mới:

1. Tải một save. Chép chính xác chỉ số, chiều cao và ngày sinh của vài cầu thủ vào một file golden mới.
   - Phải có ít nhất một cầu thủ có tên thường gọi, và ít nhất một cầu thủ không có.
   - Bỏ các khoảng do scout đưa ra, ví dụ `9-12`.
2. Chạy `find-offsets <golden.json> offsets/<build>.json --date <ngày trong game>`. Mất khoảng 25 giây.
   App dùng thêm các profile trong thư mục `offsets` đặt cạnh file exe. Nếu trùng bản game, profile trong thư mục
   sẽ thay cho profile nhúng sẵn.
3. Chạy `verify` với cùng file golden.

## Lộ trình

- **Thêm dữ liệu:** ghi lương và hợp đồng, cho mượn, điều khoản, tài chính và danh tiếng CLB, nhân sự, quốc gia, chấn thương.
- **Tính năng editor:** tìm kiếm và lọc nâng cao, sửa hàng loạt, khoá giá trị, preset (hồi phục, xoá án treo giò…),
  so sánh cầu thủ, xuất dữ liệu.
- **Phát hành:** bản self-contained, kiểm kích thước và tốc độ khởi động.

## Miễn trừ trách nhiệm

Dự án này không liên kết với và không được Sports Interactive hay SEGA bảo trợ. Football Manager là thương hiệu của họ.
Sửa bộ nhớ game có thể làm hỏng save, hãy giữ bản sao lưu. Bạn tự chịu rủi ro khi sử dụng.

## Ủng hộ

Nếu editor có ích, bạn có thể ủng hộ qua [Ko-fi](https://ko-fi.com/quyduongvu) hoặc quét mã VietQR (mọi ngân hàng và ví điện tử):

<img src="docs/images/donate-vietqr.png" alt="VietQR Vietcombank" width="240">

## Giấy phép

GPL-3.0 trở lên. Xem [LICENSE](LICENSE).
