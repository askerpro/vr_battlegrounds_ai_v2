"""Офлайн-сводка раунда cc1-round1 (подэтап cc-1 clip-cutover) по отчётам стенда.

Читает <root>/controller-*.json (B1), <root>/runtime/<combo>/summary.json (Metrics["playerFlow"] — BotStandPlayerFlowProbe,
Metrics["cc1"] — наблюдатель B4), <root>/runtime/<phase>/queue.json; база сравнения — reports/eq-round3 (те же case T07).
Пишет <root>/verdict.json и <root>/table.md. Это DEVELOPMENT-замер на стенде, не приёмка пользователем.
Запуск: python -I tasks/bots-fix/tools/cc1-round1/analyze.py [root] [baseRoot]
"""
import glob
import json
import os
import sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else "tasks/bots-fix/reports/cc1-round1"
BASE = sys.argv[2] if len(sys.argv) > 2 else "tasks/bots-fix/reports/eq-round3"
COMBOS = ["E-ak105-ref", "PE-ppk-ref", "H-ppk-holster", "U-unarmed", "P-purchase-ak105",
          "G-ghost-respawn-ak105", "AB-T01-ak105", "AB-T06-ak105", "AB-T08-ak105"]
BASE_OF = {"E-ak105-ref": "E-ak105", "PE-ppk-ref": "PE-ppk", "H-ppk-holster": "H-ppk-holster", "U-unarmed": "U-unarmed",
           "P-purchase-ak105": "P-purchase-ak105", "G-ghost-respawn-ak105": "G-ghost-respawn-ak105"}
LIVE = ("botBody", "botNavigator", "botGunner", "botAvatarDriver", "botCombatDriver", "botHands", "botWeaponAim", "botRuntimeController")
PASSIVE_ZERO = ("botCombatDriver", "botHands", "botWeaponAim")
PASSIVE_ONE = ("botBody", "botNavigator", "botGunner", "botAvatarDriver", "botRuntimeController")


def load(path):
    with open(path, encoding="utf-8-sig") as handle:
        return json.load(handle)


def r(value, digits=3):
    return round(value, digits) if isinstance(value, (int, float)) and not isinstance(value, bool) else value


def first_case(folder):
    path = os.path.join(folder, "summary.json")
    if not os.path.exists(path):
        return None, None
    summary = load(path)
    cases = summary.get("cases") or []
    return summary, (cases[0] if cases else None)


def feet_path(case):
    frames = [f for f in (case.get("Frames") or []) if f.get("Feet")]
    total = 0.0
    for a, b in zip(frames, frames[1:]):
        dx, dz = b["Feet"][0] - a["Feet"][0], b["Feet"][2] - a["Feet"][2]
        total += (dx * dx + dz * dz) ** 0.5
    return {"samples": len(frames), "pathMeters": r(total), "start": frames[0]["Feet"] if frames else None,
            "end": frames[-1]["Feet"] if frames else None,
            "combatFrames": sum(1 for f in frames if f.get("Combat")),
            "states": sorted({f.get("State") for f in frames if f.get("State")})}


def kit_check(kit):
    """Каждое тело: живое — по 1 каждого компонента; пассивное (призрак/цель) — без Hands/Aim/CombatDriver, остальные по 1."""
    rows, ok = [], True
    for body in kit or []:
        passive = body.get("passive")
        if passive:
            good = all(body.get(k) == 0 for k in PASSIVE_ZERO) and all(body.get(k) == 1 for k in PASSIVE_ONE)
        else:
            good = all(body.get(k) == 1 for k in LIVE)
        ok = ok and good
        rows.append({"role": body.get("role"), "prefab": body.get("prefab"), "alive": body.get("alive"), "passive": passive,
                     "ok": good, "counts": {k: body.get(k) for k in LIVE}})
    return ok and bool(rows), rows


