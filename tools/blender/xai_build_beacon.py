"""
xai_build_beacon.py — Procedural Build Beacon mesh for Valheim modding (v2).

Paste into Blender's Scripting tab (Blender 3.6+ / 4.x / 5.x) and press Run,
or run headless:  blender --background --python build_beacon.py

v2 additions over v1:
  - 3-tier flared base, slimmer pillar, proportions matched to concept art
  - glowing rune band moved to the pillar top, under a flared capital + pyramid cap
  - REAL Elder Futhark rune glyphs built as raised emissive geometry on the band
  - thin emissive trim rings above/below the band
  - framed, EMPTY trophy alcoves (recessed niches with a raised stone frame,
    ready to act as item-stand attach points in-game)
  - cracked/weathered stone via subdivide + noise displacement, flat-shaded
    so it keeps the faceted low-poly Valheim look

Also builds the Great Beacon (GREAT below): the same design grander, with boss-sized alcoves; see
docs/design/great-beacon.md.

Result: one object "xai_build_beacon", origin at ground center, transforms
applied, UV-unwrapped, ~1.7m footprint x ~3m tall, 6 material slots (the sixth, BeaconCore, is a glowing
core inside the floating crystal).
Re-running is idempotent. Tweak PARAMS and re-run.
"""

import bpy
import math
import random

# ----------------------------------------------------------------------------
# Parameters — tweak freely
# ----------------------------------------------------------------------------
PARAMS = {
    "name": "xai_build_beacon",
    "segments": 8,             # octagonal cross-section everywhere
    "seed": 7,                 # rune choice + stone noise offset

    # Base tiers: (radius, height) from the ground up — big flared plinth
    "base_tiers": [(0.80, 0.20), (0.64, 0.15), (0.50, 0.13)],

    # Pillar
    "pillar_r_bottom": 0.37,
    "pillar_r_top":    0.31,
    "pillar_height":   1.48,

    # Trophy alcoves — empty framed niches (item-stand attach points)
    "alcove_count":    4,
    "alcove_width":    0.20,
    "alcove_height":   0.42,
    "alcove_depth":    0.16,   # recess depth into the pillar
    "alcove_z":        0.85,   # niche center, relative to pillar bottom (~2/3 up the shaft)
    "frame_border":    0.045,  # raised stone frame width around the niche
    "frame_out":       0.030,  # how far the frame sticks out of the pillar face

    # Rune band — sits at the very top of the pillar, under the capital
    "band_height":     0.20,
    "band_out":        0.020,  # band proud of the pillar face
    "runes_per_face":  2,
    "rune_height":     0.11,
    "rune_stroke":     0.016,  # stroke width
    "rune_out":        0.014,  # glyph relief above the band face
    "trim_height":     0.028,  # emissive trim rings above/below band

    # Capital: cornice ring -> flared capital -> abacus -> pyramid cap (all three share capital_r_top at their joins)
    "ring_r_extra":    0.055,
    "ring_height":     0.06,
    "capital_r_bottom": 0.38,
    "capital_r_top":   0.54,
    "capital_height":  0.18,
    "cap_height":      0.26,
    "abacus_height":   0.09,   # straight block between the flared capital and the pyramid cap; flush with both

    # Crystal (floating above the cap)
    "crystal_r":       0.20,
    "crystal_gap":     0.24,
    "crystal_stretch": 1.55,
    # Core: a smaller copy of the crystal inside it, glowing like the wisp torch's orb (BeaconCore), seen through the
    # translucent crystal so the crystal keeps its own look. Fraction of the crystal's size; None leaves it out.
    "core_scale":      0.55,
    # The crystal and its core as their own object, exported to their own FBX with the origin at the crystal's centre,
    # so the plugin can spin and bob them (and the glow with them) while the beacon is lit. False joins them into the
    # beacon as before.
    "crystal_separate": True,
    "crystal_fbx_path": "//../BuildBeaconUnity/Assets/Beacon/xai_build_beacon_crystal.fbx",

    # Stone weathering (cracks / faceting)
    "stone_subdiv":    2,      # simple-subdivision levels before displacement
    "stone_noise_scale": 0.35, # smaller = larger crack features
    "stone_noise_strength": 0.038,
    "decimate_ratio":  None,   # e.g. 0.5 to halve tris after displacement

    # Export after every build. "//" is relative to the .blend (Blender/totem.blend), so this is the Unity project's
    # model asset; Unity reimports it on focus. None disables export.
    "fbx_path": "//../BuildBeaconUnity/Assets/Beacon/xai_build_beacon.fbx",

    # ---- Features the Great Beacon uses; the beacon's values leave them out ----
    # Alcoves as (face angle in degrees from +X, niche centre above the pillar bottom), in attach order. None: the
    # beacon's alcove_count faces from +X, evenly spaced, at alcove_z.
    "alcoves":         None,
    # A second rune band on the pillar: (bottom above the pillar bottom, height), with trims and runes like the top one.
    "mid_band":        None,
    # A framed, recessed panel with one large glyph: {"angle": deg, "z": centre above the pillar bottom, "glyph": key
    # in RUNES, "glyph_height": m, "stroke": m, "out": m, "recess": m}.
    "rune_panel":      None,
    # Wedge buttresses on the pillar's edges: {"foot_tier": index of the base tier whose top they stand on, "top": m
    # above the pillar bottom where they meet the pillar, "width": m}.
    "buttresses":      None,
    # Small crystals around the main one, turning with it: {"count", "r", "stretch", "distance", "tilt_deg", "dz"}.
    "shards":          None,
    # Print each alcove's attach point (the niche's back wall centre) for the Unity prefab.
    "print_attach":    False,
    # Where the model sits in the Blender scene after export (the FBX is exported at the origin).
    "display_offset":  (0.0, 0.0, 0.0),
}

