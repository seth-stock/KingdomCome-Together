-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-153 synthetic test: a partner in a cutscene, the Lua half, against the real kdcmp.lua under MoonSharp
-- (engine stubbed, fake clock; the stubs are Test-WO140Synthetic.lua's).
--   D  defaults (notice ON, bring-along OFF), the seven console commands, the settings line, the metres parser
--   N  the notice: shown on the host's scene start, the countdown, the end, the own-copy case, the switch, the 20 s timeout
--   K  the keys: only F11 means watch; the generic accept actions never move anyone; precedence over the sleep prompt
--   R  the agent's answer to a watch: every code has words, 'watching' clears on a refusal
--   S  the status line, and the words are the project's own
-- What this proves: the Lua half. The agent's rules are Wo153Tests (dotnet). Live evidence: docs/WO-153-findings.md.


NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}; LOCKS = {}; MSGS = {}; RAYS = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
local function d2(a, b) return (a.x - b.x) ^ 2 + (a.y - b.y) ^ 2 + (a.z - b.z) ^ 2 end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
System.GetCVar = function() return "0" end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntity = function(id) for _, e in pairs(ENTS) do if e.id == id then return e end end return nil end
System.GetEntitiesInSphere = function(p, r)
    local o = {}
    for _, e in pairs(ENTS) do
        if e.GetWorldPos and d2(e:GetWorldPos(), p) <= r * r then o[#o + 1] = e end
    end
    table.sort(o, function(a, b) return a.id < b.id end)
    return o
end
System.GetEntitiesByClass = function(cls)
    local o = {}
    for _, e in pairs(ENTS) do if e.class == cls then o[#o + 1] = e end end
    table.sort(o, function(a, b) return a.id < b.id end)
    return o
end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.RemoveEntity = function(eid) for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil end end end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Terrain = mkstub()
Game.AddSaveLock = function(name) LOCKS[name] = true; return true end
Game.RemoveSaveLock = function(name) LOCKS[name] = nil; return true end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end,
             GetWorldTimeRatio = function() return 15 end, SetWorldTimeRatio = function() end }
Framework = { IsValidWUID = function(w) return w ~= nil and w ~= 0 and w ~= "0" end }
EntityModule = mkstub()
OWNERS = {}          -- inventory id -> owner wuid
EntityModule.GetInventoryOwner = function(inv) return OWNERS[inv] end
ent_all = 287

-- The engine's typed message tables (XGenAIModule.MakeTableFromType, dumped live on 1.5.5).
local TYPES = {
    ["switch:stimulus:theft"] = function() return { kettleType = 0, method = 0, owner = 0, treatAsPersonalSource = false, pivot = 0, count = 0,
        shouldCheckHomeStashes = false, freshlyAttributedCrime = false, isNonAttributed = false,
        information = { position = { x = 0, y = 0, z = 0 }, perceivedWuid = 0, label = "" }, immediate = true } end,
    ["switch:stimulus:escalatedTrespass"] = function() return { wuidType = 0, home = 0, trespassingRepeatedly = false, trespassArea = 0,
        isKzikTrespass = false, createInformationOnly = false, stimulusKind = 0 } end,
    ["switch:stimulus:disturbance"] = function() return { perceivedWuid = 0, priceOverride = -1, skipInitialReaction = false } end,
    ["crime:attackInitiatedByConcept"] = function() return { target = 0, priorityTarget = false } end,
    ["stopFight"] = function() return { soulCount = 0, messageId = "" } end,
}
XGenAIModule = mkstub()
XGenAIModule.MakeTableFromType = function(t) local f = TYPES[t]; return f and f() or nil end
XGenAIModule.SendMessageToEntityData = function(to, kind, t) MSGS[#MSGS + 1] = { to = to, kind = kind, t = t } end
WUIDS = {}           -- wuid -> entity
XGenAIModule.GetEntityByWUID = function(w) return WUIDS[w] end
enum_crime_theftMethod = { unknown = 0, loot = 1, lootCorpse = 2, lootUnconsciousBody = 3, kettleEating = 4, pick = 5, pickpocket = 6, seenEquipped = 7 }
enum_crime_resolutionKind = { fine = 0, leaveUnconscious = 1, punishment = 2, questPunishment = 3, skillCheck = 4, fight = 5, secondArrest = 6 }
enum_crime_stimulusKind = { trespass = 40, escalatedTrespass = 13, theft = 38 }
enum_crime_trespassInformationWuid = { none = 0, home = 1, homeArea = 2 }

-- The ray: RAYS[npcName] = { dist = m, entity = e } (the first hit), nil = nothing hit.
Physics = mkstub()
Physics.RayWorldIntersection = function(from, dir, n, types, skip)
    for name, r in pairs(RAYS) do
        local e = ENTS[name]
        if e and e.id == skip then return r.hit and { { dist = r.dist, entity = r.entity } } or {} end
    end
    return {}
end

-- ---- items and inventories (as in Test-WO134Synthetic.lua) ----
ITEMS = {}
NEXTWUID = 100
local function newItem(cls, hp, amt)
    NEXTWUID = NEXTWUID + 1
    local w = "wuid" .. NEXTWUID
    ITEMS[w] = { class = cls, amount = amt or 1, health = hp or 1, id = w }
    return w
end
ItemManager = mkstub()
ItemManager.GetItem = function(w) return ITEMS[w] end
local function mkInventory(id)
    local inv = { list = {}, invId = id or "inv" }
    inv.GetInventoryTable = function(self) local t = {}; for i, w in ipairs(self.list) do t[i] = w end; return t end
    inv.CreateItem = function(self, cls, hp, amt) local w = newItem(cls, hp, amt); self.list[#self.list + 1] = w; return true end
    inv.AddItem = function(self, w)
        for n, e in pairs(ENTS) do if e.class == "PickableItem" and e.item and e.item.GetId() == w then ENTS[n] = nil end end
        self.list[#self.list + 1] = w
    end
    inv.RemoveItem = function(self, w) for i, x in ipairs(self.list) do if x == w then table.remove(self.list, i); return w end end end
    inv.GetCountOfClass = function(self, cls) local n = 0; for _, w in ipairs(self.list) do if ITEMS[w] and ITEMS[w].class == cls then n = n + ITEMS[w].amount end end; return n end
    inv.GetId = function(self) return self.invId end
    inv.GetMoney = function(self) return MONEY end
    return inv
end
MONEY = 212.1

NEXTID = 5000
local STEAL = {}     -- pickable name -> CanSteal answer
local function mkPickable(name, cls, x, y, z, wuid)
    NEXTID = NEXTID + 1
    local e = { class = "PickableItem", id = NEXTID, px = x, py = y, pz = z, Properties = { sItemClassId = cls, nAmount = 1, fHealth = 1 } }
    e.GetName = function() return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    e.SetFlags = function() end
    e.item = { GetId = function() return wuid end, BelongsToDeadBody = function() return false end,
               CanSteal = function() return STEAL[name] == true end, CanUse = function() return true end,
               GetOwnerId = function() return 0 end,
               OnUsed = function(_, uid) if not ENTS[name] then return false end ENTS[name] = nil; player.inventory:AddItem(wuid); return true end,
               OnSteal = function(_, uid) if not ENTS[name] then return false end ENTS[name] = nil; player.inventory:AddItem(wuid); return true end }
    setmetatable(e, { __index = function(_, k) return PickableItem and PickableItem[k] end })
    ENTS[name] = e
    return e
end

player = { id = 1, class = "Player", inventory = mkInventory("pinv"), this = { id = "pwuid" } }
PLAYER_POS = { x = 100, y = 100, z = 10 }
PLAYER_IN_DIALOG = false; PLAYER_DANGER = false; PLAYER_DEAD = false; PLAYER_HORSE = 0
player.GetName = function() return "Dude" end
player.GetWorldPos = function() return { x = PLAYER_POS.x, y = PLAYER_POS.y, z = PLAYER_POS.z } end
player.GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end
player.actor = { GetHealth = function() return 100 end, IsDead = function() return PLAYER_DEAD end }
player.human = { IsInDialog = function() return PLAYER_IN_DIALOG end }
player.soul = { IsInCombatDanger = function() return PLAYER_DANGER end }
player.player = { GetHorseId = function() return PLAYER_HORSE end }
ENTS["Dude"] = player

-- The game's own script tables the mod wraps (defined before the mod loads, as in the game).
PickableItem = {}
function PickableItem:Use(user) if user then return self.item:OnUsed(user.id) end return false end
function PickableItem:OnUsed(user) return self:Use(user) end
function PickableItem:OnUsedHold(user) if user and self.item:CanSteal(user.id) then return self.item:OnSteal(user.id) end return false end
BasicAIActions = {}
TALKS = {}
for _, fn in ipairs({ "OnTalk", "OnChat", "OnChatWithFocus", "OnChatRequestAccepted", "OnChatOpen", "OnLoot", "OnPickpocketing",
                      "OnMercyKill", "OnKnockout", "OnStealthKill" }) do
    local f = fn
    BasicAIActions[f] = function(self, user, slot) TALKS[#TALKS + 1] = f .. ":" .. tostring(self and self:GetName()) end
end
Stash = {}
STASH_OPENED = {}
function Stash:OnUsed(user, slot) if self.bOpened ~= 1 then self.bOpened = 1; STASH_OPENED[#STASH_OPENED + 1] = self:GetName() else self.bOpened = 0 end end
function Stash:GetInventoryToOpen() return self.inventory:GetId() end
function Stash:UsesStealUiPrompt() return self.crimeStash == true end
Minigame = { StartLockPicking = function(id) LOCKPICKS = (LOCKPICKS or 0) + 1; return true end }
Horse = {}
MOUNTS = {}
function Horse:OnMount(user, slot) MOUNTS[#MOUNTS + 1] = self:GetName() end
function Horse:IsMountLegal() return self.mountIsLegal == true or self.mountIsLegalFromAI == true end
function Horse:SetMountIsLegal(v) self.mountIsLegal = v end
Crime = { SendResolveDialogResult = function(dc, action) RESOLVED = action end }


-- WO-140: the game's bed trigger as the level has it (Scripts/Entities/WH/Triggers/BedTrigger.lua,
-- ActionTrigger's ReportUse merged in): instances resolve ReportUse through the class table.
BED_CALLS = {}
BED_CAN = true
BedTrigger = {}
function BedTrigger:IsLyingAction(action) return action.esActionType == "Stance" and action.sAction == "lying" end
function BedTrigger:CanSleep(user, accurate) return BED_CAN, BED_CAN and nil or "@ui_playerCantSkiptime_combat" end
function BedTrigger:ReportUse(user, item, action) BED_CALLS[#BED_CALLS + 1] = self:GetName() .. ":" .. tostring(action.sAction) end
local function mkBed(name, x, y)
    NEXTID = NEXTID + 1
    local e = { class = "BedTrigger", id = NEXTID, px = x, py = y, pz = 10,
                Properties = { Click = { esActionType = "Stance", sAction = "sitting" }, Hold = { esActionType = "Stance", sAction = "lying" } } }
    e.GetName = function() return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    setmetatable(e, { __index = function(_, k) return BedTrigger[k] end })
    ENTS[name] = e
    return e
end
SAVE_BEDS = {}
EntityModule.WillSleepingOnThisBedSave = function(id) return SAVE_BEDS[id] == true end
LAYING = false; INTERRUPTS = 0
player.player.IsLaying = function() return LAYING end
player.player.InterruptSitting = function() INTERRUPTS = INTERRUPTS + 1; LAYING = false end
RESTSAVES = 0
Game.SaveGameViaResting = function() RESTSAVES = RESTSAVES + 1 end

Player = { Client = { OnAction = function(...) end } }

-- @@KDCMP@@

-- Part 2: scenarios.

local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function logCount(pat, from)
    local n = 0
    for i = (from or 0) + 1, #LOG do if string.find(LOG[i], pat, 1, true) then n = n + 1 end end
    return n
end
local emitted = function(name, from)
    local out = {}
    local pat = "[KCD2-MP-EVT] v1 "
    for i = (from or 0) + 1, #LOG do
        local l = LOG[i]
        if l:find(pat, 1, true) then
            local rest = l:sub(#pat + 1)
            local ev, arg = rest:match("^%d+ (%S+) ?(.*)$")
            if ev == name then out[#out + 1] = arg end
        end
    end
    return out
end
local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return table.unpack(r)
end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end
-- the draw loop's rows: every System.DrawText of one frame
local DRAWN = {}
System.DrawText = function(x, y, text, size) DRAWN[#DRAWN + 1] = tostring(text) end
local function frame() DRAWN = {}; KCD2MP_DrawInteractionUI(); return table.concat(DRAWN, " | ") end

local function reset()
    local w = KCD2MP.w153
    w.notice = true; w.follow = false; w.relocM = 25; w.farM = 150
    w.prompt = nil; w.watching = false; w.hostScenes = {}
    w.story = true; w.tetherM = 120; w.mode = "watch"; w.lastDialogueToast = nil
    w.stats = { notices = 0, prompts = 0, watch = 0, keep = 0, timeouts = 0, results = 0, ownCopy = 0 }
    if KCD2MP.w140 then KCD2MP.w140.prompt = nil end
    ERRS = {}; TOASTS = {}
end
local function toastCount(text) local n = 0; for _, t in ipairs(TOASTS) do if t == text then n = n + 1 end end return n end
local function press(action) Player.Client.OnAction(nil, action, "press", 1); Player.Client.OnAction(nil, action, "release", 0) end

-- ================================================================ D: defaults, switches, commands

-- The defaults as shipped, read BEFORE any reset() (which sets its own state for the scenarios below).
do
    local w = KCD2MP.w153
    check("D: as shipped the notice, the bring-along and the story lock are ON", w.notice == true and w.follow == true and w.story == true, tostring(w.notice) .. tostring(w.follow) .. tostring(w.story))
    check("D: as shipped the scene mode is watch (the game's own copy plays) and the tether is 120 m", w.mode == "watch" and w.tetherM == 120)
end

do
    reset(); NOW = 10
    check("D: thresholds ship at 25 m and 150 m", KCD2MP.w153.relocM == 25 and KCD2MP.w153.farM == 150)
    local want = {
        mp_scene_notice = "KCD2MP_SetSceneNotice(%line)", mp_scene_follow = "KCD2MP_SetSceneFollow(%line)",
        mp_scene_follow_m = "KCD2MP_SetSceneFollowM(%line)", mp_scene_follow_far_m = "KCD2MP_SetSceneFollowFarM(%line)",
        mp_scene_watch = "KCD2MP_W153Answer(true)", mp_scene_play = "KCD2MP_W153Answer(false)", mp_scene_status = "KCD2MP_W153Status()" }
    local all = true
    for name, body in pairs(want) do if not (CCMDS[name] and CCMDS[name].body == body) then all = false end end
    check("D: all seven console commands are registered with the right bodies", all)

    local mark = #LOG
    KCD2MP_W153CfgEmit()
    check("D: the settings line carries every setting", emitted("w153_cfg", mark)[1] == "follow=off relocm=25 farm=150 story=on tether=120 mode=watch", emitted("w153_cfg", mark)[1])
    mark = #LOG
    KCD2MP_SetSceneFollow("on")
    check("D: mp_scene_follow on: set, logged, sent to the agent", KCD2MP.w153.follow == true and logCount("WO153-FOLLOW mp_scene_follow on", mark) == 1
        and emitted("w153_cfg", mark)[1] == "follow=on relocm=25 farm=150 story=on tether=120 mode=watch", emitted("w153_cfg", mark)[1])
    KCD2MP_SetSceneFollow("")
    check("D: bare mp_scene_follow reports and changes nothing", KCD2MP.w153.follow == true)
    KCD2MP_SetSceneFollow("%line")
    check("D: the %line placeholder is a bare report too", KCD2MP.w153.follow == true)
    check("D: a bad on|off is refused", KCD2MP_SetSceneFollow("maybe") == false and KCD2MP.w153.follow == true)
    KCD2MP_SetSceneFollow("off")
    check("D: mp_scene_follow off", KCD2MP.w153.follow == false)

    mark = #LOG
    KCD2MP_SetSceneFollowM("40"); KCD2MP_SetSceneFollowFarM("300")
    check("D: both thresholds set and sent", KCD2MP.w153.relocM == 40 and KCD2MP.w153.farM == 300 and emitted("w153_cfg", mark)[2] == "follow=off relocm=40 farm=300 story=on tether=120 mode=watch", emitted("w153_cfg", mark)[2])
    local refused = true
    for _, bad in ipairs({ "4", "5001", "12.5", "abc", "-3", "1e3" }) do
        if KCD2MP_SetSceneFollowM(bad) ~= false then refused = false end
        if KCD2MP_SetSceneFollowFarM(bad) ~= false then refused = false end
    end
    check("D: out-of-range, fractional and malformed metres are refused", refused)
    check("D: ...and leave the values alone", KCD2MP.w153.relocM == 40 and KCD2MP.w153.farM == 300)
    check("D: the edge values 5 and 5000 are accepted", KCD2MP_SetSceneFollowM("5") == true and KCD2MP.w153.relocM == 5 and KCD2MP_SetSceneFollowFarM("5000") == true and KCD2MP.w153.farM == 5000)
    check("D: the far threshold has a floor of 50 (the leash never moves a joiner within 50 m): 49 refused, 50 accepted",
        KCD2MP_SetSceneFollowFarM("49") == false and KCD2MP.w153.farM == 5000 and KCD2MP_SetSceneFollowFarM("50") == true and KCD2MP.w153.farM == 50)
    check("D: ...while the move threshold still accepts 5", KCD2MP_SetSceneFollowM("5") == true and KCD2MP.w153.relocM == 5)
    KCD2MP_SetSceneFollowM("%line")
    check("D: bare mp_scene_follow_m reports and keeps the value", KCD2MP.w153.relocM == 5)
    noErrs("D")
end

-- ================================================================ N: the notice

do
    reset(); NOW = 100
    KCD2MP_W153HostScene("1", true, "Ingame", "m03_scene", false)
    check("N: the host's scene start puts the notice up", KCD2MP.w153.prompt ~= nil and KCD2MP.w153.stats.prompts == 1)
    check("N: the words are shown once, natively", toastCount("Your host is in a cutscene.") == 1, TOASTS[1])
    check("N: the scene's internal name is logged but never put on screen", logCount("WO153-NOTICE the host's Ingame scene 'm03_scene'") == 1 and toastCount("m03_scene") == 0)
    local f = frame()
    check("N: the draw loop shows the line with its countdown", f:find("Your host is in a cutscene.  (20s)", 1, true) ~= nil, f)
    check("N: ...and the two keys with their console fallbacks", f:find("F11 stand beside them to watch", 1, true) ~= nil and f:find("F12 keep playing", 1, true) ~= nil
        and f:find("mp_scene_watch", 1, true) ~= nil and f:find("mp_scene_play", 1, true) ~= nil, f)
    NOW = NOW + 7
    f = frame()
    check("N: the countdown runs down (7 s on: 13 s)", f:find("(13s)", 1, true) ~= nil, f)

    KCD2MP_W153HostScene("1", false, "Ingame", "m03_scene", false)
    check("N: the host's scene ending takes the notice away", KCD2MP.w153.prompt == nil)
    check("N: ...with one 'over' line, since it had been offered", toastCount("Your host's cutscene is over.") == 1)
    check("N: ...and nothing more is drawn", frame():find("Your host is in a cutscene", 1, true) == nil)

    reset(); NOW = 200
    KCD2MP_W153HostScene("1", false, "Ingame", "x", false)
    check("N: an end the player never saw offered says nothing", #TOASTS == 0)

    reset(); NOW = 300
    KCD2MP_W153HostScene("1", true, "Rendered", "intro_video", true)
    check("N: when this game's own copy is already playing there is nothing to offer", KCD2MP.w153.prompt == nil and #TOASTS == 0 and KCD2MP.w153.stats.ownCopy == 1)
    check("N: ...and the reason is logged", logCount("this game's own copy is playing: nothing to offer") == 1)

    reset(); NOW = 400
    KCD2MP_SetSceneNotice("off")
    KCD2MP_W153HostScene("1", true, "Ingame", "a", false)
    check("N: with mp_scene_notice off nothing is shown", KCD2MP.w153.prompt == nil and #TOASTS == 0 and KCD2MP.w153.stats.notices == 0)
    KCD2MP_SetSceneNotice("on")
    KCD2MP_W153HostScene("1", true, "Ingame", "a", false)
    check("N: ...and on again it is", KCD2MP.w153.prompt ~= nil)
    KCD2MP_SetSceneNotice("off")
    check("N: switching it off while a notice is up takes the notice away", KCD2MP.w153.prompt == nil)
    check("N: a bad value is refused", KCD2MP_SetSceneNotice("perhaps") == false)
    KCD2MP_SetSceneNotice("on")

    reset(); NOW = 500
    local mark = #LOG
    KCD2MP_W153HostScene("1", true, "Ingame", "slow", false)
    NOW = NOW + 20.5
    frame()
    check("N: 20 s without an answer: the notice goes and the player keeps playing", KCD2MP.w153.prompt == nil and KCD2MP.w153.stats.timeouts == 1)
    check("N: ...logged once, and the agent is NOT asked to move anyone", logCount("WO153-TIMEOUT", mark) == 1 and #emitted("w153_watch", mark) == 0)
    frame()
    check("N: ...a second frame does not count a second timeout", KCD2MP.w153.stats.timeouts == 1)
    noErrs("N")
end

-- Nesting: the host's scenes nest (start, start, end, end); only the LAST end ends the notice.
do
    reset(); NOW = 1000
    local mark = #LOG
    KCD2MP_W153HostScene("1", true, "Ingame", "outer", false)
    KCD2MP_W153HostScene("1", true, "Rendered", "inner", false)
    KCD2MP_W153HostScene("1", false, "Rendered", "inner", false)
    check("N: (nesting) the inner scene's end leaves the offer up", KCD2MP.w153.prompt ~= nil)
    check("N: ...and says nothing", toastCount("Your host's cutscene is over.") == 0)
    check("N: ...but logs that another scene still runs", logCount("another of the host's scenes is still running", mark) == 1)
    press("kcd2mp_dice_bank")
    check("N: (nesting) F11 still works after the inner end", #emitted("w153_watch", mark) == 1 and KCD2MP.w153.watching == true)
    KCD2MP_W153HostScene("1", false, "Rendered", "never-started", false)
    check("N: an end naming no running scene changes nothing", KCD2MP.w153.watching == true and toastCount("Your host's cutscene is over.") == 0)
    KCD2MP_W153HostScene("1", false, "Ingame", "outer", false)
    check("N: the LAST end clears 'watching' and says it is over, once", KCD2MP.w153.watching == false and toastCount("Your host's cutscene is over.") == 1)

    reset(); NOW = 2000
    KCD2MP_W153HostScene("1", true, "Ingame", "lost-end", false)      -- its end is never logged
    NOW = NOW + 1801
    KCD2MP_W153HostScene("1", true, "Ingame", "next", false)
    KCD2MP_W153HostScene("1", false, "Ingame", "next", false)
    check("N: a scene whose end was never logged does not hold the next one open (purged after 30 min)", KCD2MP.w153.prompt == nil and toastCount("Your host's cutscene is over.") == 1)
    noErrs("N-nesting")
end

-- Chained or nested starts: the offer is made once; whatever the player answered stands until the host's LAST scene ends.
do
    reset(); NOW = 1100
    local mark = #LOG
    KCD2MP_W153HostScene("1", true, "Ingame", "first", false)
    check("N: (chained) the first scene makes the offer", KCD2MP.w153.prompt ~= nil and KCD2MP.w153.stats.prompts == 1 and toastCount("Your host is in a cutscene.") == 1)
    press("kcd2mp_dice_yield")
    check("N: (chained) F12 answers keep playing", KCD2MP.w153.prompt == nil and KCD2MP.w153.stats.keep == 1)
    KCD2MP_W153HostScene("1", true, "Ingame", "second", false)
    check("N: (chained) a second scene inside it does NOT override the F12: no new prompt", KCD2MP.w153.prompt == nil)
    check("N: ...no second toast and no second count", toastCount("Your host is in a cutscene.") == 1 and KCD2MP.w153.stats.prompts == 1 and KCD2MP.w153.stats.notices == 1)
    check("N: ...and the reason is logged", logCount("started inside another of the host's scenes -- no second offer", mark) == 1)
    KCD2MP_W153HostScene("1", false, "Ingame", "first", false)
    check("N: (chained) the first one ending leaves it quiet (another still runs)", KCD2MP.w153.prompt == nil and toastCount("Your host's cutscene is over.") == 0)
    KCD2MP_W153HostScene("1", false, "Ingame", "second", false)
    check("N: (chained) after the LAST end a scene later is a fresh offer", (function()
        KCD2MP_W153HostScene("1", true, "Ingame", "third", false)
        return KCD2MP.w153.prompt ~= nil and KCD2MP.w153.stats.prompts == 2
    end)())
    noErrs("N-chained")
end

-- Reset: a session start or end, the host leaving, or a load: no notice and no remembered scene survives it.
do
    reset(); NOW = 1200
    local mark = #LOG
    KCD2MP_W153HostScene("1", true, "Ingame", "host-quit-mid-scene", false)    -- the host quits: its end edge never comes
    check("N: (reset) a notice is up and a scene is remembered", KCD2MP.w153.prompt ~= nil and next(KCD2MP.w153.hostScenes) ~= nil)
    KCD2MP_W153Reset("host-left")
    check("N: the reset takes the notice and the remembered scenes away", KCD2MP.w153.prompt == nil and KCD2MP.w153.watching == false and next(KCD2MP.w153.hostScenes) == nil)
    check("N: ...and logs it once, with the reason", logCount("WO153-RESET host-left", mark) == 1)
    KCD2MP_W153Reset("connect")
    check("N: a reset with nothing to forget is silent", logCount("WO153-RESET connect", mark) == 0)
    KCD2MP_W153HostScene("1", true, "Ingame", "next-session-scene", false)
    check("N: (reset) the next session's first scene IS offered (it was silenced for 30 min before the reset existed)", KCD2MP.w153.prompt ~= nil and toastCount("Your host is in a cutscene.") == 2)
    KCD2MP.w153.watching = true
    KCD2MP_W153Reset("disconnect")
    check("N: a watching player is reset too", KCD2MP.w153.watching == false and KCD2MP.w153.prompt == nil)
    noErrs("N-reset")
end

-- Precedence: the older shared-quest readiness prompt (WO-94) uses the same two keys and is answered first.
do
    reset(); NOW = 1300
    local mark = #LOG
    local origQA = KCD2MP_QuestAnswer
    QUEST_ANSWERED = nil
    KCD2MP_QuestAnswer = function(yes) QUEST_ANSWERED = yes end
    KCD2MP_W153HostScene("1", true, "Ingame", "p", false)
    KCD2MP.quest.prompt = { fake = true }
    press("kcd2mp_dice_bank")
    check("K: (quest prompt) with the readiness prompt up, F11 is the quest prompt's: the notice is not answered", QUEST_ANSWERED == true and KCD2MP.w153.prompt ~= nil and #emitted("w153_watch", mark) == 0)
    press("kcd2mp_dice_yield")
    check("K: ...and so is F12", QUEST_ANSWERED == false and KCD2MP.w153.prompt ~= nil and KCD2MP.w153.stats.keep == 0)
    KCD2MP.quest.prompt = nil
    press("kcd2mp_dice_bank")
    check("K: with the readiness prompt gone, F11 answers the notice", #emitted("w153_watch", mark) == 1)
    KCD2MP_QuestAnswer = origQA
    KCD2MP.quest.prompt = nil
    noErrs("K-quest")
end

-- The race: this game's own copy of the scene starts 0.1-1.5 s after the host's, after the notice is up.
do
    reset(); NOW = 900
    local mark = #LOG
    KCD2MP_W153HostScene("1", true, "Ingame", "race", false)
    check("N: (race) the notice is up before this game's own copy starts", KCD2MP.w153.prompt ~= nil)
    KCD2MP_SetCutscene(true, "race")
    check("N: this game's own copy starting withdraws the notice (once, logged)", KCD2MP.w153.prompt == nil and logCount("WO153-NOTICE withdrawn", mark) == 1)
    press("kcd2mp_dice_bank")
    check("N: ...so a stray F11 asks the agent nothing", #emitted("w153_watch", mark) == 0)
    KCD2MP_SetCutscene(false, "race")
    KCD2MP_W153HostScene("1", false, "Ingame", "race", false)
    check("N: ...and the host's end after it says nothing (nothing was on offer)", toastCount("Your host's cutscene is over.") == 0)
    KCD2MP_SetCutscene(true, "own-only")
    check("N: this game's own scene with no notice up changes nothing", KCD2MP.w153.prompt == nil and logCount("WO153-NOTICE withdrawn", mark) == 1)
    KCD2MP_SetCutscene(false, "own-only")
    noErrs("N-race")
end

-- ================================================================ K: the keys

do
    reset(); NOW = 600
    local mark = #LOG
    KCD2MP_W153HostScene("1", true, "Ingame", "k1", false)
    press("kcd2mp_dice_bank")
    check("K: F11 answers WATCH: the agent is asked once", #emitted("w153_watch", mark) == 1 and KCD2MP.w153.stats.watch == 1)
    check("K: ...the notice is gone and the player is marked as watching", KCD2MP.w153.prompt == nil and KCD2MP.w153.watching == true)
    press("kcd2mp_dice_bank")
    check("K: a second F11 with no notice asks nothing", #emitted("w153_watch", mark) == 1)

    reset(); mark = #LOG
    KCD2MP_W153HostScene("1", true, "Ingame", "k2", false)
    press("kcd2mp_dice_yield")
    check("K: F12 answers KEEP PLAYING: the agent is not asked", #emitted("w153_watch", mark) == 0 and KCD2MP.w153.stats.keep == 1 and KCD2MP.w153.prompt == nil)

    reset(); mark = #LOG
    KCD2MP_W153HostScene("1", true, "Ingame", "k3", false)
    press("confirm"); press("ui_accept"); press("dialog_answer1")
    check("K: the generic accept actions never move a player (confirm, ui_accept, dialog_answer1)", KCD2MP.w153.prompt ~= nil and #emitted("w153_watch", mark) == 0)
    press("cancel"); press("ui_cancel")
    check("K: a menu's cancel (cancel, ui_cancel) does NOT dismiss the notice -- it times out", KCD2MP.w153.prompt ~= nil and KCD2MP.w153.stats.keep == 0 and #emitted("w153_watch", mark) == 0)
    press("kcd2mp_dice_yield")
    check("K: only F12 dismisses it, as keep playing", KCD2MP.w153.prompt == nil and KCD2MP.w153.stats.keep == 1 and #emitted("w153_watch", mark) == 0)

    reset(); mark = #LOG
    KCD2MP_W153HostScene("1", true, "Ingame", "k4", false)
    Player.Client.OnAction(nil, "kcd2mp_dice_bank", "release", 0)
    Player.Client.OnAction(nil, "kcd2mp_dice_bank", "hold", 1)
    check("K: only a PRESS answers (release and hold do not)", KCD2MP.w153.prompt ~= nil and #emitted("w153_watch", mark) == 0)

    reset(); mark = #LOG
    KCD2MP_W153HostScene("1", true, "Ingame", "k5", false)
    KCD2MP_W153Answer(true)
    check("K: mp_scene_watch is the same as F11", #emitted("w153_watch", mark) == 1)
    KCD2MP_W153HostScene("1", false, "Ingame", "k5", false)   -- the first scene ends; the next is a fresh offer
    KCD2MP_W153HostScene("1", true, "Ingame", "k6", false)
    KCD2MP_W153Answer(false)
    check("K: mp_scene_play is the same as F12", #emitted("w153_watch", mark) == 1 and KCD2MP.w153.stats.keep == 1)
    check("K: an answer with no notice up is refused and logged", KCD2MP_W153Answer(true) == false and logCount("WO153-ANSWER no cutscene notice is up", mark) == 1)

    -- precedence: the sleep prompt (WO-140) is answered first; one press never answers two prompts
    reset(); mark = #LOG
    KCD2MP.w140.prompt = { id = 7, text = "x wants to sleep", deadline = NOW + 30 }
    KCD2MP_W153HostScene("1", true, "Ingame", "k7", false)
    press("kcd2mp_dice_bank")
    check("K: with the sleep prompt up too, F11 answers the sleep prompt only", KCD2MP.w140.prompt == nil and KCD2MP.w153.prompt ~= nil and #emitted("w153_watch", mark) == 0)
    press("kcd2mp_dice_bank")
    check("K: ...and the next F11 answers the notice", KCD2MP.w153.prompt == nil and #emitted("w153_watch", mark) == 1)
    noErrs("K")
end

-- ================================================================ R: the agent's answer to a watch

do
    reset(); NOW = 700
    KCD2MP.w153.watching = true
    KCD2MP_W153Result("placed")
    check("R: placed: 'You are beside your host.'", toastCount("You are beside your host.") == 1 and KCD2MP.w153.watching == true)
    local codes = { busy = "You can't be moved right now. Try again when you are free.", nopos = "Can't tell where your host is yet.",
                    mounted = "Get off your horse first.", failed = "Could not move you beside your host.", notjoined = "You are not in a host's world.",
                    pulling = "You are already being brought to your host." }
    local all = true
    for code, text in pairs(codes) do
        KCD2MP.w153.watching = true
        local before = toastCount(text)
        KCD2MP_W153Result(code)
        if toastCount(text) ~= before + 1 or KCD2MP.w153.watching ~= false then all = false end
    end
    check("R: every refusal has its own words and clears 'watching'", all)
    KCD2MP_W153Result("something-new")
    check("R: an unknown code from a newer agent says the plain failure", toastCount("Could not move you beside your host.") >= 2)
    KCD2MP_W153Result(nil)
    check("R: a nil code does not error", true)
    check("R: results are counted", KCD2MP.w153.stats.results == 9, KCD2MP.w153.stats.results)
    local before = #TOASTS
    KCD2MP.w153.watching = true
    KCD2MP_W153Result("over")
    check("R: 'over' (the host's scene ended while the watch was set up) says nothing more, and clears 'watching'", #TOASTS == before and KCD2MP.w153.watching == false)
    noErrs("R")
end

-- ================================================================ S: status and the words

do
    reset(); NOW = 800
    KCD2MP_W153HostScene("1", true, "Ingame", "s", false)
    local mark = #LOG
    KCD2MP_W153Status()
    check("S: the status also asks the agent for its own counters", #emitted("w153_status", mark) == 1)
    check("S: the status line carries every setting and counter",
        logCount("WO153-STATUS notice=on follow=off reloc_m=25 far_m=150 prompt=yes watching=no | notices=1 prompts=1 watch=0 keep=0 timeouts=0 results=0 own_copy=0", mark) == 1, LOG[#LOG])
    local clean = true
    for _, t in pairs(KCD2MP_W153_TEXT) do
        if t:find("[\128-\255]") then clean = false end
    end
    check("S: the project's own words are plain ASCII (nothing copied from the game)", clean)
    noErrs("S")
end


-- ================================================================ T: the story sections, the tether, the scene mode

do
    reset(); NOW = 3000
    local mark = #LOG
    check("T: the three new commands are registered", CCMDS["mp_story_lock"] and CCMDS["mp_story_lock"].body == "KCD2MP_SetStoryLock(%line)"
        and CCMDS["mp_story_tether_m"] and CCMDS["mp_story_tether_m"].body == "KCD2MP_SetStoryTether(%line)"
        and CCMDS["mp_scene_mode"] and CCMDS["mp_scene_mode"].body == "KCD2MP_SetSceneMode(%line)")

    -- mp_story_lock
    KCD2MP_SetStoryLock("off")
    check("T: mp_story_lock off: set, sent to the agent", KCD2MP.w153.story == false and emitted("w153_cfg", mark)[1]:find("story=off", 1, true) ~= nil, emitted("w153_cfg", mark)[1])
    check("T: a bad value is refused", KCD2MP_SetStoryLock("maybe") == false and KCD2MP.w153.story == false)
    KCD2MP_SetStoryLock("on")
    check("T: mp_story_lock on", KCD2MP.w153.story == true)

    -- mp_story_tether_m: 60..5000, digits only
    mark = #LOG
    check("T: the tether accepts 60 and 5000 and sends it", KCD2MP_SetStoryTether("60") == true and KCD2MP.w153.tetherM == 60 and KCD2MP_SetStoryTether("5000") == true and KCD2MP.w153.tetherM == 5000)
    local refused = true
    for _, bad in ipairs({ "59", "5001", "12.5", "abc", "-1", "1e3", "0x80" }) do if KCD2MP_SetStoryTether(bad) ~= false then refused = false end end
    check("T: below 60, above 5000, fractions and malformed values are refused", refused and KCD2MP.w153.tetherM == 5000)
    KCD2MP_SetStoryTether("120")
    check("T: ...and the cfg line carries it", emitted("w153_cfg", mark)[#emitted("w153_cfg", mark)]:find("tether=120", 1, true) ~= nil)

    -- mp_scene_mode
    mark = #LOG
    KCD2MP_SetSceneMode("play")
    check("T: mp_scene_mode play: set and sent", KCD2MP.w153.mode == "play" and emitted("w153_cfg", mark)[1]:find("mode=play", 1, true) ~= nil, emitted("w153_cfg", mark)[1])
    check("T: a bad mode is refused and changes nothing", KCD2MP_SetSceneMode("sometimes") == false and KCD2MP.w153.mode == "play")
    KCD2MP_SetSceneMode("%line")
    check("T: bare mp_scene_mode reports", KCD2MP.w153.mode == "play")
    KCD2MP_SetSceneMode("WATCH")
    check("T: the value is case-insensitive", KCD2MP.w153.mode == "watch")

    -- the notice's words follow the mode
    reset(); NOW = 3100
    KCD2MP_W153HostScene("1", true, "Ingame", "mode-words", false)
    check("T: in watch mode the notice is the plain one", toastCount("Your host is in a cutscene.") == 1)
    reset(); NOW = 3200; KCD2MP_SetSceneMode("play")
    KCD2MP_W153HostScene("1", true, "Ingame", "mode-words2", false)
    check("T: in play mode the notice says the own copy is skipped", toastCount("Your host is in a cutscene. Your own copy is skipped; keep playing.") == 1)
    check("T: ...and the overlay carries the same words", frame():find("Your own copy is skipped", 1, true) ~= nil)

    -- the story toasts
    reset(); NOW = 3300; TOASTS = {}
    KCD2MP_W153Story("enter", "Wedding Crashers", "the wedding in Semine")
    check("T: a joiner is told the host entered a locked section", toastCount("Your host entered Wedding Crashers (the wedding in Semine): stay close. You are brought to them if you stray.") == 1, TOASTS[1])
    KCD2MP_W153Story("leave", "Wedding Crashers", "")
    check("T: ...and that it is over", toastCount("Wedding Crashers is over. You are free to roam again.") == 1)
    KCD2MP_W153Story("level", "", "")
    check("T: ...and that the host is moving to the next region", toastCount("Your host is moving to the next region. You are brought along when they arrive.") == 1)
    KCD2MP_W153Story("host-enter", "Trosky", "Trosky castle")
    check("T: the host is told its partners are kept within the tether", toastCount("Locked story section: Trosky (Trosky castle). Your partners are kept within 120 m.") == 1, TOASTS[#TOASTS])
    KCD2MP_W153Story("host-leave", "Trosky", "completed")
    check("T: ...and when they are free again", toastCount("Story section over: Trosky. Your partners are free to roam.") == 1)
    local n = #TOASTS
    KCD2MP_W153Story("bogus", "x", "y")
    check("T: an unknown kind says nothing", #TOASTS == n)

    -- the dialogue toast: once per 8 s
    reset(); NOW = 3400; TOASTS = {}
    KCD2MP_W153Story("dialogue", "", "")
    KCD2MP_W153Story("dialogue", "", "")
    check("T: the host's conversation is told once, not per edge", toastCount("Your host is in a conversation.") == 1)
    NOW = NOW + 9
    KCD2MP_W153Story("dialogue", "", "")
    check("T: ...and again after 8 s", toastCount("Your host is in a conversation.") == 2)

    -- the joiner-side toasts honour mp_scene_notice off; the host's own never do
    reset(); NOW = 3500; TOASTS = {}
    KCD2MP_SetSceneNotice("off")
    KCD2MP_W153Story("enter", "Wedding Crashers", "x"); KCD2MP_W153Story("leave", "Wedding Crashers", ""); KCD2MP_W153Story("level", "", ""); KCD2MP_W153Story("dialogue", "", "")
    check("T: with mp_scene_notice off a joiner is told nothing of the story", #TOASTS == 0)
    KCD2MP_W153Story("host-enter", "Trosky", "Trosky castle")
    check("T: ...but the host still hears its own section", #TOASTS == 1)
    KCD2MP_SetSceneNotice("on")

    -- the status line carries the new settings
    reset(); NOW = 3600
    mark = #LOG
    KCD2MP_W153Status()
    check("T: the status line carries the story settings", logCount("WO153-STATUS story=on tether_m=120 mode=watch", mark) == 1)
    noErrs("T")
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
