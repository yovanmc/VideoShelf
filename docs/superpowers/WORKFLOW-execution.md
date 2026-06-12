# VideoShelf Execution Workflow (Repeatable Runbook)

**Audience:** A Claude agent building VideoShelf phase-by-phase from the approved spec.
**Mode:** Run **one phase per invocation**. Each run: pick the next unstarted phase, write its
plan if missing, execute it end-to-end (implement → review → PR → merge), update the Progress
Log, stop.

**Authoritative inputs:**
- Design source of truth: `docs/superpowers/specs/2026-06-11-videoshelf-design.md`
- Per-phase task content: the plan files under `docs/superpowers/plans/` (written one per phase)
- Planning method: the `superpowers:writing-plans` skill
- Execution method: the `superpowers:subagent-driven-development` skill
- Branch finish: the `superpowers:finishing-a-development-branch` skill

> **Golden rule:** You are the **controller**. You do *not* write production code yourself. You
> dispatch fresh subagents per task, review their work in two stages, then merge. This keeps your
> context clean and the work high-quality. (Use `model: sonnet` for mechanical tasks; step up to a
> stronger model only for design/judgment or when a subagent is BLOCKED on reasoning.)

---

## 0. Environment specifics (do not rediscover)

| Thing | Value / command |
|---|---|
| Repo root | `C:\Agent Projects\VideoShelf` |
| `gh` CLI | **Not on PATH.** Use the full path: `& "C:\Program Files\GitHub CLI\gh.exe"` |
| GitHub repo | `yovanmc/VideoShelf` (public) |
| Worktree dir | `.worktrees/` at repo root (gitignored) |
| Shell | Prefer **PowerShell** tool for `gh` and multiline bodies (here-strings `@'...'@`). Bash works for plain git. |
| Default branch | `main` |
| Solution | `VideoShelf.slnx` (.NET 10; new XML solution format — **not** `.sln`) |
| Test gate (all projects) | `dotnet test VideoShelf.slnx -c Release --nologo -v q` |
| Core tests only | `dotnet test tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj -c Release --nologo -v q` |
| App tests | `tests/VideoShelf.App.Tests/…` — **does not exist yet**; created in the App-shell phase. |
| Baseline (after Plan 1) | 32 Core tests, 0 failures (grows as phases add tests) |

**Known gotchas (shared with VideoTriage):**
1. **`gh pr merge` must run from the MAIN repo root, not the worktree** — `main` is checked out in
   the main working copy, and git refuses operations on a branch "already used by worktree".
2. **`gh` not found via Bash** — always invoke through the full PowerShell path above.
3. **Stale `.git/index.lock`** — if a git command reports `Unable to create '.../index.lock'`,
   delete the file and retry; a prior process crashed.
4. **The Bash/PowerShell tool resets CWD between calls.** Always `cd` at the start of every command;
   never rely on persisted working directory.
5. **`git branch -d` from main repo fails while the worktree still references the branch** — remove
   the worktree first, then delete the branch.
6. **Direct pushes to `main` are blocked** by the harness guard. Every change ships via a branch +
   PR, even docs/chore changes.

---

## 1. The phase queue

Execute in order. Each phase = one plan file = one PR. **Do one phase per workflow run.** Check the
Progress Log (§6) for the next `[ ]` phase. Plans are written just-in-time (§2 step 2).

| # | Phase | Plan file | Depends on |
|---|---|---|---|
| 1 | Foundation (Core indexer) | `plans/2026-06-11-videoshelf-foundation.md` | — |
| 2 | App shell + library browse + thumbnails (incl. **search**, **sort**, **unwatched badges**, **missing-file marking**) | `plans/<date>-videoshelf-app-shell.md` | Phase 1 |
| 3 | Playback (player, PiP, **resume**, **auto-next-episode**, **embedded subtitle/audio pickers**, **chapters**, **seek-preview**, **screenshot**) | `plans/<date>-videoshelf-playback.md` | Phase 2 |
| 4 | Discovery + tags (**continue-watching** + **recency rails**, For-you / pick-a-tag / more-from-section) | `plans/<date>-videoshelf-discovery.md` | Phases 2 & 3 |
| 5 | Opt-in rename tool (preview diff → confirm → defensive rename + undo manifest) | `plans/<date>-videoshelf-rename.md` | Phase 2 |
| 6 | Self-verification harness + release polish (fixture/launch/screenshot; MSIX packaging + CI `package` job) | `plans/<date>-videoshelf-harness-polish.md` | Phases 2–4 |

