using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

// Disambiguate against the implicit-usings WinForms / Drawing imports
// that Canary.UI also pulls in. Aliases scoped to this file.
using UserControl = System.Windows.Controls.UserControl;
using Image = System.Windows.Controls.Image;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Rectangle = System.Windows.Shapes.Rectangle;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Canary.UI.Annotation;

// Phase 5 / design §C5 — custom WPF Canvas-based annotation surface.
// Hosted in WinForms via ElementHost (AnnotatedImageForm). Tool modes:
// Pointer / Rectangle / Freehand / Text. Color: red / yellow / green.
//
// Design pick (§C5 open question): custom Canvas over WPF InkCanvas. The
// four tool modes need uniform hit-testing semantics; InkCanvas is built
// around stylus stroke smoothing, rectangle / text labels aren't first
// class. The cost (~1-2 days more than InkCanvas + extension per the
// design note) buys cleanly-saveable JSON vector data.
public sealed class AnnotationCanvas : UserControl
{
    public enum ToolMode { Pointer, Rectangle, Freehand, Text }

    private readonly Canvas _canvas;
    private readonly Image _background;
    private readonly List<Shape> _shapes = new();

    private ToolMode _tool = ToolMode.Rectangle;
    private Brush _strokeBrush = Brushes.Red;
    private double _strokeThickness = 3;

    private Shape? _activeShape;
    private Point _dragStart;
    private bool _drawing;

    // Source image dimensions in pixels — needed to render annotated.png
    // at original resolution regardless of the on-screen canvas size.
    public int SourceWidth { get; private set; }
    public int SourceHeight { get; private set; }

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

        _canvas.MouseLeftButtonDown += OnMouseDown;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseLeftButtonUp += OnMouseUp;

