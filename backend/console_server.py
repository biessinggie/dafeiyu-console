# -*- coding: utf-8 -*-
"""控制台后端：本地 HTTP 服务，给网页 UI 提供数据与操作

只监听 127.0.0.1，端口 8771。
"""
import json
import os
import shutil
import subprocess
import sys
import threading
import time
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parent
UI = ROOT / "ui"
PY = r"C:\Program Files\Python38\python.exe"
PYW = r"C:\Program Files\Python38\pythonw.exe"
PORT = 8771

MODE_TEXT = {("priv", "group"): "都回", ("priv",): "只私聊",
             ("group",): "只群聊", (): "都不回"}


def sh(cmd, timeout=20):
    try:
        return subprocess.run(cmd, capture_output=True, text=True,
                              errors="ignore", encoding="utf-8", timeout=timeout,
                              creationflags=0x08000000).stdout or ""
    except Exception:
        return ""


_CACHE = {}
_TTL = 2.5


def cached(key, fn):
    now = time.time()
    hit = _CACHE.get(key)
    if hit and now - hit[0] < _TTL:
        return hit[1]
    val = fn()
    _CACHE[key] = (now, val)
    return val


def procs(name):
    def run():
        out = sh(["tasklist", "/FI", "IMAGENAME eq %s" % name, "/FO", "CSV", "/NH"])
        return name.lower() in out.lower() and "no tasks" not in out.lower()
    return cached("proc:" + name, run)


def cmdlines():
    return cached("cmdlines", lambda: sh(
        ["wmic", "process", "where",
         "name='python.exe' or name='pythonw.exe'", "get", "CommandLine"]))


def bot_running():
    return "bot.py" in cmdlines()


def watchdog_running():
    return "watchdog.py" in cmdlines()


def port_connected():
    def run():
        for line in sh(["netstat", "-ano"]).splitlines():
            if ":3001" in line and "ESTABLISHED" in line.upper():
                return True
        return False
    return cached("port3001", run)


def her_state():
    try:
        d = json.loads(urllib.request.urlopen(
            "http://127.0.0.1:8765/state", timeout=2).read().decode("utf-8"))
        raw = (d.get("raw") or d.get("text") or "").strip()
        return raw, d.get("doing") or ""
    except Exception:
        return "", ""


def cfg():
    try:
        return json.loads((ROOT / "config.json").read_text(encoding="utf-8"))
    except Exception:
        return {"bot": {}}


def ui_cfg():
    try:
        return json.loads((ROOT / "data" / "ui.json").read_text(encoding="utf-8"))
    except Exception:
        return {"budget": 1.0, "notify": True}


def save_ui(d):
    try:
        (ROOT / "data" / "ui.json").write_text(
            json.dumps(d, ensure_ascii=False, indent=2), encoding="utf-8")
    except Exception:
        pass


def read_mode(c=None):
    b = (c or cfg()).get("bot", {})
    on = []
    if b.get("private_enabled", True):
        on.append("priv")
    if b.get("reply_group", True):
        on.append("group")
    return tuple(on)


