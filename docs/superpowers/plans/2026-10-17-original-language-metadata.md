# Original Language Metadata Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Jellyfin 12.2 plugin that gives Swedish-/Finnish-original movies and series Swedish/Finnish metadata, automatically for new items and via two manual tasks.

**Architecture:** Pure rule mapping (`LanguageRules`) + original-language resolution (item field, TMDB fallback) + an `OriginalLanguageService` that sets/clears per-item `PreferredMetadataLanguage`/`PreferredMetadataCountryCode` and queues a replace-metadata refresh. A hosted `NewItemWatcher` and two `IScheduledTask`s drive the service.

**Tech Stack:** C# / .NET 10, Jellyfin.Controller + Jellyfin.Model 12.2.0 (NuGet, compile-only), xUnit + Moq, built inside `mcr.microsoft.com/dotnet/sdk:10.0` via Docker.

**Spec:** `docs/superpowers/specs/2026-10-17-original-language-metadata-design.md`

## Global Constraints

- Target `net10.0`; Jellyfin.Controller/Jellyfin.Model `12.2.0` with `<ExcludeAssets>runtime</ExcludeAssets>`; `targetAbi` `12.2.0.0`.
- Plugin name "Original Language Metadata", GUID `a37f7b61-f4ad-42ae-87ee-97775b676123`, assembly/namespace `Jellyfin.Plugin.OriginalLanguage`, version `1.0.0.0`.
- Rules: `sv` → language `sv`, country `SE`; `fi` → language `fi`, country `FI`. Toggles `EnableSwedish`, `EnableFinnish`, both default `true`.
- Only `Movie` and `Series` items (non-virtual). No library filtering.
- Refresh options: `MetadataRefreshMode.FullRefresh`, `ImageRefreshMode = MetadataRefreshMode.Default`, `ReplaceAllMetadata = true`, `ReplaceAllImages = false`, `ForceSave = true`, `RemoveOldMetadata = true`, `IsAutomated = false`; queued via `IProviderManager.QueueRefresh(id, options, RefreshPriority.Normal)`.
- No .NET SDK on host: every build/test command runs via `./build.sh` (Docker).
- Commits: no AI co-author trailers.

## Review Focus

1. Item with `OriginalLanguage` empty and no TMDB ID → skipped silently (no exception, no refresh).
2. TMDB lookup fails (HTTP 404/429/timeout, malformed JSON) → that item is skipped with a warning; the task continues.
3. Original language given as `"swe"`/`"SV"`/`"fin"` (OMDb/three-letter/case) → still matches.
4. Watcher must not loop: our own `UpdateItemAsync` + refresh fire `ItemUpdated` again; an item is acted on at most once.
5. Task cancellation mid-run → stops promptly via the token, no partial-item corruption.

---

### Task 1: Project scaffold + build script

**Files:**
- Create: `Jellyfin.Plugin.OriginalLanguage.sln`, `Jellyfin.Plugin.OriginalLanguage/Jellyfin.Plugin.OriginalLanguage.csproj`, `tests/Jellyfin.Plugin.OriginalLanguage.Tests/Jellyfin.Plugin.OriginalLanguage.Tests.csproj`, `build.sh`, `.gitignore`, `Directory.Build.props` (Nullable enable, TreatWarningsAsErrors false)

- [ ] Step 1: `build.sh` with subcommands `test` (`dotnet test`) and `package` (`dotnet publish -c Release` → `dist/Original Language Metadata_1.0.0.0/` containing the plugin DLL + `meta.json`, and a zip). Runs `docker run --rm -v "$PWD":/src -w /src -v ~/.nuget-docker:/root/.nuget mcr.microsoft.com/dotnet/sdk:10.0 ...`, with the files owned by the host user afterwards (`--user $(id -u):$(id -g)` with `HOME=/tmp`).
- [ ] Step 2: an empty test project referencing the plugin project, xunit, Moq.
- [ ] Step 3: `./build.sh test` → builds, 0 tests. Commit.

### Task 2: Configuration + LanguageRules

**Files:** Create `Configuration/PluginConfiguration.cs`, `LanguageRules.cs`; Test `tests/.../LanguageRulesTests.cs`

**Produces:**
- `PluginConfiguration : BasePluginConfiguration { bool EnableSwedish = true; bool EnableFinnish = true; }`
- `record LanguageTarget(string Language, string Country)`
- `static LanguageTarget? LanguageRules.GetTarget(string? originalLanguage, PluginConfiguration config)`

