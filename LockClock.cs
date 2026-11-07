using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("LockClock Screensaver")]
[assembly: System.Reflection.AssemblyProduct("LockClock")]
[assembly: System.Reflection.AssemblyVersion("1.3.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.3.0.0")]
[assembly: System.Reflection.AssemblyDescription("Full-screen clock screensaver with red night mode and weather")]

namespace LockClock
{
    enum NightMode { Auto = 0, AlwaysDay = 1, AlwaysNight = 2 }

    class Settings
    {
        const string Key = @"Software\LockClock";
        public bool Use24h = true;
        public bool ShowSeconds = false;
        public bool ShowDate = true;
        public bool PixelShift = true;
        public NightMode Mode = NightMode.Auto;
        public int NightStartMin = 21 * 60;
        public int NightEndMin = 7 * 60;
        public int DayBrightness = 80;
        public int NightBrightness = 45;
        public int FontWeight = 2; // 0 Light, 1 Regular, 2 Bold
        public bool TakeOverWinL = false; // Win+L shows the clock; touching it locks
        public bool ShowWeather = true;
        public string WeatherCity = ""; // empty = detect from IP address
        public string Display = "all"; // "all", "primary", or a device name such as \\.\DISPLAY2

        // Which screen gets the clock. Other screens are covered in black so nothing on them shows while away.
        public bool ShowsClockOn(Screen screen)
        {
            if (Display == "all") return true;
            if (Display != "primary")
            {
                foreach (var s in Screen.AllScreens)
                    if (string.Equals(s.DeviceName, Display, StringComparison.OrdinalIgnoreCase))
                        return string.Equals(screen.DeviceName, Display, StringComparison.OrdinalIgnoreCase);
            }
            return screen.Primary; // "primary", or the chosen display is no longer connected
        }
        public int WeatherUnits = 0; // 0 Auto (by region), 1 Celsius, 2 Fahrenheit

        // Automatic: Fahrenheit only where it is the local convention (by the weather location's country).
        public bool UseFahrenheit(string countryCode)
        {
            if (WeatherUnits != 0) return WeatherUnits == 2;
            switch ((countryCode ?? "").ToUpperInvariant())
            {
                case "US": case "LR": case "BS": case "BZ": case "KY": case "PW": case "FM": case "MH": return true;
                default: return false;
            }
        }

        public static Settings Load()
        {
            var s = new Settings();
            using (var k = Registry.CurrentUser.OpenSubKey(Key))
            {
                if (k == null) return s;
                s.Use24h = Get(k, "Use24h", 1) != 0;
                s.ShowSeconds = Get(k, "ShowSeconds", 0) != 0;
                s.ShowDate = Get(k, "ShowDate", 1) != 0;
                s.PixelShift = Get(k, "PixelShift", 1) != 0;
                s.Mode = (NightMode)Clamp(Get(k, "Mode", 0), 0, 2);
                s.NightStartMin = Clamp(Get(k, "NightStartMin", s.NightStartMin), 0, 1439);
                s.NightEndMin = Clamp(Get(k, "NightEndMin", s.NightEndMin), 0, 1439);
                s.DayBrightness = Clamp(Get(k, "DayBrightness", s.DayBrightness), 5, 100);
                s.NightBrightness = Clamp(Get(k, "NightBrightness", s.NightBrightness), 5, 100);
                s.FontWeight = Clamp(Get(k, "FontWeight", 2), 0, 2);
                s.ShowWeather = Get(k, "ShowWeather", 1) != 0;
                s.WeatherCity = (k.GetValue("WeatherCity") as string ?? "").Trim();
                s.Display = (k.GetValue("Display") as string ?? "all").Trim();
                if (s.Display.Length == 0) s.Display = "all";
                s.WeatherUnits = Clamp(Get(k, "WeatherUnits", 0), 0, 2);
                s.TakeOverWinL = Get(k, "TakeOverWinL", 0) != 0;
            }
            return s;
        }

