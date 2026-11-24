using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindAttic.Capture.Sets;

/// <summary>One preview set: the contents of its <c>preview.json</c> (schema <see cref="PreviewSet.SchemaV1"/>).</summary>
public sealed class PreviewSet
{
    /// <summary>The schema this library writes and reads.</summary>
    public const string SchemaV1 = "mindattic.preview/1";

    /// <summary>The manifest's file name inside a set folder.</summary>
    public const string ManifestName = "preview.json";

    /// <summary>Always <see cref="SchemaV1"/> when written by this library.</summary>
    public string Schema { get; set; } = SchemaV1;
    /// <summary>The set folder's name: <c>{yyyy-MM-ddTHH.mm.ss}_{TICKET}_{slug}</c>.</summary>
    public string Id { get; set; } = "";
    /// <summary>What the set shows, in a few words.</summary>
    public string Title { get; set; } = "";
    /// <summary>The repo it belongs to (the folder under the preview root).</summary>
    public string? Repo { get; set; }
    /// <summary>The ticket it was captured for.</summary>
    public string? Ticket { get; set; }
    /// <summary>Who made it.</summary>
    public PreviewAgent? Agent { get; set; }
    /// <summary>When the set was made.</summary>
    public DateTimeOffset Created { get; set; }
    /// <summary>When its manifest last changed.</summary>
    public DateTimeOffset Updated { get; set; }
    /// <summary><see cref="StatusOpen"/> while items are still arriving, <see cref="StatusDone"/> after.</summary>
    public string Status { get; set; } = StatusOpen;
    /// <summary>The set's media in display order.</summary>
    public List<PreviewItem> Items { get; set; } = [];

    /// <summary>Status while items are still arriving.</summary>
    public const string StatusOpen = "open";
    /// <summary>Status once the set is complete.</summary>
    public const string StatusDone = "done";

    /// <summary>The set folder this was read from or written to; not serialized.</summary>
    [JsonIgnore]
    public string Directory { get; set; } = "";
}

/// <summary>The agent that made a set.</summary>
public sealed class PreviewAgent
{
    /// <summary>The CLI: claude, codex, kimi, agy...</summary>
    public string? Client { get; set; }
    /// <summary>The CLI's session id.</summary>
    public string? Session { get; set; }
    /// <summary>The login the CLI runs as.</summary>
    public string? Login { get; set; }

    /// <summary>What the environment says: the client from CLAUDECODE / CODEX_HOME, the login from the user name.</summary>
    public static PreviewAgent FromEnvironment() => new()
    {
        Client = Environment.GetEnvironmentVariable("CLAUDECODE") is { Length: > 0 } ? "claude"
            : Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } ? "codex"
            : null,
        Login = Environment.UserName,
    };
}

/// <summary>One file in a set.</summary>
public sealed class PreviewItem
{
    /// <summary>The file's name inside the set folder: <c>NNN_{tag}.{ext}</c>.</summary>
    public string File { get; set; } = "";
    /// <summary>One of <see cref="MediaKinds"/>' kinds.</summary>
    public string Kind { get; set; } = MediaKinds.Other;
    /// <summary>A line shown under it.</summary>
    public string? Caption { get; set; }
    /// <summary>When it was added.</summary>
    public DateTimeOffset? At { get; set; }
    /// <summary>Where it came from.</summary>
    public PreviewSource? Source { get; set; }
    /// <summary>A still shown before a video plays (a hidden file in the same folder).</summary>
    public string? Poster { get; set; }
}

/// <summary>How an item was captured.</summary>
public sealed class PreviewSource
{
    /// <summary>web, screen, window or file.</summary>
    public string Type { get; set; } = "file";
    /// <summary>The page captured (web).</summary>
    public string? Url { get; set; }
    /// <summary>The page's viewport, <c>1440x900</c> (web).</summary>
    public string? Viewport { get; set; }
    /// <summary>The emulated device (web).</summary>
    public string? Device { get; set; }
    /// <summary>The screen target: a window title, monitor or region (screen, window).</summary>
    public string? Target { get; set; }
    /// <summary>The original path (file).</summary>
    public string? Path { get; set; }
}

/// <summary>The JSON shape of <c>preview.json</c>: camelCase, indented, nulls left out, read case-insensitively.</summary>
public static class PreviewJson
{
    /// <summary>The serializer options for manifests.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>A manifest's JSON.</summary>
    public static string Serialize(PreviewSet set) => JsonSerializer.Serialize(set, Options);

    /// <summary>Parses a manifest; null when the text is not one.</summary>
    public static PreviewSet? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PreviewSet>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
