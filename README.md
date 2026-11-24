# MindAttic.Media.Capture

Programmatic video capture and screenshots for MindAttic games and apps: trailer footage, store screenshots, devlog clips and bug-repro video, triggered from code or recorded by hand from the tray.

![C#](https://img.shields.io/badge/C%23-.NET%2010-512BD4) ![Platform](https://img.shields.io/badge/platform-Windows-0078D4) ![Unity](https://img.shields.io/badge/Unity-6-black) ![Status](https://img.shields.io/badge/status-in%20development-orange)

## Why

- Get trailer, store-shot and devlog footage as a by-product of playing, not a separate recording session.
- Catch the moment: a replay saved on "boss-death" includes the seconds before it happened.
- Record any app with no code changes, or wire one line into a WPF app or a Unity game.
- Leave a long run unattended: a shut-off valve stops capture before the disk or memory runs out.
- Keep shipped builds clean: capture stays out of Release builds.

## Features

- Rolling lossless (FFV1) or hardware HEVC tape that clips, replays and whole sessions are cut from.
- Screenshots, clips, replays and sessions from code, trigger rules, hotkeys, a localhost API and a JavaScript bridge.
- A system-tray recorder (`capture-tray.exe`) for hand recording: screens, any open app, or a dragged area.
- The `-capture` command-line flag, understood by every wired MindAttic GUI app.
- Sidecar JSON per file and a session manifest, with markers written as video chapters.
- Crash recovery: finished segments stay on disk and are exported on the next launch or by `capture recover`.

```text
 your app / game                     MindAttic.Media.Capture                         R:\MindAttic\Media\Capture\
 ───────────────                     ───────────────────────                         ───────────────────────────
 Capture.Raise("critical-hit") ──>  trigger rules ──> replay / clip / screenshot ──>  {RepoName}\{RepoName}_{timestamp}.{ext}
 -capture video / screen N     ──>  session tape (1 s segments, lossless FFV1)  ──>    {RepoName}_{start}.mkv + .mp4
 ScreenshotAsync / ClipAsync   ──>  host adapter (desktop, WPF, WebView2, Unity)       *.png  *.mp4  *.json sidecars
                                                                                        session.json manifest
```

| Package (1.0.0 unless noted) | Target | What it does |
|---|---|---|
| `MindAttic.Media.Capture` | netstandard2.1; net10.0 | Core: API, `-capture` parsing, trigger rules, rolling tape, exports, sidecars, session manifest. No third-party dependencies (Unity consumes the netstandard2.1 build). |
| `MindAttic.Media.Capture.Windows` | net10.0-windows10.0.19041 | Desktop capture: Windows Graphics Capture for windows, Desktop Duplication / GDI for monitors and regions, WASAPI audio, global hotkeys, DI. |
| `MindAttic.Media.Capture.Wpf` | net10.0-windows10.0.19041 | `CaptureWpf.Init(this, e.Args)`: records the main window at native size, export on exit. |
| `MindAttic.Media.Capture.WebView2` | net10.0-windows10.0.19041 | `window.mindattic.capture` JavaScript bridge for any WebView2. |
| `MindAttic.Media.Capture.Sets` 2.0.0 | net10.0 | Preview sets: `preview.json` model, store (create, list, import under a per-set lock), media kinds, Media.Viewer handoff. |
| `MindAttic.Media.Capture.Web` | net10.0 | Headless web page screenshots and video through Playwright and the installed Microsoft Edge, with capture sidecars. |
| `capture.exe` (agent) | net10.0-windows10.0.19041 | CLI and localhost API: record any app with no code changes, serve browser pages and scripts, finalize and recover sessions. |
| `com.mindattic.media.capture` (Unity) | Unity 6, Windows | Game-view capture via GPU readback, game audio, Play Mode menu; reads `-capture` from the player command line. |

The agent and the tray recorder are installed together (see Install and deploy below). With the agent installed, apps hand their session export to it and exit immediately.

## Install and deploy

Run `run.ps1` from the repo root. It installs the agent (`capture.exe`) and tray recorder (`capture-tray.exe`) only when the source is newer than the installed copy, so it is cheap to run every time.

```powershell
.\run.ps1               # install if stale
.\run.ps1 -Force        # install even if the installed copy looks current
.\run.ps1 -Launch       # also start the tray recorder
```

- Binaries stay in `%LOCALAPPDATA%\MindAttic\Capture\bin`, where `DesktopCapture` finds the agent for other apps.
- It writes `C:\Apps\MindAttic.Media.Capture\run.bat` (under `%LOCALAPPDATA%\Programs` when `C:\Apps` is missing) and removes the pre-rename `C:\Apps\MindAttic.Capture` folder and `MindAttic Capture` shortcut. The desktop shortcut `MindAttic Media Capture` opens that file, which asks first: `1) Deploy & Run` reinstalls (`-Force -Launch`) and starts the tray recorder; `2) Run` just starts the installed tray recorder. `.\run.ps1 -LauncherOnly` rewrites the launcher and shortcut without installing anything.
- `tools\install.ps1` is the underlying installer; add `-AddToPath` to get `capture` on PATH everywhere. A file in use is renamed to `*.old` and replaced, so installing is safe while an export runs.

## Tray recorder

`capture-tray.exe` sits in the system tray and records without any code changes. The icon turns red with the elapsed time while recording, and a left-click stops it.

- Record Screen and Screenshot per monitor; Record App and Screenshot App pick any open window and capture it at its own size, filed under the app's name.
- Record Area and Screenshot Area dim every monitor and let you drag a rectangle: grips move and resize it, arrow keys nudge it (Shift for 10 px), aspect-ratio buttons snap it (16:9, 9:16, 1:1, and more), Enter confirms, Esc cancels. A red frame marks the area while it records.
- The selector toolbar chooses video files (lossless + MP4, lossless only, MP4 only, GIF) and image format (PNG, JPG). Areas can be saved as named regions, starting with a 9:16 "YouTube Short".
- Hotkeys: `Ctrl+Alt+Shift+R` starts and stops recording whatever is armed, `Ctrl+Alt+Shift+S` takes a screenshot. Both are rebindable in Settings.
- Finished captures are listed with their sizes in a notification; Open Last Capture and Show Last Capture in Folder are in the menu. Exit waits for a recording that is starting or saving.
- Settings (capture folder, default formats, start with Windows, hotkeys) and saved regions live in `%LOCALAPPDATA%\MindAttic\Capture\tray.json`.

Video is encoded by **ffmpeg** (`choco install ffmpeg`, or put `ffmpeg.exe` next to the app). Hardware encoders (NVENC → QuickSync → AMF) are probed automatically, with a software fallback.

## How it works

Everything video comes from one **tape**: a recorder writing 1-second segments to
`{root}\.staging\`. A recording, clip, replay or session is just a time range cut from the
tape. Starting a recording is instant, and `SaveReplay(before, after)` can include footage from
before the event. The tape is lossless FFV1 (`TapeQuality.Lossless`) or hardware HEVC
(`HighQuality`). Exports produce a `.mkv` master plus, depending on the preset, an H.264 `.mp4`
that imports into any editor, WebM, GIF or animated WebP, and a thumbnail.

## The `-capture` flag

Every MindAttic GUI app wired to MindAttic.Media.Capture understands the same flag:

```
Game.exe -capture video                 # film from start to exit at the app's native resolution
Game.exe -capture screen 5              # a PNG every 5 seconds until exit (also 500ms, 10s, 1m)
Game.exe -capture video screen 5        # both
Game.exe -demo -capture video hq        # hardware HEVC tape (~0.2 GB/min) instead of lossless
Game.exe -capture video 8h              # stop after 8 hours (the shut-off valve can stop it sooner)
Game.exe -capture screen 15m 5h gif     # a PNG every 15 minutes for at most 5 hours, then a timelapse GIF
Game.exe -capture                       # same as -capture video
Game.exe -capture video -capture-config capture.json   # explicitly opt into settings/rules from a file
```

Capture is off unless the `-capture` flag is literally on the command line: there is no environment
variable and no project config auto-loaded on launch, so a plain launch is always a plain launch.
`-capture-config path.json` is the one way to bring in settings or trigger rules, and it is opt-in —
you have to pass it yourself.

`-capture` followed by anything that isn't a capture word (for example an app's own `-capture <folder>`)
is left alone. `-demo` and other flags belong to the app: capture only records what runs.

When the app closes, the session goes to `R:\MindAttic\Media\Capture\{RepoName}\{RepoName}_{session start}.mkv`
and `.mp4`, alongside every screenshot, clip and replay its events triggered. If `capture.exe` is
installed, the export runs in a detached process so the app exits immediately. If the app
crashes, a job object stops ffmpeg, the finished segments stay on disk, and the next launch (or
`capture recover`) exports them as `session-recovered`.

If `R:` is missing, output falls back to `%USERPROFILE%\Videos\MindAttic` with a warning.

### The shut-off valve

A long run can't fill the disk or exhaust memory. While capture runs, the engine polls the
machine every 5 s. The first limit crossed stops capture and exports everything recorded so far,
exactly as if the app had closed. The app keeps running, and nothing is deleted.

| Limit (`limits` in capture.json) | Default | Why |
|---|---|---|
| `minFreeDiskGB` | 50 | output (and staging) drive nearly full |
| `minFreeMemoryMB` | 2048 | the machine is running out of RAM |
| `maxEncoderMemoryMB` | 4096 | frames are backing up inside ffmpeg (the drive or CPU can't keep up) |
| `maxEncoderLagSeconds` | 60 | the encoder is that far behind real time |
| `maxDuration` | off | `-capture video 8h`, or `"maxDuration": "8h"` |

Set a limit to 0 to turn it off, or `"enabled": false` to turn off all the resource limits; a duration cap (`-capture video 8h` or `"maxDuration"`) still applies. Flags on the command line win over the config file.
`pollEvery` changes the interval. Every poll is appended to `{RepoName}_{start}.stats.csv`. That
file records free disk, available RAM, CPU, app and encoder memory, encoder lag, tape time,
frames written and dropped, and the drive's write rate. The engine log goes to
`{RepoName}_{start}.log`, with a status line each minute. The session manifest records the
lowest and highest values seen, plus `stoppedBy` when the valve closed.

## Quick start

**WPF app** (one line in `App.OnStartup`, before showing the main window):

```csharp
CaptureWpf.Init(this, e.Args);   // -capture video / -capture screen N; capture.json beside the exe for rules
```

The app's project needs `net10.0-windows10.0.19041.0` (or later) and `MINDATTIC_CAPTURE` in
`DefineConstants` for `Capture.Raise` hooks. Automata is the worked example.

**Unity game:** add to `Packages/manifest.json`
`"com.mindattic.media.capture": "file:../../MindAttic.Media.Capture/unity/com.mindattic.media.capture"`. Players
then honor `-capture video` / `-capture screen 5` and nothing else — there is no project config
auto-loaded from StreamingAssets, so a plain launch never has capture silently on or off by
surprise; in the editor use **MindAttic ▸ Capture** to record Play Mode. Game code raises events
with `CaptureUnity.Raise("battle-complete")`; to bring in rules, pass `-capture-config path.json`
explicitly.

**Websites:** `C:\Apps\MindAttic.Browser\MindAttic.Browser.exe <url | site folder> -capture video`. Capture
always uses this deployed copy of [MindAttic.Browser](https://github.com/mindattic/MindAttic.Browser) (`capture run --url`
included); deploy or refresh it with that repo's `run.ps1`. A folder is served at
`https://<folder>.local/`, and its captures are named after the folder (`mindattic.com\mindattic.com_<timestamp>.mkv`)
or, for generic folders like `dist` or `wwwroot`, after the repository. A URL's captures are named after its host. Pages (in the browser, any WebView2 with the bridge, or plain browsers
with `capture serve`) call:

```js
mindattic.capture.raise('critical-hit', { damage: 912 });
await mindattic.capture.screenshot('victory');
await mindattic.capture.replay(15, 5, 'boss-death');
```

**Windows app (in-process, any UI framework):**

```csharp
await using var capture = await DesktopCapture.StartAsync(CaptureTarget.CurrentApp, args,
    o => { o.AppName = "GridGame"; o.ReplayArmed = true; });

// anywhere in game/app code (compiled out when MINDATTIC_CAPTURE is not defined):
Capture.Raise("critical-hit", new CaptureData { ["damage"] = 912 });
Capture.Marker("boss-phase-2");
Capture.Screenshot("victory");

// or the instance API:
var shot   = await capture.Engine.ScreenshotAsync("store-shot");
var clip   = await capture.Engine.ClipAsync(TimeSpan.FromSeconds(10), "devlog", new() { Preset = CapturePreset.Devlog });
var replay = await capture.Engine.SaveReplayAsync(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(5), "boss-death");
capture.Engine.Triggers.On("boss-death").Replay(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(5)).Cooldown(TimeSpan.FromSeconds(30));
```

**Any app, no code changes (agent):**

```
capture run -- Game.exe -demo                  # record the app's window from launch to exit
capture run --window "Notepad" -- notepad.exe  # launcher stubs: name the real window
capture run --url http://localhost:5173        # record a web app in the deployed MindAttic Browser
capture shot --region 0,0,1920,1080 --format png
capture record --monitor 0 --duration 30s --preset devlog
capture replay --window "GridGame" --length 30s   # Enter or `capture save` saves the last 30 s
capture serve --window "Tutor"                    # localhost API for browser pages and scripts
capture raise critical-hit damage=900              # send an event to `capture serve`
capture recover R:\MindAttic\Media\Capture       # export sessions left by crashed apps
capture gif R:\MindAttic\Media\Capture\Automata --fps 4 # stitch a folder's PNGs into a timelapse GIF
```