        public void Save()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Key))
            {
                k.SetValue("Use24h", Use24h ? 1 : 0);
                k.SetValue("ShowSeconds", ShowSeconds ? 1 : 0);
                k.SetValue("ShowDate", ShowDate ? 1 : 0);
                k.SetValue("PixelShift", PixelShift ? 1 : 0);
                k.SetValue("Mode", (int)Mode);
                k.SetValue("NightStartMin", NightStartMin);
                k.SetValue("NightEndMin", NightEndMin);
                k.SetValue("DayBrightness", DayBrightness);
                k.SetValue("NightBrightness", NightBrightness);
                k.SetValue("FontWeight", FontWeight);
                k.SetValue("ShowWeather", ShowWeather ? 1 : 0);
                k.SetValue("WeatherCity", WeatherCity ?? "");
                k.SetValue("Display", Display ?? "all");
                k.SetValue("WeatherUnits", WeatherUnits);
                k.SetValue("TakeOverWinL", TakeOverWinL ? 1 : 0);
            }
        }

        public Settings Clone() { return (Settings)MemberwiseClone(); }

        static int Get(RegistryKey k, string n, int d)
        {
            object v = k.GetValue(n);
            return v is int ? (int)v : d;
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        static bool InRange(int m, int start, int end)
        {
            return start <= end ? (m >= start && m < end) : (m >= start || m < end);
        }

        // 0 = day, 1 = night; fades over 30 minutes before night starts and after it ends.
        public double NightFactor(DateTime now)
        {
            if (Mode == NightMode.AlwaysDay) return 0;
            if (Mode == NightMode.AlwaysNight) return 1;
            if (NightStartMin == NightEndMin) return 0;
            const double fade = 30;
            double m = now.Hour * 60 + now.Minute + now.Second / 60.0;
            if (InRange((int)m, NightStartMin, NightEndMin)) return 1;
            double untilStart = ((NightStartMin - m) % 1440 + 1440) % 1440;
            double sinceEnd = ((m - NightEndMin) % 1440 + 1440) % 1440;
            if (untilStart < fade) return 1 - untilStart / fade;
            if (sinceEnd < fade) return 1 - sinceEnd / fade;
            return 0;
        }
    }

    class WeatherDay { public DateTime Date; public int Code; public double Max, Min; }

    class WeatherData
    {
        public string Key, Place, Country = "";
        public double Temp;
        public int Code;
        public bool IsDay = true;
        public DateTime FetchedUtc;
        public List<WeatherDay> Days = new List<WeatherDay>();
    }

    // Weather from Open-Meteo (free, no API key). Cached on disk so a new screensaver process shows it instantly.
    static class WeatherService
    {
        static readonly object gate = new object();
        static WeatherData current;
        static bool busy;
        static string cacheCheckedKey, lastAttemptKey;
        static DateTime lastAttemptUtc = DateTime.MinValue;
        public static string LastError;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static WeatherService()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
        }

        static string CachePath { get { return Path.Combine(Installer.Dir, "weather.json"); } }

        public static string KeyFor(Settings s)
        {
            return "v2|" + (s.WeatherCity ?? "").Trim().ToLowerInvariant();
        }

        public static WeatherData Get(Settings s)
        {
            string key = KeyFor(s);
            lock (gate)
            {
                if ((current == null || current.Key != key) && cacheCheckedKey != key)
                {
                    cacheCheckedKey = key;
                    var c = LoadCache();
                    if (c != null && c.Key == key) current = c;
                }
                var have = current != null && current.Key == key ? current : null;
                bool stale = have == null || (DateTime.UtcNow - have.FetchedUtc).TotalMinutes > 30;
                bool backoff = lastAttemptKey == key && (DateTime.UtcNow - lastAttemptUtc).TotalMinutes < (have == null ? 1 : 5);
                if (stale && !busy && !backoff)
                {
                    busy = true;
                    lastAttemptKey = key;
                    lastAttemptUtc = DateTime.UtcNow;
                    string city = (s.WeatherCity ?? "").Trim();
                    ThreadPool.QueueUserWorkItem(delegate { Fetch(key, city); });
                }
                if (have != null && (DateTime.UtcNow - have.FetchedUtc).TotalHours > 6) return null;
                return have;
            }
        }

        static void Fetch(string key, string city)
        {
            try
            {
                double lat, lon;
                string place, country;
                if (city.Length == 0) LocateByIp(out lat, out lon, out place, out country);
                else Geocode(city, out lat, out lon, out place, out country);

                string url = string.Format(Inv,
                    "https://api.open-meteo.com/v1/forecast?latitude={0:F4}&longitude={1:F4}" +
                    "&current=temperature_2m,weather_code,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min" +
                    "&timezone=auto&forecast_days=4", lat, lon);
                var j = GetJson(url);
                var cur = (Dictionary<string, object>)j["current"];
                var daily = (Dictionary<string, object>)j["daily"];
                var d = new WeatherData
                {
                    Key = key,
                    Place = place,
                    Country = country,
                    FetchedUtc = DateTime.UtcNow,
                    Temp = Num(cur["temperature_2m"]),
                    Code = (int)Num(cur["weather_code"]),
                    IsDay = Num(cur["is_day"]) != 0
                };
                var times = (object[])daily["time"];
                var codes = (object[])daily["weather_code"];
                var maxs = (object[])daily["temperature_2m_max"];
                var mins = (object[])daily["temperature_2m_min"];
                for (int i = 0; i < times.Length; i++)
                    d.Days.Add(new WeatherDay
                    {
                        Date = DateTime.ParseExact((string)times[i], "yyyy-MM-dd", Inv),
                        Code = (int)Num(codes[i]),
                        Max = Num(maxs[i]),
                        Min = Num(mins[i])
                    });
                lock (gate) { current = d; LastError = null; }
                SaveCache(d);
            }
            catch (Exception ex)
            {
                lock (gate) LastError = ex.Message;
            }
            finally
            {
                lock (gate) busy = false;
            }
        }

        static string Str(Dictionary<string, object> j, string name)
        {
            object v;
            return j.TryGetValue(name, out v) && v != null ? v.ToString() : "";
        }

        static void LocateByIp(out double lat, out double lon, out string place, out string country)
        {
            try
            {
                var j = GetJson("https://ipapi.co/json/");
                lat = Num(j["latitude"]); lon = Num(j["longitude"]); place = Str(j, "city"); country = Str(j, "country_code");
                return;
            }
            catch (Exception) { }
            var k = GetJson("https://ipwho.is/");
            lat = Num(k["latitude"]); lon = Num(k["longitude"]); place = Str(k, "city"); country = Str(k, "country_code");
        }

        static void Geocode(string city, out double lat, out double lon, out string place, out string country)
        {
            var j = GetJson("https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" + Uri.EscapeDataString(city));
            object resultsObj;
            var results = j.TryGetValue("results", out resultsObj) ? resultsObj as object[] : null;
            if (results == null || results.Length == 0) throw new Exception("City not found: " + city);
            var r = (Dictionary<string, object>)results[0];
            lat = Num(r["latitude"]); lon = Num(r["longitude"]); place = r["name"] as string ?? city; country = Str(r, "country_code");
        }

        static Dictionary<string, object> GetJson(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "LockClock/1.1";
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(sr.ReadToEnd());
        }

        static double Num(object o) { return o == null ? 0 : Convert.ToDouble(o, Inv); }

        static void SaveCache(WeatherData d)
        {
            try
            {
                var days = new List<object>();
                foreach (var x in d.Days)
                    days.Add(new Dictionary<string, object> { { "date", x.Date.ToString("yyyy-MM-dd", Inv) }, { "code", x.Code }, { "max", x.Max }, { "min", x.Min } });
                var o = new Dictionary<string, object>
                {
                    { "key", d.Key }, { "place", d.Place }, { "country", d.Country }, { "temp", d.Temp }, { "code", d.Code }, { "isDay", d.IsDay },
                    { "fetched", d.FetchedUtc.Ticks.ToString(Inv) }, { "days", days }
                };
                Directory.CreateDirectory(Installer.Dir);
                File.WriteAllText(CachePath, new JavaScriptSerializer().Serialize(o));
            }
            catch (Exception) { }
        }

        static WeatherData LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return null;
                var o = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(CachePath));
                var d = new WeatherData
                {
                    Key = (string)o["key"],
                    Place = o["place"] as string ?? "",
                    Country = Str(o, "country"),
                    Temp = Num(o["temp"]),
                    Code = (int)Num(o["code"]),
                    IsDay = (bool)o["isDay"],
                    FetchedUtc = new DateTime(long.Parse((string)o["fetched"], Inv), DateTimeKind.Utc)
                };
                foreach (Dictionary<string, object> x in (object[])o["days"])
                    d.Days.Add(new WeatherDay { Date = DateTime.ParseExact((string)x["date"], "yyyy-MM-dd", Inv), Code = (int)Num(x["code"]), Max = Num(x["max"]), Min = Num(x["min"]) });
                return d;
            }
            catch (Exception) { return null; }
        }

        // Monochrome symbols from Segoe UI Symbol so they follow the clock color (red at night).
        public static string Icon(int code, bool isDay)
        {
            if (code == 0) return isDay ? "\u2600" : "\u263E";
            if (code <= 2) return isDay ? "\u26C5" : "\u263E";
            if ((code >= 51 && code <= 67) || (code >= 80 && code <= 82)) return "\u2602";
            if ((code >= 71 && code <= 77) || code == 85 || code == 86) return "\u2744";
            if (code >= 95) return "\u26A1";
            return "\u2601";
        }

        public static string Describe(int code)
        {
            if (code == 0) return "Clear";
            if (code == 1) return "Mostly clear";
            if (code == 2) return "Partly cloudy";
            if (code == 3) return "Cloudy";
            if (code == 45 || code == 48) return "Fog";
            if (code >= 51 && code <= 57) return "Drizzle";
            if (code >= 61 && code <= 67) return "Rain";
            if (code >= 71 && code <= 77) return "Snow";
            if (code >= 80 && code <= 82) return "Showers";
            if (code == 85 || code == 86) return "Snow showers";
            if (code >= 95) return "Thunderstorm";
            return "";
        }

        public static string Deg(double celsius, bool fahrenheit)
        {
            double v = fahrenheit ? celsius * 9 / 5 + 32 : celsius;
            return ((int)Math.Round(v)).ToString(Inv) + "\u00B0";
        }
    }

    class ClockForm : Form
    {
        readonly Settings settings;
        readonly bool screensaverMode;
        readonly bool lockOnExit;
        readonly DateTime startedAt = DateTime.UtcNow;
        readonly bool isPreview;
        readonly IntPtr previewParent;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Random rng = new Random();
        Point? mouseStart;
        Point shift = Point.Empty;
        int lastShiftMinute = -1;
        string lastKey = "";
        Size layoutSize = Size.Empty;
        float timeEm, dateEm, secEm, secColW;
        FontFamily timeFamily, dateFamily, symbolFamily;
        WeatherData weather;
        float wEm, fEm, wRowH, fRowH;
        FontStyle timeStyle;

        public static double? ForcedNight;
        public static DateTime? ForcedTime;
        public double? PreviewNight = ForcedNight;
        public bool TestMode; // full-screen test from the settings app: closes instead of exiting the app
        public bool Blank;    // black cover for a screen that doesn't show the clock
        public List<Form> TestGroup; // all screens of one full-screen test close together
        public static bool ForceBlank; // debug: /blank

        public static ClockForm CreateEmbedded(Settings s)
        {
            var f = new ClockForm(s, Rectangle.Empty, false, IntPtr.Zero);
            f.TopLevel = false;
            f.FormBorderStyle = FormBorderStyle.None;
            f.Dock = DockStyle.Fill;
            return f;
        }

        public void ReloadSettings()
        {
            ChooseFonts();
            layoutSize = Size.Empty;
            lastKey = "";
            Invalidate();
        }

        public ClockForm(Settings s, Rectangle bounds, bool screensaver, IntPtr parent, bool lockWhenDone = false)
        {
            settings = s;
            screensaverMode = screensaver;
            lockOnExit = lockWhenDone;
            previewParent = parent;
            isPreview = parent != IntPtr.Zero;

            Text = "LockClock";
            BackColor = Color.Black;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = !screensaver && !isPreview;
            KeyPreview = true;

            if (screensaver || isPreview)
            {
                FormBorderStyle = FormBorderStyle.None;
                Bounds = bounds;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                Bounds = bounds;
                KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            }

            if (screensaver)
            {
                TopMost = true;
                Cursor.Hide();
                KeyDown += (o, e) => ExitSaver(false);
                MouseDown += (o, e) => ExitSaver(false);
                MouseMove += OnMouseMoveExit;
                if (lockOnExit)
                {
                    // Another app took focus (e.g. Ctrl+Alt+Del > Task Manager): lock immediately.
                    Deactivate += (o, e) => BeginInvoke((Action)(() => { if (Form.ActiveForm == null) ExitSaver(true); }));
                }
            }

            ChooseFonts();
            timer.Interval = 250;
            timer.Tick += OnTick;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (isPreview)
            {
                Native.SetParent(Handle, previewParent);
                Native.SetWindowLong(Handle, -16, Native.GetWindowLong(Handle, -16) | 0x40000000); // WS_CHILD
                Native.RECT r;
                Native.GetClientRect(previewParent, out r);
                Location = Point.Empty;
                Size = new Size(r.Right - r.Left, r.Bottom - r.Top);
            }
            timer.Start();
        }

        void ChooseFonts()
        {
            string[] timeCandidates = settings.FontWeight == 0
                ? new[] { "Segoe UI Light", "Segoe UI Semilight", "Segoe UI" }
                : new[] { "Segoe UI", "Arial" };
            timeFamily = FindFamily(timeCandidates);
            timeStyle = settings.FontWeight == 2 ? FontStyle.Bold : FontStyle.Regular;
            dateFamily = FindFamily(new[] { "Segoe UI Semilight", "Segoe UI" });
            symbolFamily = FindFamily(new[] { "Segoe UI Symbol", "Segoe UI" });
        }

        static FontFamily FindFamily(string[] names)
        {
            using (var installed = new InstalledFontCollection())
            {
                foreach (var n in names)
                    foreach (var f in installed.Families)
                        if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return f;
            }
            return FontFamily.GenericSansSerif;
        }

        static bool exiting;

        void ExitSaver(bool force)
        {
            if (exiting) return;
            if (!force && (DateTime.UtcNow - startedAt).TotalMilliseconds < 1000) return;
            if (TestMode)
            {
                Cursor.Show();
                foreach (var f in (TestGroup ?? new List<Form> { this }).ToArray()) f.Close();
                return;
            }
            exiting = true;
            if (lockOnExit && !LockControl.LockNow())
            {
                exiting = false; // Windows refused to lock: keep the clock up and try again on the next input
                return;
            }
            Application.Exit();
        }

        void OnMouseMoveExit(object sender, MouseEventArgs e)
        {
            if (mouseStart == null || (DateTime.UtcNow - startedAt).TotalMilliseconds < 1000) { mouseStart = e.Location; return; }
            if (Math.Abs(e.X - mouseStart.Value.X) > 12 || Math.Abs(e.Y - mouseStart.Value.Y) > 12)
                ExitSaver(false);
        }

        DateTime Now { get { return ForcedTime ?? DateTime.Now; } }

        void OnTick(object sender, EventArgs e)
        {
            if (isPreview && !Native.IsWindowVisible(previewParent)) { Application.Exit(); return; }

            var now = Now;
            if (settings.PixelShift && now.Minute != lastShiftMinute)
            {
                lastShiftMinute = now.Minute;
                int range = Math.Max(2, Math.Min(ClientSize.Width, ClientSize.Height) / 40);
                shift = new Point(rng.Next(-range, range + 1), rng.Next(-range, range + 1));
            }
            if (Blank || ForceBlank) return;
            weather = settings.ShowWeather ? WeatherService.Get(settings) : null;
            string key = TimeText(now) + (settings.ShowSeconds ? ":" + now.Second : "") + "|" + now.Date.Ticks + "|" + Math.Round(NightFactor(now) * 100) + "|" + shift
                + "|" + (weather != null ? weather.FetchedUtc.Ticks.ToString() : (WeatherService.LastError ?? "-"));
            if (key != lastKey) { lastKey = key; Invalidate(); }
        }

        double NightFactor(DateTime now) { return PreviewNight ?? settings.NightFactor(now); }

        string TimeText(DateTime now)
        {
            return now.ToString(settings.Use24h ? "HH:mm" : "h:mm", CultureInfo.InvariantCulture);
        }

        static RectangleF PathBounds(string text, FontFamily fam, FontStyle style, float em)
        {
            using (var p = new GraphicsPath())
            using (var sf = StringFormat.GenericTypographic)
            {
                p.AddString(text, fam, (int)style, em, PointF.Empty, sf);
                return p.GetBounds();
            }
        }

        void ComputeLayout()
        {
            layoutSize = ClientSize;
            float W = ClientSize.Width, H = ClientSize.Height;
            var b = PathBounds("88:88", timeFamily, timeStyle, 100f);
            bool sideColumn = settings.ShowSeconds || !settings.Use24h;
            float widthFrac = sideColumn ? 0.78f : 0.86f;
            float maxH = H * (settings.ShowWeather ? 0.42f : (settings.ShowDate ? 0.52f : 0.62f));
            timeEm = Math.Min(100f * W * widthFrac / b.Width, 100f * maxH / b.Height);
            dateEm = Math.Max(6f, timeEm * 0.13f);
            secEm = dateEm * 1.3f;
            secColW = PathBounds("88", dateFamily, FontStyle.Regular, secEm).Width;
            wEm = dateEm * 0.8f;
            fEm = dateEm * 0.68f;
            wRowH = PathBounds("Hg\u00B0\u2600", symbolFamily, FontStyle.Regular, wEm).Height;
            fRowH = PathBounds("Hg\u00B0\u2600", symbolFamily, FontStyle.Regular, fEm).Height;
        }

        string[] WeatherLines(DateTime now)
        {
            var w = weather;
            if (w == null)
                return new[] { WeatherService.LastError != null ? "Weather unavailable" : "Loading weather\u2026", "" };
            bool f = settings.UseFahrenheit(w.Country);
            string line1 = (w.Place.Length > 0 ? w.Place + "     " : "") + WeatherService.Icon(w.Code, w.IsDay) + "  " + WeatherService.Deg(w.Temp, f)
                + "  " + WeatherService.Describe(w.Code);
            if (w.Days.Count > 0)
                line1 += "     H " + WeatherService.Deg(w.Days[0].Max, f) + "   L " + WeatherService.Deg(w.Days[0].Min, f);
            var parts = new List<string>();
            foreach (var d in w.Days)
            {
                if (d.Date.Date <= now.Date || parts.Count == 3) continue;
                parts.Add(d.Date.ToString("ddd", CultureInfo.CurrentCulture) + "  " + WeatherService.Icon(d.Code, true) + "  "
                    + WeatherService.Deg(d.Max, f) + " / " + WeatherService.Deg(d.Min, f));
            }
            return new[] { line1, string.Join("          ", parts.ToArray()) };
        }

        void DrawCentered(Graphics g, string text, FontFamily fam, float em, float y, float rowH, Color color, StringFormat sf)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var p = new GraphicsPath())
            {
                p.AddString(text, fam, (int)FontStyle.Regular, em, PointF.Empty, sf);
                var b = p.GetBounds();
                float maxW = ClientSize.Width * 0.94f;
                float k = b.Width > maxW ? maxW / b.Width : 1f;
                using (var m = new Matrix())
                {
                    // Center horizontally; align the row so baselines stay put as text changes.
                    var ref0 = PathBounds("Hg", fam, FontStyle.Regular, em);
                    m.Translate((ClientSize.Width - b.Width * k) / 2f + shift.X, y + (rowH - ref0.Height) / 2f);
                    m.Scale(k, k);
                    m.Translate(-b.X, -ref0.Y);
                    p.Transform(m);
                }
                using (var br = new SolidBrush(color)) g.FillPath(br, p);
            }
        }

        static Color Scale(Color c, double k)
        {
            return Color.FromArgb((int)(c.R * k), (int)(c.G * k), (int)(c.B * k));
        }

        static Color Lerp(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Black);
            if (Blank || ForceBlank || ClientSize.Width < 4 || ClientSize.Height < 4) return;
            if (layoutSize != ClientSize) ComputeLayout();

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var now = Now;
            double night = NightFactor(now);
            Color day = Scale(Color.FromArgb(255, 245, 235), settings.DayBrightness / 100.0);
            Color red = Scale(Color.FromArgb(255, 28, 10), settings.NightBrightness / 100.0);
            Color main = Lerp(day, red, night);
            Color dim = Scale(main, 0.62);

            string timeText = TimeText(now);
            string ampm = settings.Use24h ? null : now.ToString("tt", CultureInfo.CurrentCulture);
            if (string.IsNullOrEmpty(ampm) && !settings.Use24h) ampm = now.Hour < 12 ? "AM" : "PM";
            string dateText = now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);

            using (var sf = (StringFormat)StringFormat.GenericTypographic.Clone())
            using (var timePath = new GraphicsPath())
            using (var datePath = new GraphicsPath())
            using (var ampmPath = new GraphicsPath())
            using (var secPath = new GraphicsPath())
            {
                timePath.AddString(timeText, timeFamily, (int)timeStyle, timeEm, PointF.Empty, sf);
                var tb = timePath.GetBounds();
                RectangleF db = RectangleF.Empty, ab = RectangleF.Empty;
                if (settings.ShowDate)
                {
                    datePath.AddString(dateText, dateFamily, (int)FontStyle.Regular, dateEm, PointF.Empty, sf);
                    db = datePath.GetBounds();
                }
                if (ampm != null)
                {
                    ampmPath.AddString(ampm, dateFamily, (int)FontStyle.Regular, dateEm * 1.1f, PointF.Empty, sf);
                    ab = ampmPath.GetBounds();
                }
                RectangleF sb = RectangleF.Empty;
                if (settings.ShowSeconds)
                {
                    secPath.AddString(now.ToString("ss", CultureInfo.InvariantCulture), dateFamily, (int)FontStyle.Regular, secEm, PointF.Empty, sf);
                    sb = secPath.GetBounds();
                }

                float gap = settings.ShowDate ? tb.Height * 0.22f : 0;
                float wGap = dateEm * 0.9f, fGap = dateEm * 0.45f;
                float blockH = tb.Height + gap + db.Height + (settings.ShowWeather ? wGap + wRowH + fGap + fRowH : 0);
                // Small side column right of the big digits: AM/PM at the top, seconds at the bottom.
                bool side = ampm != null || settings.ShowSeconds;
                float sideGap = side ? dateEm * 0.5f : 0;
                float sideW = side ? Math.Max(ampm != null ? ab.Width : 0, settings.ShowSeconds ? secColW : 0) : 0;
                float rowW = tb.Width + sideGap + sideW;
                float top = (ClientSize.Height - blockH) / 2f + shift.Y;
                float left = (ClientSize.Width - rowW) / 2f + shift.X;

                using (var m = new Matrix())
                {
                    m.Translate(left - tb.X, top - tb.Y);
                    timePath.Transform(m);
                }
                using (var br = new SolidBrush(main)) g.FillPath(br, timePath);

                if (ampm != null)
                {
                    using (var m = new Matrix())
                    {
                        m.Translate(left + tb.Width + sideGap - ab.X, top - ab.Y);
                        ampmPath.Transform(m);
                    }
                    using (var br = new SolidBrush(dim)) g.FillPath(br, ampmPath);
                }

                if (settings.ShowSeconds)
                {
                    using (var m = new Matrix())
                    {
                        m.Translate(left + tb.Width + sideGap - sb.X, top + tb.Height - sb.Height - sb.Y);
                        secPath.Transform(m);
                    }
                    using (var br = new SolidBrush(dim)) g.FillPath(br, secPath);
                }

                if (settings.ShowDate)
                {
                    using (var m = new Matrix())
                    {
                        m.Translate((ClientSize.Width - db.Width) / 2f + shift.X - db.X, top + tb.Height + gap - db.Y);
                        datePath.Transform(m);
                    }
                    using (var br = new SolidBrush(dim)) g.FillPath(br, datePath);
                }

                if (settings.ShowWeather)
                {
                    var lines = WeatherLines(now);
                    float y = top + tb.Height + gap + db.Height + wGap;
                    DrawCentered(g, lines[0], symbolFamily, wEm, y, wRowH, dim, sf);
                    DrawCentered(g, lines[1], symbolFamily, fEm, y + wRowH + fGap, fRowH, Scale(main, 0.48), sf);
                }
            }
        }
    }

    static class Installer
    {
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LockClock"); } }
        public static string ScrPath { get { return Path.Combine(Dir, "LockClock.scr"); } }
        // Same binary as .exe: shortcuts to a .scr lose their command-line arguments.
        public static string ExePath { get { return Path.Combine(Dir, "LockClock.exe"); } }
        const string DesktopKey = @"Control Panel\Desktop";
        const uint Update = 0x01 | 0x02; // SPIF_UPDATEINIFILE | SPIF_SENDCHANGE

        public static bool IsActive()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(DesktopKey))
            {
                var v = k == null ? null : k.GetValue("SCRNSAVE.EXE") as string;
                return v != null && v.IndexOf("LockClock", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public static int TimeoutMinutes()
        {
            int sec;
            if (Native.SystemParametersInfo(0x000E, 0, out sec, 0) && sec > 0) return Math.Max(1, sec / 60);
            return 5;
        }

        public static bool LockOnResume()
        {
            int v;
            return !Native.SystemParametersInfo(0x0076, 0, out v, 0) || v != 0;
        }

        static void CopySelf(string dest)
        {
            string self = Application.ExecutablePath;
            if (string.Equals(Path.GetFullPath(self), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) return;
            // The helper may still be exiting and holding the file for a moment, so retry briefly.
            for (int attempt = 0; ; attempt++)
            {
                try { File.Copy(self, dest, true); return; }
                catch (IOException)
                {
                    if (attempt >= 20) { if (!File.Exists(dest)) throw; return; } // still in use: keep the existing copy
                    Thread.Sleep(250);
                }
            }
        }

        public static void Install(int timeoutMinutes, bool lockOnResume)
        {
            Directory.CreateDirectory(Dir);
            bool agentWasWanted = Settings.Load().TakeOverWinL;
            Agent.Stop();
            CopySelf(ScrPath);
            CopySelf(ExePath);

            using (var k = Registry.CurrentUser.CreateSubKey(DesktopKey))
            {
                k.SetValue("SCRNSAVE.EXE", ScrPath);
                k.SetValue("ScreenSaveActive", "1");
                k.SetValue("ScreenSaveTimeOut", (timeoutMinutes * 60).ToString(CultureInfo.InvariantCulture));
                k.SetValue("ScreenSaverIsSecure", lockOnResume ? "1" : "0");
            }
            Native.SystemParametersInfo(0x000F, (uint)(timeoutMinutes * 60), IntPtr.Zero, Update); // SPI_SETSCREENSAVETIMEOUT
            Native.SystemParametersInfo(0x0011, 1, IntPtr.Zero, Update);                          // SPI_SETSCREENSAVEACTIVE
            Native.SystemParametersInfo(0x0077, lockOnResume ? 1u : 0u, IntPtr.Zero, Update);     // SPI_SETSCREENSAVESECURE

            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            CreateShortcut(Path.Combine(programs, "Lock with Clock.lnk"), ExePath, "/lock", "Ctrl+Alt+L",
                "Show the full-screen clock; the PC locks as soon as you touch it");
            CreateShortcut(Path.Combine(programs, "LockClock Settings.lnk"), ExePath, "/c", "", "LockClock settings");
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\LockClock")) k.SetValue("ScreensaverWanted", 1);
            Agent.Configure(agentWasWanted);
        }

        // Something outside LockClock (e.g. settings sync or device management) can blank the screensaver value.
        // If the user chose LockClock and the value is now empty, put it back. A different screensaver is left alone.
        public static bool RestoreIfCleared()
        {
            using (var mine = Registry.CurrentUser.OpenSubKey(@"Software\LockClock"))
                if (mine == null || !(mine.GetValue("ScreensaverWanted") is int) || (int)mine.GetValue("ScreensaverWanted") != 1) return false;
            using (var k = Registry.CurrentUser.OpenSubKey(DesktopKey, true))
            {
                if (k == null || !string.IsNullOrEmpty(k.GetValue("SCRNSAVE.EXE") as string) || !File.Exists(ScrPath)) return false;
                k.SetValue("SCRNSAVE.EXE", ScrPath);
            }
            Native.SystemParametersInfo(0x0011, 1, IntPtr.Zero, Update);
            return true;
        }

        public static void Uninstall()
        {
            Agent.Configure(false);
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\LockClock")) k.SetValue("ScreensaverWanted", 0);
            using (var k = Registry.CurrentUser.OpenSubKey(DesktopKey, true))
            {
                var v = k == null ? null : k.GetValue("SCRNSAVE.EXE") as string;
                if (v != null && v.IndexOf("LockClock", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    k.DeleteValue("SCRNSAVE.EXE", false);
                    Native.SystemParametersInfo(0x0011, 0, IntPtr.Zero, Update); // no screensaver left to run
                }
            }
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            foreach (var name in new[] { "Lock with Clock.lnk", "LockClock Settings.lnk" })
                try { File.Delete(Path.Combine(programs, name)); } catch (Exception) { }
            try { File.Delete(Path.Combine(Dir, "weather.json")); } catch (Exception) { }
        }

        static void CreateShortcut(string path, string target, string args, string hotkey, string description)
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                var t = lnk.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                t.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk, new object[] { args });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { description });
                if (hotkey.Length > 0) t.InvokeMember("Hotkey", BindingFlags.SetProperty, null, lnk, new object[] { hotkey });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch (Exception) { /* shortcuts are a convenience; the screensaver itself is already set */ }
        }
    }

    // Fluent / Power Apps look for the settings window.
    static class Fluent
    {
        public static float K = 1f; // DPI scale
        public static readonly Color Brand = Color.FromArgb(0x74, 0x27, 0x74);        // Power Apps purple
        public static readonly Color BrandHover = Color.FromArgb(0x5C, 0x1F, 0x5C);
        public static readonly Color BrandPressed = Color.FromArgb(0x4A, 0x18, 0x4A);
        public static readonly Color Page = Color.FromArgb(0xF3, 0xF2, 0xF1);
        public static readonly Color Card = Color.White;
        public static readonly Color Stroke = Color.FromArgb(0xE1, 0xDF, 0xDD);
        public static readonly Color Text = Color.FromArgb(0x32, 0x31, 0x30);
        public static readonly Color Text2 = Color.FromArgb(0x60, 0x5E, 0x5C);
        public static readonly Color Hover = Color.FromArgb(0xF3, 0xF2, 0xF1);
        public static readonly Color Pressed = Color.FromArgb(0xED, 0xEB, 0xE9);
        static FontFamily iconFamily;

        public static int Px(float v) { return (int)Math.Round(v * K); }

        public static FontFamily IconFamily
        {
            get
            {
                if (iconFamily == null)
                {
                    using (var installed = new InstalledFontCollection())
                    {
                        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                            foreach (var fam in installed.Families)
                                if (fam.Name == name) { iconFamily = fam; return fam; }
                    }
                    iconFamily = FontFamily.GenericSansSerif;
                }
                return iconFamily;
            }
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void DrawIcon(Graphics g, string glyph, float sizePt, Color color, RectangleF box)
        {
            using (var font = new Font(IconFamily, sizePt))
            using (var br = new SolidBrush(color))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(glyph, font, br, box, sf);
            }
        }
    }

    // On/off switch that keeps the CheckBox API (Checked, CheckedChanged).
    class ToggleSwitch : CheckBox
    {
        bool hover;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = true;
            Cursor = Cursors.Hand;
        }

        public override Size GetPreferredSize(Size proposed)
        {
            var text = TextRenderer.MeasureText(Text, Font);
            return new Size(Fluent.Px(40) + Fluent.Px(12) + text.Width, Math.Max(Fluent.Px(30), text.Height + Fluent.Px(6)));
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Fluent.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Fluent.Px(40), h = Fluent.Px(20);
            var track = new RectangleF(1, (Height - h) / 2f, w - 2, h);
            using (var path = Fluent.Round(track, h / 2f))
            {
                if (Checked)
                {
                    using (var br = new SolidBrush(hover ? Fluent.BrandHover : Fluent.Brand)) g.FillPath(br, path);
                }
                else
                {
                    using (var br = new SolidBrush(Color.White)) g.FillPath(br, path);
                    using (var pen = new Pen(hover ? Fluent.Text : Fluent.Text2, Math.Max(1f, Fluent.K))) g.DrawPath(pen, path);
                }
            }
            float knob = h - Fluent.Px(8);
            float kx = Checked ? track.Right - Fluent.Px(4) - knob : track.X + Fluent.Px(4);
            using (var br = new SolidBrush(Checked ? Color.White : (hover ? Fluent.Text : Fluent.Text2)))
                g.FillEllipse(br, kx, track.Y + (h - knob) / 2f, knob, knob);
            var textRect = new Rectangle((int)w + Fluent.Px(12), 0, Width - (int)w - Fluent.Px(12), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? Fluent.Text : Fluent.Text2,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Fluent.Text, 1) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    class WheelRouter : IMessageFilter
    {
        readonly ScrollableControl target;
        readonly int step;

        public WheelRouter(ScrollableControl target, int step) { this.target = target; this.step = step; }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A /* WM_MOUSEWHEEL */ || !target.IsHandleCreated || !target.Visible) return false;
            if (!target.RectangleToScreen(target.ClientRectangle).Contains(Cursor.Position)) return false;
            var focused = Control.FromHandle(m.HWnd) as ComboBox;
            if (focused != null && focused.DroppedDown) return false; // scrolling an open list is fine
            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            int y = -target.AutoScrollPosition.Y - delta * step / 120;
            target.AutoScrollPosition = new Point(0, Math.Max(0, y));
            return true;
        }
    }

    enum ButtonKind { Primary, Secondary, Command }

    // Flat, rounded Fluent button with an optional icon glyph.
    class FluentButton : Button
    {
        readonly ButtonKind kind;
        readonly string glyph;
        bool hover, down;

        public FluentButton(string text, ButtonKind kind, string glyph = null)
        {
            this.kind = kind;
            this.glyph = glyph;
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = true;
            Cursor = Cursors.Hand;
        }

        public override Size GetPreferredSize(Size proposed)
        {
            var text = TextRenderer.MeasureText(Text, Font);
            int icon = glyph != null ? Fluent.Px(26) : 0;
            int pad = kind == ButtonKind.Command ? Fluent.Px(10) : Fluent.Px(16);
            return new Size(Math.Max(kind == ButtonKind.Command ? 0 : Fluent.Px(96), pad * 2 + icon + text.Width), kind == ButtonKind.Command ? Fluent.Px(36) : Fluent.Px(32));
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Fluent.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back, fore, border;
            if (kind == ButtonKind.Primary)
            {
                back = down ? Fluent.BrandPressed : hover ? Fluent.BrandHover : Fluent.Brand;
                fore = Color.White; border = back;
            }
            else if (kind == ButtonKind.Secondary)
            {
                back = down ? Fluent.Pressed : hover ? Fluent.Hover : Color.White;
                fore = Fluent.Text; border = Color.FromArgb(0xD1, 0xD1, 0xD1);
            }
            else
            {
                back = down ? Fluent.Pressed : hover ? Fluent.Hover : (Parent != null ? Parent.BackColor : Color.White);
                fore = Fluent.Text; border = back;
            }
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Fluent.Round(r, Fluent.Px(4)))
            {
                using (var br = new SolidBrush(back)) g.FillPath(br, path);
                using (var pen = new Pen(border, 1)) g.DrawPath(pen, path);
            }
            int pad = kind == ButtonKind.Command ? Fluent.Px(10) : Fluent.Px(16);
            var text = TextRenderer.MeasureText(Text, Font);
            int icon = glyph != null ? Fluent.Px(26) : 0;
            int x = (Width - (icon + text.Width)) / 2;
            if (glyph != null)
                Fluent.DrawIcon(g, glyph, 11f, kind == ButtonKind.Primary ? Color.White : Fluent.Brand, new RectangleF(x, 0, Fluent.Px(20), Height));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + icon, 0, text.Width + 2, Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Fluent.Text, 1) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, 2, 2, Width - 5, Height - 5);
        }
    }

    // White rounded card with an icon + title, like a Power Apps form section.
    class Card : Panel
    {
        readonly string title, glyph;
        public readonly TableLayoutPanel Body;

        public Card(string title, string glyph, int width)
        {
            this.title = title;
            this.glyph = glyph;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Fluent.Page;
            Width = width;
            Margin = new Padding(0, 0, 0, Fluent.Px(12));
            Body = new TableLayoutPanel
            {
                ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Fluent.Card,
                Location = new Point(Fluent.Px(20), Fluent.Px(52))
            };
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Fluent.Px(180)));
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width - Fluent.Px(40) - Fluent.Px(180)));
            Controls.Add(Body);
            Body.SizeChanged += (o, e) => FitHeight();
        }

        public void FitHeight() { Height = Body.Bottom + Fluent.Px(18); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Fluent.Page);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Fluent.Round(r, Fluent.Px(8)))
            {
                using (var br = new SolidBrush(Fluent.Card)) g.FillPath(br, path);
                using (var pen = new Pen(Fluent.Stroke, 1)) g.DrawPath(pen, path);
            }
            Fluent.DrawIcon(g, glyph, 13f, Fluent.Brand, new RectangleF(Fluent.Px(16), Fluent.Px(12), Fluent.Px(28), Fluent.Px(28)));
            using (var font = new Font("Segoe UI Semibold", 12f))
                TextRenderer.DrawText(g, title, font, new Point(Fluent.Px(50), Fluent.Px(14)), Fluent.Text, Color.Empty);
        }
    }
    class SettingsForm : Form
    {
        Settings saved;
        readonly Settings work;
        readonly float k;
        TableLayoutPanel grid;
        readonly List<TableLayoutPanel> grids = new List<TableLayoutPanel>();
        readonly List<Card> cards = new List<Card>();
        FlowLayoutPanel cardStack;
        Panel messageBar;
        Label messageIcon;
        readonly ClockForm preview;
        CheckBox cb24, cbSec, cbDate, cbShift, cbLock, cbWeather, cbWinL, cbSkip;
        ComboBox cmbMode, cmbFont, cmbPreview, cmbUnits, cmbDisplay;
        readonly List<string> displayValues = new List<string>();
        TextBox txtCity;
        readonly System.Windows.Forms.Timer cityDebounce = new System.Windows.Forms.Timer { Interval = 900 };
        DateTimePicker dtStart, dtEnd;
        TrackBar tbDay, tbNight;
        NumericUpDown numWait;
        Label status;
        bool loading = true;

        public SettingsForm(Settings settings)
        {
            saved = settings;
            work = settings.Clone();
            Text = "LockClock";
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            using (var g = CreateGraphics()) k = g.DpiX / 96f;
            Fluent.K = k;
            Font = new Font("Segoe UI", 10f);
            BackColor = Fluent.Page;
            ForeColor = Fluent.Text;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            ClientSize = new Size(Px(1160), Px(780));
            MinimumSize = new Size(Px(980), Px(560));
            KeyPreview = true;
            KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            // Body: preview card on the left, scrolling setting cards on the right.
            var body = new Panel { Dock = DockStyle.Fill, BackColor = Fluent.Page, Padding = new Padding(Px(20), Px(16), Px(8), Px(8)) };
            var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Fluent.Page };
            cardStack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Fluent.Page, Location = new Point(0, 0), Padding = new Padding(0, 0, Px(12), 0) };
            scroller.Controls.Add(cardStack);
            // The mouse wheel scrolls the cards instead of changing whatever dropdown or slider is under the pointer.
            var wheel = new WheelRouter(scroller, Px(60));
            Application.AddMessageFilter(wheel);
            FormClosed += (o, e) => Application.RemoveMessageFilter(wheel);

            var previewCard = new Card("Live preview", "\uE7F4", Px(520));
            previewCard.Dock = DockStyle.Left;
            var frame = new Panel { Size = new Size(Px(480), Px(270)), BackColor = Color.Black, Margin = new Padding(0, 0, 0, Px(12)) };
            preview = ClockForm.CreateEmbedded(work);
            frame.Controls.Add(preview);
            preview.Show();
            previewCard.Body.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, Px(480));
            previewCard.Body.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, 0);
            previewCard.Body.Controls.Add(frame);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Fluent.Card, Margin = new Padding(0) };
            row.Controls.Add(new Label { Text = "Show as", AutoSize = true, ForeColor = Fluent.Text2, Margin = new Padding(0, Px(7), Px(10), 0) });
            cmbPreview = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Px(170), BackColor = Color.White };
            cmbPreview.Items.AddRange(new object[] { "Right now", "Daytime", "Night (red)" });
            cmbPreview.SelectedIndex = 0;
            cmbPreview.SelectedIndexChanged += (o, e) => { preview.PreviewNight = PreviewNight(); preview.ReloadSettings(); };
            row.Controls.Add(cmbPreview);
            previewCard.Body.Controls.Add(row);
            var tip = new Label { Text = "Tip: press Win+L or Ctrl+Alt+L to see it for real.", AutoSize = true, ForeColor = Fluent.Text2, Margin = new Padding(0, Px(14), 0, 0) };
            previewCard.Body.Controls.Add(tip);
            var gutter = new Panel { Dock = DockStyle.Left, Width = Px(20), BackColor = Fluent.Page };
            body.Controls.Add(scroller);
            body.Controls.Add(gutter);
            body.Controls.Add(previewCard);

            int cardWidth = Px(560);
            AddCard("Clock", "\uE823", cardWidth);
            cb24 = AddCheck("24-hour clock", work.Use24h);
            cbSec = AddCheck("Show seconds (small ticking)", work.ShowSeconds);
            cbDate = AddCheck("Show date", work.ShowDate);
            cbShift = AddCheck("Pixel shift (burn-in protection)", work.PixelShift);
            cmbFont = AddCombo("Clock weight", new[] { "Light", "Regular", "Bold" }, work.FontWeight);
            AddDisplayChooser();
            AddCard("Night mode", "\uE708", cardWidth);
            cmbMode = AddCombo("Red at night", new[] { "Automatic (schedule)", "Always day colors", "Always red" }, (int)work.Mode);
            dtStart = AddTime("Night starts", work.NightStartMin);
            dtEnd = AddTime("Night ends", work.NightEndMin);
            tbDay = AddTrack("Day brightness", work.DayBrightness);
            tbNight = AddTrack("Night brightness", work.NightBrightness);
            AddCard("Weather", "\uE9CA", cardWidth);
            cbWeather = AddCheck("Show weather forecast", work.ShowWeather);
            AddLabel("City");
            txtCity = new TextBox { Text = work.WeatherCity, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, Px(5), 0, Px(5)) };
            grid.Controls.Add(txtCity);
            AddHint("Leave City blank to detect your location automatically.");
            cmbUnits = AddCombo("Units", new[] { "Automatic (by country)", "Celsius (\u00B0C)", "Fahrenheit (\u00B0F)" }, work.WeatherUnits);
            AddCard("Lock screen", "\uE72E", cardWidth);
            cbWinL = AddCheck("Show the clock when I press Win+L", work.TakeOverWinL);
            cbSkip = AddCheck("Skip the lock screen picture (straight to password)", LockControl.SkipLockScreen());
            AddHint("Windows asks for administrator approval once to change these.");
            AddCard("Screensaver", "\uE7F8", cardWidth);
            AddLabel("Start after (minutes)");
            numWait = new NumericUpDown { Minimum = 1, Maximum = 600, Value = Installer.TimeoutMinutes(), Width = Px(90), Anchor = AnchorStyles.Left, BorderStyle = BorderStyle.FixedSingle };
            grid.Controls.Add(numWait);
            cbLock = AddCheck("Lock the PC when the clock is dismissed", Installer.LockOnResume());
            foreach (var c in cards) c.FitHeight();
            previewCard.FitHeight();

            // Message bar (status), command bar and header, docked from the top down.
            messageBar = new Panel { Dock = DockStyle.Top, Height = Px(40), Padding = new Padding(Px(20), 0, Px(20), 0) };
            messageIcon = new Label { Dock = DockStyle.Left, Width = Px(28), TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Fluent.IconFamily, 11f) };
            status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ForeColor = Fluent.Text };
            messageBar.Controls.Add(status);
            messageBar.Controls.Add(messageIcon);

            var commandBar = new Panel { Dock = DockStyle.Top, Height = Px(48), BackColor = Color.White, Padding = new Padding(Px(12), Px(6), Px(12), Px(6)) };
            commandBar.Paint += (o, e) => { using (var pen = new Pen(Fluent.Stroke)) e.Graphics.DrawLine(pen, 0, commandBar.Height - 1, commandBar.Width, commandBar.Height - 1); };
            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.White };
            var apply = new FluentButton("Apply", ButtonKind.Command, "\uE74E");
            var lockNow = new FluentButton("Apply && lock now", ButtonKind.Command, "\uE72E");
            var full = new FluentButton("Try full screen", ButtonKind.Command, "\uE740");
            apply.Click += (o, e) => Apply();
            lockNow.Click += (o, e) => { if (Apply()) { StartInstalled("/lock"); Close(); } };
            full.Click += (o, e) => ShowFullScreenTest();
            foreach (var b in new[] { apply, lockNow, full }) { b.Margin = new Padding(0, 0, Px(4), 0); commands.Controls.Add(b); }
            commandBar.Controls.Add(commands);

            var header = new Panel { Dock = DockStyle.Top, Height = Px(48), BackColor = Fluent.Brand };
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            header.Paint += (o, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Fluent.DrawIcon(g, "\uE823", 14f, Color.White, new RectangleF(Px(14), 0, Px(32), header.Height));
                using (var bold = new Font("Segoe UI Semibold", 12f))
                using (var light = new Font("Segoe UI", 12f))
                {
                    var size = TextRenderer.MeasureText("LockClock", bold);
                    TextRenderer.DrawText(g, "LockClock", bold, new Rectangle(Px(54), 0, size.Width, header.Height), Color.White, TextFormatFlags.VerticalCenter);
                    using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255))) g.DrawLine(pen, Px(62) + size.Width, Px(16), Px(62) + size.Width, header.Height - Px(16));
                    TextRenderer.DrawText(g, "Settings", light, new Rectangle(Px(72) + size.Width, 0, Px(200), header.Height), Color.White, TextFormatFlags.VerticalCenter);
                }
                TextRenderer.DrawText(g, "v" + version, Font, new Rectangle(0, 0, header.Width - Px(20), header.Height), Color.FromArgb(220, 255, 255, 255),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            };

            Controls.Add(body);
            Controls.Add(messageBar);
            Controls.Add(commandBar);
            Controls.Add(header);

            foreach (var gr in grids) foreach (Control c in gr.Controls) Hook(c);
            numWait.ValueChanged += (o, e) => MarkDirty();
            // Wait until typing pauses before looking up a new city.
            txtCity.TextChanged += (o, e) => { cityDebounce.Stop(); cityDebounce.Start(); MarkDirty(); };
            cityDebounce.Tick += (o, e) => { cityDebounce.Stop(); Changed(); };
            cbLock.CheckedChanged += (o, e) => MarkDirty();
            cbWinL.CheckedChanged += (o, e) => MarkDirty();
            cbSkip.CheckedChanged += (o, e) => MarkDirty();
            loading = false;
            ShowStatus();
        }

        enum Tone { Success, Warning, Error, Info }

        void SetStatus(Tone tone, string text)
        {
            switch (tone)
            {
                case Tone.Success: messageBar.BackColor = Color.FromArgb(0xDF, 0xF6, 0xDD); messageIcon.ForeColor = Color.FromArgb(0x10, 0x7C, 0x10); messageIcon.Text = "\uE930"; break;
                case Tone.Warning: messageBar.BackColor = Color.FromArgb(0xFF, 0xF4, 0xCE); messageIcon.ForeColor = Color.FromArgb(0x9D, 0x5D, 0x00); messageIcon.Text = "\uE7BA"; break;
                case Tone.Error: messageBar.BackColor = Color.FromArgb(0xFD, 0xE7, 0xE9); messageIcon.ForeColor = Color.FromArgb(0xA8, 0x00, 0x00); messageIcon.Text = "\uEA39"; break;
                default: messageBar.BackColor = Color.FromArgb(0xEB, 0xF3, 0xFC); messageIcon.ForeColor = Color.FromArgb(0x0F, 0x6C, 0xBD); messageIcon.Text = "\uE946"; break;
            }
            status.Text = text;
        }

        // Windows 11: tint the title bar to match the header.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int caption = Fluent.Brand.R | (Fluent.Brand.G << 8) | (Fluent.Brand.B << 16);
                int text = 0x00FFFFFF;
                Native.DwmSetWindowAttribute(Handle, 35, ref caption, 4);
                Native.DwmSetWindowAttribute(Handle, 36, ref text, 4);
            }
            catch (Exception) { }
        }
        int Px(int v) { return (int)Math.Round(v * k); }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            CenterToScreen();
            ActiveControl = null;
        }

        double? PreviewNight()
        {
            switch (cmbPreview.SelectedIndex) { case 1: return 0; case 2: return 1; default: return null; }
        }

        void Hook(Control c)
        {
            if (c == numWait || c == cbLock || c == cbWinL || c == cbSkip) return;
            if (c is CheckBox) ((CheckBox)c).CheckedChanged += (o, e) => Changed();
            else if (c is ComboBox) ((ComboBox)c).SelectedIndexChanged += (o, e) => Changed();
            else if (c is DateTimePicker) ((DateTimePicker)c).ValueChanged += (o, e) => Changed();
            else if (c is TrackBar) ((TrackBar)c).ValueChanged += (o, e) => Changed();
        }

        void Changed()
        {
            if (loading) return;
            ReadControls(work);
            preview.ReloadSettings();
            MarkDirty();
        }

        void MarkDirty()
        {
            if (loading) return;
            SetStatus(Tone.Warning, "You have unsaved changes. Click Apply to save them.");

        }

        void ShowStatus()
        {
            if (Installer.IsActive())
            {
                SetStatus(Tone.Success, "LockClock is your screensaver (starts after " + Installer.TimeoutMinutes() + " min idle).   Ctrl+Alt+L shows the clock and locks now.");

            }
            else
            {
                SetStatus(Tone.Info, "LockClock is not your screensaver yet. Click Apply to turn it on.");

            }
        }

        bool Apply()
        {
            try
            {
                ReadControls(work);
                string lockError = LockControl.Configure(cbWinL.Checked, cbSkip.Checked);
                if (lockError != null)
                {
                    // Keep the previous lock-screen choices if the admin prompt was declined.
                    work.TakeOverWinL = saved.TakeOverWinL;
                    cbWinL.Checked = work.TakeOverWinL;
                    cbSkip.Checked = LockControl.SkipLockScreen();
                }
                work.Save();
                saved = work.Clone();
                Installer.Install((int)numWait.Value, cbLock.Checked);
                ShowStatus();
                if (lockError != null)
                {
                    SetStatus(Tone.Error, lockError);

                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(Tone.Error, "Could not apply: " + ex.Message);

                return false;
            }
        }

        static void StartInstalled(string args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(Installer.ExePath, args) { UseShellExecute = false };
            System.Diagnostics.Process.Start(psi);
        }

        void ShowFullScreenTest()
        {
            ReadControls(work);
            var s = work.Clone();
            var group = new List<Form>();
            ClockForm main = null;
            foreach (var scr in Screen.AllScreens)
            {
                var f = new ClockForm(s, scr.Bounds, true, IntPtr.Zero) { TestMode = true, PreviewNight = PreviewNight(), Blank = !s.ShowsClockOn(scr), TestGroup = group };
                group.Add(f);
                f.Show();
                if (!f.Blank && main == null) main = f;
            }
            (main ?? (ClockForm)group[0]).Activate();
        }

        static string DisplayNumber(Screen s)
        {
            string digits = new string(Array.FindAll(s.DeviceName.ToCharArray(), char.IsDigit));
            return digits.Length > 0 ? digits : "?";
        }

        void AddDisplayChooser()
        {
            AddLabel("Show on");
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Dock = DockStyle.Fill, BackColor = Fluent.Card };
            cmbDisplay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.White, Width = Px(220), Margin = new Padding(0, Px(5), 0, Px(5)) };
            displayValues.Add("all"); cmbDisplay.Items.Add("All displays");
            displayValues.Add("primary"); cmbDisplay.Items.Add("Main display only");
            var screens = Screen.AllScreens;
            Array.Sort(screens, (a, b) => string.CompareOrdinal(a.DeviceName, b.DeviceName));
            foreach (var s in screens)
            {
                displayValues.Add(s.DeviceName);
                cmbDisplay.Items.Add("Display " + DisplayNumber(s) + " (" + s.Bounds.Width + "\u00D7" + s.Bounds.Height + (s.Primary ? ", main)" : ")"));
            }
            int index = displayValues.FindIndex(v => string.Equals(v, work.Display, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                // Saved display is not connected right now: keep the choice, show it as such.
                displayValues.Add(work.Display);
                cmbDisplay.Items.Add("Display " + new string(Array.FindAll(work.Display.ToCharArray(), char.IsDigit)) + " (not connected)");
                index = displayValues.Count - 1;
            }
            cmbDisplay.SelectedIndex = index;
            int widest = 0;
            foreach (var item in cmbDisplay.Items) widest = Math.Max(widest, TextRenderer.MeasureText(item.ToString(), cmbDisplay.Font).Width);
            cmbDisplay.DropDownWidth = Math.Max(cmbDisplay.Width, widest + SystemInformation.VerticalScrollBarWidth + Px(8));
            cmbDisplay.SelectedIndexChanged += (o, e) => MarkDirty();
            var identify = new FluentButton("Identify", ButtonKind.Secondary, "\uE7F4") { Margin = new Padding(Px(8), Px(2), 0, Px(2)) };
            identify.Click += (o, e) => IdentifyDisplays();
            row.Controls.Add(cmbDisplay);
            row.Controls.Add(identify);
            grid.Controls.Add(row);
        }

        // Shows each display's number in big digits for a few seconds.
        void IdentifyDisplays()
        {
            var forms = new List<Form>();
            foreach (var s in Screen.AllScreens)
            {
                var b = s.Bounds;
                int size = Math.Min(b.Width, b.Height) / 3;
                var f = new Form
                {
                    FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false, TopMost = true,
                    BackColor = Color.Black, ForeColor = Color.White, Bounds = new Rectangle(b.X + (b.Width - size) / 2, b.Y + (b.Height - size) / 2, size, size)
                };
                f.Controls.Add(new Label
                {
                    Text = DisplayNumber(s), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", size * 0.45f, FontStyle.Bold, GraphicsUnit.Pixel), ForeColor = s.Primary ? Color.FromArgb(255, 90, 60) : Color.White
                });
                f.Show();
                forms.Add(f);
            }
            var timer = new System.Windows.Forms.Timer { Interval = 2500 };
            timer.Tick += (o, e) => { timer.Dispose(); foreach (var f in forms) f.Close(); };
            timer.Start();
        }

        void ReadControls(Settings t)
        {
            t.Use24h = cb24.Checked;
            t.ShowSeconds = cbSec.Checked;
            t.ShowDate = cbDate.Checked;
            t.PixelShift = cbShift.Checked;
            t.FontWeight = cmbFont.SelectedIndex;
            t.Mode = (NightMode)cmbMode.SelectedIndex;
            t.NightStartMin = dtStart.Value.Hour * 60 + dtStart.Value.Minute;
            t.NightEndMin = dtEnd.Value.Hour * 60 + dtEnd.Value.Minute;
            t.DayBrightness = tbDay.Value;
            t.NightBrightness = tbNight.Value;
            t.ShowWeather = cbWeather.Checked;
            t.WeatherCity = txtCity.Text.Trim();
            t.WeatherUnits = cmbUnits.SelectedIndex;
            t.TakeOverWinL = cbWinL.Checked;
            t.Display = displayValues[cmbDisplay.SelectedIndex];
        }

        void AddCard(string title, string glyph, int width)
        {
            var card = new Card(title, glyph, width);
            cards.Add(card);
            cardStack.Controls.Add(card);
            grid = card.Body;
            grids.Add(grid);
        }

        void AddHint(string text)
        {
            var l = new Label { Text = text, AutoSize = true, ForeColor = Fluent.Text2, Margin = new Padding(0, Px(2), 0, Px(6)) };
            grid.Controls.Add(l);
            grid.SetColumnSpan(l, 2);
        }

        Label AddLabel(string text)
        {
            var l = new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Fluent.Text, Margin = new Padding(0, Px(6), Px(12), Px(6)) };
            grid.Controls.Add(l);
            return l;
        }

        CheckBox AddCheck(string text, bool value)
        {
            var c = new ToggleSwitch { Text = text, Checked = value, Margin = new Padding(0, Px(3), 0, Px(3)) };
            grid.Controls.Add(c);
            grid.SetColumnSpan(c, 2);
            return c;
        }

        ComboBox AddCombo(string label, string[] items, int index)
        {
            AddLabel(label);
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.White, Dock = DockStyle.Fill, Margin = new Padding(0, Px(5), 0, Px(5)) };
            c.Items.AddRange(items); c.SelectedIndex = index;
            grid.Controls.Add(c); return c;
        }

        DateTimePicker AddTime(string label, int minutes)
        {
            AddLabel(label);
            var d = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = Px(110), Anchor = AnchorStyles.Left, Margin = new Padding(0, Px(5), 0, Px(5)) };
            d.Value = DateTime.Today.AddMinutes(minutes);
            grid.Controls.Add(d); return d;
        }

        TrackBar AddTrack(string label, int value)
        {
            var l = AddLabel(label + ": " + value + "%");
            var t = new TrackBar { Minimum = 5, Maximum = 100, TickFrequency = 10, TickStyle = TickStyle.None, Value = value, Dock = DockStyle.Fill, AutoSize = true, BackColor = Fluent.Card };
            t.ValueChanged += (o, e) => l.Text = label + ": " + t.Value + "%";
            grid.Controls.Add(t); return t;
        }
    }
    // Win+L takeover. Windows reserves Win+L unless the per-user "DisableLockWorkstation" policy is set,
    // and that same policy blocks LockWorkStation(), so it is cleared right before we lock and set again on unlock.
    static class LockControl
    {
        public const string ClockMutexName = @"Local\LockClock.Clock";
        const string PolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        const string PersonalizationKey = @"SOFTWARE\Policies\Microsoft\Windows\Personalization";

        public static bool WinLBlocked()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(PolicyKey))
                return k != null && k.GetValue("DisableLockWorkstation") is int && (int)k.GetValue("DisableLockWorkstation") == 1;
        }

        // The one-time setup grants only SetValue + Read on this key, so ask for exactly that.
        static RegistryKey OpenPolicyForWrite()
        {
            return Registry.CurrentUser.OpenSubKey(PolicyKey, RegistryKeyPermissionCheck.ReadWriteSubTree,
                System.Security.AccessControl.RegistryRights.SetValue | System.Security.AccessControl.RegistryRights.QueryValues);
        }

        public static bool CanManagePolicy()
        {
            try
            {
                using (var k = OpenPolicyForWrite()) return k != null;
            }
            catch (Exception) { return false; }
        }

        public static bool SetWinLBlocked(bool on)
        {
            try
            {
                using (var k = OpenPolicyForWrite())
                {
                    if (k == null) return !on;
                    if (on) k.SetValue("DisableLockWorkstation", 1, RegistryValueKind.DWord);
                    else k.DeleteValue("DisableLockWorkstation", false);
                    return true;
                }
            }
            catch (Exception) { return false; }
        }

        public static bool LockNow()
        {
            if (WinLBlocked()) SetWinLBlocked(false);
            for (int i = 0; i < 10; i++)
            {
                if (Native.LockWorkStation()) return true;
                Thread.Sleep(150);
            }
            return false;
        }

        public static bool SkipLockScreen()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(PersonalizationKey))
                return k != null && k.GetValue("NoLockScreen") is int && (int)k.GetValue("NoLockScreen") == 1;
        }

        // Returns null on success, otherwise a message for the user.
        public static string Configure(bool takeOver, bool skipLockScreen)
        {
            bool needAdmin = (takeOver && !CanManagePolicy()) || skipLockScreen != SkipLockScreen();
            if (needAdmin)
            {
                string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                var psi = new System.Diagnostics.ProcessStartInfo(Installer.ExePath,
                    "/setup-lock " + sid + " " + (takeOver ? "1" : "0") + " " + (skipLockScreen ? "1" : "0"))
                { UseShellExecute = true, Verb = "runas" };
                try
                {
                    using (var p = System.Diagnostics.Process.Start(psi))
                    {
                        p.WaitForExit();
                        if (p.ExitCode != 0) return "Lock screen settings could not be changed (error " + p.ExitCode + ").";
                    }
                    if ((takeOver && !CanManagePolicy()) || skipLockScreen != SkipLockScreen())
                        return "Lock screen settings did not take effect - please try Apply again.";
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    return "Administrator approval was declined - lock screen settings not changed.";
                }
            }
            if (!takeOver) SetWinLBlocked(false);
            return null;
        }

        // Runs elevated. Writes to the signed-in user's hive (by SID) in case a different admin approved the prompt.
        public static int ElevatedSetup(string sid, bool takeOver, bool skipLockScreen)
        {
            try
            {
                using (var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default))
                using (var k = users.CreateSubKey(sid + "\\" + PolicyKey))
                {
                    // Let the user (not just admins) toggle values in this one policy key.
                    var acl = k.GetAccessControl();
                    acl.AddAccessRule(new System.Security.AccessControl.RegistryAccessRule(
                        new System.Security.Principal.SecurityIdentifier(sid),
                        System.Security.AccessControl.RegistryRights.ReadKey | System.Security.AccessControl.RegistryRights.SetValue,
                        System.Security.AccessControl.AccessControlType.Allow));
                    k.SetAccessControl(acl);
                    if (!takeOver) k.DeleteValue("DisableLockWorkstation", false);
                }
                using (var k = Registry.LocalMachine.CreateSubKey(PersonalizationKey))
                {
                    if (skipLockScreen) k.SetValue("NoLockScreen", 1, RegistryValueKind.DWord);
                    else k.DeleteValue("NoLockScreen", false);
                }
                return 0;
            }
            catch (Exception) { return 3; }
        }
    }

    // Background helper started at sign-in: owns the Win+L hotkey and shows the clock.
    class Agent : NativeWindow
    {
        const string MutexName = @"Local\LockClock.Agent";
        const string QuitEventName = @"Local\LockClock.AgentQuit";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const int WM_SHOWCLOCK = 0x8001 /* WM_APP+1 */, WM_WTSSESSION_CHANGE = 0x02B1, WM_ENDSESSION = 0x0016;
        const int WTS_SESSION_LOGON = 5, WTS_SESSION_UNLOCK = 8;
        IntPtr hook;
        Native.LowLevelKeyboardProc hookProc; // kept referenced so it is not garbage-collected

        static void Log(string msg)
        {
            try
            {
                string path = Path.Combine(Installer.Dir, "agent.log");
                if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + msg + Environment.NewLine);
            }
            catch (Exception) { }
        }

        public static int Run()
        {
            if (!Settings.Load().TakeOverWinL) { LockControl.SetWinLBlocked(false); return 0; }
            bool first;
            using (var mutex = new Mutex(true, MutexName, out first))
            {
                if (!first) return 0;
                using (var quit = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName))
                {
                    quit.Reset();
                    var agent = new Agent();
                    agent.CreateHandle(new CreateParams { Caption = "LockClock Agent" });
                    Native.WTSRegisterSessionNotification(agent.Handle, 0);
                    agent.Block();

                    agent.InstallHook();
                    var timer = new System.Windows.Forms.Timer { Interval = 500 };
                    int ticks = 0;
                    timer.Tick += (o, e) =>
                    {
                        if (quit.WaitOne(0)) { Application.ExitThread(); return; }
                        if (++ticks % 60 == 1 && Installer.RestoreIfCleared()) Log("screensaver setting had been cleared by something else - restored");
                    };
                    timer.Start();
                    Log("agent started");
                    Application.Run();

                    timer.Dispose();
                    if (agent.hook != IntPtr.Zero) Native.UnhookWindowsHookEx(agent.hook);
                    Native.WTSUnRegisterSessionNotification(agent.Handle);
                    agent.DestroyHandle();
                    LockControl.SetWinLBlocked(false); // give Win+L back to Windows while we're not running
                    Log("agent stopped");
                }
            }
            return 0;
        }

        void Block()
        {
            if (!LockControl.SetWinLBlocked(true)) Log("cannot set DisableLockWorkstation (needs one-time admin setup)");
        }

        // Windows keeps Win+L reserved for RegisterHotKey even when locking is disabled, so watch the keyboard instead.
        void InstallHook()
        {
            hookProc = HookCallback;
            hook = Native.SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, hookProc, Native.GetModuleHandle(null), 0);
            Log(hook != IntPtr.Zero ? "Win+L keyboard hook installed" : "keyboard hook failed: " + Marshal.GetLastWin32Error());
        }

        IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                int vk = Marshal.ReadInt32(lParam);
                bool winDown = (Native.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x5C) & 0x8000) != 0;
                if (vk == 0x4C && winDown && (msg == 0x0100 || msg == 0x0104))
                {
                    // A dummy keystroke stops the Start menu from opening when Win is released.
                    Native.keybd_event(0xE8, 0, 0, UIntPtr.Zero);
                    Native.keybd_event(0xE8, 0, 2, UIntPtr.Zero);
                    Native.PostMessage(Handle, WM_SHOWCLOCK, IntPtr.Zero, IntPtr.Zero);
                    return (IntPtr)1;
                }
                if (vk == 0x4C && winDown && (msg == 0x0101 || msg == 0x0105)) return (IntPtr)1;
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SHOWCLOCK)
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath, "/lock") { UseShellExecute = false };
                    System.Diagnostics.Process.Start(psi);
                    Log("Win+L -> clock");
                }
                catch (Exception ex)
                {
                    Log("could not start clock: " + ex.Message + " - locking instead");
                    LockControl.LockNow();
                }
            }
            else if (m.Msg == WM_WTSSESSION_CHANGE)
            {
                int ev = m.WParam.ToInt32();
                if (ev == WTS_SESSION_UNLOCK || ev == WTS_SESSION_LOGON) { Log("session unlocked"); Block(); }
                else if (ev == 7) Log("session locked");
            }
            else if (m.Msg == WM_ENDSESSION && m.WParam != IntPtr.Zero)
            {
                LockControl.SetWinLBlocked(false);
            }
            base.WndProc(ref m);
        }

        public static void Stop()
        {
            EventWaitHandle quit;
            if (!EventWaitHandle.TryOpenExisting(QuitEventName, out quit)) return;
            using (quit) quit.Set();
            for (int i = 0; i < 40; i++)
            {
                bool created;
                using (var m = new Mutex(false, MutexName, out created)) if (created) return;
                Thread.Sleep(100);
            }
        }

        // Starts at sign-in only while the Win+L option is on.
        public static void Configure(bool enabled)
        {
            Stop();
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) k.SetValue("LockClock", "\"" + Installer.ExePath + "\" /agent");
                else k.DeleteValue("LockClock", false);
            }
            if (enabled)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Installer.ExePath, "/agent") { UseShellExecute = false });
            else
                LockControl.SetWinLBlocked(false);
        }
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int v);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", SetLastError = true)] public static extern bool LockWorkStation();
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc proc, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("wtsapi32.dll")] public static extern bool WTSRegisterSessionNotification(IntPtr h, int flags);
        [DllImport("wtsapi32.dll")] public static extern bool WTSUnRegisterSessionNotification(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, IntPtr pv, uint winIni);
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, out int pv, uint winIni);
        [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);
        public const uint ES_CONTINUOUS = 0x80000000, ES_DISPLAY_REQUIRED = 0x00000002;
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Windows passes "/s", "/c", "/c:1234", "/p 1234" or "/p:1234".
            string mode = "/c";
            IntPtr hwnd = IntPtr.Zero;
            var extra = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].Trim().ToLowerInvariant();
                if (i == 0)
                {
                    int colon = a.IndexOf(':');
                    mode = colon > 0 ? a.Substring(0, colon) : a;
                    string num = colon > 0 ? a.Substring(colon + 1) : (args.Length > 1 ? args[1] : null);
                    long h;
                    if (num != null && long.TryParse(num, out h)) { hwnd = new IntPtr(h); if (colon < 0) i++; }
                }
                else extra.Add(a);
            }
            if (mode.StartsWith("-")) mode = "/" + mode.Substring(1);

            foreach (var a in extra)
            {
                if (a == "/night") ClockForm.ForcedNight = 1;
                else if (a == "/day") ClockForm.ForcedNight = 0;
                else if (a == "/blank") ClockForm.ForceBlank = true;
                else if (a.StartsWith("/time="))
                {
                    DateTime t;
                    if (DateTime.TryParse(a.Substring(6), CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) ClockForm.ForcedTime = t;
                }
            }

            var settings = Settings.Load();
            switch (mode)
            {
                case "/s":
                case "/lock":
                {
                    // Keep the monitor on so the clock stays visible all night.
                    Native.SetThreadExecutionState(Native.ES_CONTINUOUS | Native.ES_DISPLAY_REQUIRED);
                    // While Win+L is taken over, Windows' own "lock on resume" can be blocked, so lock ourselves.
                    bool lockWhenDone = mode == "/lock" || (Installer.LockOnResume() && LockControl.WinLBlocked());
                    bool firstClock;
                    var clockMutex = new Mutex(true, LockControl.ClockMutexName, out firstClock);
                    if (!firstClock && mode == "/lock") return 0; // already showing
                    foreach (var scr in Screen.AllScreens)
                        new ClockForm(settings, scr.Bounds, true, IntPtr.Zero, lockWhenDone) { Blank = !settings.ShowsClockOn(scr) }.Show();
                    Application.Run();
                    GC.KeepAlive(clockMutex);
                    return 0;
                }
                case "/agent":
                    return Agent.Run();
                case "/stopagent":
                    Agent.Stop();
                    return 0;
                case "/setup-lock":
                    // Elevated helper: /setup-lock <user SID> <takeover 0|1> <skip lock screen 0|1>
                    return extra.Count == 3 ? LockControl.ElevatedSetup(extra[0].ToUpperInvariant(), extra[1] == "1", extra[2] == "1") : 2;
                case "/p":
                    if (hwnd == IntPtr.Zero) return 1;
                    Application.Run(new ClockForm(settings, Rectangle.Empty, false, hwnd));
                    return 0;
                case "/w":
                {
                    var wa = Screen.PrimaryScreen.WorkingArea;
                    var size = new Size(wa.Width * 2 / 3, wa.Height * 2 / 3);
                    var f = new ClockForm(settings, new Rectangle(wa.X + (wa.Width - size.Width) / 2, wa.Y + (wa.Height - size.Height) / 2, size.Width, size.Height), false, IntPtr.Zero);
                    Application.Run(f);
                    return 0;
                }
                case "/uninstall":
                    Installer.Uninstall();
                    return 0;
                case "/install":
                {
                    int minutes = Installer.TimeoutMinutes();
                    foreach (var a in extra) { int m; if (int.TryParse(a.TrimStart('/'), out m) && m > 0) minutes = m; }
                    Installer.Install(minutes, !extra.Contains("/nolock"));
                    return 0;
                }
                default:
                    Application.Run(new SettingsForm(settings));
                    return 0;
            }
        }
    }
}