# The Great Beacon (docs/design/great-beacon.md): the beacon's design about 2.4x taller, with a stouter pillar for
# boss-sized alcoves, 7 alcoves in two offset rows, a rune panel on the front face (Blender -Y, Unity +Z), a rune band
# between the rows, buttresses on the pillar's edges and four crystal shards. Faces are at multiples of 45 degrees from
# +X; the front is 270. Attach order, as seen from the front (Blender -X is the viewer's left in Unity, whose import
# flips X): front-left and front-right (top row), left and right (lower row), back-left and back-right (top row), back.
GREAT = dict(PARAMS, **{
    "name": "xai_great_beacon",
    "seed": 11,
    "base_tiers": [(1.95, 0.34), (1.62, 0.30), (1.36, 0.27), (1.18, 0.24)],
    "pillar_r_bottom": 1.12,
    "pillar_r_top":    0.98,
    "pillar_height":   4.45,
    "alcove_width":    0.62,
    "alcove_height":   1.05,
    "alcove_depth":    0.45,
    "frame_border":    0.11,
    "frame_out":       0.07,
    "alcoves": [(225, 3.30), (315, 3.30), (180, 1.45), (0, 1.45), (135, 3.30), (45, 3.30), (90, 1.45)],
    "band_height":     0.45,
    "band_out":        0.05,
    "runes_per_face":  2,
    "rune_height":     0.26,
    "rune_stroke":     0.038,
    "rune_out":        0.03,
    "trim_height":     0.06,
    "mid_band":        (2.19, 0.36),
    "rune_panel": {"angle": 270, "z": 1.45, "glyph": "othala_algiz", "glyph_height": 0.82, "stroke": 0.07,
                   "out": 0.035, "recess": 0.12},
    "buttresses": {"foot_tier": 1, "top": 0.75, "width": 0.24},
    "ring_r_extra":    0.13,
    "ring_height":     0.14,
    "capital_r_bottom": 1.12,
    "capital_r_top":   1.50,
    "capital_height":  0.43,
    "cap_height":      0.62,
    "abacus_height":   0.22,
    "crystal_r":       0.40,
    "crystal_gap":     0.30,
    "shards": {"count": 4, "r": 0.13, "stretch": 1.6, "distance": 0.80, "tilt_deg": 20.0, "dz": 0.05},
    "crystal_fbx_path": "//../BuildBeaconUnity/Assets/Beacon/xai_great_beacon_crystal.fbx",
    "stone_noise_scale": 0.84,
    "stone_noise_strength": 0.07,
    "print_attach":    True,
    "display_offset":  (-6.0, 0.0, 0.0),
    "fbx_path": "//../BuildBeaconUnity/Assets/Beacon/xai_great_beacon.fbx",
})

