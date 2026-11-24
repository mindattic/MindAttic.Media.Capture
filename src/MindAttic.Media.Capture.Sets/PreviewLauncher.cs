using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace MindAttic.Media.Capture.Sets;

/// <summary>How <see cref="PreviewLauncher.Show"/> got the set in front of the owner.</summary>
public enum PreviewShown
{
    /// <summary>Nothing could show it; print the path.</summary>
    None,
    /// <summary>Handed to the Media.Viewer already running in this Windows session.</summary>
    Pipe,
    /// <summary>Started MindAttic.Media.Viewer on it.</summary>
    Launched,
    /// <summary>Opened its first item with the shell's default app (Microsoft Photos for an image).</summary>
    Photos,
}

/// <summary>What a Media.Viewer is asked to show: a set (or file) and the item to select.</summary>
public sealed record PreviewRequest(string Set, string? Select = null)
{
    /// <summary>The Viewer command line that asks for this: <c>&lt;set&gt; [--select FILE]</c>.</summary>
    public string[] ToArgs() => string.IsNullOrEmpty(Select) ? [Set] : [Set, "--select", Select!];
}

/// <summary>
/// The Media.Viewer's own single-instance handoff (M.MVIEW-T7): the running Viewer listens on <see cref="PipeName"/>
/// (this Windows user only) for a later launch's command line, one argument per UTF-8 line ended by an empty line, and
/// shows it as if it had been launched with it. A Viewer from before the rename listens on <see cref="LegacyPipeName"/>.
/// </summary>
public static class PreviewHandoff
{
    /// <summary>The pipe the running Viewer listens on for this Windows user.</summary>
    public static string PipeName { get; } = "mindattic-media-viewer-cli-" + UserPart(Environment.UserName);

    /// <summary>The pipe of the Viewer before its rename to MindAttic.Media.Viewer, still listened on for one release.</summary>
    public static string LegacyPipeName { get; } = "mindattic-mediaviewer-cli-" + UserPart(Environment.UserName);

    /// <summary>What goes down the pipe for <paramref name="request"/>: each argument on its own line, then an empty line.</summary>
    public static string Payload(PreviewRequest request) =>
        string.Concat(request.ToArgs().Select(a => a + "\n")) + "\n";

    /// <summary>
    /// Hands <paramref name="request"/> to the running Viewer on <paramref name="pipeName"/>, or else on
    /// <see cref="PipeName"/> then <see cref="LegacyPipeName"/>. False when none answers within <paramref name="timeout"/>.
    /// </summary>
    public static bool Send(PreviewRequest request, TimeSpan timeout, string? pipeName = null) =>
        pipeName is not null ? SendTo(pipeName, request, timeout) : SendTo(PipeName, request, timeout) || SendTo(LegacyPipeName, request, timeout);

    private static bool SendTo(string pipeName, PreviewRequest request, TimeSpan timeout)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect((int)timeout.TotalMilliseconds);
            var bytes = new UTF8Encoding(false).GetBytes(Payload(request));
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string UserPart(string userName) => new(userName.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
}

/// <summary>
/// Puts a set in front of the owner, best effort, in order: hand it to a running MindAttic.Media.Viewer over
/// <see cref="PreviewHandoff"/>; start the Viewer on it; open its first item with the shell (Photos); else none.
/// </summary>
public static class PreviewLauncher
{
    /// <summary>Where run.ps1 deploys the Viewer.</summary>
    public const string DefaultViewerPath = @"C:\Apps\MindAttic.Media.Viewer\MindAttic.Media.Viewer.exe";

    /// <summary>Where the Viewer was deployed before its rename to MindAttic.Media.Viewer.</summary>
    public const string LegacyViewerPath = @"C:\Apps\MindAttic.MediaViewer\MindAttic.MediaViewer.exe";

    /// <summary>
    /// The Viewer exe: <c>MINDATTIC_MEDIA_VIEWER</c>, else <c>MINDATTIC_MEDIAVIEWER</c>, when set; else
    /// <see cref="DefaultViewerPath"/>, or <see cref="LegacyViewerPath"/> when only that one is installed.
    /// </summary>
    public static string ViewerPath =>
        Environment.GetEnvironmentVariable("MINDATTIC_MEDIA_VIEWER") is { Length: > 0 } custom ? custom
        : Environment.GetEnvironmentVariable("MINDATTIC_MEDIAVIEWER") is { Length: > 0 } legacy ? legacy
        : !File.Exists(DefaultViewerPath) && File.Exists(LegacyViewerPath) ? LegacyViewerPath
        : DefaultViewerPath;

    /// <summary>Shows <paramref name="setDir"/>, selecting <paramref name="select"/> (a file name in it) when given.</summary>
    public static PreviewShown Show(string setDir, string? select = null)
    {
        setDir = Path.GetFullPath(setDir);
        var request = new PreviewRequest(setDir, select);
        if (PreviewHandoff.Send(request, TimeSpan.FromMilliseconds(300))) return PreviewShown.Pipe;
        if (!Environment.UserInteractive) return PreviewShown.None;

        var viewer = ViewerPath;
        if (File.Exists(viewer))
        {
            var psi = new ProcessStartInfo(viewer) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(viewer)! };
            foreach (var arg in request.ToArgs()) psi.ArgumentList.Add(arg);
            if (TryStart(psi)) return PreviewShown.Launched;
        }

        var first = FirstItem(setDir, select);
        if (first is not null && TryStart(new ProcessStartInfo(first) { UseShellExecute = true })) return PreviewShown.Photos;
        return PreviewShown.None;
    }

    private static string? FirstItem(string setDir, string? select)
    {
        if (!string.IsNullOrEmpty(select) && File.Exists(Path.Combine(setDir, select!))) return Path.Combine(setDir, select!);
        try
        {
            var set = PreviewSetStore.Open(setDir);
            var item = set.Items.FirstOrDefault(i => i.Kind is MediaKinds.Image or MediaKinds.Animated) ?? set.Items.FirstOrDefault();
            return item is null ? null : Path.Combine(setDir, item.File);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryStart(ProcessStartInfo psi)
    {
        try
        {
            using var process = Process.Start(psi);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }
}