def eq_points(combo, pf):
    """Пункты pf-equipment по хронологии пробы."""
    eq = (pf or {}).get("equipment") or {}
    timeline = eq.get("timeline") or []
    hands = eq.get("hands") or {}
    crit = eq.get("criteria") or {}
    events = [e.get("event") for e in timeline]
    grabs = [e for e in timeline if e.get("event") == "primaryGrab"]
    shots_by = {}
    for e in timeline:
        if e.get("event") == "shot":
            shots_by.setdefault(e.get("what"), 0)
            shots_by[e.get("what")] += 1
    out = {"criteria": {k: v for k, v in crit.items() if k != "note"}, "hands": hands, "shotsByWeapon": shots_by,
           "bodyChanges": eq.get("bodyChanges"), "stowDropped": eq.get("stowDropped")}
    if combo == "H-ppk-holster":
        out["point"] = "кобура: PPK взят из якоря, есть выстрелы"
        out["pass"] = any((g.get("value") or {}).get("source") == "anchor" and "PPK" in (g.get("what") or "") for g in grabs) and \
            (pf or {}).get("shots", 0) > 0
    elif combo == "U-unarmed":
        un = eq.get("unarmed") or {}
        out["point"] = "без оружия: Blaze не активен, нет выстрелов/хватов, кисти в покое"
        out["unarmed"] = {k: un.get(k) for k in ("frames", "blazeActiveFrames", "shotRequestFrames", "restPoseFrames")}
        out["pass"] = bool(crit.get("unarmedNoBlazeNoShots")) and (un.get("frames") or 0) > 0 and (pf or {}).get("shots", 0) == 0 and \
            (hands.get("primaryGrabs") or 0) == 0
    elif combo == "P-purchase-ak105":
        out["point"] = "покупка AK105: PPK убран в кобуру (якорь), AK105 взят, выстрелы AK105"
        stowed = [e for e in timeline if (e.get("event") or "").startswith("stowedInAnchor")]
        out["stowedInAnchor"] = [e.get("event") for e in stowed]
        out["pass"] = (hands.get("stowsPlaced") or 0) >= 1 and bool(stowed) and \
            any("AK105" in (g.get("what") or "") for g in grabs) and any("AK105" in (w or "") and n > 0 for w, n in shots_by.items()) and \
            (eq.get("stowDropped") or 0) == 0
    elif combo == "G-ghost-respawn-ak105":
        ghost = eq.get("ghost") or {}
        after = eq.get("afterDeath") or {}
        new_body = [i for i, e in enumerate(timeline) if e.get("event") == "newBody"]
        shots_after = sum(1 for e in timeline[new_body[0]:] if e.get("event") == "shot") if new_body else 0
        out["point"] = "смерть → призрак (пассивный комплект, ведётся драйвером) → новое тело стреляет"
        out["ghost"] = {k: ghost.get(k) for k in ("bodies", "frames", "driverWrites", "adapterHandsAimComponentFrames", "restPoseFrames",
                                                   "travelMeters", "reachedOwnSpawnZoneAfterSeconds")}
        out["afterDeath"] = {k: after.get(k) for k in ("deadFrames", "dyingBodyDriverWrites", "grabs", "shots")}
        out["shotsAfterRespawn"] = shots_after
        out["passDeath"] = bool(crit.get("noActionsAfterDeath")) and ("death" in events or "ghostOrDeadBody" in events)
        out["passGhost"] = (ghost.get("bodies") or 0) >= 1 and bool(crit.get("ghostDrivenWithoutHandsAim"))
        out["passNewBody"] = bool(new_body) and shots_after > 0
        out["pass"] = out["passDeath"] and out["passGhost"] and out["passNewBody"]
    return out