# Palette: (base_color RGBA, roughness, emission_color RGBA or None, emission_strength)
# PREVIEW ONLY. Unity's .mat assets in BuildBeaconUnity/Assets/Beacon are the source of truth for how each
# material looks: the FBX importer remaps these names onto those assets, and the plugin reads them at runtime.
# Keep this table mirroring Unity so the Blender viewport resembles the game (Unity smoothness = 1 - roughness;
# Unity's HDR _EmissionColor = emission_color * emission_strength).
MATERIALS = {
    "BeaconStoneDark":  ((0.45, 0.43, 0.42, 1.0), 0.95, None, 0.0),
    "BeaconStoneLight": ((0.887, 0.862, 0.824, 1.0), 0.90, None, 0.0),
    "BeaconBandDark":   ((0.05, 0.09, 0.10, 1.0), 0.60, None, 0.0),
    "BeaconRune":       ((0.02, 0.10, 0.12, 1.0), 0.40, (0.036, 0.429, 1.00, 1.0), 1.4),
    "BeaconCrystal":    ((0.12, 0.40, 0.65, 1.0), 0.15, (0.071, 0.429, 1.00, 1.0), 0.7),
    # The wisp torch orb's material (demister_ball): albedo (0.629, 0.25, 1.0), HDR emission (0, 1.247, 1.498).
    "BeaconCore":       ((0.629, 0.25, 1.0, 1.0), 0.50, (0.0, 0.833, 1.00, 1.0), 1.498),
}

# Elder Futhark glyphs as straight strokes ((u1,v1),(u2,v2)) in a unit box
# (u: -0.5..0.5 across, v: 0..1 up). All-straight strokes = perfect for low poly.
RUNES = {
    "fehu":     [((-0.20, 0.0), (-0.20, 1.0)), ((-0.20, 0.95), (0.30, 0.70)), ((-0.20, 0.60), (0.30, 0.35))],
    "uruz":     [((-0.25, 0.0), (-0.25, 1.0)), ((-0.25, 1.0), (0.25, 0.55)), ((0.25, 0.55), (0.25, 0.0))],
    "thurisaz": [((-0.18, 0.0), (-0.18, 1.0)), ((-0.18, 0.80), (0.28, 0.55)), ((0.28, 0.55), (-0.18, 0.30))],
    "ansuz":    [((-0.18, 0.0), (-0.18, 1.0)), ((-0.18, 1.0), (0.30, 0.68)), ((-0.18, 0.72), (0.30, 0.40))],
    "raido":    [((-0.20, 0.0), (-0.20, 1.0)), ((-0.20, 1.0), (0.25, 0.75)), ((0.25, 0.75), (-0.20, 0.50)), ((-0.20, 0.50), (0.28, 0.0))],
    "kenaz":    [((0.25, 1.0), (-0.20, 0.5)), ((-0.20, 0.5), (0.25, 0.0))],
    "hagalaz":  [((-0.25, 0.0), (-0.25, 1.0)), ((0.25, 0.0), (0.25, 1.0)), ((-0.25, 0.65), (0.25, 0.40))],
    "isa":      [((0.0, 0.0), (0.0, 1.0))],
    "tiwaz":    [((0.0, 0.0), (0.0, 1.0)), ((0.0, 1.0), (-0.30, 0.70)), ((0.0, 1.0), (0.30, 0.70))],
    "sowilo":   [((0.22, 1.0), (-0.18, 0.62)), ((-0.18, 0.62), (0.22, 0.38)), ((0.22, 0.38), (-0.18, 0.0))],
    "algiz":    [((0.0, 0.0), (0.0, 1.0)), ((0.0, 0.55), (-0.30, 0.90)), ((0.0, 0.55), (0.30, 0.90))],
    "gebo":     [((-0.25, 0.05), (0.25, 0.95)), ((-0.25, 0.95), (0.25, 0.05))],
}

# Single large glyphs (the rune panel). Kept apart from RUNES: the bands pick from RUNES at random, and a new entry there
# would change every beacon's rune choices.
GLYPHS = {
    # Othala ("home") bound with Algiz ("protection"): a stave with Algiz's arms at the top, Othala's diamond on the
    # stave and its two legs below.
    "othala_algiz": [((0.0, 0.0), (0.0, 1.0)),
                     ((0.0, 0.74), (-0.32, 1.0)), ((0.0, 0.74), (0.32, 1.0)),
                     ((0.0, 0.62), (0.28, 0.40)), ((0.28, 0.40), (0.0, 0.18)),
                     ((0.0, 0.18), (-0.28, 0.40)), ((-0.28, 0.40), (0.0, 0.62)),
                     ((0.0, 0.18), (0.26, 0.0)), ((0.0, 0.18), (-0.26, 0.0))],
}


# ----------------------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------------------
def clean_previous(name):
    for obj in list(bpy.data.objects):
        if obj.name == name or obj.name.startswith(name + "."):
            bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)


def make_material(name, base_color, roughness, emission, emission_strength):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")

    def set_input(names, value):
        for n in names:
            if n in bsdf.inputs:
                bsdf.inputs[n].default_value = value
                return

    set_input(["Base Color"], base_color)
    set_input(["Roughness"], roughness)
    if emission is not None:
        set_input(["Emission Color", "Emission"], emission)   # 4.x/5.x, then 3.x
        set_input(["Emission Strength"], emission_strength)
        mat.diffuse_color = emission
    else:
        mat.diffuse_color = base_color
    return mat


