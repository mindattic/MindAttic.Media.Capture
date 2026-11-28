using System.IO.MemoryMappedFiles;

namespace MindAttic.Media.Capture.Windows.Frames;

/// <summary>
/// The frames a game with no window to record (Unreal <c>-RenderOffScreen</c>) writes into a shared-memory ring for
/// <c>capture host --frames {GUID}</c>; this reads the newest whole frame. Layout (little-endian, docs/design/engine-host.md):
/// a 64-byte header (magic "SGFR", version 1 or 2, width, height, slots, headerBytes, int64 latest at 24, int32 closed at 32,
/// int32 fps at 36; version 2: int32 fixed fps at 40, int64 taken at 48, which the host writes), 32-byte slot records from 64 (int64 seq: 0 while being written), frames of width*height*4 top-down
/// BGRA from headerBytes. A frame is taken only when its slot's seq reads the same before and after the copy, so a frame
/// the game is overwriting is never torn into the tape. Ported from SpaceGod.Unreal's Capture/SpaceGod.Capture/FrameFeed.cs.
/// </summary>
internal sealed unsafe class FrameRingSource : IDisposable
{
    internal const uint Magic = 0x52464753;
    internal const int Version = 2;
    internal const int HeaderSize = 64;
    internal const int SlotSize = 32;

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte* _base;

    private FrameRingSource(MemoryMappedFile file, MemoryMappedViewAccessor view, byte* basePtr)
    {
        _file = file;
        _view = view;
        _base = basePtr;
        if (*(uint*)_base != Magic) throw new CaptureException(CaptureError.InvalidArgument, "The game's frame feed has no SGFR header.");
        if (*(int*)(_base + 4) is < 1 or > Version)
            throw new CaptureException(CaptureError.InvalidArgument, $"The game's frame feed is version {*(int*)(_base + 4)}; this host reads versions 1 to {Version}.");
        Width = *(int*)(_base + 8);
        Height = *(int*)(_base + 12);
        Slots = *(int*)(_base + 16);
        HeaderBytes = *(int*)(_base + 20);
        if (Width < 2 || Height < 2 || Slots < 2 || HeaderBytes < HeaderSize + Slots * SlotSize
            || (long)_view.Capacity < HeaderBytes + (long)Slots * FrameBytes)
            throw new CaptureException(CaptureError.InvalidArgument, $"The game's frame feed header is not valid ({Width}x{Height}, {Slots} slots).");
    }

    /// <summary>Opens the ring the game named on <c>--frames</c> (a {GUID}).</summary>
    public static FrameRingSource Open(string name)
    {
        MemoryMappedFile file;
        try
        {
            file = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            throw new CaptureException(CaptureError.TargetNotFound, $"The game's frame feed {name} is not there: {ex.Message}", ex);
        }
        MemoryMappedViewAccessor? view = null;
        try
        {
            view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
            byte* p = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
            try
            {
                return new FrameRingSource(file, view, p + view.PointerOffset);
            }
            catch
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
                throw;
            }
        }
        catch
        {
            view?.Dispose();
            file.Dispose();
            throw;
        }
    }

    public int Width { get; }
    public int Height { get; }
    public int Slots { get; }
    public int HeaderBytes { get; }
    public int FrameBytes => Width * Height * 4;

    /// <summary>The newest whole frame's sequence number; 0 before the first.</summary>
    public long Latest => Volatile.Read(ref *(long*)(_base + 24));

    /// <summary>The game stopped feeding.</summary>
    public bool Closed => Volatile.Read(ref *(int*)(_base + 32)) != 0;

    /// <summary>The rate the game reads back at (informational; the host's fps, which the game was told in <c>ready</c>).</summary>
    public int Fps => Volatile.Read(ref *(int*)(_base + 36));

    /// <summary>The game steps a fixed time per frame at this rate (<c>-capture-fixed</c>), or 0: it reads back every frame
    /// and waits for <see cref="Taken"/> before overwriting one, so the host takes each frame once, in order. Set before the
    /// host starts; 0 in a version 1 ring.</summary>
    public int FixedFps => Volatile.Read(ref *(int*)(_base + 40));

    /// <summary>The newest sequence number the host has copied out of the ring (the host writes it; the game reads it).</summary>
    public long Taken
    {
        get => Volatile.Read(ref *(long*)(_base + 48));
        set => Interlocked.Exchange(ref *(long*)(_base + 48), value);
    }

    /// <summary>Copies frame <paramref name="seq"/> into <paramref name="dst"/>: false when its slot holds another frame or
    /// the game overwrote it mid-copy.</summary>
    public bool TryCopy(Span<byte> dst, long seq)
    {
        var slot = (int)(seq % Slots);
        ref var slotSeq = ref *(long*)(_base + HeaderSize + slot * SlotSize);
        if (Volatile.Read(ref slotSeq) != seq) return false;
        new ReadOnlySpan<byte>(_base + HeaderBytes + (long)slot * FrameBytes, FrameBytes).CopyTo(dst);
        Interlocked.MemoryBarrier();
        return Volatile.Read(ref slotSeq) == seq;
    }

    /// <summary>Copies the newest frame into <paramref name="dst"/> when it is newer than <paramref name="after"/>: its
    /// sequence number, or 0 when there is none newer (or it was overwritten mid-copy three times running).</summary>
    public long TryCopyLatest(Span<byte> dst, long after) => TryCopyLatest(dst, after, null);

    /// <summary><see cref="TryCopyLatest(Span{byte}, long)"/>; <paramref name="afterCopy"/> runs between the copy and the
    /// second seq read (tests use it to tear a slot).</summary>
    internal long TryCopyLatest(Span<byte> dst, long after, Action? afterCopy)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var seq = Latest;
            if (seq <= after) return 0;
            var slot = (int)(seq % Slots);
            ref var slotSeq = ref *(long*)(_base + HeaderSize + slot * SlotSize);
            if (Volatile.Read(ref slotSeq) != seq) continue;
            new ReadOnlySpan<byte>(_base + HeaderBytes + (long)slot * FrameBytes, FrameBytes).CopyTo(dst);
            afterCopy?.Invoke();
            Interlocked.MemoryBarrier();
            if (Volatile.Read(ref slotSeq) == seq) return seq;
        }
        return 0;
    }

    public void Dispose()
    {
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _file.Dispose();
    }
}
