using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace UsageMonitor
{
    /// Small dark, always-on-top window used for "Tentang" and "Update".
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

        public UpdateForm(ReleaseInfo r) : base("Update " + AppInfo.Name)
        {
            Release = r;
            ShowOffer();
        }

        void ShowOffer()
        {
            Begin();
            AddText("Update tersedia", TitleFont, Fg, 4);
            AddText(AppInfo.Name + " " + Release.Version + " sudah tersedia. Versi Anda: " + AppInfo.Version + ".", BodyFont, Muted, 14);
            AddText("Yang baru di versi " + Release.Version + ":", BoldFont, Fg, 6);
            string notes = Release.Notes.Length > 0 ? Release.Notes : "(Tidak ada catatan rilis.)";
            AddBox(notes.Replace("\r\n", "\n").Replace("\n", "\r\n"), 150, true, 12);
            if (Release.AssetUrl == null)
                AddText("Rilis ini tidak berisi file untuk Windows. Tombol Update membuka halaman rilis di browser.", SmallFont, Muted, 10);
            if (error != null) AddText(error, SmallFont, ErrColor, 10);
            AddText("Skip: tawaran untuk versi " + Release.Version + " tidak muncul lagi. Tutup jendela ini untuk diingatkan lagi saat aplikasi dibuka berikutnya.",
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
            Begin();
            AddText("Mengunduh update…", TitleFont, Fg, 4);
            AddText(Release.AssetName + " → folder Downloads", BodyFont, Muted, 14);
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
                if (ex != null) { error = ex.Message; ShowOffer(); return; }
                ShowDone(path);
            });
        }

        void ShowDone(string path)
        {
            string exe = Application.ExecutablePath;
            string exeName = Path.GetFileName(exe), newName = Path.GetFileName(path);
            bool isExe = newName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            Begin();
            AddText("Update berhasil diunduh", TitleFont, Fg, 4);
            AddText("File tersimpan di:", BodyFont, Muted, 6);
            AddBox(path, 0, false, 14);
            AddText(AppInfo.Name + " adalah aplikasi portable, jadi pindahkan file update secara manual:", BodyFont, Fg, 6);
            string step2 = isExe
                ? "2. Pindahkan " + newName + " ke folder " + Path.GetDirectoryName(exe) + " dan ganti " + exeName + " yang lama"
                  + (string.Equals(newName, exeName, StringComparison.OrdinalIgnoreCase) ? "." : " (beri nama " + exeName + ").")
                : "2. Ekstrak " + newName + ", lalu ganti " + exeName + " di folder " + Path.GetDirectoryName(exe) + ".";
            AddText("1. Keluar dari " + AppInfo.Name + " (tombol di bawah, atau klik kanan icon tray → Keluar).\r\n"
                    + step2 + "\r\n3. Jalankan " + exeName + " lagi.", BodyFont, Muted, 16);
            Button[] b = AddButtons("Buka folder", "Keluar dari " + AppInfo.Name, "Tutup");
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

        public AboutForm() : base("Tentang " + AppInfo.Name)
        {
            Begin();
            var pic = new PictureBox { Image = AppIcon.Bitmap(Sc(48)), Size = new Size(Sc(48), Sc(48)),
                                       Location = new Point(Sc(Pad), y), BackColor = Color.Transparent };
            Controls.Add(pic);
            var name = new Label { Text = AppInfo.Name, Font = TitleFont, ForeColor = Fg, AutoSize = true,
                                   Location = new Point(Sc(Pad + 60), y + Sc(3)) };
            var ver = new Label { Text = "Versi " + AppInfo.Version, Font = BodyFont, ForeColor = Muted, AutoSize = true,
                                  Location = new Point(Sc(Pad + 60), y + Sc(27)) };
            Controls.Add(name);
            Controls.Add(ver);
            y += Sc(48 + 14);
            AddText("Memantau sisa batas pemakaian (5 jam & 7 hari) akun Claude (claude CLI) dan ChatGPT (Codex CLI) dari system tray.",
                    BodyFont, Fg, 10);
            var link = new LinkLabel { Text = AppInfo.RepoUrl, Font = BodyFont, AutoSize = true, Location = new Point(Sc(Pad), y),
                                       LinkColor = Color.FromArgb(120, 170, 250), ActiveLinkColor = Color.FromArgb(160, 195, 255),
                                       LinkBehavior = LinkBehavior.HoverUnderline };
            link.LinkClicked += delegate { Updater.OpenUrl(AppInfo.RepoUrl); };
            Controls.Add(link);
            y += Sc(24);
            AddText("© 2026 Naraya IT Solution · Lisensi MIT. Tidak berafiliasi dengan Anthropic maupun OpenAI.", SmallFont, Muted, 14);
            status = AddText("Update dicek otomatis setiap aplikasi dibuka.", SmallFont, Muted, 12, 34);
            Button[] b = AddButtons("Periksa update", "Tutup");
            check = b[0];
            check.Click += delegate { CheckNow(); };
            b[1].Click += delegate { Close(); };
            End();
        }

        void SetStatus(string text, Color c) { status.Text = text; status.ForeColor = c; }

        void CheckNow()
        {
            check.Enabled = false;
            SetStatus("Memeriksa update…", Muted);
            RunAsync(() => Updater.FetchLatest(), (r, ex) =>
            {
                check.Enabled = true;
                if (ex != null) { SetStatus("Gagal memeriksa update: " + ex.Message, ErrColor); return; }
                if (!Updater.IsNewer(r))
                {
                    SetStatus("✓ Anda sudah memakai versi terbaru (" + AppInfo.Version + ").", Ok);
                    return;
                }
                SetStatus("Versi " + r.Version + " tersedia.", Accent);
                if (UpdateFound != null) UpdateFound(r);
            });
        }
    }
}
