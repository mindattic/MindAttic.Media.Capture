# MindAttic.Media.Capture.Unreal

MindAttic.Media.Capture for Unreal games: what the Unity package (`unity\com.mindattic.media.capture`) is to a Unity
player. `-capture video` or `-capture screen N` on the game's command line records the game; events, markers,
screenshots, recordings and preview sets are one call from C++ or Blueprints. Win64 only. Output goes to
`R:\MindAttic\Media\Capture\<AppName>\`, the same sessions, sidecars and manifests as every other MindAttic capture.

Capture is off unless `-capture` is literally on the command line, so a plain launch is a plain launch.

## How it differs from Unity

C++ cannot load .NET, so nothing is encoded in the game. On `-capture` the plugin starts the installed agent's
`capture host` as a child process, hands it the game's window (or its own frames, see Flags) and the game's command
line, and talks to it over stdin/stdout, one JSON object a line. The host does what `CaptureUnity` does inside a Unity
player: builds the options from the `-capture` flags, runs the engine, applies the rules, exports the session. The
session export runs detached, so the game exits at once. The protocol is in
[docs/design/engine-host.md](../../docs/design/engine-host.md).

## Prerequisites

- The capture agent (`capture.exe`) installed: run this repo's `run.ps1` (Redeploy), which installs it to
  `%LOCALAPPDATA%\MindAttic\Capture\bin`. The plugin looks, in this order, at `MINDATTIC_CAPTURE_HOST`,
  `MINDATTIC_CAPTURE_AGENT`, `Capture\capture.exe` under the game's root folder, `%LOCALAPPDATA%\MindAttic\Capture\bin`,
  then every folder on `PATH`. A file is only taken if its product name is `MindAttic.Media.Capture.Agent`.
- `ffmpeg` on `PATH`, for video.
- With no agent found, the game logs an error (`LogMindAtticCapture`) and runs with capture off.

## Install

Reference the plugin by path; nothing is copied into the project.

1. In the `.uproject`, point `AdditionalPluginDirectories` at the repo's `unreal` folder (relative to the `.uproject`)
   and enable the plugin:

   ```json
   {
     "AdditionalPluginDirectories": [ "../../MindAttic.Media.Capture/unreal" ],
     "Plugins": [ { "Name": "MindAtticMediaCapture", "Enabled": true } ]
   }
   ```

   Adjust the relative path to where this repo is checked out beside the game.
2. To call the C++ API, add the module to the game module's `Build.cs`:

   ```csharp
   PublicDependencyModuleNames.Add("MindAtticMediaCapture");
   ```

3. Regenerate the project files and build. The plugin has two modules: `MindAtticMediaCapture` (Runtime) and
   `MindAtticMediaCaptureEditor` (Editor).

The module ticks the capture itself and shuts it down on exit; the game only raises events, markers, screenshots and
commands.

## Flags

| Flag | Does |
|---|---|
| `-capture` / `-capture video` | Film the game from start to exit, lossless at the window's size. |
| `-capture screen N` | A PNG every N seconds (`500ms`, `10s`, `1m` also work). Combine: `-capture video screen 5`. |
| `-capture video hq` | Hardware HEVC tape instead of lossless. |
| `-capture video 8h` | Stop after a duration. |
| `-capture-config path.json` | Bring in settings and trigger rules from a file; the only way to load one from the command line. |
| `-capture-source=frames` / `window` | `frames` records the game's own rendered frames through a shared-memory ring; `window` forces window capture. |
| `-RenderOffScreen` | No window exists, so the frame feed is used automatically. |
| `-capture-fixed` / `-capture-fixed=<fps>` | With the frame feed: a frame-perfect tape for a game on a fixed time step (60 when no rate is given). Every rendered frame is read back and the game waits for the host to take each one, so the tape at fps/1 holds each frame once, in order, however slow the game or the lossless encoder; no audio. The game puts the engine on the fixed step itself (`FApp::SetUseFixedTimeStep` / `SetFixedDeltaTime`); see [engine-host.md](../../docs/design/engine-host.md#fixed-step). |
| `-CaptureStats` | Logs the game's fps and frame times as a baseline, with no capture. |

The game's whole command line goes to the host, which parses `-capture` as the repo [README](../../README.md)
documents (flag grammar, shut-off valve limits, `capture.json`).

## Settings

Project Settings, Plugins, Media Capture (`DefaultGame.ini`):

- `AppName`: the folder under `R:\MindAttic\Media\Capture`. Empty: the repo folder that holds the `.uproject`, else
  the project name.
- `DefaultsFile`: the game's shipped `capture.json`, relative to the project folder (or absolute). It is applied after
  the `-capture` flags, and only when the command line has no `-capture-config`. Empty: none.

## C++ API

`#include "MindAtticCapture.h"`, namespace `MindAttic::Media::Capture`. Every call is cheap and does nothing when
capture is off.

