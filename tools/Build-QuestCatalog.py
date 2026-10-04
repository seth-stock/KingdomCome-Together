# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""
WO-156: every quest root of the game -- main, side, task (micro), activity, event -- from the Modding Tools' Scripts.pak,
with its English title and the cutscene census's evidence about it, so the story-lock table (StorySections.cs) can be
checked against ALL of them and not only the 32 main quests.

Reads (never writes) <KCD2Mod>\\Data\\Scripts.pak and <KCD2Mod>\\Localization\\English_xml.pak, and
docs/WO-149-cutscene-census.csv. Writes docs/WO-156-quest-catalog.csv (one row per quest root, for review) and
dotnet/KcdMp.Client/QuestCatalog.g.cs (the same, as a table the tests and the agent can read).

    python tools\\Build-QuestCatalog.py [--mod-dir E:\\SteamLibrary\\steamapps\\common\\KCD2Mod] [--check]

--check recomputes in memory and fails if either file differs (the repo's drift check).
"""
import argparse, csv, hashlib, html, io, os, re, sys, zipfile, collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_OUT = os.path.join(ROOT, 'docs', 'WO-156-quest-catalog.csv')
CS_OUT = os.path.join(ROOT, 'dotnet', 'KcdMp.Client', 'QuestCatalog.g.cs')
CENSUS = os.path.join(ROOT, 'docs', 'WO-149-cutscene-census.csv')
MD_OUT = os.path.join(ROOT, 'docs', 'WO-156-quest-gating-table.md')
PLAN = os.path.join(ROOT, 'docs', 'WO-156-quest-gating-plan.csv')
TIERS = ('open', 'mixed', 'rails')

# Folders under Barbora/<level>/ that belong to a DLC (the game ships them in the base paks; entitlement switches them on).
DLC_FOLDERS = {'klaster': 'Mysteries of the Monastery', 'legacy_of_the_forge': 'Legacy of the Forge',
               'brushes_with_death_trosecko': 'Brushes with Death', 'brushes_with_death_kutnohorsko': 'Brushes with Death',
               'preorder_bonus': 'preorder bonus'}
# Quest roots the agent's own rule already marks RequiredDLC (Wo137Rules.DlcQuests).
DLC_NAMES = {'navstevaLekare': 'Mysteries of the Monastery', 'katuvSleh': 'Legacy of the Forge', 'zavodniPodkovy': 'a DLC'}


def find_mod_dir(arg):
    if arg:
        return arg
    for lib in ('E:\\SteamLibrary', 'C:\\Program Files (x86)\\Steam', 'D:\\SteamLibrary', 'F:\\SteamLibrary'):
        p = os.path.join(lib, 'steamapps', 'common', 'KCD2Mod')
        if os.path.isdir(p):
            return p
    sys.exit('KCD2Mod not found; pass --mod-dir')


def kind_of(code, typ):
    c = code[0]
    return {'M': 'main', 'S': 'side', 'U': 'task', 'A': 'activity', 'E': 'event'}.get(c, 'other')


def load(mod):
    zp = os.path.join(mod, 'Data', 'Scripts.pak')
    lp = os.path.join(mod, 'Localization', 'English_xml.pak')
    pak_hash = hashlib.sha256(open(zp, 'rb').read()).hexdigest()
    z = zipfile.ZipFile(zp)
    root_rx = re.compile(r'<Quest\s[^>]*\bProductionCode="[^"]*"[^>]*>')
    attr = lambda s, k: (re.search(r'\b' + k + r'="([^"]*)"', s) or [None, ''])[1]
    quests = []
    for n in sorted(z.namelist()):
        if not (n.startswith('Quests/Final/Barbora/') and n.endswith('.xml')):
            continue
        t = z.read(n).decode('utf-8', 'ignore')
        for m in root_rx.finditer(t):
            s = m.group(0)
            rel = n[len('Quests/Final/Barbora/'):]
            parts = rel.split('/')
            level = parts[0]
            folders = parts[1:-1]
            qn = re.search(r'qname_[A-Za-z0-9_]+', t)
            quests.append(dict(code=attr(s, 'ProductionCode'), name=attr(s, 'Name'), type=attr(s, 'Type'),
                               difficulty=attr(s, 'Difficulty'), level=level, folders='/'.join(folders),
                               qname=qn.group(0) if qn else '', file=rel))
    titles = {}
    if os.path.exists(lp):
        lz = zipfile.ZipFile(lp)
        lx = lz.read('text_ui_quest.xml').decode('utf-8', 'ignore')
        for rm in re.finditer(r'<Row>(.*?)</Row>', lx, re.S):
            cells = re.findall(r'<Cell>(.*?)</Cell>', rm.group(1), re.S)
            if len(cells) >= 2 and cells[0].startswith('qname_'):
                titles[cells[0]] = html.unescape(cells[-1])
    for q in quests:
        q['title'] = clean_title(titles.get(q['qname'], ''))
        q['kind'] = kind_of(q['code'], q['type'])
        top = q['folders'].split('/')[0] if q['folders'] else ''
        dlc = DLC_FOLDERS.get(q['level']) or DLC_FOLDERS.get(top) or DLC_NAMES.get(q['name']) or ''
        q['dlc'] = dlc
    return quests, pak_hash


def clean_title(s):
    """The localization pak has curly quotes and ellipses that arrive as U+FFFD: an apostrophe inside a title, an ellipsis at its end."""
    if s.endswith('\ufffd'):
        s = s[:-1] + '\u2026'
    return s.replace('\ufffd', "'")


def load_plan():
    """docs/WO-156-quest-gating-plan.csv: the hand-maintained answer for EVERY quest root (open | mixed | rails)."""
    plan = {}
    with open(PLAN, encoding='utf-8', newline='') as f:
        for r in csv.DictReader(f):
            if r['code'] in plan:
                sys.exit('plan: duplicate code ' + r['code'])
            plan[r['code']] = r
    return plan


def census_stats():
    st = collections.defaultdict(lambda: collections.Counter())
    if not os.path.exists(CENSUS):
        return st
    with open(CENSUS, encoding='utf-8', newline='') as f:
        for r in csv.DictReader(f):
            code = r['quest']
            kind = r['kind']
            pe = (r.get('player_effects') or '').lower()
            s = st[code]
            s['rows'] += 1
            if re.search(r'input (is )?off|input locked|filterinput', pe): s['input'] += 1
            if 'lock' in pe: s['lock'] += 1
            if kind == 'setpiece_playerswitch': s['switch'] += 1
            if kind == 'setpiece_fight': s['fight'] += 1
            if kind == 'setpiece_escort_follow': s['escort'] += 1
            if kind == 'setpiece_teleport': s['teleport'] += 1
            if kind == 'rendered_video': s['video'] += 1
            if kind == 'fasttravel_cutscene': s['fasttravel'] += 1
            if kind == 'skiptime_cutscene': s['skiptime'] += 1
            if kind == 'setpiece_bedscene': s['bed'] += 1
            if kind == 'forced_dialogue': s['forced'] += 1
            if kind in ('ingame_sequence', 'fader_cutscene'): s['scene'] += 1
            if kind == 'surrender': s['surrender'] += 1
    return st


FIELDS = ['code', 'kind', 'type', 'name', 'level', 'folders', 'title', 'dlc', 'qname', 'tier', 'period', 'why', 'markers', 'order', 'source', 'note', 'rows', 'input', 'lock', 'switch', 'fight',
          'escort', 'teleport', 'video', 'fasttravel', 'skiptime', 'bed', 'forced', 'scene', 'surrender']


def build(mod):
    quests, pak_hash = load(mod)
    st = census_stats()
    plan = load_plan()
    have = {q['code'] for q in quests}
    if have != set(plan):
        sys.exit('plan and game data disagree: no plan for %s; plan rows with no quest %s' % (sorted(have - set(plan)), sorted(set(plan) - have)))
    for c, p in plan.items():
        if p['tier'] not in TIERS: sys.exit('plan: %s has tier %r' % (c, p['tier']))
        if p['tier'] != 'open' and not (p['why'] and p['period']): sys.exit('plan: %s is %s but has no why/period' % (c, p['tier']))
        if p['tier'] == 'open' and (p['period'] or p['markers']): sys.exit('plan: %s is open but carries a period/markers' % c)
        if p['source'] not in ('online', 'data', 'online+data'): sys.exit('plan: %s has source %r' % (c, p['source']))
    mains = [c for c in plan if c[0] == 'M']
    if sorted(int(plan[c]['order']) for c in mains) != list(range(1, len(mains) + 1)): sys.exit('plan: the main quests are not numbered 1..%d' % len(mains))
    byperiod = collections.defaultdict(set)
    for c, p in plan.items():
        if p['period']: byperiod[p['period']].add(p['why'])
    for per, whys in byperiod.items():
        if len(whys) != 1: sys.exit('plan: period %s has more than one "why": %s' % (per, sorted(whys)))
    for c, p in plan.items():
        if p['period'] and p['period'] not in plan: sys.exit('plan: %s names period %s which is not a quest code' % (c, p['period']))
        if p['period'] and plan[p['period']]['period'] != p['period']: sys.exit('plan: period %s must be named by its own first section' % p['period'])
    quests.sort(key=lambda q: (q['kind'] != 'main', q['code'][0], int(re.match(r'\d+', q['code'][1:]).group(0)) if re.match(r'\d+', q['code'][1:]) else 0, q['code']))
    rows = []
    for q in quests:
        s = st.get(q['code'], collections.Counter())
        r = {k: q.get(k, '') for k in ('code', 'kind', 'type', 'name', 'level', 'folders', 'title', 'dlc', 'qname')}
        for k in ('tier', 'period', 'why', 'markers', 'order', 'source', 'note'):
            r[k] = plan[q['code']][k]
        for k in FIELDS[16:]:
            r[k] = s.get(k, 0)
        rows.append(r)
    return rows, pak_hash


def csv_text(rows):
    buf = io.StringIO(newline='')
    w = csv.DictWriter(buf, fieldnames=FIELDS, lineterminator='\n')
    w.writeheader()
    for r in rows:
        w.writerow(r)
    return buf.getvalue()


def cs_text(rows, pak_hash):
    esc = lambda s: str(s).replace('\\', '\\\\').replace('"', '\\"')
    out = io.StringIO()
    out.write('// <auto-generated> tools/Build-QuestCatalog.py -- do not edit by hand. </auto-generated>\n')
    out.write('// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only\n')
    out.write('// GPLv3 section 7 additional terms: NOTICE. This project\'s own code only; Kingdom Come: Deliverance II and its\n')
    out.write('// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.\n')
    out.write('// Source: Quests/Final in Scripts.pak sha256 %s; titles from English_xml.pak (Warhorse\'s, cited as identifiers only).\n' % pak_hash)
    out.write('namespace KcdMp.Client;\n\n')
    out.write('/// <summary>WO-156: one quest root of the game, as shipped (docs/WO-156-quest-catalog.csv).</summary>\n')
    out.write('public sealed record QuestRoot(string Code, string Kind, string Name, string Level, string Folders, string Title, string Dlc,\n')
    out.write('                                string Tier, string Period, string Why, string Markers, int Order,\n')
    out.write('                                int Input, int Switch, int Fight, int Escort, int Teleport, int Video);\n\n')
    out.write('public static partial class QuestCatalog\n{\n')
    out.write('    /// <summary>Every quest root under Quests/Final: %d.</summary>\n' % len(rows))
    out.write('    public static readonly QuestRoot[] All =\n    {\n')
    for r in rows:
        out.write('        new("%s", "%s", "%s", "%s", "%s", "%s", "%s", "%s", "%s", "%s", "%s", %d, %d, %d, %d, %d, %d, %d),\n' % (
            esc(r['code']), esc(r['kind']), esc(r['name']), esc(r['level']), esc(r['folders']), esc(r['title']), esc(r['dlc']),
            esc(r['tier']), esc(r['period']), esc(r['why']), esc(r['markers']), int(r['order'] or 0),
            r['input'], r['switch'], r['fight'], r['escort'], r['teleport'], r['video']))
    out.write('    };\n}\n')
    return out.getvalue()


def md_text(rows):
    """The review table of every quest root (docs/WO-156-quest-gating-table.md), rendered from the plan and the game's data."""
    esc = lambda s: str(s).replace('|', '\\|')
    out = io.StringIO()
    out.write('# WO-156 -- every quest of the game and how the co-op gating treats it\n\n')
    out.write("Generated by `tools/Build-QuestCatalog.py` from the game's quest data (Scripts.pak) and `docs/WO-156-quest-gating-plan.csv`; do not edit by hand.\n")
    out.write("Quest titles are Warhorse's, cited as identifiers only. Unofficial; not affiliated with or endorsed by Warhorse Studios or PLAION.\n\n")
    out.write("Tiers: **rails** = staged for its whole run (a period for the friend's choice, the tether); **mixed** = scripted stretches alternate with free ones (a period, no tether); **open** = open-world content (no lock; its scenes are the cutscene notice's).\n")
    out.write('Source: **online** = the guides named in docs/WO-156-quest-gating.md; **data** = the cutscene census / the quest\'s own modules; **online+data** = both.\n\n')
    order = [('main', "The 32 main quests, in the game's order"), ('side', 'Side quests'), ('task', 'Tasks'), ('activity', 'Activities'), ('event', 'Events')]
    for kind, head in order:
        sub = [r for r in rows if r['kind'] == kind]
        if kind == 'main':
            sub.sort(key=lambda r: int(r['order']))
        out.write('## %s (%d)\n\n' % (head, len(sub)))
        out.write('| # | Code | Title | Level | Tier | Period / why | Source | Evidence and notes |\n|---:|---|---|---|---|---|---|---|\n')
        for r in sub:
            ev = []
            for k, lab in (('input', 'input locks'), ('switch', 'player switches'), ('fight', 'fights'), ('escort', 'escort/follow'), ('teleport', 'teleports'), ('video', 'videos')):
                if int(r[k]):
                    ev.append('%s %s' % (r[k], lab))
            note = r['note'] + (' [census: ' + ', '.join(ev) + ']' if ev else '')
            title = r['title'] + (' (' + r['dlc'] + ')' if r['dlc'] else '')
            per = (r['why'] + (' [period ' + r['period'] + ']' if r['period'] and r['period'] != r['code'] else '')) if r['tier'] != 'open' else ''
            out.write('| %s | %s | %s | %s | %s | %s | %s | %s |\n' % (r['order'] or '', r['code'], esc(title), r['level'], r['tier'], esc(per), r['source'], esc(note)))
        out.write('\n')
    return out.getvalue()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--mod-dir')
    ap.add_argument('--check', action='store_true')
    a = ap.parse_args()
    mod = find_mod_dir(a.mod_dir)
    rows, pak_hash = build(mod)
    c, s, m = csv_text(rows), cs_text(rows, pak_hash), md_text(rows)
    kinds = collections.Counter((r['kind'], bool(r['dlc'])) for r in rows)
    print('quest roots: %d  %s' % (len(rows), dict(kinds)))
    print('tiers: %s' % dict(collections.Counter(r['tier'] for r in rows)))
    if a.check:
        rd = lambda q: open(q, encoding='utf-8', newline='').read().replace('\r\n', '\n')
        ok = rd(CSV_OUT) == c and rd(CS_OUT) == s and rd(MD_OUT) == m
        print('catalog files match the game data' if ok else 'DRIFT: the catalog files differ from the game data')
        sys.exit(0 if ok else 1)
    open(CSV_OUT, 'w', encoding='utf-8', newline='').write(c)
    open(CS_OUT, 'w', encoding='utf-8', newline='').write(s)
    open(MD_OUT, 'w', encoding='utf-8', newline='').write(m)
    print('wrote', CSV_OUT, ',', CS_OUT, 'and', MD_OUT)


if __name__ == '__main__':
    main()
