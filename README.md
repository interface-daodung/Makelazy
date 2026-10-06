# Makelazy

> Mở file `Makefile`, bấm ▶ chạy target, xem output màu ANSI ngay trên Windows — không cần cấu hình gì.

![Windows](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF-512BD4)
![License](https://img.shields.io/badge/License-MIT-green)
![Dependencies](https://img.shields.io/badge/NuGet-0-important)

**Makelazy** là một GUI nhỏ gọn cho GNU Make trên Windows, viết bằng C# / WPF thuần (không WebView2). Bạn mở một file `Makefile` → các target hiện thành nút **▶** → chạy trong process nền, output được stream ra **tab terminal** riêng biệt với đầy đủ màu ANSI.

Nếu máy **chưa cài GNU Make**, Makelazy tự fallback: chạy thẳng *recipe* của target qua `cmd` — vẫn dùng được ngay. Có cài make thì dùng make thật (qua `FORCE_COLOR` để tool nhả màu).

---

## Tính năng

- 📂 Mở `Makefile` bằng mở file / kéo-thả / Open-With / CLI.
- ▶ Hiển thị target (đọc cả deps, recipe, mô tả `##` và `.PHONY`) thành nút bấm.
- 🖥 Mỗi lần chạy = **1 tab terminal** riêng (bo tròn góc trên), có ô gõ **stdin**, nút **■ stop**, nút **✕ đóng tab**.
- 🎨 Render **ANSI màu** bằng `RichTextBox` (không cần xterm/webview).
- 🌙 / ☀️ Theme sáng/tối, nhớ lựa chọn vào `%AppData%\Makelazy`.
- 📌 Ghim **Open-With** cho file `Makefile` (HKCU, không cần admin) → double-click là chạy.
- 🔄 **Single instance** — mở file mới vào cùng một cửa sổ.
- 🧩 **Không phụ thuộc NuGet package nào** — chỉ dùng framework sẵn có của .NET.

## Yêu cầu

| Dùng app | Build từ source |
|---|---|
| Windows 10/11 (x64) | .NET 10 **SDK** |
| .NET 10 **Desktop Runtime** (installer tự kiểm tra & nhắc tải) | — |
| GNU Make *(tuỳ chọn)* — thiếu thì Makelazy tự fallback chạy recipe | — |

## Cài đặt

### Cách 1 — Dùng installer (khuyến nghị)

Tải `Makelazy-Setup-<version>.exe` từ trang [Releases](../../releases). Setup sẽ:

- Cài per-user vào `%LocalAppData%\Makelazy` (không cần admin).
- Tự đóng app đang chạy khi cài (`CloseApplications`).
- Kiểm tra **.NET 10 Desktop Runtime**; thiếu thì mở trang tải và dừng.
- Tự chạy `--register --silent` để ghim Open-With; lúc gỡ cài đặt thì tự `--unregister`.

### Cách 2 — Chạy từ source

Xem phần [Build từ source](#build-từ-source) bên dưới.

## Build từ source

```powershell
dotnet build Makelazy.slnx -c Release
# chạy:
src\Makelazy.App\bin\Release\net10.0-windows\Makelazy.exe [đường-dẫn-Makefile]
```

## Sử dụng

- Mở file `Makefile` (kéo-thả vào cửa sổ / double-click sau khi `--register`).
- Chọn target → bấm **▶**. Mỗi lần chạy là một tab terminal riêng.
- Gõ stdin ở ô dưới tab, **■** để dừng, **✕** để đóng tab.
- Đổi sáng/tối bằng nút 🌙/☀️.
- Muốn xem demo màu ANSI: mở `tests/colortest/Makefile` và chạy các target `naive` / `smart` / `progress`.

## Dòng lệnh (CLI)

```powershell
Makelazy.exe "C:\proj\Makefile"           # Open-With / kéo-thả / double-click
Makelazy.exe C:\proj --target build       # mở thư mục + tự chạy target
Makelazy.exe C:\proj\Makefile --list      # in targets ra console, không mở GUI
Makelazy.exe --register                   # ghim Open-With (HKCU)
Makelazy.exe --unregister                 # gỡ ghim Open-With
Makelazy.exe --help                       # trợ giúp
Makelazy.exe --version                    # phiên bản
```

> Truyền một **thư mục** cũng được: Makelazy tự tìm `Makefile` / `GNUmakefile` / `makefile` bên trong.

## Đóng gói installer (Inno Setup 6)

```powershell
dotnet build Makelazy.slnx -c Release
iscc installer\Makelazy.iss
# Kết quả: dist\Makelazy-Setup-0.1.0.exe
```

Đổi version: sửa **đồng thời** `Version` trong `src/Makelazy.App/Makelazy.App.csproj` và `MyAppVersion` trong `installer/Makelazy.iss`.

## Kiến trúc & thư mục

```
src/Makelazy.App/
├── MainWindow.xaml(.cs)      # UI chính: danh sách target + các tab terminal
├── Services/
│   ├── MakefileParser.cs     # parse target / deps / recipe / mô tả ## / .PHONY
│   ├── MakeSession.cs        # cmd /c make (hoặc fallback recipe), ép FORCE_COLOR
│   ├── AnsiWriter.cs         # render ANSI thành RichTextBox
│   ├── CliOptions.cs         # parse CLI
│   ├── FileAssociation.cs    # ghim/gỡ Open-With (HKCU)
│   ├── SingleInstance.cs     # chống chạy 2 instance
│   └── ThemeStore.cs         # lưu theme vào %AppData%
installer/Makelazy.iss        # script Inno Setup
tests/colortest/              # Makefile demo màu ANSI
Makefile.sample               # đừng nhầm: đây chỉ là file mẫu để test Makelazy 🙂
```

### Thư viện sử dụng

- **0 package NuGet** (`dotnet list package` rỗng).
- Chỉ dùng framework: `Microsoft.NET.Sdk` + `UseWPF=true` (`PresentationFramework`, `WindowsBase`…), registry qua `Microsoft.Win32`, pipe qua `System.IO.Pipes`. Icon: `MakeLazy.ico`.

## Nhánh repo

- **`native-wpf`** (mặc định): UI WPF thuần, không WebView2, không cần runtime thêm.
- **`main`**: bản cũ dùng WebView2 + React/xterm — chỉ để tham khảo, không phát triển tiếp.

## Đóng góp

Mọi đóng góp đều chào đón! Cách nhanh nhất:

1. Fork repo rồi tạo branch mới.
2. Sửa code, chạy thử với `Makefile.sample` / `tests/colortest/Makefile`.
3. Mở **Pull Request** — nhớ mô tả ngắn gọn thay đổi.

Nếu gặp lỗi hoặc có ý tưởng, tạo **Issue** trên GitHub.

## Giấy phép

[MIT](./LICENSE) — xem file `LICENSE` để biết chi tiết.