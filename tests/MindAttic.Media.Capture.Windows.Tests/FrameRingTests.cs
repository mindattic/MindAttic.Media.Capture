using System.IO.MemoryMappedFiles;
using MindAttic.Media.Capture.Hosting;
using MindAttic.Media.Capture.Windows.Frames;
using NUnit.Framework;

namespace MindAttic.Media.Capture.Windows.Tests;

public class FrameRingTests
{
    private const int W = 4, H = 2, Slots = 3, HeaderBytes = 4096;
    private const int FrameBytes = W * H * 4;

    private MemoryMappedFile _file = null!;
    private MemoryMappedViewAccessor _view = null!;
    private string _name = "";

    [SetUp]
    public void Setup()
    {
        _name = Guid.NewGuid().ToString("B");
        _file = MemoryMappedFile.CreateNew(_name, HeaderBytes + Slots * FrameBytes);
        _view = _file.CreateViewAccessor();
        _view.Write(0, FrameRingSource.Magic);
        _view.Write(4, (uint)FrameRingSource.Version);
        _view.Write(8, W);
        _view.Write(12, H);
        _view.Write(16, Slots);
        _view.Write(20, HeaderBytes);
    }

    [TearDown]
    public void Cleanup()
    {
        _view.Dispose();
        _file.Dispose();
    }

    /// <summary>Writes frame <paramref name="seq"/> (every byte <paramref name="fill"/>) as the game does.</summary>
    private void WriteFrame(long seq, byte fill)
    {
        var slot = (int)(seq % Slots);
        _view.Write(64 + slot * 32, 0L);
        _view.WriteArray(HeaderBytes + slot * FrameBytes, Enumerable.Repeat(fill, FrameBytes).ToArray(), 0, FrameBytes);
        _view.Write(64 + slot * 32, seq);
        _view.Write(24, seq);
    }

    [Test]
    public void FrameRing_reads_the_newest_whole_frame()
    {
        WriteFrame(1, 0x11);
        WriteFrame(2, 0x22);
        using var ring = FrameRingSource.Open(_name);
        Assert.That((ring.Width, ring.Height, ring.Slots, ring.FrameBytes), Is.EqualTo((W, H, Slots, FrameBytes)));
        var dst = new byte[ring.FrameBytes];
        Assert.That(ring.TryCopyLatest(dst, 0), Is.EqualTo(2));
        Assert.That(dst, Is.All.EqualTo(0x22));
        Assert.That(ring.TryCopyLatest(dst, 2), Is.EqualTo(0), "nothing newer than the frame already taken");
        Assert.That(ring.Closed, Is.False);
    }

    [Test]
    public void FrameRing_skips_a_slot_torn_mid_copy()
    {
        WriteFrame(1, 0x11);
        using var ring = FrameRingSource.Open(_name);
        var dst = new byte[ring.FrameBytes];
        // The game rewrites slot 1 during every copy: no frame is kept.
        Assert.That(ring.TryCopyLatest(dst, 0, () => _view.Write(64 + 1 * 32, 0L)), Is.EqualTo(0));

        // Torn once (the game finished frame 2 mid-copy of it), whole on the retry.
        WriteFrame(2, 0x22);
        var tears = 0;
        Assert.That(ring.TryCopyLatest(dst, 0, () =>
        {
            if (tears++ > 0) return;
            _view.Write(64 + 2 * 32, 0L);
            WriteFrame(4, 0x44);
        }), Is.EqualTo(4));
        Assert.That(dst, Is.All.EqualTo(0x44));
    }

    [Test]
    public void FrameRing_closed_ends_the_source()
    {
        WriteFrame(1, 0x11);
        using var ring = FrameRingSource.Open(_name);
        var source = new FrameRingVideoSource(ring, 60);
        var sink = new CountingSink();
        source.StartAsync(sink, CancellationToken.None);
        try
        {
            _view.Write(32, 1);
            Assert.That(ring.Closed, Is.True);
            Assert.That(source.Ended.Wait(TimeSpan.FromSeconds(5)), Is.True, "the pump stops once the game closes the ring");
            Assert.That(sink.Frames, Is.GreaterThan(0));
            Assert.That(sink.Last, Is.All.EqualTo(0x11));
        }
        finally
        {
            source.StopAsync().Wait();
        }
    }

    [Test]
    public void FrameRing_rejects_a_bad_header_or_missing_feed()
    {
        Assert.That(() => FrameRingSource.Open(Guid.NewGuid().ToString("B")),
            Throws.TypeOf<CaptureException>().With.Property(nameof(CaptureException.Error)).EqualTo(CaptureError.TargetNotFound));
        _view.Write(4, 2u);
        Assert.That(() => FrameRingSource.Open(_name), Throws.TypeOf<CaptureException>());
        _view.Write(4, 1u);
        _view.Write(0, 0u);
        Assert.That(() => FrameRingSource.Open(_name), Throws.TypeOf<CaptureException>());
    }

    private sealed class CountingSink : IFrameSink
    {
        public int Frames;
        public byte[] Last = [];

        public bool WriteFrame(ReadOnlySpan<byte> bgra)
        {
            Frames++;
            Last = bgra.ToArray();
            return true;
        }
    }
}
