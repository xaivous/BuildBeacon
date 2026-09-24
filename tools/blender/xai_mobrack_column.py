"""
xai_mobrack_column.py - Procedural Trophy Column for BuildBeacon (v1): a column with four creature trophy alcoves.

A 2 m octagonal column with a symmetric profile: the stepped plinth and its flare at the bottom are mirrored exactly at
the top into a capital. Four framed alcoves (the beacon's recipe: a raised stone frame, a niche cut through it by a
boolean, no weathering near the openings) sit around the middle of the shaft, one per side. With PARAMS["runes"] on,
glowing runes wrap round the shaft in two rings, one above the plinth flare and one under the capital's, across all
eight faces like the beacon's band but set straight into the shaft, and the shaft is left unweathered where the rings
run so the runes stay clear. The runes are off: plain stone.

Axes: Blender Z up; the front is -Y, which becomes Unity +Z after the FBX import (the model child is rotated -90
degrees about X in the prefab). Unity position = (-x, z, -y): the import flips X.
Origin: the centre of the base, on the ground.
Alcoves: attach_0 faces the front (-Y), then attach_1 +X, attach_2 +Y, attach_3 -X. Each attach empty sits at the
back of its niche; the trophy's back rests there, facing out.
Snap points: snappoint_bottom at the base centre and snappoint_top at the top centre, so columns stack.
Empties are not exported (the FBX is meshes only); the Unity prefab recreates them from the printed positions.
Result: one object "xai_mobrack_column" plus its empties. Re-running is idempotent. Tweak PARAMS and re-run.
"""

import bpy
import math
import random

PARAMS = {
    "name": "xai_mobrack_column",
    "segments": 8,
    "seed": 23,

    # Bottom half, mirrored for the top half. Heights add up to 2.0 m: 2 x (plinth + flare) + shaft.
    "plinth_tiers": [(0.46, 0.12), (0.40, 0.10)],   # (radius, height) from the ground up; each tapers by 8%
    "flare_height": 0.12,        # from the upper plinth tier in to the shaft
    "shaft_r":      0.28,
    "height":       2.00,        # total; the shaft takes what the two ends leave

    # Alcoves: the beacon's niche (0.20 x 0.42), slightly shallower so the four do not meet inside the thinner shaft
    "alcove_width":  0.20,
    "alcove_height": 0.42,
    "alcove_depth":  0.14,
    "frame_border":  0.045,
    "frame_out":     0.030,

    # Runes set into the shaft in two rings, across all eight faces, mirrored top and bottom. Off since 2026-09-24
    # (the user: no runes on the creature trophy pieces); with them off the shaft weathers evenly.
    "runes":          False,
    "runes_per_face": 2,
    "ring_offset":    0.13,      # from each end of the shaft to the ring's centre
    "rune_height":    0.09,
    "rune_stroke":    0.014,
    "rune_out":       0.012,     # relief above the face

    "stone_subdiv":   1,     # 2 doubled the beacon-sized tri count; 1 keeps it near the boss pillar's
    "stone_noise_scale": 0.35,
    "stone_noise_strength": 0.015,

    # Scene placement for the mockup (beside the boss holders); an export zeroes it first.
    "display_offset": (7.8, 0.0, 0.0),
    "fbx_path": "//../BuildBeaconUnity/Assets/Beacon/xai_mobrack_column.fbx",
}

MATERIALS = {
    "BeaconStoneDark":  ((0.45, 0.43, 0.42, 1.0), 0.95, None, 0.0),
    "BeaconStoneLight": ((0.887, 0.862, 0.824, 1.0), 0.90, None, 0.0),
    "BeaconRune":       ((0.02, 0.10, 0.12, 1.0), 0.40, (0.036, 0.429, 1.00, 1.0), 1.4),
}

