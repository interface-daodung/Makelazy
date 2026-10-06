# Makelazy — Makefile Runner (Native WPF)

> Nhánh `native-wpf`: UI WPF thuần, **không WebView2**, không cần Runtime gì thêm.
> Nhánh `main`: bản WebView2 + React/xterm.

Mở file tên `Makefile` (không đuôi) → hiện các target thành nút ▶ → bấm chạy trong process ẩn → stream output ra tab terminal (render ANSI bằng RichTextBox).

## Stack
- C# / .NET 10, WPF thuần (kéo co dãn, sáng/tối, tab bo tròn)
- Backend: `MakefileParser` (target/deps/recipe/`##` desc/`.PHONY`), `MakeSession` (`cmd /c make`, fallback chạy recipe khi chưa cài make, ép `FORCE_COLOR` để tool nhả màu), `AnsiWriter` (render ANSI), `CliOptions`, `FileAssociation`, `SingleInstance`, `ThemeStore`

## Build & chạy
```powershell
dotnet build Makelazy.slnx -c Release
# chạy: src/Makelazy.App/bin/Release/net10.0-windows/Makelazy.exe [path/Makefile]
```

## CLI / Open-With
```powershell
Makelazy.exe "C:\proj\Makefile"            # Open-With / kéo-thả / double-click sau khi --register
Makelazy.exe C:\proj --target build        # mở file + tự chạy target
Makelazy.exe C:\proj\Makefile --list       # in targets ra console, không mở GUI
Makelazy.exe --register | --unregister     # ghim/gỡ Open-With (HKCU, không cần admin)
Makelazy.exe --help | --version
```

## Dùng
- 📂 Mở Makefile / kéo-thả file / `Makelazy.exe Makefile`
- Tìm target → ▶ → mỗi lần chạy = 1 tab terminal riêng (bo tròn góc trên), gõ stdin ở ô dưới, ■ stop, ✕ đóng tab
- 🌙/☀️ đổi theme sáng/tối (nhớ lựa chọn vào %AppData%\Makelazy)
- 📌 ghim Open-With để double-click file Makefile
- Demo màu: mở `tests/colortest/Makefile` (target `naive`/`smart`/`progress`)
