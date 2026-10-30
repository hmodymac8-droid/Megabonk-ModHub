# Mod Hub — TODO (task list for a coding agent)

Source code is **not** in this repo (this repo only holds release zips). The source lives at:

```
C:\Users\hmody\Documents\Megabonk Mods\ModHub\
├─ src\              (the mod, C# / net6.0, BepInEx 6 IL2CPP plugin)
├─ ModHub.csproj     (version is here)
├─ build.ps1         (build + make the release zips in .\dist)
└─ HANDOVER-ModHub.md (rules the author cares about — read it first)
```

Build: `.\build.ps1` (or `dotnet build -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Megabonk"`).
All player-facing text goes through `L.T(english, arabic)` — both languages, always.
Never release without the author's OK.

---

## 1. Activity history — a simple log of everything Mod Hub did

**Why.** Players forget what the Hub changed, and support questions are always "what did it install / remove?".
There is no readable record today; only `BepInEx\LogOutput.log`, which is developer noise.

**What to build.** A plain-language history of Mod Hub's own actions, newest first.

Record at least:
- installed a mod (name, version, source: Thunderstore / Nexus / Downloads / drop folder)
- updated a mod (from → to, and whether the old copy was kept)
- removed a mod (and that it went to `BepInEx\_hub_removed`)
- switched a mod or character ON/OFF
- installed / updated / removed a custom character or skin
- installed or replaced CustomCharacterCreator (which version, why)
- vanilla mode entered / left
- profile applied / saved

**How.**
- Store in `BepInEx\config\ModHub.history.json` — an array of `{ TimeUtc, Kind, Name, From, To, Source, Detail }`. Append-only, cap ~500 entries (drop the oldest), write on a background thread so the game never hitches.
- One small static class, e.g. `History.cs`, with `Add(kind, name, from, to, source, detail)` and `Entries`. Call it from `Installer.Install`, `Catalog.Uninstall`, `Catalog.Set`, `Characters.InstallCharacterZip`, `Characters.SetEnabled`, `Catalog.EnterVanilla/LeaveVanilla`, `Profiles` apply, and the loader install in `Online.InstallFromStore`.
- UI: a **History** button on the Installed mods tab (or a fourth tab). Scrollable list, grouped by day ("Today", "Yesterday", "29 Oct"), each line one simple sentence in the player's language:
  - "Installed Quest Tracker 1.3.2 from Thunderstore"
  - "Updated Melon Bridge 1.0.0 → 1.0.1 (old copy kept)"
  - "Removed RunSaver (moved to BepInEx\_hub_removed)"
  - "Installed the character GodOfDeath (needs CustomCharacterCreator 1.4.1)"
  - "Turned OFF Shiori Novella"
- A **Copy** button that copies the visible history as text (users paste it when asking for help).
- Keep it out of the way: no toast, no popup, no extra file writes per frame.

**Done when.** Every action above shows up with a timestamp after a restart, both languages, capped size, and the Hub keeps its normal speed.

---

## 2. Update tracking for custom characters

**Why.** Mods get an "Update x.y.z" button; characters never do, even when the author published a newer version on Thunderstore/Nexus. Players think characters are abandoned.

**What to build.**
- When a character/skin is installed, remember where it came from and its version, the same way mods do (`Catalog.SetSource` / `PackageVersions` in `ModHub.state.json`). For a character the "id" is already `character:<folder>`.
- In `Updates.Refresh`, stop skipping characters (`if (m.IsCharacter) continue;` must go away) and compare the stored version with the store version:
  - Thunderstore: newest package with the same name (`Online.Store`, already parsed).
  - Nexus: the Nexus list the Hub already loads (`Online.Nexus`, graphql game 7674) — newer of the two wins, exactly like mods.
- The character row then offers the same **Update x.y.z** button. Pressing it downloads the new zip and replaces the character folder, keeping the old one in `_hub_removed` so "Back to old version" works.
- Characters with no known source (dropped in by hand, or from a Nexus manual download the Hub did not install) simply never show an update — no guessing.

**Watch out for.**
- Do not touch the `CustomCharacters` parent folder: replace only `CustomCharacters\<name>\`.
- A character renamed by its author (folder name differs from json name) must still match by its json `characterName`.
- Keep the existing "needs CustomCharacterCreator 1.4.1" logic working; an update must not reinstall an old loader.

**Done when.** Install a character from Thunderstore, publish a newer version of that package, reopen the Hub → the row shows "Update", pressing it updates the character, keeps the old copy, and the character still loads after a restart.

---

## 3. Validate character/skin packs more deeply

**Why.** One broken pack makes the game's loader throw, and then **no** character loads — the player blames the Hub ("nothing shows up").
`Characters.Validate` today checks: json parses, has `character`/`skin`/`soloSkin`/`weapon`, and the 3D model file it names is inside the zip. That misses data problems.

**What to add.**
- Skin packs: if the json names a base character (`eCharacter` / `characterName`) that is neither a known game character nor an installed custom one, refuse with a reason ("this skin is for X, which is not installed").
- Required fields the loader reads: `characterName` / `skinName`, `prefabPath`, `iconPath` — refuse if missing or empty.
- `assetBundleName` pointing at a file that exists but is 0 bytes.
- Any `.custom.json` that duplicates another installed character's name (two packs claiming the same character).

**Done when.** A pack that would crash the loader is refused with one clear sentence, and a good pack still installs.

---

## 4. Warn about characters that use non-game shaders

**Why.** Characters exported with Unity's **Standard** shader instead of the game's `MegabonkShader` spam
`Material 'X' with Shader 'Standard' doesn't have a color property '_GoochDarkColor'` hundreds of times, and look flat.
It is cosmetic, but it looks like the Hub broke something.

**What to build.** Cheap detection: after a session, `Safety.CountErrors`/log scan looks for `_GoochDarkColor` / `_IndirectFade` errors mentioning a material name; the character row gets a grey note: *"This character uses non-game shaders — it may look flat or pale"*, plus one line in the details/About if needed. Never make it red (not an error the player can fix) and never refuse the install.

**Done when.** Shiori Novella's row shows the note after a restart, and the note never appears for normal characters.

---

## 5. Build hygiene: backup folders end up inside the DLL

**Why.** `ModHub.csproj` compiles every `**/*.cs` under the project, including `backup-*\` folders. A backup copy
(`backup-1.2.2-before-whatsnew`) silently compiles its `.cs` files into `ModHub.dll`.

**What to do.** In `ModHub.csproj`, exclude backups and any scratch folder:

```xml
<Compile Remove="backup-*\**" />
<Compile Remove="dist\**" />
<Compile Remove="obj\**" />
```

and verify the built DLL no longer contains types coming from backup folders (or just move backups outside the project, as `Megabonk-backups\` already does).

**Done when.** Deleting/renaming a `backup-*` folder does not change the built DLL, and `build.ps1` still produces both zips.

---

## Ideas parked (not scheduled)

- **Error log viewer** (removed in 1.2.3 — left-click button that opens the log lines for one mod). Only bring it back if the author asks; if it returns it must be a normal left-click button, not a right-click target.
- Character rows currently count in "N mods found"; a separate "N characters" count would read better.
- History could offer "undo" for the last remove (move it back out of `_hub_removed`).
