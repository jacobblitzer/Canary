using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Canary.UI.Avalonia.Controls;
using Xunit;

namespace Canary.Tests.UI.Avalonia;

/// <summary>
/// BUG-0006 — `AnimatedImagePanel` must not throw on null / empty / missing
/// `SourcePath`. Three null-safe scenarios are covered here; full decode +
/// playback verification is operator-driven (no Avalonia headless test
/// runtime in this project — see docs/bugs/0006-resultsviewer-gif-crash-batch-bind.md
/// "Verification" for the manual smoke).
/// </summary>
[Trait("Category", "Unit")]
public class AnimatedImagePanelTests
{
    [Fact]
    public void SourcePath_Null_DoesNotThrow()
    {
        // Avalonia controls are usually constructed by the XAML loader on the
        // UI thread, but the SourcePath setter must be safe in any context
        // because Avalonia binding evaluation can also fire from non-attach
        // paths. The setter chains through OnSourcePathChanged which only
        // touches managed state (cancels CTS, releases Bitmaps) — no
        // Avalonia visual-tree access until OnAttachedToVisualTree fires.
        var panel = new AnimatedImagePanel();
        panel.SourcePath = null;
        Assert.Null(panel.SourcePath);
    }

    [Fact]
    public void SourcePath_Empty_DoesNotThrow()
    {
        var panel = new AnimatedImagePanel();
        panel.SourcePath = "";
        panel.SourcePath = "   ";
        Assert.Equal("   ", panel.SourcePath);
    }

    [Fact]
    public async Task SourcePath_MissingFile_DoesNotThrow()
    {
        // File.Exists check in OnSourcePathChanged short-circuits before
        // kicking off the decode task. Even if the file is deleted
        // mid-decode, DecodeAsync's try/catch swallows the IO exception.
        var panel = new AnimatedImagePanel();
        var path = Path.Combine(Path.GetTempPath(), "canary-no-such-gif-" + System.Guid.NewGuid().ToString("N") + ".gif");
        Assert.False(File.Exists(path));
        panel.SourcePath = path;
        // Give any pending async work a chance to fire its catch block —
        // there's nothing to assert on the panel itself (it stays blank).
        await Task.Delay(100);
        Assert.Equal(path, panel.SourcePath);
    }

    [Fact]
    public void SourcePath_RapidChange_DoesNotThrow()
    {
        // Cycle SourcePath through several values fast — the cancellation
        // pattern in OnSourcePathChanged should drop the in-flight decode
        // each time. No exception should escape to xunit.
        var panel = new AnimatedImagePanel();
        for (int i = 0; i < 10; i++)
        {
            panel.SourcePath = $"C:/nonexistent-{i}.gif";
            panel.SourcePath = null;
            panel.SourcePath = "";
        }
        Assert.Equal("", panel.SourcePath);
    }

    [Fact]
    public void IsPlaying_Toggle_DoesNotThrow_WithoutSource()
    {
        // Toggling IsPlaying with no SourcePath assigned should also be a
        // no-op (StartTimer early-returns when _frames is null).
        var panel = new AnimatedImagePanel();
        panel.IsPlaying = true;
        panel.IsPlaying = false;
        panel.IsPlaying = true;
        Assert.True(panel.IsPlaying);
    }
}
