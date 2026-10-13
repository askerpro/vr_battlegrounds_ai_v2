"""Сводка раунда net-round1 (этап pf-network): сервер (summary.json стенда) + наблюдатели клиентов (clients/*.json).

Запуск офлайн, после finish и копирования отчётов из worker:
    python -I tasks/bots-fix/tools/net-round1/analyze.py [--root tasks/bots-fix/reports/net-round1]
Пишет <root>/verdict.json и печатает компактную сводку. Флаги — для оценки главным агентом, не приёмка.

attempt-6 (A/B «отдача копии наблюдателя»): сервер — Metrics.netSendMarkers (флаги каждой отправки NT, серия позиций),
клиенты — bodies[].netReceive (ResetState, подстановка осей из трансформа, камера в спавне), bodies[].clientNoRecoil.
netMarkers в каждом case; abCompare — дрейф правого запястья A1 (ClientNoRecoil=false) против A2 (true).
"""
import argparse
import glob
import json
import os
import statistics
import sys
from datetime import datetime


def load(path):
    with open(path, encoding="utf-8-sig") as handle:
        return json.load(handle)


def utc(value):
    if not value:
        return None
    text = value.replace("Z", "+00:00")
    if "." in text:
        head, tail = text.split(".", 1)
        count = 0
        while count < len(tail) and tail[count].isdigit():
            count += 1
        digits, rest = tail[:count], tail[count:]
        text = head + "." + digits[:6].ljust(6, "0") + rest
    try:
        return datetime.fromisoformat(text).timestamp()
    except ValueError:
        return None


def stats(values):
    values = sorted(v for v in values if v is not None)
    if not values:
        return {"count": 0}
    p95 = values[min(len(values) - 1, int(len(values) * 0.95))]
    return {"count": len(values), "median": round(statistics.median(values), 3), "p95": round(p95, 3),
            "max": round(values[-1], 3)}


# ── Позы [px, py, pz, qx, qy, qz, qw] (Unity: левая система, кватернион x,y,z,w) ──

def q_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def q_inv(q):
    return (-q[0], -q[1], -q[2], q[3])


def q_rot(q, v):
    x, y, z, _ = q_mul(q_mul(q, (v[0], v[1], v[2], 0.0)), q_inv(q))
    return (x, y, z)


def q_angle(a, b):
    import math
    d = abs(sum(x * y for x, y in zip(a, b)))
    return math.degrees(2 * math.acos(min(1.0, d)))


def pose_delta(a, b):
    """Расстояние, мм, и угол, градусы, между двумя позами; None — нет данных."""
    if not a or not b:
        return None
    mm = sum((a[i] - b[i]) ** 2 for i in range(3)) ** 0.5 * 1000.0
    return mm, q_angle(a[3:7], b[3:7])


def relative(parent, child):
    """Поза child в системе parent (поза оружия относительно запястья)."""
    if not parent or not child:
        return None
    inv = q_inv(parent[3:7])
    pos = q_rot(inv, tuple(child[i] - parent[i] for i in range(3)))
    rot = q_mul(inv, child[3:7])
    return list(pos) + list(rot)


def forward_angle(a, b):
    """Угол между осями +Z двух поворотов (ствол и луч летят по +Z)."""
    import math
    fa, fb = q_rot(a[3:7], (0, 0, 1)), q_rot(b[3:7], (0, 0, 1))
    dot = max(-1.0, min(1.0, sum(x * y for x, y in zip(fa, fb))))
    return math.degrees(math.acos(dot))


