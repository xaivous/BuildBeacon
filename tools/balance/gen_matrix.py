"""Build the trophy x material balance matrix (HTML) from the default rules in BeaconConfig.cs.

Run from anywhere:  uv run --no-project python tools/balance/gen_matrix.py
Writes balance_matrix.html next to this script. The trophy list and biomes (B), the rare set and the targets are in
matrix_data.py; the rules come from DefaultBossRules and DefaultMobRules, so rerun after changing them.
"""
import html
import os
from collections import OrderedDict

from matrix_data import *  # noqa: F401,F403 (rules, key, PRETTY, CONSTRUCTION, RARE_DROPS, RARE, B, LEVEL_PCT, ROOT)

OUT = os.path.join(HERE, "balance_matrix.html")

boss_rules = OrderedDict()
names = {}
for trophy, material in rules("DefaultBossRules"):
    boss_rules.setdefault(trophy, []).append(key(material))
    names.setdefault(key(material), PRETTY.get(key(material), material))
mob = OrderedDict()
for parts in rules("DefaultMobRules"):
    if len(parts) != 3:
        continue
    trophy, material, level = parts
    mob.setdefault(trophy, OrderedDict())[key(material)] = int(level)
    names.setdefault(key(material), PRETTY.get(key(material), material))

known = {t for rows in B.values() for t, *_ in rows}
missing = [t for t in list(boss_rules) + list(mob) if t not in known]
assert not missing, missing

# Columns: materials each boss frees, in progression order, then materials only creature trophies discount,
# in the order their trophies' biomes come.
boss_by_trophy = {t: (n, biome) for biome, rows in B.items() for t, n, kind, *_ in rows if kind == "boss"}
groups = []
seen = set()
for trophy, mats in boss_rules.items():
    cols = [m for m in mats if m not in seen]
    seen.update(cols)
    groups.append((f"Freed by {boss_by_trophy[trophy][0]}", cols))
creature_only = []
for biome, rows in B.items():
    for t, *_ in rows:
        for m in mob.get(t, {}):
            if m not in seen:
                seen.add(m)
                creature_only.append(m)
groups.append(("Creature discounts only", creature_only))
columns = [m for _, cols in groups for m in cols]
freed_by = {m: boss_by_trophy[t][0] for t, mats in boss_rules.items() for m in mats}

col_levels = {m: sum(r.get(m, 0) for r in mob.values()) for m in columns}
col_common = {m: sum(r.get(m, 0) for t, r in mob.items() if t not in RARE) for m in columns}
col_sources = {m: sum(1 for r in mob.values() if m in r) for m in columns}


TOP = len(LEVEL_PCT)


def targets(m):
    """(level wanted from common trophies, level wanted with rare ones), None where there is no target."""
    if col_levels[m] == 0:
        return None, None  # freed by a boss only
    if m in CONSTRUCTION:
        return 2, 3
    if m in POWERFUL:
        return None, None  # capped instead (cap)
    if m in RARE_DROPS:
        return None, 1
    return 1, None


def cap(m):
    """The most levels a material may reach with every creature trophy: 2 for powerful ones, else the top level."""
    return POWERFUL_CAP if m in POWERFUL else TOP


def lv_class(lv):
    """The legend colour for a level: orange, yellow, green (lv2 to lv4) spread over the levels there are, as in the
    mod's panel, so the top is always green."""
    lv = max(1, min(lv, TOP))
    return f"lv{2 + round((lv - 1) * 2 / (TOP - 1)) if TOP > 1 else 4}"


problems = []
for trophy, mats in mob.items():
    for m, lv in mats.items():
        if lv != 1:
            problems.append(f"{trophy} gives {names[m]} {lv} levels; every creature rule gives 1")
for m in columns:
    want_common, want_all = targets(m)
    if want_common is not None and col_common[m] < want_common:
        problems.append(f"{names[m]}: {col_common[m]} from common trophies, want {want_common}")
    if want_all is not None and col_levels[m] < want_all:
        problems.append(f"{names[m]}: {col_levels[m]} with rare trophies, want {want_all}")
    if col_levels[m] > cap(m):
        problems.append(f"{names[m]}: {col_levels[m]} with rare trophies, above its cap of {cap(m)}")

