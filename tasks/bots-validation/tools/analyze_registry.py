"""Разбор снимков реестра UXR (registry-<label>.tsv из registry-snapshot.cs.txt).

Для мёртвых записей каждого снимка ищет тот же ключ живым в предыдущих снимках: сцена, путь, тип,
activeInHierarchy/enabled на момент, когда запись была жива. Живые настоящие коллизии — UniqueId не равен
OriginalUniqueId при пустом CombineIdSource.

Запуск: python -X utf8 analyze_registry.py <папка со снимками> [S1 S2 S3 ...]
"""
import collections
import csv
import os
import sys

EMPTY = "00000000-0000-0000-0000-000000000000"


def load(path):
    with open(path, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f, delimiter="\t"))


def main():
    root = sys.argv[1]
    labels = sys.argv[2:] or sorted(
        name[len("registry-"):-len(".tsv")] for name in os.listdir(root)
        if name.startswith("registry-") and name.endswith(".tsv"))
    seen_live = {}
    for label in labels:
        rows = load(os.path.join(root, "registry-%s.tsv" % label))
        live = [r for r in rows if r["state"] == "live"]
        dead = [r for r in rows if r["state"] == "dead"]
        print("== %s: total=%d live=%d dead=%d" % (label, len(rows), len(live), len(dead)))
        groups = collections.Counter()
        unknown = 0
        for r in dead:
            prev = seen_live.get(r["key"])
            if prev is None:
                unknown += 1
                groups[("?", r["type"], "?", "?")] += 1
                continue
            path = prev["path"]
            head = "/".join(path.split("/")[:2])
            groups[(prev["scene"], prev["type"], head, "active=%s enabled=%s" % (prev["activeInHierarchy"], prev["enabled"]))] += 1
        for (scene, typ, head, flags), n in groups.most_common(15):
            print("  dead %3d  scene=%s type=%s root=%s %s" % (n, scene, typ, head, flags))
        if unknown:
            print("  dead without earlier live snapshot: %d" % unknown)
        examples = [seen_live[r["key"]]["path"] for r in dead if r["key"] in seen_live][:5]
        for p in examples:
            print("    e.g.", p)
        real = [r for r in live if r["originalUniqueId"] not in ("", EMPTY) and r["uniqueIdIsKey"] == "1"
                and r["originalUniqueId"] != r["key"] and r["combineIdSource"] in ("", EMPTY)]
        print("  live real collisions: %d" % len(real))
        by = collections.Counter((r["scene"], r["type"]) for r in real)
        for (scene, typ), n in by.most_common(8):
            print("    %3d scene=%s type=%s" % (n, scene, typ))
        for r in real[:5]:
            print("    e.g.", r["path"])
        for r in live:
            seen_live[r["key"]] = r


if __name__ == "__main__":
    main()
