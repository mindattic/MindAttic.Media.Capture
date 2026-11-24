# Engine host: `capture host`

An engine game that cannot load .NET (Unreal) captures itself through the installed agent: the game starts
`capture host` beside itself and drives it over stdin/stdout, one JSON object a line. The agent does what
MindAttic.Media.Capture.Unity's `CaptureUnity` does inside a Unity player. The Unreal plugin and any future engine
binding read this page; it replaces the protocol in SpaceGod.Unreal's `Capture/README.md`, which this command was
ported from.

```
 game                                          capture host
 ────                                          ────────────
 -capture on its command line?
   └─ has a window: start ─────────────────>   capture host --hwnd <window> --pid <game> --app Name [--version V]
                                                 [--defaults <shipped capture.json>] --cmdline "<game's command line>"
                                               builds CaptureOptions as CaptureUnity does, starts CaptureEngine on
                              <── ready ────   the window (Windows Graphics Capture)
 raise / marker / screenshot / command ───>    engine.Raise / Marker / ScreenshotAsync / ExecuteCommandAsync
 record-start/stop/pause/resume, clip,         StartRecordingAsync / StopRecordingAsync / Pause / Resume /
   save-replay ───────────────────────────>    ClipAsync / SaveReplayAsync; a screenshot's "set" joins a preview set
 note (its own "[capture] ..." log lines) ─>   <session folder>.game.log
                     <── log, result ──────    engine log lines, screenshot results, rejected requests
 quit, stdin closed, or the game exits ───>    stops; the session export runs detached (capture finalize)
                              <── stopped ──
```

## Starting

`capture host (--hwnd 0x.. | --frames {GUID}) --pid N --app Name [--version V] [--defaults path.json] --cmdline "..."`

- `--hwnd`: record the game's window (Windows Graphics Capture). `--frames {GUID}`: record the frames the game writes
  into a shared-memory ring of that name instead, for a game with no window (Unreal `-RenderOffScreen`); see
  [Frame feed](#frame-feed). One of the two is required when a capture mode is asked.
- `--cmdline` is the game's command line, split by Windows rules and parsed with `CaptureArgs` (`-capture video`,
  `-capture screen 5`, `-capture-config path`, ...).
- No capture mode asked (no `-capture`, or `-capture` followed by something that is not a mode): nothing starts; the
  host prints `{"op":"ready","any":false}` and exits 0. `--hwnd` / `--frames` are not needed then.
- `--defaults`: the game's shipped capture.json, applied after the `-capture` flags only when the command line names no
  `-capture-config`. A missing or invalid file is logged, not fatal. The replay ring stays armed only when the session
  records video.
- A capture.json `hotkeys` map is never bound: the game reads its own keys and sends `command` requests.
- `--app` names the output folder (default `Game`); `--version` is the session's app version. `--gdi` / `--no-wgc` pick
  the grabber as for `capture record`.
- `--pid`: the host stops when that process exits.
- The detached export (`capture finalize`) runs through this same exe.

## Protocol

Game to host (stdin):

