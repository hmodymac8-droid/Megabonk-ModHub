MOD HUB - one home for all your Megabonk mods
=============================================

Press F4 in game (main menu is best). Esc or F4 closes it.
"Open key: F4" (top right): click it, then press any key to use that key instead.
"Restart game" (top right, click twice) closes the game and starts it again by itself right away.

CONTROLLER (Xbox, PlayStation, Steam Deck)
  - Press L3 + R3 (both sticks in) together to open or close Mod Hub.
  - D-pad or left stick moves the gold frame, A presses, B goes back (or closes), LB / RB switch tabs,
    right stick scrolls. The arrow keys and Enter work too. Moving the mouse hides the frame.

INSTALLED MODS
  - Every mod you have (BepInEx and MelonLoader), with its picture, version and status.
  - ON / OFF switch for each mod (takes effect after a restart).
  - Update / Update all: Thunderstore mods update right inside the game. Nexus-only mods open their page.
  - Remove: moves the mod to BepInEx\_hub_removed (nothing is deleted). "Empty removed" clears that folder.
  - Changed your mind? "Cancel install" right after installing a mod, "Cancel update" right after updating one,
    or "Back to" the previous version later. No restart needed to cancel.
  - Click a mod's name to read what it does.
  - Red "errors in the log" note = that mod is reporting problems.

GET MODS
  - Browse Thunderstore (one-click Install) or Nexus (opens the page: click "Manual download").
  - Search, sort by Popular / Newest / Updated, pages with Previous / Next.
  - Click any mod to see its pictures and full description.
  - "Found in your Downloads": mod zips you downloaded recently, with Install and Remove (Recycle Bin).
  - Drop folder: put a .zip / .dll (or a custom character's files) into the
    "Mod Hub - drop mods here" folder in the game folder. Open Mod Hub and it installs by itself.

PROFILES & FRIENDS
  - Profiles: save which mods are on (e.g. "Co-op", "Solo") and switch in one click.
  - Vanilla: turn every mod off, then bring exactly the same mods back.
  - Copy my mod code / Paste a friend's code: share your mod list; your friend installs the missing ones in one click.

UPDATES
  - When a new version is out, "Update Mod Hub" downloads and installs it inside the game (from
    https://github.com/hmodymac8-droid/ModHub/releases, the same files as the Nexus page). Restart to use it.
  - A Mod Hub zip downloaded from Nexus also works: Mod Hub offers "Install Mod Hub x.x.x" in one click.

SAFETY
  - If the game fails to start after you add mods, Mod Hub offers to turn the new ones off.
    If it fails twice in a row, it turns them off by itself and tells you.
  - Only mods made for Megabonk are installed (BepInEx 6 IL2CPP, or MelonLoader through Melon Bridge).

MELONLOADER MODS (Melon Bridge)
  - Mod Hub comes with Melon Bridge, which runs MelonLoader mods inside BepInEx. You don't install MelonLoader.
  - Install them like any mod (Thunderstore, Downloads or the drop folder). They show a "Melon" tag in the list.
  - Before installing, Mod Hub checks the mod: one that won't work in this game version is not installed, and
    you see why. The check is done again after every game update.
  - A Melon mod that keeps throwing errors is paused for that session; the rest of the game keeps running.
  - Melon mods live in BepInEx\melon-mods. Their settings stay in the game's UserData folder, like with MelonLoader.
  - "Melon Bridge" appears in your list too: switching it off switches the Melon mods off.

Settings: BepInEx\config\modhub.megabonk.cfg (open key, language: Auto / English / Arabic)
Uninstall: delete BepInEx\plugins\ModHub

CREDITS
Special thanks to sensei, my friend and teacher.
pinkpomelo, BepInEx team (mod loader), Il2CppInterop (IL2CPP bridge), Harmony by Andreas Pardeike (pardeike).
LavaGang / MelonLoader for the API Melon Bridge mirrors. Json.NET by James Newton-King (MIT).
Mod lists and downloads: Thunderstore and Nexus Mods.