def state():
    c = cfg()
    words, doing = her_state()
    usage = {"today": 0.0, "total": 0.0, "turns": 0}
    up = ROOT / "data" / "usage.json"
    if up.exists():
        try:
            d = json.loads(up.read_text(encoding="utf-8"))
            usage = {"today": d.get("yuan", 0), "total": d.get("yuan_total", 0),
                     "turns": d.get("turns", 0)}
        except Exception:
            pass
    mood, saved, msgs = 0.0, 0, 0
    try:
        import sqlite3
        gp = ROOT / "data" / "group-memory.db"
        if gp.exists():
            db = sqlite3.connect(str(gp))
            row = db.execute("SELECT mood FROM state WHERE chat='g:924922709'").fetchone()
            mood = float(row[0]) if row and row[0] is not None else 0.0
            saved = db.execute("SELECT COUNT(*) FROM saved_posts").fetchone()[0]
            msgs = db.execute("SELECT COUNT(*) FROM messages").fetchone()[0]
    except Exception:
        pass
    days = []
    dp = ROOT / "data" / "usage-days.json"
    if dp.exists():
        try:
            dd = json.loads(dp.read_text(encoding="utf-8"))
            import datetime as dtm
            today = dtm.date.today()
            for i in range(6, -1, -1):
                k = (today - dtm.timedelta(days=i)).isoformat()
                days.append({"date": k[5:], "yuan": float((dd.get(k) or {}).get("yuan", 0))})
        except Exception:
            days = []
    from concurrent.futures import ThreadPoolExecutor
    with ThreadPoolExecutor(max_workers=5) as ex:
        f_napcat = ex.submit(procs, "NapCatWinBootMain.exe")
        f_bot = ex.submit(bot_running)
        f_watch = ex.submit(watchdog_running)
        f_conn = ex.submit(port_connected)
        f_pet = ex.submit(procs, "BigBlueFish.exe")
        pr = {"napcat": f_napcat.result(), "bot": f_bot.result(),
              "watch": f_watch.result(), "conn": f_conn.result(),
              "pet": f_pet.result()}

    return {
        "online": pr["conn"] and pr["bot"],
        "mode": MODE_TEXT.get(read_mode(c), "未知"),
        "words": words, "doing": doing, "mood": mood,
        "today": usage["today"], "total": usage["total"],
        "turns": usage["turns"], "saved": saved, "msgs": msgs,
        "budget": float(ui_cfg().get("budget", 1.0)),
        "paused_until": paused_until(),
        "procs": pr,
        "days": days,
        "avatars": sorted(p.name for p in (ROOT / "头像").glob("Q版-*.png")),
        "settings": {
            "mode": ("both" if len(read_mode(c)) == 2 else
                     ("priv" if read_mode(c) == ("priv",) else "group")),
            "moments": bool((c.get("bot", {}).get("moments") or {}).get("enabled", True)),
            "tieba": bool((c.get("bot", {}).get("tieba") or {}).get("enabled", True)),
            "miss": bool((c.get("bot", {}).get("miss") or {}).get("enabled", True)),
            "proactive": bool(c.get("bot", {}).get("proactive", True)),
            "quiet": bool(c.get("bot", {}).get("quiet", True)),
            "amb": int(float(c.get("bot", {}).get("group_ambient_prob", 0.06)) * 100),
            "budget": float(ui_cfg().get("budget", 1.0)),
        },
    }


def log_text(f="all"):
    p = ROOT / "data" / "bot.log"
    if not p.exists():
        return ["还没有日志"]
    try:
        lines = p.read_text(encoding="utf-8", errors="ignore").splitlines()[-400:]
    except Exception as e:  # noqa: BLE001
        return [str(e)]
    if f == "group":
        lines = [l for l in lines if "群" in l or "gid=" in l]
    elif f == "priv":
        lines = [l for l in lines if "私聊" in l]
    out, last, dup = [], None, 0
    for l in lines:
        key = l.split("]")[-1][:26] if "]" in l else l[:26]
        if key == last:
            dup += 1
            continue
        if dup >= 2:
            out.append("      ⋯ 同样内容重复 %d 次" % (dup + 1))
        dup = 0
        last = key
        out.append(l[:150])
    if dup >= 2:
        out.append("      ⋯ 同样内容重复 %d 次" % (dup + 1))
    return out[-90:]


def chat_items():
    import sqlite3
    for db in ["data/group-memory.db", "data/companion.db"]:
        p = ROOT / db
        if not p.exists():
            continue
        try:
            g = sqlite3.connect(str(p))
            rows = g.execute(
                "SELECT role, content FROM messages WHERE chat LIKE 'g:%' "
                "ORDER BY id DESC LIMIT 100").fetchall()
            if not rows:
                rows = g.execute(
                    "SELECT role, content FROM messages WHERE chat LIKE 'p:%' "
                    "ORDER BY id DESC LIMIT 100").fetchall()
            items = [{"role": r, "text": " ".join((c or "").split())[:200]}
                     for r, c in rows[::-1]]
            if items:
                return items
        except Exception:
            continue
    return []