n_troph = len(known)
n_with = len(set(boss_rules) | set(mob))
n_mob_rules = sum(len(r) for r in mob.values())
n_boss_rules = sum(len(v) for v in boss_rules.values())

E = html.escape
out = []
w = out.append
w("""<title>Trophy Balance Matrix</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Alegreya+SC:wght@500;700&family=Alegreya+Sans:wght@400;500;700&family=JetBrains+Mono:wght@500;700&display=swap">
<style>
:root{
  --ground:#EEF0EE; --surface:#FAFBFA; --ink:#1B1F22; --muted:#5F676C; --line:#D3D8D6; --line-strong:#AEB6B3;
  --accent:#3E6FC4; --band:#E2E6E4; --hot:rgba(62,111,196,.10);
  --free-bg:#2A2F33; --free-ink:#F1E3B3;
  --l1:#D93829; --l2:#F2851F; --l3:#F5CC2E; --l4:#4FB940; --l5:#4794FF;
  --l-ink-dark:#1B1F22; --l-ink-light:#FFFFFF;
  --display:"Alegreya SC", "Palatino Linotype", Georgia, serif;
  --body:"Alegreya Sans", "Segoe UI", system-ui, sans-serif;
  --mono:"JetBrains Mono", ui-monospace, "Cascadia Mono", Consolas, monospace;
}
@media (prefers-color-scheme: dark){
  :root:not([data-theme="light"]){
    color-scheme:dark;
    --ground:#121518; --surface:#1A1E22; --ink:#E6E9EA; --muted:#98A1A6; --line:#2C3237; --line-strong:#434B51;
    --accent:#7FA8F0; --band:#20262B; --hot:rgba(127,168,240,.13);
    --free-bg:#F1E3B3; --free-ink:#1B1F22;
  }
}
:root[data-theme="dark"]{
  color-scheme:dark;
  --ground:#121518; --surface:#1A1E22; --ink:#E6E9EA; --muted:#98A1A6; --line:#2C3237; --line-strong:#434B51;
  --accent:#7FA8F0; --band:#20262B; --hot:rgba(127,168,240,.13);
  --free-bg:#F1E3B3; --free-ink:#1B1F22;
}
body{background:var(--ground);color:var(--ink);font-family:var(--body);font-size:15px;line-height:1.5}
.wrap{padding-inline:clamp(16px,3vw,40px);padding-block:28px 48px;display:flex;flex-direction:column;gap:20px}
h1{font-family:var(--display);font-weight:700;font-size:clamp(26px,3.4vw,38px);line-height:1.1;margin:0;letter-spacing:.01em;text-wrap:balance}
.lede{margin:0;max-width:68ch;color:var(--muted)}
.lede b{color:var(--ink);font-weight:500}
.facts{display:flex;flex-wrap:wrap;gap:8px 22px;font-family:var(--mono);font-size:12.5px;color:var(--muted)}
.facts span b{color:var(--ink);font-weight:700}
.bar{display:flex;flex-wrap:wrap;align-items:center;gap:12px 24px}
.legend{display:flex;flex-wrap:wrap;gap:6px;align-items:center}
.legend .chip{display:inline-flex;align-items:center;gap:6px;font-family:var(--mono);font-size:12px;padding:3px 8px 3px 4px;border-radius:4px;background:var(--surface);border:1px solid var(--line)}
.legend .sw{display:inline-grid;place-items:center;width:20px;height:20px;border-radius:3px;font-weight:700;font-size:11px}
.toggle{display:inline-flex;align-items:center;gap:8px;font-size:14px;cursor:pointer;user-select:none}
.toggle input{width:16px;height:16px;accent-color:var(--accent)}
.toggle input:focus-visible{outline:2px solid var(--accent);outline-offset:2px}
.scroller{overflow:auto;max-height:78vh;border:1px solid var(--line-strong);border-radius:6px;background:var(--surface)}
table{border-collapse:separate;border-spacing:0;font-variant-numeric:tabular-nums}
col.hot{background:var(--hot)}
th,td{border-right:1px solid var(--line);border-bottom:1px solid var(--line);padding:0}
thead th{position:sticky;background:var(--surface);z-index:2}
thead tr.groups th{top:0;height:30px;font-family:var(--display);font-weight:500;font-size:13px;color:var(--muted);text-align:left;padding-inline:8px;white-space:nowrap;border-bottom:1px solid var(--line-strong)}
thead tr.mats th{top:31px;height:132px;vertical-align:bottom;border-bottom:1px solid var(--line-strong)}
thead tr.mats th.m span{display:block;writing-mode:vertical-rl;transform:rotate(180deg);white-space:nowrap;font-size:13px;font-weight:500;padding:8px 0;margin-inline:auto;line-height:1}
thead th.corner{left:0;z-index:4;text-align:left;vertical-align:bottom;padding:8px 12px;font-family:var(--mono);font-size:11px;font-weight:500;color:var(--muted);letter-spacing:.04em;text-transform:uppercase}
th.rowhead{position:sticky;left:0;z-index:1;background:var(--surface);text-align:left;padding:4px 12px;min-width:210px;max-width:210px;font-weight:500}
th.rowhead .n{display:block;font-size:14.5px;line-height:1.2}
th.rowhead .p{display:block;font-family:var(--mono);font-size:10.5px;color:var(--muted);line-height:1.3}
th.rowhead .tag{font-family:var(--mono);font-size:10px;font-weight:700;letter-spacing:.05em;text-transform:uppercase;color:var(--accent);margin-left:6px}
th.rowhead .dag{color:var(--accent);margin-left:3px;font-weight:700}
td.c{width:30px;min-width:30px;height:34px;text-align:center;font-family:var(--mono);font-size:12.5px;font-weight:700}
td.lv1{background:var(--l1);color:var(--l-ink-light)}
td.lv2{background:var(--l2);color:var(--l-ink-dark)}
td.lv3{background:var(--l3);color:var(--l-ink-dark)}
td.lv4{background:var(--l4);color:var(--l-ink-dark)}
td.lv5{background:var(--l5);color:var(--l-ink-light)}
td.free{background:var(--free-bg);color:var(--free-ink);font-family:var(--display);font-size:10px;letter-spacing:.04em}
tr.biome th{position:sticky;left:0;z-index:1;background:var(--band);text-align:left;padding:6px 12px;font-family:var(--display);font-weight:700;font-size:15px;letter-spacing:.03em;border-bottom:1px solid var(--line-strong)}
tr.biome td{background:var(--band);border-bottom:1px solid var(--line-strong)}
tr.t:hover th.rowhead{background:color-mix(in srgb,var(--surface) 80%,var(--accent))}
tr.t:hover td.c:not(.lv1):not(.lv2):not(.lv3):not(.lv4):not(.lv5):not(.free){background:var(--hot)}
td.sum,th.sumh{font-family:var(--mono);font-size:12px;color:var(--muted);text-align:center;min-width:54px;padding-inline:6px}
th.sumh{font-weight:500;font-size:11px;letter-spacing:.04em;text-transform:uppercase}
td.note{font-size:13px;color:var(--muted);padding:4px 12px;min-width:250px;white-space:nowrap}
th.rowhead .tag.rare{color:var(--muted)}
tfoot td,tfoot th{position:sticky;background:var(--surface);z-index:2;height:34px;box-sizing:border-box}
tfoot tr.f1>*{bottom:68px;border-top:1px solid var(--line-strong)}
tfoot tr.f2>*{bottom:34px}
tfoot tr.f3>*{bottom:0}
tfoot th.rowhead{z-index:3;font-family:var(--mono);font-size:10.5px;font-weight:500;letter-spacing:.04em;text-transform:uppercase;color:var(--muted);padding-block:0;line-height:1.2}
tfoot td.c{font-weight:500;font-size:11px}
tfoot td.c:not(.lv1):not(.lv2):not(.lv3):not(.lv4):not(.lv5){color:var(--muted)}
tfoot td.c.miss{outline:2px dashed var(--l1);outline-offset:-3px}
tfoot td.t{font-size:10px;letter-spacing:-.02em}
tfoot td.t.build{color:var(--ink);font-weight:700}
.notes{display:grid;gap:6px;max-width:80ch;font-size:14px;color:var(--muted)}
.notes p{margin:0}
.notes b{color:var(--ink);font-weight:500}
body.only-rules tr.t.norules{display:none}
@media (prefers-reduced-motion: reduce){*{transition:none!important}}
</style>
<div class="wrap">
<header style="display:flex;flex-direction:column;gap:10px">
<h1>Trophy Balance Matrix</h1>
<p class="lede">Every trophy in the game against every material the Build Beacon discounts. A number is the <b>discount level</b> that creature trophy gives the material; <b>Free</b> marks the materials a boss trophy covers (free by default, or 95% off with the BossPercent setting at 95). Levels from different trophies add up in a beacon, to the top level of 3. Built from the mod's default rules.</p>
""")
w(f"""<div class="facts"><span><b>{n_troph}</b> trophies</span><span><b>{n_with}</b> with rules</span><span><b>{len(columns)}</b> materials</span><span><b>{n_boss_rules}</b> boss rules</span><span><b>{n_mob_rules}</b> creature rules</span></div>
</header>
<div class="bar">
<div class="legend" aria-label="Legend">
""")
for i, pct in enumerate(LEVEL_PCT, 1):
    mult = 100 / (100 - pct)
    mtxt = f"{mult:.2f}".rstrip("0").rstrip(".")
    w(f'<span class="chip"><span class="sw" style="background:var(--{lv_class(i).replace("lv", "l")});color:var({"--l-ink-light" if lv_class(i) in ("lv1", "lv5") else "--l-ink-dark"})">{i}</span>{pct}% · {mtxt}x</span>')
