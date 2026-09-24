"""
xai_bossholder_wall.py - Procedural wall-mounted boss trophy holder for BuildBeacon (v1).

A pointy-top hexagon (points up and down, flat sides left and right), 2 m point to point, mounted flat on a wall:
an angled extrusion of about 10 cm out of the wall (sloped sides, like the beacon's plinth tiers) whose face is a
rim around a flat plate recessed into it, and an iron hook in the middle of the plate for a boss head. Small
glowing runes run along the rim, three per side.

Axes: Blender Z up; the wall is the XZ plane at y = 0 and the holder comes out towards Blender -Y, which becomes
Unity +Z after the FBX import (the beacon's model child is rotated -90 degrees about X).
Origin: centre of the back face, where it touches the wall.
Snap points (empties, on the back plane): centre, left and right (midpoints of the flat sides) and top and bottom
(the points). Side by side, the flats meet exactly; stacked top to bottom, two hexes touch point to point only.
Result: one object "xai_bossholder_wall", an empty "attach_trophy" at the hook and five "snappoint_*" empties.
Re-running is idempotent. Tweak PARAMS and re-run.
"""

import bpy
import bmesh
import math
import random

PARAMS = {
    "name": "xai_bossholder_wall",
    "radius":       1.0,    # circumradius: 2 m point to point, 1.73 m flat to flat
    "depth":        0.10,   # angled extrusion out of the wall
    "side_inset":   0.06,   # how far each edge of the front face sits in from the back edge (the slope)
    "plate_border": 0.08,   # width of the rim around the plate
    "plate_recess": 0.03,   # how far the plate sits back from the rim

    # Iron hook in the plate's centre: straight out, then up (an L that holds the trophy)
    "hook_out":       0.14,
    "hook_up":        0.07,
    "hook_thickness": 0.03,
    "hook_base_r":    0.05,  # round boss where the hook enters the plate

    # Runes on the rim: evenly spaced along each of the six sides, lying on the rim, tops pointing outward
    "rim_runes_per_side": 3,
    "rim_rune_height": 0.05,
    "rim_rune_stroke": 0.009,
    "rim_rune_out":    0.008,
    "seed":            17,

    "stone_subdiv":   2,
    "stone_noise_scale": 0.35,
    "stone_noise_strength": 0.012,  # gentle: large flat faces show noise more than the pillar does

    # Scene placement for the mockup (lifted and beside the beacon); an export must zero this first.
    "display_offset": (5.4, 0.0, 1.3),
    # Exported into the Unity project on every run, like the beacon ("//" is relative to totem.blend).
    "fbx_path": "//../BuildBeaconUnity/Assets/Beacon/xai_bossholder_wall.fbx",
}

MATERIALS = {
    "BeaconStoneDark":  ((0.45, 0.43, 0.42, 1.0), 0.95),
    "BeaconStoneLight": ((0.887, 0.862, 0.824, 1.0), 0.90),
    "HolderIron":       ((0.10, 0.10, 0.11, 1.0), 0.45),
    "BeaconRune":       ((0.02, 0.10, 0.12, 1.0), 0.40),
}
RUNE_EMISSION = ((0.036, 0.429, 1.00, 1.0), 1.4)   # preview glow, as the beacon's palette

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


def make_material(name, base_color, roughness):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = base_color
    bsdf.inputs["Roughness"].default_value = roughness
    if name == "HolderIron" and "Metallic" in bsdf.inputs:
        bsdf.inputs["Metallic"].default_value = 0.8
    if name == "BeaconRune":
        bsdf.inputs["Emission Color"].default_value = RUNE_EMISSION[0]
        bsdf.inputs["Emission Strength"].default_value = RUNE_EMISSION[1]
        mat.diffuse_color = RUNE_EMISSION[0]
    else:
        mat.diffuse_color = base_color
    return mat


def hex_points(radius):
    """Pointy-top hexagon in the wall plane (x across, z up): corners at 90, 30, -30, -90, -150, 150 degrees."""
    return [(radius * math.cos(math.radians(90 - 60 * k)), radius * math.sin(math.radians(90 - 60 * k))) for k in range(6)]


