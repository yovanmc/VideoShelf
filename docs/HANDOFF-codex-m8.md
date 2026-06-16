# VideoShelf — handoff briefing for an external executor (Codex)

> Purpose: hand this project to Codex (or any external assistant) so work can continue
> while Claude usage is depleted. Contains everything needed to execute the next milestone
> from a cold start: the project, repo, how we operate, the full roadmap-skill workflow,
> and how to adapt that skill for Codex.

---

## 0. Immediate task

Execute milestone **M8 (Home + Search redesign)**. A fully self-contained plan already exists and is merged to `main`:

- **Plan file:** `docs/superpowers/plans/2026-06-12-videoshelf-home-search.md`
- **Source of truth:** `ROADMAP.md` (repo root) — M8 is the topmost row marked `📝 Plan ready`.

Read the plan, implement its 9 tasks in order, verify (tests + a Home/Search screenshot pass), open a PR, get CI green, merge, then flip the M8 row in `ROADMAP.md` to `✅ Merged`. The plan contains complete code, exact commands, and explicit "STOP and report if X doesn't match" guards — follow it literally; if reality diverges from the plan, stop and surface it rather than guessing.

---

## 1. Project

**VideoShelf** — a Windows video-library app + player, "AudioShelf for video," built on the VideoTriage stack:

- **.NET 10 WPF + WPF-UI (Fluent) + LibVLCSharp (play-everything) + SQLite.**
- Data model: **Source → Section → Series/Standalone → Episode.** In v2 a "**Creator**" is just a top-level Section, relabeled/re-presented (no schema change).
- **Strictly read-only for library files.** The only DB-side mutations are the opt-in rename tool and the v2 creator-art override (stores a *path* to a user-chosen image; never writes into library folders).
- Self-contained: all playback/thumbnails/metadata come from bundled libVLC. **No external media tools (ffmpeg/HandBrake) on PATH**, no network for content.
- v1 (M1–M6) complete. v2 (M7–M10) is a creator-centric redesign + immersive player. M7 merged. **M8 is next (the task above).**

---

## 2. Repo & environment

- **Repo:** `https://github.com/yovanmc/VideoShelf` (default branch `main`, public).
- **Local path:** `C:\Agent Projects\VideoShelf`
- **OS/shell:** Windows 11, PowerShell 7 (`pwsh`). `&&`/`||` work in pwsh.
- **Solution:** `VideoShelf.slnx` (.NET 10 XML solution format).
- Source layout: `src\VideoShelf.Core` (logic + SQLite repos), `src\VideoShelf.App` (WPF MVVM app), `tests\VideoShelf.Core.Tests`, `tests\VideoShelf.App.Tests`.

---

## 3. How we operate (two-phase, roadmap-driven)

We run a **one-milestone-per-cycle** workflow with a single source of truth at the repo root, `ROADMAP.md`:

- **Plan phase:** write a fully self-contained plan for the next milestone, save it under `docs/superpowers/plans/`, flip the ROADMAP row to `📝 Plan ready`. *(Already done for M8 — that's what you're executing.)*
- **Build phase (your job now):** implement the `📝 Plan ready` milestone exactly, verify, PR, watch CI, merge, flip the ROADMAP row to `✅ Merged`, and append durable facts/gotchas to the ROADMAP's "Decision log & gotchas" section.

A fresh executor with no memory should be able to read `ROADMAP.md` + the plan file and proceed. Keep all durable state in those files, not in chat. The ROADMAP legend: `✅ Merged · 🔨 In progress · 📝 Plan ready · 🔬 Researching · [ ] Not started`.

The plan is written to be implemented in **bite-sized tasks** with a test gate after the code, then a visual screenshot pass. If you have a subagent/parallel capability, the heavy implementation can be delegated; if not, just work the tasks sequentially yourself.

> The complete workflow this is based on (the "roadmap skill") is reproduced verbatim in **§10** below, followed by notes on adapting it for Codex in **§11**.

---

## 4. Conventions (CRITICAL — these override defaults)

