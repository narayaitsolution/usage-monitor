#!/usr/bin/env python3
"""UsageTray (Linux) - tray icon for Claude (claude CLI) and ChatGPT (Codex CLI) usage limits.

Requires: Python 3.8+, PyGObject (GTK 3) and AyatanaAppIndicator3 (or AppIndicator3).
Run `python3 usagetray.py --test` to check data fetching without the GUI.
"""
import importlib
import json
import os
import shutil
import struct
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
import zlib
from datetime import datetime, timedelta, timezone

HOME = os.path.expanduser("~")
REFRESH_SECONDS = 5 * 60
APP_ID = "usagetray"

GREEN = (63, 185, 80)
ORANGE = (240, 136, 62)
RED = (229, 72, 77)

DAYS_ID = ["Sen", "Sel", "Rab", "Kam", "Jum", "Sab", "Min"]
MONTHS_ID = ["Jan", "Feb", "Mar", "Apr", "Mei", "Jun", "Jul", "Agu", "Sep", "Okt", "Nov", "Des"]


# ---------------------------------------------------------------- data
class UsageWindow:
    def __init__(self):
        self.percent = None   # float
        self.reset_at = None  # aware datetime


class ProviderStatus:
    def __init__(self, name):
        self.name = name
        self.plan = None
        self.account = None
        self.error = None
        self.five = UsageWindow()
        self.week = UsageWindow()
        self.rate_limited = False  # server answered HTTP 429
        self.note = None           # shown under the rows, e.g. when displaying older data
        self.fetched_at = None


class HttpError(Exception):
    def __init__(self, code, msg):
        super().__init__(msg)
        self.code = code


def http_get(url, headers):
    h = {"Accept": "application/json", "User-Agent": "UsageTray/1.0"}
    h.update(headers)
    req = urllib.request.Request(url, headers=h)
    try:
        with urllib.request.urlopen(req, timeout=20) as r:
            return json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        body = ""
        try:
            body = e.read().decode("utf-8", "replace")[:120]
        except Exception:
            pass
        raise HttpError(e.code, body or e.reason)
    except urllib.error.URLError as e:
        raise Exception("Koneksi gagal: %s" % e.reason)


