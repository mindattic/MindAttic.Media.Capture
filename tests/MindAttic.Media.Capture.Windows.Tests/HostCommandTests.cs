using System.IO;
using System.Text.Json;
using MindAttic.Media.Capture.Agent;
using MindAttic.Media.Capture.Hosting;
using NUnit.Framework;

namespace MindAttic.Media.Capture.Windows.Tests;

[NonParallelizable]
public class HostCommandTests
{
    private string _root = "";

    [SetUp]
    public void Setup() => _root = Directory.CreateTempSubdirectory("capture-host-").FullName;

    [TearDown]
    public void Cleanup()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    private static (CaptureOptions Options, CaptureArgs Args) Build(params string[] args) =>
        HostCommand.BuildOptions(AgentArgs.Parse(["host", .. args]), new TraceCaptureLog());

    private static JsonElement Line(string text) => JsonDocument.Parse(text).RootElement;

    [Test]
    public void The_command_line_splits_as_windows_does()
    {
        Assert.That(HostCommand.SplitCommandLine(@"Game.exe -capture video ""C:\My Dir\a b"" -x=""1 2"""),
            Is.EqualTo(new[] { "Game.exe", "-capture", "video", @"C:\My Dir\a b", "-x=1 2" }));
        Assert.That(HostCommand.SplitCommandLine(@"a\\""b c"" """" d\e"), Is.EqualTo(new[] { @"a\b c", "", @"d\e" }));
        Assert.That(HostCommand.SplitCommandLine("   "), Is.Empty);
    }

    [Test]
    public void No_capture_mode_starts_nothing()
    {
        var (options, args) = Build("--cmdline", "Game.exe -windowed");
        Assert.That(args.Any, Is.False);
        Assert.That(options.SessionRecording, Is.False);
        Assert.That(options.ScreenshotInterval, Is.Null);
    }

    [Test]
    public async Task No_capture_mode_answers_ready_any_false_and_exits_0()
    {
        var output = new StringWriter();
        var code = await new HostCommand(output).RunAsync(AgentArgs.Parse(["host", "--cmdline", "Game.exe"]), new StringReader(""));
        Assert.That(code, Is.EqualTo(0));
        Assert.That(output.ToString().Trim(), Is.EqualTo("""{"op":"ready","any":false}"""));
    }

    [Test]
    public void Capture_video_screen_5_sets_the_session_modes()
    {
        var (options, args) = Build("--cmdline", "Game.exe -capture video screen 5", "--app", "SpaceGod", "--version", "1.2");
        Assert.That(args.Any, Is.True);
        Assert.That(options.SessionRecording, Is.True);
        Assert.That(options.ScreenshotInterval, Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(options.AppName, Is.EqualTo("SpaceGod"));
        Assert.That(options.AppVersion, Is.EqualTo("1.2"));
        Assert.That(options.Hotkeys, Is.Empty);
        Assert.That(options.DetachedFinalizerPath, Is.EqualTo(Environment.ProcessPath));
    }

    [Test]
    public void The_defaults_file_applies_without_capture_config()
    {
        var defaults = Path.Combine(_root, "defaults.json");
        File.WriteAllText(defaults, """{"fps": 24}""");
        var (options, _) = Build("--cmdline", "Game.exe -capture video", "--defaults", defaults);
        Assert.That(options.Fps, Is.EqualTo(24));
    }

    [Test]
    public void Capture_config_wins_over_defaults()
    {
        var defaults = Path.Combine(_root, "defaults.json");
        var config = Path.Combine(_root, "game.json");
        File.WriteAllText(defaults, """{"fps": 24}""");
        File.WriteAllText(config, """{"fps": 50}""");
        var (options, _) = Build("--cmdline", $"Game.exe -capture video -capture-config \"{config}\"", "--defaults", defaults);
        Assert.That(options.Fps, Is.EqualTo(50));
    }

    [Test]
    public async Task An_unknown_op_is_answered_with_an_error_result()
    {
        var output = new StringWriter();
        var more = await new HostCommand(output).HandleAsync(null!, """{"op":"dance","id":7}""");
        Assert.That(more, Is.True);
        var reply = Line(output.ToString().Trim());
        Assert.That(reply.GetProperty("op").GetString(), Is.EqualTo("result"));
        Assert.That(reply.GetProperty("id").GetInt32(), Is.EqualTo(7));
        Assert.That(reply.GetProperty("ok").GetBoolean(), Is.False);
        Assert.That(reply.GetProperty("error").GetString(), Does.Contain("dance"));
    }

    [Test]
    public async Task A_line_that_is_not_json_is_answered_with_an_error_result()
    {
        var output = new StringWriter();
        Assert.That(await new HostCommand(output).HandleAsync(null!, "not json"), Is.True);
        Assert.That(Line(output.ToString().Trim()).GetProperty("ok").GetBoolean(), Is.False);
    }

    [Test]
    public async Task Quit_ends_the_request_loop()
    {
        var output = new StringWriter();
        Assert.That(await new HostCommand(output).HandleAsync(null!, """{"op":"quit"}"""), Is.False);
        Assert.That(output.ToString(), Is.Empty);
    }
}