def decompose(server_shots, client_poses):
    """Сверка выстрелов по серверной метке: что расходится у клиента — тело, кисть, оружие в кисти или луч."""
    by_t = {}
    for shot in server_shots or []:
        if shot.get("t") is not None:
            by_t[round(shot["t"], 4)] = shot
    rows = {k: [] for k in ("rootMm", "cameraMm", "cameraDeg", "wristRMm", "wristRDeg", "wristLMm",
                             "afterNtWristRvsServerMm", "afterNtToFinalWristRMm", "serverPreDriverVsFinalWristRMm",
                             "barrelVsServerMuzzleMm", "barrelVsServerMuzzleDeg", "barrelToRayDeg",
                             "rayVsServerMuzzleMm", "rayVsServerMuzzleDeg", "weaponInWristMm", "weaponInWristDeg")}
    matched = 0
    weapon_mismatch = 0
    for pose in client_poses or []:
        server = by_t.get(round(pose.get("stamp") or -1, 4))
        if server is None:
            continue
        matched += 1
        crig, srig = pose.get("rig") or {}, server.get("rig") or {}

        def add(key, value, index=0):
            if value is not None:
                rows[key].append(value[index])
        add("rootMm", pose_delta(crig.get("root"), srig.get("root")))
        add("cameraMm", pose_delta(crig.get("camera"), srig.get("camera")))
        add("cameraDeg", pose_delta(crig.get("camera"), srig.get("camera")), 1)
        add("wristRMm", pose_delta(crig.get("wristR"), srig.get("wristR")))
        add("wristRDeg", pose_delta(crig.get("wristR"), srig.get("wristR")), 1)
        add("wristLMm", pose_delta(crig.get("wristL"), srig.get("wristL")))
        add("afterNtWristRvsServerMm", pose_delta(pose.get("afterNtWristR"), srig.get("wristR")))
        add("afterNtToFinalWristRMm", pose_delta(pose.get("afterNtWristR"), crig.get("wristR")))
        add("serverPreDriverVsFinalWristRMm", pose_delta(server.get("preDriverWristR"), srig.get("wristR")))
        add("barrelVsServerMuzzleMm", pose_delta(pose.get("barrel"), server.get("muzzle")))
        add("barrelVsServerMuzzleDeg", pose_delta(pose.get("barrel"), server.get("muzzle")), 1)
        add("rayVsServerMuzzleMm", pose_delta(pose.get("ray"), server.get("muzzle")))
        if pose.get("ray") and server.get("muzzle"):
            rows["rayVsServerMuzzleDeg"].append(forward_angle(pose["ray"], server["muzzle"]))
        if pose.get("barrel") and pose.get("ray"):
            rows["barrelToRayDeg"].append(forward_angle(pose["barrel"], pose["ray"]))
        weapon = pose_delta(relative(crig.get("wristR"), pose.get("barrel")), relative(srig.get("wristR"), server.get("muzzle")))
        add("weaponInWristMm", weapon)
        add("weaponInWristDeg", weapon, 1)
        if pose.get("weaponUniqueId") and server.get("weaponUniqueId") and pose["weaponUniqueId"] != server["weaponUniqueId"]:
            weapon_mismatch += 1
    result = {"matched": matched, "clientShots": len(client_poses or []), "serverShots": len(server_shots or []),
              "weaponUniqueIdMismatch": weapon_mismatch}
    result.update({k: stats(v) for k, v in rows.items()})
    result["note"] = ("root/camera/wrist — клиент (после UXR) против сервера на той же метке; afterNtWristR — запястье сразу после NT "
                      "против сервера (что пришло по сети); afterNtToFinal — перезапись после NT на клиенте; serverPreDriverVsFinal — "
                      "запястье сервера до записи драйвера против итоговой (если NT шлёт до UXR — клиент получает первое); "
                      "weaponInWrist — ствол в системе запястья клиент против сервера; rayVsServerMuzzle — луч события против ствола сервера.")
    return result


