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
    static class Json
    {
        static readonly JavaScriptSerializer Ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        public static object Parse(string s) { return Ser.DeserializeObject(s); }
        static object Get(object o, string k)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(k, out v) ? v : null;
        }
        public static Dictionary<string, object> Obj(object o, string k) { return Get(o, k) as Dictionary<string, object>; }
        public static string Str(object o, string k) { var v = Get(o, k); return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture); }
        public static double? Num(object o, string k)
        {
            var v = Get(o, k);
            if (v == null) return null;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return null; }
        }
    }
}
