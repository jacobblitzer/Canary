using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Canary.UI.Avalonia.Controls;

// Avalonia port of Canary.UI.Annotation.AnnotationCanvas (WPF). Same
// four tool modes (Pointer / Rectangle / Freehand / Text), same JSON
// serialization shape (annotations.json v1), same coordinate system
// (pixel space of the source PNG). RenderTargetBitmap renders the
// composite at source resolution; PNG bytes round-trip-identical with
// the WPF version on the same input.
public sealed class AnnotationCanvas : UserControl
{
    public enum ToolMode { Pointer, Rectangle, Freehand, Text }

    private readonly Canvas _canvas;
    private readonly Image _background;
    private readonly List<Shape> _shapes = new();
    private readonly List<TextRecord> _textRecords = new();
    // Each push is the inverse of a single user action (Rectangle add /
    // Freehand add / Text add). Undo pops + invokes. Cleared on Clear().
    private readonly Stack<Action> _undoStack = new();

    private ToolMode _tool = ToolMode.Rectangle;
    private IBrush _strokeBrush = Brushes.Red;
    private double _strokeThickness = 3;

    private Shape? _activeShape;
    private Point _dragStart;
    private bool _drawing;

    public int SourceWidth { get; private set; }
    public int SourceHeight { get; private set; }
    public int UndoCount => _undoStack.Count;
    public int ShapeCount => _shapes.Count;

    public event Action? StateChanged;

    public Func<Task<string?>>? TextPromptAsync { get; set; }