```cpp
using namespace MindAttic::Media::Capture;

Raise(TEXT("battle-complete"), Data().Num(TEXT("turns"), 12).Str(TEXT("map"), TEXT("Ridge")));
Marker(TEXT("Boss fight"));
int32 id = Screenshot(TEXT("store-shot"));
bool ok; FString file;
if (ScreenshotResult(id, ok, file)) { /* answered */ }
```

- State: `Requested()`, `Ready()`, `Any()`, `Enabled()`, `SessionFolder()`.
- Events: `Raise(event, data)` feeds the capture.json rules' `where` conditions; `Marker(name, data)` adds a chapter to
  the exported video; `Command("screenshot" | "toggle-recording" | "save-replay")`; `Note(line)` appends to the
  session's `.game.log`.
- Requests, each returning an id (0 when capture is off): `Screenshot(tag, data, setDir, caption)`,
  `StartRecording(tag)`, `StopRecording()`, `PauseRecording()`, `ResumeRecording()`, `Clip(before, after, tag)`,
  `SaveReplay(before, after, tag)`. Poll with `ScreenshotResult(id, ok, fileOrError)` or
  `Result(id, ok, files, error)`; each result is handed out once, in any order.
- Preview sets: see below.
- `BeginEditorSession(modes)`, `EndEditorSession()`, `Tick()` and `Shutdown()`; the modules call these themselves.

## Blueprint API

`UMindAtticCaptureLibrary` (script name `MindAtticCapture`, category `MindAttic|Capture`): `IsCaptureRunning`,
`SessionFolder`, `Raise`, `Marker`, `StartRecording`, `StopRecording`, `PauseRecording`, `ResumeRecording`,
`SaveReplay`, `Clip`, `NewPreviewSet`, `AddToPreviewSet`, `ShowPreviewSet`. Requests return an id, 0 when capture is
off.

Two latent nodes wait for results without blocking the game thread: **Take Capture Screenshot** (`OnDone` gives the
file, `OnFailed` the error; takes an optional preview set folder and caption) and **Wait For Capture Result** (takes the
id from a recording, clip or replay request; `OnDone` gives its files).

## Preview sets

Preview sets are the folders Media.Viewer opens (`capture set`). They run the agent and work whether or not `-capture`
is on:

- `NewPreviewSet(title, ticket)` returns the set's folder, or empty if the agent is missing or failed.
- `AddToPreviewSet(dir, files, tag, caption)` copies files in and returns the set's copies.
- `ShowPreviewSet(dir)` opens the set in the Viewer.
- `LastPreviewSet(repo)` (C++) returns the newest set's folder.
- A screenshot with a `setDir` also lands in the set with its caption.

## Editor

The editor module adds **Tools, MindAttic, Capture**:

- **Record Video In Play** and **Screenshot Every N s In Play**: Play-In-Editor sessions capture as `-capture video` /
  `-capture screen N` would. The switches and the interval are in Editor Preferences, Plugins, Media Capture.
- **Take Screenshot**: a screenshot of the running Play-In-Editor session.
- **Open Capture Folder**: the running session's folder, else `R:\MindAttic\Media\Capture\<AppName>`.
- **Open Last Preview Set**: this app's newest set in Media.Viewer.

When the editor itself was started with `-capture`, it is the editor that is captured and Play-In-Editor sessions add
nothing.

## Packaging

A packaged game finds the agent through the same search. To ship it with the build, copy the installed agent's whole
`bin` folder (`%LOCALAPPDATA%\MindAttic\Capture\bin`) into a `Capture` folder at the package root, so
`Capture\capture.exe` sits beside the `Engine` and project folders. A Shipping game writes no log of its own;
`Note(line)` keeps its lines in the session's `.game.log`. A game that opts into rules ships its `capture.json` and
names it in `DefaultsFile`.
