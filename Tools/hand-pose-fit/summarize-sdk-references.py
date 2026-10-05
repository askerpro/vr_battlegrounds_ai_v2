"""Сводка уже снятых SDK-хватов: описательные профили, без балла качества.

Запуск из корня проекта: python Tools/hand-pose-fit/summarize-sdk-references.py
Не управляет Unity и не меняет игровые ассеты. JSON читает целочисленные fileID без потери точности.
"""
import argparse
import html
import json
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path

ZONES = ("thumb", "index", "middle", "ring", "little", "palm_wrist")
COLORS = ("#efb85c", "#59c9ea", "#83db8d", "#b095ee", "#ed8aae", "#a7b8c5")


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def summarize(request):
    folder = Path(request["OutputDirectory"])
    report = read(folder / "report.json")
    if report.get("Error"):
        raise ValueError(f"{folder}: {report['Error']}")
    if report["AlgorithmVersion"] != "mesh-fit-0.3-precision":
        raise ValueError(f"{folder}: другая версия алгоритма")
    settings = report["Settings"]
    expected = {"ContactDistanceMinMm": 0, "ProximityMm": 2, "SampleCount": 3000,
                "CheckSamplingConvergence": True, "PalmarOnly": False, "ContactBoundsEnabled": False,
                "Profile": "exploratory", "StateName": "sdk-authored-static", "Seed": 42}
    if (report["CaptureSource"] != "prefab_static" or settings.get("ContactRendererPaths")
            or settings.get("ContactRegions") or any(settings.get(k) != v for k, v in expected.items())):
        raise ValueError(f"{folder}: конфигурация не соответствует фиксированной SDK-выборке")
    if request.get("RenderImages"):
        images = report["Cameras"]
        if len(images) != 24 or any(not (folder / x["Image"]).is_file() for x in images):
            raise ValueError(f"{folder}: неполный набор PNG")
    zones = report["Zones"]
    area = sum(x["SampledAreaMm2"] for x in zones)
    near = sum(x["SampledAreaMm2"] * x["NearSurfaceFraction"] for x in zones)
    per_zone = {x["Zone"]: x["SampledAreaMm2"] * x["NearSurfaceFraction"] for x in zones}
    normal_area = sum(x["SampledAreaMm2"] * x["NearSurfaceFraction"] for x in zones
                      if x["MeanNearNormalOpposition"] is not None)
    opposition = (sum(x["SampledAreaMm2"] * x["NearSurfaceFraction"] * x["MeanNearNormalOpposition"]
                      for x in zones if x["MeanNearNormalOpposition"] is not None) / normal_area
                  if normal_area else None)
    return {
        "Case": folder.name, "AvatarName": folder.name.split("-")[0],
        "ObjectName": folder.name.split("-")[1], "Directory": folder.as_posix(),
        "Avatar": report["Avatar"], "Object": report["Object"], "Pose": report["Pose"],
        "Settings": report["Settings"], "CaptureSource": report["CaptureSource"],
        "ControllerAlignmentApplied": report["ControllerAlignmentApplied"],
        "AppliedBlend": report["AppliedBlend"], "WholeHandNearFraction": near / area,
        "NearAreaMm2": near, "HandAreaMm2": area,
        "ContactAreaShares": {k: per_zone.get(k, 0) / near if near else 0 for k in ZONES},
        "MeanNormalOpposition": opposition,
        "UnknownSignAreaFraction": sum(x["UnknownSignAreaMm2"] for x in zones) / area,
        "IntersectingHandTriangleFraction": report["IntersectingHandTriangles"] / report["HandTriangles"],
        "HandSelfIntersectionPairs": report["HandSelfIntersectionPairs"],
        "ContactPatchCount": len(report["ContactPatches"]),
        "MaxSamplingNearDeltaPp": max((abs(x["NearSurfaceFractionDelta"]) * 100
                                        for x in report["SamplingConvergence"]), default=None),
        "ReferenceComparisonStatus": report["ReferenceComparisonStatus"],
        "Zones": zones, "Segments": report["Segments"], "Reliability": report["Reliability"],
    }


