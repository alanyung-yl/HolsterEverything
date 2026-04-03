# Optional F12 Client Plugin

This folder contains an optional BepInEx client plugin that syncs F12 toggles to:

`<SPT root>/SPT/user/mods/HolsterEverything/config.json`

The server mod reads that file when `SPT.Server.exe` starts.

## Important
- This plugin is optional. The server mod works without it.
- If this plugin is not installed, users can edit `<SPT root>/SPT/user/mods/HolsterEverything/config.json` manually.
- If `config.json` is missing, the server mod creates a default one on startup.
- After changing values in F12, restart `SPT.Server.exe` so server-side config is re-read.

## Build
From this folder:

```powershell
dotnet build .\Plugin.csproj -c Release -p:SPTRoot="C:\Path\To\Your\SPT"
```

If `SPTRoot` is valid, the project auto-copies the built plugin to:

`SPT/BepInEx/plugins/HolsterEverything/HolsterEverything.dll`

## Manual install (optional)
If auto-copy is not used, copy built `HolsterEverything.dll` to:

`SPT/BepInEx/plugins/HolsterEverything/HolsterEverything.dll`

## Runtime behavior
- `EnableAllWeapons` toggle maps to server `EnableAllWeapons`
- Category toggles map directly to `EnabledWeaponCategoryNames` in server `config.json`
- `Pistol` and `Revolver` are intentionally excluded from F12 toggles
- `EnabledWeaponCategoryIds` stays empty unless edited manually

