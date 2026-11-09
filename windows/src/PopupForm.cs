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
    class PopupForm : Form
    {
        public event EventHandler RefreshRequested;

        static readonly Color Bg = Color.FromArgb(32, 32, 32);
        static readonly Color Card = Color.FromArgb(44, 44, 44);
        static readonly Color Border = Color.FromArgb(64, 64, 64);
        static readonly Color Fg = Color.FromArgb(240, 240, 240);
        static readonly Color Muted = Color.FromArgb(160, 160, 160);
        static readonly Color Track = Color.FromArgb(68, 68, 68);
        static readonly Color ErrColor = Color.FromArgb(255, 138, 128);
        static readonly CultureInfo Id = new CultureInfo("id-ID");

        List<ProviderDef> defs = new List<ProviderDef>();
        List<ProviderStatus> statuses = new List<ProviderStatus>();
        DateTime? updated;
        bool busy;
        RectangleF refreshRect;
        bool hoverRefresh;
        int hiddenAt;
        float s = 1f;
        readonly System.Windows.Forms.Timer tick;

        const int W = 340, Pad = 14, HeaderH = 44, FooterH = 30, CardGap = 10;

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
            if (p == null || p.Error != null) return 12 + 24 + 40 + 6;
            return 12 + 24 + 2 * 58 + 4 + (p.Note != null ? 20 : 0);
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
            if (sp.TotalSeconds <= 0) return "Sudah reset - refresh untuk update";
            string d;
            if (sp.TotalDays >= 1) d = (int)sp.TotalDays + " hari " + sp.Hours + " jam";
            else if (sp.TotalHours >= 1) d = (int)sp.TotalHours + " jam " + sp.Minutes + " mnt";
            else d = Math.Max(1, (int)Math.Ceiling(sp.TotalMinutes)) + " mnt";
            DateTime r = t.Value.AddSeconds(30);
            string when = r.Date == DateTime.Today ? r.ToString("HH':'mm")
                : r.ToString("ddd d MMM, HH':'mm", Id);
            return "Reset dalam " + d + "  ·  " + when;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool h = refreshRect.Contains(e.Location);
            if (h != hoverRefresh) { hoverRefresh = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (refreshRect.Contains(e.Location) && !busy && RefreshRequested != null) RefreshRequested(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var pen = new Pen(Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            using (var title = new Font("Segoe UI Semibold", F(14), GraphicsUnit.Pixel))
            using (var small = new Font("Segoe UI", F(12), GraphicsUnit.Pixel))
            using (var fg = new SolidBrush(Fg))
            using (var muted = new SolidBrush(Muted))
            {
                g.DrawString("Usage Monitor", title, fg, F(Pad), F(14));

                string rt = busy ? "Memuat…" : "↻ Refresh";
                SizeF rs = g.MeasureString(rt, small);
                refreshRect = new RectangleF(F(W - Pad) - rs.Width - F(8), F(10), rs.Width + F(16), F(24));
                if (hoverRefresh && !busy)
                    using (var hb = new SolidBrush(Color.FromArgb(60, 60, 60)))
                    using (var path = Round(refreshRect, F(6))) g.FillPath(hb, path);
                g.DrawString(rt, small, busy ? muted : fg, refreshRect.X + F(8), refreshRect.Y + (refreshRect.Height - rs.Height) / 2);

                float y = F(HeaderH);
                for (int i = 0; i < defs.Count; i++)
                {
                    if (i > 0) y += F(CardGap);
                    y = DrawCard(g, statuses[i], defs[i].Title, defs[i].Accent, y);
                }

                string foot = updated.HasValue
                    ? "Diperbarui " + updated.Value.ToString("HH:mm:ss") + "  ·  otomatis tiap 5 menit"
                    : "Belum ada data";
                using (var tiny = new Font("Segoe UI", F(11), GraphicsUnit.Pixel))
                    g.DrawString(foot, tiny, muted, F(Pad), y + F(8));
            }
        }

        float DrawCard(Graphics g, ProviderStatus p, string title, Color accent, float y)
        {
            float x = F(Pad), w = F(W - 2 * Pad), h = F(CardH(p));
            var rect = new RectangleF(x, y, w, h);
            using (var cb = new SolidBrush(Card))
            using (var path = Round(rect, F(8))) g.FillPath(cb, path);

            float ix = x + F(12), iw = w - F(24), iy = y + F(12);
            using (var nameF = new Font("Segoe UI Semibold", F(13), GraphicsUnit.Pixel))
            using (var lbl = new Font("Segoe UI", F(12), GraphicsUnit.Pixel))
            using (var pctF = new Font("Segoe UI Semibold", F(13), GraphicsUnit.Pixel))
            using (var tiny = new Font("Segoe UI", F(11), GraphicsUnit.Pixel))
            using (var fg = new SolidBrush(Fg))
            using (var muted = new SolidBrush(Muted))
            using (var dot = new SolidBrush(accent))
            {
                g.FillEllipse(dot, ix, iy + F(5), F(8), F(8));
                g.DrawString(title, nameF, fg, ix + F(13), iy);
                float nx = ix + F(13) + g.MeasureString(title, nameF).Width;

                if (p != null && !string.IsNullOrEmpty(p.Plan))
                {
                    string plan = p.Plan.ToUpperInvariant();
                    SizeF ps = g.MeasureString(plan, tiny);
                    var pr = new RectangleF(nx + F(2), iy + F(2), ps.Width + F(6), F(16));
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

                iy += F(24);
                if (p == null)
                {
                    g.DrawString(busy ? "Memuat…" : "Belum ada data", lbl, muted, ix, iy + F(8));
                }
                else if (p.Error != null)
                {
                    using (var eb = new SolidBrush(ErrColor))
                        g.DrawString(p.Error, lbl, eb, new RectangleF(ix, iy + F(2), iw, F(40)));
                }
                else
                {
                    iy = DrawRow(g, "5 jam", p.Five, accent, ix, iw, iy, lbl, pctF, tiny, fg, muted);
                    iy = DrawRow(g, "7 hari", p.Week, accent, ix, iw, iy, lbl, pctF, tiny, fg, muted);
                    if (p.Note != null)
                        using (var nb = new SolidBrush(Color.FromArgb(240, 170, 90)))
                            g.DrawString("⚠ " + p.Note, tiny, nb, ix - F(2), iy);
                }
            }
            return y + h;
        }

        float DrawRow(Graphics g, string label, UsageWindow u, Color accent, float x, float w, float y,
                      Font lbl, Font pctF, Font tiny, Brush fg, Brush muted)
        {
            g.DrawString(label, lbl, muted, x - F(2), y + F(2));
            string pct = u.Percent.HasValue ? Math.Round(u.Percent.Value) + "%" : "-";
            SizeF ps = g.MeasureString(pct, pctF);
            Color lc = u.Percent.HasValue ? LevelColor(u.Percent.Value, accent) : Muted;
            using (var pb = new SolidBrush(u.Percent.HasValue && u.Percent.Value >= 50 ? lc : Fg))
                g.DrawString(pct, pctF, pb, x + w - ps.Width + F(2), y);

            float by = y + F(22), bh = F(8);
            var track = new RectangleF(x, by, w, bh);
            using (var tb = new SolidBrush(Track))
            using (var tp = Round(track, bh / 2)) g.FillPath(tb, tp);
            if (u.Percent.HasValue && u.Percent.Value > 0)
            {
                float fw = Math.Max(bh, (float)(w * Math.Min(100, u.Percent.Value) / 100.0));
                using (var fb = new SolidBrush(lc))
                using (var fp = Round(new RectangleF(x, by, fw, bh), bh / 2)) g.FillPath(fb, fp);
            }
            g.DrawString(ResetText(u.ResetAt), tiny, muted, x - F(2), by + bh + F(3));
            return y + F(58);
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
