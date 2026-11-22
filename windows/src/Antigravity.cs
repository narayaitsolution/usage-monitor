using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace UsageMonitor
{
    /// Antigravity CLI (agy): weekly quota per model group.
    /// agy keeps its Google sign-in in the Windows Credential Manager (generic credential "gemini:antigravity",
    /// JSON {"token": {"access_token", "expiry", ...}, "id_token_email"}). The token is only read, never refreshed:
    /// agy refreshes it (about every hour) while it runs.
    static partial class Fetcher
    {
        public static readonly Color AntigravityColor = Color.FromArgb(66, 133, 244);
        const string AgyCredential = "gemini:antigravity";
        // Identity sent with the Antigravity requests (see FetchAntigravity)
        const string AntigravityUserAgent = "antigravity";
        // agy talks to the "daily" Cloud Code host; the plain cloudcode-pa host reports a different (unused) quota
        const string CloudCode = "https://daily-cloudcode-pa.googleapis.com/v1internal:";

        public static bool AntigravityInstalled()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Directory.Exists(Path.Combine(Home, ".gemini", "antigravity-cli"))
                   || File.Exists(Path.Combine(local, "agy", "bin", "agy.exe"))
                   || OnPath("agy");
        }

        // Google Cloud project of the Code Assist account, from loadCodeAssist (cached per account)
        static string agyProjectFor, agyProject, agyPlan;

        public static ProviderStatus FetchAntigravity()
        {
            var s = new ProviderStatus { Name = "Antigravity", Accent = AntigravityColor, FixedRows = false };
            try
            {
                string raw = ReadGenericCredential(AgyCredential);
                if (raw == null) { s.Error = L.T("Belum login. Jalankan 'agy' lalu login dengan akun Google.", "Not logged in. Run 'agy' and sign in with Google."); return s; }
                object cred = Json.Parse(raw);
                var tok = Json.Obj(cred, "token");
                string token = Json.Str(tok, "access_token");
                s.Account = Json.Str(cred, "id_token_email");
                if (string.IsNullOrEmpty(token)) { s.Error = L.T("Token tidak ditemukan. Login ulang lewat 'agy'.", "Token not found. Sign in again with 'agy'."); return s; }

                DateTime? exp = ParseIso(Json.Str(tok, "expiry"));
                if (exp.HasValue && exp.Value < DateTime.Now)
                {
                    // agy refreshes its token only while it runs, so this is common: keep the last data
                    s.Error = L.T("Token kedaluwarsa. Jalankan 'agy' agar token di-refresh.", "Token expired. Run 'agy' to refresh it.");
                    s.KeepPrevious = true;
                    return s;
                }

                var h = new Dictionary<string, string>();
                h["Authorization"] = "Bearer " + token;
                // The quota endpoints answer 403 to unknown clients, so the request identifies itself as agy
                h["User-Agent"] = AntigravityUserAgent;

                if (agyProject == null || agyProjectFor != s.Account)
                {
                    object lca = Json.Parse(Http.Post(CloudCode + "loadCodeAssist", h,
                        "{\"metadata\":{\"ideType\":\"ANTIGRAVITY\",\"platform\":\"PLATFORM_UNSPECIFIED\",\"pluginType\":\"GEMINI\"}}"));
                    agyProject = Json.Str(lca, "cloudaicompanionProject");
                    agyPlan = ShortPlanName(Json.Str(Json.Obj(lca, "paidTier"), "name") ?? Json.Str(Json.Obj(lca, "currentTier"), "name"));
                    agyProjectFor = s.Account;
                }
                s.Plan = agyPlan;

                object root = Json.Parse(Http.Post(CloudCode + "retrieveUserQuotaSummary", h,
                    "{\"project\":" + JsonString(agyProject ?? "") + "}"));
                var groups = Json.Get(root, "groups") as object[] ?? new object[0];
                foreach (object gr in groups)
                {
                    string group = ShortGroupName(Json.Str(gr, "displayName"));
                    var buckets = Json.Get(gr, "buckets") as object[] ?? new object[0];
                    foreach (object b in buckets)
                    {
                        if (Json.Get(b, "disabled") is bool && (bool)Json.Get(b, "disabled")) continue;
                        string win = WindowLabel(Json.Str(b, "window"), Json.Str(b, "displayName"));
                        var w = new UsageWindow(group != null ? win + " · " + group : win);
                        double? frac = Json.Num(b, "remainingFraction");
                        double? amount = Json.Num(b, "remainingAmount");
                        // a used-up bucket omits remainingFraction (proto default 0)
                        if (!frac.HasValue && !amount.HasValue) frac = 0;
                        if (frac.HasValue) w.Percent = Math.Max(0, Math.Min(100, (1 - frac.Value) * 100));
                        else w.Label += L.T(" (sisa ", " (") + amount.Value + L.T(")", " left)");
                        w.ResetAt = ParseIso(Json.Str(b, "resetTime"));
                        s.MoreRows.Add(w);
                    }
                }

                // Gemini models are not in the quota summary: their shared limit comes per model from fetchAvailableModels
                if (!s.MoreRows.Exists(r => r.Label.Contains("Gemini")))
                {
                    try
                    {
                        object fam = Json.Parse(Http.Post(CloudCode + "fetchAvailableModels", h,
                            "{\"project\":" + JsonString(agyProject ?? "") + "}"));
                        var models = Json.Obj(fam, "models");
                        double? minFrac = null; DateTime? reset = null;
                        if (models != null)
                            foreach (var kv in models)
                            {
                                if (!kv.Key.StartsWith("gemini", StringComparison.OrdinalIgnoreCase)) continue;
                                var qi = Json.Obj(kv.Value, "quotaInfo");
                                DateTime? r = ParseIso(Json.Str(qi, "resetTime"));
                                if (qi == null || !r.HasValue) continue;
                                double f = Json.Num(qi, "remainingFraction") ?? 0;
                                if (!minFrac.HasValue || f < minFrac.Value) minFrac = f;
                                if (!reset.HasValue || r.Value < reset.Value) reset = r;
                            }
                        if (minFrac.HasValue)
                        {
                            var w = new UsageWindow(WindowLabel("weekly", null) + " · Gemini");
                            w.Percent = Math.Max(0, Math.Min(100, (1 - minFrac.Value) * 100));
                            w.ResetAt = reset;
                            s.MoreRows.Add(w);
                        }
                    }
                    catch (HttpException ex) { if (s.MoreRows.Count == 0) throw; s.Infos.Add("Gemini: HTTP " + ex.Code); }
                }
                if (s.MoreRows.Count == 0) s.Infos.Add(L.T("Tidak ada batas kuota untuk akun ini.", "No quota limits reported for this account."));
            }
            catch (HttpException ex)
            {
                s.RateLimited = ex.Code == 429;
                s.Error = ex.Code == 429 ? L.T("Dibatasi server (429). Dicoba lagi otomatis.", "Rate limited by the server (429). Retrying automatically.")
                    : ex.Code == 401 || ex.Code == 403
                    ? L.T("Token ditolak (", "Token rejected (") + ex.Code + L.T("). Jalankan 'agy' agar token di-refresh.", "). Run 'agy' to refresh it.")
                    : "HTTP " + ex.Code + ": " + ex.Message;
                if (ex.Code == 401) s.KeepPrevious = true;
                agyProject = null;
            }
            catch (Exception ex) { s.Error = ex.Message; }
            return s;
        }

        /// "weekly" -> "7 hari", "daily" -> "1 hari"; otherwise the bucket's own name.
        static string WindowLabel(string window, string displayName)
        {
            switch ((window ?? "").ToLowerInvariant())
            {
                case "weekly": return L.T("7 hari", "7 days");
                case "daily": return L.T("1 hari", "1 day");
                case "hourly": return L.T("1 jam", "1 hour");
                case "five_hour": case "5h": return L.T("5 jam", "5 hours");
            }
            return !string.IsNullOrEmpty(window) ? window : (displayName ?? "Limit");
        }

        /// "Antigravity Starter Quota" -> "Starter" (the long name leaves no room for the account)
        static string ShortPlanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name.StartsWith("Antigravity ", StringComparison.OrdinalIgnoreCase)) name = name.Substring(12);
            if (name.EndsWith(" Quota", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 6);
            return name;
        }

        /// "Claude and GPT models" -> "Claude & GPT"
        static string ShortGroupName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.EndsWith(" models", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 7);
            return name.Replace(" and ", " & ");
        }

        static string JsonString(string v)
        {
            return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(v);
        }

        // ------------------------------------------------------------ Credential Manager (read only)
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CREDENTIAL
        {
            public int Flags; public int Type; public string TargetName; public string Comment;
            public long LastWritten; public int CredentialBlobSize; public IntPtr CredentialBlob;
            public int Persist; public int AttributeCount; public IntPtr Attributes;
            public string TargetAlias; public string UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredRead(string target, int type, int flags, out IntPtr credential);
        [DllImport("advapi32.dll")]
        static extern void CredFree(IntPtr buffer);

        static string ReadGenericCredential(string target)
        {
            IntPtr p;
            if (!CredRead(target, 1 /* CRED_TYPE_GENERIC */, 0, out p)) return null;
            try
            {
                var c = (CREDENTIAL)Marshal.PtrToStructure(p, typeof(CREDENTIAL));
                if (c.CredentialBlobSize <= 0) return null;
                var bytes = new byte[c.CredentialBlobSize];
                Marshal.Copy(c.CredentialBlob, bytes, 0, bytes.Length);
                string text = Encoding.UTF8.GetString(bytes);
                // go-keyring may store base64 with a marker prefix
                const string b64 = "go-keyring-base64:";
                if (text.StartsWith(b64)) text = Encoding.UTF8.GetString(Convert.FromBase64String(text.Substring(b64.Length)));
                return text;
            }
            finally { CredFree(p); }
        }
    }
}
