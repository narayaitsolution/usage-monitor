using System;
using System.Globalization;

namespace UsageMonitor
{
    /// UI language. The "Language" setting is "" (follow Windows), "id" (Bahasa Indonesia) or "en" (English).
    static class L
    {
        static bool? cached;

        /// The saved choice: "", "id" or "en".
        public static string Choice
        {
            get
            {
                string v = Settings.Get("Language");
                return v == "id" || v == "en" ? v : "";
            }
        }

        public static void SetChoice(string choice)
        {
            Settings.Set("Language", choice == "id" || choice == "en" ? choice : "");
            cached = null;
        }

        /// True when Windows' display language is Indonesian.
        public static bool SystemIsIndonesian
        {
            get
            {
                string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                return lang == "id" || lang == "in";
            }
        }

        public static bool Id
        {
            get
            {
                if (!cached.HasValue)
                {
                    string c = Choice;
                    cached = c == "id" || (c == "" && SystemIsIndonesian);
                }
                return cached.Value;
            }
        }

        /// Text in the current language.
        public static string T(string indonesian, string english) { return Id ? indonesian : english; }

        static readonly CultureInfo IdCulture = new CultureInfo("id-ID");
        static readonly CultureInfo EnCulture = new CultureInfo("en-US");

        /// Culture for day and month names ("Kam 13:00" / "Thu 13:00").
        public static CultureInfo Culture { get { return Id ? IdCulture : EnCulture; } }
    }
}
