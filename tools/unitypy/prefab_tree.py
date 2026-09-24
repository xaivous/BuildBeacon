# Offline beacon_tree: print a vanilla prefab's hierarchy (components, materials, lights, local positions)
# straight from one of the game's SoftRef bundles, without launching Valheim.
#
#   uv run --no-project --with UnityPy python tools/unitypy/prefab_tree.py #       "E:/Steam/steamapps/common/Valheim/valheim_Data/StreamingAssets/SoftRef/Bundles/c4210710" piece_groundtorch_mist
#
# Find the bundle id by searching StreamingAssets/SoftRef/manifest_extended for "<prefab>.prefab"; the "bundle:" line
# just above it names the file. Script components print as "Mono:?" because the MonoScript assets live in another
# cab; identify them by their field names (add a print of mb.__dict__ keys if needed).
import sys, UnityPy
bundle, names = sys.argv[1], sys.argv[2:]
env = UnityPy.load(bundle)

def comp_label(c):
    try:
        obj = c.read()
    except Exception as e:
        return f"{c.type.name}?"
    t = c.type.name
    if t == "MonoBehaviour":
        try:
            s = obj.m_Script.read()
            return f"Mono:{s.m_ClassName}"
        except Exception:
            return "Mono:?"
    if t == "Light":
        try: return f"Light(type={obj.m_Type} color=({obj.m_Color.r:.2f},{obj.m_Color.g:.2f},{obj.m_Color.b:.2f}) range={obj.m_Range:.1f} intensity={obj.m_Intensity:.2f})"
        except Exception: return "Light"
    if t == "MeshRenderer":
        try:
            mats = [m.read().m_Name for m in obj.m_Materials]
            return f"MeshRenderer{mats}"
        except Exception: return "MeshRenderer"
    if t == "ParticleSystemRenderer":
        try:
            mats = [m.read().m_Name for m in obj.m_Materials]
            return f"ParticleSystemRenderer{mats}"
        except Exception: return "ParticleSystemRenderer"
    return t

def walk(go, depth):
    comps = []
    tr = None
    for c in go.m_Components:
        try:
            o = c.read()
        except Exception:
            comps.append("?"); continue
        if c.type.name in ("Transform", "RectTransform"):
            tr = o
        else:
            comps.append(comp_label(c))
    pos = ""
    if tr is not None:
        p = tr.m_LocalPosition; s = tr.m_LocalScale
        q = tr.m_LocalRotation
        rot = "" if (abs(q.x) + abs(q.y) + abs(q.z)) < 1e-4 else f" r({q.x:.3f},{q.y:.3f},{q.z:.3f},{q.w:.3f})"
        pos = f" @({p.x:.2f},{p.y:.2f},{p.z:.2f}){rot} s({s.x:.2f},{s.y:.2f},{s.z:.2f})"
    active = "" if getattr(go, "m_IsActive", 1) else " [inactive]"
    print("  " * depth + f"{go.m_Name}{active}{pos}  {comps}")
    if tr is not None:
        for ch in tr.m_Children:
            try:
                cgo = ch.read().m_GameObject.read()
                walk(cgo, depth + 1)
            except Exception as e:
                print("  " * (depth+1) + f"?? {e}")

found = set()
for obj in env.objects:
    if obj.type.name != "GameObject":
        continue
    go = obj.read()
    if go.m_Name in names:
        # only roots (no parent)
        tr = None
        for c in go.m_Components:
            if c.type.name == "Transform":
                tr = c.read(); break
        if tr is not None and tr.m_Father.path_id != 0:
            continue
        print(f"=== {go.m_Name}")
        walk(go, 0)
        found.add(go.m_Name)
print("missing:", [n for n in names if n not in found])
