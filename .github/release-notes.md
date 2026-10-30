## Which file do I download?

| File | For |
|---|---|
| **`Fm27Editor.exe`** | **The editor app. Most people only need this one.** |
| `Fm27Editor.Tools.exe` | Command-line tools for advanced users: `players`, `set`, `verify`, and `find-offsets` to support a new game build after an FM27 patch. |

Each exe runs on its own: no install, no .NET needed. Windows 10/11 x64, Football Manager 27 build `27.0.0+2486695`.

## Tải file nào?

| File | Dùng để |
|---|---|
| **`Fm27Editor.exe`** | **App editor. Đa số người dùng chỉ cần file này.** |
| `Fm27Editor.Tools.exe` | Công cụ dòng lệnh cho người dùng nâng cao: `players`, `set`, `verify`, và `find-offsets` để hỗ trợ bản game mới sau khi FM27 có patch. |

Mỗi file exe chạy độc lập: không cần cài đặt, không cần .NET. Windows 10/11 x64, Football Manager 27 build `27.0.0+2486695`.

## Verify / Kiểm chứng

Built by GitHub Actions from commit `{SHA}` (tag `{TAG}`). Compare hashes with `SHA256SUMS.txt`, or check the signed build attestation:

Được GitHub Actions build từ commit `{SHA}` (tag `{TAG}`). So hash với `SHA256SUMS.txt`, hoặc kiểm chứng thực build có chữ ký:

```
gh attestation verify Fm27Editor.exe --repo Jackhamerp24/Football-Manager-27-Editor
```
