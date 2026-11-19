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

namespace UsageMonitor
{
    class PopupForm : Form
    {
        public event EventHandler RefreshRequested;
        public event EventHandler InfoRequested;
        public event EventHandler DonorsRequested;
        /// Gear button: the argument is the screen point under the button where the settings menu opens.
        public event Action<Point> SettingsRequested;

        static readonly Color Bg = Color.FromArgb(32, 32, 32);
        static readonly Color Card = Color.FromArgb(44, 44, 44);
        static readonly Color Border = Color.FromArgb(64, 64, 64);
        static readonly Color Fg = Color.FromArgb(240, 240, 240);
        static readonly Color Muted = Color.FromArgb(160, 160, 160);
        static readonly Color Track = Color.FromArgb(68, 68, 68);
        static readonly Color ErrColor = Color.FromArgb(255, 138, 128);
        static readonly Color Heart = Color.FromArgb(240, 98, 146);

        List<ProviderDef> defs = new List<ProviderDef>();
        List<ProviderStatus> statuses = new List<ProviderStatus>();
        DateTime? updated;
        bool busy;
        RectangleF refreshRect, infoRect, settingsRect, donorsRect;
        int hover;   // 0 none, 1 refresh, 2 info, 3 settings, 4 donors
        readonly ToolTip tip = new ToolTip();
        int hiddenAt;
        float s = 1f;
        readonly System.Windows.Forms.Timer tick;

        // Compact layout (logical pixels at 96 DPI)
        const int W = 300, Pad = 10, HeaderH = 36, FooterH = 24, CardGap = 8;
        const int CardTop = 9, CardHeader = 20, RowH = 30, LineH = 17, CardBottom = 5;