w('<span class="chip"><span class="sw" style="background:var(--free-bg);color:var(--free-ink);font-family:var(--display);font-size:8px">Free</span>boss</span>')
w("""</div>
<label class="toggle" for="only-rules"><input type="checkbox" id="only-rules"> Only trophies with rules</label>
</div>
<div class="scroller">
<table id="matrix">
<colgroup><col>""")
for _ in columns:
    w("<col>")
w("<col><col><col></colgroup>\n<thead><tr class=\"groups\"><th class=\"corner\" rowspan=\"2\">Trophy<br>item name</th>")
for label, cols in groups:
    if cols:
        w(f'<th colspan="{len(cols)}">{E(label)}</th>')
w('<th class="sumh" rowspan="2">Mats</th><th class="sumh" rowspan="2">Levels</th><th class="sumh" rowspan="2" style="text-align:left;padding-left:12px">Notes</th></tr>\n<tr class="mats">')
for m in columns:
    w(f'<th class="m" title="{E(names[m])}"><span>{E(names[m])}</span></th>')
w("</tr></thead>\n<tbody>")

span = len(columns) + 3
for biome, rows in B.items():
    w(f'<tr class="biome"><th>{E(biome)}</th><td colspan="{span}"></td></tr>')
    for trophy, name, kind, note, inferred in rows:
        has = trophy in boss_rules or trophy in mob
        cls = "t" + ("" if has else " norules")
        tag = ('<span class="tag">Boss</span>' if kind == "boss"
               else '<span class="tag rare">Rare</span>' if trophy in RARE else "")
        dag = '<span class="dag" title="Biome inferred">†</span>' if inferred else ""
        w(f'<tr class="{cls}"><th class="rowhead"><span class="n">{E(name)}{dag}{tag}</span><span class="p">{E(trophy)}</span></th>')
        mats = 0
        levels = 0
        for m in columns:
            if trophy in boss_rules and m in boss_rules[trophy]:
                w('<td class="c free" title="Free">Free</td>')
                mats += 1
            elif m in mob.get(trophy, {}):
                lv = mob[trophy][m]
                w(f'<td class="c {lv_class(lv)}" title="{E(name)}: {E(names[m])} +{lv}">{lv}</td>')
                mats += 1
                levels += lv
            else:
                w('<td class="c"></td>')
        w(f'<td class="sum">{mats or ""}</td><td class="sum">{levels or ""}</td><td class="note">{E(note)}</td></tr>')
