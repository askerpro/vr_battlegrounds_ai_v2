"""Офлайн-вердикт Unity-раунда II наведения NPC (этапы 3-4: aim-слой + pitch; без Unity, после finish аренды).

Читает отчёты под tasks/bots-fix/reports/aim-round2/ и пишет b-verdict.json рядом.
Только stdlib. Unknown/NotObserved/NeedsReview никогда не считается PASS; NotApplicable не блокирует.

Основа — analyze.py раунда I (прежние гейты, очередь и база скольжения B2 сохранены). Новое в раунде II:
- aimAuthoring: aims[] = 36, повторная сборка Setup+Calibration побайтно равна (контроллеры x3, aim-клипы x3,
  маска, BotCombatAssets.asset), 0 ошибок раскладки слоёв, тождество aim-слоя при AimPitch=0 < 0.01°;
- гейт aimPitch: медиана |pitch| дула в Idle и Shoot <= 1.5° отдельно по combo, по кадрам с активным AimPitch
  (aim-frames.json, каждый кадр после gate); Move — справочно;
- гейт shootTarget: доля кадров Shoot, где действительный луч ствола упирается в цель (muzzleAim, visual-frames) >= 0.8;
- гейт shotsVsRound1: SDK-выстрелы (ledger Shot Emitted) AK105 у MEF и Cyborg не меньше раунда I (39 и 24);
  для PPK — справочно (NotApplicable);
- aimPitchControl: |AimPitch| <= 25, скорость <= 120°/с (допуск 5%), интеграл, насыщение, квант SyncVar (b-sampler 0.1 с);
- yaw-гейт и скольжение — как в раунде I;
- stage4Verdict — критерий этапов 3-4 плана; verdict — все гейты.

Запуск из корня worktree:  python -I tasks/bots-fix/tools/aim-round2/analyze.py [root]
"""
import json
import math
import pathlib
import statistics
import sys

ROOT = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "tasks/bots-fix/reports/aim-round2")
COMBOS = {
    "mef-ak105": ("Assets/Prefabs/Player/Optimized_MEF_Player.prefab", "AK105_Weapon", "Rifle", ("T07", "T05")),
    "cyborg-ak105": ("Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab", "AK105_Weapon", "Rifle", ("T07", "T05")),
    "mef-ppk": ("Assets/Prefabs/Player/Optimized_MEF_Player.prefab", "PPK_Weapon", "Pistol", ("T07",)),
    "cyborg-ppk": ("Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab", "PPK_Weapon", "Pistol", ("T07",)),
}
# Движение до наведения (b-verdict.json, metrics): B2 для трёх combo, B для cyborg-ppk (в B2 не запускался).
MOVEMENT_BASELINE = {
    "mef-ak105": dict(run="armed-runtime-b2", movingSamples=141, slidingSamples=0, directionConsistent=120, directionMismatched=4),
    "cyborg-ak105": dict(run="armed-runtime-b2", movingSamples=167, slidingSamples=1, directionConsistent=144, directionMismatched=4),
    "mef-ppk": dict(run="armed-runtime-b2", movingSamples=61, slidingSamples=0, directionConsistent=48, directionMismatched=0),
    "cyborg-ppk": dict(run="armed-runtime-b", movingSamples=158, slidingSamples=0, directionConsistent=131, directionMismatched=4),
}
SLIDING_TOLERANCE = 0.02   # допустимый рост доли slidingSamples / movingSamples
MISMATCH_TOLERANCE = 0.03  # допустимый рост доли directionMismatched / (consistent + mismatched)
AIM_YAW_LIMIT = 1.5        # градусы, медиана |yaw| дула (этап 2)
AIM_PITCH_LIMIT = 1.5      # градусы, медиана |pitch| дула в Idle/Shoot (этап 4)
SHOOT_TARGET_SHARE = 0.8   # доля кадров Shoot с лучом ствола в цель (этап 4)
SHOOT_TARGET_MIN_FRAMES = 3
PITCH_LIMIT, PITCH_RATE_LIMIT, PITCH_INTEGRAL_LIMIT = 25.0, 120.0, 5.0
PITCH_QUANT = 0.25
# SDK-выстрелы раунда I (reports/aim-round1/b-verdict.json, metrics.ledgerShotCommitsEmitted); гейт только AK105.
ROUND1_SHOTS = {"mef-ak105": 39, "cyborg-ak105": 24, "mef-ppk": 19, "cyborg-ppk": 18}
SHOT_GATED = ("mef-ak105", "cyborg-ak105")
AIM_MIN_FRAMES = 3         # кадров visual-frames (0.5 с) на состояние для вердикта
AIM_MIN_EVERY_FRAMES = 30  # кадров aim-frames.json (каждый кадр) на состояние для вердикта
DELTA_LIMIT, DELTA_RATE_LIMIT = 15.0, 90.0
# Оружейные события наблюдателя стенда (не damage/actor): по ним видно, что timeline восстановлен.
WEAPON_EVENTS = ("source-emitted", "firearm-projectile-event", "ledger-commit", "readiness-fault", "sample",
                 "preparation-first-pre-request", "first-seen-registration", "first-seen-sample",
                 "first-seen-pre-request", "first-seen-commit", "first-seen-existing")
