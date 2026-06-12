# VideoShelf v2 — Creator-centric redesign (design spec)

> Approved 2026-06-12 via the `/roadmap` brainstorming pass. Scopes the v2 phase
> (milestones M7–M10). The v1 design spec
> ([2026-06-11-videoshelf-design.md](2026-06-11-videoshelf-design.md)) still
> governs everything not restated here; this spec layers the v2 redesign on top.

## 1. Vision

v1 shipped a complete library + player on a **Source → Section → Series/Standalone → Episode**
model with a discovery-rails Home. v2 keeps that data model but **re-presents the
library around "creators"** and gives the whole app a cohesive, full-space visual
language inspired by the Windows **Movies & TV** player.

A **creator** is a top-level subfolder under a source root — i.e. exactly today's
**Section**, relabeled and re-presented. (`D:\Videos\NatGeo` and `D:\Videos\Vox`
→ two creators.) No change to the `sections` table or the four-level hierarchy;
"creator" is a presentation concept over the existing Section row.

### Card philosophy

- Where a **creator** is surfaced → a **creator card**: a representative thumbnail,
  the creator name, and a **"N videos"** count.
- Where an **individual video** is surfaced → a **video card**: that video's
  thumbnail (title/episode label as today).

These two reusable cards are used consistently everywhere — Home, Search, creator
pages — so the app reads as one cohesive surface.

## 2. Creator thumbnail resolution

A creator card's image resolves in this precedence:

1. **User-assigned override** — an image the user picked in-app for this creator,
   stored in the DB (path only). The single new mutation v2 introduces.
2. **Representative video frame** (default/fallback) — derived from the creator's
   content via the existing libVLC snapshot infra (the same seed-frame approach
   already used for series thumbnails). Zero setup, always available.

No folder-image scanning, no online scraping. The app stays **strictly offline /
read-only for library files**. The creator-art override is **DB-only** (we store a
path to a user-chosen image; we never copy into or write to the library folders) —
a second read-only-respecting mutation alongside the opt-in rename tool.

## 3. Cohesive shell + immersive player (Movies & TV cohesion)

A unified visual pass across the whole app:

- **Shared design language** — restyled nav chrome, consistent spacing/typography,
  and the two reusable cards (`CreatorCard` / `VideoCard`) as the building blocks.
- **Immersive player** — video edge-to-edge with no surrounding app chrome during
  playback; **auto-hiding overlay controls** (fade in on mouse-move, hide after
  inactivity); **polished Fluent transport** (scrubber with chapter/buffered
  markers + seek-preview thumbnail, time, volume, subtitle/audio/chapter pickers,
  PiP, fullscreen). Additive theming only (never re-base a WPF-UI control template
  for cosmetics — the standing theming rule).

## 4. Surfaces

### Home — a curated funnel (not a static grid)

Home directs the user into one of two experiences:

- **Continue watching** — in-progress videos as **video cards** (ordered by
  `resume_updated_at`), the highest-value entry point.
- **For you / you may like** — recommendations as a mix of **creator cards**
  (recommended creators) and **video cards** (recommended videos), powered by the
  existing 14-day half-life `DiscoveryScoring`.

### Creator page (Netflix-style)

Clicking a creator card opens that creator's page:

- The creator's art rendered as a **faded background** (dimmed for readability).
- A grid of the creator's **series groups** (series grouping kept; expandable to
  episodes) and **standalone videos**.
- Section/creator **tag editor** (as today).
- **Creator-art override** entry (pick/clear the creator image).
- **"Rename files…"** entry available here.

### Search — mixed results

A single query returns **matching creators as creator cards** and **matching videos
as video cards**, in grouped sections ("Creators" / "Videos").

## 5. Folded-in v1 loose threads

v2 clears all four deferred items, each placed where it fits naturally:

- **PiP black-frame fix** — the vout-restart (save position → `Stop`/`Play` → seek)
  so the mini-player renders live video, done during the immersive player work.
- **Seek-preview off-screen decoder** — replaces on-demand snapshots with a
  dedicated decoder for smooth scrubber previews, paired with the polished scrubber.
- **Browse/creator rename entry** — the "Rename files…" entry on creator surfaces
  (v1 had it only on section-detail).
- **Sort/search `_pending` race** — the harmless `LibraryViewModel` task-race tidy.

## 6. Milestones (see ROADMAP.md for the authoritative table)

| # | Milestone | Folds in |
|---|-----------|----------|
| M7 | Creator model + card system + shell foundation | sort/search race |
| M8 | Home + Search redesign (creator-centric) | — |
| M9 | Creator page (Netflix-style) | browse/creator rename entry |
| M10 | Immersive player redesign | PiP black-frame fix, seek-preview decoder |

M7 is foundational (the creator read-model + reusable cards + design language);
M8/M9 consume those cards; M10 is the self-contained immersive-player pass.

## 7. Out of scope (unchanged from v1 §13)

Playback-speed control, external subtitle sidecars/downloading, whole-library
continuous play/queue, online metadata/poster scraping, transcoding, streaming,
casting, file deletion. (The creator-art override is the only new mutation, and it
is DB-only — it never writes to library folders.)
