# Football Manager 27 Editor

[English](README.md) | Tiếng Việt

Editor dùng khi đang chơi Football Manager 27. App sửa cầu thủ, nhân sự, CLB, quốc gia và nhiều thứ khác ngay lúc game
đang chạy, bằng cách đọc và ghi bộ nhớ của `fm.exe` (module `game_plugin.dll`). Chạy được với cả bản Steam và Xbox / Game
Pass.

Chỉ dùng cho save chơi một mình, offline. Không dùng khi chơi online hay với save chung.

## App làm được gì

| Mục | Sửa được |
|---|---|
| Cầu thủ | Chỉ số, vị trí, tính cách, CA/PA, danh tiếng, chiều cao, ngày sinh, quốc tịch và thông tin khác, hợp đồng và hợp đồng tương lai, thưởng và điều khoản chuyển nhượng, kế hoạch sự nghiệp, quan hệ, ngôn ngữ, động tác ưa thích, chấn thương, án treo giò, bất mãn, triệu tập đội tuyển |
| Thao tác cầu thủ | Chuyển CLB (có phí và % bán lại), giải phóng hợp đồng, đội trưởng / đội phó, chép chỉ số sang cầu thủ khác, so sánh |
| Nhân sự | Chỉ số, CA/PA, tính cách, hợp đồng, kế hoạch sự nghiệp, quan hệ, ngôn ngữ, hiểu biết, sơ đồ, xu hướng, chuyển sang CLB khác |
| CLB | Thông tin, tài chính, nợ, CLB liên kết, tầm nhìn CLB, quan hệ, nhân sự, đội hình, trung tâm chuyển nhượng |
| Quốc gia, sân, thành phố, giải đấu, giải thưởng | Thông tin, danh tiếng và thiết lập, mỗi loại một màn riêng |
| Công cụ | Sửa hàng loạt có xem trước và Undo, preset cả đội (hồi phục, thể lực, tinh thần, xoá án treo giò, xoá bất mãn, chỉ số tối đa, chặn giải nghệ), khoá giá trị, xuất CSV / JSON, bộ lọc |

Mọi thay đổi được ghi vào lịch sử, có Undo. Xem [CHANGELOG.md](CHANGELOG.md) để biết bản mới có gì.

Đọc toàn bộ cầu thủ trong dữ liệu game (khoảng 50.000 người) mất chưa tới 1 giây.

Trước mỗi lần ghi, app kiểm tra:
- object ở địa chỉ đó vẫn còn là cầu thủ;
- giá trị trong bộ nhớ vẫn bằng giá trị app đã đọc.

Sau khi ghi, app đọc lại để xác nhận. Nếu có gì không khớp thì app không ghi gì cả và báo lỗi.

## Yêu cầu

- Windows 10/11 x64
- Football Manager 27 `27.0.0+2493426` trên Steam hoặc Xbox / Game Pass (bản Steam `27.0.0+2486695` cũng chạy). Bản game
  khác cần profile offset mới, xem [Khi game có patch](#khi-game-có-patch).
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
kết nối mạng và không đòi quyền administrator. App chỉ ghi file thiết lập của chính nó trong `%LOCALAPPDATA%m27editor`
(giá trị đang khoá, bộ lọc đã lưu) và file xuất khi bạn yêu cầu.

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

Còn nhiều lệnh khác (nhân sự, CLB, quốc gia, sửa hàng loạt, preset, giải phóng, đội trưởng, chuyển nhân sự), liệt kê ở đầu
`src/Fm27Editor.Tools/Program.cs`.

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

Với bản Xbox / Game Pass, `re/tools/gamepass-rva-map.ps1` dựng profile từ profile Steam bằng cách khớp tên lớp (RTTI),
mất vài giây.

## Lộ trình

- Chuyển cầu thủ theo ngày và cho mượn, có thêm dòng lịch sử sự nghiệp, giống FMRTE.
- Áo đấu và chiến thuật CLB, luật và tiền thưởng giải đấu, thoả thuận giữa các quốc gia.
- Danh sách theo dõi, yêu thích, theo dõi tiến bộ cầu thủ.
- Exe nhẹ hơn, mở nhanh hơn.

## Miễn trừ trách nhiệm

Dự án này không liên kết với và không được Sports Interactive hay SEGA bảo trợ. Football Manager là thương hiệu của họ.
Sửa bộ nhớ game có thể làm hỏng save, hãy giữ bản sao lưu. Bạn tự chịu rủi ro khi sử dụng.

## Ủng hộ

Nếu editor có ích, bạn có thể ủng hộ qua [Ko-fi](https://ko-fi.com/quyduongvu) hoặc chuyển khoản ngân hàng Việt Nam (VietQR, Vietcombank), quét được bằng mọi app ngân hàng và ví điện tử:

<img src="docs/images/donate-vietqr.png" alt="VietQR, Vietcombank" width="220">

## Giấy phép

GPL-3.0 trở lên. Xem [LICENSE](LICENSE).
