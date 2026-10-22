"""Набор файлов для удаления вместе с префабами: зависимости, на которые больше никто не ссылается (lobby-reflections).

Ищет GUID-ссылки в текстовых ассетах Assets/ (prefab, mat, unity, asset, controller, anim, overrideController,
shadergraph, shadersubgraph, lighting, playable, mask, preset). Зависимость удаляется, только если все ссылки на неё —
из удаляемого набора. Запуск: python tasks/lobby-reflections/tools/orphan_set.py <prefab> [...]; печатает пути.
"""
import os
import re
import sys

ROOT = "Assets"
TEXT_EXT = {".prefab", ".mat", ".unity", ".asset", ".controller", ".anim", ".overrideController", ".shadergraph",
            ".shadersubgraph", ".lighting", ".playable", ".mask", ".preset"}
GUID_RE = re.compile(r"guid: ([0-9a-f]{32})")

guid_to_path, refs_from = {}, {}
for dirpath, _, files in os.walk(ROOT):
    for f in files:
        p = os.path.join(dirpath, f).replace("\\", "/")
        if f.endswith(".meta"):
            with open(p, encoding="utf-8", errors="ignore") as fh:
                m = GUID_RE.search(fh.read())
            if m:
                guid_to_path[m.group(1)] = p[:-5]
        elif os.path.splitext(f)[1] in TEXT_EXT:
            with open(p, encoding="utf-8", errors="ignore") as fh:
                refs_from[p] = set(GUID_RE.findall(fh.read()))

path_to_guid = {v: k for k, v in guid_to_path.items()}
referrers = {}
for src, guids in refs_from.items():
    for g in guids:
        referrers.setdefault(g, set()).add(src)

doomed = set(a.replace("\\", "/") for a in sys.argv[1:])
changed = True
while changed:
    changed = False
    for src in list(doomed):
        for g in refs_from.get(src, ()):
            dep = guid_to_path.get(g)
            if not dep or dep in doomed or not dep.startswith(ROOT + "/"):
                continue
            users = {u for u in referrers.get(g, ()) if u != dep}
            if users and users <= doomed:
                doomed.add(dep)
                changed = True
    # Модели (fbx) и текстуры не текстовые: их ссылки видны только из текстовых ассетов — обработаны выше.

for p in sorted(doomed):
    print(p)
