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
                // The server only lists "Limit resets" (cedar_ember.grants) for official clients,
                // so this request identifies itself as claude-cli, like `/usage` does.
                h["x-app"] = "cli";
                h["User-Agent"] = ClaudeCliUserAgent;
                object root = Json.Parse(Http.Get("https://api.anthropic.com/api/oauth/usage?cedar_ember=1&skip_spend=1", h));

                FillClaude(s.Five, Json.Obj(root, "five_hour"));
                FillClaude(s.Week, Json.Obj(root, "seven_day"));

                // "limits" also lists extra limits with their own reset time,
                // e.g. kind "weekly_scoped" = weekly limit for one model (Fable).
                var limits = Json.Get(root, "limits") as object[];
                if (limits != null)
                {
                    foreach (object l in limits)
                    {
                        string kind = Json.Str(l, "kind");
                        if (kind == "session" || kind == "weekly_all") continue; // same as five_hour / seven_day
                        string group = Json.Str(l, "group");
                        string label = group == "session" ? "5 jam" : group == "weekly" ? "7 hari" : (group ?? kind ?? "Limit");
                        string scope = ScopeName(Json.Obj(l, "scope"));
                        var w = new UsageWindow(scope != null ? label + " · " + scope : label);
                        w.Percent = Json.Num(l, "percent");
                        w.ResetAt = ParseIso(Json.Str(l, "resets_at"));
                        s.MoreRows.Add(w);
                    }
                }

                // "Limit resets" grants (same list as Settings -> Usage on claude.ai)
                var grants = Json.Get(Json.Obj(root, "cedar_ember"), "grants") as object[];
                if (grants != null)
                {
                    foreach (object gr in grants)
                    {
                        double? left = Json.Num(gr, "resets_left");
                        if (!left.HasValue || left.Value <= 0) continue;
                        s.Infos.Add("↺ " + GrantName(gr) + (left.Value > 1 ? " ×" + left.Value : "")
                                    + GrantExpiry(ParseIso(Json.Str(gr, "ends_at")))
                                    + (Json.Get(gr, "paused") is bool && (bool)Json.Get(gr, "paused") ? " (dijeda)"
                                       : Json.Get(gr, "usable_now") is bool && !(bool)Json.Get(gr, "usable_now") ? " (belum bisa dipakai)" : ""));
                    }
                }
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
            w.ResetAt = ParseIso(Json.Str(o, "resets_at"));
        }

        // Identity sent with the Claude usage request (see FetchClaude). Version of claude-cli it mimics.
        const string ClaudeCliUserAgent = "claude-cli/2.1.326 (external, cli)";

        /// Name of a "Limit resets" grant from the limits it clears (same rule as claude-cli):
        /// clears seven_day = Full reset, clears the Fable weekly allowance only = Fable reset.
        static string GrantName(object grant)
        {
            var clears = Json.Get(grant, "clears") as object[] ?? new object[0];
            var set = new HashSet<string>();
            foreach (object c in clears) set.Add(Convert.ToString(c));
            if (set.Contains("seven_day")) return "Full reset";
            if (set.Contains("seven_day_overage_included")) return "Fable reset";
            if (set.Contains("five_hour")) return "Reset 5 jam";
            return "Limit reset";
        }

        /// ChatGPT "usage limit resets" with their expiry: "↺ Full reset (Weekly + 5 hr) · s/d 2 Des 05:04".
        /// Returns null when the request fails.
        static List<string> CodexResetDetails(Dictionary<string, string> headers)
        {
            try
            {
                object root = Json.Parse(Http.Get("https://chatgpt.com/backend-api/wham/rate-limit-reset-credits", headers));
                var list = new List<string>();
                var credits = Json.Get(root, "credits") as object[];
                if (credits == null) return list;
                foreach (object c in credits)
                {
                    if (Json.Str(c, "status") != "available") continue;
                    string title = Json.Str(c, "title");
                    bool supported = !(Json.Get(c, "is_supported_by_plan") is bool) || (bool)Json.Get(c, "is_supported_by_plan");
                    list.Add("↺ " + (string.IsNullOrEmpty(title) ? "Usage limit reset" : title)
                             + GrantExpiry(ParseIso(Json.Str(c, "expires_at")))
                             + (supported ? "" : " (tidak didukung paket)"));
                }
                return list;
            }
            catch { return null; }
        }

        static readonly CultureInfo IdCulture = new CultureInfo("id-ID");

        static string GrantExpiry(DateTime? end)
        {
            if (!end.HasValue) return "";
            DateTime e = end.Value.AddSeconds(30);
            return " · s/d " + (e.Date == DateTime.Today ? "hari ini " + e.ToString("HH':'mm")
                                                               : e.ToString("d MMM HH':'mm", IdCulture));
        }

        /// Display name of a limit's scope: {"model": {"display_name": "Fable"}, "surface": null}
        static string ScopeName(Dictionary<string, object> scope)
        {
            if (scope == null) return null;
            foreach (string key in new[] { "model", "surface" })
            {
                string n = Json.Str(Json.Obj(scope, key), "display_name");
                if (!string.IsNullOrEmpty(n)) return n;
            }
            return null;
        }

        static DateTime? ParseIso(string r)
        {
            DateTimeOffset dto;
            if (!string.IsNullOrEmpty(r) && DateTimeOffset.TryParse(r, CultureInfo.InvariantCulture, DateTimeStyles.None, out dto))
                return dto.LocalDateTime;
            return null;
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

                // "Usage limit reset" credits: lets the user reset a reached limit early
                var credits = Json.Obj(root, "rate_limit_reset_credits");
                double? available = Json.Num(credits, "available_count");
                if (available.HasValue)
                {
                    // Details (title + expiry per reset) come from a separate read-only endpoint;
                    // only asked when there is something to show. Falls back to the count, like Codex CLI.
                    // Never call ".../rate-limit-reset-credits/consume": that USES a reset.
                    List<string> details = available.Value > 0 ? CodexResetDetails(h) : null;
                    if (details != null && details.Count > 0) s.Infos.AddRange(details);
                    else
                    {
                        double? usable = Json.Num(credits, "applicable_available_count");
                        s.Infos.Add("↺ Usage limit reset: " + available.Value + " tersedia"
                                 + (usable.HasValue && usable.Value > 0 ? " (bisa dipakai sekarang)" : ""));
                    }
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
