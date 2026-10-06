-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
--
-- The in-game "Multiplayer" tab (docs/MENU.md). The game's menu is one Scaleform element ("Menu") with a small generic API (PreparePage, AddBasicButton,
-- ShowPage, ClearAll, SelectButton, GetValue ... declared in Libs/UI/UIElements/Menu.xml) that the game's own pages are built with, and an OnButton event.
-- Lua may call that API, so nothing of the game's files is changed: this file adds a button to the root page and builds its own pages the way the game does.
--
--   * GetValue(id) answers -1 for a button that is not on the current page: that is how the tab knows the ROOT page is showing (it has Settings and HelpOverlays,
--     no other page has both) and whether its own button is already there. The game rebuilds the root page every time it comes back to it, so a short timer keeps the button on.
--   * a press of one of our buttons arrives as OnButton(id) and is handled here.
-- Every setting here is the mod's own console command (mp_*) called with a value: nothing new is stored, and what a page shows is read from the mod's own state.
KCD2MP = KCD2MP or {}
local M = KCD2MP.menu or {}
KCD2MP.menu = M
M.version = 1
M.pageName = M.pageName or nil          -- the page of ours that is showing (nil: none)
M.shadow = M.shadow or {}               -- values the mod does not let us read back (the default, until the tab changes it)