DIRECTIONS = {"Move": (0.0, 1.0), "Back": (0.0, -1.0), "Left": (-1.0, 0.0), "Right": (1.0, 0.0)}
MOVING_SPEED = 0.3  # м/с между выборками 0.1 с
GATING = ("equip", "preparation", "shooting", "movement", "animationSpeed", "strafe", "turns", "aimYaw", "sliding",
          "aimPitch", "shootTarget", "shotsVsRound1")
STAGE4_GATING = ("equip", "aimYaw", "aimPitch", "shootTarget", "shotsVsRound1", "sliding")


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


def sampler_deltas(samplers):
    """Fallback без timeline: дельты ShotSequence/патронов по b-sampler внутри каждого case."""
    seq_delta = ammo_delta = 0
    observed = False
    for samples in samplers:
        seq = [s["shotSequence"] for s in samples if s.get("shotSequence") is not None]
        totals = [s["total"] for s in samples if s.get("total") is not None]
        if seq or totals:
            observed = True
        seq_delta += (max(seq) - seq[0]) if seq else 0
        ammo_delta += (totals[0] - min(totals)) if totals else 0
    return (seq_delta, ammo_delta) if observed else (None, None)


def analyse_shooting(case_docs, subject_ids, samplers):
    emitted = projectile = commits = emitted_commits = faults = hits = unauthored = requests = 0
    baselines, damage, seq_delta, ammo_delta, observed = 0, 0.0, 0, 0, False
    weapon_events = dropped = rejected = 0
    for case in case_docs:
        dropped += int(case.get("WeaponCandidateEventsDropped") or 0)
        rejected += int(case.get("WeaponCandidatesRejected") or 0)
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
            if kind in WEAPON_EVENTS:
                weapon_events += 1
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
    source = "WeaponTimeline"
    if weapon_events == 0:
        # Timeline без оружейных событий субъекта (как в прогоне B): только косвенный источник.
        source = "b-sampler fallback (WeaponTimeline пуст; ledger/emission не наблюдались)"
        seq_delta, ammo_delta = sampler_deltas(samplers)
    metrics = dict(shootingSource=source, timelineWeaponEvents=weapon_events, weaponCandidateEventsDropped=dropped,
                   weaponCandidatesRejected=rejected,
                   blazeShootRequests=requests, sourceEmitted=emitted, projectileEvents=projectile,
                   ledgerShotCommits=commits, ledgerShotCommitsEmitted=emitted_commits, readinessFaults=faults,
                   shotSequenceDelta=seq_delta, totalRoundsDelta=ammo_delta, unauthoredEmissions=unauthored,
                   preparationBaselines=baselines, targetHits=hits, targetDamage=round(damage, 2))
    if weapon_events == 0:
        if unauthored > 0:
            shooting = verdict("FAIL", reason="снаряд без author authority", source=source)
        elif (seq_delta or 0) >= 1 and (ammo_delta or 0) >= 1:
            shooting = verdict("NeedsReview", reason="выстрелы видны только по b-sampler; timeline не восстановлен",
                               source=source)
        elif requests > 0:
            shooting = verdict("FAIL", reason="запросы Blaze не стали SDK-выстрелами (по b-sampler)", source=source)
        else:
            shooting = verdict("Unknown", reason="Blaze не запрашивал атаку", source=source)
        hit = verdict("PASS") if hits else verdict("NotObserved")
        return metrics, shooting, hit
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


