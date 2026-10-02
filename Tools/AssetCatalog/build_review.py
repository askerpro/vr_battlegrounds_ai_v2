"""Проверяет измеренные сборки и создаёт листы визуального сравнения."""

import argparse
import json
import pathlib

from PIL import Image, ImageDraw, ImageFont, ImageStat


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("analysis", type=pathlib.Path)
    args = parser.parse_args()
    root = args.analysis
    measurements = json.loads((root / "measurements.json").read_text(encoding="utf-8"))
    groups = json.loads((root / "candidates.json").read_text(encoding="utf-8"))
    assembled = json.loads((root / "assembled-sizes.json").read_text(encoding="utf-8"))["models"]
    if len(measurements["models"]) != 201 or len(measurements["blocks"]) != 18:
        raise ValueError("Неполный каталог Industrial Set или эталонов")
    if any(e.get("error") for e in measurements["models"] + measurements["blocks"]):
        raise ValueError("Есть ошибки измерений")
    lookup = {c["id"]: c for g in groups for c in g["candidates"][:3]}
    if set(lookup) != {entry["name"] for entry in assembled}:
        raise ValueError("Сборки не соответствуют списку рецептов")
    max_error = 0
    for entry in assembled:
        target = lookup[entry["name"]]["actual_size"]
        error = max(abs(entry["size"][axis] - target[i]) for i, axis in enumerate("xyz"))
        max_error = max(max_error, error)
        if error > 0.002:
            raise ValueError("Сборка расходится с расчётом: " + entry["name"])
        image = Image.open(root / "previews" / (entry["name"] + ".png"))
        if image.size != (576, 192) or max(ImageStat.Stat(image).stddev) < 5:
            raise ValueError("Пустое или повреждённое превью: " + entry["name"])
    font = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 18)
    small = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 15)
    selected = [g for g in groups if g["candidates"]]
    sheets = []
    for offset in range(0, len(selected), 5):
        rows = selected[offset:offset + 5]
        sheet = Image.new("RGB", (1152, len(rows) * 272 + 48), "#171d26")
        draw = ImageDraw.Draw(sheet)
        draw.text((12, 10), "Эталон слева • геометрический кандидат справа • требует проверки", font=font, fill="white")
        for row, group in enumerate(rows):
            candidate = group["candidates"][0]
            top = 48 + row * 272
            draw.text((12, top), group["block"], font=font, fill="#7fcaff")
            draw.text((588, top), candidate["model"], font=font, fill="#ffcf80")
            sheet.paste(Image.open(root / group["preview"]).convert("RGB"), (0, top + 28))
            sheet.paste(Image.open(root / "previews" / (candidate["id"] + ".png")).convert("RGB"), (576, top + 28))
            details = "Повтор XYZ: " + str(candidate["repeat_xyz"]) + "; масштаб: " + str(candidate["uniform_scale"])
            draw.text((12, top + 226), details, font=small, fill="white")
            draw.text((588, top + 226), "Габариты: " + str(candidate["actual_size"]) + " м; ошибка: " +
                      str(round(candidate["dimension_error"] * 100, 1)) + "%", font=small, fill="white")
        name = "review-sheet-" + str(offset // 5 + 1) + ".png"
        sheet.save(root / name)
        sheets.append(name)
    result = {"models": len(measurements["models"]),
              "static_models": sum(not e.get("skipped") for e in measurements["models"]),
              "blocks": len(groups), "blocks_with_candidates": len(selected),
              "recipes": sum(len(g["candidates"]) for g in groups),
              "rendered_assemblies": len(assembled), "max_size_error_metres": max_error,
              "review_sheets": sheets, "status": "technical_checks_passed_visual_approval_pending"}
    (root / "verification.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