def memories(which="group"):
    import sqlite3
    p = ROOT / ("data/companion.db" if which == "priv" else "data/group-memory.db")
    if not p.exists():
        p = ROOT / "data" / "companion.db"
    try:
        g = sqlite3.connect(str(p))
        rows = g.execute("SELECT id, kind, text FROM memories ORDER BY id DESC LIMIT 200").fetchall()
        return [{"id": i, "kind": k, "text": t} for i, k, t in rows]
    except Exception:
        return []


def backups():
    d = Path(os.path.expanduser("~")) / "Documents" / "大肥鱼备份"
    out = []
    if d.exists():
        for x in sorted(d.iterdir(), reverse=True):
            if x.is_dir():
                size = sum(f.stat().st_size for f in x.rglob("*") if f.is_file()) // 1024
                out.append({"name": x.name, "kb": size})
    return out


def cmdlines_now():
    return sh(["wmic", "process", "where",
               "name='python.exe' or name='pythonw.exe'", "get", "CommandLine"], 15)


def procs_now(name):
    """不走缓存，立刻查"""
    out = sh(["tasklist", "/FI", "IMAGENAME eq %s" % name, "/FO", "CSV", "/NH"], 10)
    return name.lower() in out.lower() and "no tasks" not in out.lower()


def wait_gone(fn, timeout=8.0):
    t0 = time.time()
    while time.time() - t0 < timeout:
        if not fn():
            return True
        time.sleep(0.4)
    return False


def _window_titles():
    """枚举所有可见窗口标题"""
    import ctypes
    from ctypes import wintypes
    user32 = ctypes.windll.user32
    out = []

    @ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def cb(hwnd, _):
        if not user32.IsWindowVisible(hwnd):
            return True
        n = user32.GetWindowTextLengthW(hwnd)
        if n:
            buf = ctypes.create_unicode_buffer(n + 1)
            user32.GetWindowTextW(hwnd, buf, n + 1)
            if buf.value:
                out.append(buf.value)
        return True

    user32.EnumWindows(cb, 0)
    return out


def _wait_window(keyword, timeout=4.0):
    """等一个标题里带 keyword 的窗口出现"""
    t0 = time.time()
    low = keyword.lower()
    while time.time() - t0 < timeout:
        try:
            if any(low in t.lower() for t in _window_titles()):
                return True
        except Exception:
            pass
        time.sleep(0.25)
    return False


def open_folder_and_wait(path, keyword):
    try:
        subprocess.Popen(["explorer.exe", str(path)])
    except Exception as e:  # noqa: BLE001
        return "打不开：%s" % e
    if _wait_window(keyword):
        return "已打开 " + keyword
    return "已经叫了资源管理器，但没等到窗口（可能被别的窗口挡住）"


# ── 启动流程（先等 NapCat 出码，再弹二维码）──
import re as _re

STARTUP = {"state": "idle", "msg": "", "ts": 0.0}
NAPCAT_BAT = Path(r"E:\Dafeiyu\NapCat\1-启动NapCat.bat")
NAPCAT_LOG = Path(r"E:\Dafeiyu\NapCat\start.log")


def _qr_url():
    try:
        if not NAPCAT_LOG.exists():
            return ""
        txt = NAPCAT_LOG.read_text(encoding="utf-8", errors="replace")
        hits = _re.findall(r"https://txz\.qq\.com/p\?k=[^\s\"']+", txt)
        return hits[-1] if hits else ""
    except Exception:
        return ""


def _wait_qr(old, timeout=110):
    t0 = time.time()
    while time.time() - t0 < timeout:
        u = _qr_url()
        if u and u != old:
            return u
        time.sleep(2)
    return ""