def quantiles(values):
    if not values:
        return None
    ordered = sorted(values)

    def q(fraction):
        index = fraction * (len(ordered) - 1)
        low, high = math.floor(index), math.ceil(index)
        return ordered[low] + (ordered[high] - ordered[low]) * (index - low)

    return dict(n=len(ordered), min=round(ordered[0], 2), p10=round(q(0.1), 2), median=round(q(0.5), 2),
                p90=round(q(0.9), 2), max=round(ordered[-1], 2))


def muzzle_rows(frames):
    rows = []
    for frame in frames or []:
        muzzle = frame.get("muzzle")
        if not isinstance(muzzle, dict) or not frame.get("weapon"):
            continue
        animator = muzzle.get("animator") or {}
        state = animator.get("state") or "none"
        if animator.get("transition") and animator.get("next"):
            state = state + "->" + animator["next"]
        rows.append(dict(state=state, muzzle=muzzle))
    return rows


def analyse_muzzle(frame_sets):
    rows = [row for frames in frame_sets for row in muzzle_rows(frames)]
    if not rows:
        return verdict("Unknown", reason="нет кадров с полем muzzle (рекордер не обновлён или оружие не взято)")
    timing, status, colliders, states = {}, {}, {}, {}
    is_target = gate = agree = after_gate = 0
    for row in rows:
        muzzle = row["muzzle"]
        kind = "BeforeRender" if str(muzzle.get("timing", "")).startswith("BeforeRender") else "AvatarsUpdated"
        timing[kind] = timing.get(kind, 0) + 1
        forward = muzzle.get("forwardStatus") or "Unknown"
        status[forward] = status.get(forward, 0) + 1
        hit = muzzle.get("forwardHit") or {}
        target = bool(hit.get("isTarget"))
        if hit:
            label = hit.get("collider") or "?"
            if hit.get("isTarget"):
                label = "target:" + label
            elif hit.get("isSelf"):
                label = "self:" + label
            colliders[label] = colliders.get(label, 0) + 1
        is_target += target
        if kind == "BeforeRender":
            after_gate += 1
            gate += bool(muzzle.get("seesTargetGate"))
            agree += target == bool(muzzle.get("seesTargetGate"))
        group = states.setdefault(row["state"], dict(pitch=[], yaw=[], angle=[], frames=0, isTarget=0))
        group["frames"] += 1
        group["isTarget"] += target
        aim = muzzle.get("aim") or {}
        for key in ("pitch", "yaw", "angle"):
            if aim.get(key) is not None:
                group[key].append(float(aim[key]))
    by_state = {name: dict(frames=g["frames"], isTargetShare=round(g["isTarget"] / g["frames"], 3),
                           pitch=quantiles(g["pitch"]), yaw=quantiles(g["yaw"]), angle=quantiles(g["angle"]))
                for name, g in sorted(states.items(), key=lambda item: -item[1]["frames"])}
    top = dict(sorted(colliders.items(), key=lambda item: -item[1])[:8])
    return dict(verdict="Measured", frames=len(rows), isTargetShare=round(is_target / len(rows), 3),
                timing=timing, forwardStatus=status, forwardHitColliders=top, byAnimatorState=by_state,
                gateConsistency=dict(afterGateFrames=after_gate, seesTargetGate=gate, isTargetMatchesGate=agree,
                                     note="SeesTarget зависит и от допуска боя/цели driver; расхождение — повод смотреть кадры"),
                convention="pitch/yaw = ShotSource.forward минус направление на центр твёрдых коллайдеров цели; "
                           "система корня субъекта; +pitch вверх, +yaw вправо; градусы",
                scope="Информационно, не гейт: выборка рекордера 0.5 с.")


def aim_rows(frame_sets, aim_sets):
    """(источник, минимум кадров, строки state/yaw/pitch/delta/active) — покадровый, если он есть."""
    every = [row for doc in aim_sets for row in ((doc or {}).get("frames") or []) if row.get("yaw") is not None]
    if every:
        rows = [dict(state=r.get("state"), transition=r.get("transition"), yaw=r["yaw"], pitch=r.get("pitch"),
                     delta=r.get("delta"), active=bool(r.get("active")), param=r.get("aimPitch"),
                     pitchActive=r.get("pitchActive")) for r in every]
        return "aim-frames.json (каждый кадр после gate)", AIM_MIN_EVERY_FRAMES, rows
    rows = []
    for frames in frame_sets:
        for frame in frames or []:
            muzzle = frame.get("muzzle")
            if not isinstance(muzzle, dict) or not frame.get("weapon"):
                continue
            animator = muzzle.get("animator") or {}
            aim = muzzle.get("aim") or {}
            if aim.get("yaw") is None:
                continue
            control = frame.get("aim") or {}
            pitch_control = control.get("pitch") or {}
            rows.append(dict(state=animator.get("state"), transition=animator.get("transition"), yaw=aim["yaw"],
                             pitch=aim.get("pitch"), delta=control.get("delta"), active=bool(control.get("deltaActive")),
                             param=pitch_control.get("param"), pitchActive=pitch_control.get("active")))
    return "visual-frames.json (выборка 0.5 с)", AIM_MIN_FRAMES, rows


