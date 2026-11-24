using System.ComponentModel;
using MindAttic.Media.Capture.Hosting;
using MindAttic.Media.Capture.Sets;
using ModelContextProtocol.Server;

namespace MindAttic.Media.Capture.Agent;

/// <summary>
/// The six tools <c>capture mcp</c> exposes. Each builds the same <see cref="AgentArgs"/> the CLI would parse from
/// argv, then calls the exact method <c>Program.cs</c>'s dispatch calls for that command, so there is one code path
/// for CLI and MCP alike. Results come back as data (not console text) for the caller to read.
/// </summary>
[McpServerToolType]
public static class CaptureTools
{
    private static ICaptureLog Log => new TextWriterCaptureLog(Console.Error, CaptureLogLevel.Info);

    [McpServerTool(Name = "preview_new_set")]
    [Description("Creates a new preview set (capture set new) and returns its manifest.")]
    public static object PreviewNewSet(
        [Description("What the set shows, in a few words.")] string title,
        [Description("The ticket it is for.")] string? ticket = null,
        [Description("Its repo folder; default the git repo of the current directory.")] string? repo = null)
    {
        var store = new PreviewSetStore(log: Log);
        return ToManifest(store.Create(title, ticket, repo));
    }

    [McpServerTool(Name = "capture_web")]
    [Description("Captures a URL (capture web): a screenshot per viewport and, with video, a webm recording, via Playwright and the installed Edge.")]
    public static async Task<object> CaptureWeb(
        [Description("The page to capture.")] string url,
        [Description("One or more viewports as WxH, e.g. 1440x900.")] string[]? viewport = null,
        [Description("A Playwright device name, e.g. \"iPhone 13\".")] string? device = null,
        [Description("The whole scrollable page instead of just the viewport.")] bool fullPage = false,
        [Description("A CSS selector to wait for before capturing.")] string? waitFor = null,
        [Description("A pause after load before capturing, e.g. 2s.")] string? wait = null,
        [Description("prefers-color-scheme: dark.")] bool dark = false,
        [Description("Fake camera and microphone, already permitted.")] bool fakeMedia = false,
        [Description("Also record this long of each viewport as a webm, e.g. 10s.")] string? video = null,
        [Description("A preview set folder to import into; omit to write into a dated folder under the output root.")] string? set = null,
        [Description("The tag after the file number.")] string? tag = null,
        [Description("A caption shown under the item in a set.")] string? caption = null,
        [Description("An output root when not importing into a set.")] string? outDir = null)
    {
        var argv = new List<string> { "web", url };
        foreach (var v in viewport ?? []) { argv.Add("--viewport"); argv.Add(v); }
        if (device is not null) { argv.Add("--device"); argv.Add(device); }
        if (fullPage) argv.Add("--full-page");
        if (waitFor is not null) { argv.Add("--wait-for"); argv.Add(waitFor); }
        if (wait is not null) { argv.Add("--wait"); argv.Add(wait); }
        if (dark) argv.Add("--dark");
        if (fakeMedia) argv.Add("--fake-media");
        if (video is not null) { argv.Add("--video"); argv.Add(video); }
        if (set is not null) { argv.Add("--set"); argv.Add(set); }
        if (tag is not null) { argv.Add("--tag"); argv.Add(tag); }
        if (caption is not null) { argv.Add("--caption"); argv.Add(caption); }
        if (outDir is not null) { argv.Add("--out"); argv.Add(outDir); }

        var files = await WebCommand.CaptureAsync(AgentArgs.Parse(argv), Log);
        return new { files };
    }

