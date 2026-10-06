# Makelazy — Makefile Runner (WPF + WebView2 + React/xterm)

> **Nhánh `native-wpf` (bạn đang ở đây): UI WPF thuần, KHÔNG WebView2.**
> Mở file `Makefile` → nút ▶ WPF thật → chạy process ẩn → tab terminal render ANSI bằng RichTextBox.
> Nhánh `main`: bản WebView2 + React/xterm (đẹp hơn nhưng cần WebView2 Runtime + cầu nối JS/C#).

Mở file tên `Makefile` (không đuôi) → hiện các target thành nút ▶ → bấm chạy trong process ẩn → stream output ra tab terminal đẹp (xterm.js).

## Stack
- C# / .NET 10, WPF (kéo co dãn), WebView2
- Frontend: React + TS + Vite + Tailwind + xterm.js (`frontend/`, build ra `src/Makelazy.App/wwwroot/`)
- Backend: `MakefileParser` (parse target/deps/recipe/## desc/.PHONY), `MakeSession` (`cmd /c make`, CreateNoWindow, redirect stdout/stderr/stdin)

## Chạy dev
```powershell
cd frontend; npm install; npm run dev     # vite :5173
$env:MAKELAZY_DEV_URL="http://localhost:5173"; dotnet run --project src/Makelazy.App
```

## Build release
```powershell
cd frontend; npm install; npm run build    # -> src/Makelazy.App/wwwroot
dotnet build Makelazy.sln -c Release
# chạy: src/Makelazy.App/bin/Release/net10.0-windows/Makelazy.exe [path/Makefile]
```

## CLI / Open-With (mở file Makefile bằng app)
```powershell
Makelazy.exe "C:\proj\Makefile"            # Open-With / kéo-thả / double-click sau khi --register
Makelazy.exe C:\proj --target build        # mở file + tự chạy target
Makelazy.exe C:\proj\Makefile --list       # in targets ra console, không mở GUI
Makelazy.exe --register                    # ghim: double-click file không-đuôi + menu chuột phải thư-mục (HKCU, không cần admin)
Makelazy.exe --unregister                 # gỡ ghim
Makelazy.exe --help | --version
```
- Mở app trong UI rồi bấm 📌 **Ghim Open-With** cũng tương đương `--register`.
- App single-instance: double-click file thứ 2 khi app đang mở sẽ nạp vào cửa sổ hiện tại, không mở thêm.
- Giao thức WebView2 thêm: JS→C# `assocStatus | registerAssoc | unregisterAssoc`, C#→JS `assoc{registered}`.

## Dùng
- Mở app → 📂 Mở Makefile / kéo-thả file / `Makelazy.exe Makefile`
- Tìm target → ▶ → mỗi lần chạy = 1 tab terminal bên phải, gõ stdin trực tiếp, ■ stop, ✕ đóng tab.
- Cần GNU Make trong PATH (vd: `choco install make` hoặc dùng MinGW/MSYS2).

## Giao thức WebView2 (PostWebMessageAsJson)
- JS→C#: `ready | openMakefile | run{target} | kill{sessionId} | input{sessionId,data} | assocStatus | registerAssoc | unregisterAssoc`
- C#→JS: `targets{file,targets[]} | session-started{sessionId,target} | output{sessionId,data} | exit{sessionId,code} | error{message} | assoc{registered}`