RUNES = {
    "fehu":     [((-0.20, 0.0), (-0.20, 1.0)), ((-0.20, 0.95), (0.30, 0.70)), ((-0.20, 0.60), (0.30, 0.35))],
    "uruz":     [((-0.25, 0.0), (-0.25, 1.0)), ((-0.25, 1.0), (0.25, 0.55)), ((0.25, 0.55), (0.25, 0.0))],
    "thurisaz": [((-0.18, 0.0), (-0.18, 1.0)), ((-0.18, 0.80), (0.28, 0.55)), ((0.28, 0.55), (-0.18, 0.30))],
    "ansuz":    [((-0.18, 0.0), (-0.18, 1.0)), ((-0.18, 1.0), (0.30, 0.68)), ((-0.18, 0.72), (0.30, 0.40))],
    "raido":    [((-0.20, 0.0), (-0.20, 1.0)), ((-0.20, 1.0), (0.25, 0.75)), ((0.25, 0.75), (-0.20, 0.50)), ((-0.20, 0.50), (0.28, 0.0))],
    "kenaz":    [((0.25, 1.0), (-0.20, 0.5)), ((-0.20, 0.5), (0.25, 0.0))],
    "hagalaz":  [((-0.25, 0.0), (-0.25, 1.0)), ((0.25, 0.0), (0.25, 1.0)), ((-0.25, 0.65), (0.25, 0.40))],
    "tiwaz":    [((0.0, 0.0), (0.0, 1.0)), ((0.0, 1.0), (-0.30, 0.70)), ((0.0, 1.0), (0.30, 0.70))],
    "sowilo":   [((0.22, 1.0), (-0.18, 0.62)), ((-0.18, 0.62), (0.22, 0.38)), ((0.22, 0.38), (-0.18, 0.0))],
    "algiz":    [((0.0, 0.0), (0.0, 1.0)), ((0.0, 0.55), (-0.30, 0.90)), ((0.0, 0.55), (0.30, 0.90))],
    "gebo":     [((-0.25, 0.05), (0.25, 0.95)), ((-0.25, 0.95), (0.25, 0.05))],
}


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
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = emission
        bsdf.inputs["Emission Strength"].default_value = emission_strength
        mat.diffuse_color = emission
    else:
        mat.diffuse_color = base_color
    return mat


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