def decompose_samples(server_samples, client_samples):
    """
    Сверка поз без выстрелов: записи на сетке серверного времени (ключ — netId аватара и номер узла k). Клиент показывает
    позу сервера на этом времени; расхождение раскладывается на тело, запястья, оружие в системе запястья и ShotSource.
    """
    server = {}
    for sample in server_samples or []:
        server[(sample.get("avatarNetId"), sample.get("k"))] = sample
    keys = ("rootMm", "rootDeg", "cameraMm", "cameraDeg", "wristRMm", "wristRDeg", "wristLMm", "wristLDeg",
            "wristRvsServerPreDriverMm", "weaponInWristRMm", "weaponInWristRDeg", "weaponMm", "weaponDeg",
            "shotSourceMm", "shotSourceDeg", "shotForwardDeg", "clientFrameSpanMs", "serverFrameSpanMs")
    rows = {k: [] for k in keys}
    matched = 0
    weapon_mismatch = 0
    worst = []
    for sample in client_samples or []:
        other = server.get((sample.get("avatarNetId"), sample.get("k")))
        if other is None:
            continue
        matched += 1

        def add(prefix, a, b):
            d = pose_delta(a, b)
            if d is None:
                return None
            rows[prefix + "Mm"].append(d[0])
            if prefix + "Deg" in rows:
                rows[prefix + "Deg"].append(d[1])
            return d
        add("root", sample.get("root"), other.get("root"))
        add("camera", sample.get("camera"), other.get("camera"))
        wrist = add("wristR", sample.get("wristR"), other.get("wristR"))
        add("wristL", sample.get("wristL"), other.get("wristL"))
        d = pose_delta(sample.get("wristR"), other.get("preDriverWristR"))
        if d is not None:
            rows["wristRvsServerPreDriverMm"].append(d[0])
        if sample.get("weapon") and other.get("weapon"):
            if sample.get("weaponId") != other.get("weaponId"):
                weapon_mismatch += 1
            add("weaponInWristR", relative(sample.get("wristR"), sample.get("weapon")), relative(other.get("wristR"), other.get("weapon")))
            add("weapon", sample.get("weapon"), other.get("weapon"))
            shot = add("shotSource", sample.get("shot"), other.get("shot"))
            rows["shotForwardDeg"].append(forward_angle(sample["shot"], other["shot"]))
            if shot is not None:
                worst.append((round(shot[0], 1), round(rows["shotForwardDeg"][-1], 1), round(wrist[0], 1) if wrist else None, sample.get("k")))
        if sample.get("frameSpanMs") is not None:
            rows["clientFrameSpanMs"].append(sample["frameSpanMs"])
        if other.get("frameSpanMs") is not None:
            rows["serverFrameSpanMs"].append(other["frameSpanMs"])
    result = {"matched": matched, "clientSamples": len(client_samples or []), "serverSamples": len(server_samples or []),
              "weaponIdMismatch": weapon_mismatch}
    result.update({k: stats(v) for k, v in rows.items()})
    result["worstShotSource"] = sorted(worst, reverse=True)[:5]
    result["note"] = ("Ключ — (netId аватара, узел сетки 0,25 с серверного времени). Клиент против сервера: корень, камера, запястья "
                      "(после UXR); wristRvsServerPreDriver — против запястья сервера до записи драйвера; weaponInWristR — оружие в "
                      "системе правого запястья (хват); weapon/shotSource — мировые позы оружия и ствола; shotForwardDeg — угол оси ствола. "
                      "worstShotSource: (мм, °, запястье мм, k).")
    return result


POS_Y = 2  # бит Mirror.Changed.PosY


def server_markers(case):
    """Metrics.netSendMarkers: счётчики по ролям NT (без серий) и серии для сверки с клиентом по метке."""
    markers = (case.get("Metrics") or {}).get("netSendMarkers") or {}
    compact, series = [], {}
    for avatar in markers.get("avatars") or []:
        nts = {}
        for role, nt in (avatar.get("nts") or {}).items():
            nts[role] = {k: v for k, v in nt.items() if k != "series"}
            series[(avatar.get("netId"), role)] = {round(row[0], 4): row for row in nt.get("series") or []}
        compact.append({"netId": avatar.get("netId"), "kind": avatar.get("kind"), "spawns": avatar.get("spawns"),
                        "firstDriverWrite": avatar.get("firstDriverWrite"), "nts": nts})
    return {"avatars": compact, "error": markers.get("error")}, series