w("</tbody>\n<tfoot>")


def level_cell(m, lv, want, label):
    if not lv:
        return '<td class="c">·</td>'
    miss = " miss" if (want is not None and lv < want) or lv > cap(m) else ""
    tip = f"{names[m]}: level {lv} {label}" + (f", target {want}" if want is not None else "")
    return f'<td class="c {lv_class(lv)}{miss}" title="{E(tip)}">{lv}</td>'


w('<tr class="f1"><th class="rowhead">Target: common / rare</th>')
for m in columns:
    want_common, want_all = targets(m)
    if want_common is None and want_all is None:
        w('<td class="c t"></td>')
    else:
        text = f"{want_common if want_common is not None else '-'}/{want_all if want_all is not None else '-'}"
        build = " build" if m in CONSTRUCTION else ""
        w(f'<td class="c t{build}" title="{E(names[m])}: target {text}">{text}</td>')
w('<td class="sum"></td><td class="sum"></td><td class="note">Construction 2/3; powerful at most 2; rare-only drops -/1; the rest 1/-</td></tr>')
w('<tr class="f2"><th class="rowhead">From common trophies</th>')
for m in columns:
    w(level_cell(m, col_common[m], targets(m)[0], "from common trophies"))
w('<td class="sum"></td><td class="sum"></td><td class="note">One of each common creature trophy</td></tr>')
w('<tr class="f3"><th class="rowhead">With rare trophies</th>')
for m in columns:
    w(level_cell(m, col_levels[m], targets(m)[1], "with rare trophies too"))