`capture gif <folder>` takes the folder's PNGs in name order and writes `{folder}\{folder name}.gif` (or
`--output file.gif`) at `--fps` 2 or 4 (default 2), `--width` capped at 1280 by default, each frame stamped with its
time and the last held 2 s. Code can call `CaptureEngine.ExportGifAsync(frames, fps)` directly.

`gif` in the `-capture` flag (or `"gif": true` in capture.json, with `"gifFps"` and `"gifWidth"`) does the same for a
session's interval screenshots when the session ends, early or not, writing `{RepoName}_{start}.gif` beside them.
A session with fewer than 2 screenshots makes no GIF, and a GIF that fails is a warning in the session log, not a
failed session.

Targets: `--monitor N`, `--region x,y,w,h`, `--window "title"`, `--hwnd 0x1234`, `--process name`.

## capture.json

Placed beside the exe (picked up automatically), or passed with `-capture-config` / `--config`:

```json
{
  "enabled": true,
  "outputRoot": "R:\\Recording",
  "fileTemplate": "{app}_{timestamp}",
  "tapeQuality": "Lossless",
  "audio": "App",
  "replay": { "armed": true, "seconds": 30 },
  "rules": [
    { "on": "critical-hit", "action": "replay", "before": "5s", "after": "2s",
      "where": { "damage": ">=500" }, "debounce": "1s", "maxPerSession": 25 },
    { "on": "battle-complete", "action": "screenshot", "tag": "victory", "cooldown": "10s" },
    { "on": "boss-death", "action": "replay", "before": "15s", "after": "5s", "preset": "Trailer" },
    { "on": "battle-*", "action": "marker" }
  ],
  "hotkeys": { "F9": "screenshot", "F10": "toggle-recording", "F11": "save-replay" },
  "diskWarnGB": 500,
  "limits": { "minFreeDiskGB": 50, "minFreeMemoryMB": 2048, "maxEncoderMemoryMB": 4096, "maxEncoderLagSeconds": 60, "maxDuration": "8h" },
  "postCapture": [ { "on": "replay", "run": "pwsh -File notify.ps1 \"{path}\"" } ]
}
```

