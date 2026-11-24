using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MindAttic.Media.Capture.Hosting;
using MindAttic.Media.Capture.Output;

namespace MindAttic.Media.Capture.Sets;

/// <summary>How <see cref="PreviewSetStore.Import"/> brings a file into a set.</summary>
public enum ImportMode
{
    /// <summary>Copy it, leaving the original.</summary>
    Copy,
    /// <summary>Move it (and its sidecar).</summary>
    Move,
}

/// <summary>
/// Preview sets under one root: <c>{root}\{Repo}\{yyyy-MM-ddTHH.mm.ss}_{TICKET}_{slug}\</c>, each holding
/// <c>NNN_{tag}.{ext}</c> files, their <c>{file}.json</c> sidecars and <c>preview.json</c>. Every change to a set's
/// files or manifest happens under a named mutex per set, and the manifest is written to <c>preview.json.tmp</c> then
/// swapped in with <see cref="File.Replace(string, string, string?)"/>, so readers never see half a manifest.
/// </summary>
public sealed class PreviewSetStore
{
    /// <summary>Where sets live by default.</summary>
    public const string DefaultRoot = @"R:\MindAttic\Media\Preview";

    /// <summary>The sets root before previews moved under R:\MindAttic\Media.</summary>
    public const string LegacyRoot = @"R:\MindAttic\Preview"; // LEGACY-ROOT

    /// <summary>Used when <see cref="DefaultRoot"/>'s drive is missing.</summary>
    public static string FallbackRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MindAttic", "Preview");

    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

    private readonly string _configuredRoot;
    private readonly ICaptureLog _log;
    private readonly Func<DateTimeOffset> _now;
    private string? _root;

