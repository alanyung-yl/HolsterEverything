# BepInEx F12 Configuration Manager Client Plugin

This folder contains the BepInEx F12 Configuration Manager client plugin that:

- syncs weapon category toggles to:

`<SPT root>/SPT/user/mods/HolsterEverything/config.json`

- provides the client-side holster size restriction settings
- provides the client-side holster handling penalty settings

The server mod reads that file when `SPT.Server.exe` starts.

## Important
- If `config.json` is missing, the server mod creates a default one on startup.
- After changing weapon category values in BepInEx F12 Configuration Manager, restart `SPT.Server.exe` so server-side config is re-read.
- Holster size and holster handling settings apply immediately and do not require a server restart.

## Build
From this folder:

```powershell
dotnet build .\Plugin.csproj -c Release -p:SPTRoot="C:\Path\To\Your\SPT"
```

If `SPTRoot` is valid, the project auto-copies the built plugin to:

`SPT/BepInEx/plugins/HolsterEverything/HolsterEverything.dll`

## Manual install
If auto-copy is not used, copy built `HolsterEverything.dll` to:

`SPT/BepInEx/plugins/HolsterEverything/HolsterEverything.dll`

## Runtime behavior
- `Enable All Weapons` maps to server `EnableAllWeapons`
- Category toggles map directly to `EnabledWeaponCategoryNames` in server `config.json`
- Holster size options are client-side only and only affect holster drag/drop:
  - `Enable Size Limit` blocks oversized weapons from being dropped into the holster slot
  - `Limit Additional Weapons Only` defaults to `true` and keeps the size limit off for vanilla holster weapons
  - `Use Unfolded Size` makes folded weapons use their unfolded size for the holster size check
- Holster handling options are client-side only and affect the weapon in hand:
  - `Enable Handling Penalty` subtracts ergonomics while an eligible weapon is in holster
  - `Limit Additional Weapons Only` defaults to `true` and keeps the handling penalty on additional holster-enabled categories only
  - `Handling Ergo Penalty` controls how much ergonomics is removed
  - `Include Non-Foldable Weapons` lets non-foldable holstered weapons also trigger the penalty
- Vanilla holster weapons only trigger the handling penalty when they have an installed stock attachment
- `Pistol` and `Revolver` are intentionally excluded from BepInEx F12 Configuration Manager toggles
- `EnabledWeaponCategoryIds` stays empty unless edited manually