        Content = _canvas;
    }

    public ToolMode Tool { get => _tool; set => _tool = value; }
    public Brush StrokeBrush
    {
        get => _strokeBrush;
        set => _strokeBrush = value ?? Brushes.Red;
    }

    public void LoadImage(string sourcePath)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(sourcePath, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();

        _background.Source = bmp;
        _background.Width = bmp.PixelWidth;
        _background.Height = bmp.PixelHeight;

        SourceWidth = bmp.PixelWidth;
        SourceHeight = bmp.PixelHeight;

        _canvas.Width = bmp.PixelWidth;
        _canvas.Height = bmp.PixelHeight;
    }

    public void Clear()
    {
        foreach (var s in _shapes) _canvas.Children.Remove(s);
        _shapes.Clear();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_background.Source == null) return;
        _dragStart = e.GetPosition(_canvas);
        switch (_tool)
        {
            case ToolMode.Rectangle:
                _activeShape = new Rectangle
                {
                    Stroke = _strokeBrush,
                    StrokeThickness = _strokeThickness,
                    Fill = Brushes.Transparent,
                };
                Canvas.SetLeft(_activeShape, _dragStart.X);
                Canvas.SetTop(_activeShape, _dragStart.Y);
                _canvas.Children.Add(_activeShape);
                _shapes.Add(_activeShape);
                _drawing = true;
                break;
            case ToolMode.Freehand:
                var poly = new Polyline
                {
                    Stroke = _strokeBrush,
                    StrokeThickness = _strokeThickness,
                    StrokeLineJoin = PenLineJoin.Round,
                };
                poly.Points.Add(_dragStart);
                _canvas.Children.Add(poly);
                _shapes.Add(poly);
                _activeShape = poly;
                _drawing = true;
                break;
            case ToolMode.Text:
                AddTextAt(_dragStart);
                break;
            case ToolMode.Pointer:
                _drawing = false;
                break;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
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
                poly.Points.Add(p);
                break;
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _drawing = false;
        _activeShape = null;
    }

    private void AddTextAt(Point p)
    {
        // Approximate the design's TextBox as a TextBlock with a backing
        // Rectangle for legibility on dark backgrounds. Operator types in
        // a dialog (UI prompts via the host form) — keeps the canvas
        // focused on coordinates rather than text editing.
        var input = Microsoft.VisualBasic.Interaction.InputBox(
            "Annotation text:", "Add label", "", -1, -1);
        if (string.IsNullOrWhiteSpace(input)) return;

        var bg = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            Stroke = _strokeBrush,
            StrokeThickness = 1,
        };
        var tb = new TextBlock
        {
            Text = input,
            Foreground = Brushes.White,
            Padding = new Thickness(4, 2, 4, 2),
            FontSize = 14,
            FontFamily = new FontFamily("Segoe UI"),
        };

        // Measure text to size the background.
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = tb.DesiredSize;
        bg.Width = desired.Width;
        bg.Height = desired.Height;
        Canvas.SetLeft(bg, p.X);
        Canvas.SetTop(bg, p.Y);
        Canvas.SetLeft(tb, p.X);
        Canvas.SetTop(tb, p.Y);
        _canvas.Children.Add(bg);
        _canvas.Children.Add(tb);

        // Store as a "shape" for serialization purposes; the JSON layer
        // emits a text record from the Tag.
        bg.Tag = new TextShapeData { Text = input, X = p.X, Y = p.Y, Color = ColorToHex(_strokeBrush), FontSize = 14 };
        _shapes.Add(bg);
    }

    // Render the canvas (background + annotations) to a PNG at source
    // resolution. Used by the Save flow to produce annotated.png.
    public byte[] RenderAnnotatedPng()
    {
        if (_background.Source == null) return Array.Empty<byte>();
        var width = SourceWidth;
        var height = SourceHeight;
        if (width == 0 || height == 0) return Array.Empty<byte>();

        // Force a layout pass at source dimensions so children render at
        // the right scale regardless of the visible on-screen size.
        _canvas.Measure(new Size(width, height));
        _canvas.Arrange(new Rect(0, 0, width, height));
        _canvas.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(_canvas);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    // Serialize the recorded shapes into the design §C5 annotations.json
    // shape. Coordinates are in the source image's pixel space (canvas is
    // sized to PixelWidth/PixelHeight after LoadImage).
    public string SerializeAnnotationsJson(string sourceImageFile)
    {
        var doc = new
        {
            version = 1,
            sourceImage = sourceImageFile,
            imageWidth = SourceWidth,
            imageHeight = SourceHeight,
            shapes = _shapes.Select(SerializeShape).Where(s => s != null).ToArray(),
        };
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }

    private object? SerializeShape(Shape s)
    {
        switch (s)
        {
            case Rectangle r when r.Tag is TextShapeData td:
                return new
                {
                    id = $"t{_shapes.IndexOf(s)}",
                    type = "text",
                    x = td.X,
                    y = td.Y,
                    text = td.Text,
                    color = td.Color,
                    fontSize = td.FontSize,
                };
            case Rectangle r:
                return new
                {
                    id = $"r{_shapes.IndexOf(s)}",
                    type = "rect",
                    x = Canvas.GetLeft(r),
                    y = Canvas.GetTop(r),
                    w = r.Width,
                    h = r.Height,
                    stroke = ColorToHex(r.Stroke),
                    strokeWidth = r.StrokeThickness,
                };
            case Polyline poly:
                return new
                {
                    id = $"p{_shapes.IndexOf(s)}",
                    type = "freehand",
                    points = poly.Points.Select(pt => new[] { pt.X, pt.Y }).ToArray(),
                    stroke = ColorToHex(poly.Stroke),
                    strokeWidth = poly.StrokeThickness,
                };
            default:
                return null;
        }
    }

    private static string ColorToHex(Brush brush)
    {
        if (brush is SolidColorBrush scb)
        {
            var c = scb.Color;
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        return "#FFFFFF";
    }

    private sealed class TextShapeData
    {
        public required string Text { get; init; }
        public required double X { get; init; }
        public required double Y { get; init; }
        public required string Color { get; init; }
        public required double FontSize { get; init; }
    }
}