def hex_frame_with_plate(name, r_back, r_rim, r_plate, y_rim, y_plate, frame_mat, plate_mat):
    """
    One closed hexagonal body: back face on the wall (y = 0), sloped outer sides out to the rim at y_rim, a flat rim
    from r_rim in to r_plate, inner walls stepping back to y_plate, and the recessed plate. The plate face gets
    plate_mat; everything else frame_mat.
    """
    mesh = bpy.data.meshes.new(name)
    mesh.materials.append(frame_mat)
    mesh.materials.append(plate_mat)
    bm = bmesh.new()
    back = [bm.verts.new((x, 0.0, z)) for x, z in hex_points(r_back)]
    rim_out = [bm.verts.new((x, y_rim, z)) for x, z in hex_points(r_rim)]
    rim_in = [bm.verts.new((x, y_rim, z)) for x, z in hex_points(r_plate)]
    pocket = [bm.verts.new((x, y_plate, z)) for x, z in hex_points(r_plate)]
    bm.faces.new(back)
    for i in range(6):
        j = (i + 1) % 6
        bm.faces.new([back[j], back[i], rim_out[i], rim_out[j]])      # sloped outer side
        bm.faces.new([rim_out[j], rim_out[i], rim_in[i], rim_in[j]])  # flat rim
        bm.faces.new([rim_in[j], rim_in[i], pocket[i], pocket[j]])    # inner wall of the recess
    plate = bm.faces.new(list(reversed(pocket)))
    plate.material_index = 1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def add_box(name, center, size):
    bpy.ops.mesh.primitive_cube_add(location=center)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (size[0] / 2.0, size[1] / 2.0, size[2] / 2.0)
    return obj


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


def rim_glyph(strokes, center, across, up, y_face, height, stroke_w, depth, mat):
    """
    One glyph lying on the rim (the plane y = y_face, facing -Y). across/up are unit (x, z) directions in that plane:
    along the edge and outward. Strokes are cuboids in the plane, raised by depth towards -Y.
    """
    cubes = []
    for (u1, v1), (u2, v2) in strokes:
        a = ((u1 * across[0] + (v1 - 0.5) * up[0]) * height, (u1 * across[1] + (v1 - 0.5) * up[1]) * height)
        b = ((u2 * across[0] + (v2 - 0.5) * up[0]) * height, (u2 * across[1] + (v2 - 0.5) * up[1]) * height)
        dx, dz = b[0] - a[0], b[1] - a[1]
        length = math.hypot(dx, dz)
        mx, mz = center[0] + (a[0] + b[0]) / 2.0, center[1] + (a[1] + b[1]) / 2.0
        bpy.ops.mesh.primitive_cube_add(location=(mx, y_face - depth / 2.0, mz))
        cube = bpy.context.active_object
        cube.scale = (length / 2.0 + stroke_w / 2.0, depth / 2.0, stroke_w / 2.0)
        cube.rotation_euler = (0.0, math.atan2(-dz, dx), 0.0)   # local X along the stroke, in the rim plane
        cubes.append(cube)
    glyph = join(cubes, "rune")
    glyph.data.materials.clear()
    glyph.data.materials.append(mat)
    return glyph


def weather(obj, p, keep_flat=None):
    """Crackled stone; keep_flat(co) marks vertices left undisplaced (the rim), so the noise cannot bury the runes."""
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


def add_empty(name, location, parent, display="PLAIN_AXES", size=0.12):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = display
    e.empty_display_size = size
    e.location = location
    bpy.context.scene.collection.objects.link(e)
    e.parent = parent
    return e


