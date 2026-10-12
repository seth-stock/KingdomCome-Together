# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""
Session 10: who runs a quest's effects? A census of every node class in the game's quest graphs (Scripts.pak, read only), grouped by
what the quest sync does about it:

  consumer   an effect that runs when its State changes. The sync applies the host's State change on the joiner through the engine's own
             port trigger, so the SAME consumers run natively on the joiner (spawns, layers, behaviours, items, buffs).
  trigger    a local observation (an area, a distance, a death, an interaction, a dialogue line). It fires on the machine where it is
             observed and its State change is then mirrored; it is not itself mirrored.
  per-player an effect on one player (reward, quest item, buff, payment, tutorial, autosave, cutscene): each machine runs its own.
  structure  data flow (functions, arrays, branches, timers, outputs): no world effect of its own.
  other      anything not listed above; printed so the table never hides a class.

    python tools\\Audit-QuestEffects.py [--mod-dir E:\\SteamLibrary\\steamapps\\common\\KCD2Mod] [--out docs/SESSION10-QUEST-EFFECTS.md]

The classification is by class name and is a reading aid, not an engine fact: it says what the existing mirror covers by construction, not
that every instance was observed in two worlds.
"""
import argparse
import collections
import re
import sys
import zipfile
from pathlib import Path

CONSUMER = {
    'Layer': 'activates/deactivates a level layer (the spawn mechanism)', 'EnableBehavior': 'turns an NPC behaviour on/off',
    'SetEntityContext': 'AI context on an entity', 'SetEntityContextPreset': 'AI context preset', 'SetGameContext': 'game context',
    'SetRelationContext': 'relation context', 'SetOwner': 'item/entity ownership', 'AddFactionRelationBetweenArrays': 'faction relations',
    'NPCs_TeleportIngame': 'moves NPCs', 'PermaDeath': 'marks a soul permanently dead', 'skirmish': 'starts a skirmish',
    'ClothingPresetOverride': 'NPC clothing', 'WeaponPresetOverride': 'NPC weapons', 'ObjectProperties': 'entity properties',
    'SchedulerLinkActivation': 'schedule links', 'fightconfiguration_surrendering': 'fight configuration', 'fightconfiguration_indifference': 'fight configuration',
    'disabledirectionschatsoul': 'dialogue direction', 'AddInterruptConceptNode': 'AI interrupt', 'InstantSendMessage': 'AI message',
    'LinkEffectNoData': 'effect link', 'expectedeffect': 'expected-effect marker', 'TriggerSequence': 'runs a sequence', 'switchdialog': 'dialogue switch',
}
TRIGGER = {
    'AreaTrigger': 'player in an area', 'DistanceTrigger': 'player/NPC within a distance', 'SoulDeathTrigger': 'a soul died', 'HasteTrigger': 'developer trigger',
    'ProfileStateTrigger': 'profile state', 'AIConceptSignalTrigger': 'an AI concept signal', 'InteractionTriggerNode': 'an interaction',
    'SpeakTrigger': 'a dialogue line spoken', 'PatchTrigger': 'a patch/version trigger', 'itemclasstrigger_playerinventory': 'an item class in the pack',
    'ItemDescriptorTrigger': 'an item descriptor', 'areatrespassleveleffect': 'trespass area', 'Watch': 'watches a value', 'SceneFinishedWaiter': 'a scene ended',
    'SkaldBoxProbe': 'a probe volume', 'HasItems_Soul': 'a soul holds items',
}
PER_PLAYER = {
    'AddReward': 'stat/skill/story XP (native XP observer, Session 10) and items (reward window)', 'AddQuestItem': 'a quest item to the player',
    'AddSoulDefaultItem': 'default items for a soul', 'AddStashDefaultItem': 'default items for a stash', 'BuffEffect': 'a buff on the player',
    'healing': 'heals', 'payment': 'money', 'DisplayTutorial': 'a tutorial popup', 'SaveGame': 'an autosave (each machine saves its own)',
    'CutsceneHandler': 'a cutscene (the cutscene gate)', 'PlayTrackView': 'a track view',
}
STRUCTURE = {'Function', 'State', 'Output', 'MakeArray', 'IfFunction', 'If', 'MemberFunction', 'Switch', 'Timer', 'Select', 'ForEach', 'ElementAt',
             'ContainsElement', 'EventFunction', 'EventMemberFunction', 'constants', 'konstanty', 'streamprofileshandling'}


def census(mod_dir):
    z = zipfile.ZipFile(Path(mod_dir) / 'Data' / 'Scripts.pak')
    tags, files = collections.Counter(), 0
    for e in z.infolist():
        f = e.filename
        if not f.lower().startswith('quests/final/barbora/') or not f.endswith('.xml'):
            continue
        files += 1
        m = re.search(r'<Nodes>(.*)</Nodes>', z.read(e).decode('utf-8', 'replace'), re.S)
        if m:
            tags.update(re.findall(r'^\t{4}<([A-Za-z0-9_]+) Name="', m.group(1), re.M))
    return files, tags


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--mod-dir', default=r'E:\SteamLibrary\steamapps\common\KCD2Mod')
    ap.add_argument('--out')
    a = ap.parse_args()
    files, tags = census(a.mod_dir)
    groups = {'consumer': CONSUMER, 'trigger': TRIGGER, 'per-player': PER_PLAYER}
    rows = collections.defaultdict(list)
    for tag, n in tags.most_common():
        g = next((name for name, d in groups.items() if tag in d), None) or ('structure' if tag in STRUCTURE else 'other')
        rows[g].append((tag, n, groups.get(g, {}).get(tag, '')))
    total = sum(tags.values())
    out = [f'# Quest effects: who runs them (generated by tools/Audit-QuestEffects.py)', '',
           f'{files} quest graph files, {total} nodes. Read-only census of Scripts.pak; classification by class name (a reading aid).', '']
    for g in ('consumer', 'trigger', 'per-player', 'structure', 'other'):
        n = sum(r[1] for r in rows[g])
        out += [f'## {g}: {n} nodes ({100 * n / total:.1f}%), {len(rows[g])} classes', '']
        if g == 'other':
            out += ["Almost all of these are instances of OTHER graphs (the class name is a quest sub-graph's own name); what they contain is counted in their own file.", '']
        out += ['| class | nodes | what |', '|---|---|---|']
        out += [f'| {t} | {c} | {w} |' for t, c, w in rows[g][:40]]
        if len(rows[g]) > 40:
            out.append(f'| ... | {sum(r[1] for r in rows[g][40:])} | {len(rows[g]) - 40} more classes |')
        out.append('')
    text = '\n'.join(out)
    if a.out:
        Path(a.out).write_text(text, encoding='utf-8', newline='\n')
        print('wrote', a.out)
    else:
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
        print(text)


if __name__ == '__main__':
    main()
