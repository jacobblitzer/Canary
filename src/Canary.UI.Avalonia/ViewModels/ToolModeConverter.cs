using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Canary.UI.Avalonia.Controls;

namespace Canary.UI.Avalonia.ViewModels;

// Bool-returning converter for the AnnotateWindow toolbar's
// ToggleButton group. Each ToolMode-named static is a one-way binding
// that returns true when the bound ToolMode equals the converter's
// target value. Lets ToggleButton.IsChecked drive the "this is the
// active tool" indicator without writing N converter classes.
public sealed class ToolModeConverter : IValueConverter
{
    public static readonly ToolModeConverter Pointer = new(AnnotationCanvas.ToolMode.Pointer);
    public static readonly ToolModeConverter Rectangle = new(AnnotationCanvas.ToolMode.Rectangle);
    public static readonly ToolModeConverter Freehand = new(AnnotationCanvas.ToolMode.Freehand);
    public static readonly ToolModeConverter Text = new(AnnotationCanvas.ToolMode.Text);

    private readonly AnnotationCanvas.ToolMode _target;

    private ToolModeConverter(AnnotationCanvas.ToolMode target) { _target = target; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is AnnotationCanvas.ToolMode mode && mode == _target;

    // One-way binding — ToggleButton.IsChecked feeds the ToggleButton
    // visual state only; the PickToolCommand handles the actual tool
    // change. ConvertBack is never hit but must return something safe.
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
