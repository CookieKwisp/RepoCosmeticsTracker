# R.E.P.O. Cosmetic Tracker

A zero-click collection tracker for R.E.P.O.'s cosmetics. Open it and it just
works — no AssetRipper, no exports, no manual imports.

## What it does

- **Builds its own catalog** by reading the cosmetics list (names, slots,
  rarities) straight out of the game's installed Unity data files with
  AssetsTools.NET. It re-scans automatically only when the game files have
  actually changed since the last build.
- **Syncs ownership from your save.** MetaSave.es3 is decrypted in place
  (ES3 AES scheme) and its `cosmeticUnlocks` list is applied to the grid. A
  file watcher (event-driven, no polling) re-syncs the moment the game writes
  a save — unlock something in-game and its card grays out on its own, with a
  chime.
- **One click per cosmetic.** Cards are colored by rarity (Common / Uncommon /
  Rare / Ultra Rare); clicking toggles owned, which fades the card back and
  saves instantly to `catalog.json` next to the exe. Save-sync is additive —
  it never un-checks things you marked by hand.
- **Filters**: search, rarity chips, category dropdown (Hat, Eyewear, Left
  Leg, …), and a "Hide owned" toggle.
- **Feel**: hover/press/check animations, procedurally synthesized UI sounds
  (mutable via the 🔊 toggle), dark native title bar.

## Running

```
dotnet run --project RepoCosmeticTracker
```
Or download from [Here](https://github.com/CookieKwisp/RepoCosmeticsTracker/releases/download/v1.0/RepoCosmeticTracker.exe) then run *RepoCosmeticTracker.exe*


Requires .NET 8, Windows, and R.E.P.O. installed via Steam (run the game once
so a save exists). First launch downloads a small Unity type database
(`classdata.tpk`) used to parse the game files, then builds the catalog,
after that, startup is instant from the cache.

The **↻ Rescan game files** button in the status bar forces a full catalog
rebuild (normally unnecessary game updates are detected automatically).

## How the pieces fit

| File | Role |
| --- | --- |
| `RepoCosmeticTracker/Services/DirectAssetReader.cs` | Reads MetaManager's `cosmeticAssets` from level0–2 via AssetsTools.NET |
| `RepoCosmeticTracker/Services/Es3Crypto.cs` / `SaveService.cs` | ES3 save decryption + `cosmeticUnlocks` extraction |
| `RepoCosmeticTracker/Services/SaveWatcher.cs` | Debounced FileSystemWatcher over the save folder |
| `RepoCosmeticTracker/Services/CosmeticIconIndex.cs` | Matches catalog entries to the game's cached icon PNGs |
| `RepoCosmeticTracker/Services/SoundService.cs` | In-memory synthesized WAV click/chime sounds |
| `RepoCosmeticTracker/Services/GameLocator.cs` | Finds the Steam install + save folder via registry/manifests |
| `RepoCosmeticTracker/MainWindow.xaml(.cs)` | The single-screen card grid UI |
