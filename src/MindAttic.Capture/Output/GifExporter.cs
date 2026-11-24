using MindAttic.Capture.Ffmpeg;
using MindAttic.Capture.Hosting;

namespace MindAttic.Capture.Output;

/// <summary>Stitches screenshot PNGs into a palette-optimized, time-stamped timelapse GIF.</summary>
internal static class GifExporter
{
    public const double HoldSeconds = 2.0;

    public static async Task<string> ExportAsync(string ffmpeg, IReadOnlyList<string> frames, int fps, string? output, int maxWidth, ICaptureLog log, CancellationToken ct)
    {
        if (frames is null || frames.Count == 0) throw new CaptureException(CaptureError.InvalidArgument, "No frames to make a GIF from.");
        if (fps is not (2 or 4)) throw new CaptureException(CaptureError.InvalidArgument, $"A GIF runs at 2 or 4 fps, not {fps}.");
        if (maxWidth < 2) throw new CaptureException(CaptureError.InvalidArgument, $"The GIF width cap must be at least 2 pixels, not {maxWidth}.");

        var present = new List<string>();
        foreach (var frame in frames)
        {
            if (File.Exists(frame)) present.Add(Path.GetFullPath(frame));
            else log.Warn($"GIF frame \"{frame}\" is missing; skipping it.");
        }
        if (present.Count == 0) throw new CaptureException(CaptureError.InvalidArgument, "None of the GIF's frames exist.");

        var (srcWidth, srcHeight) = PngSize(present[0]);
        var width = Math.Max(2, Math.Min(maxWidth, srcWidth) / 2 * 2);
        var height = Math.Max(2, (int)Math.Round((double)srcHeight * width / srcWidth / 2) * 2);

        var first = present[0];
        output = Path.GetFullPath(output ?? Path.Combine(Path.GetDirectoryName(first)!, Path.GetFileName(Path.GetDirectoryName(first)!) + ".gif"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var work = Path.Combine(Path.GetTempPath(), "MindAttic.Capture.gif_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(work);
        try
        {
            var list = Path.Combine(work, "frames.ffconcat");
            var stamps = Path.Combine(work, "stamps.txt");
            File.WriteAllText(list, FfmpegArgs.GifConcatList(present, fps));
            File.WriteAllText(stamps, FfmpegArgs.GifStampCommands(present.Select(File.GetLastWriteTime).ToList(), fps));
            var pattern = Path.Combine(work, "%06d.png");
            await FfmpegProcess.RunAsync(ffmpeg, FfmpegArgs.GifStampFrames(list, stamps, width, height, FontFile(), pattern), log, ct).ConfigureAwait(false);
            await FfmpegProcess.RunAsync(ffmpeg, FfmpegArgs.GifFromFrames(pattern, fps, HoldSeconds, output), log, ct).ConfigureAwait(false);
            if (!File.Exists(output)) throw new CaptureException(CaptureError.FfmpegFailed, $"ffmpeg finished but wrote no GIF at {output}.");
            log.Info($"GIF of {present.Count} frame(s) at {fps} fps, {width}x{height}: {output}");
            return output;
        }
        finally
        {
            try
            {
                Directory.Delete(work, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Warn($"Could not delete the GIF work folder {work}: {ex.Message}");
            }
        }
    }

    /// <summary>Width and height from a PNG's IHDR chunk.</summary>
    private static (int Width, int Height) PngSize(string file)
    {
        var header = new byte[24];
        using (var fs = File.OpenRead(file))
        {
            var read = 0;
            while (read < header.Length)
            {
                var n = fs.Read(header, read, header.Length - read);
                if (n == 0) break;
                read += n;
            }
            if (read < header.Length || header[0] != 0x89 || header[1] != (byte)'P' || header[2] != (byte)'N' || header[3] != (byte)'G')
                throw new CaptureException(CaptureError.InvalidArgument, $"GIF frame \"{file}\" is not a PNG.");
        }
        var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
        var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        if (width <= 0 || height <= 0) throw new CaptureException(CaptureError.InvalidArgument, $"GIF frame \"{file}\" has no size.");
        return (width, height);
    }

    /// <summary>A monospace (or at least common) system font for the stamp; null lets ffmpeg's fontconfig pick.</summary>
    private static string? FontFile()
    {
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (string.IsNullOrEmpty(fonts)) return null;
        foreach (var name in new[] { "consola.ttf", "cour.ttf", "arial.ttf" })
        {
            var path = Path.Combine(fonts, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
