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
[assembly: System.Reflection.AssemblyVersion("1.5.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.5.0.0")]
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
        public bool HardwareBrightness = true; // set the real screen brightness while the clock shows
        public bool DesktopClock = false; // live clock on the desktop background
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
                s.HardwareBrightness = Get(k, "HardwareBrightness", 1) != 0;
                s.DesktopClock = Get(k, "DesktopClock", 0) != 0;
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
                k.SetValue("HardwareBrightness", HardwareBrightness ? 1 : 0);
                k.SetValue("ShowWeather", ShowWeather ? 1 : 0);
                k.SetValue("WeatherCity", WeatherCity ?? "");
                k.SetValue("Display", Display ?? "all");
                k.SetValue("WeatherUnits", WeatherUnits);
                k.SetValue("TakeOverWinL", TakeOverWinL ? 1 : 0);
                k.SetValue("DesktopClock", DesktopClock ? 1 : 0);
                k.SetValue("Revision", Revision() + 1); // lets the desktop clock pick up changes live
            }
        }

        public static int Revision()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(Key))
                return k != null && k.GetValue("Revision") is int ? (int)k.GetValue("Revision") : 0;
        }

        public Settings Clone() { return (Settings)MemberwiseClone(); }

        static int Get(RegistryKey k, string n, int d)
        {
            object v = k.GetValue(n);
            return v is int ? (int)v : d;
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        // Brightness (percent) for the current mix of day and night.
        public int BrightnessAt(double night)
        {
            return (int)Math.Round(DayBrightness + (NightBrightness - DayBrightness) * night);
        }

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
        public bool DrivesBrightness; // one clock window per process updates the hardware brightness
        // Set by upgrades/uninstall: a showing clock closes the normal way (restores brightness, locks the PC).
        public const string QuitEventName = @"Local\LockClock.ClockQuit";
        public static EventWaitHandle QuitSignal;
        public List<Form> TestGroup; // all screens of one full-screen test close together
        public static bool ForceBlank; // debug: /blank

        IntPtr desktopParent, desktopInsertAfter;
        Rectangle desktopRect;

        // A child window inside the desktop (behind the icons). Never takes input or changes brightness.
        public static ClockForm CreateDesktop(Settings s, Rectangle rectInParent, IntPtr parent, IntPtr insertAfter)
        {
            var f = new ClockForm(s, Rectangle.Empty, false, IntPtr.Zero);
            f.FormBorderStyle = FormBorderStyle.None;
            f.ShowInTaskbar = false;
            f.desktopParent = parent;
            f.desktopInsertAfter = insertAfter;
            f.desktopRect = rectInParent;
            return f;
        }

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
            if (desktopParent != IntPtr.Zero)
            {
                const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000;
                Native.SetWindowLong(Handle, -16, WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS);
                // Windows 11 24H2+ desktop hosts have no redirection surface, so the clock needs its own: a layered child.
                Native.SetWindowLong(Handle, -20, Native.GetWindowLong(Handle, -20) | 0x00080000 /* WS_EX_LAYERED */);
                Native.SetLayeredWindowAttributes(Handle, 0, 255, 0x2 /* LWA_ALPHA */);
                Native.SetParent(Handle, desktopParent);
                // SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW; z-order just below the icons when insertAfter is set
                Native.SetWindowPos(Handle, desktopInsertAfter, desktopRect.X, desktopRect.Y, desktopRect.Width, desktopRect.Height,
                    0x0010 | 0x0020 | 0x0040 | (desktopInsertAfter == IntPtr.Zero ? 0x0004u : 0u));
            }
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
                ScreenBrightness.Restore();
                foreach (var f in (TestGroup ?? new List<Form> { this }).ToArray()) f.Close();
                return;
            }
            exiting = true;
            ScreenBrightness.Restore(); // normal brightness for the password screen
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
            if (screensaverMode && !TestMode && QuitSignal != null && QuitSignal.WaitOne(0)) { ExitSaver(true); return; }
            if (DrivesBrightness) ScreenBrightness.SetLevel(settings.BrightnessAt(NightFactor(now)));
            if (Blank || ForceBlank) return;
            weather = settings.ShowWeather ? WeatherService.Get(settings) : null;
            string key = TimeText(now) + (settings.ShowSeconds ? ":" + now.Second : "") + "|" + now.Date.Ticks + "|" + Math.Round(NightFactor(now) * 100) + "|" + shift
                + "|" + (weather != null ? weather.FetchedUtc.Ticks.ToString() : (WeatherService.LastError ?? "-")) + "|" + ScreenBrightness.Version;
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
            // When the screen itself is dimmed, draw at full strength; otherwise dim the colors instead.
            // The desktop clock never changes the screen, so the sliders (screen brightness) don't dim it either.
            bool hardware = settings.HardwareBrightness && (desktopParent != IntPtr.Zero || ScreenBrightness.Controls(Screen.FromControl(this)));
            Color day = Scale(Color.FromArgb(255, 245, 235), hardware ? 1.0 : settings.DayBrightness / 100.0);
            Color red = Scale(Color.FromArgb(255, 28, 10), hardware ? 1.0 : settings.NightBrightness / 100.0);
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

        static bool SameFile(string a, string b)
        {
            try
            {
                if (string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase)) return true;
                if (!File.Exists(b) || new FileInfo(a).Length != new FileInfo(b).Length) return false;
                byte[] x = File.ReadAllBytes(a), y = File.ReadAllBytes(b);
                for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
                return true;
            }
            catch (Exception) { return false; }
        }

        public static void Install(int timeoutMinutes, bool lockOnResume)
        {
            Directory.CreateDirectory(Dir);
            bool agentWasWanted = Settings.Load().TakeOverWinL;
            string self = Application.ExecutablePath;
            bool copy = !SameFile(self, ExePath) || !SameFile(self, ScrPath);
            if (copy)
            {
                Agent.Stop();
                DesktopClock.Stop();
                CopySelf(ScrPath);
                CopySelf(ExePath);
            }
            SetScreensaver(timeoutMinutes, lockOnResume);

            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            CreateShortcut(Path.Combine(programs, "Lock with Clock.lnk"), ExePath, "/lock", "Ctrl+Alt+L",
                "Show the full-screen clock; the PC locks as soon as you touch it");
            CreateShortcut(Path.Combine(programs, "LockClock Settings.lnk"), ExePath, "/c", "", "LockClock settings");
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\LockClock")) k.SetValue("ScreensaverWanted", 1);
            Agent.Configure(agentWasWanted, copy);
            DesktopClock.Configure(Settings.Load().DesktopClock, copy);
        }

        public static void SetScreensaver(int timeoutMinutes, bool lockOnResume)
        {
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
            DesktopClock.Configure(false);
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

    // PowerToys-style (WinUI 3 / Fluent) look for the settings window.
    static class Fluent
    {
        public static float K = 1f; // DPI scale
        public static Color Accent = Color.FromArgb(0x00, 0x5F, 0xB8);
        public static Color AccentHover = Color.FromArgb(0x19, 0x6F, 0xBF);
        public static Color AccentPressed = Color.FromArgb(0x33, 0x7F, 0xC6);
        public static readonly Color Page = Color.FromArgb(0xF3, 0xF3, 0xF3);   // window (Mica-like)
        public static readonly Color Layer = Color.FromArgb(0xF9, 0xF9, 0xF9);  // content layer
        public static readonly Color Card = Color.White;
        public static readonly Color Stroke = Color.FromArgb(0xE5, 0xE5, 0xE5);
        public static readonly Color Text = Color.FromArgb(0x1A, 0x1A, 0x1A);
        public static readonly Color Text2 = Color.FromArgb(0x5F, 0x5F, 0x5F);
        public static readonly Color NavSelected = Color.FromArgb(0xE9, 0xE9, 0xE9);
        public static readonly Color NavHover = Color.FromArgb(0xED, 0xED, 0xED);
        public static readonly Color ControlStroke = Color.FromArgb(0x86, 0x86, 0x86);
        public static readonly Color Hover = Color.FromArgb(0xF6, 0xF6, 0xF6);
        public static readonly Color Pressed = Color.FromArgb(0xF0, 0xF0, 0xF0);
        static FontFamily iconFamily;

        public static int Px(float v) { return (int)Math.Round(v * K); }

        static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // Use the Windows accent color like PowerToys does (the darker shade Windows uses on light backgrounds).
        public static void LoadAccent()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    var p = k == null ? null : k.GetValue("AccentPalette") as byte[];
                    if (p != null && p.Length >= 32) Accent = Color.FromArgb(p[16], p[17], p[18]);
                }
            }
            catch (Exception) { }
            AccentHover = Mix(Accent, Color.White, 0.1);
            AccentPressed = Mix(Accent, Color.White, 0.2);
        }

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
            float d = Math.Max(1f, radius * 2);
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

        public static void DrawCard(Graphics g, Rectangle bounds, Color fill)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1.5f, bounds.Height - 1.5f);
            using (var path = Round(r, Px(4)))
            {
                using (var br = new SolidBrush(fill)) g.FillPath(br, path);
                using (var pen = new Pen(Stroke, 1)) g.DrawPath(pen, path);
            }
        }
    }

    interface IHeightForWidth { int MeasureHeight(int width); }

    // Vertical stack that stretches children to its width (like a XAML StackPanel).
    class VStack : Panel
    {
        public VStack() { BackColor = Fluent.Layer; }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int y = Padding.Top, w = Math.Max(1, Width - Padding.Horizontal);
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                y += c.Margin.Top;
                var hw = c as IHeightForWidth;
                int h = hw != null ? hw.MeasureHeight(w) : (c.AutoSize ? c.GetPreferredSize(new Size(w, 0)).Height : c.Height);
                c.SetBounds(Padding.Left, y, w, h);
                y += h + c.Margin.Bottom;
            }
            int total = y + Padding.Bottom;
            if (Height != total) Height = total;
        }
    }

    // WinUI-style toggle switch with "On"/"Off" to its left; keeps the CheckBox API (Checked, CheckedChanged).
    class ToggleSwitch : CheckBox
    {
        bool hover;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Cursor = Cursors.Hand;
        }

        public override Size GetPreferredSize(Size proposed)
        {
            int label = Math.Max(TextRenderer.MeasureText("Off", Font).Width, TextRenderer.MeasureText("On", Font).Width);
            return new Size(label + Fluent.Px(12) + Fluent.Px(40), Fluent.Px(32));
        }

        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Fluent.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Fluent.Px(40), h = Fluent.Px(20);
            var track = new RectangleF(Width - w - 1, (Height - h) / 2f, w, h);
            TextRenderer.DrawText(g, Checked ? "On" : "Off", Font, new Rectangle(0, 0, (int)track.X - Fluent.Px(12), Height), Fluent.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            using (var path = Fluent.Round(track, h / 2f))
            {
                if (Checked)
                {
                    using (var br = new SolidBrush(hover ? Fluent.AccentHover : Fluent.Accent)) g.FillPath(br, path);
                }
                else
                {
                    using (var br = new SolidBrush(hover ? Fluent.Hover : Color.White)) g.FillPath(br, path);
                    using (var pen = new Pen(Fluent.ControlStroke, Math.Max(1f, Fluent.K))) g.DrawPath(pen, path);
                }
            }
            float knob = hover ? Fluent.Px(14) : Fluent.Px(12);
            float kx = Checked ? track.Right - Fluent.Px(4) - knob : track.X + Fluent.Px(4);
            using (var br = new SolidBrush(Checked ? Color.White : Fluent.Text2))
                g.FillEllipse(br, kx, track.Y + (h - knob) / 2f, knob, knob);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Fluent.Text, 1)) g.DrawRectangle(pen, track.X - 3, track.Y - 3, track.Width + 5, track.Height + 5);
        }
    }

    enum ButtonKind { Primary, Secondary }

    // Rounded WinUI-style button (accent or standard).
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
            return new Size(Math.Max(Fluent.Px(120), Fluent.Px(24) + icon + text.Width), Fluent.Px(34));
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
            bool primary = kind == ButtonKind.Primary;
            Color back = primary ? (down ? Fluent.AccentPressed : hover ? Fluent.AccentHover : Fluent.Accent)
                                 : (down ? Fluent.Pressed : hover ? Fluent.Hover : Color.White);
            Color fore = primary ? Color.White : Fluent.Text;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Fluent.Round(r, Fluent.Px(4)))
            {
                using (var br = new SolidBrush(back)) g.FillPath(br, path);
                using (var pen = new Pen(primary ? back : Color.FromArgb(0xD6, 0xD6, 0xD6), 1)) g.DrawPath(pen, path);
            }
            var text = TextRenderer.MeasureText(Text, Font);
            int icon = glyph != null ? Fluent.Px(26) : 0;
            int x = (Width - (icon + text.Width)) / 2;
            if (glyph != null) Fluent.DrawIcon(g, glyph, 10.5f, fore, new RectangleF(x, 0, Fluent.Px(18), Height));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + icon, 0, text.Width + 2, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Fluent.Text, 1)) g.DrawRectangle(pen, 2, 2, Width - 5, Height - 5);
        }
    }

    // Left navigation item with the PowerToys selection pill.
    class NavItem : Control
    {
        readonly string glyph;
        bool selected, hover;

        public NavItem(string text, string glyph)
        {
            Text = text;
            this.glyph = glyph;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = Fluent.Px(40);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PageTab;
            AccessibleName = text;
        }

        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Fluent.Page);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, Fluent.Px(2), Width - 1.5f, Height - Fluent.Px(4));
            if (selected || hover)
                using (var path = Fluent.Round(r, Fluent.Px(4)))
                using (var br = new SolidBrush(selected ? Fluent.NavSelected : Fluent.NavHover)) g.FillPath(br, path);
            if (selected)
                using (var pill = Fluent.Round(new RectangleF(r.X, Height / 2f - Fluent.Px(8), Fluent.Px(3), Fluent.Px(16)), Fluent.Px(1.5f)))
                using (var br = new SolidBrush(Fluent.Accent)) g.FillPath(br, pill);
            Fluent.DrawIcon(g, glyph, 12f, Fluent.Text, new RectangleF(Fluent.Px(12), 0, Fluent.Px(24), Height));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Fluent.Px(48), 0, Width - Fluent.Px(52), Height), Fluent.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Fluent.Text, 1)) g.DrawRectangle(pen, 1, Fluent.Px(2), Width - 3, Height - Fluent.Px(4) - 1);
        }
    }

    // PowerToys "SettingsCard": icon, header and description on the left, the control on the right.
    class SettingsCard : Panel, IHeightForWidth
    {
        readonly string glyph;
        readonly string header;
        string description;
        public readonly Control Action;
        static Font headerFont, descFont;

        public SettingsCard(string glyph, string header, string description, Control action)
        {
            this.glyph = glyph;
            this.header = header;
            this.description = description;
            Action = action;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Fluent.Card; // the action control sits on white
            Margin = new Padding(0, 0, 0, Fluent.Px(4));
            if (headerFont == null) { headerFont = new Font("Segoe UI", 10.5f); descFont = new Font("Segoe UI", 9f); }
            if (action != null) Controls.Add(action);
            AccessibleName = header;
        }

        public string Description
        {
            get { return description; }
            set { description = value; if (Parent != null) Parent.PerformLayout(); Invalidate(); }
        }

        int TextLeft { get { return glyph != null ? Fluent.Px(56) : Fluent.Px(16); } }
        int TextWidth(int width) { return Math.Max(Fluent.Px(80), width - TextLeft - Fluent.Px(16) - (Action != null ? Action.Width + Fluent.Px(24) : 0)); }

        Size TextBlock(int width)
        {
            int tw = TextWidth(width);
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            int h = TextRenderer.MeasureText(header, headerFont, new Size(tw, int.MaxValue), flags).Height;
            if (!string.IsNullOrEmpty(description))
                h += Fluent.Px(2) + TextRenderer.MeasureText(description, descFont, new Size(tw, int.MaxValue), flags).Height;
            return new Size(tw, h);
        }

        public int MeasureHeight(int width)
        {
            int content = Math.Max(TextBlock(width).Height, Action != null ? Action.Height : 0);
            return Math.Max(Fluent.Px(68), content + Fluent.Px(28));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (Action != null) Action.Location = new Point(Width - Fluent.Px(16) - Action.Width, (Height - Action.Height) / 2);
            base.OnLayout(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Fluent.Layer);
            Fluent.DrawCard(g, ClientRectangle, Fluent.Card);
            if (glyph != null) Fluent.DrawIcon(g, glyph, 13f, Fluent.Text, new RectangleF(Fluent.Px(16), 0, Fluent.Px(24), Height));
            var block = TextBlock(Width);
            int y = (Height - block.Height) / 2;
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            int hh = TextRenderer.MeasureText(header, headerFont, new Size(block.Width, int.MaxValue), flags).Height;
            TextRenderer.DrawText(g, header, headerFont, new Rectangle(TextLeft, y, block.Width, hh), Fluent.Text, flags);
            if (!string.IsNullOrEmpty(description))
                TextRenderer.DrawText(g, description, descFont, new Rectangle(TextLeft, y + hh + Fluent.Px(2), block.Width, block.Height - hh), Fluent.Text2, flags);
        }
    }

    // Overview hero: live preview with a short pitch and the main actions.
    class HeroCard : Panel, IHeightForWidth
    {
        public readonly Panel Frame;
        public readonly Control Side;

        public HeroCard(Panel frame, Control side)
        {
            Frame = frame;
            Side = side;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Fluent.Card;
            Margin = new Padding(0, 0, 0, Fluent.Px(8));
            Controls.Add(frame);
            Controls.Add(side);
        }

        bool Wide(int width) { return width >= Frame.Width + Fluent.Px(72) + Fluent.Px(300); }

        public int MeasureHeight(int width)
        {
            int pad = Fluent.Px(24);
            int sideH = Side.GetPreferredSize(new Size(Wide(width) ? width - Frame.Width - pad * 3 : width - pad * 2, 0)).Height;
            return Wide(width) ? pad * 2 + Math.Max(Frame.Height, sideH) : pad * 3 + Frame.Height + sideH;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int pad = Fluent.Px(24);
            Frame.Location = new Point(pad, pad);
            if (Wide(Width)) Side.Location = new Point(pad * 2 + Frame.Width, pad);
            else Side.Location = new Point(pad, pad * 2 + Frame.Height);
            base.OnLayout(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Fluent.Layer);
            Fluent.DrawCard(e.Graphics, ClientRectangle, Fluent.Card);
        }
    }

    enum Tone { Success, Warning, Error, Info }

    // WinUI InfoBar.
    class InfoBar : Control
    {
        Tone tone;
        string title = "", message = "";
        public readonly FluentButton ActionButton;

        public InfoBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            ActionButton = new FluentButton("Set up", ButtonKind.Primary) { Visible = false };
            Controls.Add(ActionButton);
        }

        public void Set(Tone tone, string title, string message, string action = null)
        {
            this.tone = tone; this.title = title; this.message = message;
            ActionButton.Visible = action != null;
            if (action != null) ActionButton.Text = action;
            PerformLayout();
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            ActionButton.BackColor = BackFor(tone);
            ActionButton.Location = new Point(Width - Fluent.Px(12) - ActionButton.Width, (Height - ActionButton.Height) / 2);
            base.OnLayout(e);
        }

        static Color BackFor(Tone t)
        {
            switch (t)
            {
                case Tone.Success: return Color.FromArgb(0xDF, 0xF6, 0xDD);
                case Tone.Warning: return Color.FromArgb(0xFF, 0xF4, 0xCE);
                case Tone.Error: return Color.FromArgb(0xFD, 0xE7, 0xE9);
                default: return Color.FromArgb(0xF6, 0xF6, 0xF6);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Fluent.Layer);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Fluent.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Fluent.Px(4)))
            {
                using (var br = new SolidBrush(BackFor(tone))) g.FillPath(br, path);
                using (var pen = new Pen(Color.FromArgb(18, 0, 0, 0))) g.DrawPath(pen, path);
            }
            string glyph; Color c;
            switch (tone)
            {
                case Tone.Success: glyph = "\uEC61"; c = Color.FromArgb(0x0F, 0x7B, 0x0F); break;
                case Tone.Warning: glyph = "\uE814"; c = Color.FromArgb(0x9D, 0x5D, 0x00); break;
                case Tone.Error: glyph = "\uEB90"; c = Color.FromArgb(0xC4, 0x2B, 0x1C); break;
                default: glyph = "\uF167"; c = Fluent.Accent; break;
            }
            Fluent.DrawIcon(g, glyph, 12f, c, new RectangleF(Fluent.Px(12), 0, Fluent.Px(24), Height));
            int right = ActionButton.Visible ? ActionButton.Left - Fluent.Px(12) : Width - Fluent.Px(12);
            using (var bold = new Font("Segoe UI Semibold", Font.Size))
            {
                var tsize = TextRenderer.MeasureText(title, bold);
                TextRenderer.DrawText(g, title, bold, new Rectangle(Fluent.Px(48), 0, tsize.Width, Height), Fluent.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                int mx = Fluent.Px(48) + tsize.Width + Fluent.Px(8);
                TextRenderer.DrawText(g, message, Font, new Rectangle(mx, 0, Math.Max(0, right - mx), Height), Fluent.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    // The mouse wheel scrolls the page instead of changing whatever dropdown or slider is under the pointer.
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

    class SettingsForm : Form
    {
        Settings saved;
        readonly Settings work;
        readonly float k;
        readonly ClockForm preview;
        ToggleSwitch cb24, cbSec, cbDate, cbShift, cbLock, cbWeather, cbWinL, cbSkip, cbHardware, cbDesktop;
        ComboBox cmbMode, cmbFont, cmbPreview, cmbUnits, cmbDisplay;
        readonly List<string> displayValues = new List<string>();
        TextBox txtCity;
        readonly System.Windows.Forms.Timer cityDebounce = new System.Windows.Forms.Timer { Interval = 900 };
        readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer { Interval = 700 };
        DateTimePicker dtStart, dtEnd;
        TrackBar tbDay, tbNight;
        NumericUpDown numWait;
        SettingsCard hardwareCard;
        readonly InfoBar info;
        readonly Panel scroller;
        readonly List<NavItem> navItems = new List<NavItem>();
        readonly Dictionary<NavItem, VStack> pages = new Dictionary<NavItem, VStack>();
        VStack current;
        bool loading = true;

        public SettingsForm(Settings settings)
        {
            saved = settings;
            work = settings.Clone();
            Text = "LockClock Settings";
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            using (var g = CreateGraphics()) k = g.DpiX / 96f;
            Fluent.K = k;
            Fluent.LoadAccent();
            Font = new Font("Segoe UI", 10.5f);
            BackColor = Fluent.Page;
            ForeColor = Fluent.Text;
            ClientSize = new Size(Px(1180), Px(800));
            MinimumSize = new Size(Px(900), Px(600));
            KeyPreview = true;
            KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            // Content layer (rounded top-left corner like PowerToys) with an InfoBar and the scrolling page.
            var layer = new Panel { Dock = DockStyle.Fill, BackColor = Fluent.Layer, Padding = new Padding(1, 1, 0, 0) };
            layer.Paint += (o, e) =>
            {
                var g = e.Graphics;
                g.Clear(Fluent.Page);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Fluent.Round(new RectangleF(0.5f, 0.5f, layer.Width + Px(16), layer.Height + Px(16)), Px(8)))
                {
                    using (var br = new SolidBrush(Fluent.Layer)) g.FillPath(br, path);
                    using (var pen = new Pen(Fluent.Stroke)) g.DrawPath(pen, path);
                }
            };
            scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Fluent.Layer };
            scroller.Resize += (o, e) => { if (current != null) current.Width = scroller.ClientSize.Width; };
            var infoHost = new Panel { Dock = DockStyle.Top, Height = Px(52 + 20), BackColor = Fluent.Layer, Padding = new Padding(Px(40), Px(20), Px(40), 0) };
            info = new InfoBar { Dock = DockStyle.Fill, Font = Font };
            info.ActionButton.Click += (o, e) => SaveNow();
            infoHost.Controls.Add(info);
            layer.Controls.Add(scroller);
            layer.Controls.Add(infoHost);
            var wheel = new WheelRouter(scroller, Px(60));
            Application.AddMessageFilter(wheel);
            FormClosed += (o, e) => Application.RemoveMessageFilter(wheel);

            // Navigation pane.
            var nav = new Panel { Dock = DockStyle.Left, Width = Px(290), BackColor = Fluent.Page, Padding = new Padding(Px(8), Px(8), Px(8), Px(8)) };
            var version = new Label
            {
                Dock = DockStyle.Bottom, Height = Px(36), ForeColor = Fluent.Text2, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Px(12), 0, 0, 0),
                Text = "LockClock " + Assembly.GetExecutingAssembly().GetName().Version.ToString(3)
            };
            nav.Controls.Add(version);

            // Live preview (lives on the Overview page).
            var frame = new Panel { Size = new Size(Px(480), Px(270)), BackColor = Color.Black };
            preview = ClockForm.CreateEmbedded(work);
            frame.Controls.Add(preview);
            preview.Show();

            BuildPages(frame);

            // Nav items are docked top in reverse so they appear in order.
            for (int i = navItems.Count - 1; i >= 0; i--)
            {
                var item = navItems[i];
                item.Dock = DockStyle.Top;
                item.Font = Font;
                item.Click += (o, e) => ShowPage((NavItem)o);
                nav.Controls.Add(item);
            }

            Controls.Add(layer);
            Controls.Add(nav);

            ShowPage(navItems[0]);
            numWait.ValueChanged += (o, e) => Changed();
            txtCity.TextChanged += (o, e) => { cityDebounce.Stop(); cityDebounce.Start(); };
            cityDebounce.Tick += (o, e) => { cityDebounce.Stop(); Changed(); };
            saveTimer.Tick += (o, e) => { saveTimer.Stop(); SaveNow(); };
            FormClosing += (o, e) => { if (saveTimer.Enabled) { saveTimer.Stop(); SaveNow(); } };
            loading = false;
            ShowStatus();
            DetectBrightnessSupport();
        }

        void BuildPages(Panel frame)
        {
            // Overview
            var page = NewPage("Overview", "\uE80F");
            var side = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Fluent.Card };
            side.Controls.Add(new Label { Text = "Your lock screen, as a bedside clock", AutoSize = true, Font = new Font("Segoe UI Semibold", 15f), ForeColor = Fluent.Text, Margin = new Padding(0, 0, 0, Px(8)) });
            side.Controls.Add(new Label { Text = "A giant clock with the date and weather that turns soft red at night. Touch anything and Windows locks, straight to the password box.", AutoSize = true, MaximumSize = new Size(Px(420), 0), ForeColor = Fluent.Text2, Margin = new Padding(0, 0, 0, Px(20)) });
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Fluent.Card, Margin = new Padding(0, 0, 0, Px(16)) };
            var lockNow = new FluentButton("Lock now", ButtonKind.Primary, "\uE72E") { Margin = new Padding(0, 0, Px(8), 0) };
            lockNow.Click += (o, e) => { saveTimer.Stop(); if (SaveNow()) { StartInstalled("/lock"); Close(); } };
            var full = new FluentButton("Try full screen", ButtonKind.Secondary, "\uE740") { Margin = new Padding(0) };
            full.Click += (o, e) => ShowFullScreenTest();
            buttons.Controls.Add(lockNow);
            buttons.Controls.Add(full);
            side.Controls.Add(buttons);
            var previewRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Fluent.Card, Margin = new Padding(0) };
            previewRow.Controls.Add(new Label { Text = "Preview as", AutoSize = true, ForeColor = Fluent.Text2, Margin = new Padding(0, Px(6), Px(10), 0) });
            cmbPreview = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Px(170) };
            cmbPreview.Items.AddRange(new object[] { "Right now", "Daytime", "Night (red)" });
            cmbPreview.SelectedIndex = 0;
            cmbPreview.SelectedIndexChanged += (o, e) => { preview.PreviewNight = PreviewNight(); preview.ReloadSettings(); };
            previewRow.Controls.Add(cmbPreview);
            side.Controls.Add(previewRow);
            page.Controls.Add(new HeroCard(frame, side));
            Section(page, "Shortcuts");
            page.Controls.Add(new SettingsCard("\uE765", "Show the clock now", "Ctrl+Alt+L anywhere, or Win+L when \"Show the clock when I press Win+L\" is on.", null));
            page.Controls.Add(new SettingsCard("\uE72E", "Back to work", "Press any key or move the mouse: Windows locks and asks for your password.", null));

            // Clock
            page = NewPage("Clock", "\uE823");
            Section(page, "Appearance");
            cb24 = Toggle(page, "\uE823", "24-hour clock", "Show 21:30 instead of 9:30 PM.", work.Use24h);
            cbSec = Toggle(page, "\uE916", "Show seconds", "Small ticking seconds next to the time.", work.ShowSeconds);
            cbDate = Toggle(page, "\uE787", "Show date", "The day and date under the time.", work.ShowDate);
            cmbFont = Choice(page, "\uE8D2", "Clock weight", "How thick the digits are.", new[] { "Light", "Regular", "Bold" }, work.FontWeight);
            Section(page, "Screen care");
            cbShift = Toggle(page, "\uE7B5", "Pixel shift", "Moves the clock slightly every minute to protect the screen from burn-in.", work.PixelShift);

            // Displays
            page = NewPage("Displays", "\uE7F4");
            Section(page, "Multiple monitors");
            AddDisplayChooser(page);

            // Night & brightness
            page = NewPage("Night & brightness", "\uE706");
            Section(page, "Night mode");
            cmbMode = Choice(page, "\uE708", "Red at night", "Turns the clock dim red so it doesn't light up the room.", new[] { "Automatic (schedule)", "Always day colors", "Always red" }, (int)work.Mode);
            dtStart = TimePick(page, "\uE708", "Night starts", "The clock fades to red over the 30 minutes before this.", work.NightStartMin);
            dtEnd = TimePick(page, "\uE706", "Night ends", "And back to white over the 30 minutes after this.", work.NightEndMin);
            Section(page, "Brightness");
            cbHardware = Toggle(page, "\uE706", "Change the screen's real brightness", "Checking your displays...", work.HardwareBrightness);
            hardwareCard = (SettingsCard)cbHardware.Parent;
            tbDay = Slider(page, "\uE706", "Daytime brightness", "Screen brightness while the clock shows during the day.", work.DayBrightness);
            tbNight = Slider(page, "\uE708", "Night brightness", "Screen brightness while the clock shows at night.", work.NightBrightness);

            // Weather
            page = NewPage("Weather", "\uE9CA");
            Section(page, "Forecast");
            cbWeather = Toggle(page, "\uE9CA", "Show weather", "Current conditions, today's high/low and the next 3 days, from Open-Meteo.", work.ShowWeather);
            txtCity = new TextBox { Text = work.WeatherCity, Width = Px(240), BorderStyle = BorderStyle.FixedSingle, AccessibleName = "City" };
            page.Controls.Add(new SettingsCard("\uE707", "City", "Leave blank to detect your location automatically.", txtCity));
            cmbUnits = Choice(page, "\uE9CA", "Units", "Automatic uses \u00B0F only where that's the local custom.", new[] { "Automatic (by country)", "Celsius (\u00B0C)", "Fahrenheit (\u00B0F)" }, work.WeatherUnits);

            // Desktop
            page = NewPage("Desktop", "\uE7F8");
            Section(page, "Live desktop clock");
            cbDesktop = Toggle(page, "\uE7F8", "Show the clock on my desktop", "A live clock behind your desktop icons, with the same look, night colors and weather. Uses the displays chosen on the Displays page. It never changes your screen brightness.", work.DesktopClock);
            page.Controls.Add(new SettingsCard("\uE946", "Tip", "Turn off \"Show seconds\" to save a little battery: the desktop then redraws once a minute.", null));

            // Lock screen
            page = NewPage("Lock screen", "\uE72E");
            Section(page, "Win+L");
            cbWinL = Toggle(page, "\uE765", "Show the clock when I press Win+L", "Instead of the Windows lock screen. Touch anything and Windows locks. Needs administrator approval once.", work.TakeOverWinL);
            cbSkip = Toggle(page, "\uE8D7", "Skip the lock screen picture", "Go straight to the password box. Needs administrator approval.", LockControl.SkipLockScreen());

            // Screensaver
            page = NewPage("Screensaver", "\uE708");
            Section(page, "When idle");
            numWait = new NumericUpDown { Minimum = 1, Maximum = 600, Value = Installer.TimeoutMinutes(), Width = Px(110), BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Start after (minutes)" };
            page.Controls.Add(new SettingsCard("\uE916", "Start after (minutes)", "How long the PC waits before the clock appears on its own.", numWait));
            cbLock = Toggle(page, "\uE72E", "Lock the PC when the clock is dismissed", "Windows asks for your password when you come back.", Installer.LockOnResume());
        }

        VStack NewPage(string title, string glyph)
        {
            var item = new NavItem(title, glyph);
            navItems.Add(item);
            var page = new VStack { Padding = new Padding(Px(40), Px(4), Px(40), Px(40)) };
            page.Controls.Add(new Label { Text = title, UseMnemonic = false, AutoSize = true, Font = new Font("Segoe UI Semibold", 21f), ForeColor = Fluent.Text, Margin = new Padding(0, 0, 0, Px(12)) });
            pages[item] = page;
            return page;
        }

        void ShowPage(NavItem item)
        {
            foreach (var n in navItems) n.Selected = n == item;
            scroller.SuspendLayout();
            if (current != null) scroller.Controls.Remove(current);
            current = pages[item];
            current.Location = new Point(0, 0);
            current.Width = scroller.ClientSize.Width;
            scroller.Controls.Add(current);
            scroller.AutoScrollPosition = Point.Empty;
            scroller.ResumeLayout();
            current.PerformLayout();
        }

        void Section(VStack page, string text)
        {
            page.Controls.Add(new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = Fluent.Text, Margin = new Padding(0, Px(20), 0, Px(8)) });
        }

        ToggleSwitch Toggle(VStack page, string glyph, string header, string description, bool value)
        {
            var t = new ToggleSwitch { Checked = value, Font = Font, AccessibleName = header };
            t.Size = t.GetPreferredSize(Size.Empty);
            t.CheckedChanged += (o, e) => Changed();
            page.Controls.Add(new SettingsCard(glyph, header, description, t));
            return t;
        }

        ComboBox Choice(VStack page, string glyph, string header, string description, string[] items, int index)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Px(240), AccessibleName = header };
            c.Items.AddRange(items);
            c.SelectedIndex = index;
            c.SelectedIndexChanged += (o, e) => Changed();
            page.Controls.Add(new SettingsCard(glyph, header, description, c));
            return c;
        }

        DateTimePicker TimePick(VStack page, string glyph, string header, string description, int minutes)
        {
            var d = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = Px(120), AccessibleName = header };
            d.Value = DateTime.Today.AddMinutes(minutes);
            d.ValueChanged += (o, e) => Changed();
            page.Controls.Add(new SettingsCard(glyph, header, description, d));
            return d;
        }

        TrackBar Slider(VStack page, string glyph, string header, string description, int value)
        {
            var box = new Panel { Size = new Size(Px(300), Px(40)), BackColor = Fluent.Card };
            var label = new Label { Text = value + "%", AutoSize = false, Size = new Size(Px(48), Px(40)), TextAlign = ContentAlignment.MiddleRight, ForeColor = Fluent.Text, Location = new Point(0, 0) };
            var t = new TrackBar { Minimum = 5, Maximum = 100, TickStyle = TickStyle.None, Value = value, AutoSize = false, Size = new Size(Px(240), Px(32)), Location = new Point(Px(56), Px(6)), BackColor = Fluent.Card, AccessibleName = header };
            t.ValueChanged += (o, e) => { label.Text = t.Value + "%"; Changed(); };
            box.Controls.Add(label);
            box.Controls.Add(t);
            page.Controls.Add(new SettingsCard(glyph, header, description, box));
            return t;
        }

        int Px(int v) { return (int)Math.Round(v * k); }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            CenterToScreen();
            ActiveControl = null;
        }

        // Title bar blends into the window like PowerToys (Windows 11).
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int caption = Fluent.Page.R | (Fluent.Page.G << 8) | (Fluent.Page.B << 16);
                int text = Fluent.Text.R | (Fluent.Text.G << 8) | (Fluent.Text.B << 16);
                Native.DwmSetWindowAttribute(Handle, 35, ref caption, 4);
                Native.DwmSetWindowAttribute(Handle, 36, ref text, 4);
            }
            catch (Exception) { }
        }

        double? PreviewNight()
        {
            switch (cmbPreview.SelectedIndex) { case 1: return 0; case 2: return 1; default: return null; }
        }

        // Settings save automatically shortly after a change, like PowerToys.
        void Changed()
        {
            if (loading) return;
            ReadControls(work);
            preview.ReloadSettings();
            saveTimer.Stop();
            saveTimer.Start();
        }

        void DetectBrightnessSupport()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> found;
                try { found = ScreenBrightness.Describe(); } catch (Exception) { found = new List<string>(); }
                string text = found.Count > 0
                    ? "Works on " + string.Join(", ", found.ToArray()) + ". Your normal brightness comes back when you touch the PC."
                    : "None of your displays accept brightness changes (for monitors, turn on DDC/CI in the monitor's menu). The clock's colors are dimmed instead.";
                try { BeginInvoke((Action)(() => hardwareCard.Description = text)); } catch (Exception) { }
            });
        }

        void ShowStatus()
        {
            if (Installer.IsActive())
            {
                string how = work.TakeOverWinL ? "Win+L or Ctrl+Alt+L shows it now." : "Ctrl+Alt+L shows it now.";
                info.Set(Tone.Success, "LockClock is on.", "Starts after " + Installer.TimeoutMinutes() + " min idle. " + how + " Changes save automatically.");
            }
            else
                info.Set(Tone.Info, "Not set up yet.", "Turn on LockClock as your screensaver and lock screen clock.", "Set up");
        }

        bool SaveNow()
        {
            try
            {
                ReadControls(work);
                string lockError = null;
                if (cbWinL.Checked != saved.TakeOverWinL || cbSkip.Checked != LockControl.SkipLockScreen())
                    lockError = LockControl.Configure(cbWinL.Checked, cbSkip.Checked);
                if (lockError != null)
                {
                    // Keep the previous lock-screen choices if the admin prompt was declined.
                    loading = true;
                    work.TakeOverWinL = saved.TakeOverWinL;
                    cbWinL.Checked = work.TakeOverWinL;
                    cbSkip.Checked = LockControl.SkipLockScreen();
                    loading = false;
                }
                work.Save();
                saved = work.Clone();
                Installer.Install((int)numWait.Value, cbLock.Checked);
                ShowStatus();
                if (lockError != null)
                {
                    info.Set(Tone.Error, "Not changed.", lockError);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                info.Set(Tone.Error, "Couldn't save.", ex.Message);
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
            bool driver = true;
            foreach (var scr in Screen.AllScreens)
            {
                var f = new ClockForm(s, scr.Bounds, true, IntPtr.Zero) { TestMode = true, PreviewNight = PreviewNight(), Blank = !s.ShowsClockOn(scr), TestGroup = group, DrivesBrightness = driver && s.HardwareBrightness };
                driver = false;
                group.Add(f);
                f.Show();
                if (!f.Blank && main == null) main = f;
            }
            if (s.HardwareBrightness) ScreenBrightness.Begin();
            (main ?? (ClockForm)group[0]).Activate();
        }

        static string DisplayNumber(Screen s)
        {
            string digits = new string(Array.FindAll(s.DeviceName.ToCharArray(), char.IsDigit));
            return digits.Length > 0 ? digits : "?";
        }

        void AddDisplayChooser(VStack page)
        {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Fluent.Card };
            cmbDisplay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Px(240), Margin = new Padding(0, Px(4), 0, 0), AccessibleName = "Show the clock on" };
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
            cmbDisplay.SelectedIndexChanged += (o, e) => Changed();
            var identify = new FluentButton("Identify", ButtonKind.Secondary) { Margin = new Padding(0, 0, Px(8), 0) };
            identify.Click += (o, e) => IdentifyDisplays();
            row.Controls.Add(identify);
            row.Controls.Add(cmbDisplay);
            row.Size = row.GetPreferredSize(Size.Empty);
            page.Controls.Add(new SettingsCard("\uE7F4", "Show the clock on", "Other screens turn black while the clock is showing, so nothing on them is visible.", row));
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
                    Font = new Font("Segoe UI", size * 0.45f, FontStyle.Bold, GraphicsUnit.Pixel), ForeColor = s.Primary ? Fluent.Accent : Color.White
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
            t.HardwareBrightness = cbHardware.Checked;
            t.ShowWeather = cbWeather.Checked;
            t.WeatherCity = txtCity.Text.Trim();
            t.WeatherUnits = cmbUnits.SelectedIndex;
            t.TakeOverWinL = cbWinL.Checked;
            t.DesktopClock = cbDesktop.Checked;
            t.Display = displayValues[cmbDisplay.SelectedIndex];
        }
    }

    // Live clock on the desktop background, behind the icons (the technique live-wallpaper apps use).
    // Runs as its own process ("/desktop"), started at sign-in while the option is on.
    static class DesktopClock
    {
        const string MutexName = @"Local\LockClock.Desktop";
        const string QuitEventName = @"Local\LockClock.DesktopQuit";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "LockClockDesktop";
        static readonly List<ClockForm> forms = new List<ClockForm>();
        static IntPtr parent;
        static string layout;
        static int revision = -1;

        static void Log(string msg)
        {
            try
            {
                string path = Path.Combine(Installer.Dir, "desktop.log");
                if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + msg + Environment.NewLine);
            }
            catch (Exception) { }
        }

        public static int Run()
        {
            if (!Settings.Load().DesktopClock) return 0;
            bool first;
            using (var mutex = new Mutex(true, MutexName, out first))
            {
                if (!first) return 0;
                using (var quit = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName))
                {
                    quit.Reset();
                    Attach();
                    var timer = new System.Windows.Forms.Timer { Interval = 2000 };
                    timer.Tick += (o, e) =>
                    {
                        if (quit.WaitOne(0)) { Application.ExitThread(); return; }
                        var s = Settings.Load();
                        if (!s.DesktopClock) { Application.ExitThread(); return; }
                        // Re-attach when Explorer restarts, displays change, or settings were saved.
                        if (!Healthy() || Settings.Revision() != revision) Attach();
                    };
                    timer.Start();
                    Log("desktop clock started");
                    Application.Run();
                    timer.Dispose();
                    Detach();
                    Log("desktop clock stopped");
                }
            }
            return 0;
        }

        static string Layout()
        {
            var sb = new StringBuilder();
            foreach (var s in Screen.AllScreens) sb.Append(s.DeviceName).Append(s.Bounds).Append(';');
            return sb.ToString();
        }

        static bool Healthy()
        {
            if (parent == IntPtr.Zero || !Native.IsWindow(parent) || Layout() != layout) return false;
            foreach (var f in forms) if (f.IsDisposed || !Native.IsWindow(f.Handle)) return false;
            return true;
        }

        static void Detach()
        {
            foreach (var f in forms) if (!f.IsDisposed) f.Close();
            forms.Clear();
        }

        static void Attach()
        {
            Detach();
            revision = Settings.Revision();
            layout = Layout();
            var settings = Settings.Load();
            IntPtr insertAfter;
            parent = FindParent(out insertAfter);
            if (parent == IntPtr.Zero) { Log("desktop window not found"); return; }
            foreach (var screen in Screen.AllScreens)
            {
                if (!settings.ShowsClockOn(screen)) continue;
                var r = new Native.RECT { Left = screen.Bounds.Left, Top = screen.Bounds.Top, Right = screen.Bounds.Right, Bottom = screen.Bounds.Bottom };
                Native.MapWindowPoints(IntPtr.Zero, parent, ref r, 2);
                var f = ClockForm.CreateDesktop(settings, new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), parent, insertAfter);
                f.Show();
                forms.Add(f);
            }
            Log("attached to " + (insertAfter != IntPtr.Zero ? "Progman (Windows 11 24H2+ layout)" : "WorkerW (classic layout)") + ", " + forms.Count + " display(s)");
        }

        // Returns the window to parent the clock to. On Windows 11 24H2+ that's Progman, with the clock placed just
        // below the icon layer (insertAfter = SHELLDLL_DefView); on older builds it's the WorkerW behind the icons.
        static IntPtr FindParent(out IntPtr insertAfter)
        {
            insertAfter = IntPtr.Zero;
            IntPtr progman = Native.FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return IntPtr.Zero;
            IntPtr ignored;
            // Ask Explorer to create the wallpaper layer.
            Native.SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1000, out ignored);
            Native.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out ignored);
            IntPtr defView = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero && Native.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null) != IntPtr.Zero)
            {
                insertAfter = defView;
                return progman;
            }
            IntPtr worker = IntPtr.Zero;
            Native.EnumWindows((top, l) =>
            {
                if (Native.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                    worker = Native.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                return true;
            }, IntPtr.Zero);
            return worker;
        }

        public static bool IsRunning()
        {
            Mutex m;
            if (!Mutex.TryOpenExisting(MutexName, out m)) return false;
            m.Dispose();
            return true;
        }

        public static void Stop()
        {
            EventWaitHandle quit;
            if (!EventWaitHandle.TryOpenExisting(QuitEventName, out quit)) return;
            using (quit) quit.Set();
            for (int i = 0; i < 40 && IsRunning(); i++) Thread.Sleep(100);
        }

        // Starts at sign-in only while the option is on. Restarts only when asked (e.g. new files).
        public static void Configure(bool enabled, bool restart = false)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) k.SetValue(RunValue, "\"" + Installer.ExePath + "\" /desktop");
                else k.DeleteValue(RunValue, false);
            }
            if (!enabled) { Stop(); return; }
            if (restart || !IsRunning())
            {
                Stop();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Installer.ExePath, "/desktop") { UseShellExecute = false });
            }
        }
    }

    // Real screen brightness while the clock shows: DDC/CI for external monitors, WMI for built-in laptop panels.
    // Original levels are saved to the registry so they can be restored even if the clock process is killed.
    static class ScreenBrightness
    {
        class Target { public string Key; public IntPtr Physical; public uint Min, Max, Original; public bool Wmi; public string Device; }

        static readonly object gate = new object();      // state
        static readonly object applyGate = new object(); // hardware calls; restore waits for an in-flight change
        static int session;
        static List<Target> targets;
        static readonly List<Native.PHYSICAL_MONITOR[]> handles = new List<Native.PHYSICAL_MONITOR[]>();
        static int pending = -1, applied = -1;
        static bool worker, restored;
        public static int Version;
        const string LeftoverKey = @"Software\LockClock\BrightnessRestore";

        public static bool Controls(Screen screen)
        {
            var t = targets;
            if (t == null) return false;
            foreach (var x in t) if (string.Equals(x.Device, screen.DeviceName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static void Begin()
        {
            int mine;
            lock (gate) { mine = ++session; targets = null; restored = false; pending = -1; applied = -1; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                var found = Detect(true);
                lock (gate)
                {
                    if (restored || mine != session) return; // dismissed before we got going
                    SaveLeftover(found);
                    targets = found;
                    Version++;
                }
                Kick();
            });
        }

        public static void SetLevel(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            lock (gate)
            {
                if (restored || percent == pending) return;
                pending = percent;
            }
            Kick();
        }

        static void Kick()
        {
            lock (gate)
            {
                if (worker || targets == null || pending < 0 || pending == applied) return;
                worker = true;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                while (true)
                {
                    int want;
                    List<Target> list;
                    lock (gate)
                    {
                        if (restored || pending == applied) { worker = false; return; }
                        want = pending;
                        list = targets;
                    }
                    lock (applyGate)
                    {
                        lock (gate) if (restored) { worker = false; return; }
                        foreach (var t in list) Set(t, (uint)Math.Round(t.Min + (t.Max - t.Min) * want / 100.0));
                    }
                    lock (gate) applied = want;
                }
            });
        }

        // Puts the original brightness back (waits for any change in progress).
        public static void Restore()
        {
            List<Target> list;
            lock (gate)
            {
                if (restored) return;
                restored = true;
                list = targets;
            }
            if (list == null) return;
            lock (applyGate)
            {
                foreach (var t in list) Set(t, t.Original);
                lock (gate) { foreach (var h in handles) Native.DestroyPhysicalMonitors((uint)h.Length, h); handles.Clear(); }
            }
            ClearLeftover();
        }

        // Used by the helper after sign-in: if a clock was killed while the screen was dimmed, undo it.
        public static bool RestoreLeftover()
        {
            Dictionary<string, uint> saved = new Dictionary<string, uint>();
            using (var k = Registry.CurrentUser.OpenSubKey(LeftoverKey))
            {
                if (k == null) return false;
                foreach (var n in k.GetValueNames()) if (k.GetValue(n) is int) saved[n] = (uint)(int)k.GetValue(n);
            }
            Mutex clock;
            if (Mutex.TryOpenExisting(LockControl.ClockMutexName, out clock)) { clock.Dispose(); return false; } // still showing
            if (saved.Count == 0) { ClearLeftover(); return false; }
            foreach (var t in Detect(false))
            {
                uint v;
                if (saved.TryGetValue(t.Key, out v)) Set(t, v);
            }
            lock (gate) { foreach (var h in handles) Native.DestroyPhysicalMonitors((uint)h.Length, h); handles.Clear(); }
            ClearLeftover();
            return true;
        }

        // For the settings page: which displays can be dimmed.
        public static List<string> Describe()
        {
            var result = new List<string>();
            foreach (var t in Detect(false))
                result.Add("Display " + new string(Array.FindAll(t.Device.ToCharArray(), char.IsDigit)) + (t.Wmi ? " (built-in)" : " (DDC/CI)"));
            lock (gate) { foreach (var h in handles) Native.DestroyPhysicalMonitors((uint)h.Length, h); handles.Clear(); }
            return result;
        }

        static List<Target> Detect(bool keepHandles)
        {
            var list = new List<Target>();
            var screensWithoutDdc = new List<Screen>();
            foreach (var screen in Screen.AllScreens)
            {
                var center = new Native.POINT { X = screen.Bounds.X + screen.Bounds.Width / 2, Y = screen.Bounds.Y + screen.Bounds.Height / 2 };
                IntPtr hmon = Native.MonitorFromPoint(center, 2);
                uint n;
                bool any = false;
                if (Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out n) && n > 0)
                {
                    var arr = new Native.PHYSICAL_MONITOR[n];
                    if (Native.GetPhysicalMonitorsFromHMONITOR(hmon, n, arr))
                    {
                        lock (gate) handles.Add(arr);
                        for (int i = 0; i < arr.Length; i++)
                        {
                            uint min, cur, max;
                            if (Native.GetMonitorBrightness(arr[i].Handle, out min, out cur, out max) && max > min)
                            {
                                list.Add(new Target { Key = "ddc:" + screen.DeviceName + ":" + i, Physical = arr[i].Handle, Min = min, Max = max, Original = cur, Device = screen.DeviceName });
                                any = true;
                            }
                        }
                    }
                }
                if (!any) screensWithoutDdc.Add(screen);
            }
            // Built-in panels don't speak DDC/CI; Windows exposes them through WMI instead.
            if (screensWithoutDdc.Count > 0)
            {
                int current;
                if (WmiGet(out current))
                {
                    var internalScreen = screensWithoutDdc.Find(s => s.Primary) ?? screensWithoutDdc[0];
                    list.Add(new Target { Key = "wmi", Wmi = true, Min = 0, Max = 100, Original = (uint)current, Device = internalScreen.DeviceName });
                }
            }
            return list;
        }

        static void Set(Target t, uint value)
        {
            try
            {
                if (t.Wmi) WmiSet((int)value);
                else Native.SetMonitorBrightness(t.Physical, value);
            }
            catch (Exception) { }
        }

        static bool WmiGet(out int value)
        {
            value = 0;
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE"))
                    foreach (System.Management.ManagementObject o in s.Get()) { value = Convert.ToInt32(o["CurrentBrightness"]); return true; }
            }
            catch (Exception) { }
            return false;
        }

        static void WmiSet(int value)
        {
            using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE"))
                foreach (System.Management.ManagementObject o in s.Get())
                    o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)value });
        }

        static void SaveLeftover(List<Target> list)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(LeftoverKey))
                    foreach (var t in list) k.SetValue(t.Key, (int)t.Original, RegistryValueKind.DWord);
            }
            catch (Exception) { }
        }

        static void ClearLeftover()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(LeftoverKey, false); } catch (Exception) { }
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
                if (ev == WTS_SESSION_UNLOCK || ev == WTS_SESSION_LOGON)
                {
                    Log("session unlocked");
                    Block();
                    if (ScreenBrightness.RestoreLeftover()) Log("restored screen brightness left dimmed by a closed clock");
                }
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

        public static bool IsRunning()
        {
            Mutex m;
            if (!Mutex.TryOpenExisting(MutexName, out m)) return false;
            m.Dispose();
            return true;
        }

        // Starts at sign-in only while the Win+L option is on. Restarts only when asked (e.g. new files).
        public static void Configure(bool enabled, bool restart = false)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) k.SetValue("LockClock", "\"" + Installer.ExePath + "\" /agent");
                else k.DeleteValue("LockClock", false);
            }
            if (!enabled)
            {
                Stop();
                LockControl.SetWinLBlocked(false);
                return;
            }
            if (restart || !IsRunning())
            {
                Stop();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Installer.ExePath, "/agent") { UseShellExecute = false });
            }
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
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR { public IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern int MapWindowPoints(IntPtr from, IntPtr to, ref RECT rect, int points);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
        [DllImport("dxva2.dll")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);
        [DllImport("dxva2.dll")] public static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);
        [DllImport("dxva2.dll")] public static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);
        [DllImport("dxva2.dll")] public static extern bool SetMonitorBrightness(IntPtr monitor, uint value);
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
        // Ask any showing clock to finish the normal way so its files can be replaced; wait up to 5 seconds.
        static void CloseShowingClocks()
        {
            Mutex clock;
            if (!Mutex.TryOpenExisting(LockControl.ClockMutexName, out clock)) return;
            clock.Dispose();
            using (var quit = new EventWaitHandle(false, EventResetMode.ManualReset, ClockForm.QuitEventName))
            {
                quit.Set();
                for (int i = 0; i < 50 && Mutex.TryOpenExisting(LockControl.ClockMutexName, out clock); i++) { clock.Dispose(); Thread.Sleep(100); }
                quit.Reset();
            }
        }

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
                    if (num != null && (mode == "/p" || mode == "/c") && long.TryParse(num, out h)) { hwnd = new IntPtr(h); if (colon < 0) i++; }
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
                    ClockForm.QuitSignal = new EventWaitHandle(false, EventResetMode.ManualReset, ClockForm.QuitEventName);
                    bool driver = true;
                    foreach (var scr in Screen.AllScreens)
                    {
                        new ClockForm(settings, scr.Bounds, true, IntPtr.Zero, lockWhenDone) { Blank = !settings.ShowsClockOn(scr), DrivesBrightness = driver && settings.HardwareBrightness }.Show();
                        driver = false;
                    }
                    if (settings.HardwareBrightness) ScreenBrightness.Begin();
                    Application.ApplicationExit += (o, e) => ScreenBrightness.Restore();
                    Application.Run();
                    GC.KeepAlive(clockMutex);
                    return 0;
                }
                case "/brightness-test":
                {
                    // Diagnostic: /brightness-test <percent> <seconds>; same code path as the clock, then restores.
                    int pct = extra.Count > 0 ? int.Parse(extra[0].TrimStart('/'), CultureInfo.InvariantCulture) : 50;
                    int secs = extra.Count > 1 ? int.Parse(extra[1].TrimStart('/'), CultureInfo.InvariantCulture) : 3;
                    string report = Path.Combine(Installer.Dir, "brightness-test.txt");
                    var lines = new List<string> { "displays: " + string.Join(", ", ScreenBrightness.Describe().ToArray()) };
                    ScreenBrightness.Begin();
                    ScreenBrightness.SetLevel(pct);
                    Thread.Sleep(secs * 1000);
                    lines.Add("set to " + pct + "% for " + secs + "s; restoring");
                    ScreenBrightness.Restore();
                    Directory.CreateDirectory(Installer.Dir);
                    File.WriteAllLines(report, lines.ToArray());
                    return 0;
                }
                case "/agent":
                    return Agent.Run();
                case "/desktop":
                    return DesktopClock.Run();
                case "/stopagent":
                    Agent.Stop();
                    DesktopClock.Stop();
                    CloseShowingClocks();
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
                    CloseShowingClocks();
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