def assign(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    return obj


def ring(name, r_bottom, r_top, height, z_bottom, segments, mat):
    """An octagonal frustum with flat faces on the axes."""
    bpy.ops.mesh.primitive_cone_add(vertices=segments, radius1=r_bottom, radius2=r_top, depth=height,
                                    location=(0, 0, z_bottom + height / 2.0), rotation=(0, 0, math.pi / segments))
    obj = bpy.context.active_object
    obj.name = name
    return assign(obj, mat)


def add_empty(name, location, parent, display="PLAIN_AXES", size=0.12):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = display
    e.empty_display_size = size
    e.location = location
    bpy.context.scene.collection.objects.link(e)
    e.parent = parent
    return e


def shaft_glyph(strokes, face_angle, apothem, u_offset, z_base, p, mat):
    """One glyph as raised stroke cuboids on the shaft face at face_angle (0 = the +X face)."""
    cubes = []
    s, w, depth = p["rune_height"], p["rune_stroke"], p["rune_out"]
    ca, sa = math.cos(face_angle), math.sin(face_angle)
    for (u1, v1), (u2, v2) in strokes:
        du, dv = (u2 - u1) * s, (v2 - v1) * s
        length = math.hypot(du, dv)
        phi = math.atan2(dv, du) - math.pi / 2.0
        px, py, pz = apothem + depth / 2.0, u_offset + (u1 + u2) / 2.0 * s, z_base + (v1 + v2) / 2.0 * s
        bpy.ops.mesh.primitive_cube_add(location=(px * ca - py * sa, px * sa + py * ca, pz))
        cube = bpy.context.active_object
        cube.scale = (depth / 2.0, w / 2.0, length / 2.0 + w / 2.0)
        cube.rotation_euler = (phi, 0.0, face_angle)
        cubes.append(cube)
    return assign(join(cubes, "rune"), mat)


def build(p=PARAMS):
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    clean_previous(p["name"])
    rng = random.Random(p["seed"])
    mats = {name: make_material(name, *spec) for name, spec in MATERIALS.items()}
    seg = p["segments"]
    cos_ap = math.cos(math.pi / seg)
    H = p["height"]

    stone, glow = [], []

    # --- One end (plinth tiers, then the flare into the shaft), built bottom-up; mirror=True builds the same end
    # downward from the top, so the two ends are exact mirror images.
    def build_end(mirror):
        def seg_(name, r0, r1, h, z0, mat):
            # z0 and the radii are given as seen from this end; mirrored, the piece is flipped about the middle.
            if mirror:
                return ring(name, r1, r0, h, H - z0 - h, seg, mat)
            return ring(name, r0, r1, h, z0, seg, mat)

        z = 0.0
        for i, (r, h) in enumerate(p["plinth_tiers"]):
            stone.append(seg_(f"tier{i}", r, r * 0.92, h, z, mats["BeaconStoneDark"]))
            z += h
        top_tier_r = p["plinth_tiers"][-1][0] * 0.92
        stone.append(seg_("flare", top_tier_r, p["shaft_r"], p["flare_height"], z, mats["BeaconStoneDark"]))
        z += p["flare_height"]
        return z   # height taken by this end

    end_h = build_end(False)
    build_end(True)
    shaft_h = H - 2 * end_h
    shaft = ring("shaft", p["shaft_r"], p["shaft_r"], shaft_h, end_h, seg, mats["BeaconStoneLight"])
    alcove_z = H / 2.0

    # --- Alcove frames and niches (the beacon's recipe), attach_0 facing the front (-Y), then counter-clockwise
    apothem = p["shaft_r"] * cos_ap
    fw = p["alcove_width"] + 2 * p["frame_border"]
    fh = p["alcove_height"] + 2 * p["frame_border"]
    angles = [-math.pi / 2 + i * math.pi / 2 for i in range(4)]
    frames, cutters = [], []
    for angle in angles:
        ca, sa = math.cos(angle), math.sin(angle)
        embed = 0.05
        bpy.ops.mesh.primitive_cube_add(location=(0, 0, 0))
        frame = bpy.context.active_object
        frame.scale = ((p["frame_out"] + embed) / 2.0, fw / 2.0, fh / 2.0)
        frame.rotation_euler = (0, 0, angle)
        fx = apothem + (p["frame_out"] - embed) / 2.0
        frame.location = (fx * ca, fx * sa, alcove_z)
        frames.append(assign(frame, mats["BeaconStoneDark"]))

        punch = p["frame_out"] + 0.02
        bpy.ops.mesh.primitive_cube_add(location=(0, 0, 0))
        cutter = bpy.context.active_object
        cutter.scale = ((p["alcove_depth"] + punch) / 2.0, p["alcove_width"] / 2.0, p["alcove_height"] / 2.0)
        cutter.rotation_euler = (0, 0, angle)
        cx = apothem + (punch - p["alcove_depth"]) / 2.0
        cutter.location = (cx * ca, cx * sa, alcove_z)
        cutters.append(cutter)
    for target in [shaft] + frames:
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
    stone.append(shaft)
    stone.extend(frames)

    # --- Weather the stone (triangulate first: the booleans leave n-gons round the openings), not near the niches
    body = join(stone, "stone")
    select_only([body], body)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.quads_convert_to_tris(quad_method="BEAUTY", ngon_method="BEAUTY")
    bpy.ops.object.mode_set(mode="OBJECT")
    vg = body.vertex_groups.new(name="crackle_mask")
    vg.add(list(range(len(body.data.vertices))), 1.0, "REPLACE")
    margin = 0.04
    half_w, half_h = fw / 2.0 + margin, fh / 2.0 + margin
    masked = []
    for v in body.data.vertices:
        if abs(v.co.z - alcove_z) > half_h:
            continue
        for a in angles:
            u = -math.sin(a) * v.co.x + math.cos(a) * v.co.y
            r = math.cos(a) * v.co.x + math.sin(a) * v.co.y
            if r > 0 and abs(u) <= half_w:
                masked.append(v.index)
                break
    shaft_bottom, shaft_top = end_h, H - end_h
    ring_zs = [shaft_bottom + p["ring_offset"], shaft_top - p["ring_offset"]]
    ring_half = p["rune_height"] / 2.0 + 0.03
    for v in body.data.vertices if p["runes"] else []:
        if any(abs(v.co.z - rz) <= ring_half for rz in ring_zs) and math.hypot(v.co.x, v.co.y) >= apothem - 0.01:
            masked.append(v.index)
    if masked:
        vg.add(masked, 0.0, "REPLACE")
    tex = bpy.data.textures.get("beacon_rock") or bpy.data.textures.new("beacon_rock", "CLOUDS")
    tex.noise_scale = p["stone_noise_scale"]
    tex.noise_depth = 2
    sub = body.modifiers.new("crackle_sub", "SUBSURF")
    sub.subdivision_type = "SIMPLE"
    sub.levels = sub.render_levels = p["stone_subdiv"]
    disp = body.modifiers.new("crackle", "DISPLACE")
    disp.texture = tex
    disp.strength = p["stone_noise_strength"]
    disp.texture_coords = "LOCAL"
    disp.vertex_group = "crackle_mask"
    for mod in list(body.modifiers):
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.object.shade_flat()

    # --- Runes: two rings round the shaft, runes_per_face on each of the eight faces
    names = list(RUNES)
    face_w = 2.0 * p["shaft_r"] * math.tan(math.pi / seg)
    n = p["runes_per_face"]
    for rz in ring_zs if p["runes"] else []:
        z_base = rz - p["rune_height"] / 2.0
        for i in range(seg):
            face_angle = i * (2.0 * math.pi / seg)
            for k in range(n):
                u = (k - (n - 1) / 2.0) * face_w * 0.5 / max(1, n - 1) if n > 1 else 0.0
                glow.append(shaft_glyph(RUNES[rng.choice(names)], face_angle, apothem, u, z_base, p, mats["BeaconRune"]))

    column = join([body] + glow, p["name"])
    column.data.name = p["name"]
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.shade_flat()
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")

    # --- Empties: attach points at the backs of the niches, facing out; snap points top and bottom
    attach = []
    back_r = apothem - p["alcove_depth"]
    for i, a in enumerate(angles):
        loc = (back_r * math.cos(a), back_r * math.sin(a), alcove_z)
        e = add_empty(f"{p['name']}_attach_{i}", loc, column, "ARROWS", 0.12)
        e.rotation_euler = (0, 0, a + math.pi / 2)   # empty's -Y points out of the niche
        attach.append((f"attach_{i}", loc, (math.cos(a), math.sin(a), 0.0)))
    snaps = [("snappoint_bottom", (0.0, 0.0, 0.0)), ("snappoint_top", (0.0, 0.0, H))]
    for label, loc in snaps:
        add_empty(f"{p['name']}_{label}", loc, column)

    if p["fbx_path"]:
        if p["fbx_path"].startswith("//") and not bpy.data.filepath:
            print("[column] export skipped: fbx_path is relative to the .blend, and this file is not saved")
        else:
            column.location = (0.0, 0.0, 0.0)
            select_only([column], column)
            bpy.ops.export_scene.fbx(filepath=bpy.path.abspath(p["fbx_path"]), use_selection=True,
                                     apply_scale_options="FBX_SCALE_ALL", object_types={"MESH"}, bake_anim=False)
            print(f"[column] exported to {bpy.path.abspath(p['fbx_path'])}")
    column.location = p["display_offset"]

    def unity(v):   # Unity's FBX import flips X (the model looks the same from the front): (-x, z, -y)
        return (round(-v[0], 4), round(v[2], 4), round(-v[1], 4))

    tris = sum(len(poly.vertices) - 2 for poly in column.data.polygons)
    d = column.dimensions
    print(f"[column] '{column.name}': {tris} tris, {d.x:.2f} x {d.y:.2f} x {d.z:.2f} m; shaft {shaft_h:.2f} m, "
          f"alcove centres at {alcove_z:.2f} m")
    for name, loc, out in attach:
        print(f"[column] {name}: blender {tuple(round(c, 4) for c in loc)} out {tuple(round(c, 3) for c in out)}; "
              f"unity {unity(loc)} out {unity(out)}")
    for name, loc in snaps:
        print(f"[column] {name}: unity {unity(loc)}")
    return column


if __name__ == "__main__":
    build()
