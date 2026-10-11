# -*- coding: utf-8 -*-
"""Офлайн-разбор LV_Showcase.unity (HDRP, MilitaryCamp): мировые трансформы всех
PrefabInstance и сценовых Transform. Только чтение источников.
Выход: reports/implementation/comm-tower/showcase-instances.json
"""
import json, math, os, re, sys

PACK = r"F:/UnityProjects/EnvironmentPackCatalog/Assets/env_packs/MilitaryCamp/HDRP(Default)"
SCENE = os.path.join(PACK, "Scenes/Showcase/LV_Showcase.unity")
PREFABS = os.path.join(PACK, "Art/Prefabs")
ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(ROOT, "../../reports/implementation/comm-tower"))

HDR = re.compile(r"^--- !u!(\d+) &(-?\d+)( stripped)?")
FID = re.compile(r"fileID: (-?\d+)")
GUID = re.compile(r"guid: ([0-9a-f]{32})")


def split_docs(path):
    cur = None
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            m = HDR.match(line)
            if m:
                if cur:
                    yield cur
                cur = {"cls": int(m.group(1)), "id": int(m.group(2)), "stripped": bool(m.group(3)), "lines": []}
            elif cur is not None:
                cur["lines"].append(line.rstrip("\n"))
    if cur:
        yield cur


def vec(s):
    d = dict(re.findall(r"(\w+): (-?[\d.eE+-]+)", s))
    return {k: float(v) for k, v in d.items()}


def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw*bx + ax*bw + ay*bz - az*by,
            aw*by - ax*bz + ay*bw + az*bx,
            aw*bz + ax*by - ay*bx + az*bw,
            aw*bw - ax*bx - ay*by - az*bz)


def qrot(q, v):
    x, y, z, w = q
    p = (v[0], v[1], v[2], 0.0)
    r = qmul(qmul(q, p), (-x, -y, -z, w))
    return r[:3]


def parse_prefab_roots():
    roots = {}
    for fn in os.listdir(PREFABS):
        if not fn.endswith(".prefab"):
            continue
        meta = open(os.path.join(PREFABS, fn + ".meta"), encoding="utf-8").read()
        guid = GUID.search(meta).group(1)
        root_tf = None; local = None
        for d in split_docs(os.path.join(PREFABS, fn)):
            if d["cls"] == 4:
                txt = "\n".join(d["lines"])
                fm = re.search(r"m_Father: \{fileID: (-?\d+)\}", txt)
                if fm and fm.group(1) == "0":
                    root_tf = d["id"]
                    local = {
                        "p": vec(re.search(r"m_LocalPosition: \{([^}]*)\}", txt).group(1)),
                        "r": vec(re.search(r"m_LocalRotation: \{([^}]*)\}", txt).group(1)),
                        "s": vec(re.search(r"m_LocalScale: \{([^}]*)\}", txt).group(1)),
                    }
        roots[guid] = {"name": fn[:-7], "root_tf": root_tf, "local": local}
    return roots


