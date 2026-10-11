"""Офлайн-вердикт прогона B (без Unity, после finish аренды).

Читает отчёты под tasks/bots-fix/reports/armed-runtime-b/ и пишет b-verdict.json рядом.
Только stdlib. Unknown/NotObserved никогда не считается PASS.

Запуск из корня worktree:  python -I tasks/bots-fix/tools/armed-runtime-b/analyze.py [root]
"""
import json
import math
import pathlib
import statistics
import sys

ROOT = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "tasks/bots-fix/reports/armed-runtime-b")
COMBOS = {
    "mef-ppk": ("Assets/Prefabs/Player/Optimized_MEF_Player.prefab", "PPK_Weapon", "Pistol"),
    "mef-ak105": ("Assets/Prefabs/Player/Optimized_MEF_Player.prefab", "AK105_Weapon", "Rifle"),
    "cyborg-ppk": ("Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab", "PPK_Weapon", "Pistol"),
    "cyborg-ak105": ("Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab", "AK105_Weapon", "Rifle"),
}
DIRECTIONS = {"Move": (0.0, 1.0), "Back": (0.0, -1.0), "Left": (-1.0, 0.0), "Right": (1.0, 0.0)}
MOVING_SPEED = 0.3  # м/с между выборками 0.1 с
GATING = ("equip", "preparation", "shooting", "movement", "animationSpeed", "strafe", "turns")


def load(path):
    try:
        return json.loads(pathlib.Path(path).read_text(encoding="utf-8-sig"))
    except FileNotFoundError:
        return None


def verdict(status, **details):
    return dict(verdict=status, **details)


def passed_of(doc):
    if doc is None:
        return "Unknown"
    value = doc.get("passed")
    return "PASS" if value is True else "FAIL" if value is False else "Unknown"


def yaw_delta(a, b):
    return (b - a + 180.0) % 360.0 - 180.0


def analyse_movement(cases):
    travel, moving, consistent, mismatched, sliding, strafe, ratios, turn = 0.0, 0, 0, 0, 0, 0, [], 0.0
    for samples in cases:
        rows = [s for s in samples if s.get("feet") and s.get("yaw") is not None]
        for prev, cur in zip(rows, rows[1:]):
            dt = cur["t"] - prev["t"]
            if dt <= 1e-4:
                continue
            dx, dz = cur["feet"][0] - prev["feet"][0], cur["feet"][2] - prev["feet"][2]
            dist = math.hypot(dx, dz)
            travel += dist
            turn += abs(yaw_delta(prev["yaw"], cur["yaw"]))
            speed = dist / dt
            if speed < MOVING_SPEED:
                continue
            moving += 1
            state = cur.get("state")
            if state in ("Idle", "Shoot") and not cur.get("transition"):
                sliding += 1
                continue
            if state not in DIRECTIONS:
                continue
            yaw = math.radians(cur["yaw"])
            local_x = (dx * math.cos(yaw) - dz * math.sin(yaw)) / dist
            local_z = (dx * math.sin(yaw) + dz * math.cos(yaw)) / dist
            ex, ez = DIRECTIONS[state]
            if local_x * ex + local_z * ez >= 0.5:
                consistent += 1
                if state in ("Left", "Right"):
                    strafe += 1
            else:
                mismatched += 1
            native, rate = cur.get("nativeMetersPerSecond"), cur.get("rate")
            if native and rate and native > 0.01 and rate > 0.01:
                ratios.append(speed / (native * rate))
    metrics = dict(travelMeters=round(travel, 3), movingSamples=moving, directionConsistent=consistent,
                   directionMismatched=mismatched, slidingSamples=sliding, strafeSamples=strafe,
                   cumulativeYawDegrees=round(turn, 1), speedRatioSamples=len(ratios),
                   speedRatioMedian=round(statistics.median(ratios), 3) if ratios else None)
    directional = consistent + mismatched
    if moving and sliding / moving > 0.25:
        movement = verdict("FAIL", reason="скольжение: корень движется в Idle/Shoot")
    elif directional and mismatched / directional > 0.2:
        movement = verdict("FAIL", reason="клип не соответствует направлению движения")
    elif travel >= 1.0 and consistent > 0:
        movement = verdict("PASS")
    else:
        movement = verdict("Unknown", reason="движение не наблюдалось")
    if len(ratios) < 5:
        speed_verdict = verdict("Unknown", reason="мало moving-выборок")
    elif 0.75 <= statistics.median(ratios) <= 1.33:
        speed_verdict = verdict("PASS")
    else:
        speed_verdict = verdict("FAIL", reason="скорость корня не совпадает с темпом клипа")
    strafe_verdict = verdict("PASS") if strafe >= 3 else verdict("Unknown", reason="strafe не наблюдался")
    turns = verdict("PASS") if turn >= 45.0 else verdict("Unknown", reason="повороты < 45°")
    return metrics, movement, speed_verdict, strafe_verdict, turns


