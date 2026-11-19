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

[assembly: System.Reflection.AssemblyTitle("Usage Monitor")]
[assembly: System.Reflection.AssemblyProduct("Usage Monitor")]
[assembly: System.Reflection.AssemblyCompany("Naraya IT Solution")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 Naraya IT Solution")]
[assembly: System.Reflection.AssemblyVersion("2.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.2.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("2.2")]

namespace UsageMonitor
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--test")
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var sb = new System.Text.StringBuilder();
                var inst = new List<string>();
                foreach (var d in Providers.All) if (d.Installed()) inst.Add(d.Id);
                sb.AppendLine("installed: " + (inst.Count > 0 ? string.Join(",", inst.ToArray()) : "(none)"));
                foreach (var p in new[] { Fetcher.FetchClaude(), Fetcher.FetchCodex() })
                {
                    sb.AppendLine(p.Name + " plan=" + p.Plan + " account=" + p.Account + " error=" + p.Error);
                    foreach (var w in p.Rows())
                        sb.AppendLine("  " + w.Label + ": " + w.Percent + "% | " + PopupForm.ResetText(w.ResetAt)
                                      + " | " + PopupForm.ShortReset(w.ResetAt));
                    foreach (string info in p.Infos) sb.AppendLine("  " + info);
                }
                File.WriteAllText(args[1], sb.ToString());
                return;
            }
            if (args.Length >= 2 && args[0] == "--preview")
            {
                try { SetProcessDPIAware(); } catch { }
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var f = new PopupForm();
                // optional 3rd arg: comma-separated provider ids, e.g. "claude"
                var defs = new List<ProviderDef>(Providers.All);
                if (args.Length > 2) defs = defs.FindAll(d => ("," + args[2] + ",").Contains("," + d.Id + ","));
                var data = new Dictionary<string, ProviderStatus>();
                foreach (var d in defs) data[d.Id] = d.Fetch();
                f.SetData(defs, data, DateTime.Now, false);
                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                }
                return;
            }
            if (args.Length == 2 && args[0] == "--check-update")
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string outText;
                try
                {
                    var r = Updater.FetchLatest();
                    outText = "current=" + AppInfo.Version + " latest=" + r.Version + " tag=" + r.Tag
                              + " newer=" + Updater.IsNewer(r) + " skipped=" + Updater.IsSkipped(r)
                              + "\r\nasset=" + (r.AssetName ?? "(none)") + " " + r.AssetUrl
                              + "\r\ndownloads=" + Updater.DownloadsDir() + "\r\nnotes:\r\n" + r.Notes;
                }
                catch (Exception ex) { outText = "error: " + ex.Message; }
                File.WriteAllText(args[1], outText);
                return;
            }
            if (args.Length == 2 && (args[0] == "--preview-about" || args[0] == "--preview-update" || args[0] == "--preview-donors"))
            {
                // Render the About / Update / Donors window to a PNG (the update window uses a sample release)
                try { SetProcessDPIAware(); } catch { }
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                Form f = args[0] == "--preview-about" ? (Form)new AboutForm()
                    : args[0] == "--preview-donors" ? (Form)new DonorsForm()
                    : new UpdateForm(new ReleaseInfo { Version = "2.1", Tag = "v2.1", AssetName = "UsageMonitor.exe", AssetUrl = "x",
                                                       Notes = "Contoh catatan rilis:\n- Perbaikan tampilan popup\n- Waktu reset lebih akurat" });
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-5000, -5000);
                f.Show();
                // the donors window loads donors.json in the background
                for (int i = 0; i < (args[0] == "--preview-donors" ? 60 : 1); i++) { Application.DoEvents(); Thread.Sleep(100); }
                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                }
                f.Close();
                return;
            }
            bool created;
            using (var mutex = new Mutex(true, "UsageMonitor_SingleInstance_7f3a", out created))
            {
                if (!created) return;
                Settings.MigrateFromUsageTray();
                try { SetProcessDPIAware(); } catch { }
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }
    }
}