def client_net_receive(bots, series):
    """
    Маркеры приёма у клиента: по ролям NT суммы счётчиков; строгий bakedFromTransform — событие ResetState, где PosY не
    передан, подстановка равна итогу прошлого кадра (1e-5) и отличается от серверного Y той же метки больше чем на 1 см.
    """
    roles = {}
    strict = {}
    cameras = []
    dropped = []
    installs = []
    for body in bots:
        receive = body.get("netReceive") or {}
        installs.extend(body.get("clientNoRecoil") or [])
        if receive.get("droppedNotSpawned"):
            dropped.append({"avatarNetId": body.get("avatarNetId"), "epoch": body.get("epoch"),
                            **{k: v for k, v in receive["droppedNotSpawned"].items() if k != "events"}})
        for spawn in receive.get("spawns") or []:
            cameras.append({"avatarNetId": body.get("avatarNetId"), "epoch": body.get("epoch"),
                            **{k: spawn.get(k) for k in ("cameraLocal", "cameraAboveRootM", "cameraSnapshots", "cameraNewestSnapshotY")}})
        for role, nt in (receive.get("nts") or {}).items():
            total = roles.setdefault(role, {"messages": 0, "posY": 0, "gapResets": 0, "emptyFills": 0, "fillEqualsPrevFinal": 0,
                                            "bakedY": 0, "bakedRot": 0, "sumDeltaYM": 0.0, "sumAbsDeltaYM": 0.0,
                                            "snapshotMismatch": 0})
            baked = nt.get("bakedCandidates") or {}
            total["messages"] += nt.get("messages", 0)
            total["posY"] += nt.get("posY", 0)
            total["gapResets"] += nt.get("gapResets", 0)
            total["emptyFills"] += nt.get("emptyFills", 0)
            total["fillEqualsPrevFinal"] += nt.get("fillEqualsPrevFinal", 0)
            total["bakedY"] += baked.get("y", 0)
            total["bakedRot"] += baked.get("rot", 0)
            total["sumDeltaYM"] = round(total["sumDeltaYM"] + baked.get("sumDeltaYM", 0.0), 5)
            total["sumAbsDeltaYM"] = round(total["sumAbsDeltaYM"] + baked.get("sumAbsDeltaYM", 0.0), 5)
            total["snapshotMismatch"] += (nt.get("snapshotCheck") or {}).get("mismatch", 0)
            row = strict.setdefault(role, {"events": 0, "matchedServer": 0, "bakedFromTransform": 0, "sumDeltaYM": 0.0,
                                           "fillMinusServerYmm": []})
            server_rows = series.get((body.get("avatarNetId"), role)) or {}
            for event in nt.get("resetEvents") or []:
                if (event.get("flags") or 0) & POS_Y:
                    continue
                row["events"] += 1
                server = server_rows.get(round(event.get("stamp") or -1, 4))
                fill, previous = event.get("fill") or [None] * 3, event.get("previousSnapshot") or [None] * 3
                if server is None or fill[1] is None or server[4] is None:
                    continue
                row["matchedServer"] += 1
                off = fill[1] - server[4]
                row["fillMinusServerYmm"].append(off * 1000.0)
                if event.get("fillEqualsPrevFinal") and abs(off) > 0.01:
                    row["bakedFromTransform"] += 1
                    if previous[1] is not None:
                        row["sumDeltaYM"] = round(row["sumDeltaYM"] + fill[1] - previous[1], 5)
    for row in strict.values():
        row["fillMinusServerYmm"] = stats(row["fillMinusServerYmm"])
    return {"byRole": roles, "strict": strict, "cameraAtSpawn": cameras, "droppedNotSpawned": dropped,
            "clientNoRecoilInstalls": installs}