def analyse_aim_yaw(frame_sets, aim_sets=()):
    """Гейт этапа 2: медиана |yaw| дула в Idle и Shoot по кадрам с активной поправкой корпуса."""
    source, minimum, rows = aim_rows(frame_sets, aim_sets)
    groups = {}
    for row in rows:
        state = row["state"]
        if state not in ("Idle", "Shoot") or row.get("transition"):
            continue
        keys = [state + ":all"] + ([state + ":active"] if row["active"] else [])
        for key in keys:
            group = groups.setdefault(key, dict(yaw=[], pitch=[], delta=[]))
            group["yaw"].append(abs(float(row["yaw"])))
            if row.get("pitch") is not None:
                group["pitch"].append(float(row["pitch"]))
            if row.get("delta") is not None:
                group["delta"].append(float(row["delta"]))
    table = {key: dict(absYaw=quantiles(g["yaw"]), pitch=quantiles(g["pitch"]), delta=quantiles(g["delta"]))
             for key, g in sorted(groups.items())}
    measured, failed = [], []
    for state in ("Idle", "Shoot"):
        values = (groups.get(state + ":active") or {}).get("yaw") or []
        if len(values) < minimum:
            continue
        median = statistics.median(values)
        measured.append(state)
        if median > AIM_YAW_LIMIT:
            failed.append(f"{state}: медиана |yaw| {median:.2f}° > {AIM_YAW_LIMIT}°")
    if failed:
        status = verdict("FAIL", reason="; ".join(failed))
    elif len(measured) == 2:
        status = verdict("PASS")
    else:
        status = verdict("Unknown", reason="мало кадров Idle/Shoot с активной поправкой (нужно >= %d на состояние)" % minimum,
                         measured=measured)
    status.update(byState=table, limit=AIM_YAW_LIMIT, source=source,
                  scope="Idle/Shoot без перехода; active = поправка корпуса BotAim.DeltaActive")
    return status


def analyse_aim_pitch(frame_sets, aim_sets=()):
    """Гейт этапа 4: медиана |pitch| дула в Idle и Shoot по кадрам с активным AimPitch; Move — справочно."""
    source, minimum, rows = aim_rows(frame_sets, aim_sets)
    if rows and all(row.get("pitchActive") is None for row in rows):
        return verdict("Unknown", reason="в кадрах нет pitchActive (рекордер/input без pitch-контура)", source=source)
    groups = {}
    for row in rows:
        state = row["state"]
        if state not in ("Idle", "Shoot", "Move", "Back", "Left", "Right") or row.get("transition") or row.get("pitch") is None:
            continue
        keys = [state + ":all"] + ([state + ":active"] if row.get("pitchActive") else [])
        for key in keys:
            group = groups.setdefault(key, dict(absPitch=[], pitch=[], param=[]))
            group["absPitch"].append(abs(float(row["pitch"])))
            group["pitch"].append(float(row["pitch"]))
            if row.get("param") is not None:
                group["param"].append(float(row["param"]))
    table = {key: dict(absPitch=quantiles(g["absPitch"]), pitch=quantiles(g["pitch"]), param=quantiles(g["param"]))
             for key, g in sorted(groups.items())}
    measured, failed = [], []
    for state in ("Idle", "Shoot"):
        values = (groups.get(state + ":active") or {}).get("absPitch") or []
        if len(values) < minimum:
            continue
        median = statistics.median(values)
        measured.append(state)
        if median > AIM_PITCH_LIMIT:
            failed.append(f"{state}: медиана |pitch| {median:.2f}° > {AIM_PITCH_LIMIT}°")
    if failed:
        status = verdict("FAIL", reason="; ".join(failed))
    elif len(measured) == 2:
        status = verdict("PASS")
    else:
        status = verdict("Unknown", reason="мало кадров Idle/Shoot с активным AimPitch (нужно >= %d на состояние)" % minimum,
                         measured=measured)
    status.update(byState=table, limit=AIM_PITCH_LIMIT, source=source,
                  scope="Idle/Shoot без перехода — гейт; Move/Back/Left/Right — справочно; active = BotAim.PitchActive")
    return status