- [ ] Step 1: tests:
  - `GetTarget("sv", all on)` == `("sv","SE")`; `"fi"` == `("fi","FI")`
  - `"SV"`, `"swe"`, `" sv "` → `("sv","SE")`; `"fin"`, `"FI"` → `("fi","FI")`
  - `"en"`, `"ja"`, `null`, `""` → `null`
  - `EnableSwedish=false` → `"sv"` gives `null`, `"fi"` still matches; `EnableFinnish=false` symmetric
- [ ] Step 2: run, fail. Step 3: implement (trim, lower-invariant, map 3-letter `swe`/`fin`). Step 4: pass. Commit.

### Task 3: TMDB original-language lookup

**Files:** Create `Tmdb/TmdbOriginalLanguageClient.cs`, `Tmdb/TmdbApiKeyProvider.cs`; Test `tests/.../TmdbOriginalLanguageClientTests.cs`

**Produces:**
- `interface ITmdbApiKeyProvider { string? GetApiKey(); }`, `TmdbApiKeyProvider` (reflection: type `MediaBrowser.Providers.Plugins.Tmdb.Plugin, MediaBrowser.Providers` → static `Instance` → `Configuration` → `TmdbApiKey`; if empty, const field `ApiKey` on `MediaBrowser.Providers.Plugins.Tmdb.TmdbUtils`; result cached; `null` + one warning log if neither resolves)
- `interface ITmdbOriginalLanguageClient { Task<string?> GetOriginalLanguageAsync(BaseItemKind kind, string tmdbId, CancellationToken ct); }`
- `TmdbOriginalLanguageClient(IHttpClientFactory, ITmdbApiKeyProvider, ILogger<>)` — GET `https://api.themoviedb.org/3/{movie|tv}/{id}?api_key={key}`, returns `original_language`; any non-success status, exception (except cancellation), or missing field → `null` + warning log.

- [ ] Step 1: tests with a fake `HttpMessageHandler`:
  - Movie id `"550"` → request URI path `/3/movie/550`, query contains `api_key=k`; body `{"original_language":"sv"}` → `"sv"`
  - Series → path `/3/tv/…`
  - 404 → `null`; body `not json` → `null`; key provider returns `null` → `null` and no HTTP call
- [ ] Step 2–4: fail, implement, pass. Commit.

### Task 4: OriginalLanguageService

**Files:** Create `OriginalLanguageService.cs`; Test `tests/.../OriginalLanguageServiceTests.cs`

**Consumes:** Task 2, Task 3. **Produces:**
- `enum ApplyResult { Changed, AlreadySet, NotMatching, Unknown }`
- `interface IOriginalLanguageService` exposing the four members below, implemented by `OriginalLanguageService`
- `Task<string?> ResolveOriginalLanguageAsync(BaseItem item, CancellationToken ct)` — `item.OriginalLanguage` if non-empty, else TMDB via `item.GetProviderId(MetadataProvider.Tmdb)` (null if none)
- `Task<ApplyResult> ApplyAsync(BaseItem item, CancellationToken ct)` — target from rules with `Plugin.Instance.Configuration` (inject a `Func<PluginConfiguration>` for testability); if both fields already equal target → `AlreadySet`; else set both, `ILibraryManager.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, ct)`, queue refresh (Global Constraints options) → `Changed`
- `Task<bool> ResetAsync(BaseItem item, CancellationToken ct)` — if both fields empty → `false`; else set both `null`, update, queue refresh → `true`
- `IReadOnlyList<BaseItem> GetAllMoviesAndSeries()` — `GetItemList(new InternalItemsQuery { IncludeItemTypes = [Movie, Series], Recursive = true, IsVirtualItem = false })`

- [ ] Step 1: tests (Moq `ILibraryManager`, `IProviderManager`, `ITmdbOriginalLanguageClient`; items are `new Movie {…}` / `new Series {…}`):
  - Movie `OriginalLanguage="sv"` → `Changed`, fields `sv`/`SE`, `UpdateItemAsync` once, `QueueRefresh` once with `ReplaceAllMetadata && !ReplaceAllImages`; TMDB client not called
  - Movie already `sv`/`SE` → `AlreadySet`, no update/refresh
  - `OriginalLanguage` empty + Tmdb id `"123"`, client → `"fi"` → `Changed` `fi`/`FI`; client called with `(BaseItemKind.Series, "123")` for a Series
  - empty language, no Tmdb id → `Unknown`, client not called
  - `"en"` → `NotMatching`, no update
  - `ResetAsync` on `sv`/`SE` → `true`, both fields null, refresh queued; on empty → `false`, nothing called
- [ ] Step 2–4: fail, implement, pass. Commit.

### Task 5: Scheduled tasks