Branch naming: `feat/app-shell`, `feat/playback`, `feat/discovery`, `feat/rename-tool`,
`feat/harness-polish`.

> Phases share files (e.g. `MainViewModel`, DI registration, the library repositories). Run them
> **sequentially**, rebasing each new branch on the freshly merged `main`. Don't parallelize unless
> the user explicitly asks and you use separate worktrees.

---

## 2. Per-run procedure (top level)

```
1. Sync main + create worktree                                   (§3)
2. If the phase's plan file doesn't exist: write it first        (writing-plans)
   - Derive tasks from the spec; get the user's nod before executing a brand-new plan.
3. Read the plan file, extract ALL tasks with full text
4. For each task: implement → spec review → quality review → fix loop   (§4)
5. Final whole-branch review                                     (§4 step F)
6. Finish: push, PR, watch CI, merge, clean up                   (§5)
7. Update the Progress Log                                       (§6)
8. Stop. (One phase per run.)
```

**Continuous execution within a phase:** Don't stop to check in between *tasks*. Only stop for: a
BLOCKED subagent you can't unblock, genuine ambiguity, or phase complete.

---

## 3. Setup (start of every phase)

```bash
# From main repo root — sync first
cd "C:/Agent Projects/VideoShelf" && git checkout main && git pull
# (if index.lock error: rm "C:/Agent Projects/VideoShelf/.git/index.lock" then retry)

# Create the phase worktree + branch (name it after the phase)
cd "C:/Agent Projects/VideoShelf" && git worktree add ".worktrees/<branch>" -b "<branch>"
```

**Verify clean baseline** in the worktree before any work:
```bash
cd "C:/Agent Projects/VideoShelf/.worktrees/<branch>" && \
  dotnet test VideoShelf.slnx -c Release --nologo -v q 2>&1 | tail -5
```
If baseline is red, stop and report — do not build on a broken base.

---

## 4. Per-task cycle (the core loop)

For each task in the plan file, in order:

### Step I — Dispatch implementer subagent
- Tool: `Agent`, `subagent_type: general-purpose`, `model: sonnet` (step up only if needed).
- **Do NOT make the subagent read the plan file.** Paste the full task text, file paths, code
  snippets, and scene-setting context into the prompt. Include:
  - The exact worktree path and branch (`Do not switch branches or touch main`).
  - The TDD expectation: write the failing test first, then implement, run tests, commit.
  - The exact verification command (`dotnet test VideoShelf.slnx -c Release`).
  - The commit message + trailer (§7).
  - The required output format: end with `STATUS: DONE` / `DONE_WITH_CONCERNS — …` /
    `NEEDS_CONTEXT — …` / `BLOCKED — …`.
- Handle the returned status:
  - `DONE` → go to Step S.
  - `DONE_WITH_CONCERNS` → read concern; if correctness/scope, address before review; else note & proceed.
  - `NEEDS_CONTEXT` → provide it, re-dispatch.
  - `BLOCKED` → context problem: add context & re-dispatch; needs more reasoning: re-dispatch with a
    stronger model; task too big: split it; plan is wrong: escalate to user.

### Step S — Spec compliance review
- Dispatch a **fresh** `Agent` (`model: sonnet`) with the task spec and the list of files.
- Confirm every requirement is met and nothing extra was built.
- Output: `✅ SPEC COMPLIANT` or `❌ ISSUES FOUND` (file/line).
- If issues → send back to the **same implementer agent** (via `SendMessage`) to fix, then re-review.
  Loop until ✅. **Never** start quality review before spec is ✅.

