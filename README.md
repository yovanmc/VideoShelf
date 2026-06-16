# VideoShelf

A local-only personal Windows video library and player. VideoShelf scans folders you
point it at, organizes them as **Creator → series/standalone → episode**, and plays
everything through a lean, immersive player with draggable picture-in-picture. It adds
discovery rails, tags, favorites/ratings, playlists, watch-later, a play-queue/up-next,
an Insights dashboard, and a full library-maintenance suite (relink missing files,
duplicate review with a recoverable Recycle-Bin keeper flow, orphan cleanup, health
dashboard). The UI is a dark, "black-glass" Ice-Cyan design.

It is **self-contained**: all playback, thumbnails, and metadata come from a bundled
libVLC — there are no external media tools and no network calls for content.

## Invariants (load-bearing — these define what VideoShelf is)

- **Local-only / personal.** No telemetry, no accounts, no network access for content.
- **No external media tools.** No `ffmpeg`/`HandBrake` (or any media tool) on `PATH`;
  playback/probing/thumbnails are all bundled libVLC. CI asserts the published app
  ships no media tools.
- **Library files are never written.** All mutations are DB-only, plus a private data
  directory under `%LOCALAPPDATA%\VideoShelf\` (SQLite database, chosen art, captured
  cover frames, crash logs). The one opt-in file operation — the rename tool — is
  manifest-backed, crash-safe, and undoable, and only ever renames files you explicitly
  select. A regression test audits the source tree so no file write can land outside the
  reviewed allowlist.

## Build & run

Requires the **.NET 10** SDK on Windows.

```
dotnet build VideoShelf.slnx -c Release
dotnet run --project src/VideoShelf.App/VideoShelf.App.csproj -c Release
```

The solution file is `VideoShelf.slnx` (the .NET XML solution format).

## Tests

```
dotnet test VideoShelf.slnx -c Release
```

## More

- [`ROADMAP.md`](ROADMAP.md) — the milestone-by-milestone history and source of truth for what was built.
- [`CHANGELOG.md`](CHANGELOG.md) — high-level release notes.
