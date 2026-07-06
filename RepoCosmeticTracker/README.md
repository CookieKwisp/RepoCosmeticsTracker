# REPO Cosmetic Tracker

A WPF (.NET 8) app for tracking which cosmetics you own in R.E.P.O.,
fully automated: click **Refresh Catalog (1-click)** and it reads
everything directly from your installed game — real names, categories,
rarities, and which ones you own — with no AssetRipper, no manual export,
no BepInEx. Just the game's own files.

## How to use it

1. Build and run (see below).
2. Click **Detect** (finds your REPO install and save folder).
3. Click **Refresh Catalog (1-click)**.

First run downloads a small one-time data file (`classdata.tpk`, needed to
decode Unity's file format) automatically. After that, it reads your
game's `level0`/`level1`/`level2` files directly to find `MetaManager`'s
`cosmeticAssets` list (which is the real, authoritative ordering the game
itself uses), resolves every entry to get its name/category/rarity, then
decrypts `MetaSave.es3` to mark which ones you own. Everything gets saved
to `catalog.json`.

Re-run **Refresh Catalog** any time — e.g. after unlocking new cosmetics,
or after a game update changes the cosmetics list.

## How the pieces fit together

- **`Services/GameLocator.cs`** — finds your Steam install, REPO's folders,
  and your save folder, all without hardcoded paths.
- **`Services/Es3Crypto.cs`** — decrypts `.es3` save files. R.E.P.O. uses
  Unity's "Easy Save 3" with AES-128-CBC; salt doubles as IV, key is
  PBKDF2-HMAC-SHA1(password, salt, 100 iterations, 16 bytes). Verified
  against a known-working reference implementation.
- **`Services/DirectAssetReader.cs`** — the core of the 1-click flow. Uses
  `AssetsTools.NET` + `AssetsTools.NET.MonoCecil` to read REPO's raw Unity
  data files directly: finds the `MetaManager` MonoBehaviour (identified by
  having a populated `cosmeticAssets` field), reads its list in the game's
  own real order, and resolves each entry to a `CosmeticAsset`'s actual
  name/type/rarity.
- **`Services/SaveService.cs`** — reads `cosmeticUnlocks` (a plain
  `List<int>` of unlocked indices) out of the decrypted `MetaSave.es3`.

## Legacy/fallback tools

The **2. Scan Game Assembly**, **4. Find in Export**, and the
AssetRipper-based import buttons on the Checklist tab are all still here
as a fallback path — they're how this was originally figured out, working
entirely from an AssetRipper export instead of reading the game directly.
Not needed for normal use anymore, but useful if `DirectAssetReader` ever
breaks against a future REPO update (e.g. if the level file MetaManager
lives in changes, or a field gets renamed) — that manual path can still
get you a working catalog while a fix gets sorted out.

## Building & running

Requires the .NET 8 SDK and Windows (WPF is Windows-only).

```powershell
cd RepoCosmeticTracker
dotnet build
dotnet run
```

## Safety / scope notes

- Everything runs locally — no network calls except the one-time
  `classdata.tpk` download, nothing uploaded anywhere.
- This only *reads* your save file and game files; it never writes back
  to REPO's own data.
- This is for personal cosmetics tracking, not multiplayer manipulation —
  it doesn't touch anything that would affect other players or an active
  session.

