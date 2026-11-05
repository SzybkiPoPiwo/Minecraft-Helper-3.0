using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MinecraftHelper;

/// <summary>
/// Suwak zakresu z dwoma uchwytami (np. CPS od–do). Przeciąganie myszą lub strzałki
/// zmieniają wartości całkowite; <see cref="RangeChanged"/> informuje o zmianie przez użytkownika.
/// </summary>
public sealed class GlassRangeSlider : Control
{
    public static readonly DependencyProperty MinimumProperty = Register(nameof(Minimum), 1.0);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), 20.0);
    public static readonly DependencyProperty LowerValueProperty = Register(nameof(LowerValue), 10.0);
    public static readonly DependencyProperty UpperValueProperty = Register(nameof(UpperValue), 12.0);

    private const double ThumbSize = 18;
    private const double TrackHeight = 8;
    private readonly Canvas _canvas = new();
    private readonly Border _track = new() { Height = TrackHeight, CornerRadius = new CornerRadius(4) };
    private readonly Border _range = new() { Height = TrackHeight, CornerRadius = new CornerRadius(4) };
    private readonly Border _lowerThumb = CreateThumb();
    private readonly Border _upperThumb = CreateThumb();
    private readonly TextBlock _lowerLabel = CreateLabel();
    private readonly TextBlock _upperLabel = CreateLabel();
    private bool _draggingUpper;

    public GlassRangeSlider()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        Height = 34;
        FocusVisualStyle = null;
        Background = Brushes.Transparent;
        _track.SetResourceReference(Border.BackgroundProperty, "GlassNeutralSoftBrush");
        _range.SetResourceReference(Border.BackgroundProperty, "GlassAccentBrush");
        _canvas.Children.Add(_track);
        _canvas.Children.Add(_range);
        _canvas.Children.Add(_lowerLabel);
        _canvas.Children.Add(_upperLabel);
        _canvas.Children.Add(_lowerThumb);
        _canvas.Children.Add(_upperThumb);
        AddVisualChild(_canvas);
        SizeChanged += (_, _) => LayoutParts();
        ToolTip = "Przeciągnij uchwyty, aby ustawić zakres. Strzałki zmieniają wybrany uchwyt o 1.";
    }

    public event EventHandler? RangeChanged;

    public bool IsDragging { get; private set; }

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public double LowerValue { get => (double)GetValue(LowerValueProperty); set => SetValue(LowerValueProperty, value); }

    public double UpperValue { get => (double)GetValue(UpperValueProperty); set => SetValue(UpperValueProperty, value); }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _canvas;

    protected override Size MeasureOverride(Size constraint)
    {
        _canvas.Measure(constraint);
        return new Size(double.IsInfinity(constraint.Width) ? 200 : constraint.Width, Height);
    }

    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        _canvas.Arrange(new Rect(arrangeBounds));
        LayoutParts();
        return arrangeBounds;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Przezroczyste tło łapie kliknięcia na całej wysokości kontrolki.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        double value = ValueAt(e.GetPosition(this).X);
        // Bliższy uchwyt; przy równych wartościach kierunek kliknięcia wybiera górny lub dolny.
        double toLower = Math.Abs(value - LowerValue);
        double toUpper = Math.Abs(value - UpperValue);
        _draggingUpper = toUpper < toLower || (toUpper == toLower && value >= UpperValue);
        IsDragging = true;
        CaptureMouse();
        Apply(value);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsDragging && e.LeftButton == MouseButtonState.Pressed)
            Apply(ValueAt(e.GetPosition(this).X));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsDragging)
            return;
        IsDragging = false;
        ReleaseMouseCapture();
        LayoutParts();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        IsDragging = false;
        LayoutParts();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int step = e.Key switch { Key.Left or Key.Down => -1, Key.Right or Key.Up => 1, _ => 0 };
        if (e.Key is Key.Tab)
            return;
        if (e.Key == Key.Home || e.Key == Key.End)
        {
            _draggingUpper = e.Key == Key.End;
            Apply(e.Key == Key.End ? Maximum : Minimum);
            e.Handled = true;
            return;
        }
        if (step == 0)
            return;
        Apply((_draggingUpper ? UpperValue : LowerValue) + step);
        e.Handled = true;
    }

    private void Apply(double value)
    {
        value = Math.Clamp(Math.Round(value), Minimum, Maximum);
        double lower = LowerValue;
        double upper = UpperValue;
        if (_draggingUpper)
            upper = Math.Max(value, lower);
        else
            lower = Math.Min(value, upper);

        if (lower == LowerValue && upper == UpperValue)
            return;
        LowerValue = lower;
        UpperValue = upper;
        RangeChanged?.Invoke(this, EventArgs.Empty);
    }

    private double ValueAt(double x)
    {
        double usable = Math.Max(1, ActualWidth - ThumbSize);
        double ratio = Math.Clamp((x - ThumbSize / 2) / usable, 0, 1);
        return Minimum + ratio * (Maximum - Minimum);
    }

    private double PositionOf(double value)
    {
        double span = Math.Max(1e-6, Maximum - Minimum);
        double ratio = Math.Clamp((value - Minimum) / span, 0, 1);
        return ratio * Math.Max(1, ActualWidth - ThumbSize);
    }

    private void LayoutParts()
    {
        if (ActualWidth <= 0)
            return;

        double trackTop = Height - ThumbSize / 2 - TrackHeight / 2 - 2;
        double thumbTop = trackTop + TrackHeight / 2 - ThumbSize / 2;
        double lowerX = PositionOf(LowerValue);
        double upperX = PositionOf(UpperValue);

        _track.Width = Math.Max(0, ActualWidth - ThumbSize);
        Canvas.SetLeft(_track, ThumbSize / 2);
        Canvas.SetTop(_track, trackTop);
        _range.Width = Math.Max(TrackHeight, upperX - lowerX);
        Canvas.SetLeft(_range, ThumbSize / 2 + lowerX);
        Canvas.SetTop(_range, trackTop);
        Canvas.SetLeft(_lowerThumb, lowerX);
        Canvas.SetTop(_lowerThumb, thumbTop);
        Canvas.SetLeft(_upperThumb, upperX);
        Canvas.SetTop(_upperThumb, thumbTop);

        // Etykiety wartości nad uchwytami — tylko podczas przeciągania albo przy fokusie.
        bool showLabels = IsDragging || IsKeyboardFocused;
        _lowerLabel.Text = LowerValue.ToString("0", CultureInfo.InvariantCulture);
        _upperLabel.Text = UpperValue.ToString("0", CultureInfo.InvariantCulture);
        _lowerLabel.Visibility = showLabels ? Visibility.Visible : Visibility.Hidden;
        _upperLabel.Visibility = showLabels && UpperValue != LowerValue ? Visibility.Visible : Visibility.Hidden;
        Canvas.SetLeft(_lowerLabel, lowerX + ThumbSize / 2 - 10);
        Canvas.SetLeft(_upperLabel, upperX + ThumbSize / 2 - 10);
        Canvas.SetTop(_lowerLabel, 0);
        Canvas.SetTop(_upperLabel, 0);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        LayoutParts();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        LayoutParts();
    }

    private static Border CreateThumb()
    {
        var thumb = new Border
        {
            Width = ThumbSize,
            Height = ThumbSize,
            CornerRadius = new CornerRadius(ThumbSize / 2),
            Background = Brushes.White,
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false
        };
        thumb.SetResourceReference(Border.BorderBrushProperty, "GlassAccentSolidBrush");
        thumb.SetResourceReference(UIElement.EffectProperty, "GlassSoftShadow");
        return thumb;
    }

    private static TextBlock CreateLabel()
    {
        var label = new TextBlock
        {
            Width = 20,
            TextAlignment = TextAlignment.Center,
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Visibility = Visibility.Hidden,
            IsHitTestVisible = false
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "GlassAccentInkBrush");
        return label;
    }

    private static DependencyProperty Register(string name, double defaultValue) =>
        DependencyProperty.Register(
            name,
            typeof(double),
            typeof(GlassRangeSlider),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsArrange, (d, _) => ((GlassRangeSlider)d).LayoutParts()));
}