        public PopupForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Bg;
            DoubleBuffered = true;
            KeyPreview = true;
            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            Width = Sc(W);
            Height = Sc(200);
            tick = new System.Windows.Forms.Timer { Interval = 30000 };
            tick.Tick += delegate { Invalidate(); };
            KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Hide(); };
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; /* CS_DROPSHADOW */ cp.ExStyle |= 0x80; /* WS_EX_TOOLWINDOW */ return cp; }
        }

        int Sc(float v) { return (int)Math.Round(v * s); }
        float F(float v) { return v * s; }

        public bool JustHidden() { return Environment.TickCount - hiddenAt < 300; }

        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Hide(); }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) tick.Start(); else { tick.Stop(); hiddenAt = Environment.TickCount; }
        }

        public void SetData(List<ProviderDef> d, Dictionary<string, ProviderStatus> data, DateTime? u, bool b)
        {
            defs = d; updated = u; busy = b;
            statuses = new List<ProviderStatus>();
            int cards = 0;
            foreach (var def in defs)
            {
                ProviderStatus st;
                data.TryGetValue(def.Id, out st);
                statuses.Add(st);
                cards += CardH(st);
            }
            int h = Sc(HeaderH + cards + CardGap * Math.Max(0, defs.Count - 1) + FooterH);
            if (Height != h)
            {
                int bottom = Bottom;
                Height = h;
                if (Visible) Top = bottom - h; // keep anchored to taskbar
            }
            Invalidate();
        }

        public void ShowNearTray()
        {
            Point c = Cursor.Position;
            Rectangle wa = Screen.FromPoint(c).WorkingArea;
            int m = Sc(10);
            int x = Math.Min(Math.Max(c.X - Width / 2, wa.Left + m), wa.Right - Width - m);
            int y = (c.Y > wa.Top + wa.Height / 2) ? wa.Bottom - Height - m : wa.Top + m;
            Location = new Point(x, y);
            Show();
            Activate();
        }

        static int CardH(ProviderStatus p)
        {
            if (p == null || p.Error != null) return CardTop + CardHeader + 34 + CardBottom;
            return CardTop + CardHeader + p.Rows().Count * RowH
                   + p.Infos.Count * LineH + (p.Note != null ? LineH : 0) + CardBottom;
        }

        /// Compact reset label: "↻ 2j 12m · 13:00" / "↻ 3h 2j · Kam 13:00" (h = hari, j = jam, m = menit);
        /// English: "↻ 2h 12m · 13:00" / "↻ 3d 2h · Thu 13:00".
        public static string ShortReset(DateTime? t)
        {
            if (!t.HasValue) return "";
            TimeSpan sp = t.Value - DateTime.Now;
            if (sp.TotalSeconds <= 0) return L.T("sudah reset", "reset done");
            string dd = L.T("h", "d"), hh = L.T("j", "h");
            string d;
            if (sp.TotalDays >= 1) d = (int)sp.TotalDays + dd + " " + sp.Hours + hh;
            else if (sp.TotalHours >= 1) d = (int)sp.TotalHours + hh + " " + sp.Minutes + "m";
            else d = Math.Max(1, (int)Math.Ceiling(sp.TotalMinutes)) + "m";
            DateTime r = t.Value.AddSeconds(30);
            string when = r.Date == DateTime.Today ? r.ToString("HH':'mm") : r.ToString("ddd HH':'mm", L.Culture);
            return "↻ " + d + " · " + when;
        }

        public static Color LevelColor(double pct, Color accent)
        {
            // Same thresholds for 5h and 7d, Claude and ChatGPT (accent no longer used for bars)
            if (pct > 75) return Color.FromArgb(229, 72, 77);    // merah
            if (pct >= 50) return Color.FromArgb(240, 136, 62);  // orange
            return Color.FromArgb(63, 185, 80);                  // hijau
        }

        public static string ResetText(DateTime? t)
        {
            if (!t.HasValue) return "";
            TimeSpan sp = t.Value - DateTime.Now;
            if (sp.TotalSeconds <= 0) return L.T("Sudah reset - refresh untuk update", "Already reset - refresh to update");
            string d;
            if (sp.TotalDays >= 1) d = (int)sp.TotalDays + L.T(" hari ", " d ") + sp.Hours + L.T(" jam", " h");
            else if (sp.TotalHours >= 1) d = (int)sp.TotalHours + L.T(" jam ", " h ") + sp.Minutes + L.T(" mnt", " min");
            else d = Math.Max(1, (int)Math.Ceiling(sp.TotalMinutes)) + L.T(" mnt", " min");
            DateTime r = t.Value.AddSeconds(30);
            string when = r.Date == DateTime.Today ? r.ToString("HH':'mm")
                : r.ToString(L.Id ? "ddd d MMM, HH':'mm" : "ddd MMM d, HH':'mm", L.Culture);
            return L.T("Reset dalam ", "Resets in ") + d + "  ·  " + when;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = refreshRect.Contains(e.Location) ? 1 : infoRect.Contains(e.Location) ? 2
                  : settingsRect.Contains(e.Location) ? 3 : donorsRect.Contains(e.Location) ? 4 : 0;
            if (h == hover) return;
            hover = h;
            Cursor = h != 0 ? Cursors.Hand : Cursors.Default;
            tip.SetToolTip(this, h == 1 ? (busy ? L.T("Memuat…", "Loading…") : "Refresh")
                               : h == 2 ? L.T("Tentang & update", "About & update")
                               : h == 3 ? L.T("Pengaturan", "Settings")
                               : h == 4 ? L.T("Donatur & donasi", "Donors & donate") : "");
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != 0) { hover = 0; Cursor = Cursors.Default; tip.SetToolTip(this, ""); Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (refreshRect.Contains(e.Location) && !busy && RefreshRequested != null) RefreshRequested(this, EventArgs.Empty);
            else if (infoRect.Contains(e.Location) && InfoRequested != null) InfoRequested(this, EventArgs.Empty);
            else if (donorsRect.Contains(e.Location) && DonorsRequested != null) DonorsRequested(this, EventArgs.Empty);
            else if (settingsRect.Contains(e.Location) && SettingsRequested != null)
                SettingsRequested(PointToScreen(new Point((int)settingsRect.Right, (int)settingsRect.Bottom)));
        }

        /// Header icon buttons, right to left: gear (settings), (i) info, refresh arrow, heart (donors).
        void DrawHeaderButtons(Graphics g)
        {
            float bw = F(26), bh = F(24), top = F(6), gap = F(1);
            settingsRect = new RectangleF(F(W - Pad + 4) - bw, top, bw, bh);
            infoRect = new RectangleF(settingsRect.X - bw - gap, top, bw, bh);
            refreshRect = new RectangleF(infoRect.X - bw - gap, top, bw, bh);
            donorsRect = new RectangleF(refreshRect.X - bw - gap, top, bw, bh);
            using (var hb = new SolidBrush(Color.FromArgb(60, 60, 60)))
            {
                RectangleF[] rects = { RectangleF.Empty, refreshRect, infoRect, settingsRect, donorsRect };
                if (hover > 0 && !(hover == 1 && busy)) using (var p = Round(rects[hover], F(6))) g.FillPath(hb, p);
            }
            using (var sym = new Font("Segoe UI Symbol", F(15), GraphicsUnit.Pixel))
            using (var gearF = new Font("Segoe UI Symbol", F(16), GraphicsUnit.Pixel))
            using (var heartF = new Font("Segoe UI Symbol", F(14), GraphicsUnit.Pixel))
            using (var br = new SolidBrush(busy ? Muted : Fg))
            using (var fgb = new SolidBrush(Fg))
            using (var hrt = new SolidBrush(Heart))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("↻", sym, br, new RectangleF(refreshRect.X, refreshRect.Y - F(1), refreshRect.Width, refreshRect.Height), sf);
                g.DrawString("\u2699", gearF, fgb, new RectangleF(settingsRect.X, settingsRect.Y - F(1), settingsRect.Width, settingsRect.Height), sf);
                g.DrawString("\u2665", heartF, hrt, donorsRect, sf);
            }

            // (i): circle outline, dot and stem
            float d = F(14), cx = infoRect.X + infoRect.Width / 2, cy = infoRect.Y + infoRect.Height / 2;
            using (var pen = new Pen(Fg, F(1.4f)))
                g.DrawEllipse(pen, cx - d / 2, cy - d / 2, d, d);
            using (var b = new SolidBrush(Fg))
            {
                float dot = F(2);
                g.FillEllipse(b, cx - dot / 2, cy - F(4.2f), dot, dot);
                g.FillRectangle(b, cx - F(0.8f), cy - F(1.2f), F(1.6f), F(5));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var pen = new Pen(Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            using (var title = new Font("Segoe UI Semibold", F(13), GraphicsUnit.Pixel))
            using (var fg = new SolidBrush(Fg))
            using (var muted = new SolidBrush(Muted))
            {
                g.DrawString("Usage Monitor", title, fg, F(Pad), F(10));

                DrawHeaderButtons(g);

                float y = F(HeaderH);
                for (int i = 0; i < defs.Count; i++)
                {
                    if (i > 0) y += F(CardGap);
                    y = DrawCard(g, statuses[i], defs[i].Title, defs[i].Accent, y);
                }

                string foot = updated.HasValue
                    ? L.T("Diperbarui ", "Updated ") + updated.Value.ToString("HH:mm") + L.T("  ·  tiap 5 menit", "  ·  every 5 minutes")
                    : L.T("Belum ada data", "No data yet");
                using (var tiny = new Font("Segoe UI", F(10.5f), GraphicsUnit.Pixel))
                    g.DrawString(foot, tiny, muted, F(Pad), y + F(5));
            }
        }

        float DrawCard(Graphics g, ProviderStatus p, string title, Color accent, float y)
        {
            float x = F(Pad), w = F(W - 2 * Pad), h = F(CardH(p));
            var rect = new RectangleF(x, y, w, h);
            using (var cb = new SolidBrush(Card))
            using (var path = Round(rect, F(7))) g.FillPath(cb, path);

            float ix = x + F(10), iw = w - F(20), iy = y + F(CardTop);
            using (var nameF = new Font("Segoe UI Semibold", F(12.5f), GraphicsUnit.Pixel))
            using (var lbl = new Font("Segoe UI", F(11.5f), GraphicsUnit.Pixel))
            using (var pctF = new Font("Segoe UI Semibold", F(12), GraphicsUnit.Pixel))
            using (var tiny = new Font("Segoe UI", F(10.5f), GraphicsUnit.Pixel))
            using (var fg = new SolidBrush(Fg))
            using (var muted = new SolidBrush(Muted))
            using (var dot = new SolidBrush(accent))
            {
                g.FillEllipse(dot, ix, iy + F(5), F(7), F(7));
                g.DrawString(title, nameF, fg, ix + F(11), iy);
                float nx = ix + F(11) + g.MeasureString(title, nameF).Width;

                if (p != null && !string.IsNullOrEmpty(p.Plan))
                {
                    string plan = p.Plan.ToUpperInvariant();
                    SizeF ps = g.MeasureString(plan, tiny);
                    var pr = new RectangleF(nx + F(2), iy + F(2), ps.Width + F(6), F(15));
                    using (var pb = new SolidBrush(Color.FromArgb(70, accent)))
                    using (var pp = Round(pr, F(4))) g.FillPath(pb, pp);
                    g.DrawString(plan, tiny, fg, pr.X + F(3), pr.Y + (pr.Height - ps.Height) / 2);
                    nx = pr.Right;
                }
                if (p != null && !string.IsNullOrEmpty(p.Account))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                    g.DrawString(p.Account, tiny, muted, new RectangleF(nx + F(8), iy + F(2), ix + iw - nx - F(8), F(18)), sf);
                }

                iy += F(CardHeader);
                if (p == null)
                {
                    g.DrawString(busy ? L.T("Memuat…", "Loading…") : L.T("Belum ada data", "No data yet"), lbl, muted, ix, iy + F(6));
                }
                else if (p.Error != null)
                {
                    using (var eb = new SolidBrush(ErrColor))
                        g.DrawString(p.Error, lbl, eb, new RectangleF(ix, iy + F(2), iw, F(34)));
                }
                else
                {
                    foreach (var row in p.Rows())
                        iy = DrawRow(g, row, accent, ix, iw, iy, lbl, pctF, tiny, fg, muted);
                    foreach (string info in p.Infos)
                    {
                        g.DrawString(info, tiny, muted, ix - F(2), iy);
                        iy += F(LineH);
                    }
                    if (p.Note != null)
                        using (var nb = new SolidBrush(Color.FromArgb(240, 170, 90)))
                            g.DrawString("⚠ " + p.Note, tiny, nb, ix - F(2), iy);
                }
            }
            return y + h;
        }

        /// One compact row:  label .......... ↻ 2j 12m · 13:00   38%
        ///                   [=====-------------------------------]
        float DrawRow(Graphics g, UsageWindow u, Color accent, float x, float w, float y,
                      Font lbl, Font pctF, Font tiny, Brush fg, Brush muted)
        {
            g.DrawString(u.Label, lbl, muted, x - F(2), y);
            string pct = u.Percent.HasValue ? Math.Round(u.Percent.Value) + "%" : "-";
            SizeF ps = g.MeasureString(pct, pctF);
            Color lc = u.Percent.HasValue ? LevelColor(u.Percent.Value, accent) : Muted;
            float px = x + w - ps.Width + F(3);
            using (var pb = new SolidBrush(u.Percent.HasValue && u.Percent.Value >= 50 ? lc : Fg))
                g.DrawString(pct, pctF, pb, px, y - F(1));
            string reset = ShortReset(u.ResetAt);
            SizeF rs = g.MeasureString(reset, tiny);
            g.DrawString(reset, tiny, muted, px - rs.Width - F(2), y + F(1));

            float by = y + F(17), bh = F(5);
            var track = new RectangleF(x, by, w, bh);
            using (var tb = new SolidBrush(Track))
            using (var tp = Round(track, bh / 2)) g.FillPath(tb, tp);
            if (u.Percent.HasValue && u.Percent.Value > 0)
            {
                float fw = Math.Max(bh, (float)(w * Math.Min(100, u.Percent.Value) / 100.0));
                using (var fb = new SolidBrush(lc))
                using (var fp = Round(new RectangleF(x, by, fw, bh), bh / 2)) g.FillPath(fb, fp);
            }
            return y + F(RowH);
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
