using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace Canary.Comparison;

/// <summary>
/// Encodes a sequence of frame PNGs into a single animated GIF with a fixed per-frame
/// delay and infinite-loop playback. Wraps <c>SixLabors.ImageSharp.Formats.Gif.GifEncoder</c>
/// (already a Canary.Core dependency for pixel-diff) so no new NuGet is added for
/// Phase 4.6.F Session B GIF capture.
/// </summary>
/// <remarks>
/// The agent (RhinoScreenCapture) captures N viewport frames as sibling PNGs when
/// <see cref="Canary.Agent.CaptureSettings.RecordGif"/> is true; the orchestrator
/// (TestRunner) calls this encoder to assemble the GIF.
///
/// Out of scope: per-frame variable delays, palette overrides, transparency. The
/// encoder uses ImageSharp's default Wu quantizer (256 colours), which gives
/// usable output for kinematics-style stop-motion / animated-render fixtures.
/// </remarks>
public static class AnimatedGifEncoder
{
    /// <summary>
    /// Encodes <paramref name="framePaths"/> in order into a single animated GIF at
    /// <paramref name="outputPath"/>. Loops forever; each frame is shown for
    /// <paramref name="frameDelayCs"/> centiseconds (1/100 s).
    /// </summary>
    /// <returns>The number of frames successfully encoded.</returns>
    /// <exception cref="ArgumentException">Empty frame list.</exception>
    public static int Encode(IReadOnlyList<string> framePaths, string outputPath, int frameDelayCs)
    {
        if (framePaths == null || framePaths.Count == 0)
            throw new ArgumentException("No frames to encode.", nameof(framePaths));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is empty.", nameof(outputPath));

        int delay = Math.Max(1, frameDelayCs);
        var outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir!);

        // Build the animated image. ImageSharp's GIF encoder reads the per-frame
        // metadata (delay + disposal) off each frame's GifFrameMetadata; the
        // image-level GifMetadata holds the loop count.
        Image<Rgba32>? canvas = null;
        int encoded = 0;
        try
        {
            for (int i = 0; i < framePaths.Count; i++)
            {
                var path = framePaths[i];
                if (!File.Exists(path)) continue;

                using var frame = Image.Load<Rgba32>(path);

                if (canvas == null)
                {
                    // First frame becomes the base image. Set image-level loop = 0 (infinite).
                    canvas = frame.Clone();
                    var gm = canvas.Metadata.GetGifMetadata();
                    gm.RepeatCount = 0; // 0 = loop forever (Netscape 2.0 extension)
                    gm.ColorTableMode = GifColorTableMode.Local;

                    var fm = canvas.Frames.RootFrame.Metadata.GetGifMetadata();
                    fm.FrameDelay = delay;
                    fm.DisposalMethod = GifDisposalMethod.RestoreToBackground;
                }
                else
                {
                    // Append subsequent frames as additional GIF frames. Their per-frame
                    // delay must also be set on the appended frame's metadata.
                    var added = canvas.Frames.AddFrame(frame.Frames.RootFrame);
                    var fm = added.Metadata.GetGifMetadata();
                    fm.FrameDelay = delay;
                    fm.DisposalMethod = GifDisposalMethod.RestoreToBackground;
                }
                encoded++;
            }

            if (canvas == null || encoded == 0) return 0;

            canvas.SaveAsGif(outputPath, new GifEncoder());
            return encoded;
        }
        finally
        {
            canvas?.Dispose();
        }
    }
}
