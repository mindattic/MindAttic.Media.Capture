# Design: show the owner anything an agent captures, immediately (Capture -> Preview set -> MindAttic.MediaViewer)

Owner's goal (2026-11-24): "make sure it works and is completely integrated with all MindAttic repos so that anything you can capture you can show me immediately; this will allow me to see pictures and video of things you are working on headlessly." Viewer: arrow keys, right-click Edit in Paint / Photocraft, a list of all media files (image, video, audio), audio waveform. Open-source best practice.

Decisions (owner): name **MindAttic.MediaViewer**; the Viewer **comes to the front** when a set is shown; sets live under **R:\MindAttic\Preview** (R: "Relic" is a 14 TB fixed drive, persistent); editors: Paint (`mspaint.exe`) and Photocraft (`C:\Apps\ArtCraft Launcher\artcraft-launcher.exe`).

## Name and format scope (owner, 2026-11-24)
"Call it MindAttic.MediaViewer - for images, videos, gifs, sounds, songs, every format you can get your hands on." Repo `MindAttic.MediaViewer`, app `MindAttic.MediaViewer.exe`, deployed to `C:\Apps\MindAttic.MediaViewer`, ticket prefix to be chosen with `tickets codes check` (e.g. M.MVIEW). Format strategy (wide, not MVP-narrow):
- Video and audio: LibVLCSharp.WPF + VideoLAN.LibVLC.Windows (LGPL-2.1, dynamic) is the primary player, not a fallback. It handles practically every container and codec: mp4/mkv/webm/avi/mov/flv/wmv/ts/m2ts/ogv/FFV1, mp3/flac/wav/ogg/opus/aac/m4a/wma/aiff/ape/alac/mid (via its plugins). WPF MediaElement only if LibVLC is unavailable.
- Images: WIC (BitmapDecoder) for the common formats + Magick.NET-Q8-AnyCPU (Apache-2.0) for everything else: webp, avif, heic/heif, jxl, psd, tiff multipage, tga, dds, exr, hdr, ico/cur, svg (rasterised), camera RAW (cr2/nef/arw/dng). Animated GIF/APNG/animated WebP play (WpfAnimatedGif, or frame-by-frame from Magick.NET).
- Songs: TagLib# (LGPL-2.1) for title/artist/album/duration and embedded cover art shown on the stage beside the waveform; folder of songs plays as a playlist (auto-advance, shuffle/repeat optional).
- Waveform: decode PCM peaks via LibVLC audio callbacks or NAudio/MediaFoundation, ffmpeg (external, never bundled) as last resort; cache peaks.
- MediaKinds in Capture.Sets maps a broad extension table to image/animated/video/audio/hidden; unknown extensions are still listed as "other" with Open with….
- Register the app as an "Open with" handler for these types (per-user, HKCU\Software\Classes\Applications + SupportedTypes), not as the default, unless the owner asks.

## Approach
The folder is the contract. Every agent, with or without a desktop, writes a **preview set**: one folder of numbered media + `preview.json`. MindAttic.Media.Capture (formerly MindAttic.Capture) acquires media and owns the set format; MindAttic.MediaViewer (new WPF repo) shows it; a `/preview` ability and a `capture mcp` server tie it together for every CLI. Showing a set is best effort, in order: app bus -> named-pipe handoff to a running Viewer -> launch Viewer -> open first image in Microsoft Photos -> print path.