def analyse_pitch_control(samplers):
    """AimPitch по b-sampler (0.1 с): пределы, наблюдаемая скорость, интеграл, насыщение, квант SyncVar, g/basePitch."""
    rows = [s for samples in samplers for s in samples
            if isinstance(s.get("aim"), dict) and isinstance(s["aim"].get("pitch"), dict)]
    if not rows:
        return verdict("Unknown", reason="нет aim.pitch в b-sampler (input без pitch-контура?)")
    params = [float(r["aim"]["pitch"]["param"]) for r in rows if r["aim"]["pitch"].get("param") is not None]
    active = [r for r in rows if r["aim"]["pitch"].get("active")]
    integral = [float(r["aim"]["pitch"]["integral"]) for r in active if r["aim"]["pitch"].get("integral") is not None]
    rates = []
    for samples in samplers:
        seq = [s for s in samples if isinstance(s.get("aim"), dict) and isinstance(s["aim"].get("pitch"), dict)
               and s["aim"]["pitch"].get("param") is not None]
        for prev, cur in zip(seq, seq[1:]):
            dt = cur["t"] - prev["t"]
            if dt > 1e-3:
                rates.append(abs(cur["aim"]["pitch"]["param"] - prev["aim"]["pitch"]["param"]) / dt)
    sync_error = [abs(float(r["aim"]["pitch"]["synced"]) - float(r["aim"]["pitch"]["param"])) for r in rows
                  if r["aim"]["pitch"].get("synced") is not None and r["aim"]["pitch"].get("param") is not None]
    animator_error = [abs(float(r["aim"]["pitch"]["animator"]) - float(r["aim"]["pitch"]["param"])) for r in rows
                      if r["aim"]["pitch"].get("animator") is not None and r["aim"]["pitch"].get("param") is not None]
    by_state = {}
    for r in active:
        group = by_state.setdefault(r.get("state") or "none", dict(param=[], g=[], basePitch=[]))
        for key in ("param", "g", "basePitch"):
            if r["aim"]["pitch"].get(key) is not None:
                group[key].append(float(r["aim"]["pitch"][key]))
    over = [p for p in params if abs(p) > PITCH_LIMIT + 1e-3]
    fast = [v for v in rates if v > PITCH_RATE_LIMIT * 1.05]
    wound = [i for i in integral if abs(i) > PITCH_INTEGRAL_LIMIT + 1e-3]
    status = "FAIL" if over or fast or wound else "Measured"
    return dict(verdict=status, samples=len(rows), activeShare=round(len(active) / len(rows), 3),
                param=quantiles(params), absParam=quantiles([abs(p) for p in params]),
                saturatedShare=round(sum(abs(p) >= PITCH_LIMIT - 0.1 for p in params) / len(params), 3) if params else None,
                integral=quantiles(integral), observedRate=quantiles(rates),
                syncQuantError=quantiles(sync_error), animatorParamError=quantiles(animator_error),
                overLimit=len(over), overRate=len(fast), integralOverLimit=len(wound),
                activeByState={k: {key: quantiles(v) for key, v in g.items()} for k, g in sorted(by_state.items())},
                scope="Информационно (FAIL только при |AimPitch|>25, скорости >120°/с с допуском 5% или |I|>5°). "
                      "syncQuantError — сервер: квант SyncVar против параметра (ожидание <= 0.125°).")