- **`enabled`:** (default `true`) `false` makes every capture call a no-op. Only takes effect via an
  explicitly-loaded file (`-capture-config path.json`) — there is no auto-loaded project config and
  no environment variable, so this never turns capture off behind your back; an explicit `-capture ...`
  on the same command line always wins over it anyway.
- **Actions:** `screenshot`, `replay`, `clip`, `marker`, `start-recording`, `stop-recording`, `toggle-recording`.
- **Rule controls:** `where`, `cooldown`, `debounce`, `maxPerSession`, `enabled`, `preset`, `format`.
- **Presets:**
  - `Trailer`: lossless master + high-quality MP4
  - `Lossless`
  - `Devlog`: MP4, 1080p max
  - `BugReport`: 720p30 MP4
  - `Session`
- **Disk usage:** nothing is ever deleted automatically. `diskWarnGB` only warns.

## Localhost API (`capture serve`)

- **Endpoint:** `http://127.0.0.1:47800/`.
- **Who can call it:**
  - Browser pages on `localhost`/`127.0.0.1` origins.
  - Other origins added with `--allow-origin`.
  - Non-browser clients that send the `X-Capture-Token` header from
    `%LOCALAPPDATA%\MindAttic\Capture\agent.json`.
- **Operations:** `POST /raise {event, data}`, `/marker`, `/screenshot`, `/clip {seconds}`,
  `/replay {before, after}`, `/record/start|stop|pause|resume`, `GET /status`.
