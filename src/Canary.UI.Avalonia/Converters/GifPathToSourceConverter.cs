using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Canary.UI.Avalonia.Converters;

/// <summary>
/// Phase 4.6.F Session B++ — adapts a string GIF file path into something the
/// <c>Avalonia.Labs.Gif.GifImage.Source</c> property (typed <c>object</c> in
/// 11.3.1) will accept. GifImage unwraps Source internally: Stream → cached
/// stream; Uri / string-Uri → <c>AssetLoader.Open(uri)</c>. AssetLoader knows the
/// <c>file://</c> scheme, so we emit a <see cref="Uri"/> with that scheme for
/// local-disk paths. Returns null for empty / null / unparseable input so the
/// GifImage stays blank instead of throwing.
/// </summary>
public sealed class GifPathToSourceConverter : IValueConverter
{
    public static readonly GifPathToSourceConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return null;
        if (value is Uri u) return u;
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            // Local-disk paths need the file:/// scheme to be recognised by
            // Avalonia's AssetLoader. avares:// / http(s) Uris are kept as-is.
            if (Uri.TryCreate(s, UriKind.Absolute, out var abs))
                return abs;
            // Fallback: treat as a Windows file path.
            try
            {
                var full = System.IO.Path.GetFullPath(s).Replace('\\', '/');
                return new Uri("file:///" + full);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
