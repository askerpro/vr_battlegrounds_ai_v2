"""Офлайн-разбор строгих событий подстановки осей NT у клиентов (net-round1, attempt-7+, после патча Mirror 3).

Запуск:
    python -I tasks/bots-fix/tools/net-round1/strict-events.py [--root tasks/bots-fix/reports/net-round1] [--attempt attempt-7]
Пишет <root>/<attempt>/strict-events.json и печатает таблицу.

Источник — события сброса клиента (bodies[].netReceive.nts[role].resetEvents, где PosY не передан) и серверная серия
отправок той же метки (summary.json → Metrics.netSendMarkers). Для каждого события:
  * oldStrict — прежний строгий маркер (модель до патча: подстановка = трансформ, равный итогу прошлого кадра, и
    отличается от сервера > 1 см). После патча Mirror трансформ не используется — oldStrict без patchedDeviation
    означает артефакт модели, а не позу клиента.
  * patchedDeviation — что подставил бы патченный Mirror: последнее полученное значение. Для reset=gap это последний
    снимок буфера (previousSnapshot — точное значение Mirror); для reset=empty — восстановление модели (approx).
  * время от спавна тела (метка SpawnMessage у клиента), до конца тела (последняя отправка сервера этого netId —
    смерть/смена тела), пауза пакетов перед сбросом (gapMs), сколько отклонение жило до следующего приёма оси Y,
    совпавшего с сервером (≤ 1 см): posYEvents/resetEvents с PosY; если приёмов не видно (журнал обрезан) — граница
    по keyframeInterval.
"""
import argparse
import glob
import json
import os
import sys

POS_Y = 2
THRESHOLD_M = 0.01
WINDOW_S = 1.0


def load(path):
    with open(path, encoding="utf-8-sig") as handle:
        return json.load(handle)


def server_series(run_root, combo):
    path = os.path.join(run_root, combo, "summary.json")
    if not os.path.exists(path):
        return {}, {}
    case = (load(path).get("cases") or [{}])[0]
    markers = (case.get("Metrics") or {}).get("netSendMarkers") or {}
    series, bodies = {}, {}
    for avatar in markers.get("avatars") or []:
        net_id = avatar.get("netId")
        last = None
        for role, nt in (avatar.get("nts") or {}).items():
            rows = nt.get("series") or []
            series[(net_id, role)] = {round(r[0], 4): r for r in rows}
            if rows:
                last = max(last or rows[-1][0], rows[-1][0])
        bodies[net_id] = {"kind": avatar.get("kind"), "lastSend": last,
                          "spawns": [s.get("t") for s in avatar.get("spawns") or []]}
    return series, bodies


def receipts_y(nt):
    """Все видимые приёмы оси Y: (метка, y)."""
    rows = [(e[0], e[2]) for e in nt.get("posYEvents") or [] if e[2] is not None]
    for event in nt.get("resetEvents") or []:
        if (event.get("flags") or 0) & POS_Y and (event.get("sent") or [None] * 3)[1] is not None:
            rows.append((event["stamp"], event["sent"][1]))
    return sorted(set(rows))


