using MindAttic.Media.Capture.Hosting;

namespace MindAttic.Media.Capture.Agent;

/// <summary>
/// <c>capture gif &lt;folder&gt;</c>: stitches a folder's screenshot PNGs, in name order, into a timelapse GIF
/// (<see cref="CaptureEngine.ExportGifAsync"/>) and prints its path.
/// </summary>
internal static class GifCommand
{
    public static async Task<int> RunAsync(AgentArgs args, ICaptureLog log)
    {
        var folder = args.Positional.FirstOrDefault() ?? throw new CaptureException(CaptureError.InvalidArgument, "gif needs a folder of PNGs.");
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder)) throw new CaptureException(CaptureError.InvalidArgument, $"No folder at {folder}.");
        var frames = Directory.EnumerateFiles(folder, "*.png").Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (frames.Count == 0) throw new CaptureException(CaptureError.InvalidArgument, $"No PNGs in {folder}.");

        var gif = await CaptureEngine.ExportGifAsync(frames, args.GetInt("fps") ?? 2, args.Get("output"), args.GetInt("width") ?? 1280,
            args.Get("ffmpeg") ?? new CaptureOptions().FfmpegPath, log);
        Console.WriteLine(gif);
        return 0;
    }
}