def _startup_worker():
    try:
        STARTUP.update(state="starting", msg="正在检查她是否在线…", ts=time.time())
        if port_connected():
            STARTUP.update(state="done", msg="她本来就在线，不用扫码", ts=time.time())
            return

        old = _qr_url()
        running = procs("NapCatWinBootMain.exe")

        if not running:
            STARTUP.update(state="starting", msg="正在拉起 NapCat（会弹 UAC，点是）…",
                           ts=time.time())
            if NAPCAT_BAT.exists():
                subprocess.Popen(["cmd", "/c", str(NAPCAT_BAT)],
                                 cwd=str(NAPCAT_BAT.parent),
                                 creationflags=0x00000010)
            t0 = time.time()
            while time.time() - t0 < 70 and not procs("NapCatWinBootMain.exe"):
                time.sleep(1)
            if not procs("NapCatWinBootMain.exe"):
                STARTUP.update(state="failed",
                               msg="NapCat 没起来（看看有没有 UAC 弹窗没点）", ts=time.time())
                return
        else:
            # NapCat 在跑但没登录 → 必须重启它才会吐新码
            STARTUP.update(state="starting",
                           msg="NapCat 在跑但没登录，正在重启它换新码（会弹 UAC）…",
                           ts=time.time())
            try:
                subprocess.Popen([PY, "-c",
                                  "import sys; sys.path.insert(0, r'%s'); import qr_launcher; qr_launcher.relogin('%s')"
                                  % (str(ROOT), old)],
                                 cwd=str(ROOT), creationflags=0x00000010)
            except Exception:
                pass

        STARTUP.update(state="starting", msg="NapCat 启动中，等它出二维码…", ts=time.time())
        url = _wait_qr(old) or _qr_url()
        if not url:
            STARTUP.update(state="failed",
                           msg="等不到二维码：NapCat 可能卡住了", ts=time.time())
            return
        STARTUP.update(state="ready", msg="二维码好了，正在打开…", ts=time.time())
        subprocess.Popen([PY, str(ROOT / "出二维码.py")], cwd=str(ROOT),
                         creationflags=0x00000010)
        time.sleep(1.5)
        subprocess.Popen([PY, str(ROOT / "显示二维码.py")], cwd=str(ROOT),
                         creationflags=0x00000010)
        # 等扫完登录（3001 连上）
        t0 = time.time()
        while time.time() - t0 < 180:
            if port_connected():
                STARTUP.update(state="done", msg="扫码成功，她已经上线", ts=time.time())
                return
            time.sleep(3)
        STARTUP.update(state="done", msg="二维码还在等你扫", ts=time.time())
    except Exception as e:  # noqa: BLE001
        STARTUP.update(state="failed", msg="出错：%s" % e, ts=time.time())


def startup_state():
    return dict(STARTUP)


PAUSE_FILE = ROOT / "data" / "pause.json"


def paused_until():
    try:
        return float(json.loads(PAUSE_FILE.read_text(encoding="utf-8")).get("until", 0))
    except Exception:
        return 0.0


def _write_pause(ts):
    try:
        PAUSE_FILE.parent.mkdir(parents=True, exist_ok=True)
        PAUSE_FILE.write_text(json.dumps({"until": ts}), encoding="utf-8")
    except Exception:
        pass


def _kill_her(with_qq=False):
    sh(["wmic", "process", "where",
        "name='python.exe' and commandline like '%bot.py%'", "call", "terminate"])
    sh(["wmic", "process", "where",
        "name='pythonw.exe' and commandline like '%watchdog.py%'", "call", "terminate"])
    sh(["taskkill", "/IM", "BigBlueFish.exe", "/F"])
    if with_qq:
        sh(["taskkill", "/IM", "NapCatWinBootMain.exe", "/F"])
        try:
            for line in sh(["wmic", "process", "where", "name='QQ.exe'", "get",
                            "ProcessId,ExecutablePath", "/format:csv"], 20).splitlines():
                if "NapCat" in line:
                    pid = line.rstrip().split(",")[-1].strip()
                    if pid.isdigit():
                        sh(["taskkill", "/F", "/PID", pid])
        except Exception:
            pass


def do_pause(until_ts):
    _kill_her()
    _write_pause(until_ts)
    try:
        subprocess.Popen([PYW, str(ROOT / "pause_timer.py"), str(until_ts)],
                         cwd=str(ROOT), creationflags=0x00000010)
    except Exception:
        pass
    left = int((until_ts - time.time()) / 60)
    return "她先休息了，%d 分钟后自己回来" % max(1, left)