def server_case(run_root, combo):
    path = os.path.join(run_root, combo, "summary.json")
    if not os.path.exists(path):
        return {"missing": path}
    summary = load(path)
    cases = summary.get("cases") or []
    if not cases:
        return {"status": summary.get("status"), "cases": 0}
    case = cases[0]
    flow = (case.get("Metrics") or {}).get("playerFlow") or {}
    network = flow.get("network") or {}
    markers = flow.get("serverMs") or {}
    hands = {name: {k: markers[name].get(k) for k in ("mean", "p95", "max")}
             for name in markers if name.startswith("BotHands.")}
    equipment = flow.get("equipment") or {}
    net_markers, net_series = server_markers(case)
    return {
        "netSendMarkers": net_markers, "netSeries": net_series,
        "status": case.get("Status"), "error": case.get("Error"), "shots": flow.get("shots"), "hits": flow.get("hitsOnTarget"),
        "muzzleAtShotDeg": (flow.get("muzzleErrorDeg") or {}).get("atShot"),
        "botBytesPerSecondPerClient": network.get("bytesPerSecondPerClient"),
        "targetBytesPerSecondPerClient": (network.get("target") or {}).get("bytesPerSecondPerClient"),
        "connections": network.get("connections"), "consoleErrors": flow.get("consoleErrors"),
        "stowDropped": equipment.get("stowDropped"), "botHandsMarkersMs": hands,
        "shotPoses": ((flow.get("netShotPoses") or {}).get("shots") or []),
        "poseSamples": ((flow.get("netShotPoses") or {}).get("samples") or []),
        "networkInventory": (flow.get("netShotPoses") or {}).get("bodies"),
    }


def body_in_case(body, entry):
    start, end = utc(entry.get("caseStartedUtc")), utc(entry.get("finishedUtc"))
    first, last = utc(body.get("firstSeenUtc")), utc(body.get("lastSeenUtc"))
    if start is None or first is None:
        return False
    end = end if end is not None else float("inf")
    last = last if last is not None else first
    return first <= end + 1.0 and last >= start - 1.0


def client_case(report, entry, server=None):
    token = entry.get("botToken")
    bots = [b for b in report.get("bodies") or [] if b.get("kind") == "bot" and b.get("token") == token and body_in_case(b, entry)]
    targets = [b for b in report.get("bodies") or [] if b.get("kind") == "target" and body_in_case(b, entry)]
    # shotSamples: [utc, угол на позе той же серверной метки, смещение мм, угол при приёме, смещение при приёме]
    angles = [s[1] for b in bots for s in (b.get("shotSamples") or [])]
    receipt = [s[3] for b in bots for s in (b.get("shotSamples") or []) if len(s) > 3]
    gaps_r = [(b.get("grabberToSnapMm") or {}).get("right", {}).get("p95") for b in bots]
    delays = [(b.get("poseDelayMs") or {}).get("median") for b in bots]
    bot_rate = [b["network"]["inBytesPerSecond"] for b in bots if (b.get("seconds") or 0) > 5]
    target_rate = [b["network"]["inBytesPerSecond"] for b in targets if (b.get("seconds") or 0) > 5]
    late = [b for b in bots if b.get("epoch", 1) > 1]
    return {
        "bodies": len(bots),
        "epochs": sorted({b.get("epoch") for b in bots}),
        "shotsSeen": sum(b.get("shots") or 0 for b in bots),
        "barrelToRayDeg": stats(angles),
        "barrelToRayAtReceiptDeg": stats(receipt),
        "shotsUnresolved": sum((b.get("shotResolve") or {}).get("unresolved", 0) for b in bots),
        "commitShotApplied": sum((b.get("ledger") or {}).get("commitShotApplied", 0) for b in bots),
        "ledgerDivergences": [(b.get("ledger") or {}).get("divergences") for b in bots if (b.get("ledger") or {}).get("divergences")],
        "rightGrabberToSnapP95Mm": [g for g in gaps_r if g is not None],
        "poseDelayMedianMs": [d for d in delays if d is not None],
        "botInBytesPerSecond": [round(r, 1) for r in bot_rate],
        "targetInBytesPerSecond": [round(r, 1) for r in target_rate],
        "duplicates": [b.get("duplicates") for b in bots],
        "botComponents": sorted({c for b in bots for c in (b.get("botComponents") or [])}),
        "final": [b.get("final") for b in bots],
        "lateSnapshots": [{"epoch": b.get("epoch"), "snapshots": b.get("snapshots")} for b in late],
        "deathWeapons": [w for b in bots for w in (b.get("deathWeapons") or [])],
        "bornDead": sum(1 for b in bots for e in (b.get("timeline") or []) if e.get("event") == "bornDead"),
        "anchorEvents": [e for b in bots for e in (b.get("timeline") or []) if e.get("event", "").startswith("anchor")][:12],
        "wristOverwrite": [b.get("wristOverwrite") for b in bots if b.get("wristOverwrite")],
        "inventory": next((b.get("inventory") for b in bots if b.get("inventory")), None),
        "decomposition": decompose((server or {}).get("shotPoses"), [p for b in bots for p in (b.get("shotPoses") or [])]),
        "poseDecomposition": decompose_samples((server or {}).get("poseSamples"), [p for b in bots for p in (b.get("poseSamples") or [])]),
        "ntApplied": [{"epoch": b.get("epoch"), "avatarNetId": b.get("avatarNetId"), "nt": b.get("ntApplied")} for b in bots if b.get("ntApplied")],
        "netReceive": client_net_receive(bots, (server or {}).get("netSeries") or {}),
    }


