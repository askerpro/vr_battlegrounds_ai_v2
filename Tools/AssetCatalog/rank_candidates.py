"""Первичный подбор размеров и силуэтов; результат требует визуального ревью."""

import argparse
import csv
import itertools
import json
import math
import pathlib
import re


def size_of(entry):
    return [entry["size"][axis] for axis in "xyz"]


def mask_at(mask, x, y):
    return mask[max(0, min(15, y)) * 16 + max(0, min(15, x))]


def recipe_masks(model, turn, repeats):
    original = model["masks"]
    masks = original if not turn else [original[1], original[0],
        "".join(mask_at(original[2], y, 15 - x) for y in range(16) for x in range(16))]
    # Силуэт бесшовного повторения одинаковых деталей, без предположения о защите.
    axes = [(0, 1), (2, 1), (0, 2)]
    return ["".join(mask_at(mask, int(((x + 0.5) * repeats[a]) % 16),
                                   int(((y + 0.5) * repeats[b]) % 16))
                    for y in range(16) for x in range(16))
            for mask, (a, b) in zip(masks, axes)]


def rank(catalog):
    results = []
    repeats = [(1, 1, 1), (2, 1, 1), (3, 1, 1), (4, 1, 1),
               (1, 2, 1), (1, 3, 1), (1, 4, 1), (1, 1, 2), (2, 2, 1)]
    for block in catalog["blocks"]:
        if block.get("error"):
            continue
        target = size_of(block)
        if min(target) <= 0:
            continue
        candidates = []
        for model in catalog["models"]:
            if model.get("error") or model.get("skipped") or min(size_of(model)) <= 0:
                continue
            if not suitable_category(block["name"], model["name"]):
                continue
            # Мелкий декор и огромные готовые здания — не строительный материал укрытия.
            if max(size_of(model)) > 15 or max(size_of(model)) < 0.3:
                continue
            for turn, copies in itertools.product([0, 90], repeats):
                if math.prod(copies) > 1 and not any(word in model["name"].lower() for word in
                    ["wooden_box", "bags_on", "barrel", "concrete", "railing", "floor", "road", "pipe"]):
                    continue
                if copies[1] > 1 and not any(word in model["name"].lower() for word in ["wooden_box", "bags_on", "barrel"]):
                    continue
                source = size_of(model)
                if turn:
                    source = [source[2], source[1], source[0]]
                assembled = [a * n for a, n in zip(source, copies)]
                scale = math.exp(sum(math.log(t / s) for t, s in zip(target, assembled)) / 3)
                scale = max(0.8, min(1.5, scale))
                actual = [s * scale for s in assembled]
                errors = [abs(a - t) / t for a, t in zip(actual, target)]
                if max(errors) > 0.25:
                    continue
                masks = recipe_masks(model, turn, copies)
                differences = [sum(a != b for a, b in zip(m, t)) / 256
                               for m, t in zip(masks, block["masks"])]
                if block["name"] == "LD_Dorito_Mid" and max(differences[:2]) > 0.15:
                    continue
                count = math.prod(copies)
                score = sum(errors) / 3 + sum(differences) / 3 + 0.025 * (count - 1)
                flags = ["Не проверены материал защиты и соответствие коллайдеров"]
                if scale > 1.2:
                    flags.append("Увеличение исходной модели более 20%; проверить правдоподобие масштаба")
                if any(word in model["name"].lower() for word in ["wooden", "barrel", "bags", "door", "electric"]):
                    flags.append("Внешний материал может не соответствовать Hard; требуется решение по классу защиты")
                flags.append("Маски учитывают треугольники, но не прозрачность текстур")
                if count > 1:
                    flags.append("Нужна проверка опоры, щелей и естественности сборки")
                if max(errors) > 0.1:
                    flags.append("Расхождение габаритов больше 10%; не готовая замена")
                if max(differences) > 0.1:
                    flags.append("Силуэт отличается: возможны лишние просветы или закрытые проёмы")
                if model["triangles"] * count > 10000:
                    flags.append("Более 10000 треугольников; бюджет Quest не подтверждён")
                candidates.append({"block": block["name"], "model": model["name"],
                    "path": model["path"], "preview": model["preview"], "rotation_y": turn,
                    "repeat_xyz": copies, "uniform_scale": round(scale, 5),
                    "actual_size": [round(a, 4) for a in actual],
                    "target_size": target, "dimension_error": round(max(errors), 4),
                    "silhouette_error": round(sum(differences) / 3, 4),
                    "score": round(score, 5), "triangles": model["triangles"] * count,
                    "material_slots": model["materialSlots"] * count,
                    "status": "candidate", "warnings": flags})
        # Не забивать выдачу поворотами и сборками одного и того же префаба.
        candidates.sort(key=lambda c: c["score"])
        unique = []
        names = set()
        for candidate in candidates:
            family = re.sub(r"_LD[12]", "", candidate["path"], flags=re.IGNORECASE)
            if family in names:
                continue
            names.add(family)
            candidate["id"] = block["name"] + "_candidate_" + str(len(unique) + 1)
            unique.append(candidate)
            if len(unique) == 5:
                break
        results.append({"block": block["name"], "size": target, "preview": block["preview"],
                        "candidates": unique})
    return results


