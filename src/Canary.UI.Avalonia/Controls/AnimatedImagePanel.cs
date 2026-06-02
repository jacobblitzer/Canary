using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace Canary.UI.Avalonia.Controls;

/// <summary>
/// BUG-0006 fix — replacement for <c>Avalonia.Labs.Gif.GifImage</c>. Hosts a
/// plain <see cref="Image"/> and drives frame-by-frame playback via a
/// <see cref="DispatcherTimer"/> using frames decoded by SixLabors.ImageSharp
/// on a worker thread.
///
/// Safe to use inside a <c>DataTemplate</c> in an <c>ItemsControl</c>: no
/// custom-visual lifecycle, all error paths swallow exceptions instead of
/// propagating to the dispatcher (null / empty / missing-file SourcePath are
/// no-ops, decode failures log to Debug and leave the panel blank). Cards
/// can be batch-added with mixed null / non-null SourcePaths without
/// crashing the UI thread.
///
/// See <c>docs/bugs/0006-resultsviewer-gif-crash-batch-bind.md</c> for the
/// root-cause investigation and <c>docs/research/2026-06-02-avalonia-animated-gif-options.md</c>
/// for the alternative-survey that led here.
/// </summary>
public sealed class AnimatedImagePanel : UserControl
{
    public static readonly StyledProperty<string?> SourcePathProperty =
        AvaloniaProperty.Register<AnimatedImagePanel, string?>(nameof(SourcePath));

    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<AnimatedImagePanel, bool>(nameof(IsPlaying), defaultValue: true);

    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<AnimatedImagePanel, Stretch>(nameof(Stretch), defaultValue: Stretch.Uniform);

    public static readonly StyledProperty<StretchDirection> StretchDirectionProperty =
        AvaloniaProperty.Register<AnimatedImagePanel, StretchDirection>(nameof(StretchDirection), defaultValue: StretchDirection.DownOnly);