def flags(combo, server, clients):
    result = {}
    seen = [c for c in clients.values() if c["bodies"] > 0]
    result["seenByAllClients"] = len(seen) == len(clients) and len(clients) > 0
    result["noBotComponentsOnClients"] = all(not c["botComponents"] for c in clients.values())
    dup = [d for c in clients.values() for d in c["duplicates"] if d]
    result["noDuplicateGrabs"] = all((d.get("foreignHolderFrames", 0) + d.get("managerMismatchFrames", 0)) == 0 for d in dup)
    # Только клиенты, видевшие case с начала (эпоха 1): опоздавший видит лишь выстрелы после подключения.
    full = [c for c in seen if c["epochs"] == [1]]
    result["clientShotsMatchServer"] = all(c["shotsSeen"] >= (server.get("shots") or 0) for c in full) if server.get("shots") and full else None
    if combo.startswith("E-"):
        result["barrelToRayP95Le1deg"] = all(c["barrelToRayDeg"].get("count", 0) > 0 and c["barrelToRayDeg"]["p95"] <= 1.0 for c in seen)
    if combo.startswith("P-") or combo.startswith("LH-"):
        result["pistolInHolsterAtEnd"] = all(any(f and f.get("anchored") and "PPK" in f["anchored"] for f in c["final"]) for c in seen)
        result["ak105InHandAtEnd"] = all(any(f and f.get("right") and "AK105" in f["right"] for f in c["final"]) for c in seen)
        result["noAnchoredItemFreed"] = all(not any(e.get("event") == "anchoredItemFreed" for e in c["anchorEvents"]) for c in seen)
        result["serverStowDroppedZero"] = (server.get("stowDropped") or 0) == 0
    if combo.startswith(("LJ-", "LH-", "RC-")):
        late = [s for c in clients.values() for s in c["lateSnapshots"]]
        result["lateClientSawWeaponInHand"] = bool(late) and all(
            any(snap.get("right") for snap in (s.get("snapshots") or [])[-1:]) for s in late)
    if combo.startswith("G-"):
        deaths = [w for c in seen for w in c["deathWeapons"]]
        # Замер в момент смерти (0 с) может застать хват до прихода отпускания — критерий по замерам ≥1 с.
        settled = [w for w in deaths if w.get("exists") and (w.get("afterSeconds") or 0) >= 0.9 and w.get("reason") != "bodyDestroyed"]
        result["weaponReleasedAtDeath"] = bool(settled) and all(not w.get("grabbed") for w in settled)
        result["ghostSeen"] = all(c["bornDead"] > 0 for c in seen)
        result["newBodyAfterRespawn"] = all(c["bodies"] >= 3 for c in seen)
    return result


