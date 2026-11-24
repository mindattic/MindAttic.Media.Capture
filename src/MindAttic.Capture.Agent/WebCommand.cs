using MindAttic.Capture.Hosting;
using MindAttic.Capture.Output;
using MindAttic.Capture.Sets;
using MindAttic.Capture.Web;

namespace MindAttic.Capture.Agent;

/// <summary>
/// <c>capture web &lt;url&gt;</c>: headless screenshots and (<c>--video</c>) webm of a page through Playwright and the
/// installed Edge, straight into a preview set with <c>--set DIR</c>, else into <c>--out</c> (or the resolved default root).
/// </summary>
internal static class WebCommand
{
    public static async Task<int> RunAsync(AgentArgs args, ICaptureLog log)
    {
        var files = await CaptureAsync(args, log);
        foreach (var file in files) Console.WriteLine(file);
        return 0;
    }

    /// <summary>The shared logic behind <c>capture web</c>: used by the CLI and directly by <c>capture mcp</c>'s
    /// capture_web tool, which returns the file paths as structured data instead of console lines.</summary>
    public static async Task<IReadOnlyList<string>> CaptureAsync(AgentArgs args, ICaptureLog log)
    {
        var url = args.Positional.FirstOrDefault() ?? throw new CaptureException(CaptureError.InvalidArgument, "web needs a URL.");
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "web";
        var options = new WebCaptureOptions
        {
            Url = url,
            Viewports = args.GetAll("viewport").Select(WebCapture.ParseViewport).ToList(),
            Device = args.Get("device"),
            FullPage = args.Has("full-page"),
            WaitFor = args.Get("wait-for"),
            Wait = args.GetDuration("wait") ?? TimeSpan.Zero,
            Dark = args.Has("dark"),
            FakeMedia = args.Has("fake-media"),
            Video = args.GetDuration("video"),
            Channel = args.Get("channel") ?? "msedge",
        };

        return args.Get("set") is { } set ? await IntoSetAsync(args, options, set, host, log) : await IntoFolderAsync(args, options, host, log);
    }

    private static async Task<IReadOnlyList<string>> IntoSetAsync(AgentArgs args, WebCaptureOptions options, string set, string host, ICaptureLog log)
    {
        set = Path.GetFullPath(set);
        if (!Directory.Exists(set)) throw new CaptureException(CaptureError.InvalidArgument, $"No preview set at {set} (make one with `capture set new`).");
        var staging = Path.Combine(set, ".staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var shots = await WebCapture.CaptureAsync(options, staging, args.Get("tag") ?? host);
            var store = new PreviewSetStore(log: log);
            var files = new List<string>();
            foreach (var group in shots.GroupBy(s => (s.Viewport, s.Device)))
            {
                var source = new PreviewSource { Type = "web", Url = options.Url, Viewport = group.Key.Viewport, Device = group.Key.Device };
                var items = store.Import(set, group.Select(s => s.File).ToList(), args.Get("tag"), args.Get("caption"), source, ImportMode.Move);
                files.AddRange(items.Select(i => Path.Combine(set, i.File)));
            }
            return files;
        }
        finally
        {
            TryDelete(staging);
            TryDelete(Path.GetDirectoryName(staging)!, onlyIfEmpty: true);
        }
    }

    private static async Task<IReadOnlyList<string>> IntoFolderAsync(AgentArgs args, WebCaptureOptions options, string host, ICaptureLog log)
    {
        var defaults = new CaptureOptions();
        var root = OutputManager.ResolveRoot(args.Get("out") ?? defaults.OutputRoot, defaults.FallbackRoot, log);
        var outDir = Path.Combine(root, "Web", OutputManager.Sanitize(host), CaptureNaming.Timestamp(DateTimeOffset.Now));
        var shots = await WebCapture.CaptureAsync(options, outDir, args.Get("tag") ?? host);
        return shots.Select(s => s.File).ToList();
    }

    private static void TryDelete(string dir, bool onlyIfEmpty = false)
    {
        try
        {
            if (!Directory.Exists(dir) || onlyIfEmpty && Directory.EnumerateFileSystemEntries(dir).Any()) return;
            Directory.Delete(dir, recursive: !onlyIfEmpty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