**Files:** Create `ScheduledTasks/ApplyRulesTask.cs`, `ScheduledTasks/ResetToEnglishTask.cs`; Test `tests/.../ScheduledTaskTests.cs`

**Consumes:** Task 4 (inject `OriginalLanguageService`; make its used methods `virtual` or extract `IOriginalLanguageService` — use the interface).

- Apply: Name "Apply original-language rules to all media", Key `OriginalLanguageApplyRules`; Reset: Name "Reset all media to English", Key `OriginalLanguageResetToEnglish`; Category "Original Language Metadata"; `GetDefaultTriggers()` → empty (manual only).
- Loop over `GetAllMoviesAndSeries()`, `ct.ThrowIfCancellationRequested()` per item, report `progress` 0–100, catch+log per-item exceptions and continue, log a summary (counts per result). Apply waits 50 ms after each TMDB-backed lookup (only when `item.OriginalLanguage` was empty) to stay under TMDB rate limits.

- [ ] Step 1: tests: Apply over 3 items calls `ApplyAsync` 3×, reports progress ending at 100; an item throwing doesn't stop the rest; cancelled token → `OperationCanceledException`. Reset calls `ResetAsync` for each.
- [ ] Step 2–4: fail, implement, pass. Commit.

### Task 6: New-item watcher

**Files:** Create `NewItemWatcher.cs` (`IHostedService`); Test `tests/.../NewItemWatcherTests.cs`

- `StartAsync` subscribes `ItemAdded`/`ItemUpdated`; `StopAsync` unsubscribes.
- `ItemAdded` with `Movie`/`Series` (non-virtual) → add id to a `ConcurrentDictionary<Guid, byte>` pending set.
- `ItemUpdated` with `UpdateReason.HasFlag(MetadataDownload)` and id removed from pending (`TryRemove` → guarantees once) → fire-and-forget `ApplyAsync` (Task.Run, exceptions logged). Our own update uses `MetadataEdit`, and the id is gone from pending, so no loop.
- Expose `internal void OnItemAdded(object?, ItemChangeEventArgs)` / `OnItemUpdated` for tests (`InternalsVisibleTo`).

- [ ] Step 1: tests: added + MetadataDownload update → `ApplyAsync` once; second MetadataDownload update → still once; update without prior add → never; added + `MetadataImport`-only update → not yet; Episode added → ignored.
- [ ] Step 2–4: fail, implement, pass. Commit.

### Task 7: Plugin entry, DI, settings page, packaging

**Files:** Create `Plugin.cs`, `PluginServiceRegistrator.cs`, `Configuration/configPage.html` (EmbeddedResource), `meta.json`, `README.md`

- `Plugin : BasePlugin<PluginConfiguration>, IHasWebPages` with static `Instance`, Name/Guid per Global Constraints, one page `Name = "OriginalLanguage"`, `EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"`.
- Registrator: `AddSingleton<ITmdbApiKeyProvider, TmdbApiKeyProvider>`, `AddSingleton<ITmdbOriginalLanguageClient, TmdbOriginalLanguageClient>`, `AddSingleton<IOriginalLanguageService, OriginalLanguageService>`, `AddHostedService<NewItemWatcher>`. (Scheduled tasks are discovered automatically.)
- Config page: standard Jellyfin plugin page (`data-role="page"`, `ApiClient.getPluginConfiguration/updatePluginConfiguration`) with two checkboxes "Use Swedish metadata for Swedish-original titles" and "Use Finnish metadata for Finnish-original titles", plus a note that turning one off doesn't revert items, and pointers to the two tasks.
- README: what it does, build (`./build.sh test`, `./build.sh package`), deploy (copy `dist/Original Language Metadata_1.0.0.0` to the server's `plugins/` folder and restart Jellyfin).

- [ ] Step 1: `./build.sh test` all green; `./build.sh package` produces folder + zip. Commit.

### Task 8: Deploy + end-to-end check on the owner's server

- [ ] Step 1: copy package to the server's plugins dir, `docker restart Jellyfin`; logs show plugin loaded, no errors; plugin listed Active in `/Plugins`.
- [ ] Step 2: run "Apply original-language rules to all media"; verify with read-only sqlite on `jellyfin.db`: a known Swedish (sv) and a known Finnish (fi) movie have `PreferredMetadataLanguage` sv/fi + country SE/FI; an English movie has none; after the refresh queue drains, the Swedish movie's Overview is Swedish.
- [ ] Step 3: log summary counts look sane (≈10+ sv, ≈3+ fi, rest NotMatching/Unknown, few failures).
- [ ] Step 4: Reset task is NOT run on production unless the owner asks (it would undo step 2); its behaviour is covered by unit tests.