def suitable_category(block, name):
    """Консервативный фильтр назначения по именам; не заменяет визуальное ревью."""
    name = name.lower()
    if block == "LD_Dorito_Mid":
        return any(word in name for word in ["road_block", "concrete", "bags"])
    if block == "LD_Net_Tall_Visual":
        return "fence" in name and "concrete" not in name
    if block == "LD_Floor_Tile":
        return any(word in name for word in ["floor", "road"])
    if block == "LD_Fence_Vault":
        return any(word in name for word in ["railing", "fence", "road_block"])
    if block in {"LD_Door_Lintel", "LD_Window_Lintel", "LD_Window_Sill"}:
        return any(word in name for word in ["wall", "beam", "concrete", "support"])
    if "Wall" in block:
        return any(word in name for word in ["wall", "fence", "door", "corrugated"])
    if block == "LD_PillarBox":
        return any(word in name for word in ["column", "pillar", "electric_box", "wooden_box"])
    if block in {"LD_Can_Mid", "LD_Tree_Tall", "LD_Beam_Low"}:
        return any(word in name for word in ["barrel", "pipe"])
    if block == "LD_Crate":
        return "wooden_box" in name
    return any(word in name for word in ["wooden_box", "barrel", "bags", "road_block", "electric_box"])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("measurements", type=pathlib.Path)
    args = parser.parse_args()
    root = args.measurements.parent
    catalog = json.loads(args.measurements.read_text(encoding="utf-8"))
    results = rank(catalog)
    (root / "candidates.json").write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
    (root / "recipes-for-unity.json").write_text(json.dumps({"groups": results}, ensure_ascii=False), encoding="utf-8")
    with (root / "candidates.csv").open("w", encoding="utf-8-sig", newline="") as destination:
        writer = csv.writer(destination)
        writer.writerow(["Блок", "Рецепт", "Модель", "Повтор X/Y/Z", "Поворот Y", "Масштаб",
                         "Размер X/Y/Z", "Ошибка габаритов", "Ошибка силуэта", "Треугольники", "Замечания"])
        for group in results:
            for c in group["candidates"]:
                writer.writerow([c["block"], c["id"], c["model"], c["repeat_xyz"], c["rotation_y"],
                                 c["uniform_scale"], c["actual_size"], c["dimension_error"],
                                 c["silhouette_error"], c["triangles"], "; ".join(c["warnings"])])
    lines = ["# Предварительные соответствия Industrial Set", "",
             "Это геометрический фильтр. Все рецепты требуют визуальной проверки;",
             "класс защиты, опора сборок и игровые линии прострела не подтверждены.", "",
             "Масштаб равномерный 0.8–1.5. Увеличение больше 20% отмечено отдельно.",
             "В повторе детали касаются границами мешей;",
             "позиции нормализуются к центру основания. Произвольных комбинаций нет.", ""]
    for group in results:
        lines += ["## " + group["block"], "", "Эталон: " + str([round(s, 3) for s in group["size"]]),
                  "", "![Эталон](" + group["preview"] + ")", ""]
        if not group["candidates"]:
            lines += ["Подходящих кандидатов по текущим ограничениям нет.", ""]
        for c in group["candidates"][:3]:
            lines += ["### " + c["id"] + " — " + c["model"], "",
                      "Повтор XYZ: " + str(c["repeat_xyz"]) + "; поворот Y: " + str(c["rotation_y"]) +
                      "°; масштаб: " + str(c["uniform_scale"]) + "; размер: " + str(c["actual_size"]) + ".", "",
                      "Ошибка габаритов: " + str(round(c["dimension_error"] * 100, 1)) +
                      "%; силуэта: " + str(round(c["silhouette_error"] * 100, 1)) +
                      "%; треугольников: " + str(c["triangles"]) + ".", "",
                      "![Сборка кандидата](previews/" + c["id"] + ".png)", "",
                      "[Исходная модель](" + c["preview"] + ")", "",
                      "; ".join(c["warnings"]) + ".", ""]
    (root / "candidates.md").write_text("\n".join(lines), encoding="utf-8")
    print(json.dumps({"blocks": len(results), "blocks_with_candidates": sum(bool(r["candidates"]) for r in results),
                      "recipes": sum(len(r["candidates"]) for r in results)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