- **WebSocket:** `/ws` accepts the same operations as `{op, ...}` messages.
- **Commands:** the API can never run commands; `postCapture` only comes from config files.

## Preview sets (`capture set`)

A preview set is one folder of numbered media plus `preview.json` (schema `mindattic.preview/1`) that an agent
fills and MindAttic.Media.Viewer shows; see [docs/design/preview-sets.md](docs/design/preview-sets.md). Sets live
in `R:\MindAttic\Media\Preview\{Repo}\{yyyy-MM-ddTHH.mm.ss}_{TICKET}_{slug}\` (`%USERPROFILE%\Pictures\MindAttic\Preview`
when `R:` is missing) and hold `NNN_{tag}.{ext}` files with their sidecars.

```powershell
$set = capture set new --title "exam caption" --ticket GAD-T725      # prints the set folder
capture shot --window "Gadfly" --set $set --tag desktop --caption "Desktop 1440x900"
capture record --monitor 0 --duration 10s --set $set --tag walkthrough
capture set add $set C:\temp\q1.wav --caption "Examiner voice, Q1"  # --move to move instead of copy
capture set list --repo Gadfly --limit 5
capture set show $set                                                # pipe | launched | photos | none
```

`--set` makes shot and record write to a staging folder inside the set and then import the files as the set's next
number, sidecars included. `capture set new --json` prints the manifest with the set folder as `dir` (the same
field `capture set list --json` gives each set); `preview.json` itself never stores the folder.
`capture set show` hands the set to a running Media.Viewer, else starts it
(`C:\Apps\MindAttic.Media.Viewer\MindAttic.Media.Viewer.exe`, or `MINDATTIC_MEDIA_VIEWER` when set), else opens the first image with Photos, and prints
which; `--no-launch` only lists the items. The library behind it, `MindAttic.Media.Capture.Sets`, is packed to
`C:\LocalNuGet`.

## Output

Everything goes to `R:\MindAttic\Media\Capture\{RepoName}\`, one folder per app run. The session's timestamp is UTC,
ISO 8601 with `:` replaced by `.` (Windows file names cannot contain `:`), and every file inside starts with the
same `{timestamp}_{RepoName}`, so a file copied elsewhere still says what it shows:

```
R:\MindAttic\Media\Capture\UnitySolarSystem\
  2026.10.11T14.38.02Z_UnitySolarSystem\                        one app run (session)
    2026.10.11T14.38.02Z_UnitySolarSystem.session.json           manifest: app, version, events, markers, captures, diagnostics
    2026.10.11T14.38.02Z_UnitySolarSystem.log / .stats.csv       the session's log and encoder stats
    2026.10.11T14.38.02Z_UnitySolarSystem_Video.mkv / .mp4 / .thumb.jpg / .mkv.json   the -capture video session
    2026.10.11T14.38.02Z_UnitySolarSystem_Screenshot_000.png / .png.json              a screenshot and its sidecar
    2026.10.11T14.38.02Z_UnitySolarSystem_Replay_001.mkv / .mp4                       a replay, clip or recording
