"""BuildBeacon release flow: version check, build, package and publish to Thunderstore.

Run from anywhere (Python 3.11+, standard library only):

    uv run --no-project python tools/release/release.py check              version and package checks (what CI runs)
    uv run --no-project python tools/release/release.py check --online     ... plus Thunderstore: dependencies exist,
                                                                           the published version is not ahead of ours
    uv run --no-project python tools/release/release.py bump patch         0.1.0 -> 0.1.1 (or minor, major, or 1.2.3)
    uv run --no-project python tools/release/release.py package            Release build + dist/BuildBeacon-<v>.zip
    uv run --no-project python tools/release/release.py publish            every publish check and the zip, no upload
    uv run --no-project python tools/release/release.py upload             the same, then (after you type the version to
                                                                           confirm) upload to Thunderstore and tag

The version lives in three places that must agree: `PluginVersion` in BuildBeacon/BuildBeaconPlugin.cs (what BepInEx
and Jotunn's network check see), `version_number` in BuildBeacon/Package/manifest.json (what Thunderstore sees), and
the first `## x.y.z` heading of BuildBeacon/CHANGELOG.md. `bump` moves all three.

Uploading needs a Thunderstore API token (a service account token of the team named in thunderstore.toml): the
TCLI_AUTH_TOKEN environment variable, or else the file named by TCLI_AUTH_TOKEN_FILE, or else
~/.config/thunderstore/buildbeacon-token. It is passed to tcli through the environment, never on a command line or
printed. See docs/releasing.md.
"""
import argparse
import hashlib
import json
import os
import re
import struct
import subprocess
import sys
import tomllib
import urllib.error
import urllib.request
import zipfile

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
PLUGIN_CS = os.path.join(ROOT, "BuildBeacon", "BuildBeaconPlugin.cs")
CONFIG_CS = os.path.join(ROOT, "BuildBeacon", "BeaconConfig.cs")
CSPROJ = os.path.join(ROOT, "BuildBeacon", "BuildBeacon.csproj")
PACKAGE = os.path.join(ROOT, "BuildBeacon", "Package")
MANIFEST = os.path.join(PACKAGE, "manifest.json")
ICON = os.path.join(PACKAGE, "icon.png")
CHANGELOG = os.path.join(ROOT, "BuildBeacon", "CHANGELOG.md")
README = os.path.join(ROOT, "README.md")
TOML = os.path.join(ROOT, "thunderstore.toml")
RELEASE_DLL = os.path.join(ROOT, "BuildBeacon", "bin", "Release", "net48", "BuildBeacon.dll")
DIST = os.path.join(ROOT, "dist")

# What goes into the zip: (path inside the zip, source). Thunderstore requires manifest.json, icon.png and README.md
# at the root; r2modman installs plugins/ into BepInEx/plugins/<Team>-<Name>/. DEFAULT_DISCOUNTS.md is the defaults
# list the README points players to.
ZIP_LAYOUT = [
    ("manifest.json", MANIFEST),
    ("icon.png", ICON),
    ("README.md", README),
    ("CHANGELOG.md", CHANGELOG),
    ("DEFAULT_DISCOUNTS.md", os.path.join(PACKAGE, "DEFAULT_DISCOUNTS.md")),
    ("plugins/BuildBeacon.dll", RELEASE_DLL),
]