    [McpServerTool(Name = "capture_screen")]
    [Description("Captures a screenshot of the desktop, a monitor, a region or a window (capture shot).")]
    public static async Task<object> CaptureScreen(
        [Description("Capture the window hosting this CLI (the app embedding it, or the terminal when run solo).")] bool self = false,
        [Description("A monitor index; default the primary monitor (0).")] int? monitor = null,
        [Description("A region as x,y,w,h.")] string? region = null,
        [Description("A window by title (substring match).")] string? window = null,
        [Description("A window by handle, e.g. 0x1234.")] string? hwnd = null,
        [Description("A window by process name.")] string? process = null,
        [Description("png, jpg, webp, bmp or raw (default png).")] string? format = null,
        [Description("The tag after the file number.")] string? tag = null,
        [Description("A preview set folder to import into; omit to write into the output root.")] string? set = null,
        [Description("A caption shown under the item in a set.")] string? caption = null,
        [Description("Also copy the shot to the clipboard.")] bool clipboard = false,
        [Description("An output root when not importing into a set.")] string? outDir = null)
    {
        var argv = new List<string> { "shot" };
        if (self) argv.Add("--self");
        if (monitor is { } m) { argv.Add("--monitor"); argv.Add(m.ToString()); }
        if (region is not null) { argv.Add("--region"); argv.Add(region); }
        if (window is not null) { argv.Add("--window"); argv.Add(window); }
        if (hwnd is not null) { argv.Add("--hwnd"); argv.Add(hwnd); }
        if (process is not null) { argv.Add("--process"); argv.Add(process); }
        if (format is not null) { argv.Add("--format"); argv.Add(format); }
        if (tag is not null) { argv.Add("--tag"); argv.Add(tag); }
        if (set is not null) { argv.Add("--set"); argv.Add(set); }
        if (caption is not null) { argv.Add("--caption"); argv.Add(caption); }
        if (clipboard) argv.Add("--clipboard");
        if (outDir is not null) { argv.Add("--out"); argv.Add(outDir); }

        var args = AgentArgs.Parse(argv);
        var (result, staging, source) = await ScreenCommand.CaptureAsync(args, Log);
        if (!result.Succeeded)
        {
            if (staging is not null) SetCommands.CleanupStaging(staging);
            throw new InvalidOperationException(result.Error ?? "capture failed");
        }
        if (staging is null) return new { files = result.Files };
        var items = SetCommands.FinishImport(args, result, staging, source, Log);
        return new { files = items.Select(i => Path.Combine(Path.GetFullPath(set!), i.File)) };
    }

    [McpServerTool(Name = "add_file")]
    [Description("Imports an existing file into a preview set (capture set add).")]
    public static object AddFile(
        [Description("The preview set folder.")] string dir,
        [Description("The file(s) to import; without a tag each file becomes its own item.")] string[] file,
        [Description("One tag names one capture made of every file given.")] string? tag = null,
        [Description("A caption shown under the item.")] string? caption = null,
        [Description("Move instead of copy.")] bool move = false)
    {
        var store = new PreviewSetStore(log: Log);
        var source = new PreviewSource { Type = "file", Path = Path.GetFullPath(file[0]) };
        var items = store.Import(store.Resolve(dir), file, tag, caption, source, move ? ImportMode.Move : ImportMode.Copy);
        return new { items = items.Select(ToItem) };
    }

    [McpServerTool(Name = "show")]
    [Description("Returns a preview set's manifest (capture set show) as data, without launching a viewer.")]
    public static object Show([Description("The preview set folder.")] string dir) =>
        ToManifest(PreviewSetStore.Open(new PreviewSetStore(log: Log).Resolve(dir)));

    [McpServerTool(Name = "list_sets")]
    [Description("Lists known preview sets (capture set list), newest first.")]
    public static object ListSets(
        [Description("Only this repo's sets.")] string? repo = null,
        [Description("The most sets to return (default 20).")] int? limit = null)
    {
        var sets = new PreviewSetStore(log: Log).List(repo, limit ?? 20);
        return new
        {
            sets = sets.Select(s => new { dir = s.Directory, s.Id, s.Title, s.Repo, s.Ticket, s.Created, s.Status, items = s.Items.Count }),
        };
    }

    private static object ToManifest(PreviewSet set) => new
    {
        dir = set.Directory, set.Id, set.Title, set.Repo, set.Ticket, set.Created, set.Updated, set.Status,
        items = set.Items.Select(ToItem),
    };

    private static object ToItem(PreviewItem i) => new { i.File, i.Kind, i.Caption, i.At };
}
