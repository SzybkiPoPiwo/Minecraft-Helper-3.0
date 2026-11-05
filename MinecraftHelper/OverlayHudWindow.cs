using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MinecraftHelper
{
    internal enum OverlayCorner
    {
        BottomRight,
        BottomLeft,
        TopRight,
        TopLeft
    }

    internal enum OverlayHudTone
    {
        Active,
        Warning
    }

    internal readonly record struct OverlayHudEntry(string Title, string Body, OverlayHudTone Tone, bool Emphasize = false);

    internal sealed class OverlayHudWindow : Window
    {
        private const int GwlExStyle = -20;
        private const int GwlHwndParent = -8;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpNoOwnerZOrder = 0x0200;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hWnd, int index, int newLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr newLong);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        // Kolory jak w motywie glass: ciemny tekst na jasnej, półprzezroczystej karcie.
        private static readonly Brush LabelBrush = GlassPalette.Muted;
        private static readonly Brush OnBrush = GlassPalette.Ok;
        private static readonly Brush OffBrush = GlassPalette.Bad;
        private static readonly Brush PauseBrush = GlassPalette.Warn;
        private static readonly Brush TimeBrush = GlassPalette.Info;
        private static readonly Brush TileBackground = Freeze(new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush TileBorder = Freeze(new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)));

        private static Brush AccentBrush => GlassPalette.AccentInk;

        private static Brush Freeze(Brush brush)
        {
            brush.Freeze();
            return brush;
        }

        private readonly StackPanel _itemsPanel;
        private string _lastSignature = string.Empty;
        private string _lastStructureSignature = string.Empty;
        private Rect _workArea = SystemParameters.WorkArea;
        private OverlayCorner _corner = OverlayCorner.BottomRight;
        private double _stackOffset = 16;
        private bool _animationsEnabled = true;
        private bool _isStructureAnimating;
        private List<OverlayHudEntry>? _queuedStructureEntries;
        private IntPtr _ownerWindowHandle;
#if MINER_EDITION
        private readonly Services.MinerHudLayout _minerLayout = new();
        private System.Drawing.Rectangle? _minerWorkArea;
        private int _minerMargin = 16;
        public bool IsLayoutSuppressed => _minerLayout.NoSpace;

        public void SetMinerLayout(System.Drawing.Rectangle workArea, OverlayCorner corner, int margin)
        {
            _minerWorkArea = workArea;
            _corner = corner;
            _minerMargin = Math.Max(0, margin);
            if (IsVisible)
                RepositionToAnchor();
        }
