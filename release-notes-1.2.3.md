# Mod Hub 1.2.3

Custom characters and skins are now first-class in Mod Hub.

- **Install them like mods.** Pick a character or skin in **Get mods** and press Install. It is placed in `BepInEx\plugins\CustomCharacters\<Name>\`, appears in **Installed mods** with a `Character` tag, and can be switched **ON/OFF** or **Remove**d like any other mod. The drop folder and **Found in your Downloads** work too.
- **Packs are checked before installing.** A broken json, or a pack whose 3D model file is missing, is refused with the reason instead of breaking the game's loader (one bad pack used to stop every character from loading).
- **CustomCharacterCreator comes with them.** Mod Hub installs the newest version (1.4.1, Shadowth117's fix) automatically and replaces an old/broken 1.4.0. The old 1.4.0 does not work with the current game: with it, no character loads at all.
- **It says what is wrong, in red:** missing loader, loader switched off, or loader failing on this game version — each character row tells you which, and the character's page says which loader version is required.
- Vanilla mode now switches characters off too.

Known: characters made with Unity's Standard shader (instead of the Megabonk shader) spam `_GoochDarkColor` / `_IndirectFade` errors in the log. Cosmetic — the character still works; re-export with the Megabonk shader to silence it.

**Which file do I download?**
- `ModHub-1.2.3.zip` — you already have BepInEx. Extract into your Megabonk folder.
- `ModHub-1.2.3-AllInOne-with-BepInEx.zip` — first time modding: includes BepInEx. Extract into your Megabonk folder (winhttp.dll and BepInEx must sit next to Megabonk.exe).
- `Mod Hub Installer 1.2.3.exe` — finds the game by itself and installs the All-in-One.
