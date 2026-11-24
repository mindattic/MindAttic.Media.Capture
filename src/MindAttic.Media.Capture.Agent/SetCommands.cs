using System.Text.Json;
using MindAttic.Media.Capture.Hosting;
using MindAttic.Media.Capture.Sets;

namespace MindAttic.Media.Capture.Agent;

/// <summary>
/// <c>capture set new|add|list|show</c>, and <c>--set DIR</c> on shot/record: the engine writes into a staging folder
/// inside the set, then the files are imported as the set's next <c>NNN_tag</c> and the staging folder removed.
/// </summary>
internal static class SetCommands
{
    public static int Run(AgentArgs args, ICaptureLog log)
    {
        var sub = args.Positional.FirstOrDefault()?.ToLowerInvariant();
        var store = new PreviewSetStore(args.Get("root"), log);
        return sub switch
        {
            "new" => New(args, store),
            "add" => Add(args, store),
            "list" => List(args, store),
            "show" => Show(args, store),
            _ => throw new CaptureException(CaptureError.InvalidArgument, "set needs new, add, list or show (see `capture help`)."),
        };
    }

    private static int New(AgentArgs args, PreviewSetStore store)
    {
        var title = args.Get("title") ?? args.Positional.ElementAtOrDefault(1)
            ?? throw new CaptureException(CaptureError.InvalidArgument, "set new needs --title.");
        var set = store.Create(title, args.Get("ticket"), args.Get("repo"));
        Console.WriteLine(args.Has("json") ? ManifestJson(set) : set.Directory);
        return 0;
    }

    /// <summary>The set's manifest with its folder as <c>dir</c> first, the name <c>set list --json</c> and MCP use;
    /// <c>preview.json</c> itself never stores the folder.</summary>
    internal static string ManifestJson(PreviewSet set)
    {
        var node = JsonSerializer.SerializeToNode(set, PreviewJson.Options)!.AsObject();
        node.Insert(0, "dir", set.Directory);
        return node.ToJsonString(PreviewJson.Options);
    }

    private static int Add(AgentArgs args, PreviewSetStore store)
    {
        if (args.Positional.Count < 3) throw new CaptureException(CaptureError.InvalidArgument, "set add needs <dir> <file...>.");
        var dir = store.Resolve(args.Positional[1]);
        var mode = args.Has("move") ? ImportMode.Move : ImportMode.Copy;
        var files = args.Positional.Skip(2).ToList();
        // One tag names one capture; without --tag every file is its own item, named after itself.
        var groups = args.Get("tag") is not null ? new List<List<string>> { files } : files.Select(f => new List<string> { f }).ToList();
        foreach (var group in groups)
        {
            var source = new PreviewSource { Type = "file", Path = Path.GetFullPath(group[0]) };
            foreach (var item in store.Import(dir, group, args.Get("tag"), args.Get("caption"), source, mode))
                Console.WriteLine(Path.Combine(Path.GetFullPath(dir), item.File));
        }
        return 0;
    }

    private static int List(AgentArgs args, PreviewSetStore store)
    {
        var sets = store.List(args.Get("repo"), args.GetInt("limit") ?? 20);
        if (args.Has("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(sets.Select(s => new { dir = s.Directory, s.Id, s.Title, s.Repo, s.Ticket, s.Created, s.Status, items = s.Items.Count }), PreviewJson.Options));
            return 0;
        }
        foreach (var s in sets)
            Console.WriteLine($"{s.Created:yyyy-MM-dd HH:mm}  {s.Items.Count,3} item(s)  {s.Title}  {s.Directory}");
        return 0;
    }

    private static int Show(AgentArgs args, PreviewSetStore store)
    {
        var dir = store.Resolve(args.Positional.ElementAtOrDefault(1) ?? throw new CaptureException(CaptureError.InvalidArgument, "set show needs <dir>."));
        PreviewSet set;
        try
        {
            set = PreviewSetStore.Open(dir);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new CaptureException(CaptureError.InvalidArgument, ex.Message, ex);
        }
        Console.Error.WriteLine($"{set.Title}  ({set.Items.Count} item(s))  {set.Directory}");
        foreach (var item in set.Items)
            Console.Error.WriteLine($"  {item.File}  [{item.Kind}]{(item.Caption is null ? "" : "  " + item.Caption)}");
        var shown = args.Has("no-launch") ? PreviewShown.None : PreviewLauncher.Show(set.Directory, args.Get("select"));
        Console.WriteLine(shown.ToString().ToLowerInvariant());
        return 0;
    }

    /// <summary>For <c>--set DIR</c>: points the engine at a fresh staging folder inside the set and returns it.</summary>
    public static string? Stage(AgentArgs args, CaptureOptions o)
    {
        if (args.Get("set") is not { } set) return null;
        set = Path.GetFullPath(set);
        if (!Directory.Exists(set)) throw new CaptureException(CaptureError.InvalidArgument, $"No preview set at {set} (make one with `capture set new`).");
        var staging = Path.Combine(set, ".staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        o.OutputRoot = staging;
        o.FallbackRoot = staging;
        return staging;
    }

    /// <summary>Imports a finished capture from its staging folder into the set, prints the new paths, removes the staging folder.</summary>
    public static int Finish(AgentArgs args, CaptureResult result, string staging, PreviewSource source, ICaptureLog log)
    {
        if (!result.Succeeded)
        {
            CleanupStaging(staging);
            Console.Error.WriteLine($"error: {result.Error}");
            return (int)CaptureError.CaptureFailed;
        }
        var set = Path.GetFullPath(args.Require("set"));
        foreach (var item in FinishImport(args, result, staging, source, log)) Console.WriteLine(Path.Combine(set, item.File));
        return 0;
    }

    /// <summary>Removes a staging folder <see cref="Stage"/> made, for a capture that never reached <see cref="FinishImport"/>.</summary>
    public static void CleanupStaging(string staging)
    {
        TryDelete(staging);
        TryDelete(Path.GetDirectoryName(staging)!, onlyIfEmpty: true);
    }

    /// <summary>Imports a finished, successful capture from its staging folder into the set and removes the staging
    /// folder; used by <see cref="Finish"/> and directly by <c>capture mcp</c>'s capture_screen tool, which returns
    /// the items as structured data instead of console lines.</summary>
    public static IReadOnlyList<PreviewItem> FinishImport(AgentArgs args, CaptureResult result, string staging, PreviewSource source, ICaptureLog log)
    {
        var set = Path.GetFullPath(args.Require("set"));
        try
        {
            var store = new PreviewSetStore(log: log);
            return store.Import(set, result.Files.ToList(), args.Get("tag") ?? result.Tag, args.Get("caption"), source, ImportMode.Move);
        }
        finally
        {
            TryDelete(staging);
            TryDelete(Path.GetDirectoryName(staging)!, onlyIfEmpty: true);
        }
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
