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
    class UsageWindow
    {
        public string Label;
        public double? Percent;
        public DateTime? ResetAt;   // local time
        public UsageWindow(string label) { Label = label; }
    }

    class ProviderStatus
    {
        public string Name;
        public string Plan;
        public string Account;
        public string Error;
        public UsageWindow Five = new UsageWindow(L.T("5 jam", "5 hours"));
        public UsageWindow Week = new UsageWindow(L.T("7 hari", "7 days"));
        /// Extra limits after 5h/7d, e.g. Claude's weekly limit for one model ("7 hari · Fable").
        public List<UsageWindow> MoreRows = new List<UsageWindow>();
        /// Extra info lines: Claude "Limit resets" grants, ChatGPT "usage limit reset" credits.
        public List<string> Infos = new List<string>();

        public List<UsageWindow> Rows()
        {
            var list = new List<UsageWindow> { Five, Week };
            list.AddRange(MoreRows);
            return list;
        }
        public Color Accent;
        public bool RateLimited;   // server answered HTTP 429
        public string Note;        // shown under the rows, e.g. when displaying older data
        public DateTime FetchedAt;
    }

    /// Static description of a provider shown in the menu / popup.
    class ProviderDef
    {
        public string Id;
        public string Title;       // card title in popup
        public string MenuTitle;   // label in right-click menu
        public string Short;       // tooltip prefix
        public Color Accent;
        public Func<ProviderStatus> Fetch;
        public Func<bool> Installed;
    }

    static class Providers
    {
        public static readonly ProviderDef[] All =
        {
            new ProviderDef
            {
                Id = "claude", Title = "Claude", MenuTitle = "Claude (claude-cli)", Short = "Claude",
                Accent = Fetcher.ClaudeColor, Fetch = Fetcher.FetchClaude, Installed = Fetcher.ClaudeInstalled
            },
            new ProviderDef
            {
                Id = "codex", Title = "ChatGPT", MenuTitle = "ChatGPT (Codex)", Short = "GPT",
                Accent = Fetcher.GptColor, Fetch = Fetcher.FetchCodex, Installed = Fetcher.CodexInstalled
            },
        };

        /// Providers whose CLI is installed on this computer. If none is detected, all are offered
        /// so the popup can show the "belum login" hints.
        public static List<ProviderDef> Installed()
        {
            var list = new List<ProviderDef>();
            foreach (var d in All) if (d.Installed()) list.Add(d);
            return list.Count > 0 ? list : new List<ProviderDef>(All);
        }
    }
}
