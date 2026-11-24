using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace MindAttic.Capture.Sets;

/// <summary>How <see cref="PreviewLauncher.Show"/> got the set in front of the owner.</summary>
public enum PreviewShown
{
    /// <summary>Nothing could show it; print the path.</summary>
    None,
    /// <summary>Handed to the MediaViewer already running in this Windows session.</summary>
    Pipe,
    /// <summary>Started MindAttic.MediaViewer on it.</summary>
    Launched,
    /// <summary>Opened its first item with the shell's default app (Microsoft Photos for an image).</summary>
    Photos,
}

/// <summary>What a MediaViewer is asked to show: a set (or file) and the item to select.</summary>
public sealed record PreviewRequest(string Set, string? Select = null);

/// <summary>
/// The single-instance handoff between agents and MindAttic.MediaViewer: the running Viewer listens on a per-session
/// named pipe (<see cref="PipeName"/>, current user only); a client connects, writes one <see cref="PreviewRequest"/>
/// as UTF-8 JSON and closes. The Viewer comes to the front and shows the set.
/// </summary>
public static class PreviewHandoff
{
    /// <summary>The pipe the Viewer open in this Windows session listens on.</summary>
    public static string PipeName { get; } = $"MindAttic.MediaViewer.{Process.GetCurrentProcess().SessionId}";

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Hands <paramref name="request"/> to the running Viewer. False when none answers within <paramref name="timeout"/>.</summary>
    public static bool Send(PreviewRequest request, TimeSpan timeout, string? pipeName = null)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName ?? PipeName, PipeDirection.Out);
            pipe.Connect((int)timeout.TotalMilliseconds);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(request, PreviewJson.Options);
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The Viewer's side: listens until <paramref name="stop"/> is cancelled, calling <paramref name="received"/> with each
    /// request (on the thread pool: marshal to the UI thread yourself). Malformed or silent clients are dropped.
    /// </summary>
    public static async Task ListenAsync(Action<PreviewRequest> received, CancellationToken stop, string? pipeName = null)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName ?? PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                using var slow = CancellationTokenSource.CreateLinkedTokenSource(stop);
                slow.CancelAfter(ReadTimeout);
                string text;
                try { text = await reader.ReadToEndAsync(slow.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested) { continue; }
                PreviewRequest? request = null;
                try { request = JsonSerializer.Deserialize<PreviewRequest>(text, PreviewJson.Options); }
                catch (JsonException) { }
                if (request is { Set.Length: > 0 }) received(request);
            }
            catch (OperationCanceledException) { return; }
            catch (IOException)
            {
                try { await Task.Delay(500, stop).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            }
        }
    }
}

/// <summary>
/// Puts a set in front of the owner, best effort, in order: hand it to a running MindAttic.MediaViewer over
/// <see cref="PreviewHandoff"/>; start the Viewer on it; open its first item with the shell (Photos); else none.
/// </summary>
public static class PreviewLauncher
{
    /// <summary>Where run.ps1 deploys the Viewer.</summary>
    public const string DefaultViewerPath = @"C:\Apps\MindAttic.MediaViewer\MindAttic.MediaViewer.exe";

    /// <summary>The Viewer exe: <c>MINDATTIC_MEDIAVIEWER</c> when set, else <see cref="DefaultViewerPath"/>.</summary>
    public static string ViewerPath =>
        Environment.GetEnvironmentVariable("MINDATTIC_MEDIAVIEWER") is { Length: > 0 } custom ? custom : DefaultViewerPath;

    /// <summary>Shows <paramref name="setDir"/>, selecting <paramref name="select"/> (a file name in it) when given.</summary>
    public static PreviewShown Show(string setDir, string? select = null)
    {
        setDir = Path.GetFullPath(setDir);
        if (PreviewHandoff.Send(new PreviewRequest(setDir, select), TimeSpan.FromMilliseconds(300))) return PreviewShown.Pipe;
        if (!Environment.UserInteractive) return PreviewShown.None;

        if (File.Exists(ViewerPath))
        {
            var psi = new ProcessStartInfo(ViewerPath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(ViewerPath)! };
            psi.ArgumentList.Add(setDir);
            if (!string.IsNullOrEmpty(select))
            {
                psi.ArgumentList.Add("--select");
                psi.ArgumentList.Add(select!);
            }
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