def drift(client):
    """Дрейф правого запястья у клиента: против сервера (сетка поз и выстрелы) и внесённый подстановкой Y."""
    pose = client.get("poseDecomposition") or {}
    shots = client.get("decomposition") or {}
    receive = (client.get("netReceive") or {})
    wrist = (receive.get("byRole") or {}).get("wristR") or {}
    strict = (receive.get("strict") or {}).get("wristR") or {}
    return {
        "wristRvsServerMm": pose.get("wristRMm"), "wristRvsServerDeg": pose.get("wristRDeg"),
        "afterNtWristRvsServerMm": shots.get("afterNtWristRvsServerMm"), "barrelToRayDeg": client.get("barrelToRayDeg"),
        "gapResets": wrist.get("gapResets"), "emptyFills": wrist.get("emptyFills"), "bakedY": wrist.get("bakedY"),
        "bakedSumDeltaYM": wrist.get("sumDeltaYM"), "strictBakedFromTransform": strict.get("bakedFromTransform"),
        "strictSumDeltaYM": strict.get("sumDeltaYM"), "clientNoRecoilInstalls": len(receive.get("clientNoRecoilInstalls") or []),
    }


def ab_compare(runs):
    """Пары <попытка>/<топология>-<фаза>-A1 и -A2: по каждому case и клиенту — дрейф правого запястья."""
    result = {}
    for name in runs:
        if not name.endswith("-A1"):
            continue
        other = name[:-3] + "-A2"
        if other not in runs:
            continue
        pair = {}
        for combo, data in (runs[name].get("combos") or {}).items():
            data2 = (runs[other].get("combos") or {}).get(combo)
            if not data2:
                continue
            pair[combo] = {
                "A1": {p: drift(c) for p, c in data["clients"].items()},
                "A2": {p: drift(c) for p, c in data2["clients"].items()},
                "server": {"A1": (data["server"].get("netSendMarkers") or {}).get("avatars"),
                           "A2": (data2["server"].get("netSendMarkers") or {}).get("avatars")},
            }
        result[name[:-3]] = pair
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default="tasks/bots-fix/reports/net-round1")
    args = parser.parse_args()
    verdict = {"root": args.root, "runs": {}}
    # runtime/<topology>-<phase> (попытка 1) и runtime/<attempt>/<topology>-<phase> (attempt-2 и далее).
    run_roots = sorted({os.path.dirname(p) for p in glob.glob(os.path.join(args.root, "runtime", "**", "launch.json"), recursive=True)})
    for run_root in run_roots:
        name = os.path.relpath(run_root, os.path.join(args.root, "runtime")).replace(os.sep, "/")
        queue_path = os.path.join(run_root, "queue.json")
        if not os.path.exists(queue_path):
            verdict["runs"][name] = {"missing": "queue.json"}
            continue
        queue = load(queue_path)
        reports = {}
        for path in sorted(glob.glob(os.path.join(run_root, "clients", "*.json"))):
            report = load(path)
            reports[report.get("participant") or os.path.basename(path)] = report
        run = {"queue": {"state": queue.get("state"), "error": queue.get("error")},
               "standCommands": [{k: c.get(k) for k in ("action", "combo", "error")} | {"code": (c.get("reply") or {}).get("code")}
                                 for c in queue.get("stand") or []],
               "clients": {p: {"final": r.get("final"), "consoleErrors": (r.get("console") or {}).get("errors"),
                               "relayErrors": (r.get("console") or {}).get("relayErrors"),
                               "errorSamples": ((r.get("console") or {}).get("samples") or [])[:5],
                               "sceneBotComponents": (r.get("botComponentsOnClient") or {}).get("sceneMaxCount"),
                               "botDirectorSeen": (r.get("botComponentsOnClient") or {}).get("botDirectorSeen"),
                               "duplicateWeaponIdsMax": r.get("duplicateWeaponIdsMax"),
                               "connections": r.get("connections"),
                               "inBytesPerSecondTotal": (r.get("network") or {}).get("inBytesPerSecondTotal"),
                               "rttMs": (r.get("network") or {}).get("rttMs"),
                               "bufferTimeMs": (r.get("network") or {}).get("bufferTimeMs"),
                               "clientFrameMs": (r.get("network") or {}).get("clientFrameMs"),
                               "localAvatars": r.get("localAvatars")}
                           for p, r in reports.items()},
               "combos": {}}
        for entry in queue.get("combos") or []:
            combo = entry.get("combo")
            server = server_case(run_root, combo)
            clients = {p: client_case(r, entry, server) for p, r in reports.items()}
            server = {k: v for k, v in server.items() if k not in ("shotPoses", "poseSamples", "netSeries")}
            combo_flags = flags(combo, server, clients)
            at_start = entry.get("clientsAtStart") or []
            if at_start:
                combo_flags["clientsWithoutModeTeam"] = all(c.get("teamIndex") == 0 for c in at_start)
                combo_flags["clientsOffLane1_5m"] = all((c.get("distanceToLaneM") or 0) >= 1.5 for c in at_start if c.get("position"))
            run["combos"][combo] = {"server": server, "clients": clients, "clientsAtStart": at_start,
                                    "botTeamIndex": entry.get("botTeamIndex"), "flags": combo_flags}
        run["flags"] = {
            "clientConsoleErrorsZero": all((c["consoleErrors"] or 0) == 0 for c in run["clients"].values()),
            "relayErrorsZero": all((c["relayErrors"] or 0) == 0 for c in run["clients"].values()),
            "noBotComponentsInClientScenes": all((c["sceneBotComponents"] or 0) == 0 and not c["botDirectorSeen"] for c in run["clients"].values()),
            "noDuplicateWeaponIds": all((c["duplicateWeaponIdsMax"] or 0) == 0 for c in run["clients"].values()),
        }
        verdict["runs"][name] = run
    verdict["abCompare"] = ab_compare(verdict["runs"])
    out = os.path.join(args.root, "verdict.json")
    os.makedirs(args.root, exist_ok=True)
    with open(out, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(verdict, handle, ensure_ascii=False, indent=1)
    for name, run in verdict["runs"].items():
        print(name, json.dumps(run.get("queue"), ensure_ascii=False), json.dumps(run.get("flags"), ensure_ascii=False))
        for combo, data in (run.get("combos") or {}).items():
            failed = [k for k, v in data["flags"].items() if v is False]
            angles = {p: c["barrelToRayDeg"] for p, c in data["clients"].items()}
            print("  ", combo, "server:", data["server"].get("status"), "shots", data["server"].get("shots"),
                  "| clientAngles", json.dumps(angles), "| FAILED" if failed else "| ok", failed)
            for participant, client in data["clients"].items():
                receive = client.get("netReceive") or {}
                if receive.get("byRole"):
                    wrist = receive["byRole"].get("wristR") or {}
                    print("     ", participant, "wristR resets gap/empty", wrist.get("gapResets"), wrist.get("emptyFills"),
                          "bakedY", wrist.get("bakedY"), "sumdY", wrist.get("sumDeltaYM"),
                          "strict", (receive.get("strict") or {}).get("wristR", {}).get("bakedFromTransform"),
                          "noRecoilInstalls", len(receive.get("clientNoRecoilInstalls") or []))
    for pair, combos in verdict["abCompare"].items():
        for combo, data in combos.items():
            for variant in ("A1", "A2"):
                for participant, d in data[variant].items():
                    print("AB", pair, combo, variant, participant, "wristRvsServerMm", json.dumps(d["wristRvsServerMm"]),
                          "bakedSumdY", d["bakedSumDeltaYM"], "strict", d["strictBakedFromTransform"])
    print("verdict:", out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
