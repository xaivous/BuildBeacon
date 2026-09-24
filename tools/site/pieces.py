"""The "Building Pieces" tab of the site: one card per piece the mod adds, grouped by what it does.

The facts are kept here by hand, with the default config values (a server can change radius, range, slots and the
boss discount). Costs are checked against the recipes in BuildBeacon/BuildBeaconPlugin.cs when the site builds, so a
changed recipe stops the build instead of leaving the page wrong. Icons are the pieces' own icons from the Unity
project, copied next to the page.
"""
import html
import os
import re

E = html.escape

# Item prefab -> the name the game shows. Only the materials the pieces cost.
ITEM_NAMES = {"Stone": "Stone", "Wood": "Wood", "SurtlingCore": "Surtling Core", "Grausten": "Grausten",
              "Crystal": "Crystal"}

GROUPS = [
    {
        "title": "Beacons",
        "intro": "The crafting stations everything else links to. Build either one; holders and racks link to the "
                 "closest beacon in range, of either kind.",
        "pieces": [
            {
                "id": "build-beacon",
                "name": "Build Beacon",
                "kind": "Station",
                "icon": "xai_beacon_icon.png",
                "summary": "A rune-carved stone with a glowing crystal. Pieces built inside its ring cost less.",
                "station": "Workbench",
                "cost": [("Stone", 20), ("SurtlingCore", 1)],
                "facts": [
                    ("Holds", "4 creature trophies in its own alcoves"),
                    ("Radius", "30 m at level 1, 10 m more each level: 100 m at level 8"),
                    ("Level", "1, plus 1 for each linked boss trophy holder and trophy rack"),
                    ("Spacing", "6 m from any other beacon"),
                ],
                "notes": [
                    "Press Use to open its panel: the Trophies tab to slot and remove trophies, the Discounts tab to "
                    "see what each material costs inside the ring.",
                    "Hold a trophy and press Use on it to slot the trophy straight away.",
                    "Its crystal glows, turns and bobs while it counts any trophy.",
                ],
            },
            {
                "id": "great-beacon",
                "name": "Great Beacon",
                "kind": "Station",
                "icon": "xai_great_beacon_icon_128.png",
                "summary": "An 8-metre beacon whose seven alcoves hold the trophies of the bosses you have slain.",
                "station": "Stonecutter",
                "cost": [("Grausten", 20), ("SurtlingCore", 5), ("Crystal", 1)],
                "facts": [
                    ("Holds", "7 boss trophies, each in its own alcove; no creature trophies"),
                    ("Radius", "65 m at level 1, 5 m more each level: 100 m at level 8"),
                    ("Level", "1, plus 1 for each boss in its alcoves, then each linked holder and rack"),
                    ("Spacing", "6 m from any other beacon"),
                ],
                "notes": [
                    "Top row: Eikthyr, the Elder, Bonemass and Moder. Lower row: Yagluth, the Queen and Fader, "
                    "with a great rune on the front in place of an eighth alcove.",
                    "Creature trophies go on trophy racks beside it; boss trophy holders link to it as well.",
                    "Its great crystal turns while lit, and four small crystals circle it, each spinning on its own.",
                ],
            },
        ],
    },
    {
        "title": "Boss trophy holders",
        "intro": "Each holds one boss trophy. The boss's materials become free (or 95% off, if the server chooses) "
                 "inside the linked beacon's radius, and each holder raises the beacon one level.",
        "pieces": [
            {
                "id": "boss-trophy-pillar",
                "name": "Boss Trophy Pillar",
                "kind": "Boss holder",
                "icon": "xai_bossholder_pillar_icon_128.png",
                "summary": "A short stone pillar with an iron hook, for floors and the ground.",
                "station": "Workbench",
                "cost": [("Stone", 10), ("Wood", 5)],
                "facts": [
                    ("Holds", "1 boss trophy, on its hook"),
                    ("Links", "to the closest beacon within 30 m (a yellow thread shows it while placing)"),
                    ("Adds", "1 level to the beacon, trophy or not"),
                ],
                "notes": [
                    "Hold a boss trophy and press Use on it to hang the trophy; press Use again to take it back.",
                    "Each boss counts once per beacon: a second holder with the same boss is not counted.",
                ],
            },
            {
                "id": "boss-trophy-mount",
                "name": "Boss Trophy Mount",
                "kind": "Boss holder",
                "icon": "xai_bossholder_wall_icon_128.png",
                "summary": "A hexagonal stone plaque with a hook, for walls only.",
                "station": "Workbench",
                "cost": [("Stone", 10), ("Wood", 5)],
                "facts": [
                    ("Holds", "1 boss trophy, on its hook"),
                    ("Links", "to the closest beacon within 30 m"),
                    ("Adds", "1 level to the beacon, trophy or not"),
                ],
                "notes": [
                    "Snaps to a wall by its centre and sits on the wall's face, wood and Grausten walls included.",
                    "Lets a hall's walls carry the bosses while the beacon stands elsewhere.",
                ],
            },
        ],
    },
    {
        "title": "Trophy racks",
        "intro": "Each holds four creature trophies for the linked beacon, on top of the beacon's own four, and raises "
                 "the beacon one level.",
        "pieces": [
            {
                "id": "trophy-column",
                "name": "Trophy Column",
                "kind": "Creature rack",
                "icon": "xai_mobrack_column_icon_128.png",
                "summary": "A 2-metre carved column with four alcoves round it, for floors and the ground.",
                "station": "Workbench",
                "cost": [("Wood", 10), ("Stone", 5)],
                "facts": [
                    ("Holds", "4 creature trophies"),
                    ("Links", "to the closest beacon within 30 m"),
                    ("Adds", "4 creature slots and 1 level to the beacon"),
                ],
                "notes": [
                    "Look at an alcove and press Use with a creature trophy to place it; Use on a filled alcove "
                    "takes it back.",
                    "Columns stack: build one on top of another for a taller display.",
                    "A trophy already counted anywhere in the beacon's network is not counted again.",
                ],
            },
            {
                "id": "trophy-panel",
                "name": "Trophy Panel",
                "kind": "Creature rack",
                "icon": "xai_mobrack_wall_icon_128.png",
                "summary": "A 2-metre square stone panel with four alcoves, for walls.",
                "station": "Workbench",
                "cost": [("Wood", 10), ("Stone", 5)],
                "facts": [
                    ("Holds", "4 creature trophies"),
                    ("Links", "to the closest beacon within 30 m"),
                    ("Adds", "4 creature slots and 1 level to the beacon"),
                ],
                "notes": [
                    "Snaps to a wall by its centre; place panels side by side by eye to fill a wall.",
                    "Past the beacon's top level a rack still adds its slots, but no level.",
                ],
            },
        ],
    },
]

