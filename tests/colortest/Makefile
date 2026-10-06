## Project test màu: producer nào mất màu, pipe có giữ byte ESC không

.PHONY: naive smart smart-force progress

## Producer ngây thơ: luôn in ANSI
naive:
	@python colors.py

## Producer kiểu pytest: tự tắt màu khi bị pipe
smart:
	@python smart.py

## Ép producer kiểu pytest phải in màu
smart-force:
	@set FORCE_COLOR=1&& python smart.py

## Progress bar dùng \r (ghi đè dòng)
progress:
	@python -c "import sys,time; [exec('sys.stdout.write(f\"\\rLoading {i*20}%%\");sys.stdout.flush();time.sleep(0.2)') for i in range(6)]; print(); print('done')"
