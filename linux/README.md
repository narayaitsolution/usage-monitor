# UsageTray — Linux

Icon tray Linux (AppIndicator / StatusNotifierItem) untuk memantau usage **Claude** (`claude` CLI) dan **ChatGPT** (Codex CLI), jendela 5 jam & 7 hari.

- **Klik icon** → menu berisi usage 5 jam & 7 hari (dengan bar teks dan titik warna), waktu reset, plan, email, Refresh, **Tampilkan akun**, *Jalankan saat login*, Keluar.
- **Tampilkan akun** berisi centang Claude dan/atau ChatGPT, hanya untuk CLI yang terpasang. Minimal satu harus tercentang. Pilihan disimpan di `~/.config/usagetray/settings.json`.
- Icon berisi 2 bar mini (5h, 7d) per akun yang ditampilkan: Claude di atas, ChatGPT di bawah.
- Warna: 🟢 < 50% · 🟠 50–75% · 🔴 > 75%. Notifikasi (`notify-send`) saat usage ≥ 90%.
- Refresh otomatis tiap 5 menit.

> Di Linux, klik icon tray standar (AppIndicator) selalu membuka **menu**, bukan jendela popup. Karena itu usage ditampilkan sebagai isi menu.

## Persyaratan

Python 3.8+, PyGObject (GTK 3), dan AyatanaAppIndicator3 (atau AppIndicator3).

| Distro | Perintah |
|---|---|
| Ubuntu / Debian / Mint | `sudo apt install python3-gi gir1.2-gtk-3.0 gir1.2-ayatanaappindicator3-0.1 libnotify-bin` |
| Fedora | `sudo dnf install python3-gobject gtk3 libayatana-appindicator-gtk3 libnotify` |
| Arch | `sudo pacman -S python-gobject gtk3 libayatana-appindicator libnotify` |

**GNOME** (selain Ubuntu) butuh extension *AppIndicator and KStatusNotifierItem Support* agar icon tray tampil. KDE, XFCE, Cinnamon, dan Ubuntu GNOME sudah mendukung bawaan.

Login: `claude` CLI dengan akun langganan (Pro/Max), Codex CLI dengan **Sign in with ChatGPT**.

## Jalankan

```sh
python3 usagetray.py           # jalankan icon tray
python3 usagetray.py --test    # tes ambil data tanpa GUI
```

Aktifkan **Jalankan saat login** dari menu. Opsi ini menulis `~/.config/autostart/usagetray.desktop`.

## Lokasi kredensial

- Claude: `$CLAUDE_CONFIG_DIR/.credentials.json`, default `~/.claude/.credentials.json`
- Codex: `$CODEX_HOME/auth.json`, default `~/.codex/auth.json`
