"""
xai_bossholder_pillar.py - Procedural boss trophy pillar for BuildBeacon (v1).

A smaller sibling of the Build Beacon: a two-tier plinth, a short octagonal shaft (about half the beacon's height)
with vertical lines of glowing runes on four alternating faces (front, right, back, left), a much smaller capital, then an iron pole rising from the
cap with a hook on top for a boss head. No alcoves, no rune band, no crystal.

Axes: Blender Z up; the front (where the hook reaches and the trophy faces) is Blender -Y, which becomes Unity +Z
after the FBX import (see the beacon: the model child is rotated -90 degrees about X).
Result: one object "xai_bossholder_pillar", origin at ground centre, plus an empty "attach_trophy" at the hook.
Re-running is idempotent. Tweak PARAMS and re-run.
"""

import bpy
import math
import random

PARAMS = {
    "name": "xai_bossholder_pillar",
    "segments": 8,
    "seed": 11,

    # Plinth: fewer, smaller tiers than the beacon (0.80/0.64/0.50 m radius there)
    "base_tiers": [(0.52, 0.16), (0.42, 0.12)],

    # Shaft: the beacon's is 1.48 m tall, radius 0.37 -> 0.31
    "pillar_r_bottom": 0.27,
    "pillar_r_top":    0.23,
    "pillar_height":   0.86,

    # Capital, much smaller than the beacon's (capital 0.38 -> 0.54, cap 0.26 tall there)
    "ring_r_extra":    0.03,
    "ring_height":     0.04,
    "capital_r_bottom": 0.25,
    "capital_r_top":   0.33,
    "capital_height":  0.10,
    "abacus_height":   0.05,
    "cap_height":      0.12,

    # Iron pole from the cap apex, with a ball finial and a J hook reaching to the front (-Y)
    "pole_r":          0.028,
    "pole_height":     0.95,   # above the cap apex
    "finial_r":        0.05,
    "hook_z_below_top": 0.07,  # the hook arm leaves the pole this far below its top
    "hook_arm":        0.20,   # forward reach
    "hook_drop":       0.10,   # down from the arm's end
    "hook_foot":       0.05,   # forward again at the bottom
    "hook_tip":        0.06,   # up at the end: the J that holds the trophy
    "hook_thickness":  0.026,

    # Runes: a vertical line on each of rune_faces alternating shaft faces, starting with the front (-Y) and going
    # round every 360/rune_faces degrees; raised glowing strokes like the beacon's band
    "rune_faces":      4,
    "runes":           5,       # per face
    "rune_height":     0.11,
    "rune_stroke":     0.016,
    "rune_out":        0.014,   # relief above the face
    "rune_margin":     0.10,    # clear shaft above and below the line

    # Stone weathering, as on the beacon but gentler at this size
    "stone_subdiv":    2,
    "stone_noise_scale": 0.35,
    "stone_noise_strength": 0.03,

    # Where the finished object sits in the scene so it does not overlap the beacon. The mesh itself stays built
    # around the origin; an export must zero this location first.
    "display_offset": (2.6, 0.0, 0.0),
    # Exported into the Unity project on every run, like the beacon ("//" is relative to totem.blend).
    "fbx_path": "//../BuildBeaconUnity/Assets/Beacon/xai_bossholder_pillar.fbx",
}

# Preview palette; the stone names match the beacon's Unity materials so the plugin's MaterialTemplates dress them.
# HolderIron is new: it needs its own MaterialTemplates entry before export, or the plugin falls back to stone.
MATERIALS = {
    "BeaconStoneDark":  ((0.45, 0.43, 0.42, 1.0), 0.95, None, 0.0),
    "BeaconStoneLight": ((0.887, 0.862, 0.824, 1.0), 0.90, None, 0.0),
    "HolderIron":       ((0.10, 0.10, 0.11, 1.0), 0.45, None, 0.0),
    "BeaconRune":       ((0.02, 0.10, 0.12, 1.0), 0.40, (0.036, 0.429, 1.00, 1.0), 1.4),
}

