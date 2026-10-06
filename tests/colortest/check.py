"""Mô phỏng đúng tầng truyền data của Makelazy (MakeSession):
cmd.exe + redirect stdout/stderr qua pipe, đếm byte ESC còn sống."""
import subprocess
import sys

cmd = sys.argv[1]
p = subprocess.run(
    ["cmd.exe", "/d", "/s", "/c", f"chcp 65001 >nul & {cmd}"],
    capture_output=True, text=True, encoding="utf-8", errors="replace",
)
out = p.stdout + p.stderr
print(f"exit={p.returncode} bytes={len(out)} ESC={out.count(chr(27))}")
print("--- output thô (repr 300 ky tu dau) ---")
print(repr(out[:300]))
