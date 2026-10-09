-- SPDX-License-Identifier: GPL-3.0-only
-- Runs after the common WO-134 fixture; native inventories are stand-ins, not engine proof.
local C=KCD2MP_Containers
local R=KCD2MP_Rewards
local scope='0123456789abcdef0123456789abcdef'
local function fresh(host)
    C.mode=false; C.reset(); KCD2MP_CheckpointRelease(); world()
    if host then asHost(1) else asJoiner() end
    KCD2MP_ContainerMode(true,scope)
    KCD2MP_RewardMode(host,scope)
end
local function move(source,dest,cls)
    for _,wuid in ipairs(source.inventory:GetInventoryTable()) do
        if ITEMS[wuid].class==cls then source.inventory:RemoveItem(wuid); dest.inventory:AddItem(wuid); return wuid end
    end
end
fresh(true)
local horse=mkChest('horse_test','Horse',101,100,10,0)
horse.inventory:CreateItem(APPLE,1,2)
check('P1 named horse saddlebags are a shared container', C.kind(horse)=='horse' and C.id(horse)=='horse_test')
local stash=mkChest('live_chest','Stash',102,100,10,7); stash.inventory:CreateItem(DICE,1,1)
local shop=mkChest('live_shop','Stash',120,100,10,7); shop.inventory:CreateItem(COAT,1,1)
Shops={IsLinkedWithShop=function(id) return id==shop.id and 'shop-link' or nil end}
Framework={IsValidWUID=function(id) return id=='shop-link' end}
check('P2 shop-linked stash classification', C.kind(shop)=='shop' and C.kind(stash)=='chest')
LOG={}; C.pass()
check('P3 host broadcasts chest horse and distant shop stock', #events('w134_cstate')==3)
KCD2MP_ContainerAsk(2,1001,'horse_test',APPLE,1,1,'horse',0,false,scope)
check('P4 host saddlebag take is read back and acknowledged', horse.inventory:GetCountOfClass(APPLE)==1 and string.find(events('w134_cres')[1] or '', '1001 ok ',1,true)~=nil)
LOG={}; KCD2MP_ContainerAsk(2,1001,'horse_test',APPLE,1,1,'horse',0,false,scope)
check('P5 saddlebag retry replays without a second native deletion',horse.inventory:GetCountOfClass(APPLE)==1 and string.find(events('w134_cres')[1] or '', '1001 ok ',1,true)~=nil)
LOG={}; KCD2MP_ContainerAsk(3,1002,'horse_test',APPLE,2,1,'horse',0,false,scope)
check('P6 competing take cannot overdraw stock',horse.inventory:GetCountOfClass(APPLE)==1 and string.find(events('w134_cres')[1] or '', '1002 gone ',1,true)~=nil)
LOG={}; KCD2MP_ContainerAsk(2,1003,'live_chest',APPLE,2,0.7,'chest',0,true,scope)
KCD2MP_ContainerAsk(2,1003,'live_chest',APPLE,2,0.7,'chest',0,true,scope)
check('P7 a put is added to host stock once',stash.inventory:GetCountOfClass(APPLE)==2)
local erase=stash.inventory.DeleteItem; stash.inventory.DeleteItem=function() end
LOG={}; KCD2MP_ContainerAsk(2,1004,'live_chest',DICE,1,1,'chest',0,false,scope)
check('P8 inert native removal is not acknowledged',#events('w134_cres')==0)
stash.inventory.DeleteItem=erase
LOG={}; KCD2MP_ContainerAsk(2,1004,'live_chest',DICE,1,1,'chest',0,false,scope)
check('P9 uncertain native removal is never retried',stash.inventory:GetCountOfClass(DICE)==1 and #events('w134_cres')==0)

fresh(false)
stash=mkChest('live_chest','Stash',101,100,10,7); stash.inventory:CreateItem(APPLE,1,1)
C.pass(); LOG={}; move(stash,player,APPLE); C.pass()
local token=firstTok('w134_ctake')
check('P10 a guest take requires a player-pack gain',token~=nil and player.inventory:GetCountOfClass(APPLE)==1)
KCD2MP_ContainerState('live_chest','chest',5,1,1,{{APPLE,1,1,false}})
check('P11 an unresolved take cannot be overwritten by an older stock snapshot',stash.inventory:GetCountOfClass(APPLE)==0)
KCD2MP_ContainerResult(token,'gone')
check('P12 refusal removes Henry item and then applies deferred authoritative stock',player.inventory:GetCountOfClass(APPLE)==0 and stash.inventory:GetCountOfClass(APPLE)==1)
KCD2MP_ContainerState('live_chest','chest',4,1,1,{})
check('P13 stale stock generation cannot erase newer stock',stash.inventory:GetCountOfClass(APPLE)==1)
LOG={}; stash.inventory:CreateItem(COAT,1,1); C.pass()
check('P14 a restock without pack loss is not reported as a put',#events('w134_cput')==0)
LOG={}; stash.inventory:DeleteItem(stash.inventory.list[1],-1); C.pass()
check('P15 a stock loss without pack gain is not a player take',#events('w134_ctake')==0)
player.inventory:CreateItem(DICE,1,1); C.pass(); LOG={}; move(player,stash,DICE); C.pass()
check('P16 a pack-to-chest put asks the host',firstTok('w134_cput')~=nil)

fresh(false)
shop=mkChest('live_shop','Stash',120,100,10,7); shop.inventory:CreateItem(COAT,1,1)
Shops={IsLinkedWithShop=function(id) return id==shop.id and 'shop-link' end}; Framework={IsValidWUID=function(id) return id=='shop-link' end}
player.inventory:CreateItem(MONEY,1,100); C.pass(); LOG={}
move(shop,player,COAT)
for _,wuid in ipairs(player.inventory.list) do if ITEMS[wuid].class==MONEY then ITEMS[wuid].amount=75 end end
C.pass(); token=firstTok('w134_ctake')
check('P17 trade watches stock beyond ordinary chest reach',token~=nil and string.find(events('w134_ctake')[1] or '', ' shop 25',1,true)~=nil)
KCD2MP_ContainerResult(token,'gone')
check('P18 a denied purchase removes goods and refunds the observed personal debit once',player.inventory:GetCountOfClass(COAT)==0 and player.inventory:GetCountOfClass(MONEY)==100)
KCD2MP_ContainerResult(token,'gone')
check('P19 duplicate verdict does not repeat refund',player.inventory:GetCountOfClass(MONEY)==100)

fresh(false)
stash=mkChest('live_chest','Stash',101,100,10,7); stash.inventory:CreateItem(APPLE,1,1)
C.pass(); move(stash,player,APPLE); C.pass(); NOW=NOW+21; C.settle(20)
check('P20 unconfirmed container take is revoked after timeout',player.inventory:GetCountOfClass(APPLE)==0 and next(C.pending)==nil)
KCD2MP_W134Tick(true,false,true,1) -- the still-running agent refreshes its heartbeat after the simulated 21 seconds
KCD2MP_W131Tick(true,true)
stash.inventory:CreateItem(APPLE,1,1); C.pass(); move(stash,player,APPLE); C.pass()
local del=player.inventory.DeleteItem; player.inventory.DeleteItem=function() end
check('P21 failed native rollback remains pending and blocks snapshot settlement',C.settle(0)>0 and next(C.pending)~=nil)
local snapshotCalled=false; Game.QuickSave=function() snapshotCalled=true; return true end
LOG={}; KCD2MP_Wo125Snapshot('snapshot-test')
check('P22 unresolved native rollback cannot enter a character snapshot',not snapshotCalled and string.find(events('wo124_reply')[1] or '', 'ok=false',1,true)~=nil)
player.inventory.DeleteItem=del; C.settle(0)
KCD2MP_ContainerMode(false,scope)
check('P23 personal-ledger compatibility mode remains selectable',not C.mode)
fresh(false)
stash=mkChest('disabled_chest','Stash',101,100,10,7)
KCD2MP.w134.chests=false
check('P23a existing chest kill switch also disables live containers',not C.active())
KCD2MP.w134.chests=true
stash.inventory:CreateItem(APPLE,1,1); C.pass(); move(stash,player,APPLE)
C.settle(20); LOG={}; C.pass()
check('P23b heartbeat settlement cannot consume an unobserved native transfer',firstTok('w134_ctake')~=nil)

fresh(false)
local key=string.rep('a',32)
KCD2MP_RewardGive('q_test',key,{{APPLE,2,1,false}})
check('P24 reward arriving before quest application is held, not granted',player.inventory:GetCountOfClass(APPLE)==0 and R.pending[key]~=nil)
KCD2MP_RewardArm('arm','q_test',key,false)
player.inventory:CreateItem(APPLE,1,1); KCD2MP_RewardReady(key); R.step()
check('P25 verified quest reward subtracts what native quest already paid',player.inventory:GetCountOfClass(APPLE)==2)
KCD2MP_RewardGive('q_test',key,{{APPLE,2,1,false}})
check('P26 reward retry is not paid twice',player.inventory:GetCountOfClass(APPLE)==2)
key=string.rep('b',32); KCD2MP_RewardArm('arm2','q_test',key,false)
player.inventory:CreateItem(DICE,1,1); C.gain(DICE,1); KCD2MP_RewardReady(key)
KCD2MP_RewardGive('q_test',key,{{DICE,1,1,false}})
check('P27 unrelated loot does not count as native quest payment',player.inventory:GetCountOfClass(DICE)==2)
key=string.rep('c',32); KCD2MP_RewardArm('arm3','q_test',key,false); KCD2MP_RewardReady(key)
local create=player.inventory.CreateItem; player.inventory.CreateItem=function() end; LOG={}
KCD2MP_RewardGive('q_test',key,{{COAT,1,1,false}})
check('P28 inert native reward creation is reported unverified',#events('w134_reward_unverified')==1)
player.inventory.CreateItem=create; KCD2MP_RewardGive('q_test',key,{{COAT,1,1,false}})
check('P29 uncertain reward creation is not retried automatically',player.inventory:GetCountOfClass(COAT)==0)

fresh(true)
R.last=({}); LOG={}; key=string.rep('d',32)
KCD2MP_RewardArm('host1','q_test',key,true)
KCD2MP_RewardArm('host2','q_test',string.rep('e',32),true)
player.inventory:CreateItem(APPLE,1,2); player.inventory:CreateItem(DICE,1,1); C.gain(DICE,1)
NOW=NOW+4; R.step()
check('P30 host observes reward excluding loot, coalescing same-quest cascades',#events('w134_reward')==1 and string.find(events('w134_reward')[1] or '',APPLE..':2',1,true)~=nil and string.find(events('w134_reward')[1] or '',DICE,1,true)==nil)
LOG={}; KCD2MP_RewardArm('retry','q_test',key,true); NOW=NOW+4; R.step()
check('P31 replayed host quest signature cannot pay again',#events('w134_reward')==0)
fresh(true)
stash=mkChest('host_chest','Stash',101,100,10,7); stash.inventory:CreateItem(DICE,1,1)
C.pass(); move(stash,player,DICE); C.pass()
check('P32 host container gains are excluded from quest reward inference',#C.gains==1 and C.gains[1].class==DICE)
fresh(false)
stash=mkChest('refund_chest','Stash',101,100,10,7)
player.inventory:CreateItem(DICE,0.65,1)
local original=player.inventory.list[1]; ITEMS[original].provenance='stolen-owner-A'
C.pass(); move(player,stash,DICE); LOG={}; C.pass(); token=firstTok('w134_cput')
check('P33 whole put captures the original native item identity',token and C.pending[token].original and C.pending[token].original[1].w==original)
KCD2MP_ContainerResult(token,'gone')
check('P34 definitive refusal returns the original instance with condition and opaque metadata',player.inventory.list[1]==original and ITEMS[original].provenance=='stolen-owner-A' and ITEMS[original].health==0.65 and stash.inventory:GetCountOfClass(DICE)==0 and next(C.pending)==nil)
fresh(false)
stash=mkChest('unknown_put','Stash',101,100,10,7); player.inventory:CreateItem(DICE,1,1)
C.pass(); move(player,stash,DICE); LOG={}; C.pass(); token=firstTok('w134_cput')
KCD2MP_ContainerResult(token,'uncertain')
check('P35 crash-quarantined host put cannot be mistaken for definite refusal or refunded',player.inventory:GetCountOfClass(DICE)==0 and stash.inventory:GetCountOfClass(DICE)==1 and C.settle(0)>0)
LOG={}; KCD2MP_W134Status()
check('P35a suspended live stock never falls through to the personal inverse ledger',string.find(table.concat(LOG,' '),'chest_rec=false',1,true)~=nil)
fresh(false)
stash=mkChest('inert_refund','Stash',101,100,10,7); player.inventory:CreateItem(DICE,1,1)
C.pass(); move(player,stash,DICE); LOG={}; C.pass(); token=firstTok('w134_cput')
local add=player.inventory.AddItem; player.inventory.AddItem=function() end
KCD2MP_ContainerResult(token,'gone')
player.inventory.AddItem=add; KCD2MP_ContainerResult(token,'gone')
check('P36 uncertain native compensation stays pending and is never blindly retried',C.pending[token]~=nil and C.pending[token].restoreAttempted and player.inventory:GetCountOfClass(DICE)==0)
check('no Lua errors',#ERRS==0,ERRS[1])
local pass,fail=0,0; for _,result in ipairs(RESULTS) do if result:sub(1,4)=='PASS' then pass=pass+1 else fail=fail+1 end end
OUT=table.concat(RESULTS,'\n')..string.format('\n%d passed, %d failed',pass,fail)
