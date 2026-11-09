using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace UsageTray
{
    static class Fetcher
    {
        static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public static readonly Color ClaudeColor = Color.FromArgb(217, 119, 87);
        public static readonly Color GptColor = Color.FromArgb(16, 163, 127);

        static string CodexHome()
        {
            string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            return string.IsNullOrEmpty(codexHome) ? Path.Combine(Home, ".codex") : codexHome;
        }

        /// Installed = config folder exists (created by the CLI) or the command is on PATH.
        public static bool ClaudeInstalled() { return Directory.Exists(Path.Combine(Home, ".claude")) || OnPath("claude"); }
        public static bool CodexInstalled() { return Directory.Exists(CodexHome()) || OnPath("codex"); }

        static bool OnPath(string name)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(Path.PathSeparator))
            {
                if (dir.Trim().Length == 0) continue;
                foreach (string ext in new[] { ".exe", ".cmd", ".bat", ".ps1" })
                {
                    try { if (File.Exists(Path.Combine(dir.Trim(), name + ext))) return true; }
                    catch { }
                }
            }
            return false;
        }

        public static ProviderStatus FetchClaude()
        {
            var s = new ProviderStatus { Name = "Claude", Accent = ClaudeColor };
            try
            {
                string path = Path.Combine(Home, ".claude", ".credentials.json");
                if (!File.Exists(path)) { s.Error = "Belum login. Jalankan 'claude' lalu /login."; return s; }
                var oauth = Json.Obj(Json.Parse(File.ReadAllText(path)), "claudeAiOauth");
                string token = Json.Str(oauth, "accessToken");
                s.Plan = Json.Str(oauth, "subscriptionType");
                if (string.IsNullOrEmpty(token)) { s.Error = "Token tidak ditemukan. Login ulang via claude CLI."; return s; }

                // Email (optional) from ~/.claude.json
                try
                {
                    string cfg = Path.Combine(Home, ".claude.json");
                    if (File.Exists(cfg))
                        s.Account = Json.Str(Json.Obj(Json.Parse(File.ReadAllText(cfg)), "oauthAccount"), "emailAddress");
                }
                catch { }

                double? exp = Json.Num(oauth, "expiresAt");
                if (exp.HasValue && FromUnixMs(exp.Value) < DateTime.Now)
                {
                    s.Error = "Token kedaluwarsa. Buka 'claude' sekali agar token di-refresh.";
                    return s;
                }

                var h = new Dictionary<string, string>();
                h["Authorization"] = "Bearer " + token;
                h["anthropic-beta"] = "oauth-2025-04-20";
                object root = Json.Parse(Http.Get("https://api.anthropic.com/api/oauth/usage", h));

                FillClaude(s.Five, Json.Obj(root, "five_hour"));
                FillClaude(s.Week, Json.Obj(root, "seven_day"));
            }
            catch (HttpException ex)
            {
                s.RateLimited = ex.Code == 429;
                s.Error = ex.Code == 429 ? "Dibatasi server (429). Dicoba lagi otomatis."
                    : ex.Code == 401 || ex.Code == 403
                    ? "Token ditolak (" + ex.Code + "). Buka 'claude' agar token di-refresh."
                    : "HTTP " + ex.Code + ": " + ex.Message;
            }
            catch (Exception ex) { s.Error = ex.Message; }
            return s;
        }

        static void FillClaude(UsageWindow w, Dictionary<string, object> o)
        {
            if (o == null) return;
            w.Percent = Json.Num(o, "utilization");
            string r = Json.Str(o, "resets_at");
            DateTimeOffset dto;
            if (!string.IsNullOrEmpty(r) && DateTimeOffset.TryParse(r, CultureInfo.InvariantCulture, DateTimeStyles.None, out dto))
                w.ResetAt = dto.LocalDateTime;
        }

        public static ProviderStatus FetchCodex()
        {
            var s = new ProviderStatus { Name = "ChatGPT", Accent = GptColor };
            try
            {
                string path = Path.Combine(CodexHome(), "auth.json");
                if (!File.Exists(path)) { s.Error = "Belum login. Jalankan 'codex login'."; return s; }
                var tokens = Json.Obj(Json.Parse(File.ReadAllText(path)), "tokens");
                string token = Json.Str(tokens, "access_token");
                string account = Json.Str(tokens, "account_id");
                if (string.IsNullOrEmpty(token)) { s.Error = "Codex tidak login dengan akun ChatGPT (mode API key?)."; return s; }

                var h = new Dictionary<string, string>();
                h["Authorization"] = "Bearer " + token;
                if (!string.IsNullOrEmpty(account)) h["ChatGPT-Account-Id"] = account;
                object root = Json.Parse(Http.Get("https://chatgpt.com/backend-api/wham/usage", h));

                s.Plan = Json.Str(root, "plan_type");
                s.Account = Json.Str(root, "email");
                var rl = Json.Obj(root, "rate_limit");
                foreach (string key in new[] { "primary_window", "secondary_window" })
                {
                    var w = Json.Obj(rl, key);
                    if (w == null) continue;
                    double? secs = Json.Num(w, "limit_window_seconds");
                    // 5h = 18000s, 7d = 604800s; fall back to primary/secondary order
                    UsageWindow target = secs.HasValue
                        ? (secs.Value <= 86400 ? s.Five : s.Week)
                        : (key == "primary_window" ? s.Five : s.Week);
                    target.Percent = Json.Num(w, "used_percent");
                    double? reset = Json.Num(w, "reset_at");
                    if (reset.HasValue) target.ResetAt = FromUnixMs(reset.Value * 1000);
                }
            }
            catch (HttpException ex)
            {
                s.RateLimited = ex.Code == 429;
                s.Error = ex.Code == 429 ? "Dibatasi server (429). Dicoba lagi otomatis."
                    : ex.Code == 401 || ex.Code == 403
                    ? "Token ditolak (" + ex.Code + "). Jalankan 'codex' agar token di-refresh."
                    : "HTTP " + ex.Code + ": " + ex.Message;
            }
            catch (Exception ex) { s.Error = ex.Message; }
            return s;
        }

        static DateTime FromUnixMs(double ms)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime();
        }
    }
}
