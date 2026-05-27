using System.Text.Json;
using Canary.UI.Avalonia.Controls;
using Canary.UI.Avalonia.ViewModels;
using Xunit;
using Brushes = Avalonia.Media.Brushes;
using Color = Avalonia.Media.Color;
using SolidColorBrush = Avalonia.Media.SolidColorBrush;

namespace Canary.Tests.UI.Avalonia;

[Trait("Category", "Unit")]
public class AnnotateWindowViewModelTests
{
    private static readonly byte[] FakePngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private const string FakeAnnotationsJson = "{ \"version\": 1, \"shapes\": [] }";

    private static string WriteFakeSourcePng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"canary-annotate-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, FakePngBytes);
        return path;
    }

    [Fact]
    public void SessionSinkMode_Save_InvokesSinkAndRequestsClose()
    {
        var sourcePath = WriteFakeSourcePng();
        try
        {
            byte[]? capturedSource = null;
            byte[]? capturedAnnotated = null;
            string? capturedJson = null;
            void Sink(byte[] s, byte[] a, string j) { capturedSource = s; capturedAnnotated = a; capturedJson = j; }

            var vm = new AnnotateWindowViewModel(sourcePath, Sink)
            {
                GetAnnotatedPngBytes = () => FakePngBytes,
                GetAnnotationsJson = () => FakeAnnotationsJson,
            };
            var closeRequested = false;
            vm.RequestClose += () => closeRequested = true;

            Assert.True(vm.IsSessionSinkMode);
            Assert.False(vm.IsInboxMode);

            vm.SaveCommand.Execute(null);

            Assert.NotNull(capturedSource);
            Assert.Equal(FakePngBytes, capturedSource);
            Assert.Equal(FakePngBytes, capturedAnnotated);
            Assert.Equal(FakeAnnotationsJson, capturedJson);
            Assert.True(closeRequested);
            Assert.Contains("Saved into session", vm.StatusText);
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
        }
    }

    [Fact]
    public void InboxMode_Save_WritesFeedbackTriadAndMarkdownToDisk()
    {
        var sourcePath = WriteFakeSourcePng();
        var inboxRoot = Path.Combine(Path.GetTempPath(), $"canary-annotate-inbox-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(inboxRoot);
            var vm = new AnnotateWindowViewModel(sourcePath, inboxRoot, runRef: "workloads/qualia/results/x/runs/y", checkpointRef: "home")
            {
                GetAnnotatedPngBytes = () => FakePngBytes,
                GetAnnotationsJson = () => FakeAnnotationsJson,
                Title = "Layout broken",
                Body = "Status overlapping hotkey hint",
            };
            var closeRequested = false;
            vm.RequestClose += () => closeRequested = true;

            Assert.True(vm.IsInboxMode);
            Assert.False(vm.IsSessionSinkMode);

            vm.SaveCommand.Execute(null);

            Assert.True(closeRequested);
            // Inbox writer produces one .md + a sidecar dir with the triad.
            var mdFiles = Directory.GetFiles(inboxRoot, "*.md");
            Assert.Single(mdFiles);
            var md = File.ReadAllText(mdFiles[0]);
            Assert.Contains("Layout broken", md);
            Assert.Contains("Status overlapping hotkey hint", md);
            Assert.Contains("runRef: \"workloads/qualia/results/x/runs/y\"", md);
            Assert.Contains("checkpointRef: \"home\"", md);

            var slug = Path.GetFileNameWithoutExtension(mdFiles[0]);
            var sidecar = Path.Combine(inboxRoot, slug);
            Assert.True(File.Exists(Path.Combine(sidecar, "source.png")));
            Assert.True(File.Exists(Path.Combine(sidecar, "annotated.png")));
            Assert.True(File.Exists(Path.Combine(sidecar, "annotations.json")));
            Assert.Equal(FakeAnnotationsJson, File.ReadAllText(Path.Combine(sidecar, "annotations.json")));
            Assert.Contains("Saved:", vm.StatusText);
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
            if (Directory.Exists(inboxRoot)) Directory.Delete(inboxRoot, recursive: true);
        }
    }

    [Fact]
    public void InboxMode_EmptyTitle_FallsBackToSourceFileName()
    {
        var sourcePath = WriteFakeSourcePng();
        var inboxRoot = Path.Combine(Path.GetTempPath(), $"canary-annotate-inbox-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(inboxRoot);
            var vm = new AnnotateWindowViewModel(sourcePath, inboxRoot)
            {
                GetAnnotatedPngBytes = () => FakePngBytes,
                GetAnnotationsJson = () => FakeAnnotationsJson,
                // Leave Title empty intentionally.
            };
            vm.SaveCommand.Execute(null);

            var mdFiles = Directory.GetFiles(inboxRoot, "*.md");
            Assert.Single(mdFiles);
            var slug = Path.GetFileNameWithoutExtension(mdFiles[0]);
            // The slug derives from the source PNG name (canary-annotate-<guid>).
            // The generator collapses to a slug-safe substring; just sanity-check
            // it's non-empty and the markdown body uses the same heading.
            Assert.False(string.IsNullOrEmpty(slug));
            var md = File.ReadAllText(mdFiles[0]);
            Assert.Contains("# ", md);
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
            if (Directory.Exists(inboxRoot)) Directory.Delete(inboxRoot, recursive: true);
        }
    }

    [Fact]
    public void Save_WhenSourceMissing_SurfacesError()
    {
        var vm = new AnnotateWindowViewModel("/this/file/does/not/exist.png",
            (a, b, c) => { /* no-op sink */ })
        {
            GetAnnotatedPngBytes = () => FakePngBytes,
            GetAnnotationsJson = () => FakeAnnotationsJson,
        };
        vm.SaveCommand.Execute(null);
        Assert.Contains("Save failed", vm.StatusText);
        Assert.Equal("#FFB4B4", vm.StatusColor);
    }

    [Fact]
    public void PickTool_UpdatesCurrentTool()
    {
        var vm = new AnnotateWindowViewModel("any", (_, _, _) => { });
        Assert.Equal(AnnotationCanvas.ToolMode.Rectangle, vm.CurrentTool);
        vm.PickToolCommand.Execute("Freehand");
        Assert.Equal(AnnotationCanvas.ToolMode.Freehand, vm.CurrentTool);
        vm.PickToolCommand.Execute("Text");
        Assert.Equal(AnnotationCanvas.ToolMode.Text, vm.CurrentTool);
        vm.PickToolCommand.Execute("Pointer");
        Assert.Equal(AnnotationCanvas.ToolMode.Pointer, vm.CurrentTool);
    }

    [Fact]
    public void PickColor_SwapsBrush()
    {
        var vm = new AnnotateWindowViewModel("any", (_, _, _) => { });
        Assert.Equal(Brushes.Red, vm.CurrentBrush);
        vm.PickColorCommand.Execute("Yellow");
        Assert.IsType<SolidColorBrush>(vm.CurrentBrush);
        Assert.Equal(Color.FromRgb(0xFB, 0xBF, 0x24), ((SolidColorBrush)vm.CurrentBrush).Color);
        vm.PickColorCommand.Execute("Green");
        Assert.Equal(Color.FromRgb(0x10, 0xB9, 0x81), ((SolidColorBrush)vm.CurrentBrush).Color);
    }

    [Fact]
    public void Undo_DelegatesToRequestUndo()
    {
        var vm = new AnnotateWindowViewModel("any", (_, _, _) => { });
        var undoCalled = 0;
        vm.RequestUndo = () => undoCalled++;
        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.Equal(2, undoCalled);
    }

    [Fact]
    public void Clear_DelegatesToRequestClear()
    {
        var vm = new AnnotateWindowViewModel("any", (_, _, _) => { });
        var clearCalled = false;
        vm.RequestClear = () => clearCalled = true;
        vm.ClearCommand.Execute(null);
        Assert.True(clearCalled);
    }

    [Fact]
    public void ToolModeConverter_TrueOnlyForMatchingTarget()
    {
        var rectangleConv = ToolModeConverter.Rectangle;
        var result = rectangleConv.Convert(AnnotationCanvas.ToolMode.Rectangle, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(true, result);

        var notResult = rectangleConv.Convert(AnnotationCanvas.ToolMode.Freehand, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(false, notResult);
    }
}
