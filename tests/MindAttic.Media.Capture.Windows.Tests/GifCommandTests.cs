using System.IO;
using MindAttic.Media.Capture.Agent;
using MindAttic.Media.Capture.Ffmpeg;
using MindAttic.Media.Capture.Hosting;
using NUnit.Framework;

namespace MindAttic.Media.Capture.Windows.Tests;

[NonParallelizable]
public class GifCommandTests
{
    private string _root = "";

    [SetUp]
    public void Setup() => _root = Directory.CreateTempSubdirectory("capture-gifcmd-").FullName;

    [TearDown]
    public void Cleanup()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    private static Task<int> Run(params string[] args) => GifCommand.RunAsync(AgentArgs.Parse(["gif", .. args]), new TraceCaptureLog());

    [Test]
    public void A_missing_folder_fails_loudly()
    {
        var ex = Assert.ThrowsAsync<CaptureException>(() => Run(Path.Combine(_root, "nope")));
        Assert.That(ex!.Error, Is.EqualTo(CaptureError.InvalidArgument));
    }

    [Test]
    public void A_folder_without_pngs_fails_loudly()
    {
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "x");
        var ex = Assert.ThrowsAsync<CaptureException>(() => Run(_root));
        Assert.That(ex!.Message, Does.Contain("No PNGs"));
    }

    [Test]
    public void No_folder_fails_loudly() => Assert.ThrowsAsync<CaptureException>(() => Run());

    [Test]
    public void A_bad_fps_fails_before_ffmpeg_runs()
    {
        var ffmpeg = FfmpegLocator.TryFind("ffmpeg");
        if (ffmpeg is null) Assert.Ignore("ffmpeg is not installed.");
        File.WriteAllBytes(Path.Combine(_root, "a.png"), [0x89, (byte)'P', (byte)'N', (byte)'G']);
        var ex = Assert.ThrowsAsync<CaptureException>(() => Run(_root, "--fps", "3", "--ffmpeg", ffmpeg));
        Assert.That(ex!.Message, Does.Contain("2 or 4 fps"));
    }

    [Test]
    public async Task A_folders_pngs_are_stitched_into_the_given_output()
    {
        var ffmpeg = FfmpegLocator.TryFind("ffmpeg");
        if (ffmpeg is null) Assert.Ignore("ffmpeg is not installed.");
        foreach (var (name, color) in new[] { ("b.png", "blue"), ("a.png", "red") })
            await FfmpegProcess.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", $"color=c={color}:s=64x36", "-frames:v", "1", "-y", Path.Combine(_root, name)],
                new TraceCaptureLog(), default);
        var output = Path.Combine(_root, "out", "t.gif");

        var code = await Run(_root, "--fps", "4", "--width", "32", "--output", output, "--ffmpeg", ffmpeg);

        Assert.That(code, Is.EqualTo(0));
        Assert.That(File.Exists(output), Is.True);
    }
}