def add_cylinder(name, r_bottom, r_top, height, z_bottom, segments, rot):
    bpy.ops.mesh.primitive_cone_add(
        vertices=segments, radius1=r_bottom, radius2=r_top, depth=height,
        location=(0, 0, z_bottom + height / 2.0), rotation=(0, 0, rot))
    obj = bpy.context.active_object
    obj.name = name
    bpy.ops.object.shade_flat()
    return obj


def assign_material(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)


def select_only(objs, active=None):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]


def join(objs, name):
    select_only(objs, objs[0])
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    return obj


def pillar_radius_at(p, z_rel):
    """Linear taper of the pillar radius at height z_rel above the pillar bottom."""
    t = max(0.0, min(1.0, z_rel / p["pillar_height"]))
    return p["pillar_r_bottom"] * (1 - t) + p["pillar_r_top"] * t


def add_rune_glyph(rune_segments, face_angle, apothem, u_offset, z_base, p, mat):
    """Build one glyph as thin stroke cuboids on the band face at face_angle."""
    strokes = []
    s = p["rune_height"]              # glyph scale (height in meters)
    w = p["rune_stroke"]
    depth = p["rune_out"]
    for (u1, v1), (u2, v2) in rune_segments:
        du, dv = (u2 - u1) * s, (v2 - v1) * s
        length = math.hypot(du, dv)
        phi = math.atan2(dv, du) - math.pi / 2.0   # cube's long axis starts on +Y(u); rotate in face plane
        um, vm = (u1 + u2) / 2.0 * s, (v1 + v2) / 2.0 * s
        # canonical position (face looking along +X): x=out, y=u across face, z=v up
        px = apothem + depth / 2.0
        py = u_offset + um
        pz = z_base + vm
        ca, sa = math.cos(face_angle), math.sin(face_angle)
        bpy.ops.mesh.primitive_cube_add(
            location=(px * ca - py * sa, px * sa + py * ca, pz))
        cube = bpy.context.active_object
        cube.scale = (depth / 2.0, w / 2.0, length / 2.0 + w / 2.0)
        # Blender euler XYZ composes as Rz @ Ry @ Rx: stroke tilt in face plane, then face the right way
        cube.rotation_euler = (phi, 0.0, face_angle)
        strokes.append(cube)
    glyph = join(strokes, "rune")
    assign_material(glyph, mat)
    return glyph