def main():
    roots = parse_prefab_roots()
    tfs = {}        # id -> {go, p, r, s, father}
    stripped = {}   # id -> prefab instance id
    gos = {}        # id -> name
    insts = {}      # id -> {parent, src, mods}
    lights = []
    for d in split_docs(SCENE):
        cls, i, lines = d["cls"], d["id"], d["lines"]
        txt = "\n".join(lines)
        if cls == 4 and d["stripped"]:
            stripped[i] = int(re.search(r"m_PrefabInstance: \{fileID: (-?\d+)\}", txt).group(1))
        elif cls == 4:
            tfs[i] = {
                "go": int(re.search(r"m_GameObject: \{fileID: (-?\d+)\}", txt).group(1)),
                "p": vec(re.search(r"m_LocalPosition: \{([^}]*)\}", txt).group(1)),
                "r": vec(re.search(r"m_LocalRotation: \{([^}]*)\}", txt).group(1)),
                "s": vec(re.search(r"m_LocalScale: \{([^}]*)\}", txt).group(1)),
                "father": int(re.search(r"m_Father: \{fileID: (-?\d+)\}", txt).group(1)),
            }
        elif cls == 1 and not d["stripped"]:
            m = re.search(r"^  m_Name: (.*)$", txt, re.M)
            gos[i] = m.group(1) if m else ""
        elif cls == 108:
            lights.append({"id": i, "go": int(re.search(r"m_GameObject: \{fileID: (-?\d+)\}", txt).group(1)), "raw": txt[:1500]})
        elif cls == 1001:
            parent = int(re.search(r"m_TransformParent: \{fileID: (-?\d+)\}", txt).group(1))
            src = re.search(r"m_SourcePrefab: \{fileID: -?\d+, guid: ([0-9a-f]{32})", txt)
            src = src.group(1) if src else None
            mods = {}
            for m in re.finditer(r"- target: \{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: \d\}\s*\n\s*propertyPath: (\S+)\s*\n\s*value: ?(.*)", txt):
                mods[(int(m.group(1)), m.group(2), m.group(3))] = m.group(4).strip()
            removed = "m_RemovedComponents" in txt and "m_RemovedComponents: []" not in txt
            insts[i] = {"parent": parent, "src": src, "mods": mods}

    # Локальный трансформ экземпляра
    def inst_local(iid):
        it = insts[iid]; info = roots.get(it["src"])
        if info is None:
            return None
        base = info["local"]; rt = info["root_tf"]; g = it["src"]
        def get(path, dflt):
            v = it["mods"].get((rt, g, path))
            return float(v) if v not in (None, "") else dflt
        p = (get("m_LocalPosition.x", base["p"]["x"]), get("m_LocalPosition.y", base["p"]["y"]), get("m_LocalPosition.z", base["p"]["z"]))
        r = (get("m_LocalRotation.x", base["r"]["x"]), get("m_LocalRotation.y", base["r"]["y"]), get("m_LocalRotation.z", base["r"]["z"]), get("m_LocalRotation.w", base["r"]["w"]))
        s = (get("m_LocalScale.x", base["s"]["x"]), get("m_LocalScale.y", base["s"]["y"]), get("m_LocalScale.z", base["s"]["z"]))
        name = it["mods"].get((info["root_tf"] - 1, g, "m_Name"), info["name"])
        return p, r, s, name

    cache = {}
    def world_of_tf(tid, depth=0):
        """Мировой (pos, rot, scale) для Transform id (сценового или stripped)."""
        if tid == 0:
            return (0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0), (1.0, 1.0, 1.0), []
        if tid in cache:
            return cache[tid]
        if tid in tfs:
            t = tfs[tid]
            lp = (t["p"]["x"], t["p"]["y"], t["p"]["z"]); lr = (t["r"]["x"], t["r"]["y"], t["r"]["z"], t["r"]["w"]); ls = (t["s"]["x"], t["s"]["y"], t["s"]["z"])
            parent = t["father"]; nm = gos.get(t["go"], "?")
        elif tid in stripped:
            iid = stripped[tid]
            loc = inst_local(iid)
            if loc is None:
                res = ((0, 0, 0), (0, 0, 0, 1), (1, 1, 1), ["<unknown>"])
                cache[tid] = res; return res
            lp, lr, ls, nm = loc; parent = insts[iid]["parent"]
        else:
            return (0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0), (1.0, 1.0, 1.0), ["<missing>"]
        pp, pr, ps, path = world_of_tf(parent, depth + 1)
        sp = (lp[0]*ps[0], lp[1]*ps[1], lp[2]*ps[2])
        rp = qrot(pr, sp)
        wp = (pp[0]+rp[0], pp[1]+rp[1], pp[2]+rp[2])
        wr = qmul(pr, lr)
        ws = (ps[0]*ls[0], ps[1]*ls[1], ps[2]*ls[2])
        res = (wp, wr, ws, path + [nm])
        cache[tid] = res
        return res

    out = []
    for iid, it in insts.items():
        loc = inst_local(iid)
        if loc is None:
            continue
        lp, lr, ls, nm = loc
        pp, pr, ps, path = world_of_tf(it["parent"])
        sp = (lp[0]*ps[0], lp[1]*ps[1], lp[2]*ps[2])
        rp = qrot(pr, sp)
        out.append({
            "inst": iid, "prefab": roots[it["src"]]["name"], "name": nm,
            "parentPath": "/".join(path),
            "pos": [round(pp[0]+rp[0], 4), round(pp[1]+rp[1], 4), round(pp[2]+rp[2], 4)],
            "rot": [round(c, 6) for c in qmul(pr, lr)],
            "scale": [round(ps[0]*ls[0], 4), round(ps[1]*ls[1], 4), round(ps[2]*ls[2], 4)],
        })
    light_out = []
    for L in lights:
        go = L["go"]
        tid = next((k for k, t in tfs.items() if t["go"] == go), None)
        if tid is None:
            # объект света внутри префаба? — помечаем
            light_out.append({"light": L["id"], "go": go, "note": "transform not found"})
            continue
        wp, wr, ws, path = world_of_tf(tid)
        typ = re.search(r"m_Type: (\d+)", L["raw"])
        rng = re.search(r"m_Range: ([\d.]+)", L["raw"])
        light_out.append({"light": L["id"], "name": gos.get(go), "path": "/".join(path), "pos": [round(c, 3) for c in wp],
                          "type": typ.group(1) if typ else None, "range": rng.group(1) if rng else None})
    os.makedirs(OUT, exist_ok=True)
    json.dump({"instances": out, "lights": light_out}, open(os.path.join(OUT, "showcase-instances.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=0)
    from collections import Counter
    c = Counter(o["prefab"] for o in out)
    print("instances", len(out), "lights", len(light_out))
    for k, v in sorted(c.items()):
        if any(s in k for s in ("Antenna", "Dish", "Radio", "Tower", "Light", "Lamp", "Cable", "Ladder", "Spot", "Flood")):
            print(k, v)


if __name__ == "__main__":
    main()
