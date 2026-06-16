# Changelog

All notable changes to VideoShelf. This project is a local-only personal Windows
video library + player; "1.0" marks the feature-frozen, hardened release.

## 1.0.0 — 2026-06-16
First stable release. A self-contained (.NET 10 WPF + bundled libVLC) creator-centric
video library: multi-source scan -> Creator -> series/standalone -> episode; lean
immersive player + draggable PiP; play-queue/up-next; favorites/ratings/playlists/
watch-later; full maintenance suite (relink, duplicate keeper via Recycle Bin,
orphan cleanup, health dashboard); Insights dashboard; black-glass Ice-Cyan design.
Strictly read-only for library files. No network for content; no external media tools.

### Hardening in 1.0 (M25)
- Global UI-thread + AppDomain exception net with logged crash reports.
- Destructive-path safety audit + regression tests (Recycle-Bin keeper gate,
  rename crash-mid-apply resume, remove-source DB-only undo, frame-picker write scope,
  a "library never written" audit gate).
- Fail-path hardening (visible player errors, bitmap fallback, skip-and-continue
  scans, empty/busy-DB read safety).
- First schema-version migration (PRAGMA user_version) — dropped the feature-cut
  orphan table (smart_views).

## Earlier milestones (v1–v5, M1–M24)
- **v1 (M1–M6)** — foundation: Core indexer (scan/group/repos/SQLite), app shell + library browse, libVLC playback + draggable PiP, discovery rails + section tags, opt-in crash-safe rename tool, release harness + signed MSIX.
- **v2 (M7–M10)** — creator-centric redesign + immersive player: section→Creator model + reusable card system, creator-centric Home & Search, Netflix-style creator page (hero + accordion), full-window edge-to-edge player with auto-hiding controls.
- **v3 (M11–M15)** — polish & personalization: shell/nav & Settings restructure, personal Home + watch stats (real durations via libVLC probe), external subtitle sidecars, explicit play-queue/up-next, and the app's first owned dark-only design system + visual-consistency pass.
- **v4 (M16–M21)** — depth & scale: video-level tags + smart views, multi-select/bulk + command palette + virtualization, library health & maintenance (relink/duplicates/orphan cleanup), player depth (Films&TV transport, speed, A-B repeat, up-next card), an accessibility program, and delight/motion (toasts/undo, transitions, reduced-motion gate).
- **v5 (M22–M25)** — depth & reach: performance & scale (stress fixture + virtualization fixes + bounded-parallel probe + DB read tuning), creator-framed legibility & findability (avatars, real thumbnails, richer search), Lean + Refresh (cut bloat, black-glass Ice-Cyan, Insights dashboard, creator portrait-from-a-frame), and this Stabilize & Harden 1.0.
