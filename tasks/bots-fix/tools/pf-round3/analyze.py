"""Офлайн-сводка раунда pf-round3 (раунд 2: + raw/excluded ряды зазоров, reach-разбор, новый детектор стоп, engagedToTargetCenter) (этап pf-prototype) по отчётам стенда.

Читает <root>/runtime/<combo>/summary.json (cases[].Metrics["playerFlow"] пишет BotStandPlayerFlowProbe,
subjectProjectileEvents/subjectBlazeShootRequests — BotStandRecorder) и b-subject.json; пишет <root>/pf-verdict.json
и печатает компактную сводку. Критерии — tasks/bots-fix/pf-prototype-spec.md «Критерии этапа». Это DEVELOPMENT-замер.
Запуск: python -I tasks/bots-fix/tools/pf-round3/analyze.py [root]
"""
import json
import os
import sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else "tasks/bots-fix/reports/pf-round3"
ROUND4_MEF_AK105 = {"shots": 101, "projectiles": 101}  # раунд IV: MEF AK105 T07+T05, 101/101
MARKERS = ["BotDirector.Think", "BotRuntimeController.Update", "BotRuntimeController.LateUpdate",
           "BotAvatarDriver.Apply", "BotHands.Tick", "BotWeaponAim.Solve", "BotGunner.Sees"]


def load(path):
    with open(path, encoding="utf-8-sig") as handle:
        return json.load(handle)


def r(value, digits=3):
    return round(value, digits) if isinstance(value, (int, float)) else value


def stats(block):
    if not isinstance(block, dict):
        return None
    return {k: r(block.get(k)) for k in ("count", "median", "p95", "max", "mean")}


def gate_row(gate):
    if not isinstance(gate, dict):
        return None
    return {"maxAimErrorDeg": gate.get("maxAimErrorDeg"), "notAimedAimErrorDeg": stats(gate.get("notAimedAimErrorDeg")),
            "occludedExamples": (gate.get("occludedFirstColliderExamples") or [])[:3],
            "occludedExamplesTotal": len(gate.get("occludedFirstColliderExamples") or [])}


def lag_row(lag):
    if not isinstance(lag, dict):
        return None
    over, closer = lag.get("gapFramesOver10mm"), lag.get("gapFramesCloserToPreviousCommanded")
    return {"lagMm": stats(lag.get("lagMm")), "lagDeg": stats(lag.get("lagDeg")),
            "lagToPreviousCommandedMm": stats(lag.get("lagToPreviousCommandedMm")),
            "commandedSpeedMps": stats(lag.get("commandedSpeedMps")), "commandedAngularSpeedDegps": stats(lag.get("commandedAngularSpeedDegps")),
            "corrGapVsCommandedAngularSpeed": r(lag.get("corrGapVsCommandedAngularSpeed")),
            "corrGapVsCommandedSpeed": r(lag.get("corrGapVsCommandedSpeed")), "corrGapVsLagToPrevious": r(lag.get("corrGapVsLagToPrevious")),
            "gapFramesOver10mm": over, "gapFramesCloserToPreviousCommanded": closer,
            "closerToPreviousShare": r(closer / over, 3) if over else None}


def conditions_row(cond):
    if not isinstance(cond, dict):
        return None
    weapons = []
    for w in cond.get("weapons") or []:
        weapons.append({k: w.get(k) for k in ("weapon", "rootRecoilPolicyInstalled", "policyAppliedFrame", "policyAppliedBeforeFirstShot",
                                               "recoilAccumulatorPresent", "recoilAccumulatorEnabled", "accumulatorMaxPitchDeg",
                                               "accumulatorMaxYawDeg")})
    return {"zeroAiAimSpread": cond.get("zeroAiAimSpread"), "noRecoil": cond.get("noRecoil"), "weapons": weapons, "error": cond.get("error")}


def reach_row(reach):
    if not isinstance(reach, dict):
        return None
    over_reach, over_other = reach.get("gapFramesOver10mmReach"), reach.get("gapFramesOver10mmOther")
    total_over = (over_reach or 0) + (over_other or 0)
    return {"rightWristExcessMm": stats(reach.get("rightWristExcessMm")), "leftWristExcessMm": stats(reach.get("leftWristExcessMm")),
            "weaponPullbackMm": stats(reach.get("weaponPullbackMm")), "pullbackLimitedFrames": reach.get("pullbackLimitedFrames"),
            "gapFramesOver10mmReach": over_reach, "gapFramesOver10mmOther": over_other,
            "reachShareOfGapFrames": r(over_reach / total_over, 3) if total_over else None}