    /// <summary>
    /// Diagnostic status string. Read-only from XAML. Set to one of:
    /// "" (no source), "Decoding…", "Decoded N frames", "Decode failed: …".
    /// Bind <c>Text="{Binding #panel.Status}"</c> next to the control to
    /// see what the decoder is doing when no animation appears.
    /// </summary>
    public static readonly DirectProperty<AnimatedImagePanel, string> StatusProperty =
        AvaloniaProperty.RegisterDirect<AnimatedImagePanel, string>(nameof(Status), o => o.Status);
    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        private set => SetAndRaise(StatusProperty, ref _status, value);
    }

    public string? SourcePath
    {
        get => GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public StretchDirection StretchDirection
    {
        get => GetValue(StretchDirectionProperty);
        set => SetValue(StretchDirectionProperty, value);
    }

    private readonly global::Avalonia.Controls.Image _image;
    private List<Bitmap>? _frames;
    private List<int>? _delaysMs;
    private DispatcherTimer? _timer;
    private CancellationTokenSource? _decodeCts;
    private int _frameIndex;

    public AnimatedImagePanel()
    {
        _image = new global::Avalonia.Controls.Image();
        _image.Bind(global::Avalonia.Controls.Image.StretchProperty, this.GetObservable(StretchProperty));
        _image.Bind(global::Avalonia.Controls.Image.StretchDirectionProperty, this.GetObservable(StretchDirectionProperty));
        Content = _image;
    }

    static AnimatedImagePanel()
    {
        SourcePathProperty.Changed.AddClassHandler<AnimatedImagePanel>((s, e) =>
            s.OnSourcePathChanged(e.NewValue as string));
        IsPlayingProperty.Changed.AddClassHandler<AnimatedImagePanel>((s, e) =>
            s.OnIsPlayingChanged(e.NewValue is bool b ? b : true));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopTimer();
        try { _decodeCts?.Cancel(); } catch { }
        _decodeCts = null;
        ReleaseFrames();
    }

    private void OnSourcePathChanged(string? path)
    {
        // Cancel any in-flight decode (a rapid SourcePath change e.g. from
        // DataGrid re-selection should drop the prior work, not race with it).
        try { _decodeCts?.Cancel(); } catch { }
        _decodeCts = null;
        StopTimer();
        _image.Source = null;
        ReleaseFrames();
        Status = string.Empty;

        if (string.IsNullOrWhiteSpace(path)) return;
        // File.Exists is best-effort cheap synchronous; if false we just stay
        // blank rather than throwing. The decoder also handles missing files
        // defensively in case the file is deleted mid-decode.
        if (!File.Exists(path))
        {
            Status = $"File not found: {path}";
            return;
        }

        Status = "Decoding…";
        var cts = new CancellationTokenSource();
        _decodeCts = cts;
        _ = Task.Run(() => DecodeAsync(path!, cts.Token), cts.Token);
    }

    private async Task DecodeAsync(string path, CancellationToken ct)
    {
        try
        {
            using var image = await ImageSharpImage.LoadAsync<Rgba32>(path, ct).ConfigureAwait(false);
            int frameCount = image.Frames.Count;
            var frames = new List<Bitmap>(frameCount);
            var delays = new List<int>(frameCount);
            var pngEncoder = new PngEncoder();

            for (int i = 0; i < frameCount; i++)
            {
                ct.ThrowIfCancellationRequested();

                // Per-frame delay sits on the source image's frame metadata.
                // FrameDelay is in centiseconds (1/100 s) per the GIF spec.
                // Many encoders emit 0 for stills; clamp to 100ms minimum so
                // the timer doesn't spin tightly.
                int delayMs = 100;
                try
                {
                    var gifMeta = image.Frames[i].Metadata.GetGifMetadata();
                    if (gifMeta != null && gifMeta.FrameDelay > 0)
                        delayMs = Math.Max(20, gifMeta.FrameDelay * 10);
                }
                catch { /* metadata is optional — fall back to 100ms */ }
                delays.Add(delayMs);

                // CloneFrame gives us a single-frame Image<Rgba32> we can
                // round-trip to PNG bytes for Avalonia's Bitmap constructor.
                using var clone = image.Frames.CloneFrame(i);
                using var ms = new MemoryStream();
                await clone.SaveAsync(ms, pngEncoder, ct).ConfigureAwait(false);
                ms.Position = 0;
                frames.Add(new Bitmap(ms));
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    foreach (var b in frames) b.Dispose();
                    return;
                }
                _frames = frames;
                _delaysMs = delays;
                _frameIndex = 0;
                _image.Source = frames[0];
                Status = $"Decoded {frames.Count} frames";
                if (IsPlaying && frames.Count > 1) StartTimer();
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when SourcePath changes mid-decode.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnimatedImagePanel] decode failed for {path}: {ex.Message}");
            try { await Dispatcher.UIThread.InvokeAsync(() => Status = $"Decode failed: {ex.Message}"); } catch { }
        }
    }

    private void OnIsPlayingChanged(bool playing)
    {
        if (playing) StartTimer();
        else StopTimer();
    }

    private void StartTimer()
    {
        if (_frames == null || _frames.Count <= 1 || _delaysMs == null) return;
        StopTimer();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_delaysMs[_frameIndex])
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void StopTimer()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_frames == null || _frames.Count == 0) return;
        _frameIndex = (_frameIndex + 1) % _frames.Count;
        _image.Source = _frames[_frameIndex];
        if (_timer != null && _delaysMs != null)
            _timer.Interval = TimeSpan.FromMilliseconds(_delaysMs[_frameIndex]);
    }

    private void ReleaseFrames()
    {
        if (_frames != null)
        {
            foreach (var b in _frames) b.Dispose();
            _frames = null;
        }
        _delaysMs = null;
        _frameIndex = 0;
    }
}