Gaps found in Capture today: only screen/window/region grabs (AgentArgs.cs:73-95), which fail on a locked/non-interactive desktop (GdiScreenshotter.cs:31, M.CAPT-T72); no headless web path (URLs only through interactive MindAttic.Browser); each `capture shot` gets its own `{app}\{timestamp}_{app}\` folder (OutputManager.cs:182-195), so shots can't share a set; README Output section (252-262) is stale. Rejected: Photos-only (fallback only), HTML gallery (no right-click edit), WinUI (house style is WPF on MindAttic.Components.Wpf 47.0.0 in C:\LocalNuGet; M.CAPT-T79), reusing SessionManifest (per-engine, rewritten whole by SaveManifest, OutputManager.cs:307-327), MindAttic.Storage (ASP.NET/EF/Blob, wrong shape).

## Components
| Component | Where | Job |
|---|---|---|
| MindAttic.Media.Capture.Sets (new lib, net10.0, System.Text.Json only) | MindAttic.Media.Capture\src\MindAttic.Media.Capture.Sets\ | Set model, PreviewSetStore (create/open/list/next index/import under lock, atomic manifest write), MediaKinds (ext -> image/video/audio/hidden), PreviewLauncher (bus -> pipe -> launch -> Photos). Packed to C:\LocalNuGet. |
| CaptureSidecar + OutputManager.ResolveRoot/SanitizeTag made public | src\MindAttic.Media.Capture\Output\OutputManager.cs | One sidecar definition. Additive. |
| `capture set …`, `--set`, `capture web`, `capture mcp` | src\MindAttic.Media.Capture.Agent\Program.cs dispatch (:33-48) | CLI surface. `--set` = capture to a staging `--out` (AgentArgs.cs:154) then Import; no engine change. |
| MindAttic.Media.Capture.Web (new) | MindAttic.Media.Capture\src\MindAttic.Media.Capture.Web\ | Headless page shots and video via Microsoft.Playwright with installed Edge (`channel: msedge`, no Chromium download). PNG + sidecar (`host: "playwright"`). Kept out of core (core stays dependency-free, README:35). |
| MindAttic.MediaViewer (new repo, WPF) | D:\Projects\MindAttic\MindAttic.MediaViewer\src\MindAttic.MediaViewer\ | Stage + filmstrip, arrow keys, all-media drop-down with kind filter, recent sets, zoom/pan, video, audio + waveform, right-click, live folder watch, single instance, app-bus presence. Deployed by run.ps1 to C:\Apps\MindAttic.MediaViewer. |
| /preview ability | mindattic-agent-standard\shared\abilities\preview.md | Rendered to Claude/Codex/Kimi/agy by render-abilities.ps1 (AGENTS.md:103-117), like capture.md. |
| MCP registration | mindattic-agent-standard\install-mcp.ps1 (new) | Registers `capture.exe mcp` with each CLI, following install-hooks.ps1. No MindAttic MCP server exists yet. |
| `show-preview` bus kind | MindAttic.Orchestrator\src\MindAttic.Orchestrator.Core\Status\AppBus.cs + docs\app-bus.md | Hands a set to the owner's desktop from a desktop-less agent through the daemon's per-user pipes. Confirm the daemon forwards arbitrary kinds. |

## Contracts
Set folder: `R:\MindAttic\Preview\{Repo}\{yyyy-MM-ddTHH.mm.ss}_{TICKET}_{slug}[-rand]\` (root via ResolveRoot; fallback %USERPROFILE%\Pictures\MindAttic\Preview). Files `NNN_{tag}.{ext}`, NNN = 1 + highest prefix, under the set lock (named mutex `Global\MindAttic.Preview.<hash(dir)>`; manifest written to `preview.json.tmp` then File.Replace). Sidecars are Capture's `{file}.json`; import rewrites their `files` paths.

preview.json:
```json
{ "schema": "mindattic.preview/1", "id": "2026-11-23T14.05.11_GAD-T725_exam-caption",
  "title": "…", "repo": "Gadfly", "ticket": "GAD-T725",
  "agent": { "client": "claude", "session": "…", "login": "…" },
  "created": "…", "updated": "…", "status": "open|done",
  "items": [
    { "file": "001_desktop.png", "kind": "image", "caption": "Desktop 1440x900", "at": "…",
      "source": { "type": "web|screen|window|file", "url": "…", "viewport": "1440x900" } },
    { "file": "002_q1.wav", "kind": "audio", "caption": "Examiner voice, Q1" },
    { "file": "003_walkthrough.mp4", "kind": "video", "poster": "003_walkthrough.thumb.jpg" } ] }
```
The Viewer treats the folder as truth (lists every media file even without a manifest); the manifest adds order, captions, ticket. Hidden: *.json, *.thumb.jpg, *.session.json, *.log, *.stats.csv. Same-stem .mkv + .mp4 show as one item playing the .mp4.

CLI:
```
capture set new --title T [--ticket ID] [--repo R] [--json]          -> prints set dir
capture set add <dir> <file...> [--tag T] [--caption C] [--copy|--move]
capture set list [--repo R] [--limit N] [--json]
capture set show <dir> [--select FILE] [--no-launch]                 -> prints bus|pipe|launched|photos|none
capture shot|record <target> --set <dir> [--caption C]
capture web <url> --set <dir> [--viewport 1440x900]... [--full-page] [--wait-for CSS] [--wait 2s]
            [--dark] [--fake-media] [--device "iPhone 13"] [--video 10s] [--tag T] [--caption C]
