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
    class HttpException : Exception
    {
        public int Code;
        public HttpException(int code, string msg) : base(msg) { Code = code; }
    }

    static class Http
    {
        public static string Get(string url, Dictionary<string, string> headers)
        {
            return Send("GET", url, headers, null);
        }

        /// POST with a JSON body.
        public static string Post(string url, Dictionary<string, string> headers, string json)
        {
            return Send("POST", url, headers, json);
        }

        static string Send(string method, string url, Dictionary<string, string> headers, string json)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.Timeout = 20000;
            req.ReadWriteTimeout = 20000;
            req.Accept = "application/json";
            req.UserAgent = AppInfo.UserAgent;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            foreach (var kv in headers)
            {
                if (string.Equals(kv.Key, "User-Agent", StringComparison.OrdinalIgnoreCase)) req.UserAgent = kv.Value;
                else req.Headers[kv.Key] = kv.Value;
            }
            try
            {
                if (json != null)
                {
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    req.ContentType = "application/json";
                    req.ContentLength = bytes.Length;
                    using (var rs = req.GetRequestStream()) rs.Write(bytes, 0, bytes.Length);
                }
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                    return sr.ReadToEnd();
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                if (r == null) throw new Exception(L.T("Koneksi gagal: ", "Connection failed: ") + ex.Message);
                string body = "";
                try { using (var sr = new StreamReader(r.GetResponseStream())) body = sr.ReadToEnd(); } catch { }
                if (body.Length > 120) body = body.Substring(0, 120);
                throw new HttpException((int)r.StatusCode, body.Length > 0 ? body : r.StatusDescription);
            }
        }
    }
}
