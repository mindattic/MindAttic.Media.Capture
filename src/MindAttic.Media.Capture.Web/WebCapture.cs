using System.Globalization;
using MindAttic.Media.Capture.Hosting;
using MindAttic.Media.Capture.Output;
using Microsoft.Playwright;

namespace MindAttic.Media.Capture.Web;

/// <summary>What <see cref="WebCapture.CaptureAsync"/> captures.</summary>
public sealed class WebCaptureOptions
{
    /// <summary>The page.</summary>
    public required string Url { get; init; }
    /// <summary>One screenshot per viewport; default 1440x900 (ignored when <see cref="Device"/> is set).</summary>
    public IReadOnlyList<(int Width, int Height)> Viewports { get; init; } = [];
    /// <summary>A Playwright device descriptor name ("iPhone 13", "Pixel 7"): its viewport, scale, touch and user agent.</summary>
    public string? Device { get; init; }
    /// <summary>The whole scrollable page instead of the viewport.</summary>
    public bool FullPage { get; init; }
    /// <summary>A CSS selector to wait for before capturing.</summary>
    public string? WaitFor { get; init; }
    /// <summary>A pause after load (and <see cref="WaitFor"/>) before capturing.</summary>
    public TimeSpan Wait { get; init; }
    /// <summary>prefers-color-scheme: dark.</summary>
    public bool Dark { get; init; }
    /// <summary>Fake camera and microphone, already permitted (pages that need voice and camera).</summary>
    public bool FakeMedia { get; init; }
    /// <summary>Also record this long of each viewport as a webm.</summary>
    public TimeSpan? Video { get; init; }
    /// <summary>The browser channel: msedge (the installed Edge) by default.</summary>
    public string Channel { get; init; } = "msedge";
    /// <summary>Page load timeout.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>One file a web capture wrote (its sidecar is <c>{File}.json</c>).</summary>
/// <param name="File">Full path.</param>
/// <param name="Kind">image or video.</param>
/// <param name="Viewport">"1440x900".</param>
/// <param name="Device">The emulated device, when one was used.</param>
public sealed record WebShot(string File, string Kind, string Viewport, string? Device);

/// <summary>
/// Headless page captures through Playwright, driving the installed Microsoft Edge (no browser download): a PNG per
/// viewport and, with <see cref="WebCaptureOptions.Video"/>, a webm per viewport, each with a capture sidecar
/// (<c>host: "playwright"</c>). Works without a desktop session.
/// </summary>
public static class WebCapture
{
    /// <summary>Captures into <paramref name="outDir"/> as <c>{tag}-{WxH}.png</c> / <c>.webm</c>.</summary>
    public static async Task<IReadOnlyList<WebShot>> CaptureAsync(WebCaptureOptions o, string outDir, string tag, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outDir);
        tag = OutputManager.SanitizeTag(tag);
        var json = new SystemTextCaptureJson();
        var shots = new List<WebShot>();
        using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        var launch = new BrowserTypeLaunchOptions { Channel = o.Channel, Headless = true };
        if (o.FakeMedia) launch.Args = ["--use-fake-ui-for-media-stream", "--use-fake-device-for-media-stream"];
        await using var browser = await playwright.Chromium.LaunchAsync(launch).ConfigureAwait(false);

        BrowserNewContextOptions? device = null;
        if (o.Device is { Length: > 0 } name && !playwright.Devices.TryGetValue(name, out device))
            throw new CaptureException(CaptureError.InvalidArgument, $"Unknown device \"{name}\" (Playwright names, e.g. \"iPhone 13\", \"Pixel 7\").");
        var sizes = device?.ViewportSize is { } dv ? [(dv.Width, dv.Height)]
            : o.Viewports.Count > 0 ? o.Viewports : [(1440, 900)];

