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
    class HttpException : Exception
    {
        public int Code;
        public HttpException(int code, string msg) : base(msg) { Code = code; }
    }

    static class Http
    {
        public static string Get(string url, Dictionary<string, string> headers)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = 20000;
            req.ReadWriteTimeout = 20000;
            req.Accept = "application/json";
            req.UserAgent = "UsageTray/1.0";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            foreach (var kv in headers) req.Headers[kv.Key] = kv.Value;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                    return sr.ReadToEnd();
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                if (r == null) throw new Exception("Koneksi gagal: " + ex.Message);
                string body = "";
                try { using (var sr = new StreamReader(r.GetResponseStream())) body = sr.ReadToEnd(); } catch { }
                if (body.Length > 120) body = body.Substring(0, 120);
                throw new HttpException((int)r.StatusCode, body.Length > 0 ? body : r.StatusDescription);
            }
        }
    }
}