    public AnnotationCanvas()
    {
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));

        _canvas = new Canvas
        {
            Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
            ClipToBounds = true,
        };
        _background = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Canvas.SetLeft(_background, 0);
        Canvas.SetTop(_background, 0);
        _canvas.Children.Add(_background);

        _canvas.PointerPressed += OnPointerPressed;
        _canvas.PointerMoved += OnPointerMoved;
        _canvas.PointerReleased += OnPointerReleased;

        Content = _canvas;
    }

    public ToolMode Tool { get => _tool; set => _tool = value; }

    public IBrush StrokeBrush
    {
        get => _strokeBrush;
        set => _strokeBrush = value ?? Brushes.Red;
    }

    public void LoadImage(string sourcePath)
    {
        using var fs = File.OpenRead(sourcePath);
        var bmp = new Bitmap(fs);

        _background.Source = bmp;
        _background.Width = bmp.PixelSize.Width;
        _background.Height = bmp.PixelSize.Height;

        SourceWidth = bmp.PixelSize.Width;
        SourceHeight = bmp.PixelSize.Height;

        _canvas.Width = bmp.PixelSize.Width;
        _canvas.Height = bmp.PixelSize.Height;
    }

    public void Clear()
    {
        // Snapshot for undo so Ctrl+Z restores the cleared shapes (and the
        // associated TextBlock siblings — text shapes live on the canvas
        // as a Rectangle+TextBlock pair, only the Rectangle is in
        // _shapes; pair the TextBlock via the same TextRecord lookup).
        var snapshot = new List<(Shape Shape, Control? Sibling, TextRecord? Text)>();
        foreach (var s in _shapes)
        {
            Control? sibling = null;
            var tr = _textRecords.FirstOrDefault(t => ReferenceEquals(t.Shape, s));
            if (tr != null)
            {
                // Find the TextBlock at the same position the canvas added.
                sibling = _canvas.Children.OfType<TextBlock>()
                    .FirstOrDefault(tb => ReferenceEquals(tb.Tag, s));
            }
            snapshot.Add((s, sibling, tr));
            _canvas.Children.Remove(s);
            if (sibling != null) _canvas.Children.Remove(sibling);
        }
        var clearedShapes = _shapes.ToList();
        var clearedText = _textRecords.ToList();
        _shapes.Clear();
        _textRecords.Clear();

        _undoStack.Push(() =>
        {
            foreach (var entry in snapshot)
            {
                _canvas.Children.Add(entry.Shape);
                if (entry.Sibling != null) _canvas.Children.Add(entry.Sibling);
            }
            _shapes.AddRange(clearedShapes);
            _textRecords.AddRange(clearedText);
            StateChanged?.Invoke();
        });
        StateChanged?.Invoke();
    }

    public bool Undo()
    {
        if (_undoStack.Count == 0) return false;
        var undo = _undoStack.Pop();
        undo();
        StateChanged?.Invoke();
        return true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_background.Source == null) return;
        if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;
        _dragStart = e.GetPosition(_canvas);
        switch (_tool)
        {
            case ToolMode.Rectangle:
                var rect = new Rectangle
                {
                    Stroke = _strokeBrush,
                    StrokeThickness = _strokeThickness,
                    Fill = Brushes.Transparent,
                };
                Canvas.SetLeft(rect, _dragStart.X);
                Canvas.SetTop(rect, _dragStart.Y);
                _canvas.Children.Add(rect);
                _shapes.Add(rect);
                _activeShape = rect;
                _drawing = true;
                PushSimpleUndo(rect);
                break;
            case ToolMode.Freehand:
                var poly = new Polyline
                {
                    Stroke = _strokeBrush,
                    StrokeThickness = _strokeThickness,
                    StrokeLineCap = PenLineCap.Round,
                    StrokeJoin = PenLineJoin.Round,
                    Points = new Points { _dragStart },
                };
                _canvas.Children.Add(poly);
                _shapes.Add(poly);
                _activeShape = poly;
                _drawing = true;
                PushSimpleUndo(poly);
                break;
            case ToolMode.Text:
                _ = AddTextAtAsync(_dragStart);
                break;
            case ToolMode.Pointer:
                _drawing = false;
                break;
        }
    }

    // Push an undo that removes a single non-text shape (Rectangle /
    // Polyline) from both the canvas + _shapes. Used for the Rectangle
    // + Freehand tools. Text gets a paired undo (see AddTextAtAsync) so
    // both the background Rectangle and the TextBlock label go together.
    private void PushSimpleUndo(Shape shape)
    {
        _undoStack.Push(() =>
        {
            _canvas.Children.Remove(shape);
            _shapes.Remove(shape);
            StateChanged?.Invoke();
        });
        StateChanged?.Invoke();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_drawing || _activeShape == null) return;
        var p = e.GetPosition(_canvas);
        switch (_activeShape)
        {
            case Rectangle r:
                var x = Math.Min(_dragStart.X, p.X);
                var y = Math.Min(_dragStart.Y, p.Y);
                r.Width = Math.Abs(p.X - _dragStart.X);
                r.Height = Math.Abs(p.Y - _dragStart.Y);
                Canvas.SetLeft(r, x);
                Canvas.SetTop(r, y);
                break;
            case Polyline poly:
                var pts = new Points(poly.Points) { p };
                poly.Points = pts;
                break;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _drawing = false;
        _activeShape = null;
    }

    private async Task AddTextAtAsync(Point p)
    {
        if (TextPromptAsync == null) return;
        var input = await TextPromptAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(input)) return;

        var tb = new TextBlock
        {
            Text = input,
            Foreground = Brushes.White,
            Padding = new Thickness(4, 2, 4, 2),
            FontSize = 14,
            FontFamily = new FontFamily("Segoe UI"),
        };
        tb.Measure(Size.Infinity);
        var desired = tb.DesiredSize;

        var bg = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            Stroke = _strokeBrush,
            StrokeThickness = 1,
            Width = desired.Width,
            Height = desired.Height,
        };
        Canvas.SetLeft(bg, p.X);
        Canvas.SetTop(bg, p.Y);
        Canvas.SetLeft(tb, p.X);
        Canvas.SetTop(tb, p.Y);
        // Tag the TextBlock with its owning Rectangle so Clear() can pair
        // them up when building the snapshot undo entry.
        tb.Tag = bg;
        _canvas.Children.Add(bg);
        _canvas.Children.Add(tb);
        _shapes.Add(bg);
        var rec = new TextRecord { Shape = bg, Text = input, X = p.X, Y = p.Y, Color = ColorToHex(_strokeBrush), FontSize = 14 };
        _textRecords.Add(rec);

        _undoStack.Push(() =>
        {
            _canvas.Children.Remove(bg);
            _canvas.Children.Remove(tb);
            _shapes.Remove(bg);
            _textRecords.Remove(rec);
            StateChanged?.Invoke();
        });
        StateChanged?.Invoke();
    }

    public byte[] RenderAnnotatedPng()
    {
        if (_background.Source == null) return Array.Empty<byte>();
        if (SourceWidth == 0 || SourceHeight == 0) return Array.Empty<byte>();

        _canvas.Measure(new Size(SourceWidth, SourceHeight));
        _canvas.Arrange(new Rect(0, 0, SourceWidth, SourceHeight));
        _canvas.UpdateLayout();

        var pixelSize = new PixelSize(SourceWidth, SourceHeight);
        var dpi = new Vector(96, 96);
        using var rtb = new RenderTargetBitmap(pixelSize, dpi);
        rtb.Render(_canvas);

        using var ms = new MemoryStream();
        rtb.Save(ms);
        return ms.ToArray();
    }

    public string SerializeAnnotationsJson(string sourceImageFile)
    {
        var shapes = new List<object>();
        foreach (var s in _shapes)
        {
            switch (s)
            {
                case Rectangle r when _textRecords.FirstOrDefault(t => ReferenceEquals(t.Shape, r)) is { } tr:
                    shapes.Add(new
                    {
                        id = $"t{_shapes.IndexOf(s)}",
                        type = "text",
                        x = tr.X,
                        y = tr.Y,
                        text = tr.Text,
                        color = tr.Color,
                        fontSize = tr.FontSize,
                    });
                    break;
                case Rectangle r:
                    shapes.Add(new
                    {
                        id = $"r{_shapes.IndexOf(s)}",
                        type = "rect",
                        x = Canvas.GetLeft(r),
                        y = Canvas.GetTop(r),
                        w = r.Width,
                        h = r.Height,
                        stroke = ColorToHex(r.Stroke),
                        strokeWidth = r.StrokeThickness,
                    });
                    break;
                case Polyline poly:
                    shapes.Add(new
                    {
                        id = $"p{_shapes.IndexOf(s)}",
                        type = "freehand",
                        points = poly.Points.Select(pt => new[] { pt.X, pt.Y }).ToArray(),
                        stroke = ColorToHex(poly.Stroke),
                        strokeWidth = poly.StrokeThickness,
                    });
                    break;
            }
        }
        var doc = new
        {
            version = 1,
            sourceImage = sourceImageFile,
            imageWidth = SourceWidth,
            imageHeight = SourceHeight,
            shapes = shapes.ToArray(),
        };
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ColorToHex(IBrush? brush)
    {
        if (brush is ISolidColorBrush scb)
        {
            var c = scb.Color;
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        return "#FFFFFF";
    }

    private sealed class TextRecord
    {
        public required Shape Shape { get; init; }
        public required string Text { get; init; }
        public required double X { get; init; }
        public required double Y { get; init; }
        public required string Color { get; init; }
        public required double FontSize { get; init; }
    }
}
