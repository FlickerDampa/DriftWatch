using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using DriftWatch.Core;

namespace DriftWatch.UI
{
    /// <summary>Converts design pixels (at 96 DPI) to device pixels.</summary>
    public static class Dpi
    {
        public static float Factor = 1f;
        /// <summary>Extra font scaling, only used when DRIFTWATCH_SCALE forces a scale the system doesn't apply itself.</summary>
        public static float FontFactor = 1f;

        public static void Init()
        {
            float system = 1f;
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) system = g.DpiX / 96f;
            }
            catch (Exception) { }
            if (system < 1f) system = 1f;
            Factor = system;
            float forced;
            string env = Environment.GetEnvironmentVariable("DRIFTWATCH_SCALE");
            if (!string.IsNullOrEmpty(env) && float.TryParse(env, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out forced) && forced >= 1f && forced <= 4f)
            {
                FontFactor = forced / system;
                Factor = forced;
            }
        }

        public static int S(int px) { return (int)Math.Round(px * Factor); }
        public static float S(float px) { return px * Factor; }
        public static Size S(int w, int h) { return new Size(S(w), S(h)); }
        public static Padding P(int all) { return new Padding(S(all)); }
        public static Padding P(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }
    }

    /// <summary>The colour palette. Dark is the default look; light follows the Windows app theme.</summary>
    public sealed class Theme
    {
        public bool Dark;
        public Color Bg, Rail, Card, CardHover, Field, Line, LineSoft;
        public Color Ink, Soft, Muted, Faint;
        public Color Accent, Accent2, AccentInk, Ok, Warn, Bad;
        public Color Critical, High, Medium, Low, Info;
        public Color BeforeBg, AfterBg, BeforeInk, AfterInk;

        public static Theme Current = DarkTheme();
        public static string Mode = "system";
        public static event EventHandler Changed;

        public static Theme DarkTheme()
        {
            return new Theme
            {
                Dark = true,
                Bg = Hex("#0A0E18"), Rail = Hex("#0D1220"), Card = Hex("#111827"), CardHover = Hex("#172034"), Field = Hex("#0D1424"),
                Line = Hex("#1F2A40"), LineSoft = Hex("#182134"),
                Ink = Hex("#E8EDF7"), Soft = Hex("#B4BED3"), Muted = Hex("#7A86A1"), Faint = Hex("#4E5A74"),
                Accent = Hex("#22D3EE"), Accent2 = Hex("#8B7CF6"), AccentInk = Hex("#03121A"),
                Ok = Hex("#34D399"), Warn = Hex("#FBBF24"), Bad = Hex("#F43F5E"),
                Critical = Hex("#F43F5E"), High = Hex("#FB923C"), Medium = Hex("#FACC15"), Low = Hex("#60A5FA"), Info = Hex("#8C99B3"),
                BeforeBg = Hex("#2A1322"), AfterBg = Hex("#0E2A24"), BeforeInk = Hex("#FDA4B4"), AfterInk = Hex("#86EFC5")
            };
        }

        public static Theme LightTheme()
        {
            return new Theme
            {
                Dark = false,
                Bg = Hex("#F2F4F9"), Rail = Hex("#FFFFFF"), Card = Hex("#FFFFFF"), CardHover = Hex("#F5F7FC"), Field = Hex("#F6F8FB"),
                Line = Hex("#E0E5EF"), LineSoft = Hex("#ECEFF5"),
                Ink = Hex("#0E1525"), Soft = Hex("#3A4559"), Muted = Hex("#68738B"), Faint = Hex("#A0A8BA"),
                Accent = Hex("#0891B2"), Accent2 = Hex("#7C3AED"), AccentInk = Color.White,
                Ok = Hex("#059669"), Warn = Hex("#D97706"), Bad = Hex("#E11D48"),
                Critical = Hex("#E11D48"), High = Hex("#EA580C"), Medium = Hex("#CA8A04"), Low = Hex("#2563EB"), Info = Hex("#64748B"),
                BeforeBg = Hex("#FDECEF"), AfterBg = Hex("#E5F6EE"), BeforeInk = Hex("#9F1239"), AfterInk = Hex("#065F46")
            };
        }

        /// <summary>Picks the palette for "system", "dark" or "light". DRIFTWATCH_THEME overrides it.</summary>
        public static Theme For(string mode)
        {
            string forced = Environment.GetEnvironmentVariable("DRIFTWATCH_THEME");
            if (!string.IsNullOrEmpty(forced)) mode = forced;
            if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase)) return LightTheme();
            if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase)) return DarkTheme();
            return SystemPrefersLight() ? LightTheme() : DarkTheme();
        }

        public static bool SystemPrefersLight()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        var v = key.GetValue("AppsUseLightTheme");
                        if (v is int) return (int)v != 0;
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        public static void Apply(string mode)
        {
            Mode = mode;
            Current = For(mode);
            var h = Changed;
            if (h != null) h(null, EventArgs.Empty);
        }

        public Color For(Severity s)
        {
            switch (s)
            {
                case Severity.Critical: return Critical;
                case Severity.High: return High;
                case Severity.Medium: return Medium;
                case Severity.Low: return Low;
                default: return Info;
            }
        }

        public Color For(CheckStatus s)
        {
            switch (s)
            {
                case CheckStatus.Pass: return Ok;
                case CheckStatus.Warn: return Warn;
                case CheckStatus.Fail: return Bad;
                default: return Faint;
            }
        }

        public Color ForScore(int score)
        {
            return score >= 85 ? Ok : score >= 70 ? Accent : score >= 55 ? Warn : Bad;
        }

        /// <summary>A translucent tint of a colour over the card, for pill and icon backgrounds.</summary>
        public Color Tint(Color c, double amount)
        {
            return Mix(Card, c, amount);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, int a)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B);
        }

        public static Color Hex(string hex)
        {
            return ColorTranslator.FromHtml(hex);
        }
    }

    public static class Fonts
    {
        private static readonly string Family = Pick("Segoe UI", "Segoe UI Variable Text", "DejaVu Sans");
        private static readonly string SemiFamily = Pick("Segoe UI Semibold", "Segoe UI Variable Display Semib", "DejaVu Sans");
        private static readonly string MonoFamily = Pick("Cascadia Mono", "Consolas", "DejaVu Sans Mono");
        private static readonly bool HasSemi = SemiFamily.StartsWith("Segoe", StringComparison.Ordinal);

        public static readonly Font Body = Make(Family, 9.5f, FontStyle.Regular);
        public static readonly Font Small = Make(Family, 8.5f, FontStyle.Regular);
        public static readonly Font Tiny = Make(SemiFamily, 8f, HasSemi ? FontStyle.Regular : FontStyle.Bold);
        public static readonly Font Semi = Make(SemiFamily, 9.5f, HasSemi ? FontStyle.Regular : FontStyle.Bold);
        public static readonly Font SmallSemi = Make(SemiFamily, 8.5f, HasSemi ? FontStyle.Regular : FontStyle.Bold);
        public static readonly Font H3 = Make(SemiFamily, 11f, HasSemi ? FontStyle.Regular : FontStyle.Bold);
        public static readonly Font H2 = Make(SemiFamily, 13.5f, HasSemi ? FontStyle.Regular : FontStyle.Bold);
        public static readonly Font H1 = Make(SemiFamily, 17f, HasSemi ? FontStyle.Regular : FontStyle.Bold);
        public static readonly Font Number = Make(SemiFamily, 20f, HasSemi ? FontStyle.Regular : FontStyle.Bold);
        public static readonly Font Big = Make(SemiFamily, 30f, HasSemi ? FontStyle.Regular : FontStyle.Bold);
        public static readonly Font Mono = Make(MonoFamily, 9f, FontStyle.Regular);
        public static readonly Font MonoSmall = Make(MonoFamily, 8f, FontStyle.Regular);

        private static Font Make(string family, float size, FontStyle style)
        {
            return new Font(family, size * Dpi.FontFactor, style);
        }

        private static string Pick(params string[] names)
        {
            foreach (var n in names)
            {
                try
                {
                    using (var f = new FontFamily(n))
                        if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return f.Name;
                }
                catch (ArgumentException) { }
            }
            return SystemFonts.DefaultFont.FontFamily.Name;
        }
    }

    /// <summary>Windows-only calls for DPI awareness, dark title bars and dark scroll bars. Skipped on other platforms.</summary>
    public static class Native
    {
        public static bool IsWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public static void DpiAware()
        {
            if (!IsWindows) return;
            try { SetProcessDPIAware(); }
            catch (Exception) { }
        }

        /// <summary>Matches the window's title bar to the theme (Windows 10 2004+ and Windows 11).</summary>
        public static void TitleBar(Form f, Theme t)
        {
            if (!IsWindows || !f.IsHandleCreated) return;
            try
            {
                int dark = t.Dark ? 1 : 0;
                DwmSetWindowAttribute(f.Handle, 20, ref dark, 4);
                int caption = ColorTranslator.ToWin32(t.Rail);
                DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);
                int text = ColorTranslator.ToWin32(t.Ink);
                DwmSetWindowAttribute(f.Handle, 36, ref text, 4);
            }
            catch (Exception) { }
        }

        public static void DarkScrollbars(Control c, Theme t)
        {
            if (!IsWindows || !c.IsHandleCreated) return;
            try { SetWindowTheme(c.Handle, t.Dark ? "DarkMode_Explorer" : "Explorer", null); }
            catch (Exception) { }
        }
    }
}
