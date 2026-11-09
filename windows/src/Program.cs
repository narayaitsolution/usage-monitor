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
                    sb.AppendLine("  5h: " + p.Five.Percent + "% reset " + p.Five.ResetAt + " | " + PopupForm.ResetText(p.Five.ResetAt));
                    sb.AppendLine("  7d: " + p.Week.Percent + "% reset " + p.Week.ResetAt + " | " + PopupForm.ResetText(p.Week.ResetAt));
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
            bool created;
            using (var mutex = new Mutex(true, "UsageTray_SingleInstance_7f3a", out created))
            {
                if (!created) return;
                try { SetProcessDPIAware(); } catch { }
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }
    }
}
