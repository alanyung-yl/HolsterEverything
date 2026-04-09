# Optional BepInEx F12 Configuration Manager Client Plugin

This folder contains an optional BepInEx F12 Configuration Manager client plugin that:

- syncs weapon category toggles to:

`<SPT root>/SPT/user/mods/HolsterEverything/config.json`

- provides the client-side holster size restriction settings

The server mod reads that file when `SPT.Server.exe` starts.

## Important
- This plugin is optional. The server mod works without it.
- If this plugin is not installed, users can edit `<SPT root>/SPT/user/mods/HolsterEverything/config.json` manually.
- If `config.json` is missing, the server mod creates a default one on startup.
- After changing weapon category values in BepInEx F12 Configuration Manager, restart `SPT.Server.exe` so server-side config is re-read.
- Holster size settings apply immediately and do not require a server restart.

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
- Holster size toggles are client-side only and are applied before a weapon can be dropped into the holster slot
- `Pistol` and `Revolver` are intentionally excluded from BepInEx F12 Configuration Manager toggles
- `EnabledWeaponCategoryIds` stays empty unless edited manually
