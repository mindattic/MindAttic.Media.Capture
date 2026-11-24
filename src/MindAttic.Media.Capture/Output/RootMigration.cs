using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MindAttic.Media.Capture.Output;

/// <summary>One item <see cref="RootMigration"/> moved, or left in place and why.</summary>
/// <param name="From">Where it is.</param>
/// <param name="To">Where it goes.</param>
/// <param name="Reason">Null for a move; why it stayed for a skip.</param>
public sealed record RootMove(string From, string To, string? Reason = null);

/// <summary>What one <see cref="RootMigration.Run"/> did (or, as a dry run, would do).</summary>
public sealed class RootMigrationReport
{
    /// <summary>The old root.</summary>
    public string From { get; init; } = "";
    /// <summary>The new root.</summary>
    public string To { get; init; } = "";
    /// <summary>False for a dry run: nothing on disk changed.</summary>
    public bool Applied { get; init; }
    /// <summary>Items moved (or that would be).</summary>
    public List<RootMove> Moved { get; } = [];
    /// <summary>Items left in the old root: their target exists, or they were locked.</summary>
    public List<RootMove> Skipped { get; } = [];
    /// <summary>Moved JSON files whose old-root paths could not be rewritten.</summary>
    public List<string> Warnings { get; } = [];
    /// <summary>The marker written in the old root; null on a dry run or when the old root is missing.</summary>
    public string? Marker { get; set; }
}

/// <summary>
/// Moves an old root's folders (one per app or repo) into a new root on the same volume. A folder whose target is
/// missing is renamed across whole; one whose target exists is merged a level down, child by child, and any child
/// whose target exists stays put. Nothing is deleted or overwritten. Absolute old-root paths inside moved JSON files
/// (capture sidecars, session manifests, tape.json) are rewritten to the new root; preview.json holds only file names.
/// </summary>
public static class RootMigration
{
    /// <summary>The marker left in the old root; never moved.</summary>
    public const string MarkerName = "README.md";

    /// <summary>Plans the move of <paramref name="from"/>'s children into <paramref name="to"/>; with
    /// <paramref name="apply"/>, makes it and writes the marker.</summary>
    public static RootMigrationReport Run(string from, string to, bool apply, Func<DateTimeOffset>? now = null)
    {
        from = Normalize(from);
        to = Normalize(to);
        var report = new RootMigrationReport { From = from, To = to, Applied = apply };
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            throw new CaptureException(CaptureError.InvalidArgument, $"Cannot migrate {from} into itself.");
        if (!Directory.Exists(from)) return report;

        foreach (var entry in Children(from))
        {
            var name = Path.GetFileName(entry);
            if (name.Equals(MarkerName, StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(to, name);
            if (!Exists(target))
                Move(entry, target, report);
            else if (Directory.Exists(entry) && Directory.Exists(target))
                foreach (var child in Children(entry))
                {
                    var childTarget = Path.Combine(target, Path.GetFileName(child));
                    if (Exists(childTarget)) report.Skipped.Add(new RootMove(child, childTarget, "target exists"));
                    else Move(child, childTarget, report);
                }
            else
                report.Skipped.Add(new RootMove(entry, target, "target exists"));
        }

        if (apply) report.Marker = WriteMarker(report, (now ?? (() => DateTimeOffset.Now))());
        return report;
    }

    private static void Move(string from, string to, RootMigrationReport report)
    {
        if (!report.Applied)
        {
            report.Moved.Add(new RootMove(from, to));
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (Directory.Exists(from)) Directory.Move(from, to);
            else File.Move(from, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report.Skipped.Add(new RootMove(from, to, ex.Message.Trim()));
            return;
        }
        report.Moved.Add(new RootMove(from, to));
        RewritePaths(to, report);
    }

    // JSON writes "R:\\Old\\Root\\..."; replace that prefix (whole path segments only) with the new root.
    private static void RewritePaths(string moved, RootMigrationReport report)
    {
        IEnumerable<string> files = Directory.Exists(moved) ? Directory.EnumerateFiles(moved, "*.json", SearchOption.AllDirectories)
            : moved.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? [moved]
            : Enumerable.Empty<string>();
        var oldRoot = report.From.Replace("\\", "\\\\");
        var newRoot = report.To.Replace("\\", "\\\\");
        var pattern = new Regex(Regex.Escape(oldRoot) + @"(?=\\\\|"")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var file in files)
        {
            try
            {
                var text = File.ReadAllText(file);
                if (!pattern.IsMatch(text)) continue;
                File.WriteAllText(file, pattern.Replace(text, _ => newRoot), new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                report.Warnings.Add($"{file}: {ex.Message.Trim()}");
            }
        }
    }

    private static string WriteMarker(RootMigrationReport report, DateTimeOffset at)
    {
        var sb = new StringBuilder()
            .Append("# Moved to ").Append(report.To).Append("\n\n")
            .Append("`capture migrate-roots` moved this folder's contents to `").Append(report.To).Append("` on ")
            .Append(at.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)).Append(".\n")
            .Append("New captures and previews go there; read it, not this folder.\n");
        if (report.Skipped.Count > 0)
        {
            sb.Append("\n## Left here\n\nThese stayed because their target already exists or they were in use:\n\n");
            foreach (var s in report.Skipped)
                sb.Append("- `").Append(Path.GetRelativePath(report.From, s.From)).Append("`: ").Append(s.Reason).Append('\n');
        }
        var path = Path.Combine(report.From, MarkerName);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    private static IEnumerable<string> Children(string dir) =>
        Directory.EnumerateFileSystemEntries(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

    private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');
}