    /// <summary>A store over <paramref name="root"/> (default <see cref="DefaultRoot"/>, falling back to <see cref="FallbackRoot"/>).</summary>
    public PreviewSetStore(string? root = null, ICaptureLog? log = null, Func<DateTimeOffset>? now = null)
    {
        _configuredRoot = string.IsNullOrWhiteSpace(root) ? DefaultRoot : root!;
        _log = log ?? new TraceCaptureLog();
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>The root in use, created on first use.</summary>
    public string Root => _root ??= OutputManager.ResolveRoot(_configuredRoot, FallbackRoot, _log);

    /// <summary>The old root readers also search, only while the root is the default; null for any other root.</summary>
    internal string? LegacyReadRoot { get; init; } // LEGACY-ROOT

    /// <summary><see cref="LegacyRoot"/> for the default root, else null: a configured root never reads the old one.</summary>
    public static string? LegacyRootFor(string? root) =>
        string.IsNullOrWhiteSpace(root) || string.Equals(Path.GetFullPath(root!).TrimEnd('\\', '/'), DefaultRoot, StringComparison.OrdinalIgnoreCase)
            ? LegacyRoot // LEGACY-ROOT
            : null;

    // Readers look under Root first, then the legacy root; writers only ever use Root.
    private IEnumerable<string> ReadRoots()
    {
        yield return Root;
        var legacy = LegacyReadRoot ?? LegacyRootFor(_configuredRoot); // LEGACY-ROOT
        if (legacy is not null && Directory.Exists(legacy) && // LEGACY-ROOT
            !string.Equals(Path.GetFullPath(legacy).TrimEnd('\\', '/'), Path.GetFullPath(Root).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            yield return legacy; // LEGACY-ROOT
    }

    /// <summary>
    /// The set folder <paramref name="dirOrId"/> names: an existing folder as is, else a set ID (its folder name)
    /// found under any repo of the root, or of the legacy root while that is read. Unknown names come back unchanged.
    /// </summary>
    public string Resolve(string dirOrId)
    {
        if (Directory.Exists(dirOrId)) return Path.GetFullPath(dirOrId);
        if (dirOrId.IndexOfAny(['\\', '/', ':']) >= 0) return dirOrId;
        foreach (var root in ReadRoots().Where(Directory.Exists))
            foreach (var repoDir in Directory.EnumerateDirectories(root))
                if (Path.Combine(repoDir, dirOrId) is var dir && Directory.Exists(dir)) return dir;
        return dirOrId;
    }

    /// <summary>Makes a new, empty set and writes its manifest.</summary>
    /// <param name="title">What it shows; also its folder's slug.</param>
    /// <param name="ticket">The ticket it is for.</param>
    /// <param name="repo">Its repo folder; default the git repo of the current directory, else "Desktop".</param>
    /// <param name="agent">Who makes it; default <see cref="PreviewAgent.FromEnvironment"/>.</param>
    public PreviewSet Create(string title, string? ticket = null, string? repo = null, PreviewAgent? agent = null)
    {
        var now = _now();
        repo = OutputManager.Sanitize(string.IsNullOrWhiteSpace(repo) ? CaptureNaming.RepoName(Environment.CurrentDirectory) ?? "Desktop" : repo!);
        if (repo.Length == 0) repo = "Desktop";
        var name = now.ToString("yyyy-MM-dd'T'HH.mm.ss", CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(ticket)) name += "_" + OutputManager.SanitizeTag(ticket);
        name += "_" + Slug(title);
        var parent = Path.Combine(Root, repo);
        Directory.CreateDirectory(parent);
        var dir = Path.Combine(parent, name);
        while (Directory.Exists(dir)) dir = Path.Combine(parent, $"{name}-{RandomNumberGenerator.GetHexString(4, lowercase: true)}");
        Directory.CreateDirectory(dir);
        var set = new PreviewSet
        {
            Id = Path.GetFileName(dir), Title = title.Trim(), Repo = repo, Ticket = string.IsNullOrWhiteSpace(ticket) ? null : ticket!.Trim(),
            Agent = agent ?? PreviewAgent.FromEnvironment(), Created = now, Updated = now, Directory = dir,
        };
        using (Lock(dir)) WriteManifest(set);
        return set;
    }

    /// <summary>
    /// The set in <paramref name="dir"/>. The folder is the truth: media files the manifest does not list are added
    /// after its items (not saved), and a folder without a manifest still opens, its media in name order.
    /// </summary>
    public static PreviewSet Open(string dir)
    {
        dir = Path.GetFullPath(dir);
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException($"No preview set at {dir}.");
        var set = ReadManifest(dir) ?? new PreviewSet { Id = Path.GetFileName(dir), Title = Path.GetFileName(dir), Created = Directory.GetCreationTime(dir) };
        set.Directory = dir;
        var listed = new HashSet<string>(set.Items.Select(i => i.File), StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(dir).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (file is null || listed.Contains(file) || !MediaKinds.IsShown(file) || file.Equals(PreviewSet.ManifestName, StringComparison.OrdinalIgnoreCase)) continue;
            var path = Path.Combine(dir, file);
            set.Items.Add(new PreviewItem { File = file, Kind = MediaKinds.Detect(path), At = File.GetLastWriteTime(path) });
        }
        return set;
    }

    /// <summary>Sets under the root (and the legacy root while it is read), newest first: one repo's, or every repo's.
    /// A set in both roots is listed once, from the root.</summary>
    public IReadOnlyList<PreviewSet> List(string? repo = null, int? limit = null)
    {
        var sets = new List<PreviewSet>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in ReadRoots())
        {
            var repoDirs = string.IsNullOrWhiteSpace(repo) ? Directory.EnumerateDirectories(root)
                : [Path.Combine(root, OutputManager.Sanitize(repo!))];
            foreach (var repoDir in repoDirs.Where(Directory.Exists))
            {
                foreach (var dir in Directory.EnumerateDirectories(repoDir))
                {
                    if (!seen.Add(Path.Combine(Path.GetFileName(repoDir), Path.GetFileName(dir)))) continue;
                    if (ReadManifest(dir) is not { } set) continue;
                    set.Directory = dir;
                    sets.Add(set);
                }
            }
        }
        IEnumerable<PreviewSet> ordered = sets.OrderByDescending(s => s.Created);
        if (limit is > 0) ordered = ordered.Take(limit.Value);
        return ordered.ToList();
    }

    /// <summary>The next file number in <paramref name="dir"/>: one more than the highest <c>NNN_</c> prefix, from 1.</summary>
    public static int NextIndex(string dir)
    {
        var max = 0;
        foreach (var path in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(path);
            var digits = 0;
            while (digits < name.Length && char.IsAsciiDigit(name[digits])) digits++;
            if (digits > 0 && digits < name.Length && name[digits] == '_' &&
                int.TryParse(name.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                max = Math.Max(max, n);
        }
        return max + 1;
    }

    /// <summary>
    /// Brings one capture's files into the set as <c>NNN_{tag}.{ext}</c>, all sharing the next number (a recording's
    /// .mkv and .mp4 stay one item to a viewer). Each file's <c>{file}.json</c> sidecar comes along with its
    /// <c>files</c> paths rewritten to the new names. Returns the items added to the manifest, one per file.
    /// </summary>
    /// <param name="dir">The set folder.</param>
    /// <param name="files">The capture's files; the first names the tag when <paramref name="tag"/> is null.</param>
    /// <param name="tag">The name after the number.</param>
    /// <param name="caption">Shown under each item.</param>
    /// <param name="source">How it was captured.</param>
    /// <param name="mode">Copy (default) or move.</param>
    public IReadOnlyList<PreviewItem> Import(string dir, IReadOnlyList<string> files, string? tag = null, string? caption = null,
        PreviewSource? source = null, ImportMode mode = ImportMode.Copy)
    {
        if (files.Count == 0) return [];
        dir = Path.GetFullPath(dir);
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException($"No preview set at {dir}.");
        var sources = files.Select(Path.GetFullPath).ToList();
        foreach (var f in sources)
            if (!File.Exists(f)) throw new FileNotFoundException($"No file at {f}.", f);
        tag = OutputManager.SanitizeTag(tag ?? Path.GetFileNameWithoutExtension(sources[0]));

        using var _ = Lock(dir);
        var stem = $"{NextIndex(dir):000}_{tag}";
        var moves = new List<(string From, string To)>();
        foreach (var from in sources)
        {
            var ext = Path.GetExtension(from).ToLowerInvariant();
            var to = Path.Combine(dir, stem + ext);
            for (var n = 2; File.Exists(to) || moves.Any(m => m.To.Equals(to, StringComparison.OrdinalIgnoreCase)); n++)
                to = Path.Combine(dir, $"{stem}-{n}{ext}");
            moves.Add((from, to));
        }

        var now = _now();
        var items = new List<PreviewItem>();
        foreach (var (from, to) in moves)
        {
            if (mode == ImportMode.Move) File.Move(from, to);
            else File.Copy(from, to);
            BringSidecar(from, to, moves, mode);
            items.Add(new PreviewItem { File = Path.GetFileName(to), Kind = MediaKinds.Detect(to), Caption = caption, At = now, Source = source });
        }

        var set = ReadManifest(dir) ?? NewManifestFor(dir, now);
        set.Items.AddRange(items);
        set.Updated = now;
        set.Directory = dir;
        WriteManifest(set);
        return items;
    }

    /// <summary>Changes a set's manifest under its lock (status, title, captions, order) and saves it.</summary>
    public PreviewSet Update(string dir, Action<PreviewSet> change)
    {
        dir = Path.GetFullPath(dir);
        using var _ = Lock(dir);
        var set = ReadManifest(dir) ?? NewManifestFor(dir, _now());
        set.Directory = dir;
        change(set);
        set.Updated = _now();
        WriteManifest(set);
        return set;
    }

    private static PreviewSet NewManifestFor(string dir, DateTimeOffset now) => new()
    {
        Id = Path.GetFileName(dir), Title = Path.GetFileName(dir), Repo = Path.GetFileName(Path.GetDirectoryName(dir)),
        Created = now, Updated = now,
    };

    private static PreviewSet? ReadManifest(string dir)
    {
        var path = Path.Combine(dir, PreviewSet.ManifestName);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.Exists(path) ? PreviewJson.Deserialize(File.ReadAllText(path)) : null;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50); // a writer is swapping the file in
            }
        }
    }

    private static void WriteManifest(PreviewSet set)
    {
        var path = Path.Combine(set.Directory, PreviewSet.ManifestName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, PreviewJson.Serialize(set), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    // The sidecar follows its file; its "files" list names the new paths of every file in this import.
    private void BringSidecar(string from, string to, List<(string From, string To)> moves, ImportMode mode)
    {
        var sidecar = from + ".json";
        if (!File.Exists(sidecar)) return;
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(sidecar));
            if (node?["files"] is JsonArray list)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var old = list[i]?.GetValue<string>();
                    var match = moves.FirstOrDefault(m => string.Equals(m.From, old, StringComparison.OrdinalIgnoreCase));
                    if (match.To is not null) list[i] = match.To;
                }
            }
            File.WriteAllText(to + ".json", node?.ToJsonString(PreviewJson.Options) ?? "{}", new UTF8Encoding(false));
            if (mode == ImportMode.Move) File.Delete(sidecar);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            _log.Log(CaptureLogLevel.Warning, $"Sidecar {sidecar} is not valid JSON; copied as is.", ex);
            File.Copy(sidecar, to + ".json", overwrite: true);
            if (mode == ImportMode.Move) File.Delete(sidecar);
        }
    }

    /// <summary>The set's lock: <c>Global\MindAttic.Preview.{hash of its full path}</c>, shared by every process.</summary>
    public static IDisposable Lock(string dir)
    {
        var key = Path.GetFullPath(dir).TrimEnd('\\', '/').ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        var mutex = new Mutex(false, $@"Global\MindAttic.Preview.{hash}");
        try
        {
            if (!mutex.WaitOne(LockTimeout))
            {
                mutex.Dispose();
                throw new TimeoutException($"Preview set {dir} stayed locked for {LockTimeout.TotalSeconds:0}s.");
            }
        }
        catch (AbandonedMutexException)
        {
            // The holder died mid-write; the manifest swap is atomic, so carry on holding it.
        }
        return new Held(mutex);
    }

    private sealed class Held(Mutex mutex) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    /// <summary>A folder slug from a title: lower case, [a-z0-9.-], at most 40 characters.</summary>
    public static string Slug(string title)
    {
        var tag = OutputManager.SanitizeTag(title).ToLowerInvariant().Replace('_', '-');
        while (tag.Contains("--", StringComparison.Ordinal)) tag = tag.Replace("--", "-", StringComparison.Ordinal);
        if (tag.Length > 40) tag = tag[..40].TrimEnd('-', '.');
        return tag.Length == 0 ? "set" : tag;
    }
}
