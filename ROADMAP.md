# VideoShelf — ROADMAP

> Source of truth for what to build next. A fresh session reads this, finds the topmost milestone that is **not ✅ Merged**, and acts. Follows the `/roadmap` workflow (Opus plans/researches · Sonnet implements · ping at every phase handoff).

**Legend:** ✅ Merged · 📝 Plan ready (execute next) · 🔬 Researching/Planning · [ ] Not started (plan first)

## Definition

Windows video library + player — "AudioShelf for video" on the VideoTriage stack (.NET 10 WPF + LibVLCSharp play-everything + SQLite). Multi-source → section → series/standalone → episode; section tags + discovery; watched/unwatched; lean player + PiP; **strictly read-only** (rename tool is the only mutation). Self-contained — all playback/thumbnails/metadata from bundled libVLC, **no external tools (ffmpeg/HandBrake) on PATH**, no network for content.

- Repo: https://github.com/yovanmc/VideoShelf (default branch `main`)
- Design spec: [`docs/superpowers/specs/2026-06-11-videoshelf-design.md`](docs/superpowers/specs/2026-06-11-videoshelf-design.md)
- Runbook (env, worktrees, CI, conventions): [`docs/superpowers/WORKFLOW-execution.md`](docs/superpowers/WORKFLOW-execution.md)

## Conventions (see runbook for detail)

- `gh` is **not on PATH** → `& "C:\Program Files\GitHub CLI\gh.exe"`. Solution is `VideoShelf.slnx` (.NET 10 XML format). Test gate: `dotnet test VideoShelf.slnx -c Release --nologo -v q`.
- Work in **worktrees** under `.worktrees/`; **`gh pr merge` from the main repo root**, not the worktree. **Direct pushes to `main` are blocked** — every change (incl. docs) ships via branch + PR.
- Commits: human author `yovanmc` + `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. **No Codex trailer.** Merge `--merge` (no squash) from Phase 2 on.

## Milestones

| # | Phase | Status | Branch | PR | Notes |
|---|-------|--------|--------|----|----|
| 1 | Foundation (Core indexer) | ✅ Merged | `feat/foundation` | #1 | VideoExtensions, NaturalComparer, TitleParser, SectionGrouper, VideoShelfDb (WAL/FK, idempotent schema), FolderScanner, Library/Watch repos, ScanService. 32 Core tests. CI `build-and-test`. |
| 2 | App shell + library browse | 📝 Plan ready | `feat/app-shell` | — | **Execute next.** Plan exists: [`app-shell`](docs/superpowers/plans/2026-06-11-videoshelf-app-shell.md). WPF shell, browse, thumbnails, search, sort, unwatched badges, missing-file marking. Adds App + App.Tests. |
| 3 | Playback | [ ] Not started | `feat/playback` | — | Player + PiP, resume, auto-next (in-series), embedded subtitle/audio pickers, chapters, seek-preview, screenshot. Plan first. |
| 4 | Discovery + tags | [ ] Not started | `feat/discovery` | — | Continue-watching + recency rails, For-you / pick-a-tag / more-from-section, section tagging. Plan first. |
| 5 | Opt-in rename tool | [ ] Not started | `feat/rename-tool` | — | Preview diff → confirm → defensive rename + undo manifest; DB repaths off stable IDs. Plan first. |
| 6 | Harness + release polish | [ ] Not started | `feat/harness-polish` | — | Fixture/launch/screenshot harness; MSIX packaging + CI `package` job (assert no media tools bundled). Plan first. |

## Decision log & gotchas

- **Read-only & destructive-op discipline:** never move/delete video files except via the opt-in rename tool (verify target free, fail safe, undo manifest). Missing files flagged in-app, never auto-removed. Grouping/overrides live in SQLite, never written to disk.
- **Self-contained:** no external media tools on PATH; the harness-polish `package` job asserts this.
- **Git:** stale `.git/index.lock` → delete & retry; remove worktree before `git branch -d`.
- **History note:** Plan 1 + spec PRs (#1, #2) were squash-merged before the runbook; use `--merge` from Phase 2.