def analyse_shoot_target(muzzle, frame_sets, aim_sets=()):
    """Гейт этапа 4: доля кадров Shoot (visual-frames), где луч ствола упирается в цель; справочно — gate по кадрам."""
    states = (muzzle or {}).get("byAnimatorState") or {}
    shoot = states.get("Shoot") or {}
    every = [row for doc in aim_sets for row in ((doc or {}).get("frames") or [])
             if row.get("state") == "Shoot" and not row.get("transition") and row.get("sees") is not None]
    details = dict(frames=shoot.get("frames"), isTargetShare=shoot.get("isTargetShare"), limit=SHOOT_TARGET_SHARE,
                   everyFrameSeesShare=round(sum(bool(r["sees"]) for r in every) / len(every), 3) if every else None,
                   everyFrames=len(every),
                   scope="isTargetShare — visual-frames 0.5 с (луч ShotSource.forward = gate Sees); "
                         "everyFrameSeesShare — справочно, BotGunner.SeesTarget каждый кадр")
    if not shoot or (shoot.get("frames") or 0) < SHOOT_TARGET_MIN_FRAMES:
        # Мало выборок 0.5 с: консервативная замена — gate Sees каждого кадра (Sees требует того же луча в цель
        # и допуска боя, поэтому его доля не выше isTargetShare).
        if len(every) >= AIM_MIN_EVERY_FRAMES:
            share = details["everyFrameSeesShare"]
            details["source"] = "aim-frames.json sees (замена: мало visual-frames)"
            if share < SHOOT_TARGET_SHARE:
                return verdict("FAIL", reason=f"доля Sees в Shoot {share} < {SHOOT_TARGET_SHARE}", **details)
            return verdict("PASS", **details)
        return verdict("Unknown", reason="мало кадров Shoot в visual-frames (нужно >= %d) и в aim-frames" % SHOOT_TARGET_MIN_FRAMES, **details)
    details["source"] = "visual-frames.json isTarget"
    if shoot["isTargetShare"] < SHOOT_TARGET_SHARE:
        return verdict("FAIL", reason=f"isTargetShare в Shoot {shoot['isTargetShare']} < {SHOOT_TARGET_SHARE}", **details)
    return verdict("PASS", **details)


def analyse_shots(combo, metrics):
    """Гейт этапа 4: SDK-выстрелы AK105 (ledger Shot Emitted) не меньше раунда I; PPK — справочно."""
    shots = metrics.get("ledgerShotCommitsEmitted")
    base = ROUND1_SHOTS.get(combo)
    details = dict(shots=shots, round1=base, sourceEmitted=metrics.get("sourceEmitted"), targetHits=metrics.get("targetHits"),
                   blazeShootRequests=metrics.get("blazeShootRequests"))
    if combo not in SHOT_GATED:
        return verdict("NotApplicable", reason="справочно: гейт только для AK105", **details)
    if shots is None or base is None:
        return verdict("Unknown", reason="нет ledger-выстрелов или базы раунда I", **details)
    if shots < base:
        return verdict("FAIL", reason=f"выстрелов {shots} < раунда I {base}", **details)
    return verdict("PASS", **details)


def analyse_aim_delta(samplers):
    """Статистика δ по b-sampler (0.1 с): активность, насыщение, интеграл, наблюдаемая скорость."""
    rows = [s for samples in samplers for s in samples if isinstance(s.get("aim"), dict)]
    if not rows:
        return verdict("Unknown", reason="нет поля aim в b-sampler (input без BotAim?)")
    deltas = [float(r["aim"]["delta"]) for r in rows if r["aim"].get("delta") is not None]
    active = [r for r in rows if r["aim"].get("deltaActive")]
    integral = [float(r["aim"]["integral"]) for r in active if r["aim"].get("integral") is not None]
    rates = []
    for samples in samplers:
        seq = [s for s in samples if isinstance(s.get("aim"), dict) and s["aim"].get("delta") is not None]
        for prev, cur in zip(seq, seq[1:]):
            dt = cur["t"] - prev["t"]
            if dt > 1e-3:
                rates.append(abs(cur["aim"]["delta"] - prev["aim"]["delta"]) / dt)
    by_state = {}
    for r in active:
        by_state.setdefault(r.get("state") or "none", []).append(float(r["aim"]["delta"]))
    over = [d for d in deltas if abs(d) > DELTA_LIMIT + 1e-3]
    fast = [v for v in rates if v > DELTA_RATE_LIMIT * 1.05]
    status = "FAIL" if over or fast else "Measured"
    return dict(verdict=status, samples=len(rows), activeShare=round(len(active) / len(rows), 3),
                delta=quantiles(deltas), absDelta=quantiles([abs(d) for d in deltas]),
                saturatedShare=round(sum(abs(d) >= DELTA_LIMIT - 0.1 for d in deltas) / len(deltas), 3) if deltas else None,
                integral=quantiles(integral), observedRate=quantiles(rates),
                overLimit=len(over), overRate=len(fast),
                activeDeltaByState={k: quantiles(v) for k, v in sorted(by_state.items())},
                scope="Информационно (FAIL только при нарушении |δ|<=15° или скорости <=90°/с с допуском 5%).")