def build(p=PARAMS):
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    clean_previous(p["name"])
    mats = {name: make_material(name, *spec) for name, spec in MATERIALS.items()}
    cos30 = math.cos(math.radians(30))

    r = p["radius"]
    r_front = r - p["side_inset"] / cos30            # each edge moved in by side_inset
    r_plate = r_front - p["plate_border"] / cos30
    y_face = -p["depth"]                             # the rim, furthest out
    y_plate = y_face + p["plate_recess"]             # the plate, set back into the rim

    stone = hex_frame_with_plate("stone", r, r_front, r_plate, y_face, y_plate,
                                 mats["BeaconStoneDark"], mats["BeaconStoneLight"])
    weather(stone, p, keep_flat=lambda co: abs(co.y - y_face) < 1e-4)   # the rim ring stays flat for its runes

    # --- Runes on the rim: along each side's midline, evenly spaced, tops pointing away from the centre
    runes = []
    deck = rune_deck(p["seed"])
    r_mid = (r_front + r_plate) / 2.0
    corners = hex_points(r_mid)
    n = p["rim_runes_per_side"]
    for k in range(6):
        (x0, z0), (x1, z1) = corners[k], corners[(k + 1) % 6]
        ex, ez = x1 - x0, z1 - z0
        el = math.hypot(ex, ez)
        across = (ex / el, ez / el)
        mx, mz = (x0 + x1) / 2.0, (z0 + z1) / 2.0
        ml = math.hypot(mx, mz)
        up = (mx / ml, mz / ml)
        # Read left to right for someone facing the plate (from -Y, +X to their right): with up = outward, across must
        # be up turned clockwise in that view, i.e. across x up > 0 in (x, z).
        if across[0] * up[1] - across[1] * up[0] < 0:
            across = (-across[0], -across[1])
        for i in range(n):
            t = (i + 1) / (n + 1)
            center = (x0 + ex * t, z0 + ez * t)
            runes.append(rim_glyph(RUNES[next(deck)], center, across, up, y_face,
                                   p["rim_rune_height"], p["rim_rune_stroke"], p["rim_rune_out"], mats["BeaconRune"]))

    # --- Iron hook: a round boss on the plate, a bar straight out, then up
    t = p["hook_thickness"]
    iron = []
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=p["hook_base_r"], depth=0.03,
                                        location=(0, y_plate - 0.015, 0), rotation=(math.pi / 2, 0, 0))
    iron.append(bpy.context.active_object)
    y_end = y_plate - p["hook_out"]
    iron.append(add_box("hook_out", (0, (y_plate + y_end) / 2.0, 0), (t, p["hook_out"], t)))
    iron.append(add_box("hook_up", (0, y_end, p["hook_up"] / 2.0), (t, t, p["hook_up"] + t)))
    iron_obj = join(iron, "iron")
    iron_obj.data.materials.clear()
    iron_obj.data.materials.append(mats["HolderIron"])
    select_only([iron_obj], iron_obj)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.shade_flat()

    holder = join([stone, iron_obj] + runes, p["name"])
    holder.data.name = p["name"]
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")

    # Trophy hangs on the hook, in front of the plate; snap points on the back plane where the holder meets the wall.
    add_empty(p["name"] + "_attach_trophy", (0, y_end - t / 2.0, t / 2.0), holder, "ARROWS", 0.15)
    w = r * cos30
    for label, loc in (("center", (0, 0, 0)), ("left", (-w, 0, 0)), ("right", (w, 0, 0)),
                       ("top", (0, 0, r)), ("bottom", (0, 0, -r))):
        add_empty(f"{p['name']}_snappoint_{label}", loc, holder)

    # Export at the origin (the display offset is for the Blender scene only), meshes only, as the beacon does.
    if p["fbx_path"]:
        if p["fbx_path"].startswith("//") and not bpy.data.filepath:
            print("[wall] export skipped: fbx_path is relative to the .blend, and this file is not saved")
        else:
            fbx_path = bpy.path.abspath(p["fbx_path"])
            holder.location = (0.0, 0.0, 0.0)
            select_only([holder], holder)
            bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=True, apply_scale_options="FBX_SCALE_ALL",
                                     object_types={"MESH"}, bake_anim=False)
            print(f"[wall] exported to {fbx_path}")

    holder.location = p["display_offset"]

    tris = sum(len(poly.vertices) - 2 for poly in holder.data.polygons)
    d = holder.dimensions
    print(f"[wall] '{holder.name}': {tris} tris, {d.x:.2f} x {d.y:.2f} x {d.z:.2f} m (x across, y out of the wall, z up), "
          f"rim at {y_face:.2f}, plate recessed to {y_plate:.3f}, hook to {y_end:.3f} m out")
    return holder


if __name__ == "__main__":
    build()