capture mcp                                                          (stdio MCP server)
MindAttic.MediaViewer.exe [<dir|file|preview.json>] [--select FILE] [--follow] [--slideshow 3s]
```
`--fake-media` = Chromium fake camera/mic flags + permissions (Gadfly needs voice and camera).

Bus message: `{"kind":"show-preview","v":1,"payload":{"set":"R:\\…","select":"002_phone.png","ticket":"GAD-T725","title":"…","from":"capture-agent","to":"viewer"}}`, sent only if the Viewer announced `app-present` with `handles` containing `show-preview` (100 ms connect, app-bus.md:36-37).

MCP tools (ModelContextProtocol 2.x, stdio, pinned): preview_new_set, preview_capture_web, preview_capture_screen (clear error without a desktop), preview_add_file, preview_show, preview_list_sets; optional size-capped thumbnail in results.

Viewer settings in MindAttic.Vault 8.0.0: editors [{Paint, mspaint.exe, "\"{path}\""}, {Photocraft, C:\Apps\ArtCraft Launcher\artcraft-launcher.exe, "\"{path}\""}], previewRoot, followNewest, slideshowInterval, bringToFront=true. Right-click: Edit in <each editor>, Open with… (shell openas), Show in folder (explorer /select,), Copy image, Copy path, Delete to Recycle Bin.

## Reuse
TicketHandoff (MindAttic.Tickets.Desktop\TicketHandoff.cs:13-92) pattern for single instance; app bus client pattern (Tickets.Desktop\Bus\AppBus.cs, AppBusClient.cs); capture.exe lookup from shared/abilities/capture.md:15-23; OutputManager.ResolveRoot/SanitizeTag/CaptureSidecar; NAudio (already shipped with Capture); MindAttic.Log + MindAttic.Vault.

## Open-source building blocks
Microsoft.Playwright .NET (Apache-2.0; msedge channel). ModelContextProtocol 2.x (Apache-2.0). NAudio (MIT) peaks -> WPF StreamGeometry waveform; external ffmpeg for formats Media Foundation can't decode (never bundle, GPL). WPF MediaElement for video MVP; LibVLCSharp.WPF + VideoLAN.LibVLC.Windows (LGPL-2.1, dynamic) for MKV/FFV1/WebM. WIC thumbnails (BitmapImage.DecodePixelWidth), IShellItemImageFactory for video thumbs; avoid ImageSharp (Six Labors licence). WpfAnimatedGif (Apache-2.0). FileSystemWatcher with 250 ms debounce and unlock retry.

## Phases (each task ~80k tokens, one commit, build-only verification)
Phase 0 (no new code): MAT-a preview.md v0 — make set dir by the rule; `capture shot --self|--window "T" --out <set>\.raw` then rename to NNN_tag.png; URLs: `msedge.exe --headless=new --hide-scrollbars --window-size=1440,900 --screenshot=<set>\NNN_desktop.png <url>` (+390x844); minimal preview.json; Start-Process first image (Photos); print path plus a copy-paste PowerShell block to reopen.
Phase 1: CAPT-1 Sets lib (+public sidecar/ResolveRoot, pack). CAPT-2 `capture set` cmds + `--set` + PreviewLauncher (pipe -> launch -> Photos). CAPT-3 Capture.Web + `capture web`. VIEW-1 repo bootstrap (create GitHub repo mindattic/MindAttic.MediaViewer private, clone-repo.ps1), WPF shell on Components.Wpf 47, open dir/file/manifest, image stage, keys (Left/Right, Home/End, F11, Esc, Ctrl+C), run.ps1 deploy. VIEW-2 virtualised filmstrip, media drop-down + kind filter, recent sets, single-instance pipe. VIEW-3 right-click + Vault editors. MAT-b preview.md v1 on `capture set`/`capture web`.
Phase 2: VIEW-4 live watch/follow newest/unseen badge. VIEW-5 audio waveform (peaks cached %LOCALAPPDATA%\MindAttic\Viewer\peaks), playhead, click-seek, Space. VIEW-6 video via MediaElement; VIEW-7 LibVLCSharp fallback. CAPT-4 `capture mcp`; MAT-c install-mcp.ps1 (Claude, Codex config.toml, Kimi, agy mcp_config.json). ORCH-1 show-preview bus kind; VIEW-8 Viewer bus client; CAPT-5 bus first in PreviewLauncher. CAPT-6 `capture web --video` (Playwright RecordVideoDir -> webm).

## Risks
Concurrent writers (per-set mutex + Viewer retry; random suffix on set names). Capture's checkout has gitlinked worktree folders (M.CAPT-T12\, main-tests\, M.CAPT-T71): stage by explicit path. No desktop: GUI launch and Photos both fail -> bus + unseen badge, else print the path. Playwright grows the capture bin. Capture never deletes; add `capture set prune` only if asked.

## Verification
dotnet build MindAttic.Media.Capture.slnx / MindAttic.Media.Viewer.slnx. Smoke: `capture set new --title smoke`; `capture web https://mindattic.com --viewport 1440x900 --viewport 390x844 --set <dir>` -> 2 PNG + 2 sidecars + 2-item preview.json; `capture set show <dir>` opens the Viewer on 001, Right -> 002, right-click -> Paint opens mspaint. No-desktop proof: run the web capture from a schedule-wakeup one-shot with the machine locked.

