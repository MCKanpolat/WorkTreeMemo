# WorkTreeMemo contributor guide

## Purpose

WorkTreeMemo is a local-first .NET 10 desktop and command-line application for finding unfinished work in Git repositories. It discovers repositories below user-configured roots, reads their Git state, classifies work-in-progress, and presents the result through an Avalonia desktop UI and a Spectre.Console CLI. The application does not fetch, modify, or otherwise contact tracked repositories.

## Repository layout

- `src/WorkTreeMemo.Core`: domain records, local persistence, Git process integration, repository discovery, scanning, and WIP classification. Keep this project UI-agnostic.
- `src/WorkTreeMemo.App`: Avalonia application, CLI entry point, view models, views, themes, application composition, and release-update check.
- `tests/WorkTreeMemo.Core.Tests`: xUnit tests for core behavior. Add or update a focused test whenever core classification, scanning, storage, or Git parsing behavior changes.
- `docs`: README assets only. Reuse the existing screenshots unless a task explicitly requires new captures.
- `.github/workflows`: CI and release automation. CI builds and tests; releases publish self-contained Windows and macOS archives.

## Architecture and data flow

1. `AppDataStore` owns local data in the OS application-data directory, under `WorkTreeMemo`: `config.json`, `snapshot.json`, `notes.json`, and `activity.jsonl`.
2. `RepoDiscovery` finds repository roots under configured `ScanRoot` entries. Respect configured exclusions and depth limits.
3. `ScanCoordinator` serializes scans, reads Git state concurrently (bounded by `MaxParallelism`), saves a snapshot, and raises UI-facing events. A reindex request received during a scan is queued.
4. `GitCliReader` is the boundary around the local `git` executable. It reads branches, worktrees, porcelain status, and stashes; it must remain cancellation-aware and return a repository-level error snapshot for expected command failures.
5. `WipClassifier` converts snapshots and notes into `WipItem` rows. Keep classification rules deterministic and covered by tests.
6. `MainViewModel` and Avalonia views render and filter items. `CliApplication` exposes the same core operations from the terminal; avoid duplicating scanning or classification logic in either UI.

## Coding conventions

- Target `net10.0`; nullable reference types and implicit usings are enabled. Warnings are errors.
- Use file-scoped namespaces, primary constructors where already idiomatic, immutable `record` models, and async APIs with `CancellationToken` for I/O or process work.
- Keep platform- and UI-specific code in `WorkTreeMemo.App`; put reusable behavior in `WorkTreeMemo.Core`.
- Preserve the app's local-first behavior. Do not add Git mutations, remote fetches, telemetry, or external services without explicit product direction.
- Use `System.Text.Json` and the existing atomic write pattern in `AppDataStore` for persisted data. Do not introduce a second configuration or storage location.
- Localize user-visible UI strings through the existing localization resources. Keep CLI output readable through `Spectre.Console` and escape dynamic values before rendering markup.
- Keep the light and dark themes working together. Changes to Avalonia controls should be checked against both palettes.

## Commands and verification

The SDK is pinned in `global.json` and package versions are centralized in `Directory.Packages.props`.

```bash
dotnet restore WorkTreeMemo.slnx
dotnet build WorkTreeMemo.slnx --configuration Release
dotnet test tests/WorkTreeMemo.Core.Tests/WorkTreeMemo.Core.Tests.csproj --configuration Release
```

For application behavior, the useful development commands are:

```bash
dotnet run --project src/WorkTreeMemo.App -- status
dotnet run --project src/WorkTreeMemo.App -- reindex
dotnet run --project src/WorkTreeMemo.App -- config roots
```

Run the narrowest relevant test during iteration, then run the full core test project for a completed code change when the environment permits. For documentation-only changes, verify links, image paths, and `git diff --check` instead of running the .NET suite.

## Documentation and assets

- The README must use the existing project icon at `src/WorkTreeMemo.App/Assets/worktreememo-icon.png` and documentation images under `docs/` by repository-relative path.
- Do not replace or regenerate screenshots unless explicitly asked. Before adding an image, check that it contains no customer, company, or confidential information.
- Keep sample repository names, paths, commands, and configuration generic and local-first.

## Change hygiene

- Keep changes scoped to the request; do not reformat unrelated files or alter generated outputs.
- Inspect `git status --short` before editing and preserve pre-existing user changes.
- Do not stage, commit, push, publish releases, or edit workflow credentials unless explicitly requested.
- Report commands actually run and verification that could not be completed.