### Step Q — Code quality review
- Get the commit SHA(s): `cd <worktree> && git log --oneline <base>..HEAD`.
- Dispatch a fresh `Agent` (`model: sonnet`) with the SHA(s) and context. Ask for correctness,
  pattern-consistency, test-quality (does it fail if the bug returns?), side-effects, edge cases.
- Output: `✅ APPROVED` or `❌ ISSUES FOUND` + Strengths / Issues (Critical/Important/Minor).
- Critical/Important → back to implementer, re-review. Minor → fix if cheap, else log it. Loop until
  acceptable.

### Step M — Mark task complete; next task. Repeat I→S→Q.

### Step F — Final whole-branch review (after ALL tasks)
- Dispatch one fresh `Agent` (`model: sonnet`) to review `git diff <base>..HEAD` for the whole phase:
  internal consistency, coherent commit story, anything blocking a PR. Address blockers via the
  implementer loop, then Finish.

---

## 5. Finish the branch (push → PR → CI → merge → clean up)

Standing instruction for this project: **"Create a PR and merge it once CI passes."** Follow without
re-asking.

```powershell
# 1. Final test gate in the worktree
cd "C:\Agent Projects\VideoShelf\.worktrees\<branch>"
dotnet test VideoShelf.slnx -c Release --nologo -v q
# MUST be 0 failures. If red, fix before pushing.

# 2. Push
git push -u origin <branch>

# 3. Create PR (PowerShell here-string; closing '@ at column 0)
& "C:\Program Files\GitHub CLI\gh.exe" pr create --title "<title>" --body @'
## Summary
- <2-3 bullets of what changed and why>

## Test Plan
- [ ] <new tests + total passing count>

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@

# 4. Watch CI to green
& "C:\Program Files\GitHub CLI\gh.exe" run watch <run-id> --interval 20 --exit-status
#   (find <run-id> via: gh run list --branch <branch> --limit 3)
```

```powershell
# 5. Merge — FROM THE MAIN REPO ROOT (not the worktree!). Preserve per-task history.
cd "C:\Agent Projects\VideoShelf"
& "C:\Program Files\GitHub CLI\gh.exe" pr merge <PR#> --merge --delete-branch
```

```bash
# 6. Clean up worktree + local branch + sync main
cd "C:/Agent Projects/VideoShelf" && \
  git worktree remove ".worktrees/<branch>" && \
  git worktree prune && \
  git branch -d <branch> && \
  git checkout main && git pull --ff-only
```

> If `gh pr merge` says "already merged", that's fine — proceed to cleanup. If `--delete-branch`
> fails because the worktree still references it, run cleanup step 6 first.

**CI note:** currently one job — `build-and-test` (restore/build/test `VideoShelf.slnx` on
windows-latest, .NET 10). The harness-polish phase adds a `package` job (signed dev MSIX) that, per
the self-contained principle, must assert **no external media tools are bundled** — an intentional
guard, not a flake. Until then there is no `package` job to wait on.

---

## 6. Cross-cutting rules (apply to every phase)

- **Confirm-before-build:** before dispatching a task, check whether a prior merge already
  implemented it; tell the implementer to verify-then-skip if present.
- **Read-only & destructive-op discipline** (standing engineering principle): the app never moves or
  deletes video files except via the opt-in rename tool, which must verify the target is free, fail
  safe, and write an undo manifest. Missing files are flagged in-app, never auto-removed from the
  index. Grouping/overrides live in SQLite, never written to disk.
- **Self-contained:** playback, thumbnails, snapshots, and metadata all come from bundled libVLC.
  **No external tools on PATH.** Do not introduce ffmpeg/ffprobe/HandBrake dependencies.
- **No network for content:** no online metadata scraping, no subtitle downloading. Titles/thumbnails
  stay filename- and snapshot-derived.
