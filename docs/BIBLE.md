---
layer: bible
status: living
---
# MindAttic.Media.Capture — Project Bible

> Single source of truth for what MindAttic.Media.Capture IS, is NOT, and the rules that keep it coherent.
> README says how to build and run it; this says how to think about it. The finish line lives in
> `docs/RELEASE.md`.

## 1. The one sentence {#M.CAPT-§1}
Programmatic and hand-driven video capture and screenshots for MindAttic apps and games: one rolling lossless tape
per engine, from which recordings, clips, replays and whole sessions are cut, written with sidecars and a session
manifest under `R:\MindAttic\Media\Capture\{RepoName}`.

## 2. The product promise {#M.CAPT-§2}
- **Footage as a by-product.** Wire one line (WPF, WebView2, Unity, Unreal through `capture host`) or record any app
  with no code change (`capture.exe`, the recorder app).
- **Catch the moment.** `SaveReplay(before, after)` includes footage from before the event, because everything is cut
  from a tape that is already rolling.
- **Off unless asked.** Capture runs only when `-capture` is literally on the command line (or a user presses
  record); no environment variable or auto-loaded config turns it on.
- **Unattended safely.** The shut-off valve stops capture before disk, RAM or the encoder runs out, and exports what
  was recorded.
- **Crash-safe.** Finished tape segments stay on disk; the next launch or `capture recover` exports them.
- **Preview sets.** `capture set`, `capture web` and `capture mcp` write numbered sets that MindAttic.Media.Viewer opens.

## 3. What it is NOT {#M.CAPT-§3}
- NOT a video editor: it cuts ranges and encodes presets; editing happens elsewhere.
- NOT a streaming tool: nothing is broadcast.
- NOT in shipped builds: capture stays out of Release builds of the apps that use it.
- NOT bundling ffmpeg: it uses an `ffmpeg.exe` on PATH or beside the app.
- NOT a viewer: MindAttic.Media.Viewer shows what Capture writes.

## 4. Architecture canon {#M.CAPT-§4}
```
 app / game / user                          MindAttic.Media.Capture
 -capture flags, Capture.Raise, hotkeys --> CaptureEngine --> trigger rules --> clip / replay / screenshot
 capture.exe (CLI, serve, host, web, mcp)        |
 recorder app (capture-tray.exe)                 v
                                         tape: 1 s FFV1 or HEVC segments in {root}\.staging
                                                 |
                                         exports (mkv master, mp4/webm/gif/webp, thumbnail)
                                         sidecar JSON per file + {stem}.session.json manifest
                                         shut-off valve (disk, RAM, encoder memory/lag, duration)
 capture sources: WGC windows, Desktop Duplication / GDI monitors and regions, WASAPI audio,
                  Unity GPU readback, Unreal SGFR shared-memory frame ring
```

### 4.1 Projects
- `src/MindAttic.Media.Capture` — core (netstandard2.1; net10.0), no third-party dependencies: API, `-capture`
  parsing, trigger rules, tape, exports, sidecars, manifest.
- `src/MindAttic.Media.Capture.Windows` — desktop sources, audio, global hotkeys, DI.
- `src/MindAttic.Media.Capture.Wpf`, `.WebView2` — one-line app adapters and the `window.mindattic.capture` bridge.
- `src/MindAttic.Media.Capture.Sets` — preview sets (`preview.json`, store, Viewer handoff).
- `src/MindAttic.Media.Capture.Web` — headless page screenshots and video through Playwright and Edge.
- `src/MindAttic.Media.Capture.Agent` — `capture.exe`: record, serve, host, web, set, gif, recover, mcp.
- `src/MindAttic.Media.Capture.Tray` — `capture-tray.exe`, the hand recorder.
- `unity/com.mindattic.media.capture`, `unreal/MindAttic.Media.Capture.Unreal` — engine packages.
- `tests/MindAttic.Media.Capture.Tests`, `tests/MindAttic.Media.Capture.Windows.Tests` — NUnit.

### 4.2 Domain model (the nouns)
- **Tape** — the rolling segment recorder; a recording, clip, replay or session is a time range on it.
- **Session** — one engine run; its manifest `{stem}.session.json` records files, markers, limits and `stoppedBy`.
- **CaptureEvent** — a raised event that trigger rules turn into a clip, replay or screenshot.
- **Preview set** — a numbered folder plus `preview.json` (`mindattic.preview/1`).

### 4.3 Key services (the verbs)
`CaptureEngine` (start, events, stop, export), `OutputManager` (roots, naming, manifest), `SessionFinalizer`
(detached export after the app exits), `FfmpegProcess`, the shut-off valve, `PreviewSetStore`, `capture host`.

## 5. The Laws {#M.CAPT-§5}
Inherits `MindAttic.HouseRules.md` by reference.
- **M.CAPT-LAW-1 — Off unless the flag is there.** A plain launch is a plain launch.
- **M.CAPT-LAW-2 — Never lose recorded footage.** A crash, a valve stop or a redeploy leaves segments that are
  exported, never deleted unexported.
- **M.CAPT-LAW-3 — Never stall the host app.** Exports run detached through the agent when it is installed.
- **M.CAPT-LAW-4 — Core stays dependency-free** so Unity can consume the netstandard2.1 build.
- **M.CAPT-LAW-5 — Runtime ids keep the old name** (`capture`/`capture-tray` exes, `%LOCALAPPDATA%\MindAttic\Capture`,
  `MINDATTIC_CAPTURE*`, `window.mindattic.capture`) so existing callers keep working.

## 6. Verified state {#M.CAPT-§6}
- Media roots release: Media.Capture, Windows, Web 1.1.0 and Sets 2.1.0 packed to `C:\LocalNuGet`, Unity plugin DLL
  refreshed, agent and tray reinstalled (e25fd8f, M.CAPT-T207).
- Unreal plugin runtime, Blueprint API, editor menu and README (M.CAPT-T189..T196, 21c038f).
- Desktop integration tests skip instead of failing on a locked workstation (c875be4, M.CAPT-T218).
- `run.ps1` is the shared run menu (9389b38, MAT-T300); as of 2026-11-27 only the Debug build is under
  `R:\MindAttic\Apps\MindAttic.Media.Capture`, and the old `C:\Apps\MindAttic.Media.Capture` still exists.

## 7. Active frontier {#M.CAPT-§7}
See `docs/RELEASE.md` for the milestone, its epics and what is out of scope; designs in `docs/design/`.

## 8. Quality bar {#M.CAPT-§8}
A change is done when `dotnet build MindAttic.Media.Capture.slnx` is clean, the behaviour has an NUnit test where it is
not desktop-only, and the README describes it as built.

## 9. Glossary {#M.CAPT-§9}
- **Shut-off valve** — the resource limits that stop capture and export early.
- **SGFR** — the shared-memory frame ring an Unreal game feeds to `capture host --frames`.
- **Staging** — `{root}\.staging`, where tape segments live until exported.
