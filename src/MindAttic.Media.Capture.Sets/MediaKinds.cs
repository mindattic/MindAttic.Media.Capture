namespace MindAttic.Media.Capture.Sets;

/// <summary>
/// What a file in a set is, from its name: image, animated, video, audio, hidden (sidecars, thumbnails, logs) or
/// other (listed, opened with the shell). <see cref="Detect"/> also reads the header of a .png or .webp to tell an
/// animated one from a still.
/// </summary>
public static class MediaKinds
{
    /// <summary>A still image.</summary>
    public const string Image = "image";
    /// <summary>An animated image: gif, apng, animated webp.</summary>
    public const string Animated = "animated";
    /// <summary>A video.</summary>
    public const string Video = "video";
    /// <summary>A sound or a song.</summary>
    public const string Audio = "audio";
    /// <summary>Not shown: sidecars, posters, logs, temp files.</summary>
    public const string Hidden = "hidden";
    /// <summary>Anything else: listed, opened with the shell.</summary>
    public const string Other = "other";

    private static readonly string[] ImageExtensions =
    [
        // common (WIC)
        "png", "jpg", "jpeg", "jpe", "jfif", "bmp", "dib", "tif", "tiff", "ico", "cur", "wdp", "jxr", "hdp",
        // modern
        "webp", "avif", "heic", "heif", "jxl", "qoi",
        // editors and games
        "psd", "psb", "xcf", "kra", "ora", "tga", "dds", "exr", "hdr", "pic", "pcx", "ppm", "pgm", "pbm", "pnm", "pam",
        "sgi", "rgb", "ras", "xbm", "xpm", "wbmp", "jp2", "j2k", "jpf", "jpx", "fits", "fit",
        // vector (rasterised)
        "svg", "svgz", "emf", "wmf", "eps", "ai",
        // camera raw
        "raw", "dng", "cr2", "cr3", "crw", "nef", "nrw", "arw", "srf", "sr2", "orf", "rw2", "raf", "pef", "srw", "x3f",
        "3fr", "erf", "kdc", "mrw", "mos", "iiq", "rwl",
    ];

    private static readonly string[] AnimatedExtensions = ["gif", "apng", "mng"];

    private static readonly string[] VideoExtensions =
    [
        "mp4", "m4v", "mkv", "webm", "avi", "mov", "qt", "flv", "f4v", "wmv", "asf", "ts", "mts", "m2ts", "m2t", "tp",
        "mpg", "mpeg", "mpe", "m1v", "m2v", "mpv", "vob", "ogv", "3gp", "3g2", "divx", "xvid", "rm", "rmvb", "dv",
        "mxf", "nut", "y4m", "h264", "h265", "hevc", "264", "265", "ivf", "amv", "mjpeg", "mjpg",
    ];

    private static readonly string[] AudioExtensions =
    [
        "mp3", "flac", "wav", "wave", "ogg", "oga", "opus", "aac", "m4a", "m4b", "wma", "aif", "aiff", "aifc", "ape",
        "alac", "wv", "tta", "mpc", "ac3", "eac3", "dts", "amr", "au", "snd", "caf", "spx", "mka", "mp2", "mpa", "ra",
        "dsf", "dff", "mid", "midi", "kar", "rmi", "mod", "xm", "it", "s3m", "w64", "voc", "gsm",
    ];

    /// <summary>Kind by extension (lower case, no dot) for every extension this table knows.</summary>
    public static IReadOnlyDictionary<string, string> Extensions { get; } = Build();

    private static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in ImageExtensions) map[e] = Image;
        foreach (var e in AnimatedExtensions) map[e] = Animated;
        foreach (var e in VideoExtensions) map[e] = Video;
        foreach (var e in AudioExtensions) map[e] = Audio;
        foreach (var e in new[] { "json", "log", "tmp", "partial", "crdownload", "part", "lock" }) map[e] = Hidden;
        return map;
    }

    /// <summary>The kind of <paramref name="path"/> from its name alone.</summary>
    public static string Of(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Length == 0) return Other;
        var lower = name.ToLowerInvariant();
        if (lower.StartsWith('.') || lower is "desktop.ini" or "thumbs.db" or "folder.jpg" ||
            lower.EndsWith(".thumb.jpg", StringComparison.Ordinal) || lower.EndsWith(".thumb.png", StringComparison.Ordinal) ||
            lower.EndsWith(".stats.csv", StringComparison.Ordinal) || lower.EndsWith(".json.tmp", StringComparison.Ordinal))
            return Hidden;
        var ext = Path.GetExtension(lower).TrimStart('.');
        return Extensions.TryGetValue(ext, out var kind) ? kind : Other;
    }

    /// <summary>Like <see cref="Of"/>, but a .png or .webp whose header says it is animated is <see cref="Animated"/>.</summary>
    public static string Detect(string path)
    {
        var kind = Of(path);
        if (kind != Image) return kind;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".png" or ".webp")) return kind;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = new byte[Math.Min(stream.Length, 4096)];
            stream.ReadExactly(head);
            return (ext == ".png" ? IsApng(head) : IsAnimatedWebP(head)) ? Animated : kind;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return kind;
        }
    }

    /// <summary>True for a file a viewer shows: anything but <see cref="Hidden"/>.</summary>
    public static bool IsShown(string path) => Of(path) != Hidden;

    // APNG: an acTL chunk before the first IDAT.
    private static bool IsApng(ReadOnlySpan<byte> head)
    {
        var acTL = "acTL"u8;
        var idat = "IDAT"u8;
        var a = head.IndexOf(acTL);
        var d = head.IndexOf(idat);
        return a >= 0 && (d < 0 || a < d);
    }

    // Animated WebP: RIFF....WEBP then a VP8X chunk whose flags byte has the animation bit (0x02).
    private static bool IsAnimatedWebP(ReadOnlySpan<byte> head) =>
        head.Length >= 21 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8) &&
        head[12..16].SequenceEqual("VP8X"u8) && (head[20] & 0x02) != 0;
}
