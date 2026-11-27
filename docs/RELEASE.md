---
layer: release
status: living
---
# MindAttic.Media.Capture — Release charter

> The one finish line this repo is working toward. Every open ticket in this repo sits under one of the epics
> below, and every epic is part of the milestone. Work that does not fit is out of scope until this milestone is
> reached (owner, 2026-11-27, MAT-T319).

## 1. What this is
Programmatic and hand-driven video capture and screenshots for MindAttic apps and games (trailer footage, store
shots, devlog clips, bug-repro video), used by the owner and wired into MindAttic apps and engines.

## 2. The milestone: Media.Capture stable recorder {#milestone}
- **Milestone:** MS-45, "Media.Capture stable recorder"
- **Published means:** NuGet packages in `C:\LocalNuGet` (already at 1.1.0 / Sets 2.1.0) and the agent plus recorder
  app installed from `R:\MindAttic\Apps\MindAttic.Media.Capture\{Debug,Release}`.
- **The natural stopping point:** the feature set is broad and already shipped (engines, preview sets, web capture,
  MCP). What stands between it and a finished tool is the owner's own decision to replace the tray with a normal
  windowed app, the known bugs that lose or misplace footage, and a clean deploy. When those land, Capture is done
  until a new engine or consumer actually needs something.

## 3. Done when (acceptance checks)
1. build — `MindAttic.Media.Capture.slnx` builds clean on main.
2. exists — `R:\MindAttic\Apps\MindAttic.Media.Capture\Release\capture-tray.exe` (the recorder app) and `capture.exe`.
3. harness — `MindAttic.Media.Capture.Tests` passes.
4. signoff — the owner pins the recorder to Start, records an area and an app, and gets the files.

## 4. Epics, in priority order
| Priority | Epic (ticket) | What it delivers | Features |
|---|---|---|---|
| 900 | M.CAPT-T230 Recording correctness | Fix bugs that drop, overwrite or misplace footage and manifests | M.CAPT-T233 Session and manifest (990), M.CAPT-T234 Tape and export (980), M.CAPT-T235 Agent and sources (970) |
| 800 | M.CAPT-T231 Windowed recorder | A pinned windowed app replaces the tray (owner decision, M.CAPT-T34) | M.CAPT-T236 Main window (890), M.CAPT-T237 Tray retirement (880) |
| 700 | M.CAPT-T232 Deploy and repo hygiene | Release on R:, safe redeploys, old install and committed worktrees gone | M.CAPT-T238 R: deploy (790), M.CAPT-T239 Cleanup (780) |

## 5. Out of scope (not before this milestone)
- New engines or bindings (Godot, others), new export formats and more MCP tools.
- Moving the agent and recorder onto MindAttic.Log (M.CAPT-T16): Capture already writes its own per-session log and
  stats files, and the core must stay dependency-free.
- Open-ended loops: exhaustive bughunts (M.CAPT-T165).
- Live integration or soak runs beyond the existing test suites.

## 6. Current verified state
- Media roots release 1.1.0 / Sets 2.1.0 packed and installed (e25fd8f); Unreal plugin with README (21c038f).
- Desktop integration tests skip on a locked workstation (c875be4, M.CAPT-T218), which closes M.CAPT-T72.
- `run.ps1` is the shared run menu (9389b38); only the Debug build is deployed to R: so far.

## 7. Decision record
- 2026-11-27 — charter drafted from README/design docs/git log/tickets, each kept bug re-checked against current code; Legion vote: **Approve**, plurality quorum, 9/9 voters, consensus 1.0, no dissent.
- Triage: 20 open → done 1 (M.CAPT-T72), duplicate 0, wont-do 2 (M.CAPT-T16, M.CAPT-T165), kept 17; 1 new task (M.CAPT-T240).