def proxy_row(cc1):
    proxy = (cc1 or {}).get("proxy") or {}
    moves = proxy.get("moves") or []
    move_only = [m for m in moves if m.get("anim") == "Move"]
    first = [m for m in move_only if m.get("firstOfProxy")]
    lat = [m.get("latency") for m in move_only if m.get("result") == "moved"]
    nt = proxy.get("normalizedTime") or {}
    return {
        "proxies": [{k: p.get(k) for k in ("bornT", "removedT", "removedReason", "controller", "applyRootMotion", "cullingMode",
                                          "firstDisplacementAfterBirth")} for p in proxy.get("proxies") or []],
        "moves": len(moves), "moveAnimCount": len(move_only),
        "firstMove": [{k: m.get(k) for k in ("t", "from", "proxyAge", "result", "latency", "latencyFrames", "ntAtPlay",
                                              "animatorStateAtPlay", "animatorStateAtMove", "elapsed")} for m in first],
        "moveLatency": {"n": len(lat), "max": r(max(lat)) if lat else None, "median": r(sorted(lat)[len(lat) // 2]) if lat else None},
        "movesNotMoved": [{k: m.get(k) for k in ("t", "anim", "from", "result", "elapsed", "blazeState")} for m in moves
                          if m.get("result") != "moved"][:10],
        "otherAnimLatency": {a: [m.get("latency") for m in moves if m.get("anim") == a and m.get("result") == "moved"][:10]
                             for a in ("Back", "Left", "Right")},
        "normalizedTime": nt,
        "ntGrowthShare": r(nt.get("increasingFrames", 0) / nt["sameStateFrames"], 3) if nt.get("sameStateFrames") else None,
        "animatorStatesSeen": proxy.get("animatorStatesSeen"), "transitionsTotal": proxy.get("transitionsTotal"),
    }


def combo_row(name):
    folder = os.path.join(ROOT, "runtime", name)
    summary, case = first_case(folder)
    if case is None:
        return {"combo": name, "missing": True}
    metrics = case.get("Metrics") or {}
    pf = metrics.get("playerFlow") or {}
    cc1 = metrics.get("cc1") or {}
    kit_ok, kit_rows = kit_check(cc1.get("kit"))
    target = cc1.get("target") or {}
    height = target.get("cameraHeightAboveRoot") or {}
    row = {
        "combo": name, "case": case.get("Id"), "status": case.get("Status"), "error": case.get("Error"),
        "duration": r(case.get("Duration")), "shots": pf.get("shots"), "hits": pf.get("hitsOnTarget"), "hitRate": r(pf.get("hitRate")),
        "foreignDamage": pf.get("foreignDamageEventsOnTarget"), "consoleErrorsProbe": pf.get("consoleErrors"),
        "probeCriteria": {k: v for k, v in (pf.get("criteria") or {}).items() if k != "note"},
        "equipment": eq_points(name, pf),
        "kitOk": kit_ok, "kit": kit_rows,
        "target": {"heightMin": height.get("min"), "heightMax": height.get("max"), "heightMean": height.get("mean"),
                   "rootDrift": target.get("rootDriftFromFirstM"), "rootMaxStep": target.get("rootMaxStepM"),
                   "rootStepsOver1mm": target.get("rootStepsOver1mm"), "frames": target.get("frames")},
        "proxy": proxy_row(cc1),
        "feet": feet_path(case),
        "footSlide": {k: r((pf.get("footSlide") or {}).get(k), 4) for k in ("plantedSlideMeters", "plantedFootSeconds",
                                                                             "metersPerPlantedSecond", "bodyTravelMeters")},
        "log": cc1.get("log"), "memory": cc1.get("memory"), "sampleErrors": cc1.get("sampleErrors"),
        "playerFlowError": metrics.get("playerFlowError"),
    }
    base_name = BASE_OF.get(name)
    if base_name:
        _, base = first_case(os.path.join(BASE, "runtime", base_name))
        if base:
            bpf = (base.get("Metrics") or {}).get("playerFlow") or {}
            row["base"] = {"source": BASE + "/runtime/" + base_name, "shots": bpf.get("shots"), "hits": bpf.get("hitsOnTarget"),
                           "hitRate": r(bpf.get("hitRate")),
                           "metersPerPlantedSecond": r((bpf.get("footSlide") or {}).get("metersPerPlantedSecond"), 4),
                           "bodyTravelMeters": r((bpf.get("footSlide") or {}).get("bodyTravelMeters"), 3),
                           "feet": feet_path(base)}
    return row


def checks(rows):
    by = {row["combo"]: row for row in rows}
    out = {}
    for ref in ("E-ak105-ref", "PE-ppk-ref"):
        row = by.get(ref) or {}
        out[ref + ": hitRate=1"] = None if row.get("missing") else bool(row.get("shots")) and row.get("hitRate") == 1
    for name in ("H-ppk-holster", "U-unarmed", "P-purchase-ak105", "G-ghost-respawn-ak105"):
        row = by.get(name) or {}
        out[name + ": pf-equipment"] = None if row.get("missing") else row["equipment"].get("pass")
    present = [row for row in rows if not row.get("missing")]
    out["kit: 1 компонент на тело, у пассивных нет Hands/Aim/CombatDriver"] = all(row["kitOk"] for row in present) if present else None
    ghost_rows = [k for row in present for k in row["kit"] if k["passive"] and k["role"].startswith("subject")]
    out["kit: призрак встречен и пассивен"] = bool(ghost_rows) if by.get("G-ghost-respawn-ak105") and not by["G-ghost-respawn-ak105"].get("missing") else None
    static = [row for row in present if row.get("case") == "T07" and row["target"].get("frames")]
    out["target: камера 1,65 ± 0,02 м (T07)"] = all(1.63 <= row["target"]["heightMin"] and row["target"]["heightMax"] <= 1.67 for row in static) if static else None
    out["target: корень неподвижен (T07, дрейф < 1 мм)"] = all((row["target"]["rootDrift"] or 0) < 0.001 for row in static) if static else None
    ab = [row for row in present if row["combo"].startswith("AB-")]
    firsts = [m for row in ab for m in row["proxy"]["firstMove"]]
    out["proxy A: первый Move ≤ 0,2 с (T01/T06/T08)"] = all(m.get("result") == "moved" and m.get("latency") is not None and m["latency"] <= 0.2
                                                            for m in firsts) if firsts else None
    out["proxy A: все Move ≤ 0,2 с"] = all((row["proxy"]["moveLatency"]["max"] or 0) <= 0.2 for row in ab) if ab else None
    out["proxy A: normalizedTime растёт (>95% кадров того же состояния)"] = all((row["proxy"]["ntGrowthShare"] or 0) > 0.95 for row in ab) if ab else None
    out["proxy: 0 «doesn't exist»/«Empty animation»"] = all(((row.get("log") or {}).get("missingAnim") or 0) == 0 and
                                                           ((row.get("log") or {}).get("emptyAnim") or 0) == 0 for row in present) if present else None
    out["console: 0 ошибок/исключений в case"] = all(((row.get("log") or {}).get("errors") or 0) == 0 and
                                                     ((row.get("log") or {}).get("exceptions") or 0) == 0 for row in present) if present else None
    out["стенд: нет InvalidFixture"] = all(row.get("status") != "InvalidFixture" for row in present) if present else None
    return out


def table(rows, verdict):
    lines = ["# cc1-round1 — сводка стенда", "", "DEVELOPMENT-замер (стенд, сервер без клиентов, Editor worker); не приёмка.", "",
             "| combo | case | статус | выстр. | попад. | доля | pf-equipment | комплект | камера цели, м | дрейф корня, м | первый Move, с | Move max, с | nt растёт | warn anim | ошибки |",
             "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for row in rows:
        if row.get("missing"):
            lines.append("| %s | — | нет отчёта | | | | | | | | | | | | |" % row["combo"])
            continue
        eq = row["equipment"].get("pass")
        t = row["target"]
        p = row["proxy"]
        first = ", ".join(str(m.get("latency") if m.get("result") == "moved" else m.get("result")) for m in p["firstMove"]) or "—"
        log = row.get("log") or {}
        lines.append("| %s | %s | %s | %s | %s | %s | %s | %s | %s–%s | %s | %s | %s | %s | %s | %s |" % (
            row["combo"], row["case"], row["status"], row["shots"], row["hits"], row["hitRate"],
            "—" if eq is None else ("PASS" if eq else "FAIL"), "OK" if row["kitOk"] else "FAIL",
            t.get("heightMin"), t.get("heightMax"), t.get("rootDrift"), first, p["moveLatency"]["max"], p["ntGrowthShare"],
            (log.get("missingAnim") or 0) + (log.get("emptyAnim") or 0), (log.get("errors") or 0) + (log.get("exceptions") or 0)))
    lines += ["", "## Проверки", ""]
    for key, value in verdict["checks"].items():
        lines.append("- %s: %s" % (key, "—" if value is None else ("PASS" if value else "FAIL")))
    lines += ["", "## Путь ног (кадры стенда 0,1 с)", "", "| combo | путь, м | база eq-round3, м | кадров Combat |", "|---|---|---|---|"]
    for row in rows:
        if row.get("missing"):
            continue
        base = (row.get("base") or {}).get("feet") or {}
        lines.append("| %s | %s | %s | %s |" % (row["combo"], row["feet"]["pathMeters"], base.get("pathMeters", "нет базы"), row["feet"]["combatFrames"]))
    return "\n".join(lines) + "\n"


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass
    controller = sorted(glob.glob(os.path.join(ROOT, "controller-*.json")))
    rows = [combo_row(name) for name in COMBOS]
    queues = {}
    for phase in ("p1", "p2"):
        path = os.path.join(ROOT, "runtime", phase, "queue.json")
        queues[phase] = load(path) if os.path.exists(path) else None
    verdict = {"root": ROOT, "controller": load(controller[-1]) if controller else None, "checks": checks(rows),
               "queues": {k: (None if v is None else {"state": v.get("state"), "error": v.get("error"), "log": v.get("log"),
                                                       "memory": v.get("memory"), "notes": v.get("notes")}) for k, v in queues.items()},
               "combos": rows,
               "notMeasured": ["вариант B прокси (1-с клип) — не выполнялся", "база пути ног T01/T06/T08 в pf-round5/eq-round3 отсутствует",
                               "байт/с на бота — клиентов нет"],
               "scope": "DEVELOPMENT-замер на стенде (сервер без клиентов, Editor worker); не приёмка пользователем."}
    with open(os.path.join(ROOT, "verdict.json"), "w", encoding="utf-8", newline="\n") as handle:
        json.dump(verdict, handle, ensure_ascii=False, indent=1)
    with open(os.path.join(ROOT, "table.md"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write(table(rows, verdict))
    for key, value in verdict["checks"].items():
        print(("—" if value is None else ("PASS" if value else "FAIL")), key)


if __name__ == "__main__":
    main()