```

`{RepoName}` is the git repository the app runs from (an app in `bin\Debug`, a Unity project, a local site
folder), so projects that share a product name (GridGame, GridGame2025, GridGame2026) still get separate
folders. Unity players carry it from the build. Outside a repo it falls back to the app name. If `R:` is
missing, output goes to `%USERPROFILE%\Videos\MindAttic\Capture` with a warning. `fileTemplate` changes the
file names (default `{session}_{app}_{kind}_{index}`; tokens `{timestamp} {date} {time} {session} {tag} {index}
{kind} {app}`). `tools\prefix-existing-captures.ps1` renamed captures made under the older flat layout.
Each sidecar records:
- kind, the tag (e.g. the tour stop or event name), app, build version and session
- host, timestamp and the trigger that fired
- tape range, with markers as offsets
- your custom key/values

Markers are also written into exported video as chapters.

### Moved roots (2026-11)

Captures moved from `R:\MindAttic\Capture` to `R:\MindAttic\Media\Capture`, and preview sets from
`R:\MindAttic\Preview` to `R:\MindAttic\Media\Preview` (M.CAPT-T202). For one release the old roots are still read
(`capture recover`, set listings and set lookups by id, the Unity Open Capture Folder) but never written
(M.CAPT-T203). `capture migrate-roots` lists what would move;
`capture migrate-roots --apply` moves it, never deleting or overwriting, and leaves a `README.md` in each old root
(M.CAPT-T204). See [docs/MIGRATION.md](docs/MIGRATION.md#media-roots).

## Production builds

Capture is a development tool and stays out of shipped builds:

- **.NET apps:** reference the packages only outside Release. Automata is the example:
  ```xml
  <ItemGroup Condition="'$(Configuration)' != 'Release'">
    <PackageReference Include="MindAttic.Media.Capture.Wpf" Version="1.0.0" />
  </ItemGroup>
  ```
  The packages define `MINDATTIC_CAPTURE`, so wrap the init call and hooks in `#if MINDATTIC_CAPTURE`.
  Release builds then contain no capture code at all. The packages come from the workspace feed
  `C:\LocalNuGet` (refresh with `dotnet pack src\<project> -c Release -o C:\LocalNuGet`).
- **Unity:**
  - Capture runs in the editor, in development builds, and in players built with
    **MindAttic ▸ Capture ▸ Build Windows Player (Capture Enabled)** (adds the `MINDATTIC_CAPTURE`
    define, output `Builds/Capture/`).
  - Normal production players ignore `-capture` (one log line) and never start ffmpeg.
  - Unity projects reference the package by path
    (`file:../../MindAttic.Media.Capture/unity/com.mindattic.media.capture`), which needs this repo checked out next
    to them, the same as any sibling-repo dependency here. Once the repo is pushed it can be switched to
    `https://github.com/mindattic/MindAttic.Media.Capture.git?path=/unity/com.mindattic.media.capture#<tag>` if you
    prefer pinned versions.
  - After changing the core, run `tools\update-unity-plugin.ps1`.

## Limits worth knowing

- **Lossless is big.** FFV1 is roughly 1–7 GB per minute depending on resolution (a 5120x1440
  desktop measured about 7 GB/min). Use `-capture=hq` for long sessions.
- **Window capture size.** It uses the window's size when capture starts; later resizes are
  cropped or padded.