def bounds(values):
    return {"min": min(values), "max": max(values)} if values else None


def display(value, digits=3):
    return "неизвестно" if value is None else f"{value:.{digits}f}"


def profile(cases):
    return {
        "Cases": len(cases), "NearFraction": bounds([x["WholeHandNearFraction"] for x in cases]),
        "NearAreaMm2": bounds([x["NearAreaMm2"] for x in cases]),
        "NormalOpposition": bounds([x["MeanNormalOpposition"] for x in cases
                                     if x["MeanNormalOpposition"] is not None]),
        "ContactAreaShares": {zone: bounds([x["ContactAreaShares"][zone] for x in cases]) for zone in ZONES},
    }


def contact_bar(case):
    offset = 0
    rectangles = []
    for zone, color in zip(ZONES, COLORS):
        width = 240 * case["ContactAreaShares"][zone]
        rectangles.append(f'<rect x="{offset:.2f}" width="{width:.2f}" height="14" fill="{color}"><title>{zone}: {width/2.4:.1f}%</title></rect>')
        offset += width
    return '<svg width="240" height="14" role="img" aria-label="Доли контактной площади по зонам">' + "".join(rectangles) + "</svg>"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--library", type=Path, default=Path("Temp/HandPoseFit/sdk-reference-library"))
    parser.add_argument("--artifact", type=Path, default=Path("Docs/tasks/report/hand-pose-fit-2026-10-05/history/hand-pose-fit-sdk-references-2026-10-04.json"))
    args = parser.parse_args()
    cases = [summarize(x) for x in read(args.library / "requests.json")]
    controls = [summarize(x) for x in read(args.library / "control-requests.json")]
    by_name = {x["Case"]: x for x in cases}
    insensitive = []
    for control in controls:
        base = by_name[control["Case"].rsplit("-offset", 1)[0]]
        control["NearFractionDeltaPp"] = (control["WholeHandNearFraction"] - base["WholeHandNearFraction"]) * 100
        control["NormalOppositionDelta"] = (control["MeanNormalOpposition"] - base["MeanNormalOpposition"]
                                            if control["MeanNormalOpposition"] is not None and base["MeanNormalOpposition"] is not None else None)
        if control["NearFractionDeltaPp"] >= 0:
            insensitive.append(control["Case"])
    grouped = defaultdict(list)
    for case in cases:
        grouped[(case["ObjectName"], case["AvatarName"])].append(case)
    profiles = [{"ObjectName": key[0], "AvatarName": key[1], **profile(group)} for key, group in sorted(grouped.items())]
    ranges = bounds([x["WholeHandNearFraction"] for x in cases])
    findings = [
        f"Близость 0–2 мм занимает {100*ranges['min']:.2f}–{100*ranges['max']:.2f}% всей поверхности кисти в штатных статических случаях. Общий минимум контакта из этого не следует.",
        f"В {len(insensitive)}/{len(controls)} смещений доля близости не уменьшилась. Больше unsigned-контакта не означает лучшее положение кисти.",
        "Профиль контакта сравнивается по предмету/типу хвата, аватару и состоянию. Повторные стороны и точки одного предмета коррелируют; это не независимая статистическая выборка.",
        "Статический источник, непроверенная ладонная маска и неизвестный знак ограничивают калибровку. Обнаруженные диапазоны описательные, не нормы допуска или итоговый балл.",
    ]
    artifact = {
        "CreatedUtc": datetime.now(timezone.utc).isoformat(), "AlgorithmVersion": "mesh-fit-0.3-precision",
        "SourceAcceptedByUser": "Stock SDK Cyborg and BigHands grips are reference examples",
        "CalibrationStatus": "exploratory_profiles_no_quality_thresholds", "BaselineCount": len(cases),
        "PngCount": len(cases) * 24, "SyntheticControlCount": len(controls),
        "ExcludedStaticCases": read(args.library / "excluded.json"),
        "Profiles": profiles, "Findings": findings, "ControlsWithNonDecreasingNearFraction": insensitive,
        "Cases": cases, "Controls": controls,
    }
    args.artifact.parent.mkdir(parents=True, exist_ok=True)
    args.artifact.write_text(json.dumps(artifact, ensure_ascii=False, indent=2), encoding="utf-8")
    rows = []
    for case in cases:
        normal = case["MeanNormalOpposition"]
        rows.append(f'<tr data-case="{html.escape(case["Case"])}"><td><a href="{html.escape(case["Case"])}/index.html">{html.escape(case["Case"])}</a></td><td>{case["WholeHandNearFraction"]*100:.2f}%</td><td>{case["NearAreaMm2"]:.0f}</td><td>{contact_bar(case)}</td><td>{display(normal)}</td><td>{case["UnknownSignAreaFraction"]*100:.0f}%</td><td>{display(case["MaxSamplingNearDeltaPp"],2)}</td></tr>')
    control_rows = [f'<tr><td><a href="{html.escape(x["Case"])}/report.json">{html.escape(x["Case"])}</a></td><td>{x["NearFractionDeltaPp"]:+.2f}</td><td>{display(x["MeanNormalOpposition"])}</td></tr>' for x in controls]
    legend = " · ".join(f'<span style="color:{color}">{zone}</span>' for zone, color in zip(ZONES, COLORS))
    page = '''<!doctype html><html lang="ru"><meta charset="utf-8"><title>SDK: эталонные хваты</title>
<style>body{font:15px system-ui;background:#131a21;color:#dce5ed;margin:24px}a{color:#72d2ff}table{border-collapse:collapse}td,th{padding:8px;border-bottom:1px solid #334250;text-align:left}input{background:#263443;color:white;padding:8px;width:360px}p{max-width:1050px;line-height:1.5}</style>
<h1>SDK: Cyborg / BigHands, неоружейные хваты</h1>'''
    page += f'<p>{len(cases)} статических отчёта · {len(cases)*24} PNG · {len(controls)} временных смещений. Контакт: 0–2 мм включительно. Числа описательные; общего балла и принятого порога качества нет.</p>'
    page += "".join(f'<p>{html.escape(x)}</p>' for x in findings)
    page += '<p>Щёлкните имя для 24 снимков и подробных данных. Δ выборки — максимум изменения доли близости по зонам при 3000 → 6000 точек, в процентных пунктах.</p>'
    page += f'<p>{legend}</p><input id="filter" placeholder="Фильтр: cyborg, bighands, Battery…"><table id="cases"><thead><tr><th>Хват</th><th>Близость всей кисти</th><th>Площадь мм²</th><th>Распределение контакта</th><th>Нормали −dot</th><th>Неизвестный знак</th><th>Δ выборки, п.п.</th></tr></thead><tbody>' + "".join(rows) + "</tbody></table>"
    page += '<h2>Контрольные смещения +X предмета</h2><p>Смещение не является автоматически размеченным плохим хватом. Эти примеры проверяют чувствительность и неоднозначность метрик; игровые позы не менялись.</p><table><tr><th>Случай / JSON</th><th>Δ близости, п.п.</th><th>Нормали −dot</th></tr>' + "".join(control_rows) + "</table>"
    page += '''<script>document.getElementById('filter').addEventListener('input',e=>{const q=e.target.value.toLowerCase();document.querySelectorAll('#cases tbody tr').forEach(r=>r.hidden=!r.dataset.case.toLowerCase().includes(q));});</script></html>'''
    (args.library / "index.html").write_text(page, encoding="utf-8")
    print(json.dumps({k: artifact[k] for k in ("BaselineCount", "PngCount", "SyntheticControlCount", "Findings")}, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
