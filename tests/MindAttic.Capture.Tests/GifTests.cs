using System.Diagnostics;
using MindAttic.Capture.Ffmpeg;
using MindAttic.Capture.Hosting;
using MindAttic.Capture.Output;
using NUnit.Framework;

namespace MindAttic.Capture.Tests;

public class GifArgsTests
{
    [Test]
    public void Concat_list_keeps_frame_order_and_gives_each_frame_one_gif_frame()
    {
        var list = FfmpegArgs.GifConcatList([@"C:\s\c.png", @"C:\s\a.png", @"C:\s\b.png"], 2);
        Assert.That(list, Is.EqualTo(
            "ffconcat version 1.0\n" +
            "file 'C:\\s\\c.png'\nduration 0.5\n" +
            "file 'C:\\s\\a.png'\nduration 0.5\n" +
            "file 'C:\\s\\b.png'\nduration 0.5\n"));
        Assert.That(FfmpegArgs.GifConcatList(["x.png"], 4), Does.Contain("duration 0.25\n"));
    }

    [Test]
    public void Concat_list_escapes_apostrophes() =>
        Assert.That(FfmpegArgs.GifConcatList([@"C:\Bob's\a.png"], 2), Does.Contain(@"file 'C:\Bob'\''s\a.png'"));

    [Test]
    public void Stamp_commands_switch_the_text_as_each_frame_starts()
    {
        var times = new[] { new DateTime(2026, 11, 24, 9, 5, 0), new DateTime(2026, 11, 24, 9, 20, 7) };
        Assert.That(FfmpegArgs.GifStampCommands(times, 4), Is.EqualTo(
            @"0 drawtext reinit 'text=2026-11-24 09\:05\:00';" + "\n" +
            @"0.25 drawtext reinit 'text=2026-11-24 09\:20\:07';" + "\n"));
    }

    [Test]
    public void Stamp_pass_letterboxes_every_frame_into_one_canvas()
    {
        var args = FfmpegArgs.GifStampFrames(@"C:\w\frames.ffconcat", @"C:\w\stamps.txt", 640, 360, @"C:\Windows\Fonts\consola.ttf", @"C:\w\%06d.png");
        var vf = args[args.IndexOf("-vf") + 1];
        Assert.That(args, Does.Contain(@"C:\w\frames.ffconcat"));
        Assert.That(args[^1], Is.EqualTo(@"C:\w\%06d.png"));
        Assert.That(vf, Does.StartWith(@"sendcmd=f='C\:/w/stamps.txt',"));
        Assert.That(vf, Does.Contain("scale=640:360:force_original_aspect_ratio=decrease"));
        Assert.That(vf, Does.Contain("pad=640:360:(ow-iw)/2:(oh-ih)/2"));
        Assert.That(vf, Does.Contain(@"fontfile='C\:/Windows/Fonts/consola.ttf':"));
        var noFont = FfmpegArgs.GifStampFrames("l", "s", 640, 360, null, "o");
        Assert.That(noFont[noFont.IndexOf("-vf") + 1], Does.Not.Contain("fontfile"));
    }

    [Test]
    public void Gif_pass_runs_at_fps_holds_the_last_frame_and_loops()
    {
        var args = FfmpegArgs.GifFromFrames(@"C:\w\%06d.png", 4, GifExporter.HoldSeconds, @"C:\out.gif");
        Assert.That(args[args.IndexOf("-framerate") + 1], Is.EqualTo("4"));
        Assert.That(args[args.IndexOf("-filter_complex") + 1], Does.StartWith("tpad=stop_mode=clone:stop_duration=2,").And.Contain("palettegen").And.Contain("paletteuse"));
        Assert.That(args[args.IndexOf("-loop") + 1], Is.EqualTo("0"));
        Assert.That(args[^1], Is.EqualTo(@"C:\out.gif"));
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(30)]
    public void Fps_other_than_2_or_4_is_rejected(int fps)
    {
        var ex = Assert.ThrowsAsync<CaptureException>(() => GifExporter.ExportAsync("ffmpeg-unused", ["a.png", "b.png"], fps, null, 1280, new TraceCaptureLog(), default));
        Assert.That(ex!.Error, Is.EqualTo(CaptureError.InvalidArgument));
    }

    [Test]
    public void No_frames_or_a_tiny_width_cap_is_rejected()
    {
        Assert.ThrowsAsync<CaptureException>(() => GifExporter.ExportAsync("ffmpeg-unused", [], 2, null, 1280, new TraceCaptureLog(), default));
        Assert.ThrowsAsync<CaptureException>(() => GifExporter.ExportAsync("ffmpeg-unused", ["a.png"], 2, null, 1, new TraceCaptureLog(), default));
    }