-- Our button on the root page borrows the id of a game button that is NOT on the root page (the game opens that button's page whenever it hears the id).
-- So a press opens a game page ([root, Controls] on the game's own page stack) and we draw our page over it a moment later; the game's own Back then
-- pops to the root page by itself and rebuilds it. (Drawing over a page that the game did not open leaves the game unable to go back: seen.)
local ROOT_BTN = "MP_Root"           -- the game has never heard of this id, so a press does nothing by itself: the tab then makes the game open its Help page (below)
local ROOT_BTN_ID = ROOT_BTN
local HOST_PAGE_BTN = "HelpOverlays"     -- a button of the root page whose page has the root page as its parent: the game's own Back returns to the root

local function log(s) System.LogAlways("[KCD2-MP] MENU " .. tostring(s)) end

local function menu(fn, ...)
    local ok, r = pcall(UIAction.CallFunction, "Menu", -1, fn, ...)
    if not ok then log("call " .. fn .. " failed: " .. tostring(r)); return nil end
    return r
end

-- GetValue answers "-1.000000" for an id that is not on the page, and something else (NaN for a plain button) for one that is
local function exists(id)
    local ok, v = pcall(UIAction.CallFunction, "Menu", -1, "GetValue", id, 0)
    if not ok then return false end
    local s = tostring(v)                  -- the call hands back TEXT: "-1.000000" for an id that is not there, "-nan(ind)" or a number for one that is
    return s ~= "-1.000000" and s ~= "-1" and s ~= "nil"
end

local function say(text)
    if KCD2MP_ShowInteractionMsg then pcall(KCD2MP_ShowInteractionMsg, text) end
    log(text)
end

local function get(fn, default) local ok, v = pcall(fn); if ok and v ~= nil then return v end return default end
local function onoff(v) return v and "on" or "off" end

local function cmd(line) pcall(System.ExecuteCommand, line) end
local function lua_call(name, arg) local f = _G[name]; if type(f) ~= "function" then log(name .. " is not in this build"); return false end local ok, e = pcall(f, arg); if not ok then log(name .. ": " .. tostring(e)) end return ok end

-- ================================================================ pages
-- a page is a function returning its title and its items. an item: { id, label, tip, page = "name" | act = function() | info = true }

local PAGES = {}
local PARENT = { status = "main", world = "main", story = "main", rules = "main", keys = "main", saving = "world" }

local function cycle(list, cur)
    for i, v in ipairs(list) do if v == cur then return list[(i % #list) + 1] end end
    return list[1]
end

PAGES.main = function()
    local running = get(function() return KCD2MP.running end, false)
    return "Multiplayer", {
        { id = "MP_status", label = "Status", tip = "What the mod is doing and which options are set", page = "status" },
        { id = "MP_sync", label = "Co-op sync: " .. (running and "running" or "stopped"), tip = "Start or stop the sync with the other players (mp_start / mp_stop)",
          act = function() if KCD2MP.running then lua_call("KCD2MP_Stop") else lua_call("KCD2MP_Start") end end },
        { id = "MP_world", label = "Game world", tip = "Join the host's world, which Henry you are, send him home, save rules", page = "world" },
        { id = "MP_story", label = "Story: join or stay", tip = "What to do when the host's story goes on rails; the host's cutscenes", page = "story" },
        { id = "MP_rules", label = "Session rules", tip = "Friendly fire, the leash, quest sharing, crimes, loot", page = "rules" },
        { id = "MP_keys", label = "Keys", tip = "The keys the mod uses and where to change them", page = "keys" },
    }
end

PAGES.status = function()
    local w122, w124, w153, w114, w137, w121 = KCD2MP.w122 or {}, KCD2MP.w124 or {}, KCD2MP.w153 or {}, KCD2MP.w114 or {}, KCD2MP.w137 or {}, KCD2MP.w121 or {}
    local ghosts = 0
    for _ in pairs(KCD2MP.ghosts or {}) do ghosts = ghosts + 1 end
    local henry = w124.henry
    local items = {
        { id = "MP_i1", label = "Co-op sync: " .. (KCD2MP.running and "running" or "stopped"), info = true },
        { id = "MP_i2", label = "Other players in view: " .. ghosts, info = true },
        { id = "MP_i3", label = "World saves: " .. (w122.sharedWorld == false and "separate" or "shared with the host"), info = true },
        { id = "MP_i4", label = "Host autosave: " .. tostring(w122.autosaveMinutes or "?") .. " min", info = true },
        { id = "MP_i5", label = "On a first join: " .. (henry == "fresh" and "a new Henry" or henry == "auto" and "bring my Henry" or tostring(henry or "bring my Henry")), info = true },
        { id = "MP_i6", label = "Host's story: " .. (w153.joinPref == "join" and "I always join" or w153.joinPref == "free" and "I always stay free" or "ask me"), info = true },
        { id = "MP_i7", label = "Friendly fire: " .. onoff(w121.ffSession ~= false), info = true },
        { id = "MP_i8", label = "Leash: " .. onoff(w114.leash ~= false) .. "  Quest sharing: " .. onoff(w137.sync ~= false), info = true },
    }
    return "Status", items
end

local AUTOSAVE = { 0, 1, 2, 5, 10, 15, 30 }

PAGES.world = function()
    local w122, w124 = KCD2MP.w122 or {}, KCD2MP.w124 or {}
    local henry = w124.henry
    local home = M.shadow.homeOnLeave == true
    local dlc = M.shadow.dlc ~= false
    return "Game world", {
        { id = "MP_saving", label = "Saving", tip = "A copy of this world, world saves, autosave and DLC sharing", page = "saving" },
        { id = "MP_req", label = "Ask the host for their world", tip = "Join: the host's world (even a 100-hour one) comes to you and loads",
          act = function() lua_call("KCD2MP_JoinRequest") end },
        { id = "MP_henry", label = "On a first join: " .. (henry == "fresh" and "a new Henry" or "bring my Henry"),
          tip = "Bring my Henry: your character goes into the host's world. A new Henry: you start fresh from a new game's first save",
          act = function() lua_call("KCD2MP_SetJoinHenry", henry == "fresh" and "auto" or "fresh") end },
        { id = "MP_reset", label = "Start over in this host's world", tip = "Delete the character stored for this host's world; the next join asks again",
          act = function() lua_call("KCD2MP_Wo125Reset") end },
        { id = "MP_home", label = "Send my Henry home", tip = "Back into your own world, as a new manual save, with what he gained",
          act = function() lua_call("KCD2MP_Wo157Home", "") end },
        { id = "MP_homeleave", label = "Send him home when I leave: " .. onoff(home), tip = "When you leave the host's world, send your Henry home first",
          act = function() M.shadow.homeOnLeave = not home; lua_call("KCD2MP_Wo157Cfg", home and "off" or "on") end },
    }
end

PAGES.saving = function()
    local w122 = KCD2MP.w122 or {}
    local dlc = M.shadow.dlc ~= false
    return "Saving", {
        { id = "MP_copy", label = "Keep a copy of this world", tip = "Save a copy of the shared world, with your character, as your own save to carry on from separately",
          act = function() lua_call("KCD2MP_Wo157Copy", "") end },
        { id = "MP_save", label = "Save the world now (host)", tip = "The host writes a world save now",
          act = function() lua_call("KCD2MP_WorldSaveNow") end },
        { id = "MP_shared", label = "World saves: " .. (w122.sharedWorld == false and "separate" or "shared with the host"), tip = "Shared: only the host saves in a session. Separate: every machine saves as before",
          act = function() lua_call("KCD2MP_SetSharedWorld", (w122.sharedWorld == false) and "on" or "off") end },
        { id = "MP_auto", label = "Host autosave: " .. tostring(w122.autosaveMinutes or 5) .. " min", tip = "How often the host's world is saved (0 = never)",
          act = function() lua_call("KCD2MP_SetAutosaveMinutes", tostring(cycle(AUTOSAVE, tonumber(w122.autosaveMinutes) or 5))) end },
        { id = "MP_dlc", label = "DLC quests shared: " .. onoff(dlc), tip = "Share DLC quests like every other quest",
          act = function() M.shadow.dlc = not dlc; lua_call("KCD2MP_Wo157Dlc", dlc and "off" or "on") end },
    }
end

PAGES.story = function()
    local w153 = KCD2MP.w153 or {}
    local pref = w153.joinPref or "ask"
    local mode = w153.mode or "watch"
    local txt = { ask = "ask me each time", join = "always join the host", free = "always stay in the open world" }
    return "Story: join or stay", {
        { id = "MP_pref", label = "When the host's story locks in: " .. (txt[pref] or pref), tip = "F11 joins the host, F12 stays in the open world",
          act = function() lua_call("KCD2MP_SetStoryJoin", cycle({ "ask", "join", "free" }, pref)) end },
        { id = "MP_scene", label = "The host's cutscene: " .. (mode == "play" and "keep playing" or "watch it with them"), tip = "Watch: your own copy of the scene plays. Keep playing: it is skipped",
          act = function() lua_call("KCD2MP_SetSceneMode", mode == "play" and "watch" or "play") end },
    }
end

PAGES.rules = function()
    local w114, w137, w121 = KCD2MP.w114 or {}, KCD2MP.w137 or {}, KCD2MP.w121 or {}
    local ff, leash, qs = w121.ffSession ~= false, w114.leash ~= false, w137.sync ~= false
    return "Session rules", {
        { id = "MP_ff", label = "Friendly fire: " .. onoff(ff), tip = "Players can hurt each other (the host's value is the session's)",
          act = function() lua_call("KCD2MP_Wo121SetFriendlyFire", ff and "off" or "on") end },
        { id = "MP_leash", label = "Leash: " .. onoff(leash), tip = "Keep the joiner near the host (host only)",
          act = function() lua_call("KCD2MP_SetLeash", leash and "off" or "on") end },
        { id = "MP_qs", label = "Quest sharing: " .. onoff(qs), tip = "All quests are shared in the host's world (the host's value is the session's)",
          act = function() lua_call("KCD2MP_SetQuestSync", qs and "off" or "on") end },
    }
end

PAGES.keys = function()
    return "Keys", {
        { id = "MP_k1", label = "Join the host's story: F11", info = true },
        { id = "MP_k2", label = "Stay in the open world: F12", info = true },
        { id = "MP_k3", label = "Dice: the game's own Keybinds list", info = true },
        { id = "MP_k4", label = "Change them in Settings > Keybinds", tip = "The mod's actions are in the game's own list of key bindings", info = true },
    }
end

-- ================================================================ drawing and handling

function M.show(name, selectId)
    local page = PAGES[name]
    if not page then return end
    local ok, title, items = pcall(page)
    if not ok then log("page " .. name .. ": " .. tostring(title)); return end
    M.pageName = name
    M.items = {}
    menu("ClearAll")
    menu("PreparePage", 1500, 325, 8, title, 196)
    for _, it in ipairs(items) do
        M.items[it.id] = it
        menu("AddBasicButton", it.id, 0, it.label, it.tip or "", it.info == true)
    end
    -- the top page uses the game's own Back (it hears the press and pops its page stack to the root); a sub page is ours alone
    menu("AddBasicButton", PARENT[name] and "MP_back" or "Back", 1, "Back", "", false)
    menu("ShowPage")
    if selectId then menu("SelectButton", selectId, 0) end
end

function M.back()
    local parent = PARENT[M.pageName or ""]
    if parent then M.show(parent) end
end

function M.onButton(self, element, instance, event, args)
    local id = type(args) == "table" and (args[0] or args.Id or args.id or args[1]) or nil
    if id == ROOT_BTN and not M.pageName and exists("HelpOverlays") then      -- our button on the root page: the game opens its page, we draw ours over it next
        M.pending = "main"; M.pendingTries = 0
        -- a press of the game's own Help button, made by the tab: the game opens (and later closes) a page of its own, and ours is drawn over it
        menu("SelectButton", HOST_PAGE_BTN, 0)
        menu("SetInput", "menu_accept", 1)
        menu("SetInput", "menu_accept", 0)
        return
    end
    if M.pageName and id == "MP_back" then M.back(); return end
    if not M.pageName or type(id) ~= "string" or id:sub(1, 3) ~= "MP_" then return end
    local it = M.items and M.items[id]
    if not it then return end
    if it.page then M.show(it.page); return end
    if it.act then
        local ok, e = pcall(it.act)
        if not ok then log(id .. ": " .. tostring(e)) end
        M.show(M.pageName, id)          -- the page again, with the new value, the same button selected
    end
end

-- the menu was closed (into the game, or a new game): nothing of ours is showing any more
function M.onHide() M.pageName = nil; M.pending = nil end

-- No script timer runs in the main menu (nothing of the world is running), so the tab is driven by the menu's own events. The game plays its
-- "change focus" sound (OnPlayAudio) every time the cursor moves, and by then the page it moved on is fully built: that is the moment to look at it.
--   * the root page is rebuilt by the game every time it comes back, so the tab's button is put on it at the first cursor move;
--   * a press on the tab's button opens a game page; the tab's page is drawn at the next cursor move or sound, over the page the game just built.
function M.look()
    local root = exists("Settings") and exists("HelpOverlays")
    if root then
        M.pageName = nil
        if M.pending then
            M.pendingTries = (M.pendingTries or 0) + 1
            if M.pendingTries > 6 then M.pending = nil end        -- the game never opened the page: give up
        end
        if not exists(ROOT_BTN_ID) then
            menu("AddBasicButton", ROOT_BTN_ID, 0, "Multiplayer", "Host, join, the game world, keys and every option", false)
            menu("ShowPage")           -- a button added to a shown page is only laid out by this (seen)
        end
    elseif M.pending then
        local p = M.pending
        M.pending = nil
        M.show(p)
    end
end

function M.onAudio(self, el, inst, ev, args) M.look() end

function M.start()
    if M.started then return end
    M.started = true
    local ok, e = pcall(UIAction.RegisterElementListener, M, "Menu", -1, "OnButton", "onButton")
    pcall(UIAction.RegisterElementListener, M, "Menu", -1, "OnHide", "onHide")
    log("tab " .. M.version .. " listener " .. tostring(ok) .. " " .. tostring(e))
    pcall(UIAction.RegisterElementListener, M, "Menu", -1, "OnPlayAudio", "onAudio")
    pcall(UIAction.RegisterElementListener, M, "Menu", -1, "OnShow", "onAudio")
end

-- Registering on the Menu element while the game is still starting up ends the game (seen): start a while after the mod loads. KCD2MP_MenuStart() starts it at once
-- (the agent calls it when it first reaches the game).
function KCD2MP_MenuStart() M.start() end
local ok_t = pcall(Script.SetTimer, 15000, function() pcall(M.start) end)
if not ok_t then log("the tab will start when the agent asks (KCD2MP_MenuStart)") end