- **Agent timeouts:** every agent-run shell command must have a finite timeout. No unbounded waits.

---

## 7. Commit & authorship conventions

- VideoShelf plans are **Claude-authored** — no Codex trailer. Append:
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Git author stays the user's human identity (`yovanmc`).
- Keep commits small and per-task (one logical change each). PR merge is `--merge` (no squash) so the
  per-task history is preserved.
  > Note: Plan 1 and the spec-expansion PR (#1, #2) were squash-merged before this runbook existed;
  > from Phase 2 onward use `--merge`.

---

## 8. Progress Log (update at the end of every run)

| Phase | Branch | PR | Status | Notes |
|---|---|---|---|---|
| 1 — Foundation (Core indexer) | `feat/foundation` | #1 | ✅ Merged | VideoExtensions, NaturalComparer, TitleParser, SectionGrouper, VideoShelfDb (WAL/FK, idempotent schema), domain records, FolderScanner, Library/Watch repos, ScanService. 32 Core tests. CI `build-and-test` added. Minor: unused `db` param in `ScanService` (intentional — reserved for future transaction scoping). |
| 2 — App shell + browse | `feat/app-shell` | #4 | ✅ Merged | VideoShelf.App WPF shell + App.Tests; Core schema (missing/added_at/resume_position, idempotent); browse read-model + search/sort; folder picker, scan coordinator, fail-safe libVLC thumbnail cache (libVLC 3.9.7.1/3.0.23.1); browse VMs + shell views; unwatched badges; missing-file dimming; search jump-to. 71 tests. Review caught + fixed a Critical cross-thread ObservableCollection bug (ConfigureAwait(false) on UI-bound chains). **Visual verification deferred to Phase 6 harness.** Minor deferred: LibraryViewModel sort/search `_pending` race. |
| 3 — Playback | `feat/playback` | — | [ ] Not started | Player + PiP, resume, auto-next (in-series), embedded subtitle/audio pickers, chapters, seek-preview, screenshot. |
| 4 — Discovery + tags | `feat/discovery` | #10 | ✅ Merged | Home rails (continue-watching + recency + For-you + pick-a-tag) and section-level tagging via a dedicated section-detail view. TagRepository, `resume_updated_at` guarded migration, pure DiscoveryScoring, DiscoveryRepository, `GetSection(id)`; Discovery/section-detail VMs + views, Home/Browse/SectionDetail nav, EnumToVisibility converter, DI. **177 tests.** Whole-branch review fixed 2 IMPORTANT issues (scan now refreshes Discovery rails; suggestion autocomplete caches tags off-thread instead of per-keystroke SQLite). Views screenshot-unverified until Phase 6. |
| 5 — Rename tool | `feat/rename-tool` | — | [ ] Not started | Opt-in, preview diff → confirm → defensive rename + undo manifest; DB repaths off stable IDs. |
| 6 — Harness + release polish | `feat/harness-polish` | — | [ ] Not started | Fixture/launch/screenshot harness; MSIX packaging + CI `package` job (assert no media tools bundled). |

**How to update:** after merging a phase, flip its row to `✅ Merged`, fill in the PR number, and add
a one-line note of what shipped + any deferred minor issues.

---

## 9. One-run checklist (copy this each time)

- [ ] Identified next `[ ]` phase from the Progress Log
- [ ] Synced main, created worktree + branch, verified green baseline
- [ ] Wrote the phase plan if it didn't exist (writing-plans)
- [ ] Read the plan file, extracted all tasks with full text
- [ ] Ran each task through Implement → Spec ✅ → Quality ✅ → next
- [ ] Ran final whole-branch review, addressed blockers
- [ ] Test gate green in the worktree (`dotnet test VideoShelf.slnx -c Release`)
- [ ] Pushed, opened PR, watched CI to green
- [ ] Merged from **main repo root** (`--merge`), deleted branch, removed worktree, pulled main
- [ ] Updated the Progress Log row
- [ ] Stopped (one phase per run)