SEMVER = re.compile(r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")
DEPENDENCY = re.compile(r"^([A-Za-z0-9_]+)-([A-Za-z0-9_]+)-(\d+\.\d+\.\d+)$")
PLUGIN_VERSION = re.compile(r'(public const string PluginVersion = ")([^"]*)(";)')
PLUGIN_NAME = re.compile(r'public const string PluginName = "([^"]*)";')
DEVMODE_DEFAULT = re.compile(r'DevMode = ConfigUtil\.Local\(cfg, D, "DevMode", (true|false)')
JOTUNN_REF = re.compile(r'<PackageReference Include="JotunnLib" Version="([^"]+)"')
CHANGELOG_PLACEHOLDER = "- (describe the changes)"
USER_AGENT = "BuildBeacon-release/1.0 (+https://github.com/xaivous/BuildBeacon)"
TOKEN_FILE = os.path.join(os.path.expanduser("~"), ".config", "thunderstore", "buildbeacon-token")


# ---- Small helpers ----

class Report:
    """Collects errors, warnings and notes, and prints them at the end."""

    def __init__(self):
        self.errors, self.warnings, self.notes = [], [], []

    def error(self, msg): self.errors.append(msg)
    def warn(self, msg): self.warnings.append(msg)
    def note(self, msg): self.notes.append(msg)

    def print(self):
        for m in self.notes:
            print(f"  ok    {m}")
        for m in self.warnings:
            print(f"  warn  {m}")
        for m in self.errors:
            print(f"  ERROR {m}")

    def ok(self):
        return not self.errors


def read(path):
    with open(path, "rb") as f:
        return f.read().decode("utf-8-sig")


def write_like(path, text):
    """Write text back with the file's original line endings and BOM."""
    with open(path, "rb") as f:
        raw = f.read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    crlf = b"\r\n" in raw
    text = text.replace("\r\n", "\n")
    if crlf:
        text = text.replace("\n", "\r\n")
    with open(path, "wb") as f:
        f.write((b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8"))


def version_key(v):
    return tuple(int(p) for p in v.split("."))


def run(cmd, env=None, capture=False):
    """Run a command from the repository root; stop the flow if it fails."""
    print(f"$ {' '.join(cmd)}")
    result = subprocess.run(cmd, cwd=ROOT, env=env, text=True, capture_output=capture)
    if result.returncode != 0:
        if capture:
            sys.stdout.write(result.stdout or "")
            sys.stderr.write(result.stderr or "")
        sys.exit(f"'{cmd[0]}' failed with exit code {result.returncode}")
    return result.stdout if capture else None


def git(*args):
    return subprocess.run(["git", *args], cwd=ROOT, text=True, capture_output=True)


def thunderstore(path):
    """GET a Thunderstore experimental API path; None on 404."""
    req = urllib.request.Request(f"https://thunderstore.io/api/experimental/{path}", headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(req, timeout=20) as r:
            return json.load(r)
    except urllib.error.HTTPError as e:
        if e.code == 404:
            return None
        raise


def png_size(path):
    """(width, height) from a PNG's IHDR chunk, or None if the file is not a PNG."""
    with open(path, "rb") as f:
        head = f.read(24)
    if head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        return None
    return struct.unpack(">II", head[16:24])


# ---- Reading the sources ----

def sources():
    """Everything the checks compare, read once."""
    plugin = read(PLUGIN_CS)
    m = PLUGIN_VERSION.search(plugin)
    changelog = read(CHANGELOG)
    headings = re.findall(r"^## (.+?)\s*$", changelog, re.M)
    with open(TOML, "rb") as f:
        toml = tomllib.load(f)
    return {
        "plugin_version": m.group(2) if m else None,
        "plugin_name": (PLUGIN_NAME.search(plugin) or [None, None])[1],
        "manifest": json.loads(read(MANIFEST)),
        "changelog": changelog,
        "changelog_top": headings[0] if headings else None,
        "toml": toml,
        "devmode": (DEVMODE_DEFAULT.search(read(CONFIG_CS)) or [None, None])[1],
        "jotunn_ref": (JOTUNN_REF.search(read(CSPROJ)) or [None, None])[1],
    }


def stale_zip_entries(version):
    """The files in an existing dist/BuildBeacon-<version>.zip that differ from their sources; empty when there is no
    zip or it is current. The DLL is skipped: only a build can say whether it changed."""
    path = os.path.join(DIST, f"BuildBeacon-{version}.zip")
    if not os.path.isfile(path):
        return []
    stale = []
    with zipfile.ZipFile(path) as z:
        names = set(z.namelist())
        for arc, src in ZIP_LAYOUT:
            if arc.endswith(".dll") or not os.path.isfile(src):
                continue
            with open(src, "rb") as f:
                if arc not in names or z.read(arc) != f.read():
                    stale.append(arc)
    return stale


def changelog_entry(changelog, version):
    """The text under '## <version>' up to the next '## ' heading."""
    m = re.search(rf"^## {re.escape(version)}\s*$(.*?)(?=^## |\Z)", changelog, re.M | re.S)
    return m.group(1).strip() if m else None


# ---- The checks ----

def check(report, tag=None, release=False, online=False, publishing=False):
    s = sources()
    man = s["manifest"]
    version = man.get("version_number")

    # Versions: one number in three places, and the tag when there is one.
    print(f"Versions: PluginVersion {s['plugin_version']}, manifest {version}, CHANGELOG top {s['changelog_top']}"
          + (f", tag {tag}" if tag else ""))
    for label, v in (("PluginVersion (BuildBeaconPlugin.cs)", s["plugin_version"]),
                     ("version_number (manifest.json)", version)):
        if not v or not SEMVER.match(v):
            report.error(f"{label} is '{v}', not Major.Minor.Patch (whole numbers, no leading zeros)")
    if s["plugin_version"] != version:
        report.error(f"PluginVersion {s['plugin_version']} and manifest version_number {version} differ "
                     f"(`release.py bump {version}` sets both)")
    if s["changelog_top"] != version:
        report.error(f"CHANGELOG.md's first heading is '{s['changelog_top']}', not '{version}'")
    else:
        entry = changelog_entry(s["changelog"], version) or ""
        if not entry:
            report.error(f"CHANGELOG.md has no text under '## {version}'")
        elif CHANGELOG_PLACEHOLDER in entry:
            (report.error if release else report.warn)(f"CHANGELOG.md's {version} entry still has the placeholder line")
        else:
            report.note(f"version {version} in all three places, with a changelog entry")
    if tag is not None and tag != f"v{version}":
        report.error(f"tag {tag} does not match the version (expected v{version})")

    # manifest.json, by Thunderstore's rules.
    name = man.get("name", "")
    if not re.match(r"^[A-Za-z0-9_]{1,128}$", name):
        report.error(f"manifest name '{name}': only A-Z a-z 0-9 _ allowed, 128 at most")
    if name != s["plugin_name"]:
        report.error(f"manifest name '{name}' differs from PluginName '{s['plugin_name']}'")
    desc = man.get("description", "")
    if not desc or len(desc) > 250:
        report.error(f"manifest description is {len(desc)} characters; it must be 1 to 250")
    if not isinstance(man.get("website_url"), str):
        report.error("manifest website_url is missing (Thunderstore needs the key, even as an empty string)")
    elif not man["website_url"]:
        (report.error if release else report.warn)("manifest website_url is empty (PREPUBLISH C3: the Pages site)")
    deps = man.get("dependencies")
    if not isinstance(deps, list):
        report.error("manifest dependencies must be a list")
        deps = []
    for d in deps:
        if not DEPENDENCY.match(d):
            report.error(f"dependency '{d}' is not Team-Package-Major.Minor.Patch")
    jotunn = [d for d in deps if d.startswith("ValheimModding-Jotunn-")]
    if not jotunn:
        report.error("manifest has no ValheimModding-Jotunn dependency")
    elif jotunn[0].rsplit("-", 1)[1] != s["jotunn_ref"]:
        report.error(f"manifest depends on {jotunn[0]} but the csproj builds against JotunnLib {s['jotunn_ref']}")
    if not any(d.startswith("denikson-BepInExPack_Valheim-") for d in deps):
        report.error("manifest has no denikson-BepInExPack_Valheim dependency")
    if set(man) - {"name", "description", "version_number", "website_url", "dependencies"}:
        report.warn(f"manifest has extra keys: {sorted(set(man) - {'name', 'description', 'version_number', 'website_url', 'dependencies'})}")

    # Files that ship.
    size = png_size(ICON)
    if size != (256, 256):
        report.error(f"icon.png is {size or 'not a PNG'}; Thunderstore needs a 256x256 PNG")
    for label, path in (("README.md", README), ("CHANGELOG.md", CHANGELOG)):
        try:
            if not read(path).strip():
                report.error(f"{label} is empty")
        except UnicodeDecodeError:
            report.error(f"{label} is not UTF-8")
    report.note(f"manifest {name} {version}: description {len(desc)}/250, {len(deps)} dependencies; icon 256x256")

    # thunderstore.toml: the team and where it is listed.
    toml = s["toml"]
    team = toml.get("package", {}).get("namespace", "")
    if not re.match(r"^[A-Za-z0-9_]+$", team):
        report.error(f"thunderstore.toml package.namespace '{team}' is not a valid team name")
    if toml.get("package", {}).get("name") != name:
        report.error(f"thunderstore.toml package.name differs from the manifest name '{name}'")
    if "valheim" not in toml.get("publish", {}).get("communities", []):
        report.error("thunderstore.toml publish.communities does not include 'valheim'")

    # A zip left in dist/ by an earlier `package` that no longer matches the sources (the DLL needs a build to compare).
    stale = stale_zip_entries(version)
    if stale:
        report.warn(f"dist/BuildBeacon-{version}.zip is out of date ({', '.join(stale)} changed since it was built): "
                    f"run `package` again before testing or uploading it by hand")

    # Release-only: the pre-publish guards.
    if s["devmode"] != "false":
        (report.error if release else report.warn)(
            f"DevMode defaults to {s['devmode']} in BeaconConfig.cs (PREPUBLISH C4: must be false when shipped)")

    # Thunderstore itself.
    if online:
        for d in deps:
            m = DEPENDENCY.match(d)
            if not m:
                continue
            if thunderstore(f"package/{m[1]}/{m[2]}/{m[3]}/") is None:
                report.error(f"dependency {d} does not exist on Thunderstore")
            else:
                latest = thunderstore(f"package/{m[1]}/{m[2]}/")["latest"]["version_number"]
                if version_key(latest) > version_key(m[3]):
                    report.warn(f"{m[1]}-{m[2]} {latest} is out; the manifest uses {m[3]} (PREPUBLISH C7)")
                else:
                    report.note(f"{d} exists and is the latest")
        published = thunderstore(f"package/{team}/{name}/")
        if published is None:
            report.note(f"{team}-{name} is not on Thunderstore yet: {version} will be the first version")
        else:
            latest = published["latest"]["version_number"]
            if version_key(latest) > version_key(version):
                report.error(f"Thunderstore already has {latest}, newer than {version}")
            elif latest == version and publishing:
                report.error(f"{version} is already published; bump the version first")
            else:
                report.note(f"Thunderstore's latest is {latest}")
    return s


# ---- Commands ----

def cmd_check(args):
    report = Report()
    check(report, tag=args.tag, release=args.release, online=args.online)
    report.print()
    print("check passed" if report.ok() else "check FAILED")
    return 0 if report.ok() else 1


def cmd_bump(args):
    s = sources()
    old = s["manifest"]["version_number"]
    if args.to in ("major", "minor", "patch"):
        major, minor, patch = version_key(old)
        new = {"major": f"{major + 1}.0.0", "minor": f"{major}.{minor + 1}.0", "patch": f"{major}.{minor}.{patch + 1}"}[args.to]
    elif SEMVER.match(args.to):
        new = args.to
    else:
        sys.exit(f"'{args.to}' is not major, minor, patch or a Major.Minor.Patch version")
    if version_key(new) <= version_key(old) and not args.force:
        sys.exit(f"{new} is not newer than {old} (use --force to set it anyway)")

    plugin = read(PLUGIN_CS)
    write_like(PLUGIN_CS, PLUGIN_VERSION.sub(rf'\g<1>{new}\g<3>', plugin, count=1))
    manifest_text = read(MANIFEST)
    manifest_text, n = re.subn(r'("version_number"\s*:\s*")[^"]*(")', rf'\g<1>{new}\g<2>', manifest_text, count=1)
    if n != 1:
        sys.exit("could not find version_number in manifest.json")
    write_like(MANIFEST, manifest_text)
    changelog = read(CHANGELOG)
    if not re.search(rf"^## {re.escape(new)}\s*$", changelog, re.M):
        changelog = re.sub(r"^(# Changelog\s*\n)", rf"\g<1>\n## {new}\n\n{CHANGELOG_PLACEHOLDER}\n", changelog,
                           count=1, flags=re.M)
        write_like(CHANGELOG, changelog)
    print(f"{old} -> {new}: PluginVersion, manifest version_number, and a '## {new}' CHANGELOG entry to fill in")
    return 0


def build_release():
    run(["dotnet", "build", os.path.join(ROOT, "BuildBeacon.sln"), "-c", "Release", "-nologo", "-v", "quiet"])
    if not os.path.isfile(RELEASE_DLL):
        sys.exit(f"the Release build did not produce {RELEASE_DLL}")
    with open(RELEASE_DLL, "rb") as f:
        dll = f.read()
    if b"buildbeacon" not in dll:
        sys.exit("the Release DLL has no embedded 'buildbeacon' asset bundle")


def make_zip(version):
    """dist/BuildBeacon-<version>.zip, the same bytes for the same inputs (fixed timestamps and order)."""
    os.makedirs(DIST, exist_ok=True)
    path = os.path.join(DIST, f"BuildBeacon-{version}.zip")
    stamp = (1980, 1, 1, 0, 0, 0)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for arc, src in ZIP_LAYOUT:
            if not os.path.isfile(src):
                sys.exit(f"missing {src} for {arc}")
            info = zipfile.ZipInfo(arc, date_time=stamp)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            with open(src, "rb") as f:
                z.writestr(info, f.read())
    return path


def verify_zip(path, version):
    """Re-open the zip and check it the way Thunderstore and r2modman will read it."""
    with zipfile.ZipFile(path) as z:
        names = z.namelist()
        expected = [arc for arc, _ in ZIP_LAYOUT]
        if sorted(names) != sorted(expected):
            sys.exit(f"zip contents {names} differ from {expected}")
        man = json.loads(z.read("manifest.json").decode("utf-8-sig"))
        if man["version_number"] != version:
            sys.exit(f"the zipped manifest says {man['version_number']}, not {version}")
        if z.testzip() is not None:
            sys.exit("the zip is corrupt")
    with open(path, "rb") as f:
        digest = hashlib.sha256(f.read()).hexdigest()
    print(f"package {os.path.relpath(path, ROOT)}: {os.path.getsize(path):,} bytes, sha256 {digest[:16]}...")
    for n in names:
        print(f"  {n}")


def cmd_package(args):
    report = Report()
    s = check(report, release=args.release)
    report.print()
    if not report.ok():
        return 1
    version = s["manifest"]["version_number"]
    build_release()
    path = make_zip(version)
    verify_zip(path, version)
    return 0


def git_checks(report, version):
    if git("status", "--porcelain", "--untracked-files=no").stdout.strip():
        report.error("the working tree has uncommitted changes: publish from a commit")
    branch = git("rev-parse", "--abbrev-ref", "HEAD").stdout.strip()
    if branch != "main":
        report.error(f"on branch '{branch}', not main")
    if git("fetch", "--quiet", "origin").returncode != 0:
        report.error("git fetch origin failed; cannot check the branch is pushed")
    else:
        ahead = git("rev-list", "--count", "origin/main..HEAD").stdout.strip()
        behind = git("rev-list", "--count", "HEAD..origin/main").stdout.strip()
        if ahead != "0" or behind != "0":
            report.error(f"main is {ahead} ahead and {behind} behind origin/main: push or pull first, so the published "
                         f"version matches the repository")
    tag = f"v{version}"
    if git("rev-parse", "-q", "--verify", f"refs/tags/{tag}").returncode == 0:
        report.error(f"tag {tag} already exists locally")
    if git("ls-remote", "--tags", "origin", tag).stdout.strip():
        report.error(f"tag {tag} already exists on origin")
    if not report.errors:
        report.note(f"main is clean and pushed; {tag} is free")


def load_token():
    """The Thunderstore token: TCLI_AUTH_TOKEN, else the file TCLI_AUTH_TOKEN_FILE names, else TOKEN_FILE. Never printed."""
    token = os.environ.get("TCLI_AUTH_TOKEN", "").strip()
    if token:
        return token, "the TCLI_AUTH_TOKEN environment variable"
    path = os.environ.get("TCLI_AUTH_TOKEN_FILE") or TOKEN_FILE
    if os.path.isfile(path):
        with open(path, encoding="utf-8") as f:
            return f.read().strip(), path
    return "", None


def prepare(uploading):
    """Everything before the upload: all checks (release, online, git), then the Release build and the verified zip.
    None when a check fails."""
    report = Report()
    s = check(report, release=True, online=True, publishing=True)
    version = s["manifest"]["version_number"]
    git_checks(report, version)
    token, source = load_token()
    if not token:
        (report.error if uploading else report.warn)(
            f"no Thunderstore token: set TCLI_AUTH_TOKEN or put it in {TOKEN_FILE} (see docs/releasing.md)")
    else:
        report.note(f"Thunderstore token found in {source}")
    report.print()
    if not report.ok():
        print(("upload" if uploading else "publish") + " stopped: fix the errors above")
        return None
    build_release()
    path = make_zip(version)
    verify_zip(path, version)
    team, name = s["toml"]["package"]["namespace"], s["manifest"]["name"]
    cmd = ["dotnet", "tcli", "publish", "--config-path", TOML, "--file", path,
           "--package-namespace", team, "--package-name", name, "--package-version", version]
    return {"version": version, "team": team, "name": name, "cmd": cmd, "token": token}


def cmd_publish(args):
    """The rehearsal: every check and the zip that `upload` would send, without sending it."""
    ctx = prepare(uploading=False)
    if ctx is None:
        return 1
    print(f"ready: `upload` would run `{' '.join(ctx['cmd'])}` and tag v{ctx['version']}")
    return 0


def cmd_upload(args):
    """`publish`, then the typed confirmation, the upload to Thunderstore and the tag."""
    ctx = prepare(uploading=True)
    if ctx is None:
        return 1
    version, team, name = ctx["version"], ctx["team"], ctx["name"]
    try:
        answer = input(f"Upload {team}-{name} {version} to Thunderstore? Versions cannot be deleted, only deprecated. "
                       f"Type the version to confirm: ")
    except EOFError:
        answer = ""
    if answer.strip() != version:
        print("not uploaded: the confirmation did not match the version")
        return 1
    run(["dotnet", "tool", "restore"])
    run(ctx["cmd"], env={**os.environ, "TCLI_AUTH_TOKEN": ctx["token"]})
    tag = f"v{version}"
    run(["git", "tag", "-a", tag, "-m", f"BuildBeacon {version}"])
    print(f"uploaded {team}-{name} {version} and tagged {tag}. Push the tag (CI checks it): git push origin {tag}")
    return 0


def main():
    sys.stdout.reconfigure(line_buffering=True)  # keep our lines in order with the build's output
    p = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = p.add_subparsers(dest="command", required=True)
    c = sub.add_parser("check", help="version and package checks")
    c.add_argument("--tag", help="a release tag to check against the version, e.g. v0.1.0")
    c.add_argument("--release", action="store_true", help="also the pre-publish guards (DevMode off, website_url, ...)")
    c.add_argument("--online", action="store_true", help="also check against Thunderstore")
    c.set_defaults(func=cmd_check)
    b = sub.add_parser("bump", help="set a new version in all three places")
    b.add_argument("to", help="major, minor, patch, or a Major.Minor.Patch version")
    b.add_argument("--force", action="store_true", help="allow a version that is not newer")
    b.set_defaults(func=cmd_bump)
    k = sub.add_parser("package", help="Release build and dist/BuildBeacon-<version>.zip")
    k.add_argument("--release", action="store_true", help="apply the pre-publish guards too")
    k.set_defaults(func=cmd_package)
    u = sub.add_parser("publish", help="every check and the verified zip, without uploading (the rehearsal)")
    u.set_defaults(func=cmd_publish)
    up = sub.add_parser("upload", help="publish, then confirm by typing the version, upload to Thunderstore and tag")
    up.set_defaults(func=cmd_upload)
    args = p.parse_args()
    sys.exit(args.func(args))


if __name__ == "__main__":
    main()