    [Test]
    public void All_frames_missing_is_rejected()
    {
        var missing = Path.Combine(Path.GetTempPath(), "capture-gif-missing-" + Guid.NewGuid().ToString("N"), "a.png");
        var ex = Assert.ThrowsAsync<CaptureException>(() => GifExporter.ExportAsync("ffmpeg-unused", [missing], 2, null, 1280, new TraceCaptureLog(), default));
        Assert.That(ex!.Message, Does.Contain("None of the GIF's frames exist"));
    }
}

public class GifOptionsTests
{
    private static CaptureArgs P(params string[] args) => CaptureArgs.Parse(args);

    [Test]
    public void Gif_word_turns_on_the_session_gif()
    {
        var a = P("-capture", "screen", "15m", "gif");
        Assert.That((a.Gif, a.ScreenInterval, a.Video), Is.EqualTo((true, (TimeSpan?)TimeSpan.FromMinutes(15), false)));
        Assert.That(new CaptureOptions().Apply(a).SessionGif, Is.True);
        Assert.That(new CaptureOptions().Apply(P("-capture", "screen", "15m")).SessionGif, Is.False);
    }

    [Test]
    public void Gif_alone_implies_screenshots_at_the_default_interval()
    {
        var a = P("-capture", "gif");
        Assert.That((a.Gif, a.ScreenInterval, a.ScreenIntervalSpecified, a.Video), Is.EqualTo((true, (TimeSpan?)CaptureArgs.DefaultScreenInterval, false, false)));
    }

    [Test]
    public void A_second_duration_after_the_screen_interval_caps_the_session()
    {
        var a = P("-capture", "screen", "15m", "5h", "gif");
        Assert.That((a.ScreenInterval, a.MaxDuration, a.Gif), Is.EqualTo(((TimeSpan?)TimeSpan.FromMinutes(15), (TimeSpan?)TimeSpan.FromHours(5), true)));
        Assert.That(new CaptureOptions().Apply(a).Limits.MaxDuration, Is.EqualTo(TimeSpan.FromHours(5)));
        Assert.That(P("-capture", "screen", "15m").MaxDuration, Is.Null);
    }

    [TestCase("screen", "15m", "0")]
    [TestCase("screen", "15m", "gifs")]
    public void Bad_gif_words_fail_loudly(params string[] words) =>
        Assert.Throws<CaptureException>(() => CaptureArgs.Parse(["-capture=" + string.Join(" ", words)]));

    [Test]
    public void Config_file_sets_gif_fps_and_width()
    {
        var o = new CaptureOptions().Apply(new CaptureConfigFile { Gif = true, GifFps = 4, GifWidth = 640 });
        Assert.That((o.SessionGif, o.GifFps, o.GifMaxWidth), Is.EqualTo((true, 4, 640)));
        var d = new CaptureOptions();
        Assert.That((d.SessionGif, d.GifFps, d.GifMaxWidth), Is.EqualTo((false, 2, 1280)));
    }

    [TestCase(3, 1280)]
    [TestCase(0, 1280)]
    [TestCase(2, 1)]
    public void Bad_gif_settings_fail_validation(int fps, int width) =>
        Assert.Throws<CaptureException>(() => new CaptureOptions { GifFps = fps, GifMaxWidth = width }.Validate());

    [TestCase(2)]
    [TestCase(4)]
    public void Both_gif_rates_validate(int fps) => Assert.DoesNotThrow(() => new CaptureOptions { GifFps = fps }.Validate());
}

[NonParallelizable]
public class GifExportTests
{
    private sealed class RecordingLog : ICaptureLog
    {
        public readonly List<(CaptureLogLevel Level, string Message, Exception? Exception)> Entries = [];
        public void Log(CaptureLogLevel level, string message, Exception? exception = null)
        {
            lock (Entries) Entries.Add((level, message, exception));
        }
        public bool Warned(string text)
        {
            lock (Entries) return Entries.Any(e => e.Level == CaptureLogLevel.Warning && e.Message.Contains(text, StringComparison.Ordinal));
        }
    }

    private string _root = "";
    private string? _ffmpeg;

    [OneTimeSetUp]
    public void FindFfmpeg()
    {
        _ffmpeg = FfmpegLocator.TryFind("ffmpeg");
        if (_ffmpeg is null) Assert.Ignore("ffmpeg is not installed; GIF export tests need it.");
    }

