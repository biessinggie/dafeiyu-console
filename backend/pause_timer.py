# -*- coding: utf-8 -*-
"""暂停计时器：到点自动把看门狗拉起来

被 console_server 用 pythonw 启动，参数是目标时间戳。
"""
import json
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PAUSE = ROOT / "data" / "pause.json"
PYW = r"C:\Program Files\Python38\pythonw.exe"


def read_until():
    try:
        return float(json.loads(PAUSE.read_text(encoding="utf-8")).get("until", 0))
    except Exception:
        return 0.0


def write_until(ts):
    try:
        PAUSE.parent.mkdir(parents=True, exist_ok=True)
        PAUSE.write_text(json.dumps({"until": ts}), encoding="utf-8")
    except Exception:
        pass


def main():
    target = float(sys.argv[1]) if len(sys.argv) > 1 else 0.0
    if target <= time.time():
        sys.exit(0)

    while True:
        left = target - time.time()
        if left <= 0:
            break
        time.sleep(min(20, max(1, left)))
        # 被取消或改时间了
        if abs(read_until() - target) > 1:
            sys.exit(0)

    # 到点了：清标记，拉起看门狗
    write_until(0)
    try:
        subprocess.Popen([PYW, str(ROOT / "watchdog.py")], cwd=str(ROOT))
    except Exception:
        pass


if __name__ == "__main__":
    main()
