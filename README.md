# HolsterEverything (SPT 4.0.13)

HolsterEverything is a server-side SPT mod that expands what can be equipped in the PMC holster slot.

## Compatibility
- Built and tested on `SPT 4.0.13`
- Other versions may work, but are not guaranteed

## What This Mod Changes
- Adds template ID `5422acb9af1c889c16000029` to the PMC holster slot filter (`55d729d84bdc2de3098b456b`)
- Applies the change automatically when the SPT server starts
- Safe to load multiple times: the whitelist entry is only added once (no duplicate entry is written)

## Installation
1. Download the latest release `.7z` file.
2. Extract the `.7z` directly into your SPT installation folder.

You can also extract first, then drag-and-drop the extracted `SPT` folder into your SPT installation folder.

## Verify It Loaded
Start the SPT server and check for a `HolsterEverything:` log line confirming the patch ran.

## Behavior After Removing The Mod
- If you uninstall the mod while a non-default weapon is already in your holster, that weapon can remain there in your existing profile.
- After you unequip that weapon, you will not be able to equip it back into the holster slot without the mod enabled again.

## Uninstall
Delete the folder:

`SPT/user/mods/HolsterEverything`