## MindAttic.Media.Capture.Sets package API (as built, M.CAPT-T161; renamed in M.CAPT-T170/T171)
Package `MindAttic.Media.Capture.Sets` 2.0.0 in C:\LocalNuGet (2.0.0 drops `PreviewHandoff.LegacyPipeName` and `PreviewLauncher.LegacyViewerPath`, M.CAPT-T182; net10.0; depends on `MindAttic.Media.Capture` 1.0.0, where `CaptureSidecar`, `SidecarMarker`, `OutputManager.ResolveRoot`, `OutputManager.SanitizeTag` and `OutputManager.Sanitize` are public). Namespace `MindAttic.Media.Capture.Sets`. Formerly `MindAttic.Capture.Sets` 2.0.0 on `MindAttic.Capture` 2.0.0, namespace `MindAttic.Capture.Sets`; see the README's "Formerly MindAttic.Capture".

- `PreviewSet` — the manifest: `Schema` (`SchemaV1` = `mindattic.preview/1`), `Id`, `Title`, `Repo`, `Ticket`, `Agent` (`PreviewAgent`: `Client`, `Session`, `Login`; `PreviewAgent.FromEnvironment()`), `Created`, `Updated`, `Status` (`StatusOpen`/`StatusDone`), `Items`, and `[JsonIgnore] Directory`. `PreviewSet.ManifestName` = `preview.json`.
- `PreviewItem` — `File`, `Kind`, `Caption`, `At`, `Source` (`PreviewSource`: `Type` web|screen|window|file, `Url`, `Viewport`, `Device`, `Target`, `Path`), `Poster`.
- `PreviewJson` — `Options` (camelCase, indented, nulls omitted, case-insensitive read), `Serialize(set)`, `Deserialize(json)` (null on bad JSON).
- `MediaKinds` — kinds `Image`, `Animated`, `Video`, `Audio`, `Hidden`, `Other`; `Extensions` (ext without dot -> kind); `Of(path)` by name; `Detect(path)` also sniffs APNG and animated WebP headers; `IsShown(path)`. Hidden: dotfiles, `*.json`, `*.thumb.jpg|png`, `*.stats.csv`, `*.log`, `*.tmp`, `*.json.tmp`, partial downloads, desktop.ini, thumbs.db.
- `PreviewSetStore(root = null, log = null, now = null)` — `DefaultRoot` R:\MindAttic\Preview, `FallbackRoot` %USERPROFILE%\Pictures\MindAttic\Preview, `Root` (resolved on first use). `Create(title, ticket, repo, agent)`; static `Open(dir)` (folder is truth: unlisted media appended, a folder with no manifest still opens); `List(repo, limit)` newest first; static `NextIndex(dir)`; `Import(dir, files, tag, caption, source, mode Copy|Move)` (one capture's files share the next `NNN_tag`, sidecars follow with `files` rewritten, returns the added items); `Update(dir, change)`; static `Lock(dir)` (the per-set named mutex `Global\MindAttic.Preview.{sha256(fullpath upper)[..16]}`); static `Slug(title)`. Every write is under `Lock`, manifest via `preview.json.tmp` + `File.Replace`.
- `PreviewHandoff` — the Viewer's own single-instance pipe (M.MVIEW-T7, `SingleInstance` in MindAttic.Media.Viewer): `PipeName` = `mindattic-media-viewer-cli-{Windows user name, non-alphanumerics as -}` (current user only). Client `Send(PreviewRequest, timeout)` connects to `PipeName`, writes `Payload(request)`, the Viewer command line `PreviewRequest.ToArgs()` (`<set> [--select FILE]`) one argument per UTF-8 line ended by an empty line, and closes; the Viewer shows it as if launched with it. Since `MindAttic.Capture.Sets` 2.0.0 (M.CAPT-T174); its 1.0.0 used a JSON pipe the Viewer never listened on.
- `PreviewLauncher` — `Show(setDir, select)` returns `PreviewShown`: `Pipe` (handed to a running Viewer on `mindattic-media-viewer-cli-<user>`, 300 ms connect; the pre-rename `mindattic-mediaviewer-cli-<user>` pipe and C:\Apps\MindAttic.MediaViewer install were dropped in M.CAPT-T182), `Launched` (started `ViewerPath` = `MINDATTIC_MEDIA_VIEWER` env, else C:\Apps\MindAttic.Media.Viewer\MindAttic.Media.Viewer.exe, with `<dir> [--select FILE]`), `Photos` (shell-opened the selected or first image), `None`. The app-bus step (CAPT-5) comes first once the bus kind exists.
