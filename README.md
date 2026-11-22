# Usage Monitor

A small system tray / menu bar app that shows the remaining usage limits of your **Claude**, **ChatGPT** and **Antigravity** accounts, read from the CLIs you are already signed in to:

| Account | Sign in with | Shows |
|---|---|---|
| **Claude** | [`claude` CLI (Claude Code)](https://docs.anthropic.com/en/docs/claude-code), Pro/Max plan | 5-hour and 7-day limits, extra model limits, limit resets |
| **ChatGPT** | [Codex CLI](https://github.com/openai/codex), *Sign in with ChatGPT* | 5-hour and 7-day limits, usage limit resets |
| **Antigravity** | Antigravity CLI (`agy`), Google account | Weekly quota per model group (e.g. `7 days · Claude & GPT`, `7 days · Gemini`) |

Available for **Windows**, **macOS** and **Linux**. Only the CLIs installed on your computer are shown.

## Features

- Mini usage bars in the tray icon; click it for details with reset times, plan and email.
- Colors by usage: 🟢 under 50% · 🟠 50–75% · 🔴 over 75%. Notification at 90%.
- Auto refresh every 5 minutes, with back-off when the server rate-limits (HTTP 429).
- Built-in update check from [GitHub Releases](https://github.com/narayaitsolution/usage-monitor/releases).
- English and Bahasa Indonesia (follows the system language by default).
- Optional start at login.

| Platform | Folder | Built with | Status |
|---|---|---|---|
| Windows 10 (1903+) / 11 | [`windows/`](windows/) | C# WinForms, built-in .NET Framework 4.8 | ✅ Tested |
| macOS 12+ | [`macos/`](macos/) | Swift + SwiftUI | ⚠️ Not tested on a Mac |
| Linux (GTK desktop) | [`linux/`](linux/) | Python 3 + GTK AppIndicator | ⚠️ GUI not tested |

## Install

**Windows** — no SDK needed:

```cmd
git clone https://github.com/narayaitsolution/usage-monitor.git
cd usage-monitor\windows
build.cmd install
```

This builds `UsageMonitor.exe`, enables start at login and runs it. Or download `UsageMonitor.exe` from the [latest release](https://github.com/narayaitsolution/usage-monitor/releases/latest).

**macOS** — needs Xcode Command Line Tools:

```sh
cd macos && ./build.sh && open UsageMonitor.app
```

**Linux** — needs Python 3.8+, GTK 3 and AppIndicator (plus `secret-tool` for Antigravity):

```sh
sudo apt install python3-gi gir1.2-gtk-3.0 gir1.2-ayatanaappindicator3-0.1 libnotify-bin libsecret-tools
python3 linux/usagemonitor.py
```

See the README in each platform folder for details.

## Privacy

Usage Monitor only **reads** the sign-in tokens saved by each CLI and asks the same servers the CLIs use. Tokens are never changed, refreshed or sent anywhere else. There is no telemetry.

The usage endpoints are not officially documented, and requests identify as the official CLI (`claude-cli`, `agy`), so this may stop working at any time. Use at your own risk.

If a token has expired, run `claude`, `codex` or `agy` once and the app recovers on its own.

## Donations

Usage Monitor is free and open source, and accepts donations. Use the **♥** button in the app or the [donation page](https://lynk.id/narayait/rp8r1z3rop0w/checkout). The donor list is loaded from GitHub when the window opens and is not stored on your computer.

## License

Copyright © 2026 **Naraya IT Solution**. Released under the [MIT License](LICENSE).

Usage Monitor is an independent project, **not affiliated** with Anthropic, OpenAI or Google. "Claude" is a trademark of Anthropic; "ChatGPT" and "Codex" are trademarks of OpenAI; "Antigravity" and "Gemini" are trademarks of Google.
