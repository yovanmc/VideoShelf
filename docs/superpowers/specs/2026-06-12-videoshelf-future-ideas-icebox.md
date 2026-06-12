# VideoShelf — Future Ideas / Icebox

> A curated backlog of **net-new** improvement ideas for **beyond v3**, gathered 2026-06-12 from a
> deep, multi-perspective UX review (four senior subagent lenses: product feature-gaps,
> accessibility, power-user/large-library, and interaction/motion-delight — each studying the 8-screen
> sweep). This is an **icebox**, not a commitment — it feeds future-version scoping. Nothing here is
> on the v3 plan (M11–M14); duplicates of the plan and of already-shipped M10 player work were filtered out.

## What this is NOT (already covered — intentionally excluded)
- **On the v3 plan (M11–M14):** the gear→Settings screen + sidebar removal + back-nav/active-nav +
  first-run/empty states; Search-returns-videos; personal home + watch stats + `duration` population;
  external **subtitle sidecar loading** + **play-all/up-next queue**; the **design system & light/dark
  theming** (icon system, unified `CreatorCard`/`VideoCard`, button hierarchy, type/heading
  consistency, chip-vs-button, **focus rings**, player→Fluent chrome). Also logged-deferred: persistent
  now-playing mini-bar, Browse sort/filter, scan auto-trigger/feedback.
