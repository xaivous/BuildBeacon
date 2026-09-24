"""
xai_mobrack_wall.py - Procedural Trophy Panel for BuildBeacon (v1): a square wall panel with four creature trophy
alcoves, meant to be tiled edge to edge across a wall.

A 2 m x 2 m slab, 12 cm deep, mounted flat on a wall: sloped outer edges (so neighbouring panels meet on their back
edges and read as tiles, with a shallow V between them), a flat rim (small glowing runes on it when PARAMS["runes"] is
on; off, plain stone), and a recessed field
holding four framed niches in a 2x2 grid. The panel, its field, the frames and the niches all have small chamfered
corners, which makes each an irregular octagon (where four panels meet, the corners leave a small diamond). Nothing
overhangs the square outline, so tiled panels meet cleanly. The niches follow the beacon's recipe (a raised frame and a boolean cut), shallow because the panel is thin: trophies are
scaled to the opening and may stand out of it.

Axes: Blender Z up; the wall is the XZ plane at y = 0 and the panel comes out towards Blender -Y, which becomes Unity
+Z after the FBX import (the model child is rotated -90 degrees about X in the prefab). Unity position = (-x, z, -y): the import flips X.
Origin: the centre of the back face, where it touches the wall (as the boss wall mount).
Alcoves, as seen from the front (+X to the viewer's right): attach_0 top left, attach_1 top right, attach_2 bottom
left, attach_3 bottom right. Each attach empty sits at the back of its niche; the trophy's back rests there, facing out.
Snap points on the back plane: snappoint_c0..c3 at the corners, snappoint_e0..e3 at the edge midpoints, and
snappoint_centre.
Empties are not exported (the FBX is meshes only); the Unity prefab recreates them from the printed positions.
Result: one object "xai_mobrack_wall" plus its empties. Re-running is idempotent. Tweak PARAMS and re-run.
"""

import bpy
import bmesh
import math
import random