def analyse_rigidity(cases):
    poses = [s["weaponInHand"] for samples in cases for s in samples if s.get("weaponInHand")]
    if len(poses) < 5:
        return verdict("Unknown", reason="нет выборок оружия в руке")
    med = [statistics.median(p[i] for p in poses) for i in range(7)]
    norm = math.sqrt(sum(v * v for v in med[3:])) or 1.0
    q0 = [v / norm for v in med[3:]]
    dpos = max(math.dist(p[:3], med[:3]) for p in poses) * 1000.0
    dang = max(math.degrees(2 * math.acos(min(1.0, abs(sum(a * b for a, b in zip(p[3:], q0)))))) for p in poses)
    status = "PASS" if dpos <= 2.0 and dang <= 1.0 else "NeedsReview"
    return verdict(status, maxPositionMm=round(dpos, 3), maxAngleDeg=round(dang, 3), samples=len(poses),
                   scope="Приблизительно: выборка до LateUpdate, не гейт.")


def analyse_shooting(case_docs, subject_ids):
    emitted = projectile = commits = emitted_commits = faults = hits = unauthored = requests = 0
    baselines, damage, seq_delta, ammo_delta, observed = 0, 0.0, 0, 0, False
    for case in case_docs:
        # Каждый case создаёт новое тело и новое оружие: дельты считаются внутри case и суммируются.
        seq, totals = [], []
        metrics = case.get("Metrics") or {}
        unauthored += int(metrics.get("subjectUnauthoredProjectileEvents") or 0)
        requests += int(metrics.get("subjectBlazeShootRequests") or 0)
        for b in case.get("PreparationBaselines") or []:
            if b.get("SubjectNetId") in subject_ids:
                baselines += 1
                if b.get("ShotSequence") is not None:
                    seq.append(("baseline", b["ShotSequence"]))
                if b.get("TotalRounds") is not None:
                    totals.append(("baseline", b["TotalRounds"]))
        for e in case.get("WeaponTimeline") or []:
            kind = e.get("Event")
            if kind in ("damage-received",):
                if e.get("ActorSourceNetId") in subject_ids and not e.get("DamageCanceled") and (e.get("Damage") or 0) > 0:
                    before, after = e.get("LifeBefore"), e.get("LifeAfter")
                    if before is None or after is None or after < before:
                        hits += 1
                        damage += e.get("Damage") or 0
                continue
            if e.get("SubjectNetId") not in subject_ids:
                continue
            if kind == "source-emitted":
                emitted += 1
            elif kind == "firearm-projectile-event":
                projectile += 1
            elif kind == "readiness-fault":
                faults += 1
            elif kind == "ledger-commit" and e.get("Operation") == "Shot":
                commits += 1
                if e.get("EmissionOutcome") == "Emitted":
                    emitted_commits += 1
            for key in ("ShotSequence", "CommittedShotSequence"):
                if e.get(key) is not None:
                    seq.append((kind, e[key]))
            if e.get("TotalRounds") is not None:
                totals.append((kind, e["TotalRounds"]))
        seq_values = [v for _, v in seq]
        total_values = [v for _, v in totals]
        if seq_values or total_values:
            observed = True
        seq_delta += (max(seq_values) - seq_values[0]) if seq_values else 0
        ammo_delta += (total_values[0] - min(total_values)) if total_values else 0
    if not observed:
        seq_delta = ammo_delta = None
    metrics = dict(blazeShootRequests=requests, sourceEmitted=emitted, projectileEvents=projectile,
                   ledgerShotCommits=commits, ledgerShotCommitsEmitted=emitted_commits, readinessFaults=faults,
                   shotSequenceDelta=seq_delta, totalRoundsDelta=ammo_delta, unauthoredEmissions=unauthored,
                   preparationBaselines=baselines, targetHits=hits, targetDamage=round(damage, 2))
    if unauthored > 0:
        shooting = verdict("FAIL", reason="снаряд без author authority")
    elif emitted_commits and (not seq_delta or not ammo_delta):
        shooting = verdict("FAIL", reason="ledger Shot без изменения ShotSequence/патронов")
    elif emitted_commits >= 1 and emitted >= 1 and (seq_delta or 0) >= 1 and (ammo_delta or 0) >= 1:
        shooting = verdict("PASS")
    elif requests > 0:
        shooting = verdict("FAIL", reason="запросы Blaze не стали SDK-выстрелами")
    else:
        shooting = verdict("Unknown", reason="Blaze не запрашивал атаку")
    hit = verdict("PASS") if hits else verdict("NotObserved")
    return metrics, shooting, hit