- **Already shipped in M10** (reviewers couldn't fully see current state): player **control auto-hide**,
  **seek-preview hover thumbnail**, **chapter tick marks** on the scrubber, the player **keyboard map**
  (Space/←/→/F/Esc/Ctrl+E), the **resume-offer banner**, and the **Recently-added** rail.

## Cross-cutting top bets (surfaced by ≥2 reviewers — strongest future-milestone candidates)
1. **Video-level (+ series-level) tags → smart filters / saved views.** Tags are creator-level only
   today; per-video tags (DB-only) unlock cross-cutting **saved smart collections** ("unwatched +
   tag=tutorial", "4K not started"). The single biggest organizational unlock. *(product + power-user)*
2. **Library health: missing-file triage & relink + a maintenance dashboard.** Missing files are just
   dimmed today — silent data rot at scale. Relink (repoint a moved file, keep watch state/tags),
   orphan/empty-creator cleanup, duplicate detection, single-file-series flags, last-scan per source.
   *(product + power-user)*
3. **Multi-select + bulk actions.** Range/Ctrl-select across episodes/series → mark watched, tag,
   queue, rename in one go. Foundational primitive everything else compounds on. *(power-user)*
4. **In-player depth (all libVLC-native, no new deps):** **playback speed 0.5–2×** (deferred from v3,
   recurring ask), **chapter/bookmark panel** (chapters exist as ticks; add a list + user bookmarks),
   **A-B repeat**, **audio-track picker** + volume normalize, aspect/zoom presets, frame-step, sleep
   timer. *(product + accessibility)*
5. **Accessibility program** — several **WCAG Level-A** gaps (legal-risk if distributed): UIA
   names/roles, full keyboard reachability, color-independent state. Deserves its own milestone. *(a11y)*
6. **Command palette (Ctrl+K)** — fuzzy jump to any creator/video/action; the fast path at scale. *(power-user)*
7. **Delight layer** — skeleton/shimmer loaders, toasts with inline **Undo**, end-of-video **up-next
   countdown**, **series-complete** moment, shared-element + page transitions. *(interaction)*

---

## A. Organization & curation (DB-only; respects read-only library)
- **Video-level & series-level tags** [H/M] — per-item tags, creator tags cascade as overridable defaults.
- **Smart filters / saved views / virtual collections** [H/L] — named, persisted AND/OR of tag(s),
  creator(s), watched-state, date-added, resolution, duration; appear as sidebar/Home shelves.
- **Favorites / star ratings** [H/S] — per-video heart or 1–5★; seeds a Favorites rail + smart filters.
- **Manual playlists** [M/M] — saved, ordered, cross-creator lists (distinct from the transient queue).
- **Watchlist / "Watch later"** [M/S] — explicit-intent list separate from passive Continue-watching.
- **Watch-history view + bulk mark watched/unwatched** [H/M] — audit log of plays; stamp a series watched without playing.
- **Series grouping override UI (split/merge)** [M/M] — fix mis-grouped series; DB override survives rescan.
- **Manual episode order** [M/M] — drag-reorder a curated watch order independent of filename.
- **Per-item custom cover / "Set thumbnail from current frame"** [M/M] — extend creator-art override down to series/video; capture a good frame as the thumbnail.
- **Random / "Surprise me"** [M/S] — open a random unwatched item.

## B. In-player features (libVLC-native)
- **Playback speed 0.5–2×** [H/S] — `MediaPlayer.Rate`; deferred from v3, strong recurring ask.
- **Chapter & bookmark panel** [H/M] — list embedded chapters + user-dropped named bookmarks (DB).
- **A-B repeat** [M/S] — loop a set segment (language learners, study).
- **Audio-track picker + volume normalize/boost** [M/M] — multi-track MKV; avoid loudness jumps.
- **Aspect-ratio / zoom presets** [M/S] — Source/Fill/Crop/4:3/2.35:1 cycle.
- **Frame step (±1 frame)** [M/S] — `NextFrame()`.
- **Sleep timer** [L/S] — stop after N minutes.
- **End-of-video "Up Next" countdown card** [H/M] — thumbnail + title + countdown ring; click to play now / dismiss.
- **"Play from beginning?" on a completed video** [M/S]; **double-click-to-fullscreen** [H/S];
  **±10s skip overlay** + **volume-scroll feedback** [M/S each] — standard desktop-player idioms not yet present.

## C. Library health & scale
- **Missing-file triage & relink workflow** [H/M] — see top bet #2.
- **Library-health / maintenance dashboard** [H/M] — missing, orphans, dupes, single-file series, DB size, last-scan per source.
- **Duplicate detection** [M/L] — flag by normalized name and/or size+duration; review-only (no auto-delete).
- **Orphan / empty-creator cleanup** [M/S] — auto-hide 0-video creators; review link.
- **Scan incremental diff feedback** [M/M] — "Added 12, updated 3, missing 1" after a scan.
- **Creator-grid + creator-page virtualization** [H/M] — the inline accordion is a wall of rows at 40+ series; virtualize + collapse-all/expand-all + in-page filter; window long episode lists.
- **Command palette (Ctrl+K)** [H/M] — see top bet #6.
- **Browse list/density toggle, A–Z jump-list, breadcrumbs** [M/S] — fast scanning at 50+ creators.

## D. Bulk & power workflows
- **Multi-select + persistent action bar** [H/M] — range/Ctrl-select; "N selected → Mark watched / Tag / Queue / Rename".
- **Bulk mark watched/unwatched, bulk tag** [H/S] — first actions once multi-select exists.
- **Rename: template editor + cross-series bulk rename** [M/S–M] — `{creator} - {series} - {NN}` templates over a selection (extends the per-series tool; respects the manifest-undo safety model).

## E. Accessibility program (its own initiative; ★ = WCAG Level-A / legal-risk)
- **★ UIA automation names/roles on all controls** [H/M] — cards, rails, chips, transport, scrubber (RangeValue pattern).
- **★ Full keyboard reachability** [H/M] — roving-tabindex rails, Enter/Space activation, type-ahead lists, Esc consistency, **focus restoration after player/PiP close**.
- **★ Color-independent watched/progress state** [H/M] — add text/shape/checkmark, not color alone.
- **Contrast pass to AA** [H/S] — secondary text, count badges; **honor Windows High-Contrast themes** [H/M]; **DPI + text-zoom** scaling [M/M].
- **Live regions** [M/S] — announce now-playing, scan-complete, rename-applied.
- **Keyboard reposition for the draggable PiP** [H/M] — quadrant snap / nudge (motor users).
- **Confirm + focus-trap for "Remove source"; persistent Ctrl+Z + undo toast for rename** [M/S].
- **Media a11y** — **caption/subtitle styling** (size/color/background/position) [H/M], **audio-description track selector** [M/M], **reduced-motion** flag wired to `SystemParameters.ClientAreaAnimation` [M/S], one-time **flash/seizure** notice [H/S], caption on/off shortcut.
- **44px minimum hit targets** [M/S]; **toasts not auto-dismissing <5s** (Timing Adjustable) [M/S].

## F. Microinteractions, motion & delight (all with reduced-motion fallbacks)
- **Skeleton/shimmer loaders + thumbnail fade-in** [H/S] — kills the blank-rectangle moments.
- **Scan progress with live count** [H/S]; **toasts/snackbars with inline Undo** [H/S].
- **Shared-element card→hero transition** [H/L]; **accordion easing**, **directional page transitions**, **scroll-position memory**, **rail edge-fade + keyboard snap** [M/S–M].
- **Card hover: progress reveal + subtle elevation** [M/S].
- **"Resumed at HH:MM" toast** [H/S]; **series-complete celebration** [H/M]; **PiP hover-fade controls** + **snap-to-corner spring** [M/S–M].
- **Onboarding/guidance:** keyboard-shortcut **cheat-sheet overlay (?)** [M/S], first-run tour, **what's-new** changelog, **splash screen**, **About panel** (version + library stats), **now-playing in the window titlebar** [L/S].

---

## Constraints to keep honoring (and one worth a future look)
- Stay **read-only** for library files (only the opt-in rename tool / DB-only mutations); **self-contained**
  (bundled libVLC only — no ffmpeg/network/scraping); **OUT:** transcoding, streaming, casting, file deletion.
- **Worth a future revisit (currently out-of-bounds):** the **flat scanner** (videos must sit directly
  under a creator folder) will misgroup deeper trees (creator → year → series) as libraries grow —
  consider an optional one-level-deeper recurse or treating subfolders as series, gated behind the
  grouping-override UI (Section A).

## Suggested grouping into future milestones (if a v4 is scoped)
- **"Organize"** — video-level tags + smart filters/saved views + favorites + playlists/watchlist + watch-history & bulk mark.
- **"Power & scale"** — multi-select/bulk actions + command palette + virtualization + list/density/A–Z + breadcrumbs.
- **"Library health"** — missing-file relink + maintenance dashboard + duplicates + orphans + scan diff + grouping override.
- **"Player depth"** — playback speed + chapter/bookmark panel + A-B repeat + audio-track picker + aspect/zoom + up-next card + double-click/skip/volume idioms.
- **"Accessibility"** — the ★ Level-A program first, then AA contrast/HC/DPI, media a11y, live regions.
- **"Delight"** — motion/transitions, skeleton loaders, toasts+undo, celebratory moments (layer onto the M14 design system).
