# Original Language Metadata (Jellyfin plugin)

Keeps your libraries in English, except for titles that are originally Swedish or Finnish:
those get Swedish (`sv`/`SE`) or Finnish (`fi`/`FI`) metadata instead.

- **Detection:** the item's original language. Jellyfin 12 stores it on the item
  (`OriginalLanguage`). When that's empty, as it is for items scanned before 12.x, the
  plugin asks TMDB using the item's TMDB ID and the same API key as Jellyfin's built-in
  TMDb plugin.
- **Mechanism:** sets the item's own *Preferred metadata language* and *country* (the same
  fields as in the item metadata editor). Then it queues a refresh that replaces metadata but
  keeps images. Seasons and episodes inherit the language from their series.
- **New items:** handled automatically after their first metadata download. Existing items
  are never changed automatically.
- **Settings** (Dashboard → Plugins → Original Language Metadata): Swedish and Finnish each
  have an on/off switch. Turning one off only stops future changes.
- **Manual tasks** (Dashboard → Scheduled Tasks → Original Language Metadata):
  - *Apply original-language rules to all media*: switches every matching movie and series.
  - *Reset all media to English*: clears the per-item language on every movie and series
    so the library default applies again.

Both tasks overwrite whatever per-item language is already set.

## Build

There's no .NET SDK on the host. Everything runs in `mcr.microsoft.com/dotnet/sdk:10.0`:

```sh
./build.sh test      # unit tests
./build.sh package   # dist/Original Language Metadata_1.0.0.0/ + zip
```

Targets Jellyfin 12.2 (`net10.0`, Jellyfin.Controller 12.2.0).

## Install

Copy `dist/Original Language Metadata_1.0.0.0/` into your Jellyfin `plugins/` directory
(`/config/plugins` in the official Docker image) and restart Jellyfin.
