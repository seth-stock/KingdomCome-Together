# The Multiplayer tab (Kingdom Come: Together)

Written 2026-10-06. Unofficial community mod; not affiliated with or endorsed by Warhorse Studios or PLAION. Companion to the first game's tab ("Kingdom Come: Deliver Us", its `docs/MENU.md` and `docs/FEATURE-PARITY.md`, which lists what the two games share).

Once the mod is installed, the game's **main menu** has a **Multiplayer** entry (after *Credits*). It opens pages built in the game's own menu style:

```
Multiplayer
  Status                       what the mod is doing and which options are set
  Co-op sync: running/stopped  start or stop the sync (mp_start / mp_stop)
  Game world                   Saving > copy of this world, save now (host), world saves shared/separate, host autosave minutes, DLC quests shared
                               Ask the host for their world · On a first join: bring my Henry / a new Henry · Start over in this host's world
                               Send my Henry home · Send him home when I leave
  Story: join or stay          ask me / always join / always stay · the host's cutscene: watch / keep playing
  Session rules                friendly fire · leash · quest sharing
  Keys                         the keys the mod uses and where to change them
```

Every button calls the mod's own `mp_*` command with a value, so nothing new is stored: what a page shows is read from the mod's own state, and the console commands
keep working exactly as before. Host and join stay in the launcher (it starts the relay and the agent).

## How it works (and why no game files are changed)

KCD2's menu is one compiled Scaleform element, `Menu`, with a small generic API that the game's own pages are built with (`PreparePage`, `AddBasicButton`, `ShowPage`, `ClearAll`,
`GetValue`, ... declared in `Libs/UI/UIElements/Menu.xml`) and an `OnButton` event. Lua may call it (`UIAction.CallFunction`, `UIAction.RegisterElementListener`), so the tab is
pure Lua in `Scripts/Startup/kdcmp_menu.lua`, inside the mod's pak. No Modding Tools are needed by anyone who only plays, and nothing is made from the player's game files.
(The first game's menu is data, so its tab is built from the player's own files at install time; see its MENU.md.)

Things learned live that the code depends on:

* **No Lua timer runs in the main menu.** The tab is driven by the menu's own events: the game plays its "change focus" sound (`OnPlayAudio`) whenever the cursor moves, and the tab uses that to
  put its button on the root page (the game rebuilds the root page every time it comes back) and to draw its page.
* **`GetValue(id)` answers the text `-1.000000` for a button that is not on the page**, and NaN or a number for one that is. That is how the tab tells the root page from any other.
* **A page that the game did not open cannot be left again.** So the root button makes the game open its own *Help* page (by injecting `menu_accept` with `Menu.SetInput` while *Help* is selected),
  and the tab's page is drawn over it. The game's own *Back* then returns to the root page by itself.
* **Settings changes** re-draw the page with the new value and keep the cursor on the same button.

## What is verified

Seen live in the game's main menu (Modding Tools build 1.5.5): the root entry appears and comes back after the root page was rebuilt; it opens the page; every page draws with titles, values and
tooltips; *Back* goes up one page of the tab and then to the game's root page; **On a first join** toggled `mp_join_henry` between *auto* and *fresh* and back (seen in the mod's log), and the label followed.

**Not seen:** the tab inside the *pause* menu of a running game (the same `Menu` element, but the root page is composed differently: it may need its own anchor); the other buttons in a session with a partner
(they call commands that were each seen on their own: `mp_henry_home`, `mp_world_copy`, `mp_join_request`, `mp_story_join`, `mp_scene_mode`, `mp_quest_dlc`...); controller input; other languages (the labels are English).

Pressing a button with no world loaded (for example *Keep a copy of this world* in the main menu) does nothing: the commands check for a world themselves.
