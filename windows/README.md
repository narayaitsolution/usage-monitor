# Usage Monitor — Windows

Aplikasi system tray Windows untuk memantau batas usage **Claude** (login via `claude` CLI) dan **ChatGPT** (login via Codex CLI) — jendela **5 jam** dan **7 hari** — serta kuota mingguan **Antigravity** (login via `agy` CLI).

- **Klik kiri** icon tray → popup berisi usage 5 jam & 7 hari, waktu reset, plan, dan email akun. Di pojok kanan atas ada tombol **♥** (donatur), **↻** (refresh), **ⓘ** (tentang & update), dan **⚙** (pengaturan: menu yang sama dengan klik kanan).
- **Klik kanan** → Refresh sekarang, **Tampilkan akun** (centang Claude, ChatGPT, dan/atau Antigravity), Jalankan saat Windows start, **Bahasa** (Ikuti sistem / Bahasa Indonesia / English), Donatur & donasi…, Tentang & update…, Keluar.
  - Pilihan akun hanya berisi CLI yang terpasang di komputer ini (folder `~/.claude` / `~/.codex` / `~/.gemini/antigravity-cli` ada, atau perintah `claude` / `codex` / `agy` ditemukan).
  - Minimal satu akun harus tercentang. Pilihan disimpan di `HKCU\Software\UsageMonitor`.
- **Hover** → ringkasan, misalnya `Claude 5h 8% | 7d 45%`.
- **Icon tray** berisi 2 bar mini (5h, 7d) per akun yang ditampilkan (Antigravity: 1 bar per grup model, maks. 2), urut Claude, ChatGPT, Antigravity.
- Warna bar: 🟢 < 50% · 🟠 50–75% · 🔴 > 75%.
- Notifikasi Windows saat salah satu usage mencapai 90%.
- Refresh otomatis tiap 5 menit, plus saat popup dibuka jika data lebih dari 60 detik.
- **Cek update** dari GitHub Releases setiap aplikasi dibuka, dan manual lewat **ⓘ → Periksa update**. Saat tombol **Update** diklik, versi aplikasi dibandingkan lagi dengan versi terbaru di GitHub: jika sama, tidak ada yang diunduh; jika GitHub lebih baru, update ditawarkan dan diunduh. Versi baru (`UsageMonitor.exe`) diunduh ke folder Downloads; pindahkan sendiri menggantikan file lama (aplikasi portable). **Skip** menyembunyikan tawaran untuk versi tersebut (disimpan di `HKCU\Software\UsageMonitor`, value `SkippedVersion`).

## Persyaratan

- Windows 10 (1903+) atau Windows 11. .NET Framework 4.8 sudah bawaan, tidak perlu install apa pun.
- `claude` CLI login dengan akun langganan (Pro/Max), bukan API key.
- Codex CLI login dengan **Sign in with ChatGPT**, bukan API key.
- `agy` (Antigravity CLI) login dengan akun Google. Token `agy` berlaku ±1 jam dan diperbarui selama `agy` berjalan; jika kedaluwarsa, data terakhir tetap ditampilkan.

## Build

Tidak perlu .NET SDK. Build memakai compiler C# bawaan .NET Framework (`csc.exe`, C# 5):

```cmd
build.cmd            :: hanya build
build.cmd install    :: build + aktifkan auto-start + jalankan Usage Monitor
```

Hasilnya `UsageMonitor.exe` di folder `windows/`. Cukup salin file `.exe` ini ke komputer lain.

`build.cmd install` cocok untuk instalasi pertama di komputer baru maupun update setelah `git pull`:

1. Menghentikan Usage Monitor yang sedang berjalan, karena file `.exe` yang sedang dipakai tidak bisa ditimpa.
2. Build `UsageMonitor.exe`.
3. Mendaftarkan auto-start di `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Tidak butuh hak admin.
4. Menjalankan Usage Monitor.

Auto-start bisa dimatikan kapan saja lewat klik kanan icon → **Jalankan saat Windows start**.

## Debug tanpa GUI

```cmd
UsageMonitor.exe --test hasil.txt       :: ambil data kedua provider, tulis ringkasan ke file
UsageMonitor.exe --preview popup.png    :: render popup dengan data asli ke PNG
UsageMonitor.exe --preview-about a.png  :: render jendela Tentang ke PNG
UsageMonitor.exe --preview-update u.png :: render jendela Update (contoh rilis) ke PNG
UsageMonitor.exe --preview-donors d.png :: render jendela Donatur ke PNG
UsageMonitor.exe --check-update c.txt   :: cek rilis terbaru di GitHub, tulis hasilnya ke file
```

## Cara kerja

Aplikasi hanya **membaca** token login yang sudah dibuat oleh masing-masing CLI. Token tidak pernah di-refresh oleh aplikasi ini, supaya tidak bentrok dengan sesi CLI.

| Provider | File kredensial | Endpoint |
|---|---|---|
| Claude | `%USERPROFILE%\.claude\.credentials.json` (email dari `%USERPROFILE%\.claude.json`) | `GET https://api.anthropic.com/api/oauth/usage` (header `anthropic-beta: oauth-2025-04-20`) |
| ChatGPT | `%CODEX_HOME%\auth.json`, default `%USERPROFILE%\.codex\auth.json` | `GET https://chatgpt.com/backend-api/wham/usage` (header `ChatGPT-Account-Id`) |
| Antigravity | Windows Credential Manager, entri `gemini:antigravity` (dibuat `agy`) | `POST https://daily-cloudcode-pa.googleapis.com/v1internal:loadCodeAssist`, `.../v1internal:retrieveUserQuotaSummary` dan `.../v1internal:fetchAvailableModels` (kuota Gemini) (header `User-Agent: antigravity`; host `daily-` sama dengan yang dipakai `agy`) |

Jika token kedaluwarsa atau ditolak (401/403), popup meminta kamu membuka `claude` / `codex` sekali agar CLI me-refresh token.

> ⚠️ Kedua endpoint usage ini **tidak didokumentasikan resmi** dan bisa berubah sewaktu-waktu.

"Jalankan saat Windows start" menulis value `UsageMonitor` di `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Struktur kode

| File | Isi |
|---|---|
| `src/Program.cs` | Entry point, single-instance mutex, mode `--test` / `--preview` / `--check-update`, info versi assembly |
| `src/Models.cs` | `ProviderStatus`, `UsageWindow` |
| `src/Fetcher.cs` | Membaca kredensial & memanggil API usage Claude / ChatGPT |
| `src/Antigravity.cs` | Membaca login `agy` dari Credential Manager & memanggil API kuota Antigravity |
| `src/Http.cs` | HTTP GET helper + `HttpException` |
| `src/Json.cs` | Helper parsing JSON (`JavaScriptSerializer`) |
| `src/TrayContext.cs` | Icon tray, menu, timer refresh, notifikasi, startup |
| `src/PopupForm.cs` | Popup yang digambar custom (DPI-aware), aturan warna bar, tombol ♥ ↻ ⓘ ⚙ |
| `src/Updater.cs` | `AppInfo` (nama & versi), `Settings` (registry + migrasi dari UsageTray), cek & unduh update dari GitHub |
| `src/UpdateForms.cs` | Jendela **Tentang** (ⓘ), **Update**, dan **Donatur** (♥) (tema gelap) |
| `src/I18n.cs` | Pilihan bahasa: ikuti sistem / Bahasa Indonesia / English (`L.T("…", "…")`) |

Catatan: compiler bawaan hanya mendukung **C# 5** — jangan pakai string interpolation (`$"..."`), `?.`, `nameof`, atau expression-bodied members.

## Ide pengembangan

- Dukungan `CLAUDE_CONFIG_DIR` dan kredensial di dalam WSL.
- Interval refresh yang bisa diatur dari menu.
- Backoff otomatis saat kena rate limit (HTTP 429).