- **Build:** `dotnet build VideoShelf.slnx -c Release -v minimal`
- **Test gate:** `dotnet test VideoShelf.slnx -c Release --nologo -v q` — must be **all green**. M7 baseline is **222 tests (105 Core + 117 App)**; M8 should land a higher count.
- **`gh` is NOT on PATH** → call it by full path. pwsh: `& "C:\Program Files\GitHub CLI\gh.exe" ...` (bash: `"/c/Program Files/GitHub CLI/gh.exe" ...`).
- **Direct pushes to `main` are BLOCKED.** Every change (including docs/ROADMAP) ships via a **branch + PR**. Merge with `gh pr merge <#> --merge --delete-branch` (no squash) **from the main repo root**.
- **Worktrees:** feature work is done in worktrees under `.worktrees/`. (Optional if you prefer plain branches — just don't push to `main`.)
- **Commit identity:** human author **`yovanmc <yovanmc@users.noreply.github.com>`** (the global git config — just use plain `git commit`, don't override `user.email`). The project convention adds the trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` and explicitly says **"No Codex trailer."** Since this is now Codex doing the work, **ask the user how they want the trailer handled** — keeping author `yovanmc` is non-negotiable; the co-author trailer is the user's call.
- **CI:** two jobs — `build-and-test` and `package`. Both must pass before merge. They take ~3 min each; after opening the PR, wait ~20s then watch with `gh pr checks <#> --watch`.
- **Theming rule (caused regressions in a sibling project):** never override/re-base a WPF-UI themed control's Style/ControlTemplate for cosmetics — **additive only** (Opacity/RenderTransform/overlays).
- **Cross-thread rule:** never `ConfigureAwait(false)` on an async chain that ends by mutating a UI-bound `ObservableCollection` — WPF throws. Keep heavy work in `Task.Run` but resume on the captured UI context.
- **WPF binding gotcha:** `RangeBase.Value` (ProgressBar/Slider) binds **TwoWay by default**; pin `Mode=OneWay` when bound to a read-only/computed property (a TwoWay→read-only binding throws the instant it activates). Also: a failed binding *path* silently falls back to the DP default and unit tests never catch it — only an eyes-on screenshot pass does.

---

## 5. Current milestone state

| # | Milestone | Status |
|---|-----------|--------|
| M1 | Foundation (core indexer) | ✅ Merged (#1) |
| M2 | App shell + library browse | ✅ Merged (#4) |
| M3 | Playback (libVLC player + PiP) | ✅ Merged (#7) |
| M4 | Discovery + tags | ✅ Merged (#10) |
| M5 | Opt-in rename tool | ✅ Merged (#13) |
| M6 | Harness + release polish (MSIX) | ✅ Merged (#15) |
| M7 | Creator model + card system + shell foundation | ✅ Merged (#17) |
| **M8** | **Home + Search redesign (creator-centric)** | **📝 Plan ready ← DO THIS (plan PR #19 merged)** |
| M9 | Creator page (Netflix-style) | [ ] Not started |
| M10 | Immersive player redesign | [ ] Not started |

---

## 6. What M8 builds (locked design — full detail is in the plan file)

- **Home becomes a curated funnel** keeping all rails, reordered: Continue-watching → **Recommended creators** (creator cards) → **Recommended videos** (video cards) → Recently-added → Recently-watched → **Pick-a-tag (migrated from section cards to creator cards)**.
- **"For you" = two homogeneous sub-rails** (creators, then videos) — not one interleaved rail.
- **Search = a new `AppView.Search`** opened from a **persistent top search box** in the nav chrome; results are two grouped sections, **Creators** (creator cards) / **Videos** (video cards).
- New Core queries: `LibraryRepository.SearchCreators` / `SearchVideos`; `DiscoveryRepository.GetRecommendedVideos` via a refactored shared `ScoreSections(now)`.
- New App pieces: `CreatorCardFactory`, reworked `DiscoveryViewModel` + `DiscoveryView.xaml`, new `SearchViewModel` + `SearchView.xaml`, shell wiring (enum + search box + DI + test factory + harness `--view Search`).
- Tests at every layer + a Home/Search screenshot sweep.

The plan's 9 tasks (Task 1 Core recommended-videos, Task 2 Core search, Task 3 CreatorCardFactory+DI, Task 4 Home rework, Task 5 SearchViewModel, Task 6 SearchView + shell wiring + harness, Task 7 build/test, Task 8 visual sweep, Task 9 ship) each have complete code and a STOP-if-mismatch note. **Do not deviate from the plan without surfacing it.**

---

## 7. Durable gotchas (most likely to bite you)

- **Scanner is FLAT:** `FolderScanner` treats each immediate subfolder of a source as a Section; videos must sit *directly* under it. Nested per-series folders are ignored. Series are grouped from **filenames** by `TitleParser` = base title + **first integer token** (`"Big Buck Bunny 1"` → ep 1; `"…S01E01"` does NOT group).
- **`duration` is never populated app-wide** (the scanner doesn't probe it), so `ProgressFraction` is always 0 and continue-watching/recommended progress bars render empty. This is expected — don't try to "fix" it in M8.
- **Core DB access idiom:** repos open a connection per call via `db.Open()`, parameterize with `$`-prefixed placeholders, and SQLite `LIKE` is escaped via `query.Trim().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")` with `ESCAPE '\'`.
- **Migrations** use an idempotent `EnsureColumn(conn, table, col, def)` helper (pragma_table_info guard), not inline try/catch. (M8 needs no schema change.)
- **Persistent left sidebar** (sources + "CREATORS") renders on every view by design — when judging screenshots, evaluate only the main content area.
- **Screenshot harness (from M6):** launch hooks `--folder`/`--autostart`/`--view <…>`/`--done-signal`/`--data-dir`/`--seed-demo`; capture script `Run-VisualSweep.ps1`. Gotchas: wait for `IsWindowVisible`; settle ~5s for the WPF-UI Mica backdrop to compose (else all-black); use a `TOPMOST→NOTOPMOST` bring-to-front toggle; **a composited/unlocked desktop is required** (a locked/RDP session captures pure black). Run `Generate-Fixtures.ps1 -Force` if fixtures look stale. **M8's plan has you add a `--view Search` case to the harness.**
- **Git:** stale `.git/index.lock` → delete & retry; remove a worktree before `git branch -d`; run `gh pr merge` from the main repo root, not a worktree.
- **Commit-message tip on Windows:** don't paste multi-line messages via PowerShell here-strings into a bash shell (or vice versa) — they mangle. Use `git commit -F <file>` or your shell's native here-string consistently.

---

## 8. Definition of done for M8

1. All plan tasks implemented; `dotnet test VideoShelf.slnx -c Release --nologo -v q` is **all green**, count > 222.
2. Home renders the 6 rails correctly; Search renders grouped Creators/Videos from the top search box — verified by an actual screenshot pass (not just tests).
3. PR opened, `build-and-test` + `package` CI both green, merged `--merge --delete-branch` to `main`.
4. `ROADMAP.md` M8 row flipped to `✅ Merged` (with PR #), and an "M8 shipped" entry appended to the Decision log capturing the final test count and any STOP-and-report deviations. (This ROADMAP flip ships on the M8 branch, since direct pushes to `main` are blocked.)
5. Tell the user it's merged.

---

## 9. Reference: full milestone history & decision log

For the complete shipped-state of M1–M7, all locked v2 design decisions, and every durable gotcha captured to date, read **`ROADMAP.md`** (repo root) — its "Decision log & gotchas" section is the authoritative record. The M8 planning decisions are logged there too.

---

## 10. The roadmap skill — full text (verbatim)

> This is the workflow the whole operating model above is based on. It is a single markdown
> file living at `C:\Users\cayov\.claude\skills\roadmap\SKILL.md` on the user's machine.
> It is written for **Claude Code on mobile** — see §11 for which parts to keep vs. translate
> when building a Codex equivalent.

```markdown
# Roadmap-Driven Two-Phase Build Workflow

A generic, repeatable workflow the user uses across **all** their projects. **Opus is always the orchestrator** — the user runs Claude on mobile and cannot switch the session model — so cost is controlled by phase (plan vs build) and by delegating the build to cheap Sonnet **subagents**, never by changing the session model. All durable state lives in one file per project so a fresh (cleared) session can resume cheaply.

**Announce at start:** "Using the roadmap workflow."

## Roles

- **Opus = orchestrator (always).** The session is always Opus. It brainstorms/grills to a sharp definition, runs research, writes fully self-contained plans, and — in the build phase — drives implementation by dispatching Sonnet subagents and reviewing their committed work. Opus itself does not hand-write production code; it delegates that.
- **Sonnet = subagent builders.** Implementation is done by Sonnet *subagents* dispatched from the Opus session (via `subagent-driven-development`), **not** by switching the session to a Sonnet model. They implement → verify (tests + screenshots); the Opus controller takes it through PR → foreground CI watch → merge → update ROADMAP.md.

The assistant cannot change the session model, and the user **cannot** switch models on mobile (`/model` is a local-terminal-only picker). So this workflow **never** asks the user to change models. Cost is controlled by `/clear`-ing between phases and by delegating the heavy build work to cheap Sonnet subagents — both of which the **handoff ping** prompts.

## Operating principles

- **High autonomy, minimal interruption.** Run the phase end-to-end without stopping to ask permission for safe, reversible actions (reading, planning, coding, tests, commits on a branch, opening/merging your own PRs once CI is green). Don't narrate options you won't take or ask "should I continue?" — just continue. **Only** stop for: a genuinely destructive/irreversible or outward-facing action that isn't already authorized, a BLOCKED subagent you can't unblock, or true ambiguity that changes what you build. The phase-handoff ping is the normal stopping point — not mid-phase check-ins.
- **Safety is the bound on autonomy.** Honor the user's standing destructive-op discipline: verify before destroying, prefer recoverable ops, fail safe, never delete/overwrite something you didn't create or that contradicts how it was described. Within that boundary, act.
- **Use the superpowers skills — token-consciously.** Still drive the work with `brainstorming` (definition), `writing-plans` (plans), `subagent-driven-development` (execution), and research — but cheaply:
  - **Don't re-invoke a skill already loaded this session.** Apply its guidance from context instead of reloading it.
  - **Batch decisions.** When you must consult the user, prefer one `AskUserQuestion` with 2–4 bundled choices over many one-at-a-time round trips.
  - **Delegate ingestion.** Reading files and online research go to cheap `Explore`/general subagents that return a compact digest; the Opus session writes from the digest.
  - **Cheapest model that fits.** Mechanical implementation → Sonnet; reserve Opus for genuine design/planning judgment.

## The source of truth: `ROADMAP.md` (repo root)

One file per project, at the repository root. It holds: the definition, the milestone table (the authoritative "what's next"), links to plan files, conventions, and a decision log. A fresh session reads it, finds the **topmost milestone that is not ✅ Merged**, and acts on it.

\```markdown
# <Project> — ROADMAP

> Source of truth for what to build next. Follows the `/roadmap` workflow
> (Opus plans/researches · Sonnet implements · ping at every phase handoff).

**Legend:** ✅ Merged · 📝 Plan ready (execute next) · 🔬 Researching/Planning · [ ] Not started (plan first)

## Definition
<one-paragraph what-it-is> · Repo: <url> · Spec: <path> · Conventions/runbook: <path or inline>

## Conventions
<build / test / verify commands · commit author & trailer · CI job · merge style>

## Milestones
| # | Title | Status | Plan | PR | Notes |
|---|-------|--------|------|----|----|
| 1 | ... | ✅ Merged | <plan path> | #1 | one-line shipped summary |
| 2 | ... | 📝 Plan ready | <plan path> | — | next session executes this |
| 3 | ... | [ ] Not started | — | — | scope note |

## Decision log & gotchas
- <durable facts, design decisions, env gotchas — so no work is duplicated or re-broken>
\```

## On invocation — decide the phase

1. **No `ROADMAP.md`?** This is a new (or un-onboarded) project → you are in an **Opus planning phase**. If the project is also undefined, run `superpowers:brainstorming` first (grill the user one question at a time; research the domain online/in-repo as needed) until the definition is sharp, then write `ROADMAP.md` with the milestones.
2. **`ROADMAP.md` exists** → read it and find the topmost non-✅ row:
   - `[ ] Not started` → **planning phase**: write that milestone's self-contained plan (`superpowers:writing-plans`), save it under `docs/superpowers/plans/`, flip the row to `📝 Plan ready` with the plan link.
   - `📝 Plan ready` → **build phase** (Opus controller + Sonnet subagents): execute that plan.
   - All `✅ Merged` → the roadmap is complete; tell the user and offer to scope the next phase.

Run whichever phase the roadmap calls for **in this Opus session** — there is no model to match. `/clear` between phases (via the handoff ping) keeps each context small; never ask the user to switch models.

## Phase A — Opus (research + plan)

- **Delegate the reading/research to cheap subagents.** Don't ingest many files in Opus context. Dispatch a Sonnet `Explore` (or general) subagent to return a *compact digest* of the exact signatures/shapes/APIs you need, then write the plan from that digest. Same for web research — a subagent returns a synthesis.
- Use `superpowers:brainstorming` for definition (new projects/features) and `superpowers:writing-plans` for plans. Plans must be **fully self-contained** (exact files, complete code, exact commands, expected output per bite-sized task) with a header: *"Written for Sonnet execution; if something doesn't match, STOP and report rather than guess."*
- **Update `ROADMAP.md` on completion** (flip the row to `📝 Plan ready`, link the plan, append any decisions to the decision log). Commit + push.
- **Ping the user** (see Handoff) and **STOP**.

## Phase B — Sonnet (implement)

- Execute the `📝 Plan ready` milestone with `superpowers:subagent-driven-development` (fresh sub-implementer per task; review by reading committed code; don't pause between tasks).
- Verify: run the project's tests and its screenshot/self-verification harness; inspect results.
  - **Screenshots are viewed by a subagent, never loaded into this Opus session.** The screenshot/self-verification harness writes PNGs to disk; dispatch a Sonnet subagent to Read those PNGs, inspect them against the milestone's acceptance criteria, and return a **text verdict** (PASS/FAIL + specific observations + the absolute file paths it viewed). Act on the text. This keeps image tokens out of the long-lived controller context — they'd otherwise be re-sent every turn (~1.2–1.6k tokens each), the single biggest avoidable token leak in a build session.
  - **Escape hatch:** only if the user **explicitly** asks to see a screenshot ("show me", "let me see it") do you Read the relevant PNG into this session (it renders on their iOS app; SendUserFile/widgets don't). Use the paths the subagent reported — don't re-run capture. On a FAIL, it's fine to proactively offer to show it rather than auto-loading.
- Finish: push the branch → open a PR → **foreground** `gh pr checks <PR#> --watch` (sleep ~20s first to dodge "no checks reported") → merge from main `--merge --delete-branch` → sync main.
- **Update `ROADMAP.md`** (flip the row to `✅ Merged` with PR # and a one-line summary; add gotchas to the decision log). Commit + push.
- **Ping the user** (see Handoff) and **STOP**.

## Handoff (the ping)

The session that **finishes a phase** sends a `PushNotification` whose body includes **the exact prompt to paste into the next session**. The next session **stays on Opus** — the ping asks only for `/clear` (or `/compact`), never a model switch.

**Always fully qualify the next session's target.** The user has **multiple repos, each with its own `ROADMAP.md`** — a bare `ROADMAP.md` is ambiguous to a freshly-cleared session that may be in a different repo. Every paste-prompt MUST include the **absolute path** to *this* project's `ROADMAP.md` and **name the specific milestone** (number + title) being handed off, so it stands alone with zero working-directory assumptions. Templates (replace every `<...>` with concrete values — never leave a bare `ROADMAP.md`):

- End of Phase A: `"<Project> <M# Title> plan ready (pushed). /clear, then paste: 'Use the roadmap skill to execute the 📝 Plan ready milestone (<M# Title>) in <ABSOLUTE\PATH\TO\ROADMAP.md> end-to-end — implement (Sonnet subagents), verify, PR, watch CI, merge, update that ROADMAP.md, ping me when merged.'"`
- End of Phase B: `"<Project> <M# Title> merged & CI-green. /clear, then paste: 'Use the roadmap skill to plan the next milestone (<next M# Title>) in <ABSOLUTE\PATH\TO\ROADMAP.md>.'"`

(Example absolute path: `C:\Agent Projects\AudioShelf\ROADMAP.md`.) If you can name the next milestone's plan path, include it too.

Keep the notification body within ~200 chars; if the full paste-prompt is long, put the short form in the ping and the full paste-prompt — with the absolute path — as the last line of your chat reply.

### Why always-Opus (the mobile constraint)

The user drives Claude from the **mobile / remote-control** surface, where `/clear` and `/compact` **work** but `/model` does **not** (it's a local-terminal-only picker), and you generally can't start a new session there. **No script/hook/SDK can switch a running session's model for the user.** So this workflow is **single-model by design**: stay on Opus the whole time, `/clear` (or `/compact`) between phases to shed context (the biggest token lever — and the one that IS available remotely), and delegate the build tier to Sonnet **subagents** (an Opus controller dispatches Sonnet sub-implementers; the heavy lifting is cheap). **Never put a `/model` step in a ping.** (Pinning a model at all needs the local CLI — `claude --model <x>`, `settings.json` `"model"`, or `ANTHROPIC_MODEL` — which the user can't do mid-session on mobile.)

## Token discipline (why this exists)

1. **`/clear` between phases** — a long session reprocesses its whole transcript every turn; clearing is the biggest saving.
2. **State lives in `ROADMAP.md`/plan files, not the conversation** — a fresh session re-reads a lean file cheaply.
3. **Opus delegates reading/research — and, in the build phase, implementation — to cheap subagents** — the Opus controller spends tokens on judgment, writing, and review, not on ingestion or hand-coding.
4. **CI + tests + screenshot walkthrough are the quality gate** — not the controller re-reading every commit.
5. **Screenshots stay in a subagent, never the controller** — a Sonnet subagent views the PNGs and returns a text verdict; images enter the main context only if the user explicitly asks to see one. Images are re-sent every turn they linger (~1.2–1.6k tokens each), so this is a major saving. See Phase B.
6. **Plan just-in-time, one milestone per Opus session** — avoids drift-rework and keeps each context small.
7. **Read a file once, then pass excerpts — never re-read.** A Read sits in context and is re-billed every turn; a transcript audit found single files re-read 14–55× per session (~860k wasted tokens). The orchestrator Reads a file once and injects the relevant *snippet* into each subagent prompt; subagents must not re-Read the whole file per task. Don't re-Read a file you just edited — the Edit/Write tool already confirmed the change.
8. **Never enumerate a tree without excludes.** Use `Glob` with a targeted pattern. A raw `Get-ChildItem -Recurse` over a repo dumped 280–400 KB artifact listings into context; always exclude `target/ dist/ node_modules/ .build/`.
9. **Quiet builds.** Build with `-v minimal` (`dotnet build -v minimal`); verbose MSBuild logs added 3×106 KB of zero-value output. On success none of it matters; on failure only the error lines do.

## Red flags

- Hand-writing production code in the controller instead of dispatching Sonnet subagents.
- Putting a `/model` step in a handoff ping (the user can't switch models on mobile — `/clear` only).
- Advancing a milestone whose plan doesn't exist yet (plan first).
- Finishing a phase without updating `ROADMAP.md` (next session repeats work).
- Finishing a phase without pinging the paste-ready handoff prompt.
- Handoff prompt that says a bare `ROADMAP.md` instead of the **absolute path** (the user has multiple repos with that filename — a cleared session can't disambiguate) or that omits the specific milestone (number + title).
- Reading many files directly in Opus instead of via a subagent digest.
- Loading verification screenshots into the controller context instead of having a subagent view them and return a text verdict (only surface an image when the user explicitly asks).
- Re-reading the same file repeatedly — or re-reading a file right after editing it — instead of reading once and passing excerpts to subagents.
- `Get-ChildItem -Recurse` / unfiltered tree listings that dump build artifacts into context (use `Glob` with excludes).
- Verbose build output in context (build with `-v minimal`).
```

---

## 11. Adapting this skill for Codex (read before copying it wholesale)

The skill above is written for **Claude Code on mobile**, where the central constraint is *"the session is always Opus and the user can't switch models."* Several mechanics exist purely to work around that. When Codex builds its own version, **keep the durable workflow, drop the Claude-specific scaffolding:**

**Keep (these are the actual value):**

- **One `ROADMAP.md` at the repo root as the single source of truth** — milestone table with statuses (`✅ Merged / 📝 Plan ready / [ ] Not started`), plan links, a conventions block, and a decision-log/gotchas section. A fresh session reads it, finds the topmost non-merged row, and acts.
- **The two-phase split: plan, then build.** Phase A writes a **fully self-contained plan** (exact files, complete code, exact commands, expected output per bite-sized task, plus a "STOP and report if reality doesn't match" header) saved under `docs/superpowers/plans/`, and flips the row to `📝 Plan ready`. Phase B executes that plan, verifies, PRs, watches CI, merges, flips the row to `✅ Merged`, and appends durable gotchas to the decision log.
- **One milestone per cycle**, planned just-in-time.
- **CI + tests + a screenshot/self-verification pass are the quality gate** — not re-reading every commit. For UI work, verify with actual screenshots, not just unit tests (binding-path failures pass tests but render wrong).
- **High autonomy:** run a phase end-to-end without asking permission for safe, reversible actions; only stop for destructive/irreversible/outward-facing actions, a genuine blocker, or ambiguity that changes *what* you build. Batch any necessary user questions into one round.
- **Self-contained handoff at each phase boundary** so the next (fresh-context) session can resume from files alone, with the **absolute path to the ROADMAP and the specific milestone (number + title)** named explicitly (the user has multiple repos all named `ROADMAP.md`).

**Drop or translate (Claude-Code-specific):**

- *"Always Opus / never switch models / Sonnet subagents / never put `/model` in a ping"* — this entire axis exists only because the user is pinned to one model on Claude's mobile surface. Codex should substitute its own cost-control lever (e.g. its own model tiers / reasoning-effort settings, or whatever delegation Codex supports). The principle that survives is: **spend the expensive tier on planning/design/review judgment, the cheap tier on mechanical implementation and file ingestion.**
- *`/clear` / `/compact` between phases* — translate to "start the build phase with a fresh context that loads only `ROADMAP.md` + the plan file," however Codex resets context.
- *`superpowers:brainstorming` / `writing-plans` / `subagent-driven-development` skill references, `Explore` subagents, `PushNotification`, `AskUserQuestion`* — these are Claude tool/skill names. Map them to Codex equivalents (its own planning routine, its own sub-task/agent mechanism, its own notification path, its own clarifying-question UI).
- *Token-discipline specifics (`-v minimal`, "read once then pass excerpts", "no `Get-ChildItem -Recurse`")* — keep the spirit (don't flood context with build logs or unfiltered tree dumps; don't re-read files you just edited), but the exact numbers are from a Claude transcript audit and aren't laws.

**Net:** the reusable core is *"ROADMAP.md as durable state → plan one milestone to a self-contained spec → execute it autonomously → verify with CI/tests/screenshots → PR → merge → log gotchas → hand off cleanly."* Everything about Opus/Sonnet/`/model`/`/clear` is a workaround for a Claude-mobile limitation Codex doesn't share.