| op | fields | does |
|---|---|---|
| `raise` | `event`, `data` | `Raise`: the capture.json rules see it, the manifest records it |
| `marker` | `name`, `data` | a chapter in the exported video |
| `screenshot` | `id`, `tag`, `data`, `set`, `caption` | `ScreenshotAsync`; answered with a `result` of the same `id` once written. With `set` (a preview set folder, from `capture set new --json`'s `dir`), the shot and its sidecar are also copied into the set as its next `NNN_tag` with `caption`, and `files` names the set's copies; the session keeps its own |
| `record-start` | `id`, `tag` (default `recording`), `data` | `StartRecordingAsync`; `result` `ok` once recording (error `Already recording.` when one runs) |
| `record-stop` | `id` | `StopRecordingAsync`; `result` with the exported `files` once written |
| `record-pause` | `id` | `PauseRecording`: footage from now until `record-resume` is left out; `result` `ok` |
| `record-resume` | `id` | `ResumeRecording`; `result` `ok` |
| `clip` | `id`, `before` (s, default 0, up to 3600), `after` (s, default 10, up to 600), `tag` (default `clip`), `data` | no `before`: `ClipAsync(after)`, footage from now; with `before`: `SaveReplayAsync(before, after)` (the past needs the replay ring); `result` once written |
| `save-replay` | `id`, `before` (default the session's replay length), `after` (default 0), `tag` (default `replay`), `data` | `SaveReplayAsync`; `result` once written |
| `command` | `command` | `ExecuteCommandAsync`: `screenshot`, `toggle-recording`, `save-replay` (Alt+F9/F10/F11) |
| `note` | `text` | appends a timestamped line to `<session folder>.game.log` |
| `quit` | | stop and export |

`data` is a flat object; numbers, strings, booleans and nulls keep their type, anything else is passed as its JSON
text.

Host to game (stdout):

| op | fields | when |
|---|---|---|
| `ready` | `any`, `enabled`, `folder`, `session`, `fps`, `source` (`window` or `frames`), or `error` | once, after start (`any` false: nothing started, the host exits) |
| `result` | `id`, `ok`, `files`, `error` | a screenshot, recording, clip or replay was written or failed, or a recording op took effect; or a request was rejected (`ok` false, `error` says why, `id` when the request had one) |
| `log` | `level`, `message` | an engine log line at Info or above; the game writes it to its own log |
| `stopped` | | the session stopped and its export was handed off; the host exits 0 |

An unknown `op`, a line that is not a JSON object, or a request missing a required field is answered with an error
`result`; the host keeps reading. So are `record-stop`, `record-pause` and `record-resume` while nothing is recording
(`Not recording.`), and a `screenshot` whose `set` folder does not exist. A `clip` or `save-replay` the engine cannot
cut (no tape running) is answered with the engine's error. Requests are answered in the order their captures finish,
not the order sent: match replies by `id`.

## Frame feed

A game with no window to record (Unreal `-RenderOffScreen` opens none, yet the GPU renders every frame) feeds the host
its own frames through a shared-memory ring. The layout is SpaceGod.Unreal's (`Cpp/Source/SpaceGod/Capture/FrameFeed.cpp`),
kept byte for byte, magic and version included, so its writer ports unchanged. The reader is
`src/MindAttic.Media.Capture.Windows/Frames/FrameRingSource.cs`.

1. The game creates a named file mapping (a page-file-backed shared memory section) of `headerBytes + slots * width *
   height * 4` bytes, named with a fresh `{GUID}` (`{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}`, no `Global\` prefix, so no
   administrator rights are needed), zeroes the header and fills it in. Width and height are even (the HQ tape encodes
   yuv420p straight from these frames).
2. It starts `capture host --frames {GUID} ...` (instead of `--hwnd`). The host opens the mapping read-only, checks the
   header and answers `ready` with `"source":"frames"` and `fps`; the game writes `fps` into the header and reads back
   frames at that rate.
3. Each frame goes into the next slot (below). The host's pump sends the newest whole frame to the tape every 1/fps (the
   last one again while nothing newer came: a hitch is a held frame, not a gap). A screenshot is the first frame fed
   after the request (up to 1 s; 10 s before any frame), else the newest.
4. On shutdown the game sets `closed` to 1 and unmaps its view; the host's pump sends the last frame and ends, and the
   host keeps its own view until it stops.

### Header (64 bytes at offset 0, little-endian)

| offset | type | field | value |
|---|---|---|---|
| 0 | uint32 | magic | `0x52464753` (the bytes `S G F R`) |
| 4 | uint32 | version | 1; the host refuses any other |
| 8 | int32 | width | frame width in pixels, >= 2 |
| 12 | int32 | height | frame height in pixels, >= 2 |
| 16 | int32 | slots | frame slots in the ring, >= 2 (SpaceGod writes 3) |
| 20 | int32 | headerBytes | where frame 0 starts, >= 64 + slots * 32 (SpaceGod writes 4096) |
| 24 | int64 | latest | sequence number of the newest whole frame (1, 2, ...); 0 before the first |
| 32 | int32 | closed | 1 once the game stops feeding |
| 36 | int32 | fps | the rate the game reads back at (the host's, from `ready`; informational) |
| 40 | int64[3] | reserved | 0 |

The host rejects a mapping whose magic, version or sizes are wrong, or that is smaller than
`headerBytes + slots * width * height * 4`, with a `ready` carrying `enabled` false and `error`.

### Slot records (32 bytes each, from offset 64)

Slot `i` is at `64 + i * 32`:

| offset in record | type | field | value |
|---|---|---|---|
| 0 | int64 | seq | the sequence number of the frame in this slot; 0 while the game writes it |
| 8 | int64 | cycles | the game's clock when written (SpaceGod: `FPlatformTime::Cycles64`; informational) |
| 16 | int64[2] | reserved | 0 |

### Frames

Frame slot `i` is `width * height * 4` bytes at `headerBytes + i * width * height * 4`: top-down BGRA, stride
`width * 4`, alpha ignored.

### Sync

One writer (the game's render thread), one reader (the host). Frame `seq` (1, 2, ...) goes into slot `seq % slots`.

The game, for each frame:

1. `seq = latest + 1`, `slot = seq % slots`.
2. Atomically set the slot's `seq` to 0.
3. Write the frame's pixels into the slot.
4. Atomically set the slot's `seq` to `seq`, then atomically set `latest` to `seq` (full barriers: `InterlockedExchange`).

The host, to take the newest frame newer than the one it has (up to three tries):

1. Read `latest`; nothing newer than what it has: no frame.
2. `slot = latest % slots`; read the slot's `seq`; not equal to `latest` (the game is rewriting it): try again.
3. Copy the slot's pixels, full memory barrier, read the slot's `seq` again.
4. Still equal: the copy is whole, keep it. Changed (the game overwrote the slot mid-copy): drop the copy, try again.

A torn copy is never sent to the tape: the pump copies into a spare buffer and swaps it in only when it was whole.