def add_wedge(name, angle, r_in, depth, width, z0, height, embed=0.06):
    """A buttress: a triangular prism against the pillar at `angle`, its foot `depth` out from radius r_in, sloping
    back to the pillar at z0 + height. Starts `embed` inside the pillar so no gap shows."""
    x0, x1 = r_in - embed, r_in + depth
    hw = width / 2.0
    verts = [(x0, -hw, z0), (x1, -hw, z0), (x0, -hw, z0 + height),
             (x0, hw, z0), (x1, hw, z0), (x0, hw, z0 + height)]
    faces = [(0, 1, 2), (3, 5, 4), (0, 3, 4, 1), (1, 4, 5, 2), (0, 2, 5, 3)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.rotation_euler = (0, 0, angle)
    select_only([obj], obj)   # outward normals, whatever the vertex order (Unity culls back faces)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj


def add_band(p, mats, rng, bottom, height, radius, seg, rot, cos_ap, glow_parts):
    """A dark rune band of `height` from `bottom`, emissive trim rings above and below, and runes_per_face raised glyphs
    on each face (chosen with rng). The beacon's top band; the Great Beacon also has one between its alcove rows."""
    band = add_cylinder("band", radius, radius, height, bottom, seg, rot)
    assign_material(band, mats["BeaconBandDark"])
    glow_parts.append(band)

    for tz in (bottom - p["trim_height"], bottom + height):
        trim = add_cylinder("trim", radius + 0.012, radius + 0.012, p["trim_height"], tz, seg, rot)
        assign_material(trim, mats["BeaconRune"])
        glow_parts.append(trim)

    rune_names = list(RUNES.keys())
    band_apothem = radius * cos_ap
    face_w = 2.0 * radius * math.tan(math.pi / seg)
    glyph_z = bottom + (height - p["rune_height"]) / 2.0
    for i in range(seg):
        face_angle = i * (2.0 * math.pi / seg)
        for k in range(p["runes_per_face"]):
            spread = face_w * 0.52
            u = (k - (p["runes_per_face"] - 1) / 2.0) * spread / max(1, p["runes_per_face"] - 1) if p["runes_per_face"] > 1 else 0.0
            rune = RUNES[rng.choice(rune_names)]
            glow_parts.append(add_rune_glyph(rune, face_angle, band_apothem, u, glyph_z, p, mats["BeaconRune"]))


# ----------------------------------------------------------------------------
# Build
# ----------------------------------------------------------------------------
def build(p=PARAMS):
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    clean_previous(p["name"])
    clean_previous(p["name"] + "_crystal")
    for obj in [o for o in bpy.data.objects if o.name.startswith(p["name"] + "_shard_")]:
        bpy.data.objects.remove(obj, do_unlink=True)
    rng = random.Random(p["seed"])

    mats = {name: make_material(name, *spec) for name, spec in MATERIALS.items()}
    seg = p["segments"]
    rot = math.pi / seg            # flat faces aligned to +X, +Y, ... axes
    cos_ap = math.cos(math.pi / seg)   # apothem factor: face distance = r * cos_ap

    stone_parts, glow_parts = [], []
    z = 0.0

    # --- Base tiers: flared plinth, alternating stone shades
    for i, (radius, height) in enumerate(p["base_tiers"]):
        tier = add_cylinder(f"tier{i}", radius, radius * 0.92, height, z, seg, rot)
        assign_material(tier, mats["BeaconStoneDark" if i < 2 else "BeaconStoneLight"])
        stone_parts.append(tier)
        z += height
    pillar_bottom = z

    # --- Pillar (band occupies its top section, so stone stops below the band)
    stone_h = p["pillar_height"] - p["band_height"]
    pillar = add_cylinder("pillar", p["pillar_r_bottom"],
                          pillar_radius_at(p, stone_h), stone_h, pillar_bottom, seg, rot)
    assign_material(pillar, mats["BeaconStoneLight"])

    # --- Alcove frames: raised stone borders, then cut the empty niche through both. The rune panel is the same
    # recipe with a shallow recess (its dark plate and glyph come later, with the glowing parts).
    frames, cutters = [], []
    if p["alcoves"]:
        alcoves = [(math.radians(a), z_rel) for a, z_rel in p["alcoves"]]
    else:
        alcoves = [(i * (2.0 * math.pi / p["alcove_count"]), p["alcove_z"]) for i in range(p["alcove_count"])]
    openings = [(a, z_rel, p["alcove_width"], p["alcove_height"], p["alcove_depth"]) for a, z_rel in alcoves]
    panel = p["rune_panel"]
    if panel:
        openings.append((math.radians(panel["angle"]), panel["z"], p["alcove_width"], p["alcove_height"], panel["recess"]))
    attach = []
    for angle, z_rel, width, height, depth in openings:
        alcove_z = pillar_bottom + z_rel
        apothem = pillar_radius_at(p, z_rel) * cos_ap
        fw = width + 2 * p["frame_border"]
        fh = height + 2 * p["frame_border"]
        ca, sa = math.cos(angle), math.sin(angle)

        # frame block: embedded `embed` into the pillar, proud by frame_out
        embed = 0.05
        bpy.ops.mesh.primitive_cube_add(location=(0, 0, 0))
        frame = bpy.context.active_object
        frame.scale = ((p["frame_out"] + embed) / 2.0, fw / 2.0, fh / 2.0)
        frame.rotation_euler = (0, 0, angle)
        fx = apothem + (p["frame_out"] - embed) / 2.0
        frame.location = (fx * ca, fx * sa, alcove_z)
        assign_material(frame, mats["BeaconStoneLight"])
        frames.append(frame)

        # niche cutter: punches all the way through the frame front, ends
        # inside the pillar so the pillar wall forms the back of the niche
        punch = p["frame_out"] + 0.02
        bpy.ops.mesh.primitive_cube_add(location=(0, 0, 0))
        cutter = bpy.context.active_object
        cutter.scale = ((depth + punch) / 2.0, width / 2.0, height / 2.0)
        cutter.rotation_euler = (0, 0, angle)
        cx = apothem + (punch - depth) / 2.0
        cutter.location = (cx * ca, cx * sa, alcove_z)
        cutters.append(cutter)
        attach.append(((apothem - depth) * ca, (apothem - depth) * sa, alcove_z, ca, sa))
    if panel:
        panel_back = attach.pop()   # the panel's recess back, for its plate and glyph; not an attach point

    for target in [pillar] + frames:
        for cutter in cutters:
            mod = target.modifiers.new(name="niche", type="BOOLEAN")
            mod.operation = "DIFFERENCE"
            mod.object = cutter
            mod.solver = "EXACT"
        select_only([target], target)
        for mod in list(target.modifiers):
            bpy.ops.object.modifier_apply(modifier=mod.name)
    for cutter in cutters:
        bpy.data.objects.remove(cutter, do_unlink=True)
    stone_parts.append(pillar)
    stone_parts.extend(frames)

    # --- Buttresses on the pillar's edges (the octagon's corners, between the faces), from the pillar's foot
    if p["buttresses"]:
        bt = p["buttresses"]
        # The foot stands on top of base tier `foot_tier` (its corners, where the buttresses sit, reach radius * 0.92);
        # the wedge passes through the tiers above it and meets the pillar `top` above the pillar's bottom.
        foot_z = sum(h for _, h in p["base_tiers"][:bt["foot_tier"] + 1])
        foot_r = p["base_tiers"][bt["foot_tier"]][0] * 0.92 - 0.04
        for i in range(seg):
            corner = rot + i * (2.0 * math.pi / seg)   # the faces are at multiples of 2pi/seg, the edges halfway
            wedge = add_wedge("buttress", corner, p["pillar_r_bottom"], foot_r - p["pillar_r_bottom"], bt["width"],
                              foot_z, pillar_bottom + bt["top"] - foot_z)
            assign_material(wedge, mats["BeaconStoneDark"])
            stone_parts.append(wedge)

    # --- Rune band at the pillar top, with trim rings and raised glyphs
    band_bottom = pillar_bottom + stone_h
    band_r = pillar_radius_at(p, stone_h) + p["band_out"]
    add_band(p, mats, rng, band_bottom, p["band_height"], band_r, seg, rot, cos_ap, glow_parts)

    # --- A second band between the alcove rows (Great Beacon)
    if p["mid_band"]:
        mid_rel, mid_h = p["mid_band"]
        mid_r = pillar_radius_at(p, mid_rel + mid_h) + p["band_out"]
        add_band(p, mats, rng, pillar_bottom + mid_rel, mid_h, mid_r, seg, rot, cos_ap, glow_parts)

    # --- The rune panel: a dark plate at the back of its recess and one large glyph on it (Great Beacon)
    if panel:
        px, py, pz, ca, sa = panel_back
        plate_t = 0.02
        bpy.ops.mesh.primitive_cube_add(location=(0, 0, 0))
        plate = bpy.context.active_object
        plate.scale = (plate_t / 2.0, p["alcove_width"] / 2.0, p["alcove_height"] / 2.0)
        plate.rotation_euler = (0, 0, math.radians(panel["angle"]))
        plate_r = math.hypot(px, py) + plate_t / 2.0
        plate.location = (plate_r * ca, plate_r * sa, pz)
        assign_material(plate, mats["BeaconBandDark"])
        glow_parts.append(plate)
        glyph_p = dict(p, rune_height=panel["glyph_height"], rune_stroke=panel["stroke"], rune_out=panel["out"])
        glow_parts.append(add_rune_glyph(GLYPHS[panel["glyph"]], math.radians(panel["angle"]), plate_r + plate_t / 2.0,
                                         0.0, pz - panel["glyph_height"] / 2.0, glyph_p, mats["BeaconRune"]))

    # --- Capital: cornice ring -> flared capital -> pyramid cap
    zc = band_bottom + p["band_height"] + p["trim_height"]
    ring = add_cylinder("ring", band_r + p["ring_r_extra"], band_r + p["ring_r_extra"],
                        p["ring_height"], zc, seg, rot)
    assign_material(ring, mats["BeaconStoneDark"])
    stone_parts.append(ring)
    zc += p["ring_height"]

    capital = add_cylinder("capital", p["capital_r_bottom"], p["capital_r_top"],
                           p["capital_height"], zc, seg, rot)
    assign_material(capital, mats["BeaconStoneDark"])
    stone_parts.append(capital)
    zc += p["capital_height"]

    # Abacus: straight block whose radius matches the capital top and the cap base, so both joins are flush
    abacus = add_cylinder("abacus", p["capital_r_top"], p["capital_r_top"], p["abacus_height"], zc, seg, rot)
    assign_material(abacus, mats["BeaconStoneDark"])
    stone_parts.append(abacus)
    zc += p["abacus_height"]

    cap = add_cylinder("cap", p["capital_r_top"], 0.05, p["cap_height"], zc, seg, rot)
    assign_material(cap, mats["BeaconStoneDark"])
    stone_parts.append(cap)
    zc += p["cap_height"]

    # --- Crystal: stretched icosphere shard
    crystal_z = zc + p["crystal_gap"] + p["crystal_r"] * p["crystal_stretch"] * 0.5
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=p["crystal_r"],
                                          location=(0, 0, crystal_z))
    crystal = bpy.context.active_object
    crystal.scale = (1.0, 1.0, p["crystal_stretch"])
    crystal.rotation_euler = (0, 0, rng.uniform(0, math.pi))
    bpy.ops.object.shade_flat()
    assign_material(crystal, mats["BeaconCrystal"])
    glow_parts.append(crystal)

    # --- Core: the same shard, smaller, inside the crystal (no random draws, so the rest of the model is unchanged)
    if p["core_scale"]:
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=p["crystal_r"] * p["core_scale"],
                                              location=(0, 0, crystal_z))
        core = bpy.context.active_object
        core.scale = crystal.scale.copy()
        core.rotation_euler = crystal.rotation_euler.copy()
        bpy.ops.object.shade_flat()
        assign_material(core, mats["BeaconCore"])
        glow_parts.append(core)

    # --- Shards: small crystals around the main one, leaning out, turning with it (Great Beacon; no random draws)
    shards = []
    if p["shards"]:
        sh = p["shards"]
        for i in range(sh["count"]):
            a = math.pi / 4.0 + i * (2.0 * math.pi / sh["count"])
            dz = sh["dz"] if i % 2 == 0 else -sh["dz"]
            bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=sh["r"],
                                                  location=(sh["distance"] * math.cos(a), sh["distance"] * math.sin(a), crystal_z + dz))
            shard = bpy.context.active_object
            shard.scale = (1.0, 1.0, sh["stretch"])
            # lean outward: XYZ euler applies Y (lean the long axis towards +X) before Z (turn +X to the shard's direction)
            shard.rotation_euler = (0.0, math.radians(sh["tilt_deg"]), a)
            bpy.ops.object.shade_flat()
            assign_material(shard, mats["BeaconCrystal"])
            shards.append(shard)

    crystal_parts = [crystal] + ([core] if p["core_scale"] else [])
    if p["crystal_separate"]:
        for o in crystal_parts:
            if o in glow_parts:
                glow_parts.remove(o)
    else:
        glow_parts.extend(shards)
        shards = []

    # --- Weather the stone: subdivide + noise displacement, keep flat shading
    stone = join(stone_parts, "stone")
    select_only([stone], stone)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # Booleans leave ring-shaped n-gons around the niche openings; subdividing
    # those folds faces across the hole. Triangulate first so subdivision is safe
    # (and the extra facets suit the chunky stone look anyway).
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.quads_convert_to_tris(quad_method="BEAUTY", ngon_method="BEAUTY")
    bpy.ops.object.mode_set(mode="OBJECT")

    # Mask: no displacement near the alcoves so niche openings stay crisp
    vg = stone.vertex_groups.new(name="crackle_mask")
    all_idx = list(range(len(stone.data.vertices)))
    vg.add(all_idx, 1.0, "REPLACE")
    margin = 0.04
    fw = p["alcove_width"] / 2.0 + p["frame_border"] + margin
    fh = p["alcove_height"] / 2.0 + p["frame_border"] + margin
    masked = []
    for v in stone.data.vertices:
        for a, z_rel, *_ in openings:
            if abs(v.co.z - (pillar_bottom + z_rel)) > fh:
                continue
            u = -math.sin(a) * v.co.x + math.cos(a) * v.co.y   # across the face
            r = math.cos(a) * v.co.x + math.sin(a) * v.co.y    # outward
            if r > 0 and abs(u) <= fw:
                masked.append(v.index)
                break
    if p["mid_band"]:   # the pillar under the middle band stays smooth, so no bump pokes through it
        lo = pillar_bottom + p["mid_band"][0] - p["trim_height"] - margin
        hi = pillar_bottom + p["mid_band"][0] + p["mid_band"][1] + p["trim_height"] + margin
        masked.extend(v.index for v in stone.data.vertices if lo <= v.co.z <= hi)
    if masked:
        vg.add(masked, 0.0, "REPLACE")

    tex = bpy.data.textures.get("beacon_rock") or bpy.data.textures.new("beacon_rock", "CLOUDS")
    tex.noise_scale = p["stone_noise_scale"]
    tex.noise_depth = 2

    sub = stone.modifiers.new("crackle_sub", "SUBSURF")
    sub.subdivision_type = "SIMPLE"
    sub.levels = sub.render_levels = p["stone_subdiv"]
    disp = stone.modifiers.new("crackle", "DISPLACE")
    disp.texture = tex
    disp.strength = p["stone_noise_strength"]
    disp.texture_coords = "LOCAL"
    disp.vertex_group = "crackle_mask"
    select_only([stone], stone)
    for mod in list(stone.modifiers):
        bpy.ops.object.modifier_apply(modifier=mod.name)
    if p["decimate_ratio"]:
        dec = stone.modifiers.new("dec", "DECIMATE")
        dec.ratio = p["decimate_ratio"]
        bpy.ops.object.modifier_apply(modifier=dec.name)
    bpy.ops.object.shade_flat()

    # --- Final join, origin, transforms, UVs
    beacon = join([stone] + glow_parts, p["name"])
    beacon.data.name = p["name"]
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")

    tris = sum(len(poly.vertices) - 2 for poly in beacon.data.polygons)
    dims = beacon.dimensions
    print(f"[beacon] '{beacon.name}': {tris} tris, "
          f"{dims.x:.2f} x {dims.y:.2f} x {dims.z:.2f} m, "
          f"{len(beacon.data.materials)} materials")

    if p["fbx_path"]:
        if p["fbx_path"].startswith("//") and not bpy.data.filepath:
            print("[beacon] export skipped: fbx_path is relative to the .blend, and this file is not saved")
        else:
            fbx_path = bpy.path.abspath(p["fbx_path"])
            select_only([beacon], beacon)
            bpy.ops.export_scene.fbx(
                filepath=fbx_path, use_selection=True,
                apply_scale_options="FBX_SCALE_ALL", object_types={"MESH"},
                bake_anim=False)
            print(f"[beacon] exported to {fbx_path}")
    beacon.location = p["display_offset"]

    def unity(v):   # Unity's FBX import flips X (the model looks the same from the front): (-x, z, -y)
        return (round(-v[0], 4), round(v[2], 4), round(-v[1], 4))

    if p["print_attach"]:
        for i, (ax, ay, az, ca, sa) in enumerate(attach):
            print(f"[beacon] attach_{i}: unity {unity((ax, ay, az))} out {unity((ca, sa, 0.0))}")
        print(f"[beacon] total height {max(v.co.z for v in beacon.data.vertices):.3f} m (body), "
              f"crystal centre at {crystal_z:.3f} m")

    # --- The crystal and core on their own: origin at the crystal's centre, exported there, shown in place
    if p["crystal_separate"]:
        gem = join(crystal_parts, p["name"] + "_crystal")
        gem.data.name = gem.name
        bpy.context.scene.cursor.location = (0, 0, crystal_z)
        bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        bpy.context.scene.cursor.location = (0, 0, 0)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
        bpy.ops.object.mode_set(mode="OBJECT")
        gem_tris = sum(len(poly.vertices) - 2 for poly in gem.data.polygons)
        print(f"[beacon] '{gem.name}': {gem_tris} tris, {len(gem.data.materials)} materials; "
              f"centre blender (0, 0, {crystal_z:.4f}), unity (0, {crystal_z:.4f}, 0)")

        # Shards stay separate objects, children of the crystal, each with its origin at its own centre and its tilt
        # kept as its rotation, so the plugin can spin and bob each about its own long axis.
        for i, shard in enumerate(shards):
            shard.name = f"{p['name']}_shard_{i}"
            shard.data.name = shard.name
            select_only([shard], shard)
            bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
            bpy.ops.object.mode_set(mode="OBJECT")
            world = shard.matrix_world.copy()
            shard.parent = gem
            shard.matrix_world = world
            print(f"[beacon] '{shard.name}': offset from the crystal, unity "
                  f"({-shard.location.x:.3f}, {shard.location.z:.3f}, {-shard.location.y:.3f})")

        if p["crystal_fbx_path"]:
            if p["crystal_fbx_path"].startswith("//") and not bpy.data.filepath:
                print("[beacon] crystal export skipped: the .blend is not saved")
            else:
                gem_path = bpy.path.abspath(p["crystal_fbx_path"])
                gem.location = (0.0, 0.0, 0.0)
                select_only([gem] + shards, gem)
                bpy.ops.export_scene.fbx(
                    filepath=gem_path, use_selection=True,
                    apply_scale_options="FBX_SCALE_ALL", object_types={"MESH"},
                    bake_anim=False)
                print(f"[beacon] exported to {gem_path}")
        ox, oy, oz = p["display_offset"]
        gem.location = (ox, oy, oz + crystal_z)

    return beacon


if __name__ == "__main__":
    build()        # the Build Beacon
    build(GREAT)   # the Great Beacon