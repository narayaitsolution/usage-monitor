using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace UsageMonitor
{
    /// Small dark, always-on-top window used for "Tentang", "Update" and "Donatur".
    class DarkForm : Form
    {
        protected static readonly Color Bg = Color.FromArgb(32, 32, 32);
        protected static readonly Color Card = Color.FromArgb(44, 44, 44);
        protected static readonly Color Fg = Color.FromArgb(240, 240, 240);
        protected static readonly Color Muted = Color.FromArgb(160, 160, 160);
        protected static readonly Color Accent = Color.FromArgb(76, 139, 245);
        protected static readonly Color Ok = Color.FromArgb(63, 185, 80);
        protected static readonly Color ErrColor = Color.FromArgb(255, 138, 128);

        protected const int ContentW = 360, Pad = 18;
        protected readonly float s = 1f;
        protected readonly Font TitleFont, BodyFont, BoldFont, SmallFont;
        protected int y;   // layout cursor (pixels)

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        public DarkForm(string title)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Bg;
            ForeColor = Fg;
            KeyPreview = true;
            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            TitleFont = new Font("Segoe UI Semibold", F(17), GraphicsUnit.Pixel);
            BodyFont = new Font("Segoe UI", F(12.5f), GraphicsUnit.Pixel);
            BoldFont = new Font("Segoe UI Semibold", F(12.5f), GraphicsUnit.Pixel);
            SmallFont = new Font("Segoe UI", F(11.5f), GraphicsUnit.Pixel);
            Font = BodyFont;
            Icon = AppIcon.Create(32);
            KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int on = 1; // dark title bar (Windows 10 2004+ uses 20, older builds 19)
                if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4);
            }
            catch { }
        }

        protected int Sc(float v) { return (int)Math.Round(v * s); }
        protected float F(float v) { return v * s; }

        protected void Begin() { SuspendLayout(); Controls.Clear(); y = Sc(Pad); }

        protected void End()
        {
            ClientSize = new Size(Sc(ContentW + 2 * Pad), y + Sc(Pad));
            ResumeLayout();
        }

        /// Adds a wrapped label at the layout cursor.
        protected Label AddText(string text, Font font, Color color, int gapAfter, int minHeight = 0)
        {
            int w = Sc(ContentW);
            Size sz = TextRenderer.MeasureText(text, font, new Size(w, 0), TextFormatFlags.WordBreak);
            sz.Height = Math.Max(sz.Height, Sc(minHeight));
            var l = new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent,
                                AutoSize = false, Location = new Point(Sc(Pad), y), Size = new Size(w, sz.Height + Sc(2)),
                                UseMnemonic = false };
            Controls.Add(l);
            y += l.Height + Sc(gapAfter);
            return l;
        }

        protected LinkLabel AddLink(string text, string url, int advance)
        {
            var link = new LinkLabel { Text = text, Font = BodyFont, AutoSize = true, Location = new Point(Sc(Pad), y),
                                       LinkColor = Color.FromArgb(120, 170, 250), ActiveLinkColor = Color.FromArgb(160, 195, 255),
                                       LinkBehavior = LinkBehavior.HoverUnderline };
            link.LinkClicked += delegate { Updater.OpenUrl(url); };
            Controls.Add(link);
            y += Sc(advance);
            return link;
        }

        protected TextBox AddBox(string text, int height, bool multiline, int gapAfter)
        {
            var panel = new Panel { BackColor = Card, Location = new Point(Sc(Pad), y),
                                    Size = new Size(Sc(ContentW), multiline ? Sc(height) : Sc(30)) };
            var tb = new TextBox { Text = text, ReadOnly = true, Multiline = multiline, BorderStyle = BorderStyle.None,
                                   BackColor = Card, ForeColor = Fg, Font = multiline ? BodyFont : SmallFont,
                                   Location = new Point(Sc(8), Sc(multiline ? 6 : 8)),
                                   Size = new Size(panel.Width - Sc(multiline ? 10 : 16), panel.Height - Sc(multiline ? 12 : 12)),
                                   ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, TabStop = false };
            panel.Controls.Add(tb);
            Controls.Add(panel);
            y += panel.Height + Sc(gapAfter);
            return tb;
        }

        /// Right-aligned row of buttons; the first is the primary action (rightmost, accent color).
        protected Button[] AddButtons(params string[] labels)
        {
            var result = new Button[labels.Length];
            int x = Sc(Pad + ContentW), h = Sc(30);
            for (int i = 0; i < labels.Length; i++)
            {
                bool primary = i == 0;
                var b = new Button { Text = labels[i], Font = primary ? BoldFont : BodyFont, FlatStyle = FlatStyle.Flat,
                                     BackColor = primary ? Accent : Color.FromArgb(62, 62, 62), ForeColor = Color.White,
                                     Height = h, UseMnemonic = false, Cursor = Cursors.Hand };
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(98, 156, 248) : Color.FromArgb(78, 78, 78);
                b.Width = Math.Max(Sc(84), TextRenderer.MeasureText(labels[i], b.Font).Width + Sc(28));
                x -= b.Width;
                b.Location = new Point(x, y);
                x -= Sc(8);
                Controls.Add(b);
                result[i] = b;
            }
            y += h;
            return result;
        }

        /// Runs work on a background thread, then done(result, error) on the UI thread if the window is still open.
        protected void RunAsync<T>(Func<T> work, Action<T, Exception> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                T result = default(T);
                Exception error = null;
                try { result = work(); } catch (Exception ex) { error = ex; }
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) done(result, error); });
                }
                catch { }
            });
        }

        protected void OnUi(Action a)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) a(); }); }
            catch { }
        }
    }

    /// The app icon: two pairs of usage bars, like the tray icon.
    static class AppIcon
    {
        public static Bitmap Bitmap(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float r = size * 0.22f;
                using (var bg = new SolidBrush(Color.FromArgb(44, 44, 44)))
                using (var path = RoundRect(new RectangleF(0, 0, size - 1, size - 1), r)) g.FillPath(bg, path);
                float m = size * 0.17f, w = size - 2 * m, bh = size * 0.11f, gap = size * 0.06f, group = size * 0.12f;
                float y = (size - (4 * bh + 2 * gap + group)) / 2;
                var pcts = new[] { 0.35f, 0.62f, 0.28f, 0.85f };
                for (int i = 0; i < 4; i++)
                {
                    using (var track = new SolidBrush(Color.FromArgb(80, 80, 80))) g.FillRectangle(track, m, y, w, bh);
                    using (var fill = new SolidBrush(PopupForm.LevelColor(pcts[i] * 100, Color.Empty)))
                        g.FillRectangle(fill, m, y, w * pcts[i], bh);
                    y += bh + (i == 1 ? group : gap);
                }
            }
            return bmp;
        }

        public static Icon Create(int size)
        {
            using (var bmp = Bitmap(size)) return Icon.FromHandle(bmp.GetHicon());
        }

        static GraphicsPath RoundRect(RectangleF rc, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(rc.X, rc.Y, d, d, 180, 90);
            p.AddArc(rc.Right - d, rc.Y, d, d, 270, 90);
            p.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0, 90);
            p.AddArc(rc.X, rc.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// "Update tersedia" window: offer -> downloading -> downloaded (move the file manually; the app is portable).
    class UpdateForm : DarkForm
    {
        public event EventHandler ExitRequested;
        public readonly ReleaseInfo Release;
        string error;
        /// True while the download runs (the window is not rebuilt for a language change then).
        public bool Busy { get; private set; }

        public UpdateForm(ReleaseInfo r) : base("Update " + AppInfo.Name)
        {
            Release = r;
            ShowOffer();
        }

        void ShowOffer()
        {
            Begin();
            AddText(L.T("Update tersedia", "Update available"), TitleFont, Fg, 4);
            AddText(L.T(AppInfo.Name + " " + Release.Version + " sudah tersedia. Versi Anda: " + AppInfo.Version + ".",
                        AppInfo.Name + " " + Release.Version + " is available. You have version " + AppInfo.Version + "."), BodyFont, Muted, 14);
            AddText(L.T("Yang baru di versi " + Release.Version + ":", "What's new in version " + Release.Version + ":"), BoldFont, Fg, 6);
            string notes = Release.Notes.Length > 0 ? Release.Notes : L.T("(Tidak ada catatan rilis.)", "(No release notes.)");
            AddBox(notes.Replace("\r\n", "\n").Replace("\n", "\r\n"), 150, true, 12);
            if (Release.AssetUrl == null)
                AddText(L.T("Rilis ini tidak berisi file untuk Windows. Tombol Update membuka halaman rilis di browser.",
                            "This release has no file for Windows. The Update button opens the release page in your browser."), SmallFont, Muted, 10);
            if (error != null) AddText(error, SmallFont, ErrColor, 10);
            AddText(L.T("Skip: tawaran untuk versi " + Release.Version + " tidak muncul lagi. Tutup jendela ini untuk diingatkan lagi saat aplikasi dibuka berikutnya.",
                        "Skip: version " + Release.Version + " won't be offered again. Close this window to be reminded the next time the app starts."),
                    SmallFont, Muted, 14);
            Button[] b = AddButtons("Update", "Skip");
            b[0].Click += delegate { StartDownload(); };
            b[1].Click += delegate { Settings.SkippedVersion = Release.Version; Close(); };
            AcceptButton = b[0];
            End();
        }

        void StartDownload()
        {
            if (Release.AssetUrl == null) { Updater.OpenUrl(Release.PageUrl); Close(); return; }
            error = null;
            Busy = true;
            Begin();
            AddText(L.T("Mengunduh update…", "Downloading update…"), TitleFont, Fg, 4);
            AddText(Release.AssetName + L.T(" → folder Downloads", " → Downloads folder"), BodyFont, Muted, 14);
            var bar = new ProgressBar { Location = new Point(Sc(Pad), y), Size = new Size(Sc(ContentW), Sc(8)),
                                        Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
            Controls.Add(bar);
            y += bar.Height + Sc(8);
            Label pctLabel = AddText(" ", SmallFont, Muted, 0);
            End();

            RunAsync(() => Updater.Download(Release, pct => OnUi(delegate
            {
                if (pct < 0) return;
                if (bar.Style != ProgressBarStyle.Continuous) bar.Style = ProgressBarStyle.Continuous;
                bar.Value = pct;
                pctLabel.Text = pct + "%";
            })), (path, ex) =>
            {
                Busy = false;
                if (ex != null) { error = ex.Message; ShowOffer(); return; }
                ShowDone(path);
            });
        }

        void ShowDone(string path)
        {
            string exe = Application.ExecutablePath;
            string exeName = Path.GetFileName(exe), newName = Path.GetFileName(path), dir = Path.GetDirectoryName(exe);
            bool isExe = newName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            bool sameName = string.Equals(newName, exeName, StringComparison.OrdinalIgnoreCase);
            Begin();
            AddText(L.T("Update berhasil diunduh", "Update downloaded"), TitleFont, Fg, 4);
            AddText(L.T("File tersimpan di:", "Saved to:"), BodyFont, Muted, 6);
            AddBox(path, 0, false, 14);
            AddText(L.T(AppInfo.Name + " adalah aplikasi portable, jadi pindahkan file update secara manual:",
                        AppInfo.Name + " is a portable app, so move the update into place yourself:"), BodyFont, Fg, 6);
            string step2 = isExe
                ? L.T("2. Pindahkan " + newName + " ke folder " + dir + " dan ganti " + exeName + " yang lama"
                      + (sameName ? "." : " (beri nama " + exeName + ")."),
                      "2. Move " + newName + " to the folder " + dir + " and replace the old " + exeName
                      + (sameName ? "." : " (name it " + exeName + ")."))
                : L.T("2. Ekstrak " + newName + ", lalu ganti " + exeName + " di folder " + dir + ".",
                      "2. Extract " + newName + ", then replace " + exeName + " in the folder " + dir + ".");
            AddText(L.T("1. Keluar dari " + AppInfo.Name + " (tombol di bawah, atau klik kanan icon tray → Keluar).",
                        "1. Exit " + AppInfo.Name + " (button below, or right-click the tray icon → Exit).")
                    + "\r\n" + step2 + "\r\n"
                    + L.T("3. Jalankan " + exeName + " lagi.", "3. Run " + exeName + " again."), BodyFont, Muted, 16);
            Button[] b = AddButtons(L.T("Buka folder", "Open folder"), L.T("Keluar dari ", "Exit ") + AppInfo.Name, L.T("Tutup", "Close"));
            b[0].Click += delegate { Updater.ShowInFolder(path); };
            b[1].Click += delegate { Close(); if (ExitRequested != null) ExitRequested(this, EventArgs.Empty); };
            b[2].Click += delegate { Close(); };
            AcceptButton = b[0];
            End();
        }
    }

    /// "Tentang" window from the (i) button: app info and a manual update check.
    class AboutForm : DarkForm
    {
        public event Action<ReleaseInfo> UpdateFound;
        Label status;
        Button check;

        public AboutForm() : base(L.T("Tentang ", "About ") + AppInfo.Name)
        {
            Begin();
            var pic = new PictureBox { Image = AppIcon.Bitmap(Sc(48)), Size = new Size(Sc(48), Sc(48)),
                                       Location = new Point(Sc(Pad), y), BackColor = Color.Transparent };
            Controls.Add(pic);
            var name = new Label { Text = AppInfo.Name, Font = TitleFont, ForeColor = Fg, AutoSize = true,
                                   Location = new Point(Sc(Pad + 60), y + Sc(3)) };
            var ver = new Label { Text = L.T("Versi ", "Version ") + AppInfo.Version, Font = BodyFont, ForeColor = Muted, AutoSize = true,
                                  Location = new Point(Sc(Pad + 60), y + Sc(27)) };
            Controls.Add(name);
            Controls.Add(ver);
            y += Sc(48 + 14);
            AddText(L.T("Memantau sisa batas pemakaian (5 jam & 7 hari) akun Claude (claude CLI) dan ChatGPT (Codex CLI) dari system tray.",
                        "Shows the remaining usage limits (5 hours & 7 days) of your Claude (claude CLI) and ChatGPT (Codex CLI) accounts in the system tray."),
                    BodyFont, Fg, 10);
            AddLink(AppInfo.RepoUrl, AppInfo.RepoUrl, 24);
            AddText(L.T("© 2026 Naraya IT Solution · Lisensi MIT. Tidak berafiliasi dengan Anthropic maupun OpenAI.",
                        "© 2026 Naraya IT Solution · MIT License. Not affiliated with Anthropic or OpenAI."), SmallFont, Muted, 14);
            status = AddText(L.T("Update dicek otomatis setiap aplikasi dibuka.", "Updates are checked automatically every time the app starts."),
                             SmallFont, Muted, 12, 34);
            Button[] b = AddButtons(L.T("Periksa update", "Check for updates"), L.T("Tutup", "Close"));
            check = b[0];
            check.Click += delegate { CheckNow(); };
            b[1].Click += delegate { Close(); };
            End();
        }

        void SetStatus(string text, Color c) { status.Text = text; status.ForeColor = c; }

        void CheckNow()
        {
            check.Enabled = false;
            SetStatus(L.T("Memeriksa update…", "Checking for updates…"), Muted);
            RunAsync(() => Updater.FetchLatest(), (r, ex) =>
            {
                check.Enabled = true;
                if (ex != null) { SetStatus(L.T("Gagal memeriksa update: ", "Update check failed: ") + ex.Message, ErrColor); return; }
                if (!Updater.IsNewer(r))
                {
                    SetStatus(L.T("✓ Anda sudah memakai versi terbaru (", "✓ You have the latest version (") + AppInfo.Version + ").", Ok);
                    return;
                }
                SetStatus(L.T("Versi " + r.Version + " tersedia.", "Version " + r.Version + " is available."), Accent);
                if (UpdateFound != null) UpdateFound(r);
            });
        }
    }

    /// One supporter from donors.json.
    class Donor
    {
        public string Name;
        public string Note;   // optional message or amount
        public string Date;   // optional, as written in the file
    }

    class DonorList
    {
        public string DonateUrl = AppInfo.DonateUrl;
        public List<Donor> Donors = new List<Donor>();
    }

    static class DonorSource
    {
        /// Reads donors.json from the repository:
        /// { "donate_url": "https://...", "donors": [ { "name": "Budi", "note": "Semangat!", "date": "2026-11-19" } ] }
        public static DonorList Fetch()
        {
            string body;
            try { body = Http.Get(AppInfo.DonorsUrl + "?t=" + DateTime.UtcNow.Ticks, new Dictionary<string, string>()); }
            catch (HttpException ex)
            {
                if (ex.Code == 404) return new DonorList(); // file not published yet
                throw new Exception(L.T("GitHub membalas HTTP ", "GitHub answered HTTP ") + ex.Code + ".");
            }
            object root = Json.Parse(body);
            var list = new DonorList();
            string url = Json.Str(root, "donate_url");
            if (!string.IsNullOrEmpty(url) && (url.StartsWith("https://") || url.StartsWith("http://"))) list.DonateUrl = url;
            var donors = Json.Get(root, "donors") as object[];
            if (donors != null)
                foreach (object d in donors)
                {
                    string name = d is string ? (string)d : Json.Str(d, "name");
                    if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) continue;
                    list.Donors.Add(new Donor { Name = name.Trim(), Note = Json.Str(d, "note") ?? Json.Str(d, "message"),
                                                Date = Json.Str(d, "date") });
                }
            return list;
        }
    }

    /// Heart button: thanks the supporters listed in donors.json and links to the donation page.
    class DonorsForm : DarkForm
    {
        DonorList list;
        string error;
        bool loading = true;

        public DonorsForm() : base(L.T("Donatur ", "Donors of ") + AppInfo.Name)
        {
            Build();
            RunAsync(() => DonorSource.Fetch(), (r, ex) =>
            {
                loading = false;
                list = r;
                if (ex != null) error = L.T("Daftar donatur gagal dimuat: ", "Couldn't load the donor list: ") + ex.Message;
                Build();
            });
        }

        string DonateUrl { get { return list != null ? list.DonateUrl : AppInfo.DonateUrl; } }

        void Build()
        {
            Begin();
            AddText("♥ " + L.T("Dukung " + AppInfo.Name, "Support " + AppInfo.Name), TitleFont, Fg, 4);
            AddText(L.T(AppInfo.Name + " gratis dan open source. Donasi Anda membantu aplikasi ini terus dirawat dan dikembangkan.",
                        AppInfo.Name + " is free and open source. Your donation helps keep it maintained and improved."),
                    BodyFont, Muted, 14);
            AddText(L.T("Terima kasih kepada para donatur:", "Thank you to our donors:"), BoldFont, Fg, 6);
            string text;
            if (loading) text = L.T("Memuat daftar donatur…", "Loading the donor list…");
            else if (error != null) text = error;
            else if (list.Donors.Count == 0) text = L.T("Belum ada donatur. Jadilah yang pertama!", "No donors yet. Be the first!");
            else
            {
                var sb = new System.Text.StringBuilder();
                foreach (var d in list.Donors)
                {
                    sb.Append("♥ ").Append(d.Name);
                    if (!string.IsNullOrEmpty(d.Date)) sb.Append("  ·  ").Append(d.Date);
                    if (!string.IsNullOrEmpty(d.Note)) sb.Append("\r\n    ").Append(d.Note.Replace("\r", " ").Replace("\n", " "));
                    sb.Append("\r\n");
                }
                text = sb.ToString().TrimEnd();
            }
            AddBox(text, 170, true, 10);
            AddText(L.T("Ingin nama Anda tercantum? Tulis nama di catatan donasi.", "Want your name listed? Add it to the donation note."),
                    SmallFont, Muted, 14);
            Button[] b = AddButtons(L.T("Donasi", "Donate"), L.T("Tutup", "Close"));
            b[0].Click += delegate { Updater.OpenUrl(DonateUrl); };
            b[1].Click += delegate { Close(); };
            AcceptButton = b[0];
            End();
        }
    }
}