        foreach (var (width, height) in sizes)
        {
            ct.ThrowIfCancellationRequested();
            var viewport = $"{width}x{height}";
            var stem = Path.Combine(outDir, $"{tag}-{viewport}");
            var videoDir = o.Video is null ? null : Path.Combine(outDir, ".video-" + Guid.NewGuid().ToString("N"));
            var context = await browser.NewContextAsync(ContextOptions(device, width, height, o, videoDir)).ConfigureAwait(false);
            if (o.FakeMedia)
            {
                var origin = new Uri(o.Url).GetLeftPart(UriPartial.Authority);
                await context.GrantPermissionsAsync(["camera", "microphone"], new() { Origin = origin }).ConfigureAwait(false);
            }
            var page = await context.NewPageAsync().ConfigureAwait(false);
            var started = DateTimeOffset.Now;
            try
            {
                await page.GotoAsync(o.Url, new() { WaitUntil = WaitUntilState.Load, Timeout = (float)o.Timeout.TotalMilliseconds }).ConfigureAwait(false);
                try
                {
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 10_000 }).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // Pages that keep a socket or poll never go idle; the load event is enough.
                }
                if (o.WaitFor is { Length: > 0 } selector)
                    await page.WaitForSelectorAsync(selector, new() { Timeout = (float)o.Timeout.TotalMilliseconds }).ConfigureAwait(false);
                if (o.Wait > TimeSpan.Zero) await Task.Delay(o.Wait, ct).ConfigureAwait(false);

                var png = stem + ".png";
                await page.ScreenshotAsync(new() { Path = png, FullPage = o.FullPage }).ConfigureAwait(false);
                WriteSidecar(json, png, "Screenshot", tag, o, viewport, width, height, DateTimeOffset.Now, 0);
                shots.Add(new WebShot(png, "image", viewport, o.Device));

                if (o.Video is { } length)
                {
                    var remaining = length - (DateTimeOffset.Now - started);
                    if (remaining > TimeSpan.Zero) await Task.Delay(remaining, ct).ConfigureAwait(false);
                }
            }
            catch (PlaywrightException ex)
            {
                throw new CaptureException(CaptureError.CaptureFailed, $"{o.Url} at {viewport}: {ex.Message}", ex);
            }
            finally
            {
                await context.CloseAsync().ConfigureAwait(false); // finishes the video file
            }

            if (videoDir is not null && page.Video is { } video)
            {
                var recorded = await video.PathAsync().ConfigureAwait(false);
                var webm = stem + ".webm";
                File.Move(recorded, webm, overwrite: true);
                TryDelete(videoDir);
                WriteSidecar(json, webm, "Recording", tag, o, viewport, width, height, started, (DateTimeOffset.Now - started).TotalSeconds);
                shots.Add(new WebShot(webm, "video", viewport, o.Device));
            }
        }
        return shots;
    }

    private static BrowserNewContextOptions ContextOptions(BrowserNewContextOptions? device, int width, int height, WebCaptureOptions o, string? videoDir)
    {
        var c = device is null ? new BrowserNewContextOptions { ViewportSize = new() { Width = width, Height = height } }
            : new BrowserNewContextOptions(device);
        c.ColorScheme = o.Dark ? ColorScheme.Dark : ColorScheme.Light;
        if (videoDir is not null)
        {
            c.RecordVideoDir = videoDir;
            c.RecordVideoSize = new() { Width = width, Height = height };
        }
        return c;
    }

    private static void WriteSidecar(ICaptureJson json, string file, string kind, string tag, WebCaptureOptions o, string viewport,
        int width, int height, DateTimeOffset at, double seconds)
    {
        var sidecar = new CaptureSidecar
        {
            Kind = kind, Tag = tag, App = Uri.TryCreate(o.Url, UriKind.Absolute, out var uri) ? uri.Host : "Web",
            Session = CaptureNaming.Timestamp(at), Host = "playwright", Timestamp = at, DurationSeconds = seconds,
            Width = width, Height = height, Files = [file],
            Data = new()
            {
                ["url"] = o.Url, ["viewport"] = viewport, ["device"] = o.Device, ["fullPage"] = o.FullPage, ["dark"] = o.Dark,
                ["channel"] = o.Channel,
            },
        };
        File.WriteAllText(file + ".json", json.Serialize(sidecar));
    }

    /// <summary>Parses "1440x900".</summary>
    public static (int Width, int Height) ParseViewport(string text)
    {
        var parts = text.Split('x', 'X', ',');
        if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var w) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var h) && w > 0 && h > 0)
            return (w, h);
        throw new CaptureException(CaptureError.InvalidArgument, $"--viewport must be WxH, e.g. 1440x900; got \"{text}\".");
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
