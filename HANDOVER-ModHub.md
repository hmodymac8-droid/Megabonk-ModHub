# Mod Hub — handover (paste this into a new chat)

You're helping AhmedHus21 develop **Mod Hub**, an in-game mod manager for **Megabonk** (BepInEx 6 IL2CPP).
It opens with F4 (or L3 + R3 on a controller). Full page with everything: https://claude.ai/artifact/WRGtYt639U9zTjm24ePGVG ("Mod Hub Workbench").

## Where it stands (23 Oct 2026)
- **1.2.2 is released** on Nexus (https://www.nexusmods.com/megabonk/mods/194; both files: "ModHub" and "ModHub All-in-One (includes BepInEx)"; virus scan "Safe to use") and on GitHub (https://github.com/hmodymac8-droid/ModHub/releases/tag/v1.2.2).
- Mod Hub **updates itself since 1.2.1** from GitHub releases (asset `ModHub-x.y.z.zip`). Players on 1.2.0 or older must download once from Nexus.
- 1.2.2 changes: the newer of Thunderstore and Nexus wins for update badges; when Nexus is ahead, the Hub asks Thunderstore's single-package API so it can still install in game; re-checks every 15 min while the game is open and on opening the Hub if older than 5 min; "Restart game" restarts through Steam (no stuck "Stopping").
- Not tested yet: the Restart fallback when the game wasn't started from Steam; the 15-minute re-check in a long session.
- Released DLL SHA-256 starts 9120C1D7; All-in-One zip SHA-256 starts fe5baf4a.

## Rules (from the user)
- Never release without the user's OK. Every release goes to **Nexus (both files) and a GitHub release**.
- Nexus "What's new" must always keep "Mod Hub updates itself (since 1.2.1)" plus how older versions get there.
- Start the game via Steam (steam://rungameid/3405340); close it only with the in-game EXIT button, never kill it.
- When the user is at the PC, don't send scripted clicks/keys; ask them to drive and only take screenshots.
- No cmd.exe / ping / PowerShell helpers in released mods (Nexus quarantined 1.0.1). No zip inside the All-in-One.
- BonkWithFriends must stay 0.4.1 in the user's game (0.5.2 + BonkWithFriendsFixes crashes).
- Thunderstore rejected the Hub itself (20 Oct 2026); the user's other mods (Quest Tracker, Megabonk Arabic) are on Thunderstore as team AhmedHus21.
- Credits on every README/Nexus page: special thanks to **sensei** (friend and teacher) first, then pinkpomelo, BepInEx team, Il2CppInterop, Harmony by Andreas Pardeike (pardeike); Melon Bridge: MelonLoader API by LavaGang, Json.NET by James Newton-King (MIT).
- All UI text is English + Arabic through `L.T(english, arabic)`.

## Where things are (on the user's PC)
- Source: `C:\Users\hmody\Documents\Megabonk Mods\ModHub\src` (build: `.\build.ps1`, `.\build.ps1 -Install` with the game closed). Version is in `ModHub.csproj` and `Plugin.Version`.
- Game: `C:\Program Files (x86)\Steam\steamapps\common\Megabonk` (Hub in `BepInEx\plugins\ModHub`, state in `BepInEx\config\ModHub.state.json`, removed mods in `BepInEx\_hub_removed`).
- Release zips: `ModHub\dist`; Nexus descriptions: `ModHub\desc_122.txt` (live); Melon Bridge: `Megabonk Mods\MelonBridge` and `MELON_BRIDGE_PLAN.md`; installer: `ModHub\Installer` (Nexus review of the exe still pending).
- Test switches: `BepInEx\config\ModHub.updatefeed` (replace GitHub latest release), `ModHub.storefeed` (replace Thunderstore's list with a file/URL).
- Claude Code memory for this project: `C:\Users\hmody\.claude\projects\C--Users-hmody-Documents-Megabonk-Mods\memory` (megabonk-mod-hub.md has the long history).

## Code map
Plugin.cs (entry, Restart, Online.Tick) · Panel.cs (window, buttons via Btn) · Catalog.cs (installed mods, ON/OFF by .dll.off, swaps) · Online.cs (Thunderstore, Nexus GraphQL game 7674, GitHub self-update, Updates.Refresh) · Installer.cs · Details/Images/WebP.cs (pictures; Nexus WebP via Windows WIC) · Profiles.cs (MODHUB- share codes) · Safety.cs (crash safety, auto-revert) · Melon.cs (Melon Bridge side) · Pad/Nav.cs (controller) · KeyConflicts.cs · Characters.cs · Theme/Rtl/RtlShaper.cs (look + Arabic).

## Release steps
Bump version → build → test in game → check zips (ModHub.dll first entry, hash = tested DLL, nothing nested) → desc_1xx.txt (EN+AR, self-update line, credits) → `gh release create vX.Y.Z -R hmodymac8-droid/ModHub <both zips>` → Nexus Files tab in the user's Chrome: Add file input → "Update existing file" → pick the file → Archive existing → version → "Update mod version" → changelog → Save file (33 MB All-in-One in 4 × 9 MB parts joined in the page, SHA-256 checked) → description via SCEditor → wait for "Safe to use".

## Version history
1.2.2 (23 Oct) update detection + Steam restart · 1.2.1 (23 Oct) self-update · 1.2.0 (22 Oct) controller, Cancel install · 1.1.1 (22 Oct) Cancel update / Back to, auto-rollback · 1.1.0 (21 Oct) Melon Bridge · 1.0.5 (21 Oct) redesign, Open key · 1.0.3 / 1.0.2 / 1.0.0 (20 Oct).

## Ideas / next
Mark Melon packages more clearly in Get mods; long refusal reasons cut off in toasts; Steam Deck floating keyboard for search; more ideas in memory "megabonk-mod-ideas".