def case_row(case, folder):
    metrics = case.get("Metrics") or {}
    pf = metrics.get("playerFlow") or {}
    subject = None
    subject_path = os.path.join(folder, case["Id"] + "-" + str(case["Seed"]), "b-subject.json")
    if os.path.exists(subject_path):
        subject = load(subject_path)
    inputs_path = os.path.join(folder, case["Id"] + "-" + str(case["Seed"]), "inputs.json")
    avatar = load(inputs_path).get("avatarPath") if os.path.exists(inputs_path) else None
    muzzle = pf.get("muzzleErrorDeg") or {}
    grips = pf.get("gripGapMm") or {}
    foot = pf.get("footSlide") or {}
    server = pf.get("serverMs") or {}
    return {
        "case": case["Id"], "seed": case["Seed"], "status": case.get("Status"), "error": case.get("Error"),
        "duration": r(case.get("Duration")), "avatar": avatar,
        "findings": (case.get("Findings") or [])[:5],
        "mode": pf.get("mode"), "frames": pf.get("frames"),
        "shots": pf.get("shots", metrics.get("shots")), "standShots": metrics.get("shots"),
        "projectiles": metrics.get("subjectProjectileEvents"), "blazeRequests": metrics.get("subjectBlazeShootRequests"),
        "hitsOnTarget": pf.get("hitsOnTarget"), "shotOutcomes": pf.get("shotOutcomes"),
        "hitRate": r(pf.get("hitRate")), "foreignDamageEventsOnTarget": pf.get("foreignDamageEventsOnTarget"),
        "gate": gate_row(pf.get("gate")), "weaponLag": lag_row(pf.get("weaponLagToCommanded")),
        "recoilEvidence": {"postShotPrimaryGrabberToSnapMm": stats((pf.get("recoilEvidence") or {}).get("postShotPrimaryGrabberToSnapMm")),
                           "windowSeconds": (pf.get("recoilEvidence") or {}).get("windowSeconds")} if pf.get("recoilEvidence") else None,
        "shotConditions": conditions_row(metrics.get("shotConditions")),
        "muzzleAtShot": stats(muzzle.get("atShot")), "muzzleEngaged": stats(muzzle.get("engagedFrames")),
        "muzzleToTargetCenter": stats(muzzle.get("engagedToTargetCenter")),
        "grip": {k: stats(grips.get(k)) for k in ("primaryCommandedToSnap", "supportCommandedToSnap",
                                                  "primaryGrabberToSnap", "supportGrabberToSnap")},
        "gripRaw": {k: stats((grips.get("raw") or {}).get(k)) for k in ("primaryCommandedToSnap", "supportCommandedToSnap",
                                                                       "primaryGrabberToSnap", "supportGrabberToSnap")},
        "gripExcluded": grips.get("excludedTransient"),
        "reach": reach_row(grips.get("reach")),
        "footSlide": dict({k: r(foot.get(k), 4) for k in ("plantedSlideMeters", "plantedFootSeconds", "metersPerPlantedSecond",
                                                          "bodyTravelMeters", "slidePerTravelMeter", "plantedSampleFraction",
                                                          "plantedEpisodes", "onsetHorizontalSpeedMps")},
                          lowestFootAboveRoot=stats(foot.get("lowestFootAboveRootMeters"))),
        "serverMs": {name: {"mean": r((server.get(name) or {}).get("mean"), 4), "p95": r((server.get(name) or {}).get("p95"), 4),
                            "meanPerBot": r((server.get(name) or {}).get("meanPerBotMs"), 4),
                            "valid": (server.get(name) or {}).get("recorderValid"), "n": (server.get(name) or {}).get("count")}
                     for name in MARKERS},
        "gcAllocBytesPerFrame": stats(pf.get("gcAllocBytesPerFrame")),
        "network": pf.get("network"), "weaponUseContext": pf.get("weaponUseContext"),
        "consoleErrors": pf.get("consoleErrors"), "consoleErrorSamples": (pf.get("consoleErrorSamples") or [])[:5],
        "criteria": pf.get("criteria"), "playerFlowError": metrics.get("playerFlowError"),
        "subject": subject and {"started": subject.get("started"), "finished": subject.get("finished")},
    }


