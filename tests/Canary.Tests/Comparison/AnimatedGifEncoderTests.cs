using System.Collections.Generic;
using System.IO;
using Canary.Comparison;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Canary.Tests.Comparison;

[Trait("Category", "Unit")]
public class AnimatedGifEncoderTests
{
    private static string WriteSolidPng(string dir, string name, int width, int height, Rgba32 color)
    {
        var path = Path.Combine(dir, name);
        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++) row[x] = color;
            }
        });
        image.SaveAsPng(path);
        return path;
    }

    [Fact]
    public void Encode_ThreeFrames_WritesValidGif()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "canary-gif-test-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var f0 = WriteSolidPng(tmp, "f0.png", 32, 32, new Rgba32(255, 0, 0, 255));
            var f1 = WriteSolidPng(tmp, "f1.png", 32, 32, new Rgba32(0, 255, 0, 255));
            var f2 = WriteSolidPng(tmp, "f2.png", 32, 32, new Rgba32(0, 0, 255, 255));
            var outPath = Path.Combine(tmp, "out.gif");

            int n = AnimatedGifEncoder.Encode(new List<string> { f0, f1, f2 }, outPath, frameDelayCs: 10);

            Assert.Equal(3, n);
            Assert.True(File.Exists(outPath));
            // GIF89a header sanity check
            using var fs = File.OpenRead(outPath);
            var header = new byte[6];
            Assert.Equal(6, fs.Read(header, 0, 6));
            Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(header));

            // Reload and confirm frame count round-trips
            using var loaded = Image.Load(outPath);
            Assert.Equal(3, loaded.Frames.Count);
            Assert.Equal(32, loaded.Width);
            Assert.Equal(32, loaded.Height);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Encode_EmptyFrameList_Throws()
    {
        Assert.Throws<System.ArgumentException>(() =>
            AnimatedGifEncoder.Encode(System.Array.Empty<string>(), "ignored.gif", frameDelayCs: 10));
    }

    [Fact]
    public void Encode_SkipsMissingFrames()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "canary-gif-test-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var f0 = WriteSolidPng(tmp, "f0.png", 16, 16, new Rgba32(128, 128, 128, 255));
            var missing = Path.Combine(tmp, "missing.png");
            var f2 = WriteSolidPng(tmp, "f2.png", 16, 16, new Rgba32(64, 64, 64, 255));
            var outPath = Path.Combine(tmp, "out.gif");

            int n = AnimatedGifEncoder.Encode(new List<string> { f0, missing, f2 }, outPath, frameDelayCs: 5);

            Assert.Equal(2, n);
            using var loaded = Image.Load(outPath);
            Assert.Equal(2, loaded.Frames.Count);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* best-effort */ }
        }
    }
}
