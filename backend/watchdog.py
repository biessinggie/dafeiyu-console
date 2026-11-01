# -*- coding: utf-8 -*-
"""看门狗：bot.py 万一退出了，自动重开，并记录退出原因"""
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path

HERE = Path(__file__).resolve().parent
PY = r"C:\Program Files\Python38\python.exe"
LOG = HERE / "data" / "watchdog.log"


def log(msg):
    line = "[%s] %s" % (datetime.now().strftime("%m-%d %H:%M:%S"), msg)
    try:
        print(line, flush=True)
    except Exception:
        pass
    try:
        LOG.parent.mkdir(parents=True, exist_ok=True)
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(line + "\n")
    except Exception:
        pass


def main():
    log("看门狗启动")
    while True:
        started = time.time()
        try:
            p = subprocess.Popen(
                [PY, str(HERE / "bot.py")],
                cwd=str(HERE),
                stdout=subprocess.DEVNULL,
                stderr=subprocess.PIPE,
                creationflags=0x08000000,  # CREATE_NO_WINDOW：不弹黑窗口
            )
            log("启动 bot.py (pid=%d)" % p.pid)
            _, err = p.communicate()
            code = p.returncode
            ran = time.time() - started
            if err:
                tail = err.decode("utf-8", "replace").strip().splitlines()[-12:]
                for line in tail:
                    log("  错误: " + line[:180])
            log("bot 退出，code=%s，运行了 %.0f 秒" % (code, ran))
        except Exception as e:  # noqa: BLE001
            log("看门狗异常：%s" % str(e)[:120])
        time.sleep(5)


if __name__ == "__main__":
    main()
