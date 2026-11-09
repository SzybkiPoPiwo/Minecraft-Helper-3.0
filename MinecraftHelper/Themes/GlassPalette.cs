using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace MinecraftHelper
{
    /// <summary>
    /// Kolory glass używane w kodzie. Kod nadal przekazuje Brushes.MediumSpringGreen/DeepSkyBlue/
    /// Orange/OrangeRed jako znaczenie statusu, a <see cref="Readable"/> zamienia je na odcienie
    /// czytelne w bieżącym motywie.
    /// Pędzle są współdzielone i NIE są zamrożone: <see cref="ApplyMode"/> (wołane przez
    /// <see cref="GlassThemeMode"/>) zmienia ich kolor w miejscu, więc każdy element, który ich
    /// używa — także w już otwartych oknach — przemalowuje się sam przy zmianie motywu.
    /// Wszystko działa na wątku UI.
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
        // Powierzchnie dla elementów budowanych w kodzie (odpowiedniki kluczy z GlassTheme.xaml).
        public static readonly SolidColorBrush Surface = Create(0x99, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush Subtle = Create(0x66, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush Popup = Create(0xF2, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush PopupStroke = Create(0xFF, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush HudTile = Create(0x8C, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush HudTileBorder = Create(0x99, 0xFF, 0xFF, 0xFF);

        public static bool IsDark { get; private set; }

        /// <summary>Kolor cienia kart i okien: granatowy na jasnym tle, czarny na ciemnym.</summary>
        public static Color ShadowColor => IsDark ? Color.FromRgb(0x00, 0x00, 0x00) : Color.FromRgb(0x1E, 0x24, 0x50);

        /// <summary>Przestawia współdzielone pędzle na motyw jasny albo ciemny.</summary>
        public static void ApplyMode(bool dark)
        {
            IsDark = dark;
            if (dark)
            {
                Set(Ink, 0xFF, 0xEC, 0xEE, 0xFA);
                Set(Muted, 0xFF, 0xA6, 0xAB, 0xC8);
                Set(Line, 0x1F, 0xFF, 0xFF, 0xFF);
                Set(Ok, 0xFF, 0x4A, 0xDE, 0x80);
                Set(OkSoft, 0x26, 0x4A, 0xDE, 0x80);
                Set(Info, 0xFF, 0x7D, 0xD3, 0xFC);
                Set(InfoSoft, 0x26, 0x7D, 0xD3, 0xFC);
                Set(Warn, 0xFF, 0xFB, 0xBF, 0x24);
                Set(WarnSoft, 0x26, 0xFB, 0xBF, 0x24);
                Set(Bad, 0xFF, 0xF8, 0x71, 0x71);
                Set(BadSoft, 0x2E, 0xF8, 0x71, 0x71);
                Set(NeutralSoft, 0x14, 0xFF, 0xFF, 0xFF);
                Set(Surface, 0x12, 0xFF, 0xFF, 0xFF);
                Set(Subtle, 0x0A, 0xFF, 0xFF, 0xFF);
                Set(Popup, 0xF5, 0x1E, 0x21, 0x38);
                Set(PopupStroke, 0x29, 0xFF, 0xFF, 0xFF);
                Set(HudTile, 0xD9, 0x16, 0x18, 0x2C);
                Set(HudTileBorder, 0x29, 0xFF, 0xFF, 0xFF);
                return;
            }

            Set(Ink, 0xFF, 0x16, 0x1A, 0x2B);
            Set(Muted, 0xFF, 0x4F, 0x56, 0x6B);
            Set(Line, 0x17, 0x16, 0x1A, 0x2B);
            Set(Ok, 0xFF, 0x15, 0x80, 0x3D);
            Set(OkSoft, 0x21, 0x16, 0xA3, 0x4A);
            Set(Info, 0xFF, 0x03, 0x69, 0xA1);
            Set(InfoSoft, 0x1C, 0x03, 0x69, 0xA1);
            Set(Warn, 0xFF, 0xB4, 0x53, 0x09);
            Set(WarnSoft, 0x24, 0xF5, 0x9E, 0x0B);
            Set(Bad, 0xFF, 0xC8, 0x1E, 0x1E);
            Set(BadSoft, 0x1F, 0xEF, 0x44, 0x44);
            Set(NeutralSoft, 0x12, 0x16, 0x1A, 0x2B);
            Set(Surface, 0x99, 0xFF, 0xFF, 0xFF);
            Set(Subtle, 0x66, 0xFF, 0xFF, 0xFF);
            Set(Popup, 0xF2, 0xFF, 0xFF, 0xFF);
            Set(PopupStroke, 0xFF, 0xFF, 0xFF, 0xFF);
            Set(HudTile, 0x8C, 0xFF, 0xFF, 0xFF);
            Set(HudTileBorder, 0x99, 0xFF, 0xFF, 0xFF);
        }

        private static void Set(SolidColorBrush brush, byte alpha, byte red, byte green, byte blue) =>
            brush.Color = Color.FromArgb(alpha, red, green, blue);

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
                // Ciemne powierzchnie: najciemniejsze tło staje się prawie przezroczyste, wyniesione panele — bielsze
                // (w motywie ciemnym: lekka biel na ciemnym szkle).
                double depth = Math.Clamp(luminance, 0, 0.22) / 0.22;
                byte alpha = IsDark
                    ? (byte)Math.Round(0x0A + depth * (0x26 - 0x0A))
                    : (byte)Math.Round(0x73 + depth * (0xE0 - 0x73));
                return Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF);
            }
            if (luminance < 0.5)
                return IsDark ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x26, 0x16, 0x1A, 0x2B);
            return luminance < 0.75 ? Muted.Color : Ink.Color;
        }

        /// <summary>
        /// Jak <see cref="MapLegacyColor"/>, ale dla kolorów statusów i tekstu zwraca współdzielony pędzel
        /// palety, który sam przełącza się z motywem (np. tekst paska stanu ustawiony przed zmianą motywu).
        /// </summary>
        public static SolidColorBrush LegacyBrush(byte red, byte green, byte blue)
        {
            Color color = MapLegacyColor(red, green, blue);
            foreach (SolidColorBrush shared in new[] { Ok, Warn, Bad, Info, Ink, Muted })
            {
                if (shared.Color == color)
                    return shared;
            }
            return new SolidColorBrush(color);
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
            // Celowo bez Freeze(): ApplyMode zmienia kolor przy przełączaniu motywu.
            return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
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
                if (!source.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                    dictionaries[index] = new ResourceDictionary { Source = new Uri("Themes/" + fileName, UriKind.Relative) };
                break;
            }

            // Ciemny motyw wylicza jaśniejszy tekst akcentu i ciemne tło z kolorów nowego akcentu.
            GlassThemeMode.Refresh();
        }

        /// <summary>Słownik aktualnego akcentu (oryginalne wartości, bez nadpisań ciemnego motywu).</summary>
        internal static ResourceDictionary? CurrentDictionary()
        {
            if (Application.Current == null)
                return null;
            foreach (ResourceDictionary dictionary in Application.Current.Resources.MergedDictionaries)
            {
                if (dictionary.Source?.OriginalString.Contains("GlassAccent", StringComparison.OrdinalIgnoreCase) == true)
                    return dictionary;
            }
            return null;
        }
    }

    /// <summary>
    /// Motyw jasny / ciemny / jak w Windows. Jasne wartości są w GlassTheme.xaml i GlassAccent*.xaml;
    /// ciemny motyw nadpisuje te same klucze w zasobach aplikacji (mają pierwszeństwo przed
    /// słownikami scalonymi), więc wszystko, co korzysta z DynamicResource, przemalowuje się od razu.
    /// Pędzle używane w kodzie przestawia <see cref="GlassPalette.ApplyMode"/>.
    /// </summary>
    internal static class GlassThemeMode
    {
        public const string Light = "light";
        public const string Dark = "dark";
        public const string System = "system";

        private static bool _systemHooked;

        public static string Mode { get; private set; } = Light;

        public static bool IsDark { get; private set; }

        /// <summary>Po każdej zmianie wyglądu (motyw, akcent, ustawienie Windows).</summary>
        public static event Action? Changed;

        public static string Normalize(string? mode) => mode?.Trim().ToLowerInvariant() switch
        {
            Light => Light,
            Dark => Dark,
            System => System,
            _ => Light // brak albo nieznana wartość: jasny, jak w nowej instalacji
        };

        public static void Apply(string? mode)
        {
            Mode = Normalize(mode);
            if (!_systemHooked)
            {
                _systemHooked = true;
                // „Jak w Windows”: zmiana trybu aplikacji w Ustawieniach Windows przełącza też program.
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, e) =>
                {
                    if (Mode == System && e.Category is Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.Color)
                        Application.Current?.Dispatcher.BeginInvoke(new Action(Refresh));
                };
            }
            Refresh();
        }

        /// <summary>Ustawienie Windows „Tryb aplikacji”: ciemny, gdy AppsUseLightTheme = 0.</summary>
        public static bool SystemPrefersDark()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch
            {
                return false;
            }
        }

        public static void Refresh()
        {
            Application? app = Application.Current;
            if (app == null)
                return;

            bool dark = Mode == Dark || (Mode == System && SystemPrefersDark());
            IsDark = dark;
            GlassPalette.ApplyMode(dark);

            ResourceDictionary resources = app.Resources;
            foreach (string key in DarkBrushes.Keys)
                resources.Remove(key);
            foreach (string key in DarkShadowKeys)
                resources.Remove(key);
            foreach (string key in DarkAccentKeys)
                resources.Remove(key);

            if (dark)
            {
                foreach (var (key, color) in DarkBrushes)
                    resources[key] = Frozen(new SolidColorBrush(color));
                resources["GlassCardShadow"] = Shadow(22, 6, 0.32);
                resources["GlassPopupShadow"] = Shadow(24, 8, 0.5);
                resources["GlassSoftShadow"] = Shadow(8, 2, 0.35);
                ApplyDarkAccent(resources);
            }

            foreach (Window window in app.Windows)
                GlassChrome.ApplyTitleBar(window);
            Changed?.Invoke();
        }

        private static readonly string[] DarkShadowKeys = { "GlassCardShadow", "GlassPopupShadow", "GlassSoftShadow" };
        private static readonly string[] DarkAccentKeys = { "GlassAccentInkBrush", "GlassAccentSoftBrush", "GlassAccentSolidBrush", "GlassBackdropBrush" };

        // Ciemne szkło: granatowo-czarne tło, karty z kilku procent bieli, jasny tekst,
        // statusy podbite tak, żeby zachować kontrast (makieta „tryb jasny i ciemny”).
        private static readonly Dictionary<string, Color> DarkBrushes = new()
        {
            ["GlassInkBrush"] = Color.FromRgb(0xEC, 0xEE, 0xFA),
            ["GlassInk2Brush"] = Color.FromRgb(0xA6, 0xAB, 0xC8),
            ["GlassLineBrush"] = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF),
            ["GlassSurfaceBrush"] = Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF),
            ["GlassSurface2Brush"] = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
            ["GlassFieldBrush"] = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
            ["GlassStrokeBrush"] = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF),
            ["GlassNeutralSoftBrush"] = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
            ["GlassOkBrush"] = Color.FromRgb(0x4A, 0xDE, 0x80),
            ["GlassOkSoftBrush"] = Color.FromArgb(0x26, 0x4A, 0xDE, 0x80),
            ["GlassWarnBrush"] = Color.FromRgb(0xFB, 0xBF, 0x24),
            ["GlassWarnSoftBrush"] = Color.FromArgb(0x26, 0xFB, 0xBF, 0x24),
            ["GlassBadBrush"] = Color.FromRgb(0xF8, 0x71, 0x71),
            ["GlassBadSoftBrush"] = Color.FromArgb(0x2E, 0xF8, 0x71, 0x71),
            ["GlassRaisedBrush"] = Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF),
            ["GlassHoverBrush"] = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
            ["GlassPopupBrush"] = Color.FromArgb(0xF5, 0x1E, 0x21, 0x38),
            ["GlassPopupStrokeBrush"] = Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF),
            ["GlassControlBrush"] = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
            ["GlassKeyCapBorderBrush"] = Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF),
            ["GlassKeyCapHoverBrush"] = Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF),
            ["GlassThumbBrush"] = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
            ["GlassThumbHoverBrush"] = Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF),
            ["GlassThumbPressedBrush"] = Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF),
            ["GlassNavHoverBrush"] = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
            ["GlassChoiceCardBrush"] = Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF),
            ["GlassChoiceCardHoverBrush"] = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF),
            ["GlassRingBrush"] = Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF),
            ["GlassToggleOffBrush"] = Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF),
        };

        private static void ApplyDarkAccent(ResourceDictionary resources)
        {
            ResourceDictionary? accent = GlassAccents.CurrentDictionary();
            Color main = accent?["GlassAccentColor"] is Color a ? a : Color.FromRgb(0x54, 0x58, 0xE8);
            Color blob1 = accent?["GlassBlob1Color"] is Color b1 ? b1 : Color.FromArgb(0x66, 0x8B, 0x5C, 0xF6);
            Color blob2 = accent?["GlassBlob2Color"] is Color b2 ? b2 : Color.FromArgb(0x4D, 0x54, 0x58, 0xE8);
            Color blob3 = accent?["GlassBlob3Color"] is Color b3 ? b3 : Color.FromArgb(0x47, 0xF4, 0x72, 0xB6);

            // Na ciemnym tle tekst i obramowania akcentu muszą być jaśniejsze, żeby dało się je przeczytać.
            resources["GlassAccentInkBrush"] = Frozen(new SolidColorBrush(Mix(main, Colors.White, 0.5)));
            resources["GlassAccentSolidBrush"] = Frozen(new SolidColorBrush(Mix(main, Colors.White, 0.25)));
            resources["GlassAccentSoftBrush"] = Frozen(new SolidColorBrush(Color.FromArgb(0x38, main.R, main.G, main.B)));
            resources["GlassBackdropBrush"] = DarkBackdrop(blob1, blob2, blob3);
        }

        /// <summary>To samo tło co w GlassAccent*.xaml (trzy plamy akcentu), tylko na granatowo-czarnej bazie.</summary>
        private static Brush DarkBackdrop(Color blob1, Color blob2, Color blob3)
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x0D, 0x0F, 0x1E)), null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
            group.Children.Add(Blob(blob1, new Point(0, 0), 0.8, 0.45));
            group.Children.Add(Blob(blob2, new Point(1, 0.35), 0.72, 0.4));
            group.Children.Add(Blob(blob3, new Point(0.4, 1.05), 0.88, 0.48));
            var brush = new DrawingBrush(group) { Stretch = Stretch.Fill, Viewbox = new Rect(0, 0, 1, 1), ViewboxUnits = BrushMappingMode.Absolute };
            brush.Freeze();
            return brush;

            static GeometryDrawing Blob(Color color, Point center, double radiusX, double radiusY)
            {
                var gradient = new RadialGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B))
                {
                    Center = center,
                    GradientOrigin = center,
                    RadiusX = radiusX,
                    RadiusY = radiusY
                };
                return new GeometryDrawing(gradient, null, new RectangleGeometry(new Rect(0, 0, 1, 1)));
            }
        }

        private static Color Mix(Color color, Color with, double amount) => Color.FromRgb(
            (byte)Math.Round(color.R + (with.R - color.R) * amount),
            (byte)Math.Round(color.G + (with.G - color.G) * amount),
            (byte)Math.Round(color.B + (with.B - color.B) * amount));

        private static DropShadowEffect Shadow(double blur, double depth, double opacity)
        {
            var effect = new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = opacity, Color = Colors.Black };
            effect.Freeze();
            return effect;
        }

        private static SolidColorBrush Frozen(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>Wspólny wygląd okien budowanych w kodzie: tło glass, czcionka i jasny pasek tytułu.</summary>
    internal static class GlassChrome
    {
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;

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

            // Pasek tytułu w kolorze bazy tła: #EEF1F8 w jasnym motywie, #16182C w ciemnym (COLORREF = 0x00BBGGRR).
            int darkMode = GlassThemeMode.IsDark ? 1 : 0;
            int attribute = Environment.OSVersion.Version.Build >= 18985
                ? DwmwaUseImmersiveDarkMode
                : DwmwaUseImmersiveDarkModeBefore20H1;
            _ = DwmSetWindowAttribute(handle, attribute, ref darkMode, sizeof(int));
            int captionColor = GlassThemeMode.IsDark ? 0x002C1816 : 0x00F8F1EE;
            _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            int textColor = GlassThemeMode.IsDark ? 0x00FAEEEC : 0x002B1A16;
            _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
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
        private static readonly TimeSpan SwapDuration = TimeSpan.FromMilliseconds(320);
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, object> SwapTokens = new();

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

        /// <summary>
        /// Podmienia panele leżące w tej samej komórce: wysokość gospodarza przechodzi płynnie ze starej
        /// na nową, a nowy panel się pojawia. Dwa równoległe SetExpanded dałyby tu skok w połowie ruchu.
        /// </summary>
        public static void Swap(FrameworkElement host, FrameworkElement? from, FrameworkElement to, bool animate)
        {
            double startHeight = host.ActualHeight;
            host.BeginAnimation(FrameworkElement.HeightProperty, null);
            host.Height = double.NaN;
            SwapTokens.Remove(host);
            if (from != null)
            {
                from.BeginAnimation(UIElement.OpacityProperty, null);
                from.RenderTransform = Transform.Identity;
                from.Opacity = 1.0;
                from.Visibility = Visibility.Collapsed;
            }

            to.BeginAnimation(UIElement.OpacityProperty, null);
            to.RenderTransform = Transform.Identity;
            to.Opacity = 1.0;
            to.Visibility = Visibility.Visible;
            if (!animate || !host.IsVisible || !SystemParameters.ClientAreaAnimation)
                return;

            host.UpdateLayout();
            double endHeight = host.ActualHeight;
            var shift = new TranslateTransform(0, -8);
            to.RenderTransform = shift;
            to.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = Ease });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 0, SwapDuration) { EasingFunction = Ease });
            if (Math.Abs(endHeight - startHeight) < 1)
                return;

            var height = new DoubleAnimation(startHeight, endHeight, SwapDuration) { EasingFunction = Ease };
            object token = new();
            SwapTokens.Add(host, token);
            height.Completed += (_, _) =>
            {
                // Szybki drugi klik uruchamia nową animację — stara nie może jej zdjąć.
                if (SwapTokens.TryGetValue(host, out object? current) && ReferenceEquals(current, token))
                    host.BeginAnimation(FrameworkElement.HeightProperty, null);
            };
            host.BeginAnimation(FrameworkElement.HeightProperty, height);
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
