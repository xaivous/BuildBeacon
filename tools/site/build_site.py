"""Build the BuildBeacon GitHub Pages site into site/_build/.

Run from anywhere:  uv run --no-project --with markdown python tools/site/build_site.py
(GitHub Actions runs the same after `pip install markdown`; see .github/workflows/pages.yml.)

The page is site/index.html with these filled in:
- the mod's README (README.md at the root, also the Thunderstore page), rendered to HTML for "The Mod" tab, and the same
  for BuildBeacon/CHANGELOG.md ("Changelog") and ROADMAP.md ("Roadmap");
- the matrix data for the "Discount Matrix" tab: the trophy catalogue by biome, the rare set and the balance targets
  (tools/balance/matrix_data.py) and the default rules in rules-file format, from BuildBeacon/BeaconConfig.cs, so the
  browser parses them with the same code it uses for a server's pasted rules files;
- the "Building Pieces" tab, from tools/site/pieces.py (costs checked against the plugin's recipes), with each piece's
  icon copied from BuildBeaconUnity/Assets/Beacon into img/;
- the version and description from BuildBeacon/Package/manifest.json.
site/site.css and site/matrix.js are copied as they are.
"""
import html
import json
import os
import re
import shutil
import sys

import markdown
from markdown.extensions.toc import slugify

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
SITE = os.path.join(ROOT, "site")
OUT = os.path.join(SITE, "_build")
sys.path.insert(0, os.path.join(ROOT, "tools", "balance"))
import matrix_data as md  # noqa: E402
import pieces  # noqa: E402  (tools/site/pieces.py, next to this script)

REPO_URL = "https://github.com/xaivous/BuildBeacon"  # the public repository
THUNDERSTORE_URL = None  # set once the package is published; the header shows the button only then
DISCORD_URL = "https://discord.gg/7vcX9mujW3"

# Header button icons, inline so they take the button's text colour (currentColor) in both themes.
# GitHub: the Octicons mark-github (MIT). Discord: the Simple Icons mark (CC0).
GITHUB_ICON = ('<svg class="ico" viewBox="0 0 16 16" width="16" height="16" aria-hidden="true" focusable="false">'
               '<path fill="currentColor" d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49'
               '-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66'
               '.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18'
               ' 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75'
               '-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z"/></svg>')
DISCORD_ICON = ('<svg class="ico" viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" focusable="false">'
                '<path fill="currentColor" d="M20.317 4.37a19.79 19.79 0 0 0-4.885-1.515.074.074 0 0 0-.079.037c-.211.375-.445.865-.608'
                ' 1.25a18.27 18.27 0 0 0-5.487 0 12.64 12.64 0 0 0-.617-1.25.077.077 0 0 0-.079-.037A19.74 19.74 0 0 0 3.677 4.37a.07.07'
                ' 0 0 0-.032.027C.533 9.046-.32 13.58.099 18.058a.082.082 0 0 0 .031.056 19.9 19.9 0 0 0 5.993 3.03.078.078 0 0 0'
                ' .084-.028c.462-.63.874-1.295 1.226-1.994a.076.076 0 0 0-.041-.106 13.1 13.1 0 0 1-1.872-.892.077.077 0 0 1-.008-.128'
                'c.126-.094.252-.192.372-.291a.074.074 0 0 1 .078-.01c3.928 1.793 8.18 1.793 12.062 0a.074.074 0 0 1 .078.009c.12.099'
                '.246.198.373.292a.077.077 0 0 1-.006.127 12.3 12.3 0 0 1-1.873.892.077.077 0 0 0-.041.107c.36.698.772 1.362 1.225'
                ' 1.993a.076.076 0 0 0 .084.028 19.84 19.84 0 0 0 6.002-3.03.077.077 0 0 0 .032-.054c.5-5.177-.838-9.674-3.549-13.66'
                'a.061.061 0 0 0-.031-.03zM8.02 15.33c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.956-2.419 2.157-2.419 1.21 0 2.176'
                ' 1.096 2.157 2.42 0 1.333-.956 2.418-2.157 2.418zm7.975 0c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.955-2.419 2.157'
                '-2.419 1.21 0 2.176 1.096 2.157 2.42 0 1.333-.946 2.418-2.157 2.418z"/></svg>')


def header_link(url, icon, label, tip):
    """A header button: icon, label, and a popover (the .tip span, shown on hover and keyboard focus)."""
    return (f'<a class="btn has-tip" href="{html.escape(url)}" rel="noopener">{icon}<span>{html.escape(label)}</span>'
            f'<span class="tip" role="tooltip">{html.escape(tip)}</span></a>')


