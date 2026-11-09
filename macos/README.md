# UsageTray — macOS

Aplikasi menu bar macOS untuk memantau usage **Claude** (`claude` CLI) dan **ChatGPT** (Codex CLI), jendela 5 jam & 7 hari.

- **Klik icon** di menu bar → popover berisi usage 5 jam & 7 hari, waktu reset, plan, email, tombol Refresh, opsi *Jalankan saat login*, dan Keluar.
- **Klik kanan** (atau Control+klik) → menu: Refresh, **Tampilkan akun** (centang Claude dan/atau ChatGPT), Jalankan saat login, Keluar.
  - Pilihan akun hanya berisi CLI yang terpasang di Mac ini. Minimal satu akun harus tercentang. Pilihan disimpan di UserDefaults.
- **Hover** → ringkasan usage.
- Icon berisi 2 bar mini (5h, 7d) per akun yang ditampilkan: Claude di atas, ChatGPT di bawah.
- Warna bar: 🟢 < 50% · 🟠 50–75% · 🔴 > 75%. Notifikasi saat usage ≥ 90%.
- Refresh otomatis tiap 5 menit.

## Persyaratan

- macOS 12 (Monterey) atau lebih baru.
- Xcode Command Line Tools: `xcode-select --install`
- `claude` CLI login dengan akun langganan (Pro/Max), Codex CLI login dengan **Sign in with ChatGPT**.

## Build & jalankan

```sh
chmod +x build.sh
./build.sh
open UsageTray.app
```

Untuk tes tanpa GUI:

```sh
UsageTray.app/Contents/MacOS/UsageTray --test
```

Salin `UsageTray.app` ke `/Applications` jika ingin, lalu aktifkan **Jalankan saat login** dari popover. Opsi ini menulis `~/Library/LaunchAgents/com.usagetray.app.plist` yang menunjuk ke lokasi aplikasi saat itu. Jika aplikasi dipindah, matikan lalu aktifkan lagi opsinya.

## Catatan

- **Kredensial Claude di macOS ada di Keychain** (item `Claude Code-credentials`), dibaca lewat `/usr/bin/security`. Jika macOS meminta izin akses Keychain, pilih **Always Allow**. Jika tidak ada di Keychain, aplikasi membaca `~/.claude/.credentials.json`.
- Kredensial Codex dibaca dari `~/.codex/auth.json` (atau `$CODEX_HOME`). Jika Codex dikonfigurasi menyimpan kredensial di keyring (`cli_auth_credentials_store = "keyring"`), versi ini belum mendukungnya.
- Variabel `CLAUDE_CONFIG_DIR` / `CODEX_HOME` hanya terbaca jika aplikasi dijalankan dari terminal. Aplikasi yang dibuka dari Finder tidak mewarisi environment shell.
- Aplikasi tidak ditandatangani dengan Developer ID. Jika `UsageTray.app` diunduh dari internet dan diblokir Gatekeeper: klik kanan → **Open**, atau `xattr -dr com.apple.quarantine UsageTray.app`.