def analyse_combo(combo, expected):
    source, weapon, category = expected
    folder = ROOT / "runtime" / combo
    summary = load(folder / "summary.json")
    if summary is None:
        return dict(verdict="Unknown", reason="нет summary.json")
    cases = summary.get("cases") or []
    invalid = [c.get("Id") + ": " + str(c.get("Error")) for c in cases if c.get("Status") in ("InvalidFixture", "Unsupported")]
    samplers = []
    for case in cases:
        doc = load(folder / f"{case.get('Id')}-{case.get('Seed')}" / "b-sampler.json")
        samplers.append((doc or {}).get("samples") or [])
    flat = [s for samples in samplers for s in samples if not s.get("missingSubject")]
    subject_ids = {s.get("subjectNetId") for s in flat if s.get("subjectNetId")}
    sources = {s.get("source") for s in flat}
    weapons = {s.get("weapon") for s in flat if s.get("weapon")}
    categories = {s.get("category") for s in flat if s.get("category")}
    controllers = {s.get("controller") for s in flat if s.get("controller")}
    result = dict(caseStatus={c.get("Id"): c.get("Status") for c in cases},
                  findings={c.get("Id"): c.get("Findings") for c in cases if c.get("Findings")},
                  samples=len(flat), sources=sorted(x for x in sources if x), weapons=sorted(weapons),
                  controllers=sorted(controllers))
    if not flat:
        result["equip"] = verdict("Unknown", reason="нет выборок sampler")
    elif sources != {source}:
        result["equip"] = verdict("FAIL", reason="модель subject не совпадает с ожидаемой")
    elif not weapons:
        result["equip"] = verdict("FAIL", reason="оружие не экипировано")
    elif weapons != {weapon} or categories != {category} or not all(category in c for c in controllers if c != "BotUnarmed"):
        result["equip"] = verdict("FAIL", reason="оружие/категория/контроллер не совпадают")
    else:
        result["equip"] = verdict("PASS")
    preparations = [s.get("preparation") for s in flat if s.get("weapon")]
    shooting_metrics, result["shooting"], result["targetHit"] = analyse_shooting(cases, subject_ids)
    if "Fired" in preparations and shooting_metrics["preparationBaselines"] >= 1:
        result["preparation"] = verdict("PASS")
    elif preparations and "Fired" not in preparations:
        result["preparation"] = verdict("FAIL", reason="подготовка не достигла готовности", seen=sorted(set(preparations)))
    else:
        result["preparation"] = verdict("Unknown", reason="нет cold baseline или выборок")
    movement_metrics, result["movement"], result["animationSpeed"], result["strafe"], result["turns"] = analyse_movement(samplers)
    result["weaponRigidity"] = analyse_rigidity(samplers)
    result["lethalHit"] = verdict("NotApplicable", reason="стенд даёт цели Life=100000 (BotStandSession.cs:104)")
    result["metrics"] = dict(shooting_metrics, **movement_metrics)
    gating = [result[k]["verdict"] for k in GATING]
    if invalid:
        result["verdict"], result["invalid"] = "InvalidFixture", invalid
    elif "FAIL" in gating:
        result["verdict"] = "FAIL"
    elif all(v == "PASS" for v in gating):
        result["verdict"] = "PASS"
    else:
        result["verdict"] = "Unknown"
    return result


def main():
    queue = load(ROOT / "runtime" / "queue.json")
    out = dict(
        authoring=passed_of(load(ROOT / "result.json")),
        identityPostAuthoring=passed_of(load(ROOT / "identity-post-authoring.json")),
        identityPostPlay=passed_of(load(ROOT / "identity-post-play.json")),
        queueState=(queue or {}).get("state"),
        combos={combo: analyse_combo(combo, expected) for combo, expected in COMBOS.items()},
        scope="Технический прогон B на стенде; не игровая/пользовательская приёмка, не Quest/remote client.",
    )
    states = [out["authoring"], out["identityPostAuthoring"], out["identityPostPlay"]] + \
             [c["verdict"] for c in out["combos"].values()]
    if out["queueState"] != "completed":
        states.append("Unknown")
    out["verdict"] = "FAIL" if "FAIL" in states else "PASS" if all(s == "PASS" for s in states) else "Unknown"
    (ROOT / "b-verdict.json").write_text(json.dumps(out, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    compact = dict(verdict=out["verdict"], authoring=out["authoring"], identityPostAuthoring=out["identityPostAuthoring"],
                   identityPostPlay=out["identityPostPlay"], queueState=out["queueState"],
                   combos={k: dict(verdict=v.get("verdict"), **{g: v[g]["verdict"] for g in GATING if g in v})
                           for k, v in out["combos"].items()},
                   report=str(ROOT / "b-verdict.json"))
    print(json.dumps(compact, ensure_ascii=False))


if __name__ == "__main__":
    main()