def markdown_html(*path, id_prefix=""):
    """A Markdown file of the repository as HTML for a tab. Heading ids get id_prefix, so the Changelog's and Roadmap's
    anchors cannot clash with the README's (matrix.js opens the tab holding whatever element a link names)."""
    text = open(os.path.join(ROOT, *path), encoding="utf-8").read()
    # The page header carries the title; drop the file's own H1.
    text = re.sub(r"\A# [^\n]*\n+", "", text)
    # The README indents list continuations and nested lists by 3 spaces, which GitHub and Thunderstore accept but
    # Python-Markdown does not (it wants 4); widen them outside code fences.
    # Python-Markdown also folds a list item that directly follows a paragraph line into that paragraph, so give such
    # items the blank line before them that GitHub does not need.
    item = re.compile(r"^\s*(\d+\.|[-*])\s")
    out, fenced = [], False
    for line in text.split("\n"):
        if line.lstrip().startswith("```"):
            fenced = not fenced
        elif not fenced:
            if re.match(r"^ {3}\S", line):
                line = " " + line
            if item.match(line) and out and out[-1].strip() and not item.match(out[-1]):
                out.append("")
        out.append(line)
    text = "\n".join(out)
    body = markdown.markdown(text, extensions=["tables", "fenced_code", "toc"],
                             extension_configs={"toc": {"permalink": False,
                                                        "slugify": lambda value, sep: id_prefix + slugify(value, sep)}})
    # Tables scroll inside their own box on narrow screens instead of widening the page.
    body = body.replace("<table>", '<div class="table-scroll"><table>').replace("</table>", "</table></div>")
    return body


def rules_text(name):
    """A DefaultBossRules/DefaultMobRules constant as the lines of a rules file: "Trophy | Material [| Level]"."""
    return "\n".join(" | ".join(parts) for parts in md.rules(name)) + "\n"


def matrix_payload():
    biomes = []
    for biome, rows in md.B.items():
        biomes.append({
            "name": biome,
            "trophies": [{"id": t, "name": n, "boss": kind == "boss", "rare": t in md.RARE, "note": note, "inferred": inferred}
                         for t, n, kind, note, inferred in rows],
        })
    return {
        "biomes": biomes,
        "bossRules": rules_text("DefaultBossRules"),
        "mobRules": rules_text("DefaultMobRules"),
        "levelPercents": md.LEVEL_PCT,
        "bossPercent": md.BOSS_PCT,
        "prettyNames": md.PRETTY,
        "construction": sorted(md.CONSTRUCTION),
        "powerful": sorted(md.POWERFUL),
        "powerfulCap": md.POWERFUL_CAP,
        "rareDrops": sorted(md.RARE_DROPS),
    }


def main():
    pieces.check_costs(ROOT)
    manifest = json.load(open(os.path.join(ROOT, "BuildBeacon", "Package", "manifest.json"), encoding="utf-8"))
    page = open(os.path.join(SITE, "index.html"), encoding="utf-8").read()
    links = [header_link(DISCORD_URL, DISCORD_ICON, "Discord", "Join our Dev Community"),
             header_link(REPO_URL, GITHUB_ICON, "GitHub", "See source code")]
    if THUNDERSTORE_URL:
        links.insert(0, f'<a class="btn primary" href="{THUNDERSTORE_URL}">Thunderstore</a>')
    data = json.dumps(matrix_payload(), ensure_ascii=True, separators=(",", ":")).replace("</", "<\\/")
    fills = {
        "{{VERSION}}": html.escape(manifest["version_number"]),
        "{{LINKS}}": "\n".join(links),
        "{{README}}": markdown_html("README.md"),
        "{{CHANGELOG}}": markdown_html("BuildBeacon", "CHANGELOG.md", id_prefix="changelog-"),
        "{{ROADMAP}}": markdown_html("ROADMAP.md", id_prefix="roadmap-"),
        "{{PIECES}}": pieces.render(),
        "{{MATRIX_DATA}}": data,
    }
    for k, v in fills.items():
        if k not in page:
            raise SystemExit(f"site/index.html has no {k}")
        page = page.replace(k, v)

    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    os.makedirs(OUT)
    open(os.path.join(OUT, "index.html"), "w", encoding="utf-8", newline="\n").write(page)
    for name in ("site.css", "matrix.js"):
        shutil.copyfile(os.path.join(SITE, name), os.path.join(OUT, name))
    os.makedirs(os.path.join(OUT, "img"))
    for src, name in pieces.icons():
        shutil.copyfile(os.path.join(ROOT, src), os.path.join(OUT, "img", name))
    open(os.path.join(OUT, ".nojekyll"), "w").close()  # serve the files as they are
    n = sum(len(b["trophies"]) for b in matrix_payload()["biomes"])
    print(f"wrote {OUT}: index.html (version {manifest['version_number']}, {n} trophies, {len(pieces.pieces())} pieces), "
          f"site.css, matrix.js, {len(pieces.icons())} icons")


if __name__ == "__main__":
    main()