def total(rows, key):
    values = [row.get(key) for row in rows if isinstance(row.get(key), (int, float))]
    return sum(values) if values else None


def combo(name):
    folder = os.path.join(ROOT, "runtime", name)
    path = os.path.join(folder, "summary.json")
    if not os.path.exists(path):
        return {"combo": name, "missing": True}
    summary = load(path)
    rows = [case_row(case, folder) for case in summary.get("cases", [])]
    slide = total([row["footSlide"] for row in rows], "plantedSlideMeters")
    seconds = total([row["footSlide"] for row in rows], "plantedFootSeconds")
    travel = total([row["footSlide"] for row in rows], "bodyTravelMeters")
    return {"combo": name, "status": summary.get("status"), "options": summary.get("options"), "cases": rows,
            "totals": {"shots": total(rows, "shots"), "projectiles": total(rows, "projectiles"),
                       "hitsOnTarget": total(rows, "hitsOnTarget"), "blazeRequests": total(rows, "blazeRequests"),
                       "consoleErrors": total(rows, "consoleErrors"),
                       "plantedSlideMeters": r(slide, 4), "plantedFootSeconds": r(seconds, 3), "bodyTravelMeters": r(travel, 3),
                       "metersPerPlantedSecond": r(slide / seconds, 4) if slide is not None and seconds else None,
                       "slidePerTravelMeter": r(slide / travel, 4) if slide is not None and travel and travel > 0.01 else None}}


COMBOS = ["E-ak105", "PE-ppk", "F-ak105", "R-ak105", "S-ak105", "PF-ppk", "B-ak105"]


def verdict(pf, base):
    if pf.get("missing"):
        return {"stage": "Unknown", "reason": "нет отчёта"}
    t, rows = pf["totals"], pf["cases"]
    checks = {
        "hitRateAll": r((t["hitsOnTarget"] or 0) / t["shots"], 3) if t["shots"] else None,
        "allHitsCreditedToBot": all((row["criteria"] or {}).get("allHitsCreditedToBot") for row in rows),
        "muzzleAtShotMedianLe1_5": all(row["muzzleAtShot"] and row["muzzleAtShot"]["count"] and row["muzzleAtShot"]["median"] <= 1.5 for row in rows),
        "handToSnapLt10mmBothHands(p95)": all(row["grip"][k] and row["grip"][k]["count"] and row["grip"][k]["p95"] < 10
                                              for row in rows for k in ("primaryCommandedToSnap", "supportCommandedToSnap")),
        "weaponToGrabberLt1mm(p95)": all(row["grip"][k] and row["grip"][k]["count"] and row["grip"][k]["p95"] < 1
                                         for row in rows for k in ("primaryGrabberToSnap", "supportGrabberToSnap")),
        "consoleErrorsZero": (t["consoleErrors"] or 0) == 0,
        "noInvalidFixture": all(row["status"] != "InvalidFixture" for row in rows),
    }
    if base is not None and not base.get("missing"):
        b = base["totals"]
        checks["footSlideNotWorseThanBase(m/planted s)"] = bool(t["plantedFootSeconds"] and b["plantedFootSeconds"] and
                                                                t["metersPerPlantedSecond"] <= b["metersPerPlantedSecond"] * 1.02)
    return {"checks": checks, "notMeasured": ["байт/с на бота — клиентов нет (network.connections=0)"]}


def main():
    out = {"root": ROOT, "combos": {name: combo(name) for name in COMBOS}}
    out["queues"] = {}
    for phase in ("p1", "p2"):
        path = os.path.join(ROOT, "runtime", phase, "queue.json")
        out["queues"][phase] = load(path) if os.path.exists(path) else None
    base = out["combos"]["B-ak105"]
    out["verdicts"] = {name: verdict(c, base if name.endswith("ak105") and name != "B-ak105" else None)
                       for name, c in out["combos"].items()}
    out["scope"] = "DEVELOPMENT-замер на стенде (сервер без клиентов, Editor); не приёмка пользователем."
    with open(os.path.join(ROOT, "pf-verdict.json"), "w", encoding="utf-8", newline="\n") as handle:
        json.dump(out, handle, ensure_ascii=False, indent=1)
    for name, c in out["combos"].items():
        print(name, "MISSING" if c.get("missing") else json.dumps(c["totals"], ensure_ascii=False))
        print("   verdict", json.dumps(out["verdicts"][name], ensure_ascii=False))


if __name__ == "__main__":
    main()
