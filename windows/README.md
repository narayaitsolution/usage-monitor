# UsageTray — Windows

Aplikasi system tray Windows untuk memantau batas usage **Claude** (login via `claude` CLI) dan **ChatGPT** (login via Codex CLI) — jendela **5 jam** dan **7 hari**.

- **Klik kiri** icon tray → popup berisi usage 5 jam & 7 hari, waktu reset, plan, dan email akun.
- **Klik kanan** → Refresh sekarang, **Tampilkan akun** (centang Claude dan/atau ChatGPT), Jalankan saat Windows start, Keluar.
  - Pilihan akun hanya berisi CLI yang terpasang di komputer ini (folder `~/.claude` / `~/.codex` ada, atau perintah `claude` / `codex` ada di PATH).
  - Minimal satu akun harus tercentang. Pilihan disimpan di `HKCU\Software\UsageTray`.
- **Hover** → ringkasan, misalnya `Claude 5h 8% | 7d 45%`.
- **Icon tray** berisi 2 bar mini (5h, 7d) per akun yang ditampilkan: Claude di atas, ChatGPT di bawah.
- Warna bar: 🟢 < 50% · 🟠 50–75% · 🔴 > 75%.
- Notifikasi Windows saat salah satu usage mencapai 90%.
- Refresh otomatis tiap 5 menit, plus saat popup dibuka jika data lebih dari 60 detik.

## Persyaratan

- Windows 10 (1903+) atau Windows 11. .NET Framework 4.8 sudah bawaan, tidak perlu install apa pun.
- `claude` CLI login dengan akun langganan (Pro/Max), bukan API key.
- Codex CLI login dengan **Sign in with ChatGPT**, bukan API key.

## Build

Tidak perlu .NET SDK. Build memakai compiler C# bawaan .NET Framework (`csc.exe`, C# 5):

```cmd
build.cmd            :: hanya build
build.cmd install    :: build + aktifkan auto-start + jalankan UsageTray
```

Hasilnya `UsageTray.exe` di folder `windows/`. Cukup salin file `.exe` ini ke komputer lain.

`build.cmd install` cocok untuk instalasi pertama di komputer baru maupun update setelah `git pull`:

1. Menghentikan UsageTray yang sedang berjalan, karena file `.exe` yang sedang dipakai tidak bisa ditimpa.
2. Build `UsageTray.exe`.
3. Mendaftarkan auto-start di `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Tidak butuh hak admin.
4. Menjalankan UsageTray.

Auto-start bisa dimatikan kapan saja lewat klik kanan icon → **Jalankan saat Windows start**.

## Debug tanpa GUI

```cmd
UsageTray.exe --test hasil.txt       :: ambil data kedua provider, tulis ringkasan ke file
UsageTray.exe --preview popup.png    :: render popup dengan data asli ke PNG
```

## Cara kerja

Aplikasi hanya **membaca** token login yang sudah dibuat oleh masing-masing CLI. Token tidak pernah di-refresh oleh aplikasi ini, supaya tidak bentrok dengan sesi CLI.

| Provider | File kredensial | Endpoint |
|---|---|---|
| Claude | `%USERPROFILE%\.claude\.credentials.json` (email dari `%USERPROFILE%\.claude.json`) | `GET https://api.anthropic.com/api/oauth/usage` (header `anthropic-beta: oauth-2025-04-20`) |
| ChatGPT | `%CODEX_HOME%\auth.json`, default `%USERPROFILE%\.codex\auth.json` | `GET https://chatgpt.com/backend-api/wham/usage` (header `ChatGPT-Account-Id`) |

Jika token kedaluwarsa atau ditolak (401/403), popup meminta kamu membuka `claude` / `codex` sekali agar CLI me-refresh token.

> ⚠️ Kedua endpoint usage ini **tidak didokumentasikan resmi** dan bisa berubah sewaktu-waktu.

"Jalankan saat Windows start" menulis value `UsageTray` di `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Struktur kode

| File | Isi |
|---|---|
| `src/Program.cs` | Entry point, single-instance mutex, mode `--test` / `--preview` |
| `src/Models.cs` | `ProviderStatus`, `UsageWindow` |
| `src/Fetcher.cs` | Membaca kredensial & memanggil API usage Claude / ChatGPT |
| `src/Http.cs` | HTTP GET helper + `HttpException` |
| `src/Json.cs` | Helper parsing JSON (`JavaScriptSerializer`) |
| `src/TrayContext.cs` | Icon tray, menu, timer refresh, notifikasi, startup |
| `src/PopupForm.cs` | Popup yang digambar custom (DPI-aware), aturan warna bar |

Catatan: compiler bawaan hanya mendukung **C# 5** — jangan pakai string interpolation (`$"..."`), `?.`, `nameof`, atau expression-bodied members.

## Ide pengembangan

- Dukungan `CLAUDE_CONFIG_DIR` dan kredensial di dalam WSL.
- Interval refresh yang bisa diatur dari menu.
- Backoff otomatis saat kena rate limit (HTTP 429).
