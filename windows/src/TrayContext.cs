using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace UsageMonitor
{
    class TrayContext : ApplicationContext
    {
        const string RunKey = Settings.RunKey;
        const string RunName = Settings.RunName;
        const int RefreshMs = 5 * 60 * 1000;

        readonly NotifyIcon tray;
        readonly PopupForm popup;
        readonly ContextMenuStrip menu;
        ToolStripLabel providerHeader;
        readonly System.Windows.Forms.Timer timer;
        ToolStripMenuItem startupItem;
        readonly List<ToolStripMenuItem> providerItems = new List<ToolStripMenuItem>();
        readonly HashSet<string> hidden;   // providers the user unticked (new installs show by default)
        readonly Dictionary<string, ProviderStatus> data = new Dictionary<string, ProviderStatus>();
        readonly Dictionary<string, bool> alerted = new Dictionary<string, bool>();
        List<ProviderDef> installed;
        IntPtr currentHIcon = IntPtr.Zero;
        DateTime? lastUpdated;
        bool busy, pendingRefresh, keepMenuOpen;
        AboutForm about;
        UpdateForm updateForm;
        DonorsForm donors;

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public TrayContext()
        {
            hidden = LoadHidden();
            installed = Providers.Installed();

            popup = new PopupForm();
            popup.RefreshRequested += delegate { RefreshAsync(); };
            popup.InfoRequested += delegate { ShowAbout(); };
            popup.DonorsRequested += delegate { ShowDonors(); };
            // The gear button opens the same menu as a right-click on the tray icon
            popup.SettingsRequested += delegate(Point p) { menu.Show(p, ToolStripDropDownDirection.BelowLeft); };

            menu = new ContextMenuStrip();
            BuildMenu();

            // Re-detect installed CLIs every time the menu opens
            menu.Opening += delegate
            {
                installed = Providers.Installed();
                BuildProviderItems();
                UpdateView();
            };
            // Keep the menu open while ticking provider checkboxes (multi-select)
            menu.ItemClicked += delegate(object s, ToolStripItemClickedEventArgs e) { keepMenuOpen = e.ClickedItem.Tag is ProviderDef; };
            menu.Closing += delegate(object s, ToolStripDropDownClosingEventArgs e)
            {
                if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && keepMenuOpen) e.Cancel = true;
                keepMenuOpen = false;
            };

            tray = new NotifyIcon();
            tray.ContextMenuStrip = menu;
            tray.Text = L.T("Usage Monitor - memuat...", "Usage Monitor - loading...");
            UpdateView();
            tray.Visible = true;
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) TogglePopup(); };

            timer = new System.Windows.Forms.Timer { Interval = RefreshMs };
            timer.Tick += delegate { installed = Providers.Installed(); RefreshAsync(); };
            timer.Start();
            RefreshAsync();

            // Check GitHub for a newer version shortly after start up
            var once = new System.Windows.Forms.Timer { Interval = 4000 };
            once.Tick += delegate { once.Stop(); once.Dispose(); CheckUpdateInBackground(); };
            once.Start();
        }

        // ------------------------------------------------------------ menu & language
        /// Right-click menu (also opened by the gear button in the popup). Rebuilt when the language changes.
        void BuildMenu()
        {
            menu.Items.Clear();
            providerItems.Clear();
            menu.Items.Add(L.T("Tampilkan usage", "Show usage"), null, delegate { TogglePopup(); });
            menu.Items.Add(L.T("Refresh sekarang", "Refresh now"), null, delegate { RefreshAsync(); });
            menu.Items.Add(new ToolStripSeparator());
            providerHeader = new ToolStripLabel(L.T("Tampilkan akun:", "Show accounts:")) { ForeColor = SystemColors.GrayText };
            menu.Items.Add(providerHeader);
            // provider checkboxes are inserted after the header in BuildProviderItems()
            menu.Items.Add(new ToolStripSeparator());
            startupItem = new ToolStripMenuItem(L.T("Jalankan saat Windows start", "Run at Windows startup"), null, delegate { ToggleStartup(); });
            startupItem.Checked = IsStartupEnabled();
            menu.Items.Add(startupItem);
            menu.Items.Add(BuildLanguageMenu());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Donatur && donasi…", "Donors && donate…"), null, delegate { ShowDonors(); });
            menu.Items.Add(L.T("Tentang && update…", "About && update…"), null, delegate { ShowAbout(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Keluar", "Exit"), null, delegate { ExitApp(); });
            BuildProviderItems();
        }

        ToolStripMenuItem BuildLanguageMenu()
        {
            var root = new ToolStripMenuItem(L.T("Bahasa", "Language"));
            string sys = L.SystemIsIndonesian ? "Bahasa Indonesia" : "English";
            string[][] choices =
            {
                new[] { "", L.T("Ikuti sistem", "Follow system") + " (" + sys + ")" },
                new[] { "id", "Bahasa Indonesia" },
                new[] { "en", "English" },
            };
            foreach (string[] c in choices)
            {
                string code = c[0];
                var item = new ToolStripMenuItem(c[1]) { Checked = L.Choice == code };
                item.Click += delegate { SetLanguage(code); };
                root.DropDownItems.Add(item);
            }
            return root;
        }

        void SetLanguage(string code)
        {
            if (L.Choice == code) return;
            bool wasId = L.Id;
            L.SetChoice(code);
            BuildMenu();
            if (L.Id == wasId) return;
            // Reopen windows in the new language; texts from the last fetch are redone by a refresh
            if (about != null && !about.IsDisposed) { about.Close(); about = null; }
            if (donors != null && !donors.IsDisposed) { donors.Close(); donors = null; }
            if (updateForm != null && !updateForm.IsDisposed)
            {
                ReleaseInfo r = updateForm.Release;
                if (!updateForm.Busy) { updateForm.Close(); ShowUpdate(r); }
            }
            foreach (var st in data.Values) st.Note = null;
            UpdateView();
            RefreshAsync();
        }

        void ShowDonors()
        {
            popup.Hide();
            if (donors == null || donors.IsDisposed) donors = new DonorsForm();
            donors.Show();
            donors.Activate();
        }

        // ------------------------------------------------------------ update & about
        void CheckUpdateInBackground()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                ReleaseInfo r;
                try { r = Updater.FetchLatest(); }
                catch { return; } // offline or GitHub unavailable: try again next start
                if (!Updater.IsNewer(r) || Updater.IsSkipped(r)) return;
                try { popup.BeginInvoke((MethodInvoker)delegate { ShowUpdate(r); }); } catch { }
            });
        }

        void ShowUpdate(ReleaseInfo r)
        {
            if (updateForm != null && !updateForm.IsDisposed)
            {
                if (Updater.Compare(updateForm.Release.Version, r.Version) == 0) { updateForm.Activate(); return; }
                updateForm.Close();
            }
            updateForm = new UpdateForm(r);
            updateForm.ExitRequested += delegate { ExitApp(); };
            updateForm.Show();
            updateForm.Activate();
        }

        void ShowAbout()
        {
            popup.Hide();
            if (about == null || about.IsDisposed)
            {
                about = new AboutForm();
                about.UpdateFound += delegate(ReleaseInfo r) { about.Close(); ShowUpdate(r); };
            }
            about.Show();
            about.Activate();
        }

        // ------------------------------------------------------------ provider selection
        /// Installed providers that are ticked. Never empty.
        List<ProviderDef> Shown()
        {
            var list = installed.Where(d => !hidden.Contains(d.Id)).ToList();
            return list.Count > 0 ? list : new List<ProviderDef> { installed[0] };
        }

        void BuildProviderItems()
        {
            foreach (var it in providerItems) { if (menu.Items.Contains(it)) menu.Items.Remove(it); it.Dispose(); }
            providerItems.Clear();
            var shown = Shown();
            int index = menu.Items.IndexOf(providerHeader) + 1;
            foreach (var def in installed)
            {
                var item = new ToolStripMenuItem(def.MenuTitle) { Tag = def, Checked = shown.Contains(def) };
                item.Click += delegate(object s, EventArgs e) { ToggleProvider((ProviderDef)((ToolStripMenuItem)s).Tag); };
                providerItems.Add(item);
                menu.Items.Insert(index++, item);
            }
        }

        void ToggleProvider(ProviderDef def)
        {
            bool isShown = Shown().Contains(def);
            if (isShown)
            {
                if (Shown().Count == 1) { System.Media.SystemSounds.Beep.Play(); return; } // at least one
                hidden.Add(def.Id);
            }
            else hidden.Remove(def.Id);
            SaveHidden();
            foreach (var it in providerItems) it.Checked = Shown().Contains((ProviderDef)it.Tag);
            UpdateView();
            if (!isShown && !data.ContainsKey(def.Id) && CanFetch(def)) RefreshAsync();
        }

        HashSet<string> LoadHidden()
        {
            var set = new HashSet<string>();
            string v = Settings.Get("HiddenProviders");
            if (!string.IsNullOrEmpty(v)) foreach (string id in v.Split(',')) set.Add(id.Trim());
            return set;
        }

        void SaveHidden()
        {
            Settings.Set("HiddenProviders", string.Join(",", hidden.ToArray()));
        }

        // ------------------------------------------------------------ popup & refresh
        void TogglePopup()
        {
            if (popup.Visible) { popup.Hide(); return; }
            if (popup.JustHidden()) return; // click on tray icon closed it via Deactivate
            popup.SetData(Shown(), data, lastUpdated, busy);
            popup.ShowNearTray();
            if (!busy && (!lastUpdated.HasValue || (DateTime.Now - lastUpdated.Value).TotalSeconds > 60))
                RefreshAsync();
        }

        // Back-off after HTTP 429: skip that provider for 5, 10, 20, then max 30 minutes.
        readonly Dictionary<string, DateTime> nextAllowed = new Dictionary<string, DateTime>();
        readonly Dictionary<string, int> backoffMin = new Dictionary<string, int>();

        bool CanFetch(ProviderDef d)
        {
            DateTime t;
            return !nextAllowed.TryGetValue(d.Id, out t) || DateTime.Now >= t;
        }

        void Store(ProviderDef d, ProviderStatus r)
        {
            ProviderStatus prev = Get(d);
            if (r.RateLimited)
            {
                int prevMin;
                backoffMin.TryGetValue(d.Id, out prevMin);
                int min = prevMin == 0 ? 5 : Math.Min(30, prevMin * 2);
                backoffMin[d.Id] = min;
                DateTime retry = DateTime.Now.AddMinutes(min);
                nextAllowed[d.Id] = retry;
                if (prev != null && prev.Error == null)
                {
                    // keep showing the last good data
                    prev.Note = L.T("Dibatasi server (429) · data ", "Rate limited (429) · data ") + prev.FetchedAt.ToString("HH:mm")
                                + L.T(" · coba lagi ", " · retry ") + retry.ToString("HH:mm");
                    return;
                }
                r.Error = L.T("Dibatasi server (429). Dicoba lagi ", "Rate limited by the server (429). Retrying at ") + retry.ToString("HH:mm") + ".";
            }
            else if (r.KeepPrevious && prev != null && prev.Error == null)
            {
                // soft error (e.g. expired token): keep showing the last good data
                prev.Note = r.Error + L.T(" · data ", " · data from ") + prev.FetchedAt.ToString("HH:mm");
                return;
            }
            else
            {
                backoffMin.Remove(d.Id);
                nextAllowed.Remove(d.Id);
                r.FetchedAt = DateTime.Now;
            }
            data[d.Id] = r;
        }

        void RefreshAsync()
        {
            if (busy) { pendingRefresh = true; return; }
            var defs = Shown().Where(CanFetch).ToList();
            if (defs.Count == 0) { UpdateView(); return; } // everything is backing off
            busy = true;
            pendingRefresh = false;
            popup.SetData(defs, data, lastUpdated, true);
            ThreadPool.QueueUserWorkItem(delegate
            {
                var results = new Dictionary<string, ProviderStatus>();
                var threads = new List<Thread>();
                foreach (var d in defs)
                {
                    var def = d;
                    var t = new Thread(() => { var r = def.Fetch(); lock (results) results[def.Id] = r; });
                    threads.Add(t);
                    t.Start();
                }
                foreach (var t in threads) t.Join();
                try
                {
                    popup.BeginInvoke((MethodInvoker)delegate
                    {
                        foreach (var d in defs)
                        {
                            ProviderStatus r;
                            if (results.TryGetValue(d.Id, out r)) Store(d, r);
                        }
                        lastUpdated = DateTime.Now;
                        busy = false;
                        UpdateView();
                        foreach (var d in defs) CheckAlerts(d);
                        if (pendingRefresh) RefreshAsync();
                    });
                }
                catch { busy = false; }
            });
        }

        void UpdateView()
        {
            var defs = Shown();
            SetIcon(defs);
            tray.Text = Truncate(string.Join("\n", defs.Select(d => TipLine(d)).ToArray()), 63);
            popup.SetData(defs, data, lastUpdated, busy);
        }

        ProviderStatus Get(ProviderDef d)
        {
            ProviderStatus s;
            return data.TryGetValue(d.Id, out s) ? s : null;
        }

        void CheckAlerts(ProviderDef d)
        {
            var p = Get(d);
            if (p == null || p.Error != null) return;
            var rows = p.Rows();
            for (int i = 0; i < rows.Count; i++) CheckAlert(d, i, rows[i]);
        }

        // Keyed by row position, not label, so switching the language doesn't repeat an alert
        void CheckAlert(ProviderDef d, int index, UsageWindow w)
        {
            string label = w.Label;
            string key = d.Id + "#" + index;
            bool high = w.Percent.HasValue && w.Percent.Value >= 90;
            bool was;
            alerted.TryGetValue(key, out was);
            if (high && !was)
                tray.ShowBalloonTip(5000, L.T("Usage " + d.Title + " tinggi", d.Title + " usage is high"),
                    d.Title + " " + label + ": " + Math.Round(w.Percent.Value) + L.T("% terpakai. ", "% used. ") + PopupForm.ResetText(w.ResetAt),
                    ToolTipIcon.Warning);
            alerted[key] = high;
        }

        string TipLine(ProviderDef d)
        {
            var p = Get(d);
            if (p == null) return d.Short + L.T(": memuat...", ": loading...");
            if (p.Error != null) return d.Short + ": error";
            if (p.FixedRows) return d.Short + " 5h " + Pct(p.Five.Percent) + " | 7d " + Pct(p.Week.Percent);
            var rows = p.Rows();
            return d.Short + (rows.Count == 0 ? " -" : " 7d " + Pct(rows[0].Percent)
                              + (rows.Count > 1 ? " | " + Pct(rows[1].Percent) : ""));
        }

        /// Bars of one provider in the tray icon: 5h & 7d, or the first two limits (Antigravity: one per model group).
        List<double?> IconValues(ProviderDef d, out bool err)
        {
            var st = Get(d);
            err = st != null && st.Error != null;
            var v = new List<double?>();
            if (st == null || err) { v.Add(null); v.Add(null); return v; }
            if (st.FixedRows) { v.Add(st.Five.Percent); v.Add(st.Week.Percent); return v; }
            foreach (var r in st.Rows()) { if (v.Count < 2) v.Add(r.Percent); }
            if (v.Count == 0) v.Add(null);
            return v;
        }

        static string Pct(double? v) { return v.HasValue ? Math.Round(v.Value) + "%" : "-"; }
        static string Truncate(string s, int n) { return s.Length <= n ? s : s.Substring(0, n); }

        // ------------------------------------------------------------ tray icon
        // Two bars (5h, 7d) per shown provider, stacked in menu order.
        void SetIcon(List<ProviderDef> defs)
        {
            Size sz = SystemInformation.SmallIconSize;
            using (var bmp = new Bitmap(sz.Width, sz.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.None;
                    float u = sz.Height / 16f;
                    var groups = new List<List<double?>>();
                    var errs = new List<bool>();
                    int bars = 0;
                    foreach (var d in defs)
                    {
                        bool err;
                        var v = IconValues(d, out err);
                        groups.Add(v); errs.Add(err); bars += v.Count;
                    }
                    int n = groups.Count;
                    int gap = Math.Max(1, (int)Math.Round(u)), groupGap = Math.Max(1, (int)Math.Round(2 * u));
                    if (bars > 4) groupGap = gap; // 3 providers: keep the bars visible in 16 px
                    int fixedSpace = (bars - n) * gap + (n - 1) * groupGap;
                    int barH = Math.Max(1, (sz.Height - fixedSpace) / bars);
                    int y = Math.Max(0, (sz.Height - (bars * barH + fixedSpace)) / 2);
                    for (int i = 0; i < n; i++)
                    {
                        for (int j = 0; j < groups[i].Count; j++)
                        {
                            if (j > 0) y += gap;
                            DrawBar(g, groups[i][j], errs[i], y, sz.Width, barH);
                            y += barH;
                        }
                        y += groupGap;
                    }
                }
                IntPtr h = bmp.GetHicon();
                Icon old = tray.Icon;
                tray.Icon = Icon.FromHandle(h);
                if (old != null) old.Dispose();
                if (currentHIcon != IntPtr.Zero) DestroyIcon(currentHIcon);
                currentHIcon = h;
            }
        }

        static void DrawBar(Graphics g, double? pct, bool err, int y, int width, int h)
        {
            using (var track = new SolidBrush(err ? Color.FromArgb(200, 120, 40, 40) : Color.FromArgb(150, 90, 90, 90)))
                g.FillRectangle(track, 0, y, width, h);
            if (!pct.HasValue) return;
            double v = Math.Max(0, Math.Min(100, pct.Value));
            float w = (float)(width * v / 100.0);
            if (v > 0 && w < 1) w = 1;
            using (var b = new SolidBrush(PopupForm.LevelColor(v, Color.Empty)))
                g.FillRectangle(b, 0, y, w, h);
        }

        // ------------------------------------------------------------ startup & exit
        bool IsStartupEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunName) != null;
        }

        void ToggleStartup()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (IsStartupEnabled()) k.DeleteValue(RunName, false);
                else k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
            }
            startupItem.Checked = IsStartupEnabled();
        }

        void ExitApp()
        {
            timer.Stop();
            if (about != null && !about.IsDisposed) about.Close();
            if (updateForm != null && !updateForm.IsDisposed) updateForm.Close();
            if (donors != null && !donors.IsDisposed) donors.Close();
            tray.Visible = false;
            tray.Dispose();
            if (currentHIcon != IntPtr.Zero) DestroyIcon(currentHIcon);
            popup.Close();
            ExitThread();
        }
    }
}