#endif

        public OverlayHudWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            Focusable = false;
            SizeToContent = SizeToContent.WidthAndHeight;

            _itemsPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };
            _itemsPanel.RenderTransform = new TranslateTransform();

            Content = _itemsPanel;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyNativeWindowBehavior();
            ApplyOwnerWindowHandle();
            EnsureNativeTopmost();
        }

        public bool IsHudVisible => IsVisible;

        // Okno właściciela (gra) może zniszczyć ten HUD razem ze sobą, np. gdy
        // Minecraft tworzy nowe okno po F11. Zamkniętego okna nie da się pokazać.
        public bool IsClosed { get; private set; }

        protected override void OnClosed(EventArgs e)
        {
#if MINER_EDITION
            _minerLayout.Dispose();
#endif
            IsClosed = true;
            base.OnClosed(e);
        }

        public double CurrentHeight => ActualHeight > 0 ? ActualHeight : RenderSize.Height;

        public void SetOwnerWindowHandle(IntPtr ownerWindowHandle)
        {
#if MINER_EDITION
            // A foreign owner implicitly attaches both UI input queues. The HUD
            // runs on Kopacz's main thread, so owning it by Minecraft also joins
            // the main window's focus/capture state to LWJGL's fake focus.
            // Keep this HUD independent even when it is hidden. Z-order and
            // positioning are already maintained explicitly with NOACTIVATE.
            ownerWindowHandle = IntPtr.Zero;
#endif
            if (_ownerWindowHandle == ownerWindowHandle)
                return;

            _ownerWindowHandle = ownerWindowHandle;
            ApplyOwnerWindowHandle();
            EnsureNativeTopmost();
        }

        public void SetAnimationsEnabled(bool enabled)
        {
            _animationsEnabled = enabled;
        }

        public void SetLayout(Rect workArea, OverlayCorner corner, double stackOffset)
        {
            _workArea = workArea;
            _corner = corner;
            _stackOffset = Math.Max(0, stackOffset);
            if (IsVisible)
                RepositionToAnchor();
        }

        public void UpdateEntries(IReadOnlyList<OverlayHudEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                _lastSignature = string.Empty;
                _lastStructureSignature = string.Empty;
                _isStructureAnimating = false;
                _queuedStructureEntries = null;
                CancelItemsAnimations();
                HideWindow();
                return;
            }

            string signature = BuildSignature(entries);
            string structureSignature = BuildStructureSignature(entries);
            bool signatureChanged = !string.Equals(signature, _lastSignature, StringComparison.Ordinal);
            bool structureChanged = !string.Equals(structureSignature, _lastStructureSignature, StringComparison.Ordinal);
            if (signatureChanged || structureChanged)
            {
                _lastSignature = signature;
                _lastStructureSignature = structureSignature;
                if (IsVisible && structureChanged)
                    AnimateStructureRefresh(entries);
                else if (structureChanged || !TryUpdateTilesInPlace(entries))
                    RebuildTiles(entries);
            }

            ShowWindow();
            if (!_isStructureAnimating)
                RepositionToAnchor();
            EnsureNativeTopmost();
        }

        private void ApplyNativeWindowBehavior()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            int extendedStyle = GetWindowLong(handle, GwlExStyle);
            extendedStyle |= WsExTransparent | WsExToolWindow | WsExNoActivate;
            _ = SetWindowLong(handle, GwlExStyle, extendedStyle);
        }

        private void ApplyOwnerWindowHandle()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            if (IntPtr.Size == 8)
                _ = SetWindowLongPtr64(handle, GwlHwndParent, _ownerWindowHandle);
            else
                _ = SetWindowLong(handle, GwlHwndParent, _ownerWindowHandle.ToInt32());
        }

        private void EnsureNativeTopmost()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            _ = SetWindowPos(
                handle,
                HwndTopmost,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
        }

        // Liczniki (czas, „za 9 s”) zmieniają treść kilka razy na sekundę. Przebudowa całego
        // HUD-a za każdym razem dawała w przezroczystym oknie widoczne migotanie, więc przy
        // tych samych kafelkach podmieniamy tylko treść tych, które się zmieniły.
        private bool TryUpdateTilesInPlace(IReadOnlyList<OverlayHudEntry> entries)
        {
            if (_itemsPanel.Children.Count != entries.Count)
                return false;

            for (int i = 0; i < entries.Count; i++)
            {
                if (_itemsPanel.Children[i] is not Border { Tag: OverlayHudEntry previous, Child: StackPanel { Children.Count: 2 } panel } tile)
                    return false;

                OverlayHudEntry entry = entries[i];
                if (previous == entry)
                    continue;
                if (previous.Title != entry.Title || previous.Tone != entry.Tone || previous.Emphasize != entry.Emphasize)
                    return false;

                if (previous.Body != entry.Body)
                {
                    StackPanel fresh = BuildBodyPanel(entry.Body, entry.Tone == OverlayHudTone.Warning, entry.Emphasize);
                    if (panel.Children[1] is StackPanel current && current.Children.Count == fresh.Children.Count)
                        CopyChangedLines(current, fresh);
                    else
                    {
                        panel.Children.RemoveAt(1);
                        panel.Children.Add(fresh);
                    }
                }
                tile.Tag = entry;
            }

            return true;
        }

        // Ta sama liczba linii: przenosimy tylko nowe fragmenty tekstu do istniejących linii.
        // Podmiana całego bloku na jedną klatkę zmieniała wysokość okna i kafelek mrugał.
        private static void CopyChangedLines(StackPanel current, StackPanel fresh)
        {
            for (int i = 0; i < current.Children.Count; i++)
            {
                if (current.Children[i] is not TextBlock target || fresh.Children[i] is not TextBlock source)
                    continue;
                if (Equals(target.Tag, source.Tag))
                    continue;

                var inlines = new List<Inline>(source.Inlines);
                source.Inlines.Clear();
                target.Inlines.Clear();
                target.Inlines.AddRange(inlines);
                target.Tag = source.Tag;
            }
        }

        private void RebuildTiles(IReadOnlyList<OverlayHudEntry> entries)
        {
            _itemsPanel.Children.Clear();

            for (int i = 0; i < entries.Count; i++)
            {
                OverlayHudEntry entry = entries[i];
                _itemsPanel.Children.Add(BuildTile(entry, i));
            }
        }

        private static Border BuildTile(OverlayHudEntry entry, int index)
        {
            bool warning = entry.Tone == OverlayHudTone.Warning;
            Brush statusBrush = warning ? PauseBrush : OnBrush;

            var title = new TextBlock
            {
                Text = entry.Title,
                FontSize = entry.Emphasize ? 14 : 12,
                FontWeight = FontWeights.Bold,
                Foreground = entry.Emphasize ? AccentBrush : GlassPalette.Ink,
                VerticalAlignment = VerticalAlignment.Center
            };
#if MINER_EDITION
            title.MaxWidth = entry.Emphasize ? 374 : 258;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.ToolTip = entry.Title;
#endif
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = statusBrush,
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            header.Children.Add(title);

            var panel = new StackPanel();
            panel.Children.Add(header);
            panel.Children.Add(BuildBodyPanel(entry.Body, warning, entry.Emphasize));

            var tile = new Border
            {
                Width = entry.Emphasize ? 420 : 300,
                Background = TileBackground,
                BorderBrush = TileBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = entry.Emphasize ? new Thickness(14, 12, 14, 12) : new Thickness(12, 9, 12, 10),
                Margin = new Thickness(6, index == 0 ? 6 : 2, 6, 6),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 14,
                    ShadowDepth = 3,
                    Direction = 270,
                    Opacity = 0.10,
                    Color = Color.FromRgb(0x1E, 0x24, 0x50)
                },
                Child = panel,
                IsHitTestVisible = false,
                Tag = entry
            };
            if (Application.Current?.TryFindResource("GlassFont") is FontFamily font)
                TextElement.SetFontFamily(tile, font);
            return tile;
        }

        private static StackPanel BuildBodyPanel(string body, bool warningTile, bool emphasize)
        {
            var bodyPanel = new StackPanel
            {
                Margin = new Thickness(0, 4, 0, 0)
            };

            string[] lines = body.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                if (emphasize)
                {
                    TextBlock emphasized = BuildEmphasizedLine(line, warningTile);
                    emphasized.Tag = line;
                    bodyPanel.Children.Add(emphasized);
                    continue;
                }

                var text = new TextBlock
                {
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Tag = line
                };

                int separator = line.IndexOf(':');
                if (separator > 0)
                {
                    string label = line.Substring(0, separator + 1);
                    string value = line.Substring(separator + 1).TrimStart();

                    text.Inlines.Add(new Run(label + " ")
                    {
                        Foreground = LabelBrush,
                        FontWeight = FontWeights.SemiBold
                    });
                    AppendStyledValue(text, value, warningTile);
                }
                else
                {
                    AppendStyledValue(text, line, warningTile);
                }

                bodyPanel.Children.Add(text);
            }

            return bodyPanel;
        }

        private static TextBlock BuildEmphasizedLine(string line, bool warningTile)
        {
            var text = new TextBlock
            {
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            };

            int separator = line.IndexOf(':');
            if (separator > 0)
            {
                string label = line.Substring(0, separator + 1);
                string value = line.Substring(separator + 1).TrimStart();
                bool isEntityCountLine = label.StartsWith("Encje", StringComparison.OrdinalIgnoreCase);

                text.Inlines.Add(new Run(label + " ")
                {
                    Foreground = LabelBrush,
                    FontWeight = FontWeights.Bold,
                    FontSize = isEntityCountLine ? 16 : 14
                });
                text.Inlines.Add(new Run(value)
                {
                    Foreground = isEntityCountLine ? ResolveEntityCountBrush(value, warningTile) : ResolveValueBrush(value, warningTile),
                    FontWeight = FontWeights.ExtraBold,
                    FontSize = isEntityCountLine ? 30 : 18
                });
                return text;
            }

            text.Inlines.Add(new Run(line)
            {
                Foreground = ResolveValueBrush(line, warningTile),
                FontWeight = FontWeights.Bold,
                FontSize = 18
            });
            return text;
        }

        private static void AppendStyledValue(TextBlock block, string value, bool warningTile)
        {
            string[] parts = value.Split(new[] { " | " }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                block.Inlines.Add(new Run(part)
                {
                    Foreground = ResolveValueBrush(part, warningTile),
                    FontWeight = FontWeights.SemiBold
                });

                if (i < parts.Length - 1)
                {
                    block.Inlines.Add(new Run(" | ")
                    {
                        Foreground = LabelBrush
                    });
                }
            }
        }

        private static Brush ResolveValueBrush(string valuePart, bool warningTile)
        {
            string lower = valuePart.ToLowerInvariant();

            if (lower.Contains("pauza") || lower.Contains("wstrzym"))
                return PauseBrush;

            if (lower.Contains("off") || lower.Contains("wyłącz") || lower.Contains("brak") || lower == "stop")
                return OffBrush;

            if (lower.Contains("on") || lower.Contains("kopanie") || lower.Contains("aktywn") || lower.Contains("teraz") || lower.Contains("wznawianie") || lower.Contains("wykonane"))
                return OnBrush;

            if (lower.Contains("za ") || lower.EndsWith("ms", StringComparison.Ordinal) || lower.EndsWith("s", StringComparison.Ordinal))
                return TimeBrush;

            if (warningTile)
                return PauseBrush;

            return AccentBrush;
        }

        private static Brush ResolveEntityCountBrush(string valuePart, bool warningTile)
        {
            string lower = valuePart.ToLowerInvariant();
            if (lower.Contains("brak", StringComparison.Ordinal))
                return warningTile ? PauseBrush : OffBrush;

            int index = 0;
            while (index < valuePart.Length && !char.IsDigit(valuePart[index]))
                index++;

            int start = index;
            while (index < valuePart.Length && char.IsDigit(valuePart[index]))
                index++;

            if (start < index && int.TryParse(valuePart.Substring(start, index - start), out int parsedPlayers))
                return parsedPlayers > 0 ? OnBrush : PauseBrush;

            return warningTile ? PauseBrush : AccentBrush;
        }

        private static string BuildSignature(IReadOnlyList<OverlayHudEntry> entries)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                OverlayHudEntry entry = entries[i];
                sb.Append(entry.Title);
                sb.Append('|');
                sb.Append(entry.Body);
                sb.Append('|');
                sb.Append((int)entry.Tone);
                sb.Append('\n');
            }

            return sb.ToString();
        }

        private static string BuildStructureSignature(IReadOnlyList<OverlayHudEntry> entries)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                OverlayHudEntry entry = entries[i];
                sb.Append(entry.Title);
                sb.Append('|');
                sb.Append((int)entry.Tone);
                sb.Append('|');
                sb.Append(entry.Emphasize ? '1' : '0');
                sb.Append('\n');
            }

            return sb.ToString();
        }

        private void AnimateStructureRefresh(IReadOnlyList<OverlayHudEntry> entries)
        {
            if (!_animationsEnabled)
            {
                RebuildTiles(entries);
                RepositionToAnchor();
                return;
            }

            if (_isStructureAnimating)
            {
                _queuedStructureEntries = new List<OverlayHudEntry>(entries);
                return;
            }

            _isStructureAnimating = true;
            _queuedStructureEntries = null;
            CancelItemsAnimations();

            TranslateTransform translate = EnsureItemsTranslateTransform();
            var hideEase = new CubicEase { EasingMode = EasingMode.EaseIn };
            var showEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            double hideY = _corner is OverlayCorner.TopLeft or OverlayCorner.TopRight ? -6 : 6;
            var hideOpacity = new DoubleAnimation
            {
                From = _itemsPanel.Opacity <= 0 ? 1 : _itemsPanel.Opacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(90),
                EasingFunction = hideEase
            };

            hideOpacity.Completed += (_, __) =>
            {
                if (!IsVisible)
                {
                    _isStructureAnimating = false;
                    _queuedStructureEntries = null;
                    return;
                }

                RebuildTiles(entries);
                RepositionToAnchor();

                double showFromY = _corner is OverlayCorner.TopLeft or OverlayCorner.TopRight ? -12 : 12;
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = showFromY;

                _itemsPanel.BeginAnimation(UIElement.OpacityProperty, null);
                _itemsPanel.Opacity = 0;

                var showOpacity = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(180),
                    EasingFunction = showEase
                };
                showOpacity.Completed += (_, __2) =>
                {
                    _isStructureAnimating = false;
                    if (_queuedStructureEntries is { Count: > 0 } queued)
                    {
                        _queuedStructureEntries = null;
                        AnimateStructureRefresh(queued);
                    }
                };
                _itemsPanel.BeginAnimation(UIElement.OpacityProperty, showOpacity);

                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
                {
                    From = showFromY,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(210),
                    EasingFunction = showEase
                });
            };

            _itemsPanel.BeginAnimation(UIElement.OpacityProperty, hideOpacity);

            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
            {
                From = 0,
                To = hideY,
                Duration = TimeSpan.FromMilliseconds(90),
                EasingFunction = hideEase
            });
        }

        private void ShowWindow()
        {
            BeginAnimation(OpacityProperty, null);

            if (!IsVisible)
            {
                if (_animationsEnabled)
                {
                    Opacity = 0;
                    Show();
                    _itemsPanel.BeginAnimation(UIElement.OpacityProperty, null);
                    _itemsPanel.Opacity = 1;
                    BeginAnimation(OpacityProperty, new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(140),
                    });
                }
                else
                {
                    Opacity = 1;
                    Show();
                }
            }
            else
            {
                Opacity = 1;
            }
        }

        private void HideWindow()
        {
#if MINER_EDITION
            _minerLayout.Invalidate();
#endif
            if (!IsVisible)
                return;

            CancelItemsAnimations();
            _isStructureAnimating = false;
            _queuedStructureEntries = null;
            BeginAnimation(OpacityProperty, null);
            if (!_animationsEnabled)
            {
                Opacity = 1;
                Hide();
                return;
            }

            var fade = new DoubleAnimation
            {
                From = Opacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(180),
            };
            fade.Completed += (_, __) =>
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
                Hide();
            };
            BeginAnimation(OpacityProperty, fade);
        }

        private TranslateTransform EnsureItemsTranslateTransform()
        {
            if (_itemsPanel.RenderTransform is TranslateTransform translate)
                return translate;

            translate = new TranslateTransform();
            _itemsPanel.RenderTransform = translate;
            return translate;
        }

        private void CancelItemsAnimations()
        {
            _itemsPanel.BeginAnimation(UIElement.OpacityProperty, null);
            TranslateTransform translate = EnsureItemsTranslateTransform();
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            _itemsPanel.Opacity = 1;
            translate.Y = 0;
        }

        private void RepositionToAnchor()
        {
            UpdateLayout();
#if MINER_EDITION
            if (_minerWorkArea is { } area)
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (!_minerLayout.TryPosition(handle, area,
                    _corner is OverlayCorner.BottomLeft or OverlayCorner.TopLeft,
                    _corner is OverlayCorner.TopLeft or OverlayCorner.TopRight, _minerMargin, 8))
                {
                    // A fade can be cancelled by the next status tick. Hide now
                    // so a full monitor never leaves a HUD on top of its peers.
                    CancelItemsAnimations();
                    _isStructureAnimating = false;
                    _queuedStructureEntries = null;
                    BeginAnimation(OpacityProperty, null);
                    _minerLayout.Invalidate();
                    Hide();
                }
                return;
            }
#endif

            double left = _corner is OverlayCorner.BottomLeft or OverlayCorner.TopLeft
                ? _workArea.Left + 16
                : _workArea.Right - ActualWidth - 16;

            double top = _corner is OverlayCorner.TopLeft or OverlayCorner.TopRight
                ? _workArea.Top + _stackOffset
                : _workArea.Bottom - ActualHeight - _stackOffset;

            Left = left;
            Top = top;
        }
    }
}