def analyse_sliding(combo, metrics):
    base = MOVEMENT_BASELINE.get(combo)
    moving = metrics.get("movingSamples") or 0
    directional = (metrics.get("directionConsistent") or 0) + (metrics.get("directionMismatched") or 0)
    if base is None or moving < 20 or directional < 10:
        return verdict("Unknown", reason="мало moving-выборок или нет базы", baseline=base)
    share = (metrics.get("slidingSamples") or 0) / moving
    mismatch = (metrics.get("directionMismatched") or 0) / directional
    base_share = base["slidingSamples"] / base["movingSamples"]
    base_mismatch = base["directionMismatched"] / (base["directionConsistent"] + base["directionMismatched"])
    details = dict(slidingShare=round(share, 3), baselineSlidingShare=round(base_share, 3),
                   mismatchShare=round(mismatch, 3), baselineMismatchShare=round(base_mismatch, 3), baseline=base)
    if share > base_share + SLIDING_TOLERANCE or mismatch > base_mismatch + MISMATCH_TOLERANCE:
        return verdict("FAIL", reason="скольжение/рассогласование направления хуже прогона до наведения", **details)
    return verdict("PASS", **details)


def analyse_combo(combo, expected):
    source, weapon, category, expected_cases = expected
    folder = ROOT / "runtime" / combo
    summary = load(folder / "summary.json")
    if summary is None:
        return dict(verdict="Unknown", reason="нет summary.json")
    cases = summary.get("cases") or []
    invalid = [c.get("Id") + ": " + str(c.get("Error")) for c in cases if c.get("Status") in ("InvalidFixture", "Unsupported")]
    samplers, frame_sets, aim_sets = [], [], []
    for case in cases:
        case_folder = folder / f"{case.get('Id')}-{case.get('Seed')}"
        doc = load(case_folder / "b-sampler.json")
        samplers.append((doc or {}).get("samples") or [])
        frame_sets.append(load(case_folder / "visual-frames.json") or [])
        aim_sets.append(load(case_folder / "aim-frames.json") or {})
    missing_cases = sorted(set(expected_cases) - {c.get("Id") for c in cases})
    if missing_cases:
        invalid.append("нет case: " + ", ".join(missing_cases))
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
    shooting_metrics, result["shooting"], result["targetHit"] = analyse_shooting(cases, subject_ids, samplers)
    result["muzzleAim"] = analyse_muzzle(frame_sets)
    result["aimYaw"] = analyse_aim_yaw(frame_sets, aim_sets)
    result["aimDelta"] = analyse_aim_delta(samplers)
    result["aimPitch"] = analyse_aim_pitch(frame_sets, aim_sets)
    result["aimPitchControl"] = analyse_pitch_control(samplers)
    result["shootTarget"] = analyse_shoot_target(result["muzzleAim"], frame_sets, aim_sets)
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
    result["sliding"] = analyse_sliding(combo, movement_metrics)
    result["shotsVsRound1"] = analyse_shots(combo, shooting_metrics)
    if result["aimPitchControl"].get("verdict") == "FAIL":
        result["aimPitch"] = verdict("FAIL", reason="AimPitch нарушает ограничения |param|/скорость/интеграл",
                                     byState=result["aimPitch"].get("byState"))
    if result["aimDelta"].get("verdict") == "FAIL":
        result["aimYaw"] = verdict("FAIL", reason="δ нарушает ограничения |δ|/скорость", byState=result["aimYaw"].get("byState"))
    stage4 = [result[k]["verdict"] for k in STAGE4_GATING if result[k]["verdict"] != "NotApplicable"]
    result["stage4Verdict"] = "InvalidFixture" if invalid else "FAIL" if "FAIL" in stage4 else \
        "PASS" if all(v == "PASS" for v in stage4) else "Unknown"
    gating = [result[k]["verdict"] for k in GATING if result[k]["verdict"] != "NotApplicable"]
    if invalid:
        result["verdict"], result["invalid"] = "InvalidFixture", invalid
    elif "FAIL" in gating:
        result["verdict"] = "FAIL"
    elif all(v == "PASS" for v in gating):
        result["verdict"] = "PASS"
    else:
        result["verdict"] = "Unknown"
    return result


