# Mod Hub — notes for Claude

In-game mod manager for **Megabonk** (BepInEx 6, IL2CPP, C# net6.0). F4 opens it. Players get it from Nexus
(mod 194); release zips live on this repo's GitHub releases, which the Hub self-updates from.

## Can't build here
The project references the game's BepInEx/IL2CPP assemblies, which are only on the author's PC. In a cloud
session, **don't try to `dotnet build`** — read carefully instead, and say what the author should test in game.
Local build: `dotnet build -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Megabonk"`.
`build.ps1` = build + zips in `dist\` (also needs `..\MelonBridge`). `build-thunderstore.ps1` = Thunderstore edition.

## Rules (from README — always follow)
- Every player-facing string: `L.T(english, arabic)` (in `Plugin.cs`). Both languages, always.
- Never delete a player's files. Removed mods / old versions go to `BepInEx\_hub_removed` (`Catalog.RemovedDir`).
- Offline must not break the Hub: no Thunderstore/Nexus → installed list still works, no crash.
- No release without the author's OK. A release = the two zips, on GitHub **and** Nexus.
- Keep the credits in the readme/Nexus page.
- Version lives in **two** places: `ModHub.csproj` `<Version>` and `Plugin.Version` in `src/Plugin.cs`.
- Never commit build output, zips, dlls, or `backup-*` / `github-releases` / `nexus-review` folders.

## Map of `src/`
| File | Contents |
|---|---|
| `Plugin.cs` | Entry point (`Plugin`, `HubBehaviour`), config, `L.T` translation |
| `Panel.cs` | Main UI window; `CursorGuard`, `InputGuard`. `Panel` is a partial class also spread over `Nav.cs`, `Details.cs`, `Theme.cs`, `HistoryPage.cs`, `WhatsNewPage.cs`, `Profiles.cs` |
| `Online.cs` | Thunderstore + Nexus (GraphQL, game 7674) lists, downloads, self-update; `Updates` (update checks), `LocalFiles` (Downloads / drop folder) |
| `Catalog.cs` | Installed mods: on/off, uninstall, vanilla mode, sources/versions; state in `BepInEx\config\ModHub.state.json` |
| `Installer.cs` | Installing a mod from a zip/package |
| `Characters.cs` | Custom characters/skins (id `character:<folder>`), CustomCharacterCreator |
| `Profiles.cs` | Profiles UI + `ShareCode` |
| `History.cs` | Activity log → `ModHub.history.json` (capped, background writes) |
| `Safety.cs` | Crash safety: detects a failed game start and offers to turn off the mods added since the last good start |
| `Rtl.cs`, `RtlShaper.cs` | Arabic right-to-left + letter shaping |
| `Melon.cs` | MelonLoader mods via Melon Bridge |
| `Images.cs`, `WebP.cs` | Image loading/cache, WebP decode; `Readmes` |
| `WhatsNew.cs` | "What's new" data (`ModHub.whatsnew.json`) |
| `KeyConflicts.cs`, `ModInfo.cs`, `Pad.cs` | Hotkey conflicts, mod metadata, controller input |
| `Edition.cs` | `THUNDERSTORE` build flag: no Nexus, no self-update |

Other folders: `Installer/` = standalone "Mod Hub Installer.exe" (WinForms). `package/` = texts copied into the
release zips. `thunderstore/` = Thunderstore readme/changelog/icon. Open tasks are in `TODO.md`.

## Working style
- The author prefers plain, simple language. Keep answers short; skip long explanations unless asked.
- Read only the files the task needs — use this map instead of exploring.
- Game code is IL2CPP: watch for Il2CppInterop types, and keep per-frame UI code allocation-free.
