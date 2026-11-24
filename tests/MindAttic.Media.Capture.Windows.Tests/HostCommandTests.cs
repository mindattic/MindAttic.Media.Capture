using System.IO;
using System.Text.Json;
using MindAttic.Media.Capture.Agent;
using MindAttic.Media.Capture.Hosting;
using MindAttic.Media.Capture.Sets;
using MindAttic.Media.Capture.Triggers;
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

    private static async Task<JsonElement> Reply(ICapture capture, string request)
    {
        var output = new StringWriter();
        Assert.That(await new HostCommand(output).HandleAsync(capture, request), Is.True);
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Has.Length.EqualTo(1));
        return Line(lines[0]);
    }

    private static void AssertError(JsonElement reply, int id, string error)
    {
        Assert.That(reply.GetProperty("op").GetString(), Is.EqualTo("result"));
        Assert.That(reply.GetProperty("id").GetInt32(), Is.EqualTo(id));
        Assert.That(reply.GetProperty("ok").GetBoolean(), Is.False);
        Assert.That(reply.GetProperty("error").GetString(), Does.Contain(error));
    }

    [Test]
    public async Task Record_start_starts_a_recording_with_its_tag_and_data()
    {
        var capture = new FakeCapture(_root);
        var reply = await Reply(capture, """{"op":"record-start","id":1,"tag":"boss","data":{"wave":3}}""");
        Assert.That(reply.GetProperty("ok").GetBoolean(), Is.True);
        Assert.That(capture.StartRecordingCall?.Tag, Is.EqualTo("boss"));
        Assert.That(capture.StartRecordingCall?.Options?.Data?["wave"], Is.EqualTo(3.0));
    }

    [Test]
    public async Task Record_start_while_recording_is_an_error()
    {
        var capture = new FakeCapture(_root) { IsRecording = true };
        AssertError(await Reply(capture, """{"op":"record-start","id":2}"""), 2, "Already recording");
        Assert.That(capture.StartRecordingCall, Is.Null);
    }

    [Test]
    public async Task Record_stop_answers_with_the_exported_files()
    {
        var capture = new FakeCapture(_root) { IsRecording = true };
        var reply = await Reply(capture, """{"op":"record-stop","id":3}""");
        Assert.That(reply.GetProperty("ok").GetBoolean(), Is.True);
        Assert.That(reply.GetProperty("files")[0].GetString(), Does.EndWith("video.mp4"));
        Assert.That(capture.StopRecordingCalled, Is.True);
    }

    [TestCase("record-stop")]
    [TestCase("record-pause")]
    [TestCase("record-resume")]
    public async Task Recording_ops_with_nothing_recording_are_errors(string op)
    {
        var capture = new FakeCapture(_root);
        AssertError(await Reply(capture, JsonSerializer.Serialize(new { op, id = 4 })), 4, "Not recording");
        Assert.That(capture.StopRecordingCalled || capture.PauseCalled || capture.ResumeCalled, Is.False);
    }

    [Test]
    public async Task Record_pause_and_resume_reach_the_recording()
    {
        var capture = new FakeCapture(_root) { IsRecording = true };
        Assert.That((await Reply(capture, """{"op":"record-pause","id":5}""")).GetProperty("ok").GetBoolean(), Is.True);
        Assert.That((await Reply(capture, """{"op":"record-resume","id":6}""")).GetProperty("ok").GetBoolean(), Is.True);
        Assert.That(capture.PauseCalled && capture.ResumeCalled, Is.True);
    }

    [Test]
    public async Task Clip_without_before_records_after_seconds_from_now()
    {
        var capture = new FakeCapture(_root);
        await Reply(capture, """{"op":"clip","id":7,"after":4,"tag":"jump"}""");
        Assert.That(capture.ClipCall?.Duration, Is.EqualTo(TimeSpan.FromSeconds(4)));
        Assert.That(capture.ClipCall?.Tag, Is.EqualTo("jump"));
        Assert.That(capture.ReplayCall, Is.Null);
    }

    [Test]
    public async Task Clip_with_before_cuts_from_the_replay_tape_within_bounds()
    {
        var capture = new FakeCapture(_root);
        await Reply(capture, """{"op":"clip","id":8,"before":5,"after":99999}""");
        Assert.That(capture.ReplayCall?.Before, Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(capture.ReplayCall?.After, Is.EqualTo(TimeSpan.FromSeconds(600)));
        Assert.That(capture.ReplayCall?.Tag, Is.EqualTo("clip"));
    }

    [Test]
    public async Task Save_replay_defaults_to_the_replay_length()
    {
        var capture = new FakeCapture(_root);
        var output = new StringWriter();
        await new HostCommand(output) { ReplayLength = TimeSpan.FromSeconds(20) }.HandleAsync(capture, """{"op":"save-replay","id":9}""");
        Assert.That(capture.ReplayCall?.Before, Is.EqualTo(TimeSpan.FromSeconds(20)));
        Assert.That(capture.ReplayCall?.After, Is.EqualTo(TimeSpan.Zero));
        Assert.That(capture.ReplayCall?.Tag, Is.EqualTo("replay"));
    }

    [TestCase("""{"op":"clip","id":10}""")]
    [TestCase("""{"op":"save-replay","id":10}""")]
    public async Task Clip_and_save_replay_with_no_tape_answer_its_error(string request)
    {
        var capture = new FakeCapture(_root) { Fail = "Nothing is recording: no tape." };
        AssertError(await Reply(capture, request), 10, "no tape");
    }

    [Test]
    public async Task A_screenshot_with_set_joins_the_set_with_its_sidecar()
    {
        var set = Directory.CreateDirectory(Path.Combine(_root, "set")).FullName;
        var capture = new FakeCapture(_root);
        var reply = await Reply(capture, JsonSerializer.Serialize(new { op = "screenshot", id = 11, tag = "title", caption = "The title screen", set }));
        Assert.That(reply.GetProperty("ok").GetBoolean(), Is.True);
        var file = reply.GetProperty("files")[0].GetString()!;
        Assert.That(file, Is.EqualTo(Path.Combine(set, "001_title.png")));
        Assert.That(File.Exists(file) && File.Exists(file + ".json"), Is.True);
        Assert.That(File.Exists(capture.Shot), Is.True, "the session keeps its own copy");
        var item = PreviewSetStore.Open(set).Items.Single();
        Assert.That(item.Caption, Is.EqualTo("The title screen"));
        Assert.That(item.Source?.Type, Is.EqualTo("window"));
    }

    [Test]
    public async Task A_screenshot_with_a_missing_set_is_an_error()
    {
        var capture = new FakeCapture(_root);
        AssertError(await Reply(capture, JsonSerializer.Serialize(new { op = "screenshot", id = 12, set = Path.Combine(_root, "nope") })), 12, "No preview set");
        Assert.That(capture.ScreenshotCalled, Is.False);
    }

    private sealed class FakeCapture(string root) : ICapture
    {
        public bool Enabled => true;
        public string SessionId => "session-1";
        public string SessionFolder { get; } = Directory.CreateDirectory(Path.Combine(root, "session")).FullName;
        public bool IsRecording { get; set; }
        public CaptureDiagnostics Diagnostics { get; } = new();
        public ITriggerRegistry Triggers => NullCapture.Instance.Triggers;
        public event Action<CaptureResult>? Captured { add { } remove { } }
        public event Action<double>? DiskWarning { add { } remove { } }

        public string? Fail { get; set; }
        public string Shot => Path.Combine(SessionFolder, "shot.png");
        public bool ScreenshotCalled { get; private set; }
        public (string Tag, RecordingOptions? Options)? StartRecordingCall { get; private set; }
        public bool StopRecordingCalled { get; private set; }
        public bool PauseCalled { get; private set; }
        public bool ResumeCalled { get; private set; }
        public (TimeSpan Duration, string Tag)? ClipCall { get; private set; }
        public (TimeSpan Before, TimeSpan After, string Tag)? ReplayCall { get; private set; }

        private Task<CaptureResult> Result(CaptureKind kind, string tag, string file) => Task.FromResult(Fail is null
            ? new CaptureResult { Kind = kind, Tag = tag, Files = [file], Timestamp = DateTimeOffset.Now }
            : new CaptureResult { Kind = kind, Tag = tag, Error = Fail, Timestamp = DateTimeOffset.Now });

        public Task<CaptureResult> ScreenshotAsync(string tag, ScreenshotOptions? options = null)
        {
            ScreenshotCalled = true;
            File.WriteAllBytes(Shot, [1, 2, 3]);
            File.WriteAllText(Shot + ".json", JsonSerializer.Serialize(new { files = new[] { Shot } }));
            return Result(CaptureKind.Screenshot, tag, Shot);
        }

        public Task StartRecordingAsync(string tag, RecordingOptions? options = null)
        {
            StartRecordingCall = (tag, options);
            IsRecording = true;
            return Task.CompletedTask;
        }

        public Task<CaptureResult> StopRecordingAsync()
        {
            StopRecordingCalled = true;
            IsRecording = false;
            return Result(CaptureKind.Recording, "recording", Path.Combine(SessionFolder, "video.mp4"));
        }

        public void PauseRecording() => PauseCalled = true;

        public void ResumeRecording() => ResumeCalled = true;

        public Task<CaptureResult> ClipAsync(TimeSpan duration, string tag, RecordingOptions? options = null)
        {
            ClipCall = (duration, tag);
            return Result(CaptureKind.Clip, tag, Path.Combine(SessionFolder, "clip.mp4"));
        }

        public Task<CaptureResult> SaveReplayAsync(TimeSpan before, TimeSpan after, string tag, RecordingOptions? options = null)
        {
            ReplayCall = (before, after, tag);
            return Result(CaptureKind.Replay, tag, Path.Combine(SessionFolder, "replay.mp4"));
        }

        public void Marker(string name, CaptureData? data = null) { }

        public void Raise(string eventName, CaptureData? data = null) { }

        public Task<CaptureResult?> ExecuteCommandAsync(string command) => Task.FromResult<CaptureResult?>(null);

        public ValueTask DisposeAsync() => default;
    }
}
