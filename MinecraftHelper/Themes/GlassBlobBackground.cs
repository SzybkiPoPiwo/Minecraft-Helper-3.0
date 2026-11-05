using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MinecraftHelper
{
    /// <summary>
    /// Tło glass: trzy miękkie plamy koloru akcentu pod szkłem. Gdy animacja jest włączona,
    /// plamy powoli dryfują (maks. 15 FPS) i zatrzymują się, gdy okno jest ukryte.
    /// </summary>
    public sealed class GlassBlobBackground : Grid
    {
        public static readonly DependencyProperty IsAnimationEnabledProperty = DependencyProperty.Register(
            nameof(IsAnimationEnabled),
            typeof(bool),
            typeof(GlassBlobBackground),
            new PropertyMetadata(true, (d, _) => ((GlassBlobBackground)d).UpdateAnimation()));

        private readonly (Ellipse Shape, TranslateTransform Shift, double Dx, double Dy, double Seconds)[] _blobs;
        private bool _animating;

        public GlassBlobBackground()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            _blobs = new[]
            {
                CreateBlob("GlassBlob1Color", HorizontalAlignment.Left, VerticalAlignment.Top, -0.18, -0.25, 140, 90, 23),
                CreateBlob("GlassBlob2Color", HorizontalAlignment.Right, VerticalAlignment.Top, 0.2, 0.0, -120, 110, 29),
                CreateBlob("GlassBlob3Color", HorizontalAlignment.Center, VerticalAlignment.Bottom, 0.05, 0.3, 160, -80, 34)
            };
            foreach (var blob in _blobs)
                Children.Add(blob.Shape);

            SizeChanged += (_, _) => LayoutBlobs();
            Loaded += (_, _) => { RefreshColors(); UpdateAnimation(); };
            IsVisibleChanged += (_, _) => UpdateAnimation();
        }

        public bool IsAnimationEnabled
        {
            get => (bool)GetValue(IsAnimationEnabledProperty);
            set => SetValue(IsAnimationEnabledProperty, value);
        }

        /// <summary>Ponownie odczytuje kolory plam po zmianie akcentu.</summary>
        public void RefreshColors()
        {
            string[] keys = { "GlassBlob1Color", "GlassBlob2Color", "GlassBlob3Color" };
            for (int index = 0; index < _blobs.Length; index++)
            {
                if (TryFindResource(keys[index]) is not Color color)
                    continue;
                _blobs[index].Shape.Fill = new RadialGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(color, 0),
                        new GradientStop(Color.FromArgb((byte)(color.A / 2), color.R, color.G, color.B), 0.45),
                        new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1)
                    });
            }
        }

        private (Ellipse, TranslateTransform, double, double, double) CreateBlob(
            string colorKey,
            HorizontalAlignment horizontal,
            VerticalAlignment vertical,
            double offsetX,
            double offsetY,
            double driftX,
            double driftY,
            double seconds)
        {
            var shift = new TranslateTransform();
            var shape = new Ellipse
            {
                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,
                RenderTransform = shift,
                Tag = new Point(offsetX, offsetY)
            };
            return (shape, shift, driftX, driftY, seconds);
        }

        private void LayoutBlobs()
        {
            double width = Math.Max(1, ActualWidth);
            double height = Math.Max(1, ActualHeight);
            double size = Math.Max(width, height) * 0.85;
            foreach (var blob in _blobs)
            {
                var offset = (Point)blob.Shape.Tag;
                blob.Shape.Width = size;
                blob.Shape.Height = size * 0.78;
                blob.Shape.Margin = new Thickness(offset.X * width, offset.Y * height, -offset.X * width, -offset.Y * height);
            }
        }

        private void UpdateAnimation()
        {
            bool shouldAnimate = IsAnimationEnabled && IsVisible && IsLoaded && SystemParameters.ClientAreaAnimation;
            if (shouldAnimate == _animating)
                return;

            _animating = shouldAnimate;
            foreach (var blob in _blobs)
            {
                if (!shouldAnimate)
                {
                    blob.Shift.BeginAnimation(TranslateTransform.XProperty, null);
                    blob.Shift.BeginAnimation(TranslateTransform.YProperty, null);
                    continue;
                }

                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                var x = new DoubleAnimation(0, blob.Dx, TimeSpan.FromSeconds(blob.Seconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = ease
                };
                var y = new DoubleAnimation(0, blob.Dy, TimeSpan.FromSeconds(blob.Seconds * 1.3))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = ease
                };
                Timeline.SetDesiredFrameRate(x, 15);
                Timeline.SetDesiredFrameRate(y, 15);
                blob.Shift.BeginAnimation(TranslateTransform.XProperty, x);
                blob.Shift.BeginAnimation(TranslateTransform.YProperty, y);
            }
        }
    }
}