# Elder Futhark glyphs as straight strokes ((u1,v1),(u2,v2)) in a unit box (u: -0.5..0.5 across, v: 0..1 up),
# the same set as the beacon's rune band.
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


def rune_deck(seed):
    """Glyph names shuffled like a deck, without the single-stroke isa (a bare line reads as a dash); repeats only
    once all have been dealt."""
    rng = random.Random(seed)
    names = [n for n in RUNES if n != "isa"]
    while True:
        rng.shuffle(names)
        for n in names:
            yield n


def clean_previous(name):
    for obj in list(bpy.data.objects):
        if obj.name == name or obj.name.startswith(name + ".") or obj.name.startswith(name + "_"):
            bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)


def make_material(name, base_color, roughness, emission, emission_strength):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = base_color
    bsdf.inputs["Roughness"].default_value = roughness
    if name == "HolderIron" and "Metallic" in bsdf.inputs:
        bsdf.inputs["Metallic"].default_value = 0.8
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = emission
        bsdf.inputs["Emission Strength"].default_value = emission_strength
        mat.diffuse_color = emission
    else:
        mat.diffuse_color = base_color
    return mat


def add_cylinder(name, r_bottom, r_top, height, z_bottom, segments, rot):
    bpy.ops.mesh.primitive_cone_add(vertices=segments, radius1=r_bottom, radius2=r_top, depth=height,
                                    location=(0, 0, z_bottom + height / 2.0), rotation=(0, 0, rot))
    obj = bpy.context.active_object
    obj.name = name
    bpy.ops.object.shade_flat()
    return obj


def add_box(name, center, size):
    bpy.ops.mesh.primitive_cube_add(location=center)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (size[0] / 2.0, size[1] / 2.0, size[2] / 2.0)
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


def add_rune_glyph(strokes, face_angle, apothem, u_offset, z_base, height, stroke_w, depth, mat):
    """One glyph as thin stroke cuboids on a flat face whose outward normal is at face_angle (0 = +X)."""
    cubes = []
    for (u1, v1), (u2, v2) in strokes:
        du, dv = (u2 - u1) * height, (v2 - v1) * height
        length = math.hypot(du, dv)
        phi = math.atan2(dv, du) - math.pi / 2.0
        um, vm = (u1 + u2) / 2.0 * height, (v1 + v2) / 2.0 * height
        px, py, pz = apothem + depth / 2.0, u_offset + um, z_base + vm   # canonical: face looking along +X
        ca, sa = math.cos(face_angle), math.sin(face_angle)
        bpy.ops.mesh.primitive_cube_add(location=(px * ca - py * sa, px * sa + py * ca, pz))
        cube = bpy.context.active_object
        cube.scale = (depth / 2.0, stroke_w / 2.0, length / 2.0 + stroke_w / 2.0)
        cube.rotation_euler = (phi, 0.0, face_angle)
        cubes.append(cube)
    glyph = join(cubes, "rune")
    assign_material(glyph, mat)
    return glyph


def weather(obj, p, keep_flat=None):
    """
    Triangulate, then subdivide + cloud-noise displacement, flat shaded: the beacon's crackled stone.
    keep_flat(co) marks vertices to leave undisplaced (the rune face), so the noise cannot bury the runes.
    """
    select_only([obj], obj)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.quads_convert_to_tris(quad_method="BEAUTY", ngon_method="BEAUTY")
    bpy.ops.object.mode_set(mode="OBJECT")
    vg = obj.vertex_groups.new(name="crackle_mask")
    vg.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
    if keep_flat is not None:
        flat = [v.index for v in obj.data.vertices if keep_flat(v.co)]
        if flat:
            vg.add(flat, 0.0, "REPLACE")
    tex = bpy.data.textures.get("beacon_rock") or bpy.data.textures.new("beacon_rock", "CLOUDS")
    tex.noise_scale = p["stone_noise_scale"]
    tex.noise_depth = 2
    sub = obj.modifiers.new("crackle_sub", "SUBSURF")
    sub.subdivision_type = "SIMPLE"
    sub.levels = sub.render_levels = p["stone_subdiv"]
    disp = obj.modifiers.new("crackle", "DISPLACE")
    disp.texture = tex
    disp.strength = p["stone_noise_strength"]
    disp.texture_coords = "LOCAL"
    disp.vertex_group = "crackle_mask"
    for mod in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.object.shade_flat()