- **Windows 10 border.** Windows 10 shows a yellow capture border (Windows 11 doesn't).
- **H.264 width.** Hardware H.264 is limited to 4096 px wide; wider MP4s use x264.
- **Audio.** "App" audio is currently the whole-system mix (WASAPI loopback). Per-process audio
  capture is a later step. If the host can't create its audio tracks, the recording is made
  video-only instead of failing.
- **ffmpeg licensing.** The common Windows builds are GPL. That's fine as a separate exe for
  internal use. Public builds that bundle ffmpeg need an LGPL build or GPL compliance.

## Development

```
dotnet build MindAttic.Media.Capture.slnx
dotnet test  MindAttic.Media.Capture.slnx      # engine and desktop tests need ffmpeg and an interactive desktop
```

Source layout: `src\` holds the core, Windows, Wpf, WebView2, Sets, Web, Agent and Tray projects
(`MindAttic.Media.Capture.*`, namespaces the same); `tests\` holds
`MindAttic.Media.Capture.Tests` and `MindAttic.Media.Capture.Windows.Tests`; `unity\` is the Unity package and `js\`
the JavaScript client. `capture recover` with no root also scans the fallback output folder.

## MindAttic.Media.Capture.Unity

The Unity package lives in `unity\com.mindattic.media.capture` and shows in Package Manager as
MindAttic.Media.Capture.Unity. Consumers reference it from `Packages/manifest.json` by path (see below);
`tools\update-unity-plugin.ps1` rebuilds the core DLL into its `Runtime/Plugins`.

**Naming.** Engine packages carry the engine as a suffix: MindAttic.Media.Capture.Unity and
MindAttic.Media.Capture.Unreal. The Unity package id stays `com.mindattic.media.capture`, as does its folder: about
twelve Unity projects were pointed at that id and path in M.CAPT-T167, and renaming it again would mean another
`manifest.json` and `packages-lock.json` edit and re-import in each for no runtime gain. UPM ids are lowercase
reverse-DNS names of the product, and this package can only be the Unity one. The suffix already appears where code
sees it: the asmdefs `MindAttic.Media.Capture.Unity` (`.Editor`, `.Tests`) and the namespace
`MindAttic.Media.Capture.Unity`.

## Formerly MindAttic.Capture

The repo, projects, packages, namespaces and Unity package were renamed to MindAttic.Media.Capture
(M.CAPT-T170 to T179). The new package ids start again at 1.0.0:

| Was | Now |
|---|---|
| `MindAttic.Capture` 2.0.0 | `MindAttic.Media.Capture` 1.0.0 |
| `MindAttic.Capture.Windows` 1.0.0 | `MindAttic.Media.Capture.Windows` 1.0.0 |
| `MindAttic.Capture.Wpf` 1.0.0 | `MindAttic.Media.Capture.Wpf` 1.0.0 |
| `MindAttic.Capture.WebView2` 2.0.0 | `MindAttic.Media.Capture.WebView2` 1.0.0 |
| `MindAttic.Capture.Sets` 2.0.0 | `MindAttic.Media.Capture.Sets` 1.0.0, now 2.0.0 (old Media.Viewer fallbacks removed) |
| `MindAttic.Capture.Web` 1.0.0 | `MindAttic.Media.Capture.Web` 1.0.0 |
| namespaces `MindAttic.Capture.*` | `MindAttic.Media.Capture.*` |
| Unity `com.mindattic.capture` (`unity/com.mindattic.capture`) | `com.mindattic.media.capture` (`unity/com.mindattic.media.capture`) |
| `MindAttic.Capture.slnx`, github.com/mindattic/MindAttic.Capture | `MindAttic.Media.Capture.slnx`, github.com/mindattic/MindAttic.Media.Capture |
| `C:\Apps\MindAttic.Capture`, "MindAttic Capture" shortcut | `C:\Apps\MindAttic.Media.Capture`, "MindAttic Media Capture" shortcut (`run.ps1` removes the old ones) |

To move a consumer: change each `PackageReference` id (and version to 1.0.0), replace `using MindAttic.Capture`
with `using MindAttic.Media.Capture`, and point `Packages/manifest.json` at
`"com.mindattic.media.capture": "file:../../MindAttic.Media.Capture/unity/com.mindattic.media.capture"`.
[docs/MIGRATION.md](docs/MIGRATION.md) lists the consumers moved and their tickets.

These names stay on purpose, so installed tools, scripts and saved output keep working:

- the `capture.exe` and `capture-tray.exe` executables, installed in `%LOCALAPPDATA%\MindAttic\Capture\bin`, with
  `tray.json` and `agent.json` beside them in `%LOCALAPPDATA%\MindAttic\Capture`;
- the `MINDATTIC_CAPTURE` define, the `MINDATTIC_CAPTURE_*` environment variables (`MINDATTIC_CAPTURE_AGENT`) and
  the `MindAtticCaptureStrip` MSBuild property;
- the JavaScript bridge `window.mindattic.capture`;
- runtime ids: event and mutex names, the settings app name, thread names, log and trace tags, Unity EditorPrefs keys,
  GameObject names and the **MindAttic ▸ Capture** menu all keep `MindAttic.Capture`. The agent's product name is
  now `MindAttic.Media.Capture.Agent`; the old `MindAttic.Capture.Agent` is still accepted for one release.

## Documentation

This README is also the home page of the published docs at https://mindattic.github.io/MindAttic.Media.Capture/
(built by `.github\workflows\docs.yml` through the shared DocFX pipeline).

- [User stories](docs/USER_STORIES.md)
- [Agent instructions](AGENTS.md)

## License

No license file: all rights reserved.

Part of [MindAttic](https://mindattic.com) — see more projects at [github.com/mindattic](https://github.com/mindattic).
