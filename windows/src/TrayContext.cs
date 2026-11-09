using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace UsageTray
{
    class TrayContext : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "UsageTray";
        const string SettingsKey = @"Software\UsageTray";
        const int RefreshMs = 5 * 60 * 1000;

        readonly NotifyIcon tray;
        readonly PopupForm popup;
        readonly ContextMenuStrip menu;
        readonly ToolStripLabel providerHeader;
        readonly System.Windows.Forms.Timer timer;
        readonly ToolStripMenuItem startupItem;
        readonly List<ToolStripMenuItem> providerItems = new List<ToolStripMenuItem>();
        readonly HashSet<string> hidden;   // providers the user unticked (new installs show by default)
        readonly Dictionary<string, ProviderStatus> data = new Dictionary<string, ProviderStatus>();
        readonly Dictionary<string, bool> alerted = new Dictionary<string, bool>();
        List<ProviderDef> installed;
        IntPtr currentHIcon = IntPtr.Zero;
        DateTime? lastUpdated;
        bool busy, pendingRefresh, keepMenuOpen;

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public TrayContext()
        {
            hidden = LoadHidden();
            installed = Providers.Installed();

            popup = new PopupForm();
            popup.RefreshRequested += delegate { RefreshAsync(); };

            menu = new ContextMenuStrip();
            menu.Items.Add("Tampilkan usage", null, delegate { TogglePopup(); });
            menu.Items.Add("Refresh sekarang", null, delegate { RefreshAsync(); });
            menu.Items.Add(new ToolStripSeparator());
            providerHeader = new ToolStripLabel("Tampilkan akun:") { ForeColor = SystemColors.GrayText };
            menu.Items.Add(providerHeader);
            // provider checkboxes are inserted after the header in BuildProviderItems()
            menu.Items.Add(new ToolStripSeparator());
            startupItem = new ToolStripMenuItem("Jalankan saat Windows start", null, delegate { ToggleStartup(); });
            startupItem.Checked = IsStartupEnabled();
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Keluar", null, delegate { ExitApp(); });
            BuildProviderItems();

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
            tray.Text = "Usage Monitor - memuat...";
            UpdateView();
            tray.Visible = true;
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) TogglePopup(); };

            timer = new System.Windows.Forms.Timer { Interval = RefreshMs };
            timer.Tick += delegate { installed = Providers.Installed(); RefreshAsync(); };
            timer.Start();
            RefreshAsync();
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
            foreach (var it in providerItems) { menu.Items.Remove(it); it.Dispose(); }
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
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    string v = k == null ? null : k.GetValue("HiddenProviders") as string;
                    if (!string.IsNullOrEmpty(v)) foreach (string id in v.Split(',')) set.Add(id.Trim());
                }
            }
            catch { }
            return set;
        }

        void SaveHidden()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey))
                    k.SetValue("HiddenProviders", string.Join(",", hidden.ToArray()));
            }
            catch { }
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
                    prev.Note = "Dibatasi server (429) · data " + prev.FetchedAt.ToString("HH:mm")
                                + " · coba lagi " + retry.ToString("HH:mm");
                    return;
                }
                r.Error = "Dibatasi server (429). Dicoba lagi " + retry.ToString("HH:mm") + ".";
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
            CheckAlert(d, "5 jam", p.Five);
            CheckAlert(d, "7 hari", p.Week);
        }

        void CheckAlert(ProviderDef d, string label, UsageWindow w)
        {
            string key = d.Id + label;
            bool high = w.Percent.HasValue && w.Percent.Value >= 90;
            bool was;
            alerted.TryGetValue(key, out was);
            if (high && !was)
                tray.ShowBalloonTip(5000, "Usage " + d.Title + " tinggi",
                    d.Title + " " + label + ": " + Math.Round(w.Percent.Value) + "% terpakai. " + PopupForm.ResetText(w.ResetAt),
                    ToolTipIcon.Warning);
            alerted[key] = high;
        }

        string TipLine(ProviderDef d)
        {
            var p = Get(d);
            if (p == null) return d.Short + ": memuat...";
            if (p.Error != null) return d.Short + ": error";
            return d.Short + " 5h " + Pct(p.Five.Percent) + " | 7d " + Pct(p.Week.Percent);
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
                    int n = defs.Count;
                    int gap = Math.Max(1, (int)Math.Round(u)), groupGap = Math.Max(1, (int)Math.Round(2 * u));
                    int fixedSpace = n * gap + (n - 1) * groupGap;
                    int barH = Math.Max(1, (sz.Height - fixedSpace) / (2 * n));
                    int y = Math.Max(0, (sz.Height - (2 * n * barH + fixedSpace)) / 2);
                    foreach (var d in defs)
                    {
                        var st = Get(d);
                        bool err = st != null && st.Error != null;
                        DrawBar(g, st == null || err ? null : st.Five.Percent, err, y, sz.Width, barH);
                        y += barH + gap;
                        DrawBar(g, st == null || err ? null : st.Week.Percent, err, y, sz.Width, barH);
                        y += barH + groupGap;
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
            tray.Visible = false;
            tray.Dispose();
            if (currentHIcon != IntPtr.Zero) DestroyIcon(currentHIcon);
            popup.Close();
            ExitThread();
        }
    }
}
