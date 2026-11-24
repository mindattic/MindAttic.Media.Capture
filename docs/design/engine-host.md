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
 note (its own "[capture] ..." log lines) ─>   <session folder>.game.log
                     <── log, result ──────    engine log lines, screenshot results, rejected requests
 quit, stdin closed, or the game exits ───>    stops; the session export runs detached (capture finalize)
                              <── stopped ──
```

## Starting

`capture host --hwnd 0x.. --pid N --app Name [--version V] [--defaults path.json] --cmdline "..."`

- `--cmdline` is the game's command line, split by Windows rules and parsed with `CaptureArgs` (`-capture video`,
  `-capture screen 5`, `-capture-config path`, ...).
- No capture mode asked (no `-capture`, or `-capture` followed by something that is not a mode): nothing starts; the
  host prints `{"op":"ready","any":false}` and exits 0. `--hwnd` is not needed then.
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
| `screenshot` | `id`, `tag`, `data` | `ScreenshotAsync`; answered with a `result` of the same `id` once written |
| `command` | `command` | `ExecuteCommandAsync`: `screenshot`, `toggle-recording`, `save-replay` (Alt+F9/F10/F11) |
| `note` | `text` | appends a timestamped line to `<session folder>.game.log` |
| `quit` | | stop and export |

`data` is a flat object; numbers, strings, booleans and nulls keep their type, anything else is passed as its JSON
text.

Host to game (stdout):

| op | fields | when |
|---|---|---|
| `ready` | `any`, `enabled`, `folder`, `session`, `fps`, `source` (`window`), or `error` | once, after start (`any` false: nothing started, the host exits) |
| `result` | `id`, `ok`, `files`, `error` | a screenshot was written or failed; or a request was rejected (`ok` false, `error` says why, `id` when the request had one) |
| `log` | `level`, `message` | an engine log line at Info or above; the game writes it to its own log |
| `stopped` | | the session stopped and its export was handed off; the host exits 0 |

An unknown `op`, a line that is not a JSON object, or a request missing a required field is answered with an error
`result`; the host keeps reading.
