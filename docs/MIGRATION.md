# Migrating from MindAttic.Capture

The repo, projects, packages, namespaces and Unity package were renamed to MindAttic.Media.Capture
(M.CAPT-T167, done in M.CAPT-T170 to T182). The old -> new ids, how to move a consumer and the names kept on purpose
are in the README's [Formerly MindAttic.Capture](../README.md#formerly-mindatticcapture) section.

## Package versions

| Package | Version | Why |
|---|---|---|
| `MindAttic.Media.Capture`, `.Windows`, `.Wpf`, `.WebView2`, `.Web` | 1.0.0 | New ids start at 1.0.0 (M.CAPT-T170). |
| `MindAttic.Media.Capture.Sets` | 2.0.0 | `PreviewHandoff.LegacyPipeName` and `PreviewLauncher.LegacyViewerPath` were removed with the old MediaViewer fallbacks (M.CAPT-T182, M.CAPT-T181). No consumer outside this repo used them. |

## Consumers moved

| Repo | Ticket |
|---|---|
| Automata | AUT-T39 |
| Auto.GamePlay | AUTO-T5 |
| GridGame.Unity | GG-T999 |
| TownWalker | GGC-T40 |
| MindAttic.Browser | M.BRWS-T39 |
| MindAttic.Components (CaptureDemo) | M.COMP-T231 |
| MindAttic.Orchestrator (docs) | M.ORCH-T2182 |
| MindAttic.Telemetry (docs) | M.TELE-T26 |
| MindAttic.Web (comments) | M.WEB-T25 |
| MindAttic.Agency (capture ability, root entry) | MAT-T198, MAT-T201 |
| Prose | PRS-T1124 |
| Mods.RogueTrader.Brevity | RTB-T274 |
| Unity.Rea.Chain | U.CHN-T117 |
| Unity.Eve | U.EVE-T57 |
| Unity.Rea.Hare | U.HARE-T135 |
| Unity.Rea.Incognito | U.INC-T61 |
| Unity.Rea.Overworld (docs) | U.OVW-T107 |
| UnityRonin | U.RON-T32 |
| Unity.Cursory | UC-T66 |
| Unity.Multiplayer (docs) | UM-T96 |
| Unreal.Playground | UNR-T117 |
| SpaceGod.Unreal (names shown to people; `Vendor/MindAttic.Capture` stays until the next re-vendor) | US-T687 |

Left after the sweep (M.CAPT-T181): SpaceGod.Unreal prose (US-T691) and Unity.SpaceGod's package reference
(U.SPC-T643). Archived repos (the SpaceGod.Unity.CSharp, SpaceGod.Unreal.Cpp, SpaceGod.Unreal.CSharp and
Unreal.SpaceGod candidates) and dated records keep the old name.
