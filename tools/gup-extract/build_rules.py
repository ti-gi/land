"""
Zone rules for the web map: source/zagreb_gup_zones_v0.json → ../../data/gup/rules.json

The source file was written by hand (with Claude) from the consolidated GUP text (articles 8–23, 32–33),
in plain ASCII. This script restores Croatian diacritics and reshapes it for the map.

    python build_rules.py [--out path]
"""
import argparse, json, re
from pathlib import Path

HERE = Path(__file__).resolve().parent
ap = argparse.ArgumentParser()
ap.add_argument("--src", type=Path, default=HERE / "source" / "zagreb_gup_zones_v0.json")
ap.add_argument("--out", type=Path, default=HERE.parents[1] / "data" / "gup" / "rules.json")
args = ap.parse_args()
d = json.load(open(args.src, encoding='utf-8'))
M = {
 'biciklisticke':'biciklističke','biciklisticki':'biciklistički','crpilista':'crpilišta','djecja':'dječja',
 'drustvena':'društvena','drustvene':'društvene','drustvenih':'društvenih','elektricna':'električna',
 'gradjevina':'građevina','gradjevine':'građevine','gradjevinom':'građevinom','garaze':'garaže',
 'gledalista':'gledališta','igralista':'igrališta','istrazivacki':'istraživački','izgradjenost':'izgrađenost',
 'izletista':'izletišta','izlozbeno':'izložbeno','kuce':'kuće','kupaliste':'kupalište','lezaja':'ležaja',
 'mjesovita':'mješovita','mrtvacnice':'mrtvačnice','mreza':'mreža','obrtnicki':'obrtnički','okolis':'okoliš',
 'parkiralista':'parkirališta','pjesacke':'pješačke','pjesacko':'pješačko','poste':'pošte','potrosnje':'potrošnje',
 'povrsine':'površine','povrsina':'površina','prateci':'prateći','preciscavanje':'pročišćavanje','predskolska':'predškolska',
 'predskolske':'predškolske','predstavnistva':'predstavništva','pretezito':'pretežito','rezima':'režima',
 'rjesenja':'rješenja','sadrzaji':'sadržaji','sadrzajima':'sadržajima','skladisni':'skladišni','skladista':'skladišta',
 'skole':'škole','skolska':'školska','skolske':'školske','spremista':'spremišta','sredista':'središta',
 'stajalista':'stajališta','sume':'šume','tehnoloski':'tehnološki','trgovacke':'trgovačke','trgovacki':'trgovački',
 'trznice':'tržnice','trznica':'tržnica','turisticka':'turistička','ucili':'učili','ulicna':'ulična','uredjaji':'uređaji',
 'uvuceni':'uvučeni','visokoskolske':'visokoškolske','zastite':'zaštite','zastitne':'zaštitne','zastitnim':'zaštitnim',
 'zeljeznicka':'željeznička','zicare':'žičare','filipovica':'Filipovića','horvacanska':'Horvaćanska','hanusa':'Hanuša',
 'tudmana':'Tuđmana','jakusevec':'Jakuševec','markusevec':'Markuševec','sestine':'Šestine','vrapce':'Vrapče',
 'gracani':'Gračani','cucerje':'Čučerje','krvaric':'Krvarić','laniste':'Lanište','zagrebacki':'Zagrebački',
}
def fix(s):
    def rep(m):
        w = m.group(0); r = M.get(w.lower())
        if not r: return w
        return r[0].upper() + r[1:] if w[0].isupper() else r
    return re.sub(r'[A-Za-z]+', rep, s)
SKIP = {'code','group','buildability','tag','article'}
def walk(o, key=None):
    if isinstance(o, dict): return {k: (v if k in SKIP else walk(v, k)) for k, v in o.items()}
    if isinstance(o, list): return [walk(x, key) for x in o]
    if isinstance(o, str): return fix(o)
    return o
zones = {z['code']: walk(z) for z in d['zones']}
out = dict(
  meta=dict(plan=d['meta']['plan'], version=d['meta']['version'],
            note='Permitted uses extracted from the consolidated GUP text (articles 8–23, 32–33). Verify against the official text before legal use.'),
  zones=zones,
  everywhere=walk(d['all_zones_general_provisions']),
  building_types=d['building_types'],
  buildability_classes={k: dict(name_hr=fix(v['name_hr']), zones=v['zones']) for k, v in d['buildability_classes'].items()},
)
args.out.parent.mkdir(parents=True, exist_ok=True)
json.dump(out, open(args.out, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(len(zones), 'zones →', args.out)