def build(p=PARAMS):
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    clean_previous(p["name"])
    random.seed(p["seed"])
    mats = {name: make_material(name, *spec) for name, spec in MATERIALS.items()}
    seg = p["segments"]
    rot = math.pi / seg

    stone = []
    z = 0.0
    for i, (radius, height) in enumerate(p["base_tiers"]):
        tier = add_cylinder(f"tier{i}", radius, radius * 0.92, height, z, seg, rot)
        assign_material(tier, mats["BeaconStoneDark"] if i == 0 else mats["BeaconStoneLight"])
        stone.append(tier)
        z += height

    shaft = add_cylinder("shaft", p["pillar_r_bottom"], p["pillar_r_top"], p["pillar_height"], z, seg, rot)
    assign_material(shaft, mats["BeaconStoneLight"])
    stone.append(shaft)
    shaft_bottom = z
    z += p["pillar_height"]
    shaft_top = z
    cos_ap = math.cos(math.pi / seg)

    def shaft_r(zz):
        t = max(0.0, min(1.0, (zz - shaft_bottom) / p["pillar_height"]))
        return p["pillar_r_bottom"] * (1 - t) + p["pillar_r_top"] * t

    # Faces that carry runes: the front (-Y) and every 360/rune_faces degrees round from it. Each must be a flat face
    # of the octagon (multiples of 45 degrees from the front), which 1, 2, 4 and 8 faces all are.
    rune_face_angles = [-math.pi / 2.0 + k * 2.0 * math.pi / p["rune_faces"] for k in range(p["rune_faces"])]

    def on_rune_face(co):
        # Near one of those faces' planes, within its width, between the shaft's ends.
        if not (shaft_bottom - 1e-4 <= co.z <= shaft_top + 1e-4):
            return False
        half_w = shaft_r(co.z) * math.tan(math.pi / seg) + 0.01
        for a in rune_face_angles:
            out = co.x * math.cos(a) + co.y * math.sin(a)
            across = -co.x * math.sin(a) + co.y * math.cos(a)
            if out >= shaft_r(co.z) * cos_ap - 0.02 and abs(across) <= half_w:
                return True
        return False

    ring_r = p["pillar_r_top"] + p["ring_r_extra"]
    ring = add_cylinder("ring", ring_r, ring_r, p["ring_height"], z, seg, rot)
    assign_material(ring, mats["BeaconStoneDark"])
    stone.append(ring)
    z += p["ring_height"]

    capital = add_cylinder("capital", p["capital_r_bottom"], p["capital_r_top"], p["capital_height"], z, seg, rot)
    assign_material(capital, mats["BeaconStoneDark"])
    stone.append(capital)
    z += p["capital_height"]

    abacus = add_cylinder("abacus", p["capital_r_top"], p["capital_r_top"], p["abacus_height"], z, seg, rot)
    assign_material(abacus, mats["BeaconStoneDark"])
    stone.append(abacus)
    z += p["abacus_height"]

    cap = add_cylinder("cap", p["capital_r_top"], 0.04, p["cap_height"], z, seg, rot)
    assign_material(cap, mats["BeaconStoneDark"])
    stone.append(cap)
    stone_top = z + p["cap_height"]

    stone_obj = join(stone, "stone")
    weather(stone_obj, p, keep_flat=on_rune_face)

    # --- Runes: a vertical line centred on each rune face, evenly spaced between the margins
    runes = []
    deck = rune_deck(p["seed"])
    usable = p["pillar_height"] - 2 * p["rune_margin"]
    step = (usable - p["rune_height"]) / max(1, p["runes"] - 1)
    for face_angle in rune_face_angles:
        for i in range(p["runes"]):
            z_base = shaft_bottom + p["rune_margin"] + i * step
            apothem = shaft_r(z_base + p["rune_height"] / 2.0) * cos_ap
            runes.append(add_rune_glyph(RUNES[next(deck)], face_angle, apothem, 0.0, z_base,
                                        p["rune_height"], p["rune_stroke"], p["rune_out"], mats["BeaconRune"]))

    # --- Iron pole and hook (not weathered: crisp metal against the crackled stone)
    iron = []
    pole_bottom = stone_top - p["cap_height"] * 0.5   # sunk into the cap so the joint hides
    pole_top = stone_top + p["pole_height"]
    pole = add_cylinder("pole", p["pole_r"], p["pole_r"] * 0.85, pole_top - pole_bottom, pole_bottom, 8, rot)
    iron.append(pole)

    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=p["finial_r"], location=(0, 0, pole_top))
    finial = bpy.context.active_object
    bpy.ops.object.shade_flat()
    iron.append(finial)

    t = p["hook_thickness"]
    arm_z = pole_top - p["hook_z_below_top"]
    arm_end = -p["hook_arm"]
    iron.append(add_box("hook_arm", (0, arm_end / 2.0, arm_z), (t, p["hook_arm"] + t, t)))
    drop_bottom = arm_z - p["hook_drop"]
    iron.append(add_box("hook_drop", (0, arm_end, (arm_z + drop_bottom) / 2.0), (t, t, p["hook_drop"] + t)))
    foot_end = arm_end - p["hook_foot"]
    iron.append(add_box("hook_foot", (0, (arm_end + foot_end) / 2.0, drop_bottom), (t, p["hook_foot"] + t, t)))
    iron.append(add_box("hook_tip", (0, foot_end, drop_bottom + p["hook_tip"] / 2.0), (t, t, p["hook_tip"] + t)))

    iron_obj = join(iron, "iron")
    assign_material(iron_obj, mats["HolderIron"])
    select_only([iron_obj], iron_obj)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.shade_flat()

    # --- Final object: origin at ground centre, UVs, then the display offset
    holder = join([stone_obj, iron_obj] + runes, p["name"])
    holder.data.name = p["name"]
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")

    # Where the trophy hangs: in the J of the hook, facing the front (-Y).
    attach = bpy.data.objects.new(p["name"] + "_attach_trophy", None)
    attach.empty_display_type = "ARROWS"
    attach.empty_display_size = 0.15
    attach.location = (0, foot_end + t, drop_bottom + t)
    bpy.context.scene.collection.objects.link(attach)
    attach.parent = holder

    # Export at the origin (the display offset is for the Blender scene only), meshes only, as the beacon does.
    if p["fbx_path"]:
        if p["fbx_path"].startswith("//") and not bpy.data.filepath:
            print("[pillar] export skipped: fbx_path is relative to the .blend, and this file is not saved")
        else:
            fbx_path = bpy.path.abspath(p["fbx_path"])
            holder.location = (0.0, 0.0, 0.0)
            select_only([holder], holder)
            bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=True, apply_scale_options="FBX_SCALE_ALL",
                                     object_types={"MESH"}, bake_anim=False)
            print(f"[pillar] exported to {fbx_path}")

    holder.location = p["display_offset"]

    tris = sum(len(poly.vertices) - 2 for poly in holder.data.polygons)
    d = holder.dimensions
    print(f"[pillar] '{holder.name}': {tris} tris, {d.x:.2f} x {d.y:.2f} x {d.z:.2f} m, "
          f"stone top {stone_top:.2f} m, hook arm at {arm_z:.2f} m, attach at {tuple(round(v, 3) for v in attach.location)}")
    return holder


if __name__ == "__main__":
    build()
