using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MinecraftHelper
{
    /// <summary>
    /// Czytelne kolory statusów dla jasnego motywu glass. Kod nadal przekazuje
    /// Brushes.MediumSpringGreen/DeepSkyBlue/Orange/OrangeRed jako znaczenie statusu,
    /// a <see cref="Readable"/> zamienia je na odcienie widoczne na białym tle.
    /// </summary>
    internal static class GlassPalette
    {
        public static readonly SolidColorBrush Ink = Create(0x16, 0x1A, 0x2B);
        public static readonly SolidColorBrush Muted = Create(0x4F, 0x56, 0x6B);
        public static readonly SolidColorBrush Line = Create(0x17, 0x16, 0x1A, 0x2B);
        public static readonly SolidColorBrush Ok = Create(0x15, 0x80, 0x3D);
        public static readonly SolidColorBrush OkSoft = Create(0x21, 0x16, 0xA3, 0x4A);
        public static readonly SolidColorBrush Info = Create(0x03, 0x69, 0xA1);
        public static readonly SolidColorBrush InfoSoft = Create(0x1C, 0x03, 0x69, 0xA1);
        public static readonly SolidColorBrush Warn = Create(0xB4, 0x53, 0x09);
        public static readonly SolidColorBrush WarnSoft = Create(0x24, 0xF5, 0x9E, 0x0B);
        public static readonly SolidColorBrush Bad = Create(0xC8, 0x1E, 0x1E);
        public static readonly SolidColorBrush BadSoft = Create(0x1F, 0xEF, 0x44, 0x44);
        public static readonly SolidColorBrush NeutralSoft = Create(0x12, 0x16, 0x1A, 0x2B);

        public static Brush Readable(Brush brush)
        {
            if (ReferenceEquals(brush, Brushes.MediumSpringGreen) || ReferenceEquals(brush, Brushes.LimeGreen))
                return Ok;
            if (ReferenceEquals(brush, Brushes.DeepSkyBlue))
                return Info;
            if (ReferenceEquals(brush, Brushes.Orange) || ReferenceEquals(brush, Brushes.Gold))
                return Warn;
            if (ReferenceEquals(brush, Brushes.OrangeRed) || ReferenceEquals(brush, Brushes.Red) || ReferenceEquals(brush, Brushes.IndianRed))
                return Bad;
            return brush;
        }

        public static Brush SoftFor(Brush readable)
        {
            if (ReferenceEquals(readable, Ok))
                return OkSoft;
            if (ReferenceEquals(readable, Info))
                return InfoSoft;
            if (ReferenceEquals(readable, Warn))
                return WarnSoft;
            if (ReferenceEquals(readable, Bad))
                return BadSoft;
            return NeutralSoft;
        }

        public static Brush AccentInk => Resource("GlassAccentInkBrush", Info);

        public static Brush AccentSoft => Resource("GlassAccentSoftBrush", InfoSoft);

        public static Brush Accent => Resource("GlassAccentBrush", Info);

        public static Brush AccentSolid => Resource("GlassAccentSolidBrush", Info);

        /// <summary>
        /// Zamienia kolor ze starego, ciemnego motywu na odpowiednik glass: ciemne tła na białe szkło,
        /// obramowania na delikatną linię, jasny tekst na atrament, a kolory statusów na czytelne odcienie.
        /// </summary>
        public static Color MapLegacyColor(byte red, byte green, byte blue)
        {
            switch ((red, green, blue))
            {
                case (46, 168, 255):
                case (74, 181, 255):
                case (70, 177, 255):
                    return AccentColor("GlassAccentColor", Info.Color);
                case (127, 200, 255):
                case (151, 174, 255):
                case (98, 200, 255):
                    return ((SolidColorBrush)AccentInk).Color;
                case (23, 50, 74):
                case (18, 50, 77):
                case (17, 50, 74):
                    return ((SolidColorBrush)AccentSoft).Color;
            }

            double r = red / 255.0, g = green / 255.0, b = blue / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double saturation = max <= 0 ? 0 : (max - min) / max;
            double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

            if (saturation > 0.35 && max > 0.6)
                return HueColor(Hue(r, g, b, max, min));
            if (saturation > 0.3 && luminance < 0.32)
            {
                Color tint = HueColor(Hue(r, g, b, max, min));
                return Color.FromArgb(0x24, tint.R, tint.G, tint.B);
            }
            if (luminance < 0.22)
            {
                // Ciemne powierzchnie: najciemniejsze tło staje się prawie przezroczyste, wyniesione panele — bielsze.
                byte alpha = (byte)Math.Round(0x73 + (Math.Clamp(luminance, 0, 0.22) / 0.22) * (0xE0 - 0x73));
                return Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF);
            }
            if (luminance < 0.5)
                return Color.FromArgb(0x26, 0x16, 0x1A, 0x2B);
            return luminance < 0.75 ? Muted.Color : Ink.Color;
        }

        public static SolidColorBrush MapLegacyBrush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(MapLegacyColor(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static double Hue(double r, double g, double b, double max, double min)
        {
            double delta = max - min;
            if (delta <= 0)
                return 0;
            double hue = max == r ? (g - b) / delta % 6 : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
            hue *= 60;
            return hue < 0 ? hue + 360 : hue;
        }

        private static Color HueColor(double hue)
        {
            if (hue < 20 || hue >= 330)
                return Bad.Color;
            if (hue < 65)
                return Warn.Color;
            if (hue < 185)
                return Ok.Color;
            if (hue < 245)
                return ((SolidColorBrush)AccentInk).Color;
            return Color.FromRgb(0x7C, 0x3A, 0xED);
        }

        private static Brush Resource(string key, Brush fallback) =>
            Application.Current?.TryFindResource(key) as Brush ?? fallback;

        private static Color AccentColor(string key, Color fallback) =>
            Application.Current?.TryFindResource(key) is Color color ? color : fallback;

        private static SolidColorBrush Create(byte red, byte green, byte blue) => Create(0xFF, red, green, blue);

        private static SolidColorBrush Create(byte alpha, byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>Podmiana koloru akcentu w czasie działania (GlassAccentIris/Koral/Ocean.xaml).</summary>
    internal static class GlassAccents
    {
        public static readonly string[] Ids = { "iris", "koral", "ocean" };

        public static string Normalize(string? id)
        {
            foreach (string known in Ids)
            {
                if (string.Equals(known, id?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return known;
            }
            return Ids[0];
        }

        public static void Apply(string? id)
        {
            if (Application.Current == null)
                return;

            string normalized = Normalize(id);
            string fileName = "GlassAccent" + char.ToUpperInvariant(normalized[0]) + normalized[1..] + ".xaml";
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            for (int index = 0; index < dictionaries.Count; index++)
            {
                string? source = dictionaries[index].Source?.OriginalString;
                if (source == null || !source.Contains("GlassAccent", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (source.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                    return;

                dictionaries[index] = new ResourceDictionary { Source = new Uri("Themes/" + fileName, UriKind.Relative) };
                return;
            }
        }
    }

    /// <summary>Wspólny wygląd okien budowanych w kodzie: tło glass, czcionka i jasny pasek tytułu.</summary>
    internal static class GlassChrome
    {
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaCaptionColor = 35;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        public static void Apply(Window window)
        {
            window.SetResourceReference(Control.BackgroundProperty, "GlassBackdropBrush");
            window.SetResourceReference(Control.FontFamilyProperty, "GlassFont");
            window.Foreground = GlassPalette.Ink;
            window.FontSize = 12.5;
            window.UseLayoutRounding = true;
            window.SourceInitialized += (_, _) => ApplyTitleBar(window);
        }

        public static void ApplyTitleBar(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;

            int disabled = 0;
            int attribute = Environment.OSVersion.Version.Build >= 18985
                ? DwmwaUseImmersiveDarkMode
                : DwmwaUseImmersiveDarkModeBefore20H1;
            _ = DwmSetWindowAttribute(handle, attribute, ref disabled, sizeof(int));
            int captionColor = 0x00F8F1EE;
            _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
        }

        public static Style GetStyle(string key) => (Style)Application.Current.FindResource(key);

        public static Brush GetBrush(string key) => (Brush)Application.Current.FindResource(key);
    }

    /// <summary>
    /// Płynne rozwijanie i zwijanie sekcji (jak w projekcie canvy): wysokość, górny odstęp,
    /// przezroczystość i lekki zjazd treści w ~0,4 s z miękkim wyhamowaniem.
    /// </summary>
    public static class GlassMotion
    {
        private static readonly TimeSpan ExpandDuration = TimeSpan.FromMilliseconds(420);
        private static readonly TimeSpan CollapseDuration = TimeSpan.FromMilliseconds(360);
        private static readonly IEasingFunction Ease = CreateEase();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, SectionState> States = new();

        private sealed class SectionState
        {
            public bool Expanded;
            public Thickness Margin;
            public bool MarginKnown;
        }

        public static void SetExpanded(FrameworkElement section, bool expanded, bool animate)
        {
            SectionState state = States.GetValue(section, _ => new SectionState { Expanded = section.Visibility == Visibility.Visible });
            if (!state.MarginKnown)
            {
                state.Margin = section.Margin;
                state.MarginKnown = true;
            }

            bool wasExpanded = state.Expanded && section.Visibility == Visibility.Visible;
            state.Expanded = expanded;
            if (wasExpanded == expanded && section.Height is double.NaN)
                return;

            StopAnimations(section);
            if (!animate || !SystemParameters.ClientAreaAnimation)
            {
                Reset(section, state);
                section.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                return;
            }

            var shift = new TranslateTransform(0, expanded ? -10 : 0);
            section.RenderTransform = shift;

            if (expanded)
            {
                section.Visibility = Visibility.Visible;
                section.Height = double.NaN;
                section.Margin = state.Margin;
                section.UpdateLayout();
                double target = Math.Max(1.0, section.ActualHeight);

                section.Height = 0;
                section.Opacity = 0;
                section.Margin = WithoutTop(state.Margin);

                var height = new DoubleAnimation(0, target, ExpandDuration) { EasingFunction = Ease };
                height.Completed += (_, _) =>
                {
                    if (!state.Expanded)
                        return;
                    StopAnimations(section);
                    Reset(section, state);
                };
                section.BeginAnimation(FrameworkElement.HeightProperty, height);
                section.BeginAnimation(FrameworkElement.MarginProperty, new ThicknessAnimation(WithoutTop(state.Margin), state.Margin, ExpandDuration) { EasingFunction = Ease });
                section.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(340)) { BeginTime = TimeSpan.FromMilliseconds(50), EasingFunction = Ease });
                shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, 0, ExpandDuration) { EasingFunction = Ease });
                return;
            }

            if (section.Visibility != Visibility.Visible)
            {
                Reset(section, state);
                section.Visibility = Visibility.Collapsed;
                return;
            }

            double current = Math.Max(1.0, section.ActualHeight);
            section.Height = current;
            var collapse = new DoubleAnimation(current, 0, CollapseDuration) { EasingFunction = Ease };
            collapse.Completed += (_, _) =>
            {
                if (state.Expanded)
                    return;
                StopAnimations(section);
                Reset(section, state);
                section.Visibility = Visibility.Collapsed;
            };
            section.BeginAnimation(FrameworkElement.HeightProperty, collapse);
            section.BeginAnimation(FrameworkElement.MarginProperty, new ThicknessAnimation(state.Margin, WithoutTop(state.Margin), CollapseDuration) { EasingFunction = Ease });
            section.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(section.Opacity, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = Ease });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -8, CollapseDuration) { EasingFunction = Ease });
        }

        private static IEasingFunction CreateEase()
        {
            var ease = new CubicBezierEase(0.2, 0.8, 0.2, 1.0);
            ease.Freeze();
            return ease;
        }

        private static void StopAnimations(FrameworkElement section)
        {
            section.BeginAnimation(FrameworkElement.HeightProperty, null);
            section.BeginAnimation(FrameworkElement.MarginProperty, null);
            section.BeginAnimation(UIElement.OpacityProperty, null);
        }

        private static void Reset(FrameworkElement section, SectionState state)
        {
            section.Height = double.NaN;
            section.Opacity = 1.0;
            section.Margin = state.Margin;
            section.RenderTransform = Transform.Identity;
        }

        private static Thickness WithoutTop(Thickness margin) => new(margin.Left, 0, margin.Right, margin.Bottom);
    }

    /// <summary>Krzywa cubic-bezier jak w CSS (np. cubic-bezier(.2,.8,.2,1) z projektu canvy).</summary>
    internal sealed class CubicBezierEase : EasingFunctionBase
    {
        private readonly double _x1;
        private readonly double _y1;
        private readonly double _x2;
        private readonly double _y2;

        public CubicBezierEase(double x1, double y1, double x2, double y2)
        {
            _x1 = x1;
            _y1 = y1;
            _x2 = x2;
            _y2 = y2;
            EasingMode = EasingMode.EaseIn;
        }

        protected override double EaseInCore(double normalizedTime)
        {
            // Szuka parametru t, dla którego krzywa osiąga zadany czas, i zwraca jej wartość.
            double t = normalizedTime;
            for (int iteration = 0; iteration < 8; iteration++)
            {
                double x = Bezier(t, _x1, _x2) - normalizedTime;
                double slope = BezierSlope(t, _x1, _x2);
                if (Math.Abs(x) < 1e-5 || Math.Abs(slope) < 1e-6)
                    break;
                t = Math.Clamp(t - x / slope, 0, 1);
            }
            return Bezier(t, _y1, _y2);
        }

        private static double Bezier(double t, double p1, double p2) =>
            3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;

        private static double BezierSlope(double t, double p1, double p2) =>
            3 * (1 - t) * (1 - t) * p1 + 6 * (1 - t) * t * (p2 - p1) + 3 * t * t * (1 - p2);

        protected override Freezable CreateInstanceCore() => new CubicBezierEase(_x1, _y1, _x2, _y2);
    }
}