w('<td class="sum"></td><td class="sum"></td><td class="note">One of every creature trophy</td></tr>')
w("</tfoot>\n</table>\n</div>")
w("""<section class="notes">
<p><b>Targets.</b> Every creature rule gives one level, and no material has more than three trophies, so none passes the top level of 3. Construction woods and stones (bold 2/3) should reach level 2 from the common creature trophies of their biome and 3 with a rare one. Powerful materials (metals, cores, crystal, Dvergr parts) stop at level 2. Materials only a rare creature drops need level 1 from that creature's trophy. Every other creature-discounted material should reach level 1 from common trophies, and more where more creatures drop it. The footer's last two rows add the levels as if one of each trophy were slotted in one beacon; a dashed outline marks a miss or a level past the cap. A beacon counts each trophy once unless it is marked stackable (none are by default), and caps the sum at level 3.</p>
<p><b>Rare</b> marks mini-bosses, Hildir's chest bosses, rare spawns and creatures from a single cave or dungeon.</p>
<p><b>†</b> The biome is placed from the game's own names and descriptions (for example, the Moose is "the mighty ruler of the northern forests"), not from spawn data. Worth a check in game.</p>
<p><b>Grouping.</b> Trophies found in several biomes sit in the first one, with the others in Notes. Hildir's chest bosses sit in the biome of their dungeon. Materials are grouped by the boss that frees them, in progression order; the last group is discounted only by creature trophies.</p>
</section>
</div>
<script>
(function(){
  var table=document.getElementById('matrix');
  var cols=table.querySelectorAll('colgroup col');
  var hot=null;
  table.addEventListener('mouseover',function(e){
    var cell=e.target.closest('td,th'); if(!cell||!table.contains(cell)) return;
    var idx=cell.cellIndex; var row=cell.parentElement;
    if(row.classList.contains('biome')||row.classList.contains('groups')) idx=-1;
    var col=idx>0?cols[idx]:null;
    if(col===hot) return; if(hot) hot.classList.remove('hot'); hot=col; if(hot) hot.classList.add('hot');
  });
  table.addEventListener('mouseleave',function(){ if(hot){hot.classList.remove('hot'); hot=null;} });
  var box=document.getElementById('only-rules');
  function apply(){ document.body.classList.toggle('only-rules',box.checked); }
  try{ box.checked=localStorage.getItem('only-rules')==='1'; }catch(_){}
  apply();
  box.addEventListener('change',function(){ apply(); try{ localStorage.setItem('only-rules',box.checked?'1':'0'); }catch(_){} });
})();
</script>
""")
# Non-ASCII characters (·, †) as HTML entities: the page reads correctly whatever encoding a browser assumes
# when the file is opened straight from disk, where there is no charset declaration.
open(OUT, "w", encoding="ascii", newline="\n").write("".join(out).encode("ascii", "xmlcharrefreplace").decode("ascii"))
for p in problems:
    print("off target:", p)
print(f"wrote {OUT}: {n_troph} trophies ({n_with} with rules), {len(columns)} materials in {len([g for g in groups if g[1]])} groups")
