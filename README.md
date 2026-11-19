# Usage Monitor

**Usage Monitor** adalah aplikasi kecil di system tray / menu bar untuk memantau sisa batas pemakaian (usage limit) akun **Claude** dan **ChatGPT** yang login lewat CLI:

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
- [Update aplikasi](#update-aplikasi)
- [Menu dan pengaturan](#menu-dan-pengaturan)
- [Bahasa](#bahasa)
- [Donasi](#donasi)
- [Cara kerja & privasi](#cara-kerja--privasi)
- [Pemecahan masalah](#pemecahan-masalah)
- [Struktur repository](#struktur-repository)
- [Pengembangan](#pengembangan)
- [Lisensi](#lisensi)

---

## Fitur

- **Icon di system tray / menu bar** berisi bar mini: 2 bar (5 jam & 7 hari) untuk setiap akun yang ditampilkan — Claude di atas, ChatGPT di bawah.
- **Klik icon** untuk melihat detail yang ringkas: satu baris per limit berisi persentase, progress bar, dan waktu reset (`↻ 2j 12m · 13:00`, `↻ 3h 2j · Kam 13:00` — h = hari, j = jam, m = menit), plus jenis langganan dan email akun.
- **Claude: semua limit beserta waktu reset-nya**, termasuk limit tambahan dari server seperti limit mingguan khusus model (misalnya `7 hari · Fable`).
- **Claude: Limit resets** — daftar jatah reset yang sama dengan *Settings → Usage* di claude.ai dan `/usage` di claude-cli, beserta batas berlakunya (`↺ Full reset · s/d 11 Des 05:00`, `↺ Fable reset · s/d hari ini 14:59`).
- **ChatGPT: Usage limit resets** — daftar jatah reset yang tersedia beserta batas berlakunya, sama seperti `/usage` di Codex CLI (`↺ Full reset (Weekly + 5 hr) · s/d 2 Des 05:04`). Jika detailnya gagal dimuat, ditampilkan jumlahnya saja (`↺ Usage limit reset: 2 tersedia`).
- **Warna sesuai tingkat pemakaian** (sama untuk 5 jam & 7 hari, Claude & ChatGPT):

  | Pemakaian | Warna |
  |---|---|
  | di bawah 50% | 🟢 hijau |
  | 50% – 75% | 🟠 orange |
  | di atas 75% | 🔴 merah |

- **Notifikasi** saat salah satu pemakaian mencapai 90%.
- **Refresh otomatis tiap 5 menit**, plus tombol refresh **↻** di pojok kanan atas popup.
- **Cek update otomatis** setiap aplikasi dibuka: jika ada versi baru, muncul jendela berisi versi terbaru dan catatan rilisnya dengan pilihan **Update** (unduh otomatis ke folder Downloads) atau **Skip**. Lihat [Update aplikasi](#update-aplikasi).
- **Tombol ⓘ** di sebelah tombol refresh: informasi aplikasi (versi, tautan, lisensi) dan tombol **Periksa update** untuk update manual.
- **Tombol ⚙ (pengaturan)** di pojok kanan atas popup: membuka menu yang sama dengan klik kanan icon (pilihan akun, jalankan saat login, bahasa, dan lainnya).
- **Dua bahasa: Bahasa Indonesia dan English.** Secara default mengikuti bahasa sistem; bisa diganti lewat menu **Bahasa / Language**. Semua teks di aplikasi ikut berganti. Lihat [Bahasa](#bahasa).
- **Tombol ♥ (donatur)**: daftar donatur aplikasi dan tombol **Donasi**. Lihat [Donasi](#donasi).
- **Icon aplikasi** dengan desain yang sama seperti icon tray (bar usage 5 jam & 7 hari per akun di atas kotak gelap), tampil di Explorer, taskbar, Finder/Dock, dan jendela aplikasi.
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

Usage Monitor **tidak punya login sendiri**. Aplikasi membaca login yang sudah dibuat oleh masing-masing CLI, jadi pastikan:

1. **Claude** — install Claude Code, jalankan `claude`, lalu login dengan akun **Claude Pro/Max** (`/login`).
   Login dengan API key tidak memiliki batas 5 jam/7 hari sehingga tidak bisa ditampilkan.
2. **ChatGPT** — install Codex CLI, jalankan `codex login` dan pilih **Sign in with ChatGPT**.
   Login dengan API key tidak didukung.

Cukup salah satu saja. Jika hanya satu CLI yang terpasang, Usage Monitor hanya menampilkan akun itu.

---

## Windows

### Instalasi

Tidak perlu install .NET SDK — compiler C# sudah ada di Windows.

**Cara cepat (disarankan)** — build, aktifkan auto-start, dan langsung jalankan dengan satu perintah:

```cmd
git clone https://github.com/narayaitsolution/usage-monitor.git
cd usage-monitor\windows
build.cmd install
```

Icon langsung muncul di system tray, dan Usage Monitor otomatis berjalan setiap login Windows.

**Hanya build** (tanpa auto-start dan tanpa menjalankan):

```cmd
cd usage-monitor\windows
build.cmd
```

Hasilnya `windows\UsageMonitor.exe`. File `.exe` ini berdiri sendiri; boleh disalin ke folder mana pun atau ke komputer lain. Jalankan `UsageMonitor.exe` untuk memunculkan icon, lalu aktifkan auto-start lewat klik kanan → **Jalankan saat Windows start**.

| Perintah | Build | Aktifkan auto-start | Jalankan aplikasi |
|---|---|---|---|
| `build.cmd` | ✅ | — | — |
| `build.cmd install` | ✅ | ✅ | ✅ |

> Auto-start menyimpan lokasi `UsageMonitor.exe` saat itu. Jika folder repository dipindah, jalankan `build.cmd install` lagi dari lokasi baru.

### Update ke versi terbaru

**Dari aplikasi (disarankan):** saat ada versi baru, Usage Monitor menawarkannya sendiri, atau klik **ⓘ → Periksa update**. File baru diunduh ke folder Downloads; keluar dari aplikasi, ganti `UsageMonitor.exe` lama dengan file tersebut, lalu jalankan lagi. Detail: [Update aplikasi](#update-aplikasi).

**Dari source** (jika memakai hasil build sendiri):

```cmd
cd usage-monitor
git pull
cd windows
build.cmd install
```

`build.cmd install` otomatis menghentikan Usage Monitor yang sedang berjalan sebelum build, lalu menjalankannya lagi. Jika memakai `build.cmd` biasa, keluar dulu dari aplikasi (klik kanan → **Keluar**), karena file `.exe` yang sedang berjalan tidak bisa ditimpa.

> Icon baru biasanya tersembunyi di menu panah **^** di taskbar. Seret icon tersebut ke taskbar agar selalu terlihat.

> Jika file `.exe` didapat dari download/email dan diblokir SmartScreen: klik kanan file → **Properties** → centang **Unblock**.

### Penggunaan

| Aksi | Hasil |
|---|---|
| **Klik kiri** icon | Membuka/menutup popup usage |
| **♥** di popup | Jendela *Donatur*: daftar donatur dan tombol **Donasi** |
| **↻** di popup | Refresh sekarang |
| **ⓘ** di popup | Jendela *Tentang*: versi aplikasi dan tombol **Periksa update** |
| **⚙** di popup | Menu pengaturan (sama dengan klik kanan icon) |
| **Klik kanan** icon | Menu: Tampilkan usage, Refresh sekarang, **Tampilkan akun**, Jalankan saat Windows start, **Bahasa**, Donatur & donasi…, Tentang & update…, Keluar |
| **Hover** icon | Ringkasan, misalnya `Claude 5h 18% \| 7d 46%` |
| **Esc** / klik di luar popup | Menutup popup |

Menu *Tampilkan akun* tetap terbuka saat dicentang, sehingga bisa memilih lebih dari satu akun sekaligus.

### Menghapus

1. Klik kanan icon → hilangkan centang **Jalankan saat Windows start** → **Keluar**.
2. Hapus `UsageMonitor.exe`.
3. (Opsional) hapus pengaturan: `reg delete HKCU\Software\UsageMonitor /f`

---

## macOS

### Instalasi

Butuh macOS 12 (Monterey) atau lebih baru dan Xcode Command Line Tools.

```sh
xcode-select --install        # jika belum ada
cd macos
chmod +x build.sh
./build.sh
open UsageMonitor.app
```

Icon muncul di menu bar (aplikasi tidak tampil di Dock). Jika ingin, salin `UsageMonitor.app` ke `/Applications`.

> **Akses Keychain:** di macOS, claude CLI menyimpan login di Keychain (item `Claude Code-credentials`). Jika macOS meminta izin, pilih **Always Allow** agar tidak ditanya setiap refresh.

> **Gatekeeper:** aplikasi tidak ditandatangani dengan Developer ID. Jika `UsageMonitor.app` diunduh dan diblokir: klik kanan → **Open**, atau jalankan `xattr -dr com.apple.quarantine UsageMonitor.app`.

### Penggunaan

| Aksi | Hasil |
|---|---|
| **Klik** icon | Membuka popover usage (berisi tombol ♥ donatur, ↻ refresh, ⓘ tentang & update, ⚙ pengaturan, Jalankan saat login, Keluar) |
| **Klik kanan** / **Control+klik**, atau **⚙** di popover | Menu: Tampilkan usage, Refresh, **Tampilkan akun**, Jalankan saat login, **Bahasa**, Donatur & donasi…, Tentang & update…, Keluar |
| **Hover** icon | Ringkasan usage |

### Menghapus

1. Matikan **Jalankan saat login**, lalu **Keluar**.
2. Hapus `UsageMonitor.app`.
3. (Opsional) hapus pengaturan: `defaults delete com.usagemonitor.app`

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
python3 usagemonitor.py
```

> **GNOME** (selain Ubuntu) membutuhkan extension *AppIndicator and KStatusNotifierItem Support* agar icon tray tampil. KDE, XFCE, Cinnamon, MATE, dan GNOME bawaan Ubuntu sudah mendukung.

### Penggunaan

Di Linux, klik icon tray standar selalu membuka **menu** (bukan jendela). Menu berisi:

- usage 5 jam & 7 hari per akun, dengan bar teks `▰▰▱▱▱` dan titik warna 🟢🟠🔴
- waktu reset, plan, dan email
- Refresh sekarang
- **Tampilkan akun** (centang Claude / ChatGPT)
- Jalankan saat login
- **⚙ Bahasa** (Ikuti sistem / Bahasa Indonesia / English)
- **♥ Donatur & donasi…** (daftar donatur dan tombol Donasi)
- **ⓘ Tentang & update…** (informasi aplikasi dan tombol Periksa update)
- Keluar

Karena di Linux klik icon sudah membuka menu, menu ini sekaligus berfungsi sebagai menu pengaturan.

### Menghapus

1. Matikan **Jalankan saat login**, lalu **Keluar**.
2. Hapus folder repository.
3. (Opsional) hapus pengaturan dan cache: `rm -rf ~/.config/usagemonitor ~/.cache/usagemonitor`

## Update aplikasi

Mulai versi **2.0**, Usage Monitor memeriksa sendiri apakah ada versi baru di [GitHub Releases](https://github.com/narayaitsolution/usage-monitor/releases).

**Otomatis saat aplikasi dibuka.** Beberapa detik setelah start up, aplikasi menanyakan rilis terbaru ke GitHub. Jika ada versi yang lebih baru, muncul jendela kecil berisi nomor versi terbaru dan catatan rilisnya (apa saja yang baru), dengan dua pilihan:

| Pilihan | Hasil |
|---|---|
| **Update** | File versi baru diunduh otomatis ke folder **Downloads**. Karena Usage Monitor aplikasi *portable* (tanpa installer), kamu diminta memindahkan file tersebut secara manual: keluar dari aplikasi, ganti file lama dengan file baru, lalu jalankan lagi. Jendela ini menyediakan tombol **Buka folder** dan **Keluar**. |
| **Skip** | Tawaran untuk versi itu tidak akan muncul lagi. Versi berikutnya tetap ditawarkan. |
| Tutup jendela (✕) | Tidak memilih apa pun; tawaran muncul lagi saat aplikasi dibuka berikutnya. |

Jika sedang offline atau GitHub tidak bisa dihubungi, pengecekan dilewati tanpa pesan.

**Manual lewat tombol ⓘ.** Klik tombol **ⓘ** (di sebelah tombol refresh **↻** di pojok kanan atas popup) untuk membuka jendela **Tentang** berisi nama, versi, dan informasi aplikasi. Tekan **Periksa update** untuk mengecek saat itu juga; jika ada versi baru, jendela update di atas langsung ditawarkan (termasuk versi yang sebelumnya di-skip). Jendela yang sama juga bisa dibuka dari menu klik kanan → **Tentang & update…** (di Linux: menu tray → **ⓘ Tentang & update…**).

**File yang diunduh per platform**

| Platform | File dari rilis | Yang perlu dilakukan |
|---|---|---|
| Windows | `UsageMonitor.exe` | Keluar, ganti `UsageMonitor.exe` lama dengan yang baru, jalankan lagi |
| macOS | file rilis berakhiran `-macos.zip` / `.dmg`; jika tidak ada, kode sumber (`.zip`) | Keluar, ganti `UsageMonitor.app` (atau build ulang dari kode sumber dengan `./build.sh`) |
| Linux | `usagemonitor.py`; jika tidak ada, kode sumber (`.zip`) | Keluar, ganti `usagemonitor.py` lama, jalankan lagi |

**Penomoran versi** memakai format `Mayor.Minor`:

- **Mayor** naik saat ada **fitur baru** (contoh: `2.0` → `3.0`).
- **Minor** naik untuk **perubahan/perbaikan pada fitur** di versi tersebut (contoh: `2.0` → `2.1`).

> **Pindah dari UsageTray (versi 1.x).** Aplikasi ini sebelumnya bernama *UsageTray*. Saat `UsageMonitor.exe` pertama kali dijalankan, pilihan akun dan auto-start dari UsageTray dipindahkan otomatis, dan `UsageTray.exe` yang masih berjalan dihentikan supaya tidak ada dua icon. Setelah itu file `UsageTray.exe` lama boleh dihapus. Hal yang sama berlaku di macOS (`com.usagetray.app`) dan Linux (`~/.config/usagetray`).

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
| Pilihan akun | Registry `HKCU\Software\UsageMonitor` | UserDefaults `com.usagemonitor.app` | `~/.config/usagemonitor/settings.json` |
| Bahasa | Registry `HKCU\Software\UsageMonitor` (`Language`) | UserDefaults `com.usagemonitor.app` (`language`) | `~/.config/usagemonitor/settings.json` (`language`) |
| Versi update yang di-skip | Registry `HKCU\Software\UsageMonitor` (`SkippedVersion`) | UserDefaults `com.usagemonitor.app` (`skippedVersion`) | `~/.config/usagemonitor/settings.json` (`skipped_version`) |
| Jalankan saat login | Registry `HKCU\...\CurrentVersion\Run` | `~/Library/LaunchAgents/com.usagemonitor.app.plist` | `~/.config/autostart/usagemonitor.desktop` |

Opsi *jalankan saat login* menyimpan lokasi aplikasi saat itu. Jika aplikasi dipindah, matikan lalu aktifkan lagi opsinya.

---

## Bahasa

Usage Monitor tersedia dalam **Bahasa Indonesia** dan **English**. Semua teks di aplikasi (popup, menu, notifikasi, pesan error, jendela Tentang, Update, dan Donatur, serta format waktu reset) mengikuti bahasa yang dipilih.

Pilih lewat menu klik kanan (atau tombol **⚙** di popup) → **Bahasa / Language**:

| Pilihan | Hasil |
|---|---|
| **Ikuti sistem** (default) | Bahasa Indonesia jika bahasa tampilan sistem adalah Indonesia, selain itu English |
| **Bahasa Indonesia** | Selalu Bahasa Indonesia |
| **English** | Selalu English |

Bahasa sistem dibaca dari: Windows — bahasa tampilan (*Display language*); macOS — bahasa pertama di *Preferred Languages*; Linux — variabel `LANGUAGE`, `LC_ALL`, `LC_MESSAGES`, atau `LANG`.

Format waktu reset ikut berganti: `↻ 2j 12m · Kam 13:00` (h = hari, j = jam, m = menit) menjadi `↻ 2h 12m · Thu 13:00` (d = day, h = hour, m = minute).

---

## Donasi

Usage Monitor gratis dan open source. Jika aplikasi ini bermanfaat, dukung pengembangannya lewat **[halaman donasi](https://lynk.id/narayait/rp8r1z3rop0w/checkout)**.

Di aplikasi, tombol **♥** di popup (atau menu → **Donatur & donasi…**) membuka jendela berisi daftar donatur dan tombol **Donasi**.

**Daftar donatur** dibaca dari file [`donors.json`](donors.json) di branch `main` repository ini, jadi daftar bisa diperbarui tanpa merilis versi baru:

```json
{
  "donate_url": "https://lynk.id/narayait/rp8r1z3rop0w/checkout",
  "donors": [
    { "name": "Budi", "note": "Semangat terus!", "date": "2026-11-19" },
    { "name": "Ani" }
  ]
}
```

- `name` wajib; `note` (pesan) dan `date` opsional.
- `donate_url` adalah tujuan tombol **Donasi**. Jika file tidak bisa dimuat, aplikasi memakai link bawaan.

---

## Cara kerja & privasi

Usage Monitor hanya **membaca** token login yang sudah dibuat oleh CLI, lalu menanyakan data usage ke server yang sama dengan yang dipakai CLI:

| Akun | Kredensial yang dibaca | Endpoint |
|---|---|---|
| Claude | `~/.claude/.credentials.json` (macOS: Keychain `Claude Code-credentials`), email dari `~/.claude.json` | `GET https://api.anthropic.com/api/oauth/usage?cedar_ember=1&skip_spend=1` |
| ChatGPT | `~/.codex/auth.json` (atau `$CODEX_HOME/auth.json`) | `GET https://chatgpt.com/backend-api/wham/usage`, dan `GET .../wham/rate-limit-reset-credits` (hanya jika ada jatah reset) |

Aplikasi **hanya membaca**. Endpoint untuk *memakai* jatah reset (`.../rate-limit-reset-credits/consume`) tidak pernah dipanggil, jadi jatah reset kamu tidak akan terpakai oleh Usage Monitor.

- Token **tidak pernah diubah, di-refresh, atau dikirim ke pihak lain**. Aplikasi tidak menulis ke file kredensial, sehingga tidak mengganggu sesi CLI.
- Tidak ada telemetry atau server pihak ketiga.
- **Daftar donatur** dimuat dari `https://raw.githubusercontent.com/narayaitsolution/usage-monitor/main/donors.json` (tanpa data pribadi) hanya saat jendela Donatur dibuka.
- **Cek update** hanya mengirim satu request tanpa data pribadi ke API publik GitHub (`GET https://api.github.com/repos/narayaitsolution/usage-monitor/releases/latest`) saat aplikasi dibuka atau saat menekan **Periksa update**. File update hanya diunduh jika kamu menekan **Update**.
- **Request usage Claude mengaku sebagai claude-cli** (header `x-app: cli` dan `User-Agent: claude-cli/…`). Server Anthropic hanya mengirim daftar *Limit resets* ke aplikasi resminya, jadi tanpa header ini daftar tersebut kosong. Ini bukan cara yang didukung resmi oleh Anthropic: bisa berhenti bekerja sewaktu-waktu, dan pengguna menanggung risikonya sendiri. Versi claude-cli yang ditiru diatur di konstanta `ClaudeCliUserAgent` (Windows), `claudeCliUserAgent` (macOS), dan `CLAUDE_CLI_USER_AGENT` (Linux).
- Jika token kedaluwarsa, cukup jalankan `claude` atau `codex` sekali; CLI akan memperbaruinya dan Usage Monitor pulih sendiri.

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
UsageMonitor.exe --test hasil.txt
# macOS
UsageMonitor.app/Contents/MacOS/UsageMonitor --test
# Linux
python3 usagemonitor.py --test
```

---

## Struktur repository

```
.
├── README.md
├── LICENSE
├── donors.json              daftar donatur & link donasi (dibaca aplikasi)
├── tools/make_icons.py      membuat icon aplikasi (app.ico, AppIcon.icns, usagemonitor.svg)
├── windows/
│   ├── build.cmd            build dengan csc.exe bawaan Windows
│   ├── app.ico              icon aplikasi (ditanam ke UsageMonitor.exe)
│   └── src/
│       ├── Program.cs       entry point, mode --test / --preview
│       ├── Models.cs        model data & daftar provider
│       ├── Fetcher.cs       baca kredensial, panggil API, deteksi CLI
│       ├── Http.cs          HTTP helper
│       ├── Json.cs          JSON helper
│       ├── TrayContext.cs   icon tray, menu, timer, back-off 429, startup, cek update
│       ├── PopupForm.cs     popup (digambar custom, DPI-aware)
│       ├── Updater.cs       versi aplikasi, pengaturan, cek & unduh update dari GitHub
│       ├── I18n.cs          pilihan bahasa (Indonesia / English)
│       └── UpdateForms.cs   jendela Tentang (ⓘ), Update, dan Donatur (♥)
├── macos/
│   ├── build.sh             build UsageMonitor.app dengan swiftc
│   ├── AppIcon.icns         icon aplikasi
│   └── Sources/UsageMonitor.swift
└── linux/
    ├── usagemonitor.py
    └── usagemonitor.svg     icon aplikasi (jendela & autostart)
```

---

## Pengembangan

Kontribusi dipersilakan — fork, buat perubahan, lalu kirim pull request.

**Windows**
- Dikompilasi dengan compiler bawaan .NET Framework (`csc.exe`) yang hanya mendukung **C# 5**. Hindari string interpolation (`$"..."`), operator `?.`, `nameof`, dan expression-bodied members.
- `UsageMonitor.exe --preview popup.png [claude,codex]` merender popup dengan data asli ke file PNG.
- `UsageMonitor.exe --preview-about about.png` / `--preview-update update.png` / `--preview-donors donors.png` merender jendela Tentang / Update / Donatur ke PNG.
- Teks baru ditulis dalam dua bahasa dengan `L.T("teks Indonesia", "English text")` (Windows), `T(...)` (macOS & Linux).
- `UsageMonitor.exe --check-update hasil.txt` mengecek rilis terbaru di GitHub tanpa GUI.

**macOS**
- Satu file Swift, dikompilasi dengan `swiftc -parse-as-library`, target macOS 12.

**Linux**
- Satu file Python tanpa dependensi pip. Icon PNG dibuat dengan Python murni.
- `python3 usagemonitor.py --icon icon.png [claude,codex]` menulis icon tray dengan data asli.
- `python3 usagemonitor.py --check-update` mengecek rilis terbaru di GitHub tanpa GUI.
- `python3 usagemonitor.py --donors` menampilkan daftar donatur dari `donors.json`.

**Membuat rilis baru**

1. Naikkan nomor versi (`Mayor.Minor`, lihat [Update aplikasi](#update-aplikasi)) di ketiga tempat ini, lalu commit:
   - Windows: `AppInfo.Version` dan atribut `AssemblyVersion` di `windows/src/Updater.cs` & `windows/src/Program.cs`
   - macOS: `AppInfo.version` di `macos/Sources/UsageMonitor.swift` dan `CFBundleShortVersionString` di `macos/build.sh`
   - Linux: `VERSION` di `linux/usagemonitor.py`
2. Build `windows\UsageMonitor.exe` (dan `UsageMonitor-macos.zip` berisi `UsageMonitor.app` jika ada Mac untuk build).
3. Buat rilis di GitHub dengan tag `v<versi>` (misalnya `v2.1`), isi deskripsi dengan daftar perubahan — teks ini yang tampil di jendela update — lalu lampirkan `UsageMonitor.exe`, `linux/usagemonitor.py`, `linux/usagemonitor.svg`, dan (opsional) `UsageMonitor-macos.zip`.

Aplikasi membaca rilis **terbaru yang sudah dipublikasikan** (bukan draft atau pre-release), membandingkan nomor versinya dengan versi yang sedang berjalan, dan mencari file yang cocok untuk platformnya berdasarkan nama file.

**Icon aplikasi** dibuat dari satu desain oleh `python tools/make_icons.py` (butuh Pillow: `pip install pillow`). File hasilnya (`windows/app.ico`, `macos/AppIcon.icns`, `linux/usagemonitor.svg`) ikut di-commit, jadi build biasa tidak butuh Python. Jalankan ulang script ini hanya jika desain icon diubah; ubah juga `AppIcon` di `windows/src/UpdateForms.cs` agar logo di jendela Tentang tetap sama.

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

Usage Monitor adalah proyek independen dan **tidak berafiliasi** dengan Anthropic maupun OpenAI. "Claude" adalah merek dagang Anthropic; "ChatGPT" dan "Codex" adalah merek dagang OpenAI.
