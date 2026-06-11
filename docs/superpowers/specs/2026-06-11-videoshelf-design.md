# VideoShelf — Design Spec

- **Date:** 2026-06-11
- **Status:** Approved (brainstorming complete; ready for implementation plan)
- **Working name:** VideoShelf (parallels AudioShelf; renameable before public release)
- **Repo home:** `C:\Agent Projects\VideoShelf` (own git repo, published to the user's GitHub)

## 1. Summary

A Windows desktop app for browsing and watching a large personal **video** library
spanning **multiple root folders**. The collection is mixed (a creator's skits, home
videos, episodic series, one-offs) and carries **no reliable embedded metadata** — the
signal is the folder structure and filenames. VideoShelf builds and owns an indexed library
on top of those files, tracks watched/unwatched state, lets the user tag sections, and
surfaces content through a tag-driven discovery panel. Playback is fully integrated and
**plays any format** by bundling libVLC. The app is **read-only on the video files by
default**, with one explicit, reversible rename tool.

This is, in shape, "AudioShelf for video" built on the VideoTriage stack: the same
library/discovery/harness ideas as AudioShelf, but on .NET WPF with an embedded libVLC
player because robust play-everything playback is the core requirement.

## 2. Principles

- **Read-only by default.** The app never moves or deletes video files, and never mutates
  them except through the explicit, opt-in rename tool (Section 10), which requires preview +
  confirmation and writes an undo manifest. Grouping corrections are stored in SQLite, never
  written to disk.
- **App-owned metadata.** Files carry no reliable tags, so the SQLite DB is the source of
  truth for grouping, tags, watched-flags, and cleaned-up display titles. The video bytes on
  disk are the only thing the DB does not own.
- **Crash-safe & resumable scanning.** Re-scans are incremental and idempotent; an
  interrupted scan never corrupts the index or loses prior state. (Standing user preference.)
- **Self-contained.** libVLC (bundled via NuGet) handles playback, thumbnail snapshots, and
  duration/metadata parsing — **no external tools on PATH** (unlike VideoTriage's
  ffmpeg/HandBrake dependency).
- **Autonomous self-verification.** Ships with a fixture generator and launch/screenshot
  hooks so UI states can be verified end-to-end headlessly before being shown to the user
  (ported from VideoTriage's proven WPF harness).

## 3. Tech stack & architecture

**.NET 10 + WPF + WPF-UI (Fluent, dark Mica theme) + LibVLCSharp/libVLC + SQLite.**
Mirrors VideoTriage's stack and conventions, including its WPF-UI design-token system
(shared color/surface/radius/spacing/type tokens in a merged `DesignTokens.xaml`).

Two projects:

- **`VideoShelf.Core`** — folder scanning, filename→series grouping, SQLite access,
  thumbnail/metadata extraction (via libVLC), and the launch/fixture/screenshot harness
  support. All heavy or I/O work lives here and is unit-testable without the UI.
- **`VideoShelf.App`** — WPF views/viewmodels (CommunityToolkit.Mvvm), the embedded
  `LibVLCSharp.WPF.VideoView` player, the mini-player/PiP window, and DI wiring.

Playback, thumbnails, and duration all come from **libVLC** (`LibVLCSharp` +
`VideoLAN.LibVLC.Windows` NuGet packages). No transcoding; no external binaries.

Tests: **xUnit + Shouldly** (matching VideoTriage). CI mirrors VideoTriage (build + test).

## 4. Library model & scanning

Four-level hierarchy:

```
Source (a root folder the user adds; MULTIPLE)
  Section (immediate subfolder of a source; named by creator or category)
    Series (multi-part, grouped from filenames)  OR  Standalone (single video)
      Episode (video file)
```

- **Sources:** the user adds one or more **root folders**. Each immediate subfolder of a
  source is a **section**. All sources merge into one browsable library, but every video
  records which source it came from.
- **On disk:** a section folder holds all its videos **flat** (no nested season folders).
- **Scale target:** large — virtualized lists; search and discovery run off indexed SQLite
  queries; scanning is incremental.
- **Formats:** any container/codec libVLC supports (.mp4, .mkv, .mov, .avi, .webm, .m4v,
  .wmv, .flv, .ts, etc.). The scanner includes a broad video-extension allow-list.

### Grouping heuristic (filenames → series)

The only grouping signal within a section is the filename. Rule (ported from AudioShelf):
strip a trailing `<number> <optional extra words>` from the filename stem to derive a **base
title**. Files within one section sharing a base title form one **series**; the unnumbered
file is episode 1, numbered files order by their number (natural sort). A file with no
detected siblings is a **standalone**.

The heuristic is fuzzy, so grouping is **reviewable and overridable in the UI**
(merge/split/reassign episode, set base title, set episode number). Overrides are stored in
the DB and **never written to disk**.

## 5. Data model (SQLite)

A **standalone** is modeled as a degenerate **series with a single episode** (its
`base_title` is the video's derived title, `is_standalone = 1`). This keeps `videos.series_id`
always set and lets the UI render series and standalones through one path.

- `sources` — `id`, `root_path`, `display_name`
- `sections` — `id`, `source_id`, `folder_name`, `display_name`
- `series` — `id`, `section_id`, `base_title`, `sort_key`, `is_standalone` (bool)
- `videos` — `id`, `series_id`, `file_path`, `episode_no`, `raw_filename`, `format`,
  `duration`, `thumbnail_path`, `watched` (bool)
- `section_tags` — `section_id`, `tag` (many-to-many)
- `watch_events` — `video_id`, `watched_at` (feeds "recent" for discovery)
- `grouping_overrides` — user corrections to the heuristic grouping
- `settings` — app preferences (source list is the `sources` table)

## 6. Library UI — Source → section → series/standalone → episode

- Virtualized section sidebar (handles many sections across multiple sources).
- Drill-down: section → its series/standalones → expand a series → its episodes.
- **Poster thumbnails** — a representative frame grabbed via libVLC snapshot (series shows
  its first episode's thumbnail; section shows a representative).
- Watched videos are visibly marked.
- Global search box filters across sections, series, and videos.

## 7. Discovery panel

Three coordinated parts (ported from AudioShelf):

- **"For you" (default).** Suggests sections that share tags with the user's
  recently-watched sections (via `watch_events` + `section_tags`).
- **"Pick a tag".** A multi-select tag chooser; results re-rank to sections/series with
  matching tags, weighted toward **mostly unwatched** content.
- **"More from this section".** Contextual section showing other series/videos in the
  section the user is currently viewing/watching.

## 8. Tagging

- Tags are assigned to **sections** (creator/category), from the section view.
- Tag input autocompletes from existing tags.
- Tags are the sole driver of the discovery panel.

## 9. Playback

Embedded `VideoView` (libVLC) with an overlay control bar:

- Play / pause and a draggable seek bar (current time + total time).
- **Volume** control.
- **Fullscreen** toggle.
- **Keyboard shortcuts:** space (play/pause), ←/→ (seek), F (fullscreen), Esc (exit
  fullscreen).
- **Mini-player / PiP** — a detachable, always-on-top small player window for watching while
  browsing the library.

Behavior:

- **Watched/unwatched only — no resume.** No per-second position bookmarking.
- A video is auto-marked **watched** when playback reaches its end; the user can also
  **manually toggle** any video watched/unwatched.
- **No auto-advance, no play queue, no continuous library play.** The user picks the next
  item.
- **No subtitles, no audio-track switching, no playback-speed control** (explicitly out of
  scope for v1).

## 10. Opt-in rename tool

A separate, explicitly-triggered screen (off by default; the app is fully usable without it):

- Shows a **preview diff** of current → proposed clean filenames.
- Requires explicit confirmation before any disk change.
- Performs renames **defensively** (verify the target path is free, fail safe) and writes an
  **undo manifest** enabling rollback — same defensive pattern as VideoTriage's swap/manifest
  approach.
- After a successful rename, the library DB is updated to the new `file_path`s; grouping
  overrides and watched-state survive because they key off stable IDs, not paths.

## 11. Self-verification harness

Ported from VideoTriage's proven WPF approach:

- **`tools/gen-fixture`** builds a synthetic library: multiple source roots, sections,
  multi-episode series and standalones, tiny **playable** video clips across a few formats
  and varied durations.
- **Launch hooks** — `--folder`/`--source`, `--autostart`, `--done-signal` — to drive the
  real app headlessly.
- **Screenshot capture** for walking every UI state (library, drill-down, discovery, player,
  mini-player).
- **`tools/verify.ps1`** orchestrates fixture → launch → screenshot → checks, so UI can be
  self-verified before being shown to the user.

## 12. Project layout

`C:\Agent Projects\VideoShelf`, own git repo on the user's GitHub, mirroring VideoTriage:

```
VideoShelf/
  src/
    VideoShelf.Core/   scanning, grouping, natsort, SQLite, thumbnails/metadata (libVLC), harness support
    VideoShelf.App/    WPF views/viewmodels, embedded player, mini-player, DI
  tests/
    VideoShelf.Core.Tests/
    VideoShelf.App.Tests/
  tools/               gen-fixture, verify.ps1, capture scripts
  docs/                specs and notes
  .github/workflows/   CI (build + test)
```

.NET 10, WPF, WPF-UI, LibVLCSharp, SQLite, xUnit + Shouldly. Commits keep the user's human
identity as git author.

## 13. Out of scope (YAGNI for v1)

- Subtitles, audio-track switching, playback-speed control.
- Resume / exact-position bookmarking.
- Auto-advance, play queue, continuous library play.
- Transcoding or format conversion.
- Streaming / online sources / downloading.
- Casting (Chromecast/DLNA).
- Moving or deleting files (the only file mutation is the opt-in rename tool, Section 10).
- Tags on series/videos (tags are section-level only in v1).