def analyse_run(run_root):
    queue = load(os.path.join(run_root, "queue.json"))
    clients = [load(p) for p in sorted(glob.glob(os.path.join(run_root, "clients", "*.json")))]
    out = []
    for entry in queue.get("combos") or []:
        combo = entry.get("combo")
        series, server_bodies = server_series(run_root, combo)
        if not series:
            continue
        for report in clients:
            participant = report.get("participant")
            for body in report.get("bodies") or []:
                net_id = body.get("avatarNetId")
                if body.get("kind") != "bot" or net_id not in server_bodies:
                    continue
                receive = body.get("netReceive") or {}
                spawns = [s.get("stamp") for s in receive.get("spawns") or [] if s.get("stamp") is not None]
                spawns += [t for t in server_bodies[net_id]["spawns"] if t is not None]
                body_end = server_bodies[net_id]["lastSend"]
                for role, nt in (receive.get("nts") or {}).items():
                    rows = series.get((net_id, role)) or {}
                    truncated = len(nt.get("posYEvents") or []) < (nt.get("posY") or 0)
                    seen = receipts_y(nt)
                    for event in nt.get("resetEvents") or []:
                        if (event.get("flags") or 0) & POS_Y:
                            continue
                        server = rows.get(round(event.get("stamp") or -1, 4))
                        fill = event.get("fill") or [None] * 3
                        previous = event.get("previousSnapshot") or [None] * 3
                        if server is None or server[4] is None:
                            continue
                        stamp = event["stamp"]
                        server_y = server[4]
                        old = bool(event.get("fillEqualsPrevFinal")) and fill[1] is not None and abs(fill[1] - server_y) > THRESHOLD_M
                        base = previous[1]
                        patched = None if base is None else base - server_y
                        real = patched is not None and abs(patched) > THRESHOLD_M
                        if not old and not real:
                            continue
                        # Сколько жило отклонение (по патченной модели): до первого приёма Y, совпавшего с сервером.
                        lifetime, closed_by = None, None
                        if real:
                            for t, y in seen:
                                if t <= stamp:
                                    continue
                                other = rows.get(round(t, 4))
                                if other is not None and other[4] is not None and abs(y - other[4]) <= THRESHOLD_M:
                                    lifetime, closed_by = round(t - stamp, 3), t
                                    break
                        since_spawn = min((stamp - s for s in spawns if s <= stamp + 1e-6), default=None)
                        to_end = None if body_end is None else round(body_end - stamp, 3)
                        gap_ms = event.get("gapMs")
                        in_window = since_spawn is not None and since_spawn <= WINDOW_S or (to_end is not None and to_end <= WINDOW_S)
                        out.append({
                            "combo": combo, "participant": participant, "avatarNetId": net_id, "epoch": body.get("epoch"),
                            "role": role, "stamp": stamp, "clientFrame": event.get("frame"), "serverFrame": server[1],
                            "reset": event.get("reset"), "gapMs": gap_ms,
                            "oldStrict": old, "oldFillMinusServerYm": None if fill[1] is None else round(fill[1] - server_y, 4),
                            "patchedBaselineExact": event.get("reset") == "gap",
                            "patchedDeviationYm": None if patched is None else round(patched, 4), "patchedReal": real,
                            "sinceSpawnS": None if since_spawn is None else round(since_spawn, 3), "toBodyEndS": to_end,
                            "lifetimeS": lifetime, "closedByReceiptAt": closed_by,
                            "lifetimeNote": None if not real or lifetime is not None else
                            ("журнал PosY обрезан — граница keyframeInterval" if truncated else "нет совпавшего приёма до конца тела"),
                            "nearSpawnDeathOrLoss": bool(in_window) or gap_ms is not None,
                        })
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default="tasks/bots-fix/reports/net-round1")
    parser.add_argument("--attempt", default="attempt-7")
    args = parser.parse_args()
    runs = sorted(os.path.dirname(p) for p in glob.glob(os.path.join(args.root, "runtime", args.attempt, "*", "queue.json")))
    events = []
    for run_root in runs:
        for row in analyse_run(run_root):
            row["run"] = os.path.basename(run_root)
            events.append(row)
    real = [e for e in events if e["patchedReal"]]
    long_lived = [e for e in real if (e["lifetimeS"] is None or e["lifetimeS"] > WINDOW_S) and
                  not ((e["sinceSpawnS"] is not None and e["sinceSpawnS"] <= WINDOW_S) or (e["toBodyEndS"] is not None and e["toBodyEndS"] <= WINDOW_S))]
    summary = {
        "events": len(events), "oldStrict": sum(1 for e in events if e["oldStrict"]),
        "oldStrictArtifacts": sum(1 for e in events if e["oldStrict"] and not e["patchedReal"]),
        "patchedReal": len(real),
        "patchedRealWithin1sOfSpawnOrBodyEnd": sum(1 for e in real if (e["sinceSpawnS"] is not None and e["sinceSpawnS"] <= WINDOW_S) or
                                                   (e["toBodyEndS"] is not None and e["toBodyEndS"] <= WINDOW_S)),
        "patchedRealLifetimeLe1s": sum(1 for e in real if e["lifetimeS"] is not None and e["lifetimeS"] <= WINDOW_S),
        "longLivedWithoutBodyChange": [{k: e[k] for k in ("run", "combo", "participant", "avatarNetId", "role", "stamp", "clientFrame",
                                                         "serverFrame", "patchedDeviationYm", "lifetimeS", "lifetimeNote")} for e in long_lived],
    }
    out_dir = os.path.join(args.root, args.attempt)
    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, "strict-events.json"), "w", encoding="utf-8", newline="\n") as handle:
        json.dump({"summary": summary, "events": events}, handle, ensure_ascii=False, indent=1)
    print(json.dumps(summary, ensure_ascii=False))
    print("run | combo | participant | netId | role | stamp | cFrame | sFrame | reset | gapMs | old dY m | patched dY m | sinceSpawn s | toEnd s | life s")
    for e in events:
        print(" | ".join(str(x) for x in (e["run"], e["combo"], e["participant"], e["avatarNetId"], e["role"], e["stamp"], e["clientFrame"],
                                          e["serverFrame"], e["reset"], e["gapMs"], e["oldFillMinusServerYm"], e["patchedDeviationYm"],
                                          e["sinceSpawnS"], e["toBodyEndS"], e["lifetimeS"] if e["lifetimeS"] is not None else e["lifetimeNote"])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