def do_resume():
    _write_pause(0)
    if not watchdog_running():
        subprocess.Popen([PYW, str(ROOT / "watchdog.py")], cwd=str(ROOT))
    return "叫她回来了，几秒后上线"


# ── 操作 ──
def act(name):
    if name == "start":
        if not watchdog_running():
            subprocess.Popen([PYW, str(ROOT / "watchdog.py")], cwd=str(ROOT))
        if port_connected() and procs("NapCatWinBootMain.exe"):
            STARTUP.update(state="done", msg="她本来就在线", ts=time.time())
            return "已经在跑了"
        if STARTUP.get("state") == "starting" and time.time() - STARTUP.get("ts", 0) < 150:
            return "正在启动中，稍等"
        threading.Thread(target=_startup_worker, daemon=True).start()
        return "正在启动 NapCat，出码后会自动弹二维码"
    if name == "qr":
        subprocess.Popen([PY, str(ROOT / "qr_launcher.py")], cwd=str(ROOT),
                         creationflags=0x00000010)
        return "正在刷新二维码"
    if name == "restart":
        sh(["wmic", "process", "where",
            "name='python.exe' and commandline like '%bot.py%'", "call", "terminate"])
        if wait_gone(lambda: "bot.py" in cmdlines_now(), 6):
            return "机器人已停，看门狗马上接回来"
        return "已经叫停了，但还没退干净"
    if name in ("pause30", "pause120", "pause_morning"):
        if name == "pause30":
            until = time.time() + 30 * 60
        elif name == "pause120":
            until = time.time() + 120 * 60
        else:
            import datetime as dt
            now = dt.datetime.now()
            t = now.replace(hour=8, minute=0, second=0, microsecond=0)
            if t <= now:
                t += dt.timedelta(days=1)
            until = t.timestamp()
        return do_pause(until)
    if name == "resume":
        return do_resume()
    if name == "stop":
        _write_pause(0)
        _kill_her(with_qq=True)
        wait_gone(lambda: procs_now("python.exe") and "bot.py" in cmdlines_now(), 6)
        return "全部关掉了（连 QQ 一起）"
    if name == "diag":
        cs = [("NapCat", procs("NapCatWinBootMain.exe")), ("机器人", bot_running()),
              ("看门狗", watchdog_running()), ("3001 连接", port_connected())]
        try:
            d = json.loads(urllib.request.urlopen(
                "http://127.0.0.1:8765/state", timeout=3).read().decode("utf-8"))
            cs.append(("机器人接口", bool(d.get("ok"))))
        except Exception:
            cs.append(("机器人接口", False))
        bad = [n for n, ok in cs if not ok]
        return ("全部正常，她在线" if not bad else "异常：" + "、".join(bad))
    if name == "backup":
        dest = Path(os.path.expanduser("~")) / "Documents" / "大肥鱼备份"
        target = dest / time.strftime("%Y%m%d-%H%M")
        target.mkdir(parents=True, exist_ok=True)
        for f in ["data/companion.db", "data/group-memory.db", "config.json",
                  "persona-benzun.md", "persona-group.md", "profile.md",
                  "profile-group.md"]:
            if (ROOT / f).exists():
                shutil.copy2(ROOT / f, target / Path(f).name)
        return "已备份到 " + target.name
    if name == "export":
        dest = Path(os.path.expanduser("~")) / "Documents" / "大肥鱼导出"
        dest.mkdir(parents=True, exist_ok=True)
        zp = dest / ("大肥鱼资料-%s.zip" % time.strftime("%Y%m%d-%H%M"))
        import zipfile
        n = 0
        with zipfile.ZipFile(zp, "w", zipfile.ZIP_DEFLATED) as z:
            for f in ["persona-benzun.md", "persona-group.md", "profile.md",
                      "profile-group.md"]:
                if (ROOT / f).exists():
                    z.write(ROOT / f, f)
            try:
                d = json.loads((ROOT / "config.json").read_text(encoding="utf-8"))
                d["api"]["api_key"] = "已抹掉"
                if "vision" in d["bot"]:
                    d["bot"]["vision"]["api_key"] = "已抹掉"
                z.writestr("config.json", json.dumps(d, ensure_ascii=False, indent=2))
            except Exception:
                pass
            for f in ["data/companion.db", "data/group-memory.db",
                      "data/usage.json", "data/usage-days.json"]:
                if (ROOT / f).exists():
                    z.write(ROOT / f, Path(f).name)
            if (ROOT / "stickers").exists():
                for f in (ROOT / "stickers").rglob("*"):
                    if f.is_file():
                        z.write(f, str(Path("stickers") / f.relative_to(ROOT / "stickers")))
                        n += 1
        return "已导出 %s（%d 张表情）" % (zp.name, n)
    if name == "mode":
        order = [("group",), ("priv",), ("priv", "group")]
        cur = read_mode()
        try:
            nxt = order[(order.index(cur) + 1) % len(order)]
        except ValueError:
            nxt = ("group",)
        p = ROOT / "config.json"
        d = cfg()
        d["bot"]["private_enabled"] = "priv" in nxt
        d["bot"]["reply_group"] = "group" in nxt
        p.write_text(json.dumps(d, ensure_ascii=False, indent=2), encoding="utf-8")
        act("restart")
        return "模式切换到：" + MODE_TEXT.get(nxt, "?")
    if name == "folder":
        return open_folder_and_wait(ROOT, ROOT.name)
    if name == "stickers":
        return open_folder_and_wait(ROOT / "stickers", "stickers")
    if name == "open_export":
        d = Path(os.path.expanduser("~")) / "Documents" / "大肥鱼导出"
        d.mkdir(parents=True, exist_ok=True)
        return open_folder_and_wait(d, "大肥鱼导出")
    if name == "open_backup":
        d = Path(os.path.expanduser("~")) / "Documents" / "大肥鱼备份"
        d.mkdir(parents=True, exist_ok=True)
        return open_folder_and_wait(d, "大肥鱼备份")
    if name == "restore_latest":
        bs = backups()
        if not bs:
            return "还没有备份"
        src = Path(os.path.expanduser("~")) / "Documents" / "大肥鱼备份" / bs[0]["name"]
        tmp = src.parent / ("还原前-" + time.strftime("%Y%m%d-%H%M%S"))
        tmp.mkdir(parents=True, exist_ok=True)
        for f in ["data/companion.db", "data/group-memory.db", "config.json",
                  "persona-benzun.md", "persona-group.md"]:
            if (ROOT / f).exists():
                shutil.copy2(ROOT / f, tmp / Path(f).name)
        for f in ["companion.db", "group-memory.db"]:
            if (src / f).exists():
                shutil.copy2(src / f, ROOT / "data" / f)
        for f in ["config.json", "persona-benzun.md", "persona-group.md",
                  "profile.md", "profile-group.md"]:
            if (src / f).exists():
                shutil.copy2(src / f, ROOT / f)
        act("restart")
        return "已还原 " + bs[0]["name"]
    if name == "quit":
        subprocess.Popen(["taskkill", "/IM", "msedge.exe", "/F"])
        threading.Timer(0.5, lambda: os._exit(0)).start()
        return "控制台关闭"
    return "未知操作"