def analyse_aim_authoring(result):
    """B1: aims[] = 36 с g, побайтная повторяемость Setup+Calibration, раскладка слоёв, тождество слоя при AimPitch=0."""
    if result is None:
        return verdict("Unknown", reason="нет result.json")
    determinism = result.get("determinism") or {}
    layer = result.get("aimLayer") or {}
    count = result.get("aimCount")
    aims = result.get("aims") or []
    gains = [a.get("pitchGain") for a in aims]
    nonlinearity = [a.get("pitchNonlinearity") for a in aims if a.get("pitchNonlinearity") is not None]
    finite_gains = [g for g in gains if isinstance(g, (int, float)) and math.isfinite(g)]
    details = dict(aimCount=count, repeatBytesEqual=determinism.get("repeatBytesEqual"),
                   generatedBytesEqual=determinism.get("generatedBytesEqual"),
                   layoutErrors=len(layer.get("layoutErrors") or []) if layer.get("layoutErrors") is not None else None,
                   identityMaxDeg=layer.get("identityMaxDeg"), maskLeakMaxDeg=layer.get("maskLeakMaxDeg"),
                   gainSigns=layer.get("gainSigns"), nonlinearOver10=layer.get("nonlinearOver10"),
                   gain=quantiles(finite_gains), absGain=quantiles([abs(g) for g in finite_gains]),
                   nonlinearity=quantiles(nonlinearity),
                   libraryShaFirst=determinism.get("libraryShaFirst"), libraryShaRepeat=determinism.get("libraryShaRepeat"))
    if count is None or determinism.get("generatedBytesEqual") is None or layer.get("identityMaxDeg") is None:
        return verdict("Unknown", reason="B1 не дошёл до проверки aims/детерминизма/тождества слоя", **details)
    failed = []
    if count != 36 or len(finite_gains) != 36 or any(abs(g) < 0.1 for g in finite_gains):
        failed.append("aims != 36 или g не конечен/около нуля")
    if determinism.get("repeatBytesEqual") is not True or determinism.get("generatedBytesEqual") is not True:
        failed.append("повторная сборка Setup+Calibration изменила байты")
    if details["layoutErrors"] != 0:
        failed.append("раскладка слоёв не по контракту")
    if not (layer.get("identityMaxDeg") < 0.01):
        failed.append("aim-слой при AimPitch=0 меняет позу")
    if failed:
        return verdict("FAIL", reason="; ".join(failed), **details)
    return verdict("PASS", **details)


def main():
    queue = load(ROOT / "runtime" / "queue.json")
    out = dict(
        authoring=passed_of(load(ROOT / "result.json")),
        identityPostAuthoring=passed_of(load(ROOT / "identity-post-authoring.json")),
        identityPostPlay=passed_of(load(ROOT / "identity-post-play.json")),
        aimAuthoring=analyse_aim_authoring(load(ROOT / "result.json")),
        queueState=(queue or {}).get("state"),
        combos={combo: analyse_combo(combo, expected) for combo, expected in COMBOS.items()},
        scope="Технический Unity-раунд II наведения на стенде (сервер); не игровая/пользовательская приёмка, "
              "не Quest/remote client (клиентская проекция AimPitch — этап 5).",
    )
    common = [out["authoring"], out["identityPostAuthoring"], out["identityPostPlay"], out["aimAuthoring"]["verdict"]]
    if out["queueState"] != "completed":
        common.append("Unknown")

    def fold(states):
        return "FAIL" if "FAIL" in states else "PASS" if all(s == "PASS" for s in states) else "Unknown"

    out["verdict"] = fold(common + [c["verdict"] for c in out["combos"].values()])
    out["stage4Verdict"] = fold(common + [c.get("stage4Verdict", "Unknown") for c in out["combos"].values()])
    (ROOT / "b-verdict.json").write_text(json.dumps(out, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    compact = dict(verdict=out["verdict"], stage4Verdict=out["stage4Verdict"], authoring=out["authoring"],
                   aimAuthoring=out["aimAuthoring"]["verdict"], identityPostAuthoring=out["identityPostAuthoring"],
                   identityPostPlay=out["identityPostPlay"], queueState=out["queueState"],
                   combos={k: dict(verdict=v.get("verdict"), stage4Verdict=v.get("stage4Verdict"),
                                   **{g: v[g]["verdict"] for g in GATING if g in v},
                                   shootingSource=(v.get("metrics") or {}).get("shootingSource"),
                                   muzzleIsTargetShare=(v.get("muzzleAim") or {}).get("isTargetShare"))
                           for k, v in out["combos"].items()},
                   report=str(ROOT / "b-verdict.json"))
    print(json.dumps(compact, ensure_ascii=False))


if __name__ == "__main__":
    main()
