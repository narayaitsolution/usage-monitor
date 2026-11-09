# UsageTray

**UsageTray** adalah aplikasi kecil di system tray / menu bar untuk memantau sisa batas pemakaian (usage limit) akun **Claude** dan **ChatGPT** yang login lewat CLI:

- **Claude** — login via [`claude` CLI (Claude Code)](https://docs.anthropic.com/en/docs/claude-code) dengan akun langganan Pro/Max
- **ChatGPT** — login via [Codex CLI](https://github.com/openai/codex) dengan *Sign in with ChatGPT*

Aplikasi menampilkan pemakaian jendela **5 jam** dan **7 hari** untuk masing-masing akun, lengkap dengan waktu reset, tanpa perlu membuka terminal atau browser.

Tersedia untuk **Windows**, **macOS**, dan **Linux**.

---

## Daftar isi

- [Fitur](#fitur)
- [Persyaratan akun](#persyaratan-akun)
- [Windows](#windows)
- [macOS](#macos)
- [Linux](#linux)
- [Menu dan pengaturan](#menu-dan-pengaturan)
- [Cara kerja & privasi](#cara-kerja--privasi)
- [Pemecahan masalah](#pemecahan-masalah)
- [Struktur repository](#struktur-repository)
- [Pengembangan](#pengembangan)
- [Lisensi](#lisensi)

---

## Fitur

- **Icon di system tray / menu bar** berisi bar mini: 2 bar (5 jam & 7 hari) untuk setiap akun yang ditampilkan — Claude di atas, ChatGPT di bawah.
- **Klik icon** untuk melihat detail: persentase pemakaian, progress bar, waktu reset ("Reset dalam 3 jam 12 mnt · 13:00"), jenis langganan, dan email akun.
- **Warna sesuai tingkat pemakaian** (sama untuk 5 jam & 7 hari, Claude & ChatGPT):

  | Pemakaian | Warna |
  |---|---|
  | di bawah 50% | 🟢 hijau |
  | 50% – 75% | 🟠 orange |
  | di atas 75% | 🔴 merah |

- **Notifikasi** saat salah satu pemakaian mencapai 90%.
- **Refresh otomatis tiap 5 menit**, plus tombol Refresh manual.
- **Pilih akun yang ditampilkan** (Claude, ChatGPT, atau keduanya). Hanya CLI yang terpasang di komputer yang muncul di pilihan.
- **Jalankan otomatis saat login** (opsional).
- **Tahan terhadap rate limit**: bila server membalas HTTP 429, data terakhir tetap ditampilkan dan aplikasi menunda pengecekan secara bertahap.

| Platform | Folder | Teknologi | Saat icon diklik | Status |
|---|---|---|---|---|
| Windows 10 (1903+) / 11 | [`windows/`](windows/) | C# WinForms, .NET Framework 4.8 bawaan | Popup | ✅ Diuji |
| macOS 12+ | [`macos/`](macos/) | Swift + SwiftUI | Popover (klik kanan: menu) | ⚠️ Belum diuji |
| Linux (GTK desktop) | [`linux/`](linux/) | Python 3 + GTK AppIndicator | Menu | ⚠️ Logika data diuji, GUI belum |

> Versi macOS dan Linux ditulis mengikuti versi Windows tetapi belum dijalankan di perangkat aslinya. Laporan bug dan perbaikan sangat diterima.

---

## Persyaratan akun

UsageTray **tidak punya login sendiri**. Aplikasi membaca login yang sudah dibuat oleh masing-masing CLI, jadi pastikan:

1. **Claude** — install Claude Code, jalankan `claude`, lalu login dengan akun **Claude Pro/Max** (`/login`).
   Login dengan API key tidak memiliki batas 5 jam/7 hari sehingga tidak bisa ditampilkan.
2. **ChatGPT** — install Codex CLI, jalankan `codex login` dan pilih **Sign in with ChatGPT**.
   Login dengan API key tidak didukung.

Cukup salah satu saja. Jika hanya satu CLI yang terpasang, UsageTray hanya menampilkan akun itu.

---

## Windows

### Instalasi

Tidak perlu install .NET SDK — compiler C# sudah ada di Windows.

1. Clone atau download repository ini.
2. Buka folder `windows` lalu jalankan:

   ```cmd
   build.cmd
   ```

   Hasilnya `windows\UsageTray.exe`. File `.exe` ini berdiri sendiri; boleh disalin ke folder mana pun atau ke komputer lain.
3. Jalankan `UsageTray.exe`. Icon akan muncul di system tray.

> Icon baru biasanya tersembunyi di menu panah **^** di taskbar. Seret icon tersebut ke taskbar agar selalu terlihat.

> Jika file `.exe` didapat dari download/email dan diblokir SmartScreen: klik kanan file → **Properties** → centang **Unblock**.

### Penggunaan

| Aksi | Hasil |
|---|---|
| **Klik kiri** icon | Membuka/menutup popup usage |
| **Klik kanan** icon | Menu: Tampilkan usage, Refresh sekarang, **Tampilkan akun**, Jalankan saat Windows start, Keluar |
| **Hover** icon | Ringkasan, misalnya `Claude 5h 18% \| 7d 46%` |
| **Esc** / klik di luar popup | Menutup popup |

Menu *Tampilkan akun* tetap terbuka saat dicentang, sehingga bisa memilih lebih dari satu akun sekaligus.

### Menghapus

1. Klik kanan icon → hilangkan centang **Jalankan saat Windows start** → **Keluar**.
2. Hapus `UsageTray.exe`.
3. (Opsional) hapus pengaturan: `reg delete HKCU\Software\UsageTray /f`

---

## macOS

### Instalasi

Butuh macOS 12 (Monterey) atau lebih baru dan Xcode Command Line Tools.

```sh
xcode-select --install        # jika belum ada
cd macos
chmod +x build.sh
./build.sh
open UsageTray.app
```

Icon muncul di menu bar (aplikasi tidak tampil di Dock). Jika ingin, salin `UsageTray.app` ke `/Applications`.

> **Akses Keychain:** di macOS, claude CLI menyimpan login di Keychain (item `Claude Code-credentials`). Jika macOS meminta izin, pilih **Always Allow** agar tidak ditanya setiap refresh.

> **Gatekeeper:** aplikasi tidak ditandatangani dengan Developer ID. Jika `UsageTray.app` diunduh dan diblokir: klik kanan → **Open**, atau jalankan `xattr -dr com.apple.quarantine UsageTray.app`.

### Penggunaan

| Aksi | Hasil |
|---|---|
| **Klik** icon | Membuka popover usage (berisi Refresh, Jalankan saat login, Keluar) |
| **Klik kanan** / **Control+klik** | Menu: Tampilkan usage, Refresh, **Tampilkan akun**, Jalankan saat login, Keluar |
| **Hover** icon | Ringkasan usage |

### Menghapus

1. Matikan **Jalankan saat login**, lalu **Keluar**.
2. Hapus `UsageTray.app`.
3. (Opsional) hapus pengaturan: `defaults delete com.usagetray.app`

---

## Linux

### Instalasi

Butuh Python 3.8+, GTK 3, dan AppIndicator. Tidak ada paket Python tambahan dari pip.

| Distro | Perintah |
|---|---|
| Ubuntu / Debian / Mint | `sudo apt install python3-gi gir1.2-gtk-3.0 gir1.2-ayatanaappindicator3-0.1 libnotify-bin` |
| Fedora | `sudo dnf install python3-gobject gtk3 libayatana-appindicator-gtk3 libnotify` |
| Arch | `sudo pacman -S python-gobject gtk3 libayatana-appindicator libnotify` |

Jalankan:

```sh
cd linux
python3 usagetray.py
```

> **GNOME** (selain Ubuntu) membutuhkan extension *AppIndicator and KStatusNotifierItem Support* agar icon tray tampil. KDE, XFCE, Cinnamon, MATE, dan GNOME bawaan Ubuntu sudah mendukung.

### Penggunaan

Di Linux, klik icon tray standar selalu membuka **menu** (bukan jendela). Menu berisi:

- usage 5 jam & 7 hari per akun, dengan bar teks `▰▰▱▱▱` dan titik warna 🟢🟠🔴
- waktu reset, plan, dan email
- Refresh sekarang
- **Tampilkan akun** (centang Claude / ChatGPT)
- Jalankan saat login, Keluar

### Menghapus

1. Matikan **Jalankan saat login**, lalu **Keluar**.
2. Hapus folder repository.
3. (Opsional) hapus pengaturan dan cache: `rm -rf ~/.config/usagetray ~/.cache/usagetray`

---

## Menu dan pengaturan

**Tampilkan akun**

- Hanya berisi CLI yang terdeteksi terpasang: folder `~/.claude` / `~/.codex` ada, atau perintah `claude` / `codex` ditemukan.
- Deteksi diulang setiap menu dibuka dan setiap refresh. CLI yang baru di-install otomatis muncul dan langsung tercentang.
- Minimal satu akun harus tercentang.
- Jika tidak ada CLI yang terdeteksi, kedua akun ditampilkan beserta petunjuk login.

**Lokasi pengaturan**

| | Windows | macOS | Linux |
|---|---|---|---|
| Pilihan akun | Registry `HKCU\Software\UsageTray` | UserDefaults `com.usagetray.app` | `~/.config/usagetray/settings.json` |
| Jalankan saat login | Registry `HKCU\...\CurrentVersion\Run` | `~/Library/LaunchAgents/com.usagetray.app.plist` | `~/.config/autostart/usagetray.desktop` |

Opsi *jalankan saat login* menyimpan lokasi aplikasi saat itu. Jika aplikasi dipindah, matikan lalu aktifkan lagi opsinya.

---

## Cara kerja & privasi

UsageTray hanya **membaca** token login yang sudah dibuat oleh CLI, lalu menanyakan data usage ke server yang sama dengan yang dipakai CLI:

| Akun | Kredensial yang dibaca | Endpoint |
|---|---|---|
| Claude | `~/.claude/.credentials.json` (macOS: Keychain `Claude Code-credentials`), email dari `~/.claude.json` | `GET https://api.anthropic.com/api/oauth/usage` |
| ChatGPT | `~/.codex/auth.json` (atau `$CODEX_HOME/auth.json`) | `GET https://chatgpt.com/backend-api/wham/usage` |

- Token **tidak pernah diubah, di-refresh, atau dikirim ke pihak lain**. Aplikasi tidak menulis ke file kredensial, sehingga tidak mengganggu sesi CLI.
- Tidak ada telemetry atau server pihak ketiga.
- Jika token kedaluwarsa, cukup jalankan `claude` atau `codex` sekali; CLI akan memperbaruinya dan UsageTray pulih sendiri.

> ⚠️ Kedua endpoint usage di atas **tidak didokumentasikan secara resmi** dan bisa berubah sewaktu-waktu tanpa pemberitahuan.

---

## Pemecahan masalah

| Pesan / gejala | Penyebab & solusi |
|---|---|
| **Dibatasi server (429)** | Terlalu banyak request ke server usage. Data terakhir tetap ditampilkan; aplikasi menunggu 5 → 10 → 20 → maks. 30 menit sebelum mencoba lagi. Hindari menekan Refresh berulang-ulang. |
| **Token kedaluwarsa** / **Token ditolak (401/403)** | Jalankan `claude` atau `codex` sekali agar token diperbarui, lalu tekan Refresh. |
| **Belum login** | Login lewat CLI terlebih dahulu (lihat [Persyaratan akun](#persyaratan-akun)). |
| **Codex tidak login dengan akun ChatGPT** | Codex memakai API key. Jalankan `codex login` dan pilih *Sign in with ChatGPT*. |
| Icon tidak terlihat (Windows) | Cek menu panah **^** di taskbar, lalu seret icon ke taskbar. |
| Icon tidak terlihat (Linux GNOME) | Install extension *AppIndicator and KStatusNotifierItem Support*. |
| Akun tidak muncul di *Tampilkan akun* | CLI-nya tidak terdeteksi. Pastikan CLI sudah dijalankan minimal sekali (agar folder `~/.claude` / `~/.codex` terbentuk). |

Untuk mengecek data tanpa GUI:

```sh
# Windows
UsageTray.exe --test hasil.txt
# macOS
UsageTray.app/Contents/MacOS/UsageTray --test
# Linux
python3 usagetray.py --test
```

---

## Struktur repository

```
.
├── README.md
├── LICENSE
├── windows/
│   ├── build.cmd            build dengan csc.exe bawaan Windows
│   └── src/
│       ├── Program.cs       entry point, mode --test / --preview
│       ├── Models.cs        model data & daftar provider
│       ├── Fetcher.cs       baca kredensial, panggil API, deteksi CLI
│       ├── Http.cs          HTTP helper
│       ├── Json.cs          JSON helper
│       ├── TrayContext.cs   icon tray, menu, timer, back-off 429, startup
│       └── PopupForm.cs     popup (digambar custom, DPI-aware)
├── macos/
│   ├── build.sh             build UsageTray.app dengan swiftc
│   └── Sources/UsageTray.swift
└── linux/
    └── usagetray.py
```

---

## Pengembangan

Kontribusi dipersilakan — fork, buat perubahan, lalu kirim pull request.

**Windows**
- Dikompilasi dengan compiler bawaan .NET Framework (`csc.exe`) yang hanya mendukung **C# 5**. Hindari string interpolation (`$"..."`), operator `?.`, `nameof`, dan expression-bodied members.
- `UsageTray.exe --preview popup.png [claude,codex]` merender popup dengan data asli ke file PNG.

**macOS**
- Satu file Swift, dikompilasi dengan `swiftc -parse-as-library`, target macOS 12.

**Linux**
- Satu file Python tanpa dependensi pip. Icon PNG dibuat dengan Python murni.
- `python3 usagetray.py --icon icon.png [claude,codex]` menulis icon tray dengan data asli.

**Menambah provider baru**: tambahkan fungsi fetch dan entri di daftar provider (`Providers.All` di Windows, `allProviders` di macOS, `PROVIDERS` di Linux).

**Ide pengembangan**
- Interval refresh yang bisa diatur dari menu
- Dukungan `CLAUDE_CONFIG_DIR` di Windows dan kredensial di dalam WSL
- Dukungan Codex yang menyimpan kredensial di keyring
- Installer / paket rilis (MSI, DMG, AppImage/Flatpak)

---

## Lisensi

Copyright © 2026 **Naraya IT Solution** (narayaitsolution@gmail.com).

Dirilis di bawah [Lisensi MIT](LICENSE) — bebas digunakan, dimodifikasi, dikembangkan, dan didistribusikan ulang, termasuk untuk keperluan komersial, selama menyertakan teks lisensi.

UsageTray adalah proyek independen dan **tidak berafiliasi** dengan Anthropic maupun OpenAI. "Claude" adalah merek dagang Anthropic; "ChatGPT" dan "Codex" adalah merek dagang OpenAI.