ICON_DIR = os.path.join("BuildBeaconUnity", "Assets", "Beacon")


def pieces():
    return [p for g in GROUPS for p in g["pieces"]]


def check_costs(root):
    """Stop the build if a listed cost is not one of the plugin's recipe lines."""
    src = open(os.path.join(root, "BuildBeacon", "BuildBeaconPlugin.cs"), encoding="utf-8").read()
    recipes = set(re.findall(r'new RequirementConfig\("(\w+)",\s*(\d+)', src))
    missing = [f'{p["name"]}: {n} {item}' for p in pieces() for item, n in p["cost"] if (item, str(n)) not in recipes]
    if missing:
        raise SystemExit("pieces.py costs not found in BuildBeaconPlugin.cs recipes: " + "; ".join(missing))


def icons():
    """(source path relative to the repository, published name) for each piece's icon."""
    return [(os.path.join(ICON_DIR, p["icon"]), p["icon"]) for p in pieces()]


def render():
    out = ['<div class="pc-intro">',
           '<h2>Building Pieces</h2>',
           '<p class="lede">Everything the mod adds to the Hammer\'s <b>Misc</b> tab. A <b>beacon</b> is the crafting '
           'station; <b>boss trophy holders</b> and <b>trophy racks</b> link to it like a workbench\'s upgrades, and '
           'each one raises its level and widens its radius. Numbers are the default settings; a server can change '
           'them.</p>',
           '<nav class="pc-jump" aria-label="Pieces">']
    for p in pieces():
        out.append(f'<a href="#piece-{p["id"]}"><img src="img/{E(p["icon"])}" alt="" width="28" height="28">'
                   f'{E(p["name"])}</a>')
    out.append('</nav></div>')
    for g in GROUPS:
        out.append(f'<section class="pc-group"><h2>{E(g["title"])}</h2><p class="pc-group-intro">{E(g["intro"])}</p>'
                   '<div class="pc-grid">')
        for p in g["pieces"]:
            cost = " &middot; ".join(f"<b>{n}</b> {E(ITEM_NAMES[item])}" for item, n in p["cost"])
            facts = [("Cost", cost), ("Built", f"Hammer, <i>Misc</i> tab, near a {E(p['station'])}")]
            facts += [(k, E(v)) for k, v in p["facts"]]
            out.append(f'<article class="piece" id="piece-{p["id"]}">'
                       f'<header class="piece-head"><div class="piece-icon"><img src="img/{E(p["icon"])}" alt="" '
                       f'width="96" height="96"></div><div><p class="piece-kind">{E(p["kind"])}</p>'
                       f'<h3>{E(p["name"])}</h3><p class="piece-sum">{E(p["summary"])}</p></div></header>'
                       '<dl class="piece-facts">'
                       + "".join(f"<div><dt>{k}</dt><dd>{v}</dd></div>" for k, v in facts)
                       + '</dl><ul class="piece-notes">'
                       + "".join(f"<li>{E(n)}</li>" for n in p["notes"])
                       + '</ul></article>')
        out.append('</div></section>')
    return "\n".join(out)
