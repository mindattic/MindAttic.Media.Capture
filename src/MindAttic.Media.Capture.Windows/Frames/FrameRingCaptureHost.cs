using System.Diagnostics;
using System.Runtime.InteropServices;
using MindAttic.Media.Capture.Hosting;

namespace MindAttic.Media.Capture.Windows.Frames;

/// <summary>
/// The capture host for a game's own frames: video and screenshots from <see cref="FrameRingSource"/>, the same tape,
/// ffmpeg pipeline, session folder, markers and events as window capture; audio is the system mix, as the desktop host's.
/// </summary>
internal sealed class FrameRingCaptureHost(FrameRingSource ring) : ICaptureHost
{
    public string Name => "frames";

    public HostCapabilities Capabilities => HostCapabilities.None;

    public ValueTask<CapturedImage> CaptureStillAsync(ScreenshotOptions options, CancellationToken ct) => new(Task.Run(() =>
    {
        // A frame rendered after the request (it trails the game by the readback's few frames), else the newest there is.
        var asked = ring.Latest;
        var clock = Stopwatch.StartNew();
        while (ring.Latest <= asked && clock.Elapsed < TimeSpan.FromSeconds(asked == 0 ? 10 : 1) && !ring.Closed)
        {
            ct.ThrowIfCancellationRequested();
            Thread.Sleep(5);
        }
        var bgra = new byte[ring.FrameBytes];
        var got = false;
        for (var attempt = 0; attempt < 10 && !got; attempt++)
        {
            got = ring.TryCopyLatest(bgra, 0) > 0;
            if (!got) Thread.Sleep(2);
        }
        if (!got) throw new CaptureException(CaptureError.CaptureFailed, "The game has fed no frame yet.");
        return new CapturedImage(ring.Width, ring.Height, bgra);
    }, ct));

    public VideoSource CreateVideoSource(int fps) => new FrameRingVideoSource(ring, fps);

    public IReadOnlyList<IAudioTrackCapture> CreateAudioTracks(AudioMode mode) => DesktopCaptureHost.CreateWasapiTracks(mode);
}

/// <summary>
/// The tape's frames from the ring, at a constant rate as WgcWindowSource sends a window's: the newest frame each tick,
/// the last one again while the game has fed nothing newer (a hitch shows as a held frame, never as a gap in time).
/// Ends once the game marks the ring closed.
/// </summary>
internal sealed class FrameRingVideoSource(FrameRingSource ring, int fps) : PushVideoSource
{
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _pump;
    private volatile bool _running;

    public override int Width => ring.Width;
    public override int Height => ring.Height;
    public override bool RealTime => true;
    public override string Description => $"game frames {ring.Width}x{ring.Height}";

    /// <summary>Completes when the pump stops: <see cref="StopAsync"/>, or the game closed the ring.</summary>
    internal Task Ended => _ended.Task;

    public override Task StartAsync(IFrameSink sink, CancellationToken ct)
    {
        _running = true;
        _pump = new Thread(() => Pump(sink)) { IsBackground = true, Name = "Frame ring pump", Priority = ThreadPriority.AboveNormal };
        _pump.Start();
        return Task.CompletedTask;
    }

    private void Pump(IFrameSink sink)
    {
        TimeBeginPeriod(1);
        try
        {
            var frame = new byte[ring.FrameBytes];
            var spare = new byte[ring.FrameBytes];
            long have = 0;
            var interval = Stopwatch.Frequency / (double)fps;
            var clock = Stopwatch.StartNew();
            long n = 0;
            while (_running)
            {
                var due = (long)(n * interval);
                var wait = due - clock.ElapsedTicks;
                if (wait > 0)
                {
                    var ms = (int)(wait * 1000 / Stopwatch.Frequency);
                    if (ms > 1) Thread.Sleep(ms - 1);
                    else Thread.SpinWait(50);
                    continue;
                }
                n++;
                var closed = ring.Closed;
                // Into the spare buffer: a copy the game overwrote midway is dropped, never sent.
                var seq = ring.TryCopyLatest(spare, have);
                if (seq > 0)
                {
                    have = seq;
                    (frame, spare) = (spare, frame);
                }
                if (have > 0) sink.WriteFrame(frame);
                // Closed before that copy: the last frame the game wrote has been sent.
                if (closed) break;
                if (clock.ElapsedTicks - (long)(n * interval) > Stopwatch.Frequency) n = (long)(clock.ElapsedTicks / interval);
            }
        }
        finally
        {
            TimeEndPeriod(1);
            _ended.TrySetResult();
        }
    }

    public override Task StopAsync()
    {
        _running = false;
        _pump?.Join(TimeSpan.FromSeconds(2));
        return Task.CompletedTask;
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint ms);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint ms);
}
