"""Producer NGUY HIỂM (giống pytest): chỉ in màu khi stdout là terminal thật,
trừ khi có FORCE_COLOR=1 hoặc PY_COLORS=1."""
import os
import sys

use_color = sys.stdout.isatty() or os.environ.get("FORCE_COLOR") == "1" or os.environ.get("PY_COLORS") == "1"

def c(code, text):
    return f"\x1b[{code}m{text}\x1b[0m" if use_color else text

print(f"isatty={sys.stdout.isatty()} color={'ON' if use_color else 'OFF'}")
print("test_imports.py " + c("32", ".") + "  [100%]")
print(c("32", "1 passed") + " in 0.79s")
