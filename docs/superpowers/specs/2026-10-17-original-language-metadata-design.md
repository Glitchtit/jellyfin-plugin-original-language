# Original Language Metadata — Jellyfin plugin design

Date: 2026-10-17
Status: draft, awaiting approval

## Intent

The owner's Jellyfin server has its
libraries' metadata language set to English, so every item gets English
titles, overviews, etc. The owner wants Swedish- and Finnish-original media to
be the exception: when TMDB says a movie or show's original language is
Swedish (or Finnish), that item should get its metadata in that language
instead. Everything else stays English.

## Goals

- When a movie or series is added, if its TMDB original language is an
  enabled language, set the item's per-item metadata language/country and
  refresh its metadata so it arrives in that language.
- Supported languages: **Swedish** (`sv` → language `sv`, country `SE`) and
  **Finnish** (`fi` → language `fi`, country `FI`). Each has its own on/off
  toggle on the plugin's settings page. Both on by default.
- Two manually-run Scheduled Tasks:
  1. **"Apply original-language rules to all media"** — goes over every movie
     and series in the server, and for each one whose TMDB original language
     is an enabled language, sets the per-item language/country accordingly
     and refreshes it.
  2. **"Reset all media to English"** — goes over every movie and series and,
     for each one that has a per-item metadata language and/or country set,
     **clears** both fields (so the library default, English, applies again)
     and refreshes it. Items with nothing set are skipped — no refresh. TMDB
     ID is irrelevant for this task. (Owner decided: clear, not stamp `en`.)
- Series: the setting is applied on the series item; seasons and episodes
  must end up with metadata in the same language (Jellyfin resolves an
  episode's preferred language through its parent series — the builder
  verifies this and makes the refresh cover children).

## Non-goals

- **No notion of "manual choices".** The owner explicitly ruled this out. The
  plugin keeps no record of what it changed. The tasks do exactly what their
  names say and overwrite whatever per-item language is there.
- Turning a language off does **not** revert anything. It only stops future
  automatic changes for that language.
- No languages beyond Swedish and Finnish (no generic "any language" list).
- No changes to library-level settings.
- Audiobooks, music, anime-specific providers (AniDB/AniList etc.): not
  targeted. Only Movie and Series items that have a TMDB ID.

## Anti-goals (would count as failure even if it "works")

- Flipping non-Swedish/Finnish items away from English.
- Hammering TMDB or the server: a full-library run must be throttled/sequential
  and must not lock up Jellyfin.
- Breaking existing metadata (images, provider IDs, user data) during refresh.

## Constraints / environment facts (verified 2026-10-17)

- Jellyfin **12.2.0**, runtime **net10.0** (Docker `jellyfin/jellyfin`).
- Plugin dir: `/config/plugins` in the container. Deploy = copy a `<Name>_<version>/` folder
  there and restart the container.
- No .NET SDK on the dev workstation; build with the
  `mcr.microsoft.com/dotnet/sdk:10.0` Docker image (don't install the SDK
  system-wide).
- **Original language is probably not stored by Jellyfin.** BaseItem has
  `OriginalTitle` but no original-language field that I know of. Expect the
  plugin to look it up from TMDB by the item's `Tmdb` provider ID. Prefer
  reusing Jellyfin's bundled TMDb client/API key if it's reachable from a
  plugin; otherwise call the TMDB API directly with the same API key Jellyfin
  uses, or a key set in plugin config (builder's call, but avoid requiring the
  owner to register a key if at all possible). Cache lookups per run.

## Decided (by the owner)

- Detection = TMDB original language.
- Swedish and Finnish, each toggleable.
- Automatic on newly added items.
- Two manual tasks as described above.
- No tracking of manual vs. plugin changes.

## My calls (Claude's, flagged for owner review)

- Mechanism: set the item's built-in `PreferredMetadataLanguage` /
  `PreferredMetadataCountryCode` (the same fields as "Preferred metadata
  language" in the item metadata editor), then queue a full metadata refresh
  that replaces existing metadata. Because the setting is stored on the item,
  later normal refreshes keep the language.
- Finland-Swedish productions (original language `sv`, country Finland) get
  `sv` / `SE`. The country only affects certification/release-date selection.
- Items without a TMDB ID are skipped.
- Project name: `jellyfin-plugin-original-language`, plugin display name
  "Original Language Metadata". Repo at
  `~/GIT/jellyfin-plugin-original-language`.

## Left to the builder

- Exact event hook for "newly added" (e.g. `ILibraryManager.ItemAdded` vs.
  a post-scan task), making sure the refresh happens after the TMDB ID is
  known, without causing a refresh loop.
- How children (seasons/episodes) are refreshed.
- Throttling strategy and progress reporting for the tasks.
- Settings page implementation (standard plugin config page).
- Tests: unit tests for the language-rule mapping; a manual end-to-end check
  on the owner's server with one known Swedish film, one Finnish series, and one English
  title.

## More of my calls (from the reviewer check)

- Refresh mode: replace all metadata (text) so the new language actually
  lands, but **do not replace images**, and honour Jellyfin's per-item
  locked fields (Jellyfin does this by default). Unlocked hand-edited text on
  touched items may be overwritten — acceptable given "no manual choices".
- Apply task skips items already in the target state (language and country
  already match) — no refresh for those.
- No per-library filtering. Anime library items are Movie/Series too; Apply
  can't flip them (original language Japanese), and Reset only touches items
  that have a per-item language set.