def read_json(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def num(v):
    try:
        return float(v) if v is not None else None
    except (TypeError, ValueError):
        return None


def claude_dir():
    return os.environ.get("CLAUDE_CONFIG_DIR") or os.path.join(HOME, ".claude")


def fetch_claude():
    s = ProviderStatus("Claude")
    try:
        path = os.path.join(claude_dir(), ".credentials.json")
        if not os.path.exists(path):
            s.error = "Belum login. Jalankan 'claude' lalu /login."
            return s
        oauth = read_json(path).get("claudeAiOauth") or {}
        token = oauth.get("accessToken")
        s.plan = oauth.get("subscriptionType")
        if not token:
            s.error = "Token tidak ditemukan. Login ulang via claude CLI."
            return s
        for cfg in (os.path.join(os.environ.get("CLAUDE_CONFIG_DIR", HOME), ".claude.json"),
                    os.path.join(HOME, ".claude.json")):
            try:
                s.account = (read_json(cfg).get("oauthAccount") or {}).get("emailAddress")
                if s.account:
                    break
            except Exception:
                pass
        exp = num(oauth.get("expiresAt"))
        if exp and exp / 1000 < time.time():
            s.error = "Token kedaluwarsa. Buka 'claude' sekali agar token di-refresh."
            return s
        root = http_get("https://api.anthropic.com/api/oauth/usage",
                        {"Authorization": "Bearer " + token, "anthropic-beta": "oauth-2025-04-20"})
        for key, w in (("five_hour", s.five), ("seven_day", s.week)):
            o = root.get(key) or {}
            w.percent = num(o.get("utilization"))
            if o.get("resets_at"):
                try:
                    w.reset_at = datetime.fromisoformat(o["resets_at"].replace("Z", "+00:00"))
                except ValueError:
                    pass
    except HttpError as e:
        s.rate_limited = e.code == 429
        s.error = ("Dibatasi server (429). Dicoba lagi otomatis." if e.code == 429 else
                   "Token ditolak (%d). Buka 'claude' agar token di-refresh." % e.code
                   if e.code in (401, 403) else "HTTP %d: %s" % (e.code, e))
    except Exception as e:
        s.error = str(e)
    return s


def fetch_codex():
    s = ProviderStatus("ChatGPT")
    try:
        codex_home = os.environ.get("CODEX_HOME") or os.path.join(HOME, ".codex")
        path = os.path.join(codex_home, "auth.json")
        if not os.path.exists(path):
            s.error = "Belum login. Jalankan 'codex login'."
            return s
        tokens = read_json(path).get("tokens") or {}
        token = tokens.get("access_token")
        if not token:
            s.error = "Codex tidak login dengan akun ChatGPT (mode API key?)."
            return s
        h = {"Authorization": "Bearer " + token}
        if tokens.get("account_id"):
            h["ChatGPT-Account-Id"] = tokens["account_id"]
        root = http_get("https://chatgpt.com/backend-api/wham/usage", h)
        s.plan = root.get("plan_type")
        s.account = root.get("email")
        rl = root.get("rate_limit") or {}
        for key in ("primary_window", "secondary_window"):
            w = rl.get(key)
            if not w:
                continue
            secs = num(w.get("limit_window_seconds"))
            if secs is not None:
                target = s.five if secs <= 86400 else s.week
            else:
                target = s.five if key == "primary_window" else s.week
            target.percent = num(w.get("used_percent"))
            reset = num(w.get("reset_at"))
            if reset:
                target.reset_at = datetime.fromtimestamp(reset, tz=timezone.utc)
    except HttpError as e:
        s.rate_limited = e.code == 429
        s.error = ("Dibatasi server (429). Dicoba lagi otomatis." if e.code == 429 else
                   "Token ditolak (%d). Jalankan 'codex' agar token di-refresh." % e.code
                   if e.code in (401, 403) else "HTTP %d: %s" % (e.code, e))
    except Exception as e:
        s.error = str(e)
    return s


# ---------------------------------------------------------------- formatting
def level_color(pct):
    """Same rule for 5h/7d, Claude/ChatGPT: <50 green, 50-75 orange, >75 red."""
    if pct > 75:
        return RED
    if pct >= 50:
        return ORANGE
    return GREEN


def level_dot(pct):
    if pct is None:
        return "⚪"
    return {RED: "\U0001F534", ORANGE: "\U0001F7E0", GREEN: "\U0001F7E2"}[level_color(pct)]


def text_bar(pct, n=10):
    filled = 0 if pct is None else int(round(max(0, min(100, pct)) / 100.0 * n))
    return "▰" * filled + "▱" * (n - filled)


def reset_text(dt):
    if dt is None:
        return ""
    local = dt.astimezone()
    sp = local - datetime.now().astimezone()
    secs = sp.total_seconds()
    if secs <= 0:
        return "Sudah reset - refresh untuk update"
    days, rem = divmod(int(secs), 86400)
    hours, rem = divmod(rem, 3600)
    mins = rem // 60
    if days >= 1:
        d = "%d hari %d jam" % (days, hours)
    elif hours >= 1:
        d = "%d jam %d mnt" % (hours, mins)
    else:
        d = "%d mnt" % max(1, -(-int(secs) // 60))
    r = local + timedelta(seconds=30)
    if r.date() == datetime.now().date():
        when = r.strftime("%H:%M")
    else:
        when = "%s %d %s, %s" % (DAYS_ID[r.weekday()], r.day, MONTHS_ID[r.month - 1], r.strftime("%H:%M"))
    return "Reset dalam %s  ·  %s" % (d, when)


def pct_text(p):
    return "-" if p is None else "%d%%" % round(p)


# ---------------------------------------------------------------- providers & selection
class ProviderDef:
    def __init__(self, pid, title, menu_title, fetch, installed):
        self.id = pid
        self.title = title
        self.menu_title = menu_title
        self.fetch = fetch
        self.installed = installed


def codex_home():
    return os.environ.get("CODEX_HOME") or os.path.join(HOME, ".codex")


PROVIDERS = [
    ProviderDef("claude", "Claude", "Claude (claude-cli)", fetch_claude,
                lambda: os.path.isdir(claude_dir()) or shutil.which("claude") is not None),
    ProviderDef("codex", "ChatGPT", "ChatGPT (Codex)", fetch_codex,
                lambda: os.path.isdir(codex_home()) or shutil.which("codex") is not None),
]


def installed_providers():
    """Providers whose CLI is installed (config folder exists or command on PATH).
    If none is detected, all are offered so the 'belum login' hints are visible."""
    found = [p for p in PROVIDERS if p.installed()]
    return found or list(PROVIDERS)


SETTINGS = os.path.join(os.environ.get("XDG_CONFIG_HOME") or os.path.join(HOME, ".config"),
                        APP_ID, "settings.json")


def load_hidden():
    try:
        return set(read_json(SETTINGS).get("hidden_providers") or [])
    except Exception:
        return set()


def save_hidden(hidden):
    try:
        os.makedirs(os.path.dirname(SETTINGS), exist_ok=True)
        with open(SETTINGS, "w", encoding="utf-8") as f:
            json.dump({"hidden_providers": sorted(hidden)}, f)
    except OSError:
        pass


# ---------------------------------------------------------------- icon (pure-python PNG)
def write_icon(path, statuses, size=22):
    """Two bars (5h, 7d) per shown provider, stacked in menu order."""
    px = [[(0, 0, 0, 0)] * size for _ in range(size)]
    n = max(1, len(statuses))
    gap, group_gap = 1, 3
    fixed = n * gap + (n - 1) * group_gap
    bar_h = max(1, min(9, (size - fixed) // (2 * n)))
    y = max(0, (size - (2 * n * bar_h + fixed)) // 2)
    for st in statuses:
        for w in ((st.five if st else None), (st.week if st else None)):
            err = st is not None and st.error is not None
            track = (150, 60, 60, 200) if err else (128, 128, 128, 150)
            pct = None if (w is None or err) else w.percent
            fill = 0
            if pct is not None:
                fill = int(round(size * max(0, min(100, pct)) / 100.0))
                if pct > 0 and fill < 1:
                    fill = 1
            color = level_color(pct) + (255,) if pct is not None else None
            for yy in range(y, min(size, y + bar_h)):
                for xx in range(size):
                    px[yy][xx] = color if xx < fill else track
            y += bar_h + gap
        y += group_gap - gap

    raw = b"".join(b"\x00" + bytes(c for p in row for c in p) for row in px)

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)

    data = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))
    with open(path, "wb") as f:
        f.write(data)


# ---------------------------------------------------------------- autostart
AUTOSTART = os.path.join(os.environ.get("XDG_CONFIG_HOME") or os.path.join(HOME, ".config"),
                         "autostart", APP_ID + ".desktop")


def autostart_enabled():
    return os.path.exists(AUTOSTART)


def set_autostart(on):
    if on:
        os.makedirs(os.path.dirname(AUTOSTART), exist_ok=True)
        with open(AUTOSTART, "w") as f:
            f.write("[Desktop Entry]\nType=Application\nName=UsageTray\n"
                    "Comment=Claude & ChatGPT usage monitor\n"
                    'Exec="%s" "%s"\nX-GNOME-Autostart-enabled=true\n'
                    % (sys.executable, os.path.abspath(__file__)))
    elif os.path.exists(AUTOSTART):
        os.remove(AUTOSTART)


# ---------------------------------------------------------------- GUI
def load_gui():
    try:
        import gi
        gi.require_version("Gtk", "3.0")
        from gi.repository import GLib, Gtk
    except (ImportError, ValueError):
        sys.exit("PyGObject/GTK3 tidak ditemukan. Lihat README untuk dependensi.")
    for name in ("AyatanaAppIndicator3", "AppIndicator3"):
        try:
            gi.require_version(name, "0.1")
            return Gtk, GLib, importlib.import_module("gi.repository." + name)
        except (ImportError, ValueError):
            continue
    sys.exit("AppIndicator tidak ditemukan. Ubuntu/Debian: sudo apt install gir1.2-ayatanaappindicator3-0.1")


class TrayApp:
    def __init__(self):
        self.Gtk, self.GLib, AppIndicator = load_gui()
        self.data = {}           # provider id -> ProviderStatus
        self.hidden = load_hidden()
        self.installed = installed_providers()
        self.updated = None
        self.busy = False
        self.pending = False
        self.alerted = {}
        self.next_allowed = {}   # provider id -> datetime; back-off after HTTP 429
        self.backoff_min = {}
        self.icon_dir = os.path.join(os.environ.get("XDG_CACHE_HOME") or os.path.join(HOME, ".cache"), APP_ID)
        os.makedirs(self.icon_dir, exist_ok=True)
        for f in os.listdir(self.icon_dir):
            if f.endswith(".png"):
                try:
                    os.remove(os.path.join(self.icon_dir, f))
                except OSError:
                    pass
        self.icon_path = None

        self.ind = AppIndicator.Indicator.new(APP_ID, "", AppIndicator.IndicatorCategory.APPLICATION_STATUS)
        self.ind.set_status(AppIndicator.IndicatorStatus.ACTIVE)
        self.ind.set_title("Usage Monitor")
        self.update_icon()
        self.rebuild_menu()

        self.GLib.timeout_add_seconds(REFRESH_SECONDS, self._tick)
        # keep "reset dalam" countdown fresh and pick up newly installed CLIs
        self.GLib.timeout_add_seconds(30, self._rebuild_tick)
        self.refresh()

    def shown(self):
        """Installed providers that are ticked. Never empty."""
        lst = [p for p in self.installed if p.id not in self.hidden]
        return lst or self.installed[:1]

    def _tick(self):
        self.refresh()
        return True

    def _rebuild_tick(self):
        before = [p.id for p in self.installed]
        self.installed = installed_providers()
        if [p.id for p in self.installed] != before:
            self.update_icon()
            if any(p.id not in self.data for p in self.shown()):
                self.refresh()
        self.rebuild_menu()
        return True

    def toggle_provider(self, item, prov):
        on = item.get_active()
        shown = self.shown()
        if not on and prov in shown and len(shown) == 1:
            # at least one provider must stay ticked
            self.GLib.idle_add(self.rebuild_menu)
            return
        if on:
            self.hidden.discard(prov.id)
        else:
            self.hidden.add(prov.id)
        save_hidden(self.hidden)
        self.update_icon()
        self.GLib.idle_add(self.rebuild_menu)
        if on and prov.id not in self.data and self.can_fetch(prov):
            self.refresh()

    def can_fetch(self, prov):
        t = self.next_allowed.get(prov.id)
        return t is None or datetime.now() >= t

    def store(self, prov, r):
        prev = self.data.get(prov.id)
        if r.rate_limited:
            # back-off: 5, 10, 20, then max 30 minutes
            mins = 5 if not self.backoff_min.get(prov.id) else min(30, self.backoff_min[prov.id] * 2)
            self.backoff_min[prov.id] = mins
            retry = datetime.now() + timedelta(minutes=mins)
            self.next_allowed[prov.id] = retry
            if prev is not None and prev.error is None:
                prev.note = "Dibatasi server (429) · data %s · coba lagi %s" % (
                    prev.fetched_at.strftime("%H:%M"), retry.strftime("%H:%M"))
                return  # keep showing the last good data
            r.error = "Dibatasi server (429). Dicoba lagi %s." % retry.strftime("%H:%M")
        else:
            self.backoff_min.pop(prov.id, None)
            self.next_allowed.pop(prov.id, None)
            r.fetched_at = datetime.now()
        self.data[prov.id] = r

    def refresh(self, *_):
        if self.busy:
            self.pending = True
            return
        provs = [p for p in self.shown() if self.can_fetch(p)]
        if not provs:  # everything is backing off
            self.rebuild_menu()
            return
        self.busy = True
        self.pending = False
        self.rebuild_menu()

        def work():
            res = {}
            threads = [threading.Thread(target=lambda p=p: res.__setitem__(p.id, p.fetch())) for p in provs]
            for t in threads:
                t.start()
            for t in threads:
                t.join()
            self.GLib.idle_add(self.apply, res, provs)

        threading.Thread(target=work, daemon=True).start()

    def apply(self, res, provs):
        for p in provs:
            if p.id in res:
                self.store(p, res[p.id])
        self.updated = datetime.now()
        self.busy = False
        self.update_icon()
        self.rebuild_menu()
        for p in provs:
            st = self.data.get(p.id)
            if st and st.error is None:
                self.check_alert(p, "5 jam", st.five)
                self.check_alert(p, "7 hari", st.week)
        if self.pending:
            self.refresh()
        return False

    def check_alert(self, prov, label, w):
        key = prov.id + label
        high = w.percent is not None and w.percent >= 90
        if high and not self.alerted.get(key) and shutil.which("notify-send"):
            subprocess.Popen(["notify-send", "-a", "UsageTray", "-u", "critical",
                              "Usage %s tinggi" % prov.title,
                              "%s %s: %s terpakai. %s" % (prov.title, label, pct_text(w.percent), reset_text(w.reset_at))])
        self.alerted[key] = high

    def update_icon(self):
        path = os.path.join(self.icon_dir, "icon-%d.png" % int(time.time() * 1000))
        write_icon(path, [self.data.get(p.id) for p in self.shown()])
        self.ind.set_icon_full(path, "Usage Monitor")
        if self.icon_path and os.path.exists(self.icon_path):
            try:
                os.remove(self.icon_path)
            except OSError:
                pass
        self.icon_path = path

    def rebuild_menu(self):
        Gtk = self.Gtk
        menu = Gtk.Menu()

        def label(text):
            item = Gtk.MenuItem(label=text)
            menu.append(item)
            return item

        shown = self.shown()
        for prov in shown:
            st = self.data.get(prov.id)
            head = prov.title
            if st and st.plan:
                head += "  [%s]" % st.plan.upper()
            if st and st.account:
                head += "  —  " + st.account
            label(head)
            if st is None:
                label("    " + ("Memuat…" if self.busy else "Belum ada data"))
            elif st.error:
                label("    ⚠ " + st.error)
            else:
                for name, w in (("5 jam ", st.five), ("7 hari", st.week)):
                    label("    %s %s  %s  %s" % (level_dot(w.percent), name, text_bar(w.percent), pct_text(w.percent)))
                    if w.reset_at:
                        label("          " + reset_text(w.reset_at))
                if st.note:
                    label("    ⚠ " + st.note)
            menu.append(Gtk.SeparatorMenuItem())

        if self.busy:
            foot = "Memuat…"
        elif self.updated:
            foot = "Diperbarui %s  ·  tiap 5 menit" % self.updated.strftime("%H:%M:%S")
        else:
            foot = "Belum ada data"
        label(foot).set_sensitive(False)

        label("↻ Refresh sekarang").connect("activate", self.refresh)
        menu.append(Gtk.SeparatorMenuItem())

        # Provider selection: only CLIs installed on this computer are listed
        label("Tampilkan akun:").set_sensitive(False)
        for prov in self.installed:
            item = Gtk.CheckMenuItem(label=prov.menu_title)
            item.set_active(prov in shown)
            item.connect("toggled", self.toggle_provider, prov)
            menu.append(item)
        menu.append(Gtk.SeparatorMenuItem())

        auto = Gtk.CheckMenuItem(label="Jalankan saat login")
        auto.set_active(autostart_enabled())
        auto.connect("toggled", lambda i: set_autostart(i.get_active()))
        menu.append(auto)
        menu.append(Gtk.SeparatorMenuItem())
        label("Keluar").connect("activate", self.quit)

        menu.show_all()
        self.ind.set_menu(menu)
        self.menu = menu  # keep reference
        return False

    def quit(self, *_):
        if self.icon_path and os.path.exists(self.icon_path):
            try:
                os.remove(self.icon_path)
            except OSError:
                pass
        self.Gtk.main_quit()

    def run(self):
        self.Gtk.main()


def run_test():
    print("installed:", ",".join(p.id for p in PROVIDERS if p.installed()) or "(none)")
    for prov in PROVIDERS:
        st = prov.fetch()
        print("%s plan=%s account=%s error=%s" % (prov.title, st.plan, st.account, st.error))
        for name, w in (("5h", st.five), ("7d", st.week)):
            print("  %s: %s %s | %s" % (name, pct_text(w.percent), text_bar(w.percent), reset_text(w.reset_at)))


def main():
    if "--test" in sys.argv:
        run_test()
        return
    if "--icon" in sys.argv:  # debug: write icon PNG with live data (optional ids after path)
        i = sys.argv.index("--icon")
        out = sys.argv[i + 1]
        ids = sys.argv[i + 2].split(",") if len(sys.argv) > i + 2 else [p.id for p in PROVIDERS]
        write_icon(out, [p.fetch() for p in PROVIDERS if p.id in ids])
        return
    import signal
    signal.signal(signal.SIGINT, signal.SIG_DFL)
    TrayApp().run()


if __name__ == "__main__":
    main()
