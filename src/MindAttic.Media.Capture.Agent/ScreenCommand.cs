using MindAttic.Capture.Hosting;
using MindAttic.Capture.Sets;
using MindAttic.Capture.Windows;
using MindAttic.Capture.Windows.Native;

namespace MindAttic.Capture.Agent;

/// <summary>
/// <c>capture shot|screenshot &lt;target&gt;</c>: one still from the desktop capture engine, optionally into a
/// preview set with <c>--set DIR</c> (see <see cref="SetCommands"/>) or copied to the clipboard with
/// <c>--clipboard</c>. <see cref="CaptureAsync"/> is the shared logic; <c>capture mcp</c>'s capture_screen tool
/// calls it directly instead of going through the CLI's console output.
/// </summary>
internal static class ScreenCommand
{
    public static async Task<int> ShotAsync(AgentArgs args, ICaptureLog log)
    {
        var (result, staging, source) = await CaptureAsync(args, log);
        return staging is null ? AgentProgram.Report(result) : SetCommands.Finish(args, result, staging, source, log);
    }

    public static async Task<(CaptureResult Result, string? Staging, PreviewSource Source)> CaptureAsync(AgentArgs args, ICaptureLog log)
    {
        var o = AgentProgram.Options(args, log, "Desktop");
        var staging = SetCommands.Stage(args, o);
        var target = args.Target();
        // The host remembers the frame it handed the engine, so --clipboard copies exactly the saved shot
        // rather than a second, later capture of the target.
        var host = new FrameRememberingHost(new DesktopCaptureHost(target, args.HostOptions()));
        CaptureResult result;
        await using (var engine = await CaptureEngine.StartNewAsync(host, o))
        {
            result = await engine.ScreenshotAsync(args.Get("tag") ?? target.Label, new ScreenshotOptions { Format = args.ImageFormat() });
            if (result.Succeeded && args.Has("clipboard") && host.LastStill is { } frame)
                ClipboardWriter.SetImage(frame);
        }
        return (result, staging, AgentProgram.ScreenSource(args, target));
    }

    /// <summary>Passes everything through to <paramref name="inner"/>, keeping the last still it captured.</summary>
    private sealed class FrameRememberingHost(ICaptureHost inner) : ICaptureHost
    {
        public CapturedImage? LastStill { get; private set; }
        public string Name => inner.Name;
        public HostCapabilities Capabilities => inner.Capabilities;

        public async ValueTask<CapturedImage> CaptureStillAsync(ScreenshotOptions options, CancellationToken ct)
        {
            var image = await inner.CaptureStillAsync(options, ct).ConfigureAwait(false);
            LastStill = image;
            return image;
        }

        public VideoSource CreateVideoSource(int fps) => inner.CreateVideoSource(fps);
        public IReadOnlyList<IAudioTrackCapture> CreateAudioTracks(AudioMode mode) => inner.CreateAudioTracks(mode);
    }
}
