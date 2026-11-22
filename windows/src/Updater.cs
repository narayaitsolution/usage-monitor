using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace UsageMonitor
{
    static class AppInfo
    {
        public const string Name = "Usage Monitor";
        /// Major.Minor: Major naik untuk fitur baru, Minor untuk perubahan pada fitur yang ada.
        public const string Version = "3.3";
        public const string Repo = "narayaitsolution/usage-monitor";
        public const string RepoUrl = "https://github.com/" + Repo;
        public const string UserAgent = "UsageMonitor/" + Version;
        /// Donor list and donation link, edited in the repository without a new release.
        public const string DonorsUrl = "https://raw.githubusercontent.com/" + Repo + "/main/donors.json";
        /// Used when donors.json can't be loaded or has no donate_url.
        public const string DonateUrl = "https://lynk.id/narayait/rp8r1z3rop0w/checkout";
    }

    /// Settings in HKCU\Software\UsageMonitor (moved from the old "UsageTray" name on first run).
    static class Settings
    {
        const string Key = @"Software\UsageMonitor";
        const string OldKey = @"Software\UsageTray";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunName = "UsageMonitor";
        const string OldRunName = "UsageTray";

        public static string Get(string name)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key))
                    return k == null ? null : k.GetValue(name) as string;
            }
            catch { return null; }
        }

        public static void Set(string name, string value)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                    k.SetValue(name, value);
            }
            catch { }
        }

        public static string SkippedVersion
        {
            get { return Get("SkippedVersion"); }
            set { Set("SkippedVersion", value); }
        }

        /// Move settings and auto-start from the old "UsageTray" name, and stop a running UsageTray.exe
        /// so two tray icons don't show at once.
        public static void MigrateFromUsageTray()
        {
            try
            {
                bool hadOld = false;
                using (var old = Registry.CurrentUser.OpenSubKey(OldKey))
                {
                    if (old != null)
                    {
                        hadOld = true;
                        using (var k = Registry.CurrentUser.CreateSubKey(Key))
                            foreach (string n in old.GetValueNames())
                                if (k.GetValue(n) == null) k.SetValue(n, old.GetValue(n));
                    }
                }
                if (hadOld) Registry.CurrentUser.DeleteSubKeyTree(OldKey, false);
            }
            catch { }
            try
            {
                using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (run != null && run.GetValue(OldRunName) != null)
                    {
                        run.DeleteValue(OldRunName, false);
                        run.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                    }
                }
            }
            catch { }
            foreach (var p in Process.GetProcessesByName("UsageTray"))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch { }
                finally { p.Dispose(); }
            }
        }
    }

    class ReleaseInfo
    {
        public string Version;     // "2.1"
        public string Tag;         // "v2.1"
        public string Notes;       // release description (what changed)
        public string PageUrl;     // release page on GitHub
        public string AssetName;   // file for this platform, null if the release has none
        public string AssetUrl;
        public long AssetSize;
    }

    static class Updater
    {
        /// Latest published release on GitHub (drafts and pre-releases are ignored by the API).
        public static ReleaseInfo FetchLatest()
        {
            string body;
            try
            {
                body = Http.Get("https://api.github.com/repos/" + AppInfo.Repo + "/releases/latest",
                                new Dictionary<string, string>());
            }
            catch (HttpException ex)
            {
                if (ex.Code == 404) throw new Exception(L.T("Belum ada rilis di GitHub.", "No release on GitHub yet."));
                if (ex.Code == 403 || ex.Code == 429) throw new Exception(L.T("GitHub membatasi permintaan. Coba lagi nanti.", "GitHub is rate limiting requests. Try again later."));
                throw new Exception(L.T("GitHub membalas HTTP ", "GitHub answered HTTP ") + ex.Code + ".");
            }
            object root = Json.Parse(body);
            var r = new ReleaseInfo();
            r.Tag = Json.Str(root, "tag_name") ?? "";
            r.Version = Normalize(r.Tag) ?? Normalize(Json.Str(root, "name") ?? "");
            if (r.Version == null) throw new Exception(L.T("Tag rilis tidak berisi nomor versi: ", "Release tag has no version number: ") + r.Tag);
            r.Notes = (Json.Str(root, "body") ?? "").Trim();
            r.PageUrl = Json.Str(root, "html_url") ?? AppInfo.RepoUrl + "/releases";

            int best = 0;
            var assets = Json.Get(root, "assets") as object[];
            if (assets != null)
                foreach (object a in assets)
                {
                    string name = Json.Str(a, "name") ?? "";
                    int score = AssetScore(name);
                    if (score > best)
                    {
                        best = score;
                        r.AssetName = name;
                        r.AssetUrl = Json.Str(a, "browser_download_url");
                        r.AssetSize = (long)(Json.Num(a, "size") ?? 0);
                    }
                }
            if (r.AssetUrl == null) r.AssetName = null;
            return r;
        }

        /// How well a release file fits Windows: UsageMonitor.exe > any .exe > a Windows .zip.
        static int AssetScore(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("mac") || n.Contains("darwin") || n.Contains("linux")) return 0;
            string plain = Regex.Replace(n, "[^a-z0-9.]", "");
            if (n.EndsWith(".exe")) return plain.Contains("usagemonitor") ? 3 : 2;
            if (n.EndsWith(".zip") && n.Contains("win")) return 1;
            return 0;
        }

        /// "v.1.0.0" -> "1.0.0", "v2.1" -> "2.1"
        public static string Normalize(string tag)
        {
            Match m = Regex.Match(tag ?? "", @"\d+(\.\d+)*");
            return m.Success ? m.Value : null;
        }

        // Compares version numbers part by part: "2.0" equals "2.0.0", "2.1" is newer, "10.0" beats "9.9".
        public static int Compare(string a, string b)
        {
            string[] pa = (a ?? "0").Split('.'), pb = (b ?? "0").Split('.');
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                long x = 0, y = 0;
                if (i < pa.Length) long.TryParse(pa[i], out x);
                if (i < pb.Length) long.TryParse(pb[i], out y);
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }

        public static bool IsNewer(ReleaseInfo r) { return Compare(r.Version, AppInfo.Version) > 0; }

        public static bool IsSkipped(ReleaseInfo r)
        {
            string s = Settings.SkippedVersion;
            return s != null && Compare(s, r.Version) == 0;
        }

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        public static string DownloadsDir()
        {
            try
            {
                IntPtr p;
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
                {
                    string s = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        /// "UsageMonitor.exe" -> "UsageMonitor (1).exe" when the name is taken, like a browser does.
        static string UniquePath(string dir, string name)
        {
            string path = Path.Combine(dir, name);
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            for (int i = 1; File.Exists(path) || File.Exists(path + ".part"); i++)
                path = Path.Combine(dir, stem + " (" + i + ")" + ext);
            return path;
        }

        /// Downloads the release file into the Downloads folder. progress gets 0-100, or -1 if the size is unknown.
        public static string Download(ReleaseInfo r, Action<int> progress)
        {
            string dir = DownloadsDir();
            Directory.CreateDirectory(dir);
            string name = Path.GetFileName(r.AssetName);
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            string path = UniquePath(dir, name);
            string tmp = path + ".part";

            var req = (HttpWebRequest)WebRequest.Create(r.AssetUrl);
            req.UserAgent = AppInfo.UserAgent;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            req.AllowAutoRedirect = true;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var src = resp.GetResponseStream())
                using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                {
                    long total = resp.ContentLength > 0 ? resp.ContentLength : r.AssetSize, done = 0;
                    var buf = new byte[65536];
                    int n, last = -2;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, n);
                        done += n;
                        int pct = total > 0 ? (int)Math.Min(100, done * 100 / total) : -1;
                        if (pct != last) { last = pct; progress(pct); }
                    }
                }
                File.Move(tmp, path);
                return path;
            }
            catch (WebException ex)
            {
                try { File.Delete(tmp); } catch { }
                throw new Exception(L.T("Unduhan gagal: ", "Download failed: ") + ex.Message);
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(url); } catch { }
        }

        public static void ShowInFolder(string path)
        {
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }
    }
}