    [SetUp]
    public void Setup() => _root = Directory.CreateTempSubdirectory("capture-gif-").FullName;

    [TearDown]
    public void Cleanup()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    /// <summary>A one-colour PNG of the given size.</summary>
    private async Task<string> FrameAsync(string name, string color, int width = 320, int height = 180)
    {
        var file = Path.Combine(_root, "shots", name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await FfmpegProcess.RunAsync(_ffmpeg!, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", $"color=c={color}:s={width}x{height}", "-frames:v", "1", "-y", file],
            new TraceCaptureLog(), default);
        return file;
    }

    private static string Ffprobe(string file, string args)
    {
        var psi = new ProcessStartInfo(FfmpegLocator.Find("ffprobe"), $"-v error {args} \"{file}\"")
        {
            RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        return text;
    }

    private static string Size(string gif) => Ffprobe(gif, "-select_streams v:0 -show_entries stream=width,height -of csv=p=0");

    /// <summary>The GIF's frames as their centre pixel's dominant channel (r, g or b), runs of one colour collapsed.</summary>
    private string Colors(string gif)
    {
        var raw = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".rgb");
        var psi = new ProcessStartInfo(_ffmpeg!)
        {
            UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in new[] { "-v", "error", "-i", gif, "-vf", "crop=1:1:iw/2:ih/2", "-f", "rawvideo", "-pix_fmt", "rgb24", "-y", raw }) psi.ArgumentList.Add(a);
        using (var p = Process.Start(psi)!) p.WaitForExit();
        var bytes = File.ReadAllBytes(raw);
        var colors = "";
        for (var i = 0; i + 2 < bytes.Length; i += 3)
        {
            var (r, g, b) = (bytes[i], bytes[i + 1], bytes[i + 2]);
            var c = r >= g && r >= b ? 'r' : g >= b ? 'g' : 'b';
            if (colors.Length == 0 || colors[^1] != c) colors += c;
        }
        return colors;
    }

    [Test]
    public async Task Frames_play_in_the_order_given_and_the_last_is_held()
    {
        var blue = await FrameAsync("a.png", "blue");
        var red = await FrameAsync("b.png", "red");
        var green = await FrameAsync("c.png", "lime");

        var gif = await CaptureEngine.ExportGifAsync([red, green, blue], 2, Path.Combine(_root, "out", "t.gif"), ffmpegPath: _ffmpeg!);

        Assert.That(gif, Is.EqualTo(Path.Combine(_root, "out", "t.gif")));
        Assert.That(Colors(gif), Is.EqualTo("rgb"));
        // 3 frames at 2 fps plus the 2s hold.
        Assert.That(double.Parse(Ffprobe(gif, "-show_entries format=duration -of default=nw=1:nk=1"), System.Globalization.CultureInfo.InvariantCulture),
            Is.EqualTo(1.5 + GifExporter.HoldSeconds).Within(0.3));
    }

    [Test]
    public async Task Output_defaults_to_the_folder_name_beside_the_first_frame()
    {
        var a = await FrameAsync("a.png", "red");
        var b = await FrameAsync("b.png", "blue");
        var gif = await CaptureEngine.ExportGifAsync([a, b], 4, ffmpegPath: _ffmpeg!);
        Assert.That(gif, Is.EqualTo(Path.Combine(_root, "shots", "shots.gif")));
        Assert.That(File.Exists(gif), Is.True);
    }

    [Test]
    public async Task A_missing_frame_is_skipped_with_a_warning()
    {
        var red = await FrameAsync("a.png", "red");
        var blue = await FrameAsync("c.png", "blue");
        var missing = Path.Combine(_root, "shots", "b.png");
        var log = new RecordingLog();

        var gif = await CaptureEngine.ExportGifAsync([red, missing, blue], 2, Path.Combine(_root, "t.gif"), ffmpegPath: _ffmpeg!, log: log);

        Assert.That(Colors(gif), Is.EqualTo("rb"));
        Assert.That(log.Warned("b.png"), Is.True);
    }

    [Test]
    public async Task An_odd_sized_frame_is_letterboxed_into_the_first_frames_even_canvas()
    {
        var first = await FrameAsync("a.png", "red", 321, 181);
        var odd = await FrameAsync("b.png", "blue", 201, 99);

        var gif = await CaptureEngine.ExportGifAsync([first, odd], 2, Path.Combine(_root, "t.gif"), ffmpegPath: _ffmpeg!);

        Assert.That(Size(gif), Is.EqualTo("320,180"));
        Assert.That(Colors(gif), Is.EqualTo("rb"));
    }

    [Test]
    public async Task Width_is_capped_and_aspect_kept()
    {
        var a = await FrameAsync("a.png", "red", 640, 360);
        var b = await FrameAsync("b.png", "blue", 640, 360);
        var gif = await CaptureEngine.ExportGifAsync([a, b], 2, Path.Combine(_root, "t.gif"), maxWidth: 161, ffmpegPath: _ffmpeg!);
        Assert.That(Size(gif), Is.EqualTo("160,90"));
    }

    [Test]
    public async Task A_frame_that_is_not_a_png_is_rejected()
    {
        var bad = Path.Combine(_root, "bad.png");
        await File.WriteAllTextAsync(bad, "not a png");
        var ex = Assert.ThrowsAsync<CaptureException>(() => CaptureEngine.ExportGifAsync([bad], 2, ffmpegPath: _ffmpeg!));
        Assert.That(ex!.Error, Is.EqualTo(CaptureError.InvalidArgument));
    }

    // ---------------------------------------------------------------- session GIF

    private CaptureOptions Options(TimeSpan interval, ICaptureLog? log = null)
    {
        var o = new CaptureOptions { OutputRoot = _root, AppName = "Test", FfmpegPath = _ffmpeg!, ScreenshotInterval = interval, SessionGif = true };
        o.Limits.Enabled = false;
        if (log is not null) o.Log = log;
        return o;
    }

    [Test]
    public async Task A_session_that_ends_early_still_makes_its_gif()
    {
        string folder;
        await using (var engine = await CaptureEngine.StartNewAsync(new SyntheticHost(), Options(TimeSpan.FromMilliseconds(300))))
        {
            folder = engine.SessionFolder;
            await Task.Delay(1500);
        }
        Assert.That(Directory.GetFiles(folder, "*.png"), Has.Length.GreaterThanOrEqualTo(2));
        var gifs = Directory.GetFiles(folder, "*.gif");
        Assert.That(gifs, Has.Length.EqualTo(1));
        Assert.That(Path.GetFileName(gifs[0]), Does.EndWith("_Test.gif"));
    }

    [Test]
    public async Task The_max_duration_ends_the_session_and_makes_its_gif()
    {
        var o = Options(TimeSpan.FromMilliseconds(300));
        o.Limits.MaxDuration = TimeSpan.FromSeconds(1.5);
        o.Limits.PollInterval = TimeSpan.FromMilliseconds(250);
        await using var engine = await CaptureEngine.StartNewAsync(new SyntheticHost(), o);
        var wait = Stopwatch.StartNew();
        while (Directory.GetFiles(engine.SessionFolder, "*.gif").Length == 0 && wait.Elapsed < TimeSpan.FromSeconds(60)) await Task.Delay(200);
        Assert.That(Directory.GetFiles(engine.SessionFolder, "*.gif"), Has.Length.EqualTo(1));
        Assert.That(engine.Enabled, Is.False);
    }

    [Test]
    public async Task Fewer_than_two_screenshots_make_no_gif()
    {
        string folder;
        await using (var engine = await CaptureEngine.StartNewAsync(new SyntheticHost(), Options(TimeSpan.FromHours(1))))
        {
            folder = engine.SessionFolder;
            await Task.Delay(500);
        }
        Assert.That(Directory.Exists(folder) ? Directory.GetFiles(folder, "*.gif") : Array.Empty<string>(), Is.Empty);
    }

    [Test]
    public async Task A_failing_ffmpeg_is_a_warning_not_a_failed_session()
    {
        var log = new RecordingLog();
        var o = Options(TimeSpan.FromMilliseconds(300), log);
        var engine = await CaptureEngine.StartNewAsync(new SyntheticHost(), o);
        var folder = engine.SessionFolder;
        await Task.Delay(1200);
        // The screenshots already resolved ffmpeg; only the session GIF looks it up again, and finds nothing.
        o.FfmpegPath = Path.Combine(_root, "missing", "ffmpeg.exe");

        Assert.DoesNotThrowAsync(async () => await engine.DisposeAsync());

        Assert.That(Directory.GetFiles(folder, "*.png"), Has.Length.GreaterThanOrEqualTo(2));
        Assert.That(Directory.GetFiles(folder, "*.gif"), Is.Empty);
        Assert.That(log.Warned("session GIF could not be made"), Is.True);
    }
}