def say(text):
    try:
        body = json.dumps({"text": text, "token": "dafeiyu"}).encode("utf-8")
        req = urllib.request.Request(
            "http://127.0.0.1:8765/say", data=body,
            headers={"Content-Type": "application/json"}, method="POST")
        urllib.request.urlopen(req, timeout=8).read()
        return True
    except Exception:
        return False


def save_settings(payload):
    p = ROOT / "config.json"
    d = cfg()
    b = d.setdefault("bot", {})
    m = payload.get("mode", "group")
    b["private_enabled"] = m in ("both", "priv")
    b["reply_group"] = m in ("both", "group")
    for key in ["moments", "tieba", "miss"]:
        node = b.get(key)
        if isinstance(node, dict):
            node["enabled"] = bool(payload.get(key, True))
        elif key in payload:
            b[key] = bool(payload[key])
    for key in ["proactive", "quiet"]:
        if key in payload:
            b[key] = bool(payload[key])
    if "amb" in payload:
        try:
            b["group_ambient_prob"] = max(0.0, min(0.5, float(payload["amb"]) / 100.0))
        except Exception:
            pass
    p.write_text(json.dumps(d, ensure_ascii=False, indent=2), encoding="utf-8")
    if "budget" in payload:
        u = ui_cfg()
        try:
            u["budget"] = max(0.01, float(payload["budget"]))
        except Exception:
            pass
        save_ui(u)
    act("restart")
    return "已保存并重启"


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def _send(self, obj, code=200, ctype="application/json; charset=utf-8"):
        body = obj if isinstance(obj, bytes) else json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        try:
            self.wfile.write(body)
        except Exception:
            pass

    def do_GET(self):
        path = self.path.split("?")[0]
        q = {}
        if "?" in self.path:
            for kv in self.path.split("?", 1)[1].split("&"):
                if "=" in kv:
                    k, v = kv.split("=", 1)
                    q[k] = v
        if path == "/api/state":
            return self._send(state())
        if path == "/api/logs":
            f = q.get("filter", "all")
            lp = ROOT / "data" / "bot.log"
            size = lp.stat().st_size if lp.exists() else 0
            mark = "%s:%s" % (f, size)
            key = "logtext:" + mark
            if key in _CACHE and time.time() - _CACHE[key][0] < 1.5:
                return self._send({"lines": _CACHE[key][1], "size": size})
            lines = log_text(f)
            _CACHE[key] = (time.time(), lines)
            return self._send({"lines": lines, "size": size})
        if path == "/api/chat":
            return self._send({"items": chat_items()})
        if path == "/api/memories":
            return self._send({"items": memories(q.get("which", "group"))})
        if path == "/api/startup":
            return self._send(startup_state())
        if path == "/api/backups":
            return self._send({"items": backups()})
        if path == "/api/avatar":
            f = ROOT / "头像" / os.path.basename(unquote(q.get("name", "")))
            if f.exists():
                return self._send(f.read_bytes(), 200, "image/png")
            return self._send({"err": "no"}, 404)
        # 静态文件
        rel = path.lstrip("/") or "index.html"
        f = (UI / rel).resolve()
        if not str(f).startswith(str(UI.resolve())) or not f.exists():
            return self._send({"err": "not found"}, 404)
        ctype = {".html": "text/html; charset=utf-8",
                 ".css": "text/css; charset=utf-8",
                 ".js": "application/javascript; charset=utf-8",
                 ".png": "image/png", ".jpg": "image/jpeg",
                 ".svg": "image/svg+xml"}.get(f.suffix.lower(), "application/octet-stream")
        return self._send(f.read_bytes(), 200, ctype)

    def do_POST(self):
        n = int(self.headers.get("Content-Length") or 0)
        try:
            data = json.loads(self.rfile.read(n).decode("utf-8") or "{}")
        except Exception:
            data = {}
        path = self.path.split("?")[0]
        if path == "/api/action":
            return self._send({"ok": True, "msg": act(data.get("name", ""))})
        if path == "/api/say":
            return self._send({"ok": say(data.get("text", "")),
                               "msg": "发出去了" if data.get("text") else ""})
        if path == "/api/settings":
            return self._send({"ok": True, "msg": save_settings(data)})
        if path == "/api/mem/delete":
            import sqlite3
            which = data.get("which", "group")
            p = ROOT / ("data/companion.db" if which == "priv" else "data/group-memory.db")
            try:
                g = sqlite3.connect(str(p))
                g.execute("DELETE FROM memories WHERE id=?", (int(data.get("id", 0)),))
                g.commit()
                return self._send({"ok": True})
            except Exception as e:  # noqa: BLE001
                return self._send({"ok": False, "msg": str(e)})
        if path == "/api/avatar":
            # 头像文件
            f = ROOT / "头像" / os.path.basename(data.get("name", ""))
            if f.exists():
                return self._send(f.read_bytes(), 200, "image/png")
            return self._send({"err": "no"}, 404)
        return self._send({"err": "not found"}, 404)


def main():
    srv = ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
    print("console server on http://127.0.0.1:%d" % PORT)
    srv.serve_forever()


if __name__ == "__main__":
    main()
