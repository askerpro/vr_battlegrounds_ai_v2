"""Сравнение семантических дампов префаба до/после пересборки (WeaponRebuildProbe).

Строка дампа: <prefab>|<путь GO>|<Тип#i>|<propertyPath>=<значение> или <prefab>|<путь GO>|GO|...
Числа сравниваются с допуском --tol (по умолчанию 1e-5). Вывод сгруппирован: узлы только до / только после,
компоненты только до / после, изменённые свойства.

    python compare_dumps.py <pre.txt> <post.txt> [--tol 1e-5] [--limit 400] [--semantic]

--semantic: без перенумерации объектов (m_FileID/m_PathID), списка детей Transform и номера среди братьев.
"""
import argparse
import re
from collections import defaultdict

NUM = re.compile(r"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?")


def load(path):
    props = {}
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n")
            if not line:
                continue
            parts = line.split("|", 3)
            if len(parts) < 3:
                continue
            if parts[2] == "GO":
                props[(parts[0], parts[1], "GO", "")] = parts[3] if len(parts) > 3 else ""
                continue
            if len(parts) < 4:
                props[(parts[0], parts[1], parts[2], "")] = ""
                continue
            key, _, value = parts[3].partition("=")
            props[(parts[0], parts[1], parts[2], key)] = value
    return props


SIBLING = re.compile(r" sibling=\d+")


def semantic(props):
    result = {}
    for (prefab, go, comp, key), value in props.items():
        if key.endswith(".m_FileID") or key.endswith(".m_PathID"):
            continue
        if comp.startswith("Transform#") and (key.startswith("m_Children.") or key == "m_Father"):
            continue
        if comp == "GO":
            value = SIBLING.sub("", value)
        result[(prefab, go, comp, key)] = value
    return result


def same(a, b, tol):
    if a == b:
        return True
    na, nb = NUM.findall(a), NUM.findall(b)
    if not na or len(na) != len(nb) or NUM.sub("#", a) != NUM.sub("#", b):
        return False
    return all(abs(float(x) - float(y)) <= tol for x, y in zip(na, nb))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("pre")
    ap.add_argument("post")
    ap.add_argument("--tol", type=float, default=1e-5)
    ap.add_argument("--limit", type=int, default=400)
    ap.add_argument("--semantic", action="store_true")
    a = ap.parse_args()
    pre, post = load(a.pre), load(a.post)
    if a.semantic:
        pre, post = semantic(pre), semantic(post)
    gos_pre = {(k[0], k[1]) for k in pre}
    gos_post = {(k[0], k[1]) for k in post}
    comps_pre = {(k[0], k[1], k[2]) for k in pre}
    comps_post = {(k[0], k[1], k[2]) for k in post}
    out = []
    for title, items in (("узлы только ДО", sorted(gos_pre - gos_post)), ("узлы только ПОСЛЕ", sorted(gos_post - gos_pre))):
        if items:
            out.append(f"## {title}: {len(items)}")
            out += [f"  {p} :: {g}" for p, g in items]
    common_gos = gos_pre & gos_post
    for title, items in (("компоненты только ДО", comps_pre - comps_post), ("компоненты только ПОСЛЕ", comps_post - comps_pre)):
        items = sorted(c for c in items if (c[0], c[1]) in common_gos)
        if items:
            out.append(f"## {title}: {len(items)}")
            out += [f"  {p} :: {g} :: {c}" for p, g, c in items]
    common = comps_pre & comps_post
    changed = defaultdict(list)
    for k in sorted(set(pre) | set(post)):
        if (k[0], k[1], k[2]) not in common:
            continue
        va, vb = pre.get(k), post.get(k)
        if va is None or vb is None or not same(va, vb, a.tol):
            changed[(k[0], k[1], k[2])].append((k[3], va, vb))
    n = sum(len(v) for v in changed.values())
    out.append(f"## изменённые свойства: {n} в {len(changed)} компонентах")
    shown = 0
    for comp, rows in changed.items():
        out.append(f"  {comp[0]} :: {comp[1]} :: {comp[2]}")
        for prop, va, vb in rows:
            out.append(f"      {prop}: {va} → {vb}")
            shown += 1
            if shown >= a.limit:
                break
        if shown >= a.limit:
            out.append("  … обрезано")
            break
    print("\n".join(out) if out else "РАВНЫ")


if __name__ == "__main__":
    main()
