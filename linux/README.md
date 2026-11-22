# Usage Monitor — Linux

Icon tray Linux (AppIndicator / StatusNotifierItem) untuk memantau usage **Claude** (`claude` CLI) dan **ChatGPT** (Codex CLI), jendela 5 jam & 7 hari, serta kuota mingguan **Antigravity** (`agy` CLI).

- **Klik icon** → menu berisi usage 5 jam & 7 hari (dengan bar teks dan titik warna), waktu reset, plan, email, Refresh, **Tampilkan akun**, *Jalankan saat login*, **⚙ Bahasa** (Ikuti sistem / Bahasa Indonesia / English), **♥ Donatur & donasi…**, **ⓘ Tentang & update…**, Keluar.
- **Tampilkan akun** berisi centang Claude, ChatGPT, dan/atau Antigravity, hanya untuk CLI yang terpasang. Minimal satu harus tercentang. Pilihan disimpan di `~/.config/usagemonitor/settings.json`.
- Icon berisi 2 bar mini (5h, 7d) per akun yang ditampilkan (Antigravity: 1 bar per grup model, maks. 2), urut Claude, ChatGPT, Antigravity.
- Warna: 🟢 < 50% · 🟠 50–75% · 🔴 > 75%. Notifikasi (`notify-send`) saat usage ≥ 90%.
- Refresh otomatis tiap 5 menit.
- **Cek update** dari GitHub Releases setiap aplikasi dibuka, dan manual lewat **ⓘ Tentang & update… → Periksa update**. Saat tombol **Update** diklik, versi aplikasi dibandingkan lagi dengan versi terbaru di GitHub: jika sama, tidak ada yang diunduh; jika GitHub lebih baru, update ditawarkan dan diunduh. Versi baru (`usagemonitor.py`, atau kode sumber jika rilis tidak menyertakannya) diunduh ke folder Downloads; ganti file lama secara manual. **Skip** menyembunyikan tawaran untuk versi tersebut (`skipped_version` di `settings.json`).

> Di Linux, klik icon tray standar (AppIndicator) selalu membuka **menu**, bukan jendela popup. Karena itu usage ditampilkan sebagai isi menu.

## Persyaratan

Python 3.8+, PyGObject (GTK 3), dan AyatanaAppIndicator3 (atau AppIndicator3).

| Distro | Perintah |
|---|---|
| Ubuntu / Debian / Mint | `sudo apt install python3-gi gir1.2-gtk-3.0 gir1.2-ayatanaappindicator3-0.1 libnotify-bin libsecret-tools` |
| Fedora | `sudo dnf install python3-gobject gtk3 libayatana-appindicator-gtk3 libnotify libsecret` |
| Arch | `sudo pacman -S python-gobject gtk3 libayatana-appindicator libnotify libsecret` |

**GNOME** (selain Ubuntu) butuh extension *AppIndicator and KStatusNotifierItem Support* agar icon tray tampil. KDE, XFCE, Cinnamon, dan Ubuntu GNOME sudah mendukung bawaan.

Login: `claude` CLI dengan akun langganan (Pro/Max), Codex CLI dengan **Sign in with ChatGPT**, `agy` dengan akun Google. `secret-tool` (libsecret) dipakai untuk membaca login `agy` dari keyring.

## Jalankan

```sh
python3 usagemonitor.py           # jalankan icon tray
python3 usagemonitor.py --test    # tes ambil data tanpa GUI
python3 usagemonitor.py --check-update   # cek rilis terbaru di GitHub tanpa GUI
```

Aktifkan **Jalankan saat login** dari menu. Opsi ini menulis `~/.config/autostart/usagemonitor.desktop`.

## Lokasi kredensial

- Claude: `$CLAUDE_CONFIG_DIR/.credentials.json`, default `~/.claude/.credentials.json`
- Antigravity: Secret Service (keyring), atribut `service=gemini`, `username=antigravity` (dibuat `agy`)
- Codex: `$CODEX_HOME/auth.json`, default `~/.codex/auth.json`