PARAMS = {
    "name": "xai_mobrack_wall",
    "half":         1.00,   # half the side: a 2 m square
    "depth":        0.12,   # out of the wall
    "side_inset":   0.03,   # the front face's edge sits in from the back edge (the slope that makes the tile seam)
    "rim":          0.10,   # width of the flat rim around the field
    "corner":       0.08,   # chamfer on the panel's back outline; the rim and field follow it inward
    "field_recess": 0.02,   # the field sits back from the rim

    # Niches: one per quarter, centred in it
    "alcove_width":  0.55,
    "alcove_height": 0.65,
    "alcove_depth":  0.07,  # into the slab from the field; 3 cm of stone stays behind
    "frame_border":  0.05,
    "frame_out":     0.02,  # frames stand out of the field, flush with the rim
    "alcove_corner": 0.07,  # chamfer on the niche opening; the frame's outline follows it outward

    # Runes on the rim, evenly spaced along each side, tops pointing outward. Off since 2026-09-24 (the user: no runes
    # on the creature trophy pieces); the rim stays flat either way.
    "runes":           False,
    "rim_runes_per_side": 5,
    "rim_rune_height": 0.055,
    "rim_rune_stroke": 0.010,
    "rim_rune_out":    0.008,
    "seed":            29,

    "stone_subdiv":   1,
    "stone_noise_scale": 0.35,
    "stone_noise_strength": 0.010,   # gentle: big flat faces show noise strongly

    # Scene placement for the mockup (lifted so its bottom edge is on the ground, beside the column)
    "display_offset": (10.6, 0.0, 1.0),
    "fbx_path": "//../BuildBeaconUnity/Assets/Beacon/xai_mobrack_wall.fbx",
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


def rune_deck(seed):
    rng = random.Random(seed)
    names = list(RUNES)
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


def square(h):
    """Square corners in the wall plane (x across, z up), clockwise from the top left as seen from the front."""
    return [(-h, h), (h, h), (h, -h), (-h, -h)]


def chamfered_rect(cx, cz, half_w, half_h, c):
    """A rectangle with its corners cut at 45 degrees by c (an irregular octagon), clockwise from the top edge's left
    end as seen from the front, in the wall plane (x across, z up)."""
    return [(cx - half_w + c, cz + half_h), (cx + half_w - c, cz + half_h),
            (cx + half_w, cz + half_h - c), (cx + half_w, cz - half_h + c),
            (cx + half_w - c, cz - half_h), (cx - half_w + c, cz - half_h),
            (cx - half_w, cz - half_h + c), (cx - half_w, cz + half_h - c)]


def offset_poly(points, d):
    """Move every edge of a convex polygon in by d (out when d < 0), keeping the edges parallel."""
    n = len(points)
    # Signed area tells the winding, so "in" is the right side of each edge.
    area = sum(points[i][0] * points[(i + 1) % n][1] - points[(i + 1) % n][0] * points[i][1] for i in range(n))
    sign = -1.0 if area > 0 else 1.0
    lines = []
    for i in range(n):
        (x0, z0), (x1, z1) = points[i], points[(i + 1) % n]
        ex, ez = x1 - x0, z1 - z0
        el = math.hypot(ex, ez)
        nx, nz = sign * ez / el, -sign * ex / el            # inward normal
        lines.append(((x0 + nx * d, z0 + nz * d), (ex / el, ez / el)))
    out = []
    for i in range(n):
        (px, pz), (dx, dz) = lines[i - 1]
        (qx, qz), (ex, ez) = lines[i]
        den = dx * ez - dz * ex
        t = ((qx - px) * ez - (qz - pz) * ex) / den
        out.append((px + dx * t, pz + dz * t))
    return out


def prism(name, points, y0, y1):
    """A closed prism with the given (x, z) outline, from y0 to y1."""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    a = [bm.verts.new((x, y0, z)) for x, z in points]
    b = [bm.verts.new((x, y1, z)) for x, z in points]
    bm.faces.new(a)
    bm.faces.new(list(reversed(b)))
    n = len(points)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new([a[i], a[j], b[j], b[i]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def slab(name, back_pts, rim_pts, field_pts, y_rim, y_field, rim_mat, field_mat):
    """One closed body: back on the wall (y = 0), sloped sides out to the rim outline at y_rim, a flat rim in to the
    field outline, inner walls back to y_field, and the recessed field (field_mat). The three outlines have the same
    number of corners."""
    mesh = bpy.data.meshes.new(name)
    mesh.materials.append(rim_mat)
    mesh.materials.append(field_mat)
    bm = bmesh.new()
    back = [bm.verts.new((x, 0.0, z)) for x, z in back_pts]
    rim_out = [bm.verts.new((x, y_rim, z)) for x, z in rim_pts]
    rim_in = [bm.verts.new((x, y_rim, z)) for x, z in field_pts]
    pocket = [bm.verts.new((x, y_field, z)) for x, z in field_pts]
    bm.faces.new(back)
    n = len(back_pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new([back[j], back[i], rim_out[i], rim_out[j]])
        bm.faces.new([rim_out[j], rim_out[i], rim_in[i], rim_in[j]])
        bm.faces.new([rim_in[j], rim_in[i], pocket[i], pocket[j]])
    field = bm.faces.new(list(reversed(pocket)))
    field.material_index = 1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def box(center, size):
    bpy.ops.mesh.primitive_cube_add(location=center)
    obj = bpy.context.active_object
    obj.scale = (size[0] / 2.0, size[1] / 2.0, size[2] / 2.0)
    return obj


def rim_glyph(strokes, center, across, up, y_face, height, stroke_w, depth, mat):
    """One glyph lying on the rim (the plane y = y_face, facing -Y); across/up are unit (x, z) directions."""
    cubes = []
    for (u1, v1), (u2, v2) in strokes:
        a = ((u1 * across[0] + (v1 - 0.5) * up[0]) * height, (u1 * across[1] + (v1 - 0.5) * up[1]) * height)
        b = ((u2 * across[0] + (v2 - 0.5) * up[0]) * height, (u2 * across[1] + (v2 - 0.5) * up[1]) * height)
        dx, dz = b[0] - a[0], b[1] - a[1]
        length = math.hypot(dx, dz)
        bpy.ops.mesh.primitive_cube_add(location=(center[0] + (a[0] + b[0]) / 2.0, y_face - depth / 2.0,
                                                  center[1] + (a[1] + b[1]) / 2.0))
        cube = bpy.context.active_object
        cube.scale = (length / 2.0 + stroke_w / 2.0, depth / 2.0, stroke_w / 2.0)
        cube.rotation_euler = (0.0, math.atan2(-dz, dx), 0.0)
        cubes.append(cube)
    return assign(join(cubes, "rune"), mat)


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

    h = p["half"]
    h_rim = h - p["side_inset"]
    h_field = h_rim - p["rim"]
    y_rim = -p["depth"]
    y_field = y_rim + p["field_recess"]
    back_pts = chamfered_rect(0.0, 0.0, h, h, p["corner"])
    rim_pts = offset_poly(back_pts, p["side_inset"])
    field_pts = offset_poly(rim_pts, p["rim"])
    body = slab("stone", back_pts, rim_pts, field_pts, y_rim, y_field, mats["BeaconStoneDark"], mats["BeaconStoneLight"])

    # --- Four framed niches, one per quarter of the field
    q = h_field / 2.0
    centres = [(-q, q), (q, q), (-q, -q), (q, -q)]      # top left, top right, bottom left, bottom right
    fw = p["alcove_width"] + 2 * p["frame_border"]
    fh = p["alcove_height"] + 2 * p["frame_border"]
    y_frame_front = y_field - p["frame_out"]
    embed = 0.01
    frames, cutters = [], []
    for cx, cz in centres:
        opening = chamfered_rect(cx, cz, p["alcove_width"] / 2.0, p["alcove_height"] / 2.0, p["alcove_corner"])
        outline = offset_poly(opening, -p["frame_border"])
        f = prism("frame", outline, y_frame_front, y_field + embed)
        frames.append(assign(f, mats["BeaconStoneDark"]))
        y_back = y_field + p["alcove_depth"]
        punch = 0.02
        cutters.append(prism("niche", opening, y_frame_front - punch, y_back))
    for target in [body] + frames:
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

    # --- Weather the stone, but not the rim (runes lie on it), the frames' fronts, or around the niches
    stone = join([body] + frames, "stone")
    select_only([stone], stone)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.quads_convert_to_tris(quad_method="BEAUTY", ngon_method="BEAUTY")
    bpy.ops.object.mode_set(mode="OBJECT")
    vg = stone.vertex_groups.new(name="crackle_mask")
    vg.add(list(range(len(stone.data.vertices))), 1.0, "REPLACE")
    margin = 0.03

    def keep_flat(co):
        if abs(co.y - y_rim) < 1e-4:
            return True                                     # rim and frame fronts
        if abs(co.x) >= h_rim - 1e-4 or abs(co.z) >= h_rim - 1e-4:
            return True                                     # the outer edges, so tiles meet exactly
        return any(abs(co.x - cx) <= fw / 2.0 + margin and abs(co.z - cz) <= fh / 2.0 + margin for cx, cz in centres)

    flat = [v.index for v in stone.data.vertices if keep_flat(v.co)]
    if flat:
        vg.add(flat, 0.0, "REPLACE")
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
    for mod in list(stone.modifiers):
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.object.shade_flat()

    # --- Runes along the rim's midline, tops pointing outward, reading left to right from the front
    runes = []
    deck = rune_deck(p["seed"])
    r_mid = (h_rim + h_field) / 2.0
    corners = square(r_mid)
    n = p["rim_runes_per_side"]
    for k in range(4) if p["runes"] else []:
        (x0, z0), (x1, z1) = corners[k], corners[(k + 1) % 4]
        ex, ez = x1 - x0, z1 - z0
        el = math.hypot(ex, ez)
        across = (ex / el, ez / el)
        mx, mz = (x0 + x1) / 2.0, (z0 + z1) / 2.0
        ml = math.hypot(mx, mz)
        up = (mx / ml, mz / ml)
        if across[0] * up[1] - across[1] * up[0] < 0:
            across = (-across[0], -across[1])
        for i in range(n):
            t = (i + 1) / (n + 1)
            runes.append(rim_glyph(RUNES[next(deck)], (x0 + ex * t, z0 + ez * t), across, up, y_rim,
                                   p["rim_rune_height"], p["rim_rune_stroke"], p["rim_rune_out"], mats["BeaconRune"]))

    panel = join([stone] + runes, p["name"])
    panel.data.name = p["name"]
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")

    # --- Empties: attach points at the backs of the niches, facing out (-Y); snap points on the back plane
    y_back = y_field + p["alcove_depth"]
    attach = []
    for i, (cx, cz) in enumerate(centres):
        loc = (cx, y_back, cz)
        add_empty(f"{p['name']}_attach_{i}", loc, panel, "ARROWS", 0.12)
        attach.append((f"attach_{i}", loc))
    snaps = [(f"snappoint_c{i}", (x, 0.0, z)) for i, (x, z) in enumerate(square(h))]
    snaps += [(f"snappoint_e{i}", loc) for i, loc in enumerate([(0.0, 0.0, h), (h, 0.0, 0.0), (0.0, 0.0, -h), (-h, 0.0, 0.0)])]
    snaps.append(("snappoint_centre", (0.0, 0.0, 0.0)))
    for label, loc in snaps:
        add_empty(f"{p['name']}_{label}", loc, panel)

    if p["fbx_path"]:
        if p["fbx_path"].startswith("//") and not bpy.data.filepath:
            print("[panel] export skipped: fbx_path is relative to the .blend, and this file is not saved")
        else:
            panel.location = (0.0, 0.0, 0.0)
            select_only([panel], panel)
            bpy.ops.export_scene.fbx(filepath=bpy.path.abspath(p["fbx_path"]), use_selection=True,
                                     apply_scale_options="FBX_SCALE_ALL", object_types={"MESH"}, bake_anim=False)
            print(f"[panel] exported to {bpy.path.abspath(p['fbx_path'])}")
    panel.location = p["display_offset"]

    def unity(v):   # Unity's FBX import flips X (the model looks the same from the front): (-x, z, -y)
        return (round(-v[0], 4), round(v[2], 4), round(-v[1], 4))

    tris = sum(len(poly.vertices) - 2 for poly in panel.data.polygons)
    d = panel.dimensions
    print(f"[panel] '{panel.name}': {tris} tris, {d.x:.2f} x {d.y:.2f} x {d.z:.2f} m (x across, y out of the wall, z up); "
          f"rim at {y_rim:.3f}, field at {y_field:.3f}, niche backs at {y_back:.3f}")
    for name, loc in attach:
        print(f"[panel] {name}: blender {tuple(round(c, 4) for c in loc)}; unity {unity(loc)} out (0, 0, 1)")
    for name, loc in snaps:
        print(f"[panel] {name}: unity {unity(loc)}")
    return panel


if __name__ == "__main__":
    build()
