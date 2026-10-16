"""Офлайн-разбор прогонов cc3-perf (clip-cutover cc-3: H8/C8, H10/C10, RH, S8) -> reports/cc3-perf/verdict.json и table.md.

Вход: tasks/bots-fix/reports/cc3-perf/runtime/<attempt>/<cell>/{launch.json,watch.json,stop.json,probe/*.json}
(отчёты pinned BotStandMatchProbe 2fa11b27, схема bot-stand-match-probe@1). Только чтение входа; пишет verdict.json и table.md.
Запуск: python -I tasks/bots-fix/tools/cc3-perf/analyze.py [--attempt attempt-1]
"""
import argparse
import json
import os
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
REPORTS = os.path.normpath(os.path.join(HERE, '..', '..', 'reports', 'cc3-perf'))
CELL_ORDER = ['H8', 'C8', 'H10', 'C10', 'RH', 'S8']
BOT_MARKERS = [
    'BotDirector.Think',
    'BotRuntimeController.Update',
    'BotRuntimeController.LateUpdate',
    'BotAvatarDriver.Apply',
    'BotHands.Tick',
    'BotWeaponAim.Solve',
    'BotGunner.Sees',
]


def load(path):
    with open(path, encoding='utf-8-sig') as handle:
        return json.load(handle)


def frame_block(frame, label, windows, gc_valid, marker_valid_map):
    block = ((frame or {}).get('byLabel') or {}).get(label)
    if not block:
        return None
    fm = block.get('frameMs') or {}
    gc = block.get('gcBytesPerFrame') or {}
    frames = block.get('frames') or 0
    label_windows = [w for w in (windows or []) if w.get('label') == label]
    duration_sec = round(sum((w.get('seconds') or 0.0) for w in label_windows), 3)
    gc_total = block.get('gcBytesTotal') if gc_valid else 'UNKNOWN'
    gc_collections = block.get('gcCollections')
    gc_bytes_per_sec = round(gc_total / duration_sec, 1) if (gc_valid and isinstance(gc_total, (int, float)) and duration_sec > 0) else 'UNKNOWN'
    gc_collections_per_sec = round(gc_collections / duration_sec, 4) if (isinstance(gc_collections, (int, float)) and duration_sec > 0) else 'UNKNOWN'
    markers_raw = block.get('markers') or {}
    markers_out = {}
    for m in BOT_MARKERS:
        if not (marker_valid_map or {}).get(m, False):
            markers_out[m] = 'UNKNOWN'
        elif m in markers_raw:
            markers_out[m] = {
                'meanMs': markers_raw[m].get('meanMs'),
                'maxMs': markers_raw[m].get('maxMs'),
            }
        else:
            markers_out[m] = 'UNKNOWN'
    return {
        'frames': frames,
        'durationSeconds': duration_sec,
        'p50': fm.get('p50'),
        'p95': fm.get('p95'),
        'max': fm.get('max'),
        'mean': fm.get('mean'),
        'gcRecorderValid': bool(gc_valid),
        'gcMeanBytesPerFrame': gc.get('mean') if gc_valid else 'UNKNOWN',
        'gcP95BytesPerFrame': gc.get('p95') if gc_valid else 'UNKNOWN',
        'gcBytesTotal': gc_total,
        'gcBytesPerSecond': gc_bytes_per_sec,
        'gcGen0Collections': gc_collections,
        'gcGen0CollectionsPerSecond': gc_collections_per_sec,
        'gcPauseDurationMs': 'UNKNOWN',
        'gcAllGenerations': 'UNKNOWN',
        'markers': markers_out,
    }


def server_bytes(net, bots, live_labels):
    if not net:
        return None
    by_label = net.get('byLabel') or {}
    live = defaultdict(int)
    for label in live_labels:
        for cat, value in (by_label.get(label) or {}).items():
            live[cat] += value
    seconds = net.get('serverSeconds') or 0
    cats = net.get('byCategory') or {}
    bot_total = (cats.get('botAvatar') or {}).get('bytes', 0) + (cats.get('botSession') or {}).get('bytes', 0)
    human_total = (cats.get('humanAvatar') or {}).get('bytes', 0) + (cats.get('humanSession') or {}).get('bytes', 0)
    conns = net.get('meanConnections') or 0
    return {
        'seconds': seconds,
        'meanConnections': conns,
        'botBytes': bot_total,
        'humanBytes': human_total,
        'relayBytes': (cats.get('relay') or {}).get('bytes', 0),
        'otherBytes': (cats.get('other') or {}).get('bytes', 0),
        'botBytesPerSecPerBotPerConn': round(bot_total / seconds / max(1, bots) / max(1.0, conns), 1) if seconds else None,
        'liveBotBytes': live.get('botAvatar', 0) + live.get('botSession', 0),
    }


def client_bytes(report):
    net = report.get('clientNetwork') or {}
    seconds = net.get('connectedSeconds') or 0
    own_avatar, own_session = net.get('ownAvatarNetId'), net.get('ownSessionNetId')
    per = defaultdict(int)
    ids = defaultdict(set)
    for row in net.get('byNetId') or []:
        if row.get('netId') in (own_avatar, own_session):
            per['own'] += row.get('bytes', 0)
            continue
        cat = row.get('category')
        per[cat] += row.get('bytes', 0)
        ids[cat].add(row.get('netId'))
    bot_ids = len(ids['botSession']) or len(set(net.get('botAvatarIds') or []))
    remote_humans = len(ids['humanSession']) or len(ids['humanAvatar'])
    bot_bytes = per['botAvatar'] + per['botSession']
    human_bytes = per['humanAvatar'] + per['humanSession']
    return {
        'participant': report.get('participant'),
        'seconds': seconds,
        'ownRole': net.get('ownRole'),
        'ownTeam': net.get('ownTeam'),
        'ownAvatarNetId': own_avatar,
        'ownSessionNetId': own_session,
        'botSessions': bot_ids,
        'remoteHumans': remote_humans,
        'bytesPerSecPerBot': round(bot_bytes / seconds / bot_ids, 1) if seconds and bot_ids else None,
        'bytesPerSecPerRemoteHuman': round(human_bytes / seconds / remote_humans, 1) if seconds and remote_humans else None,
        'bytesPerSecTotal': round((net.get('inBytesTotal') or 0) / seconds, 1) if seconds else None,
        'ownBytesExcluded': per['own'],
    }


def analyze_cell(cell_dir, cell):
    result = {'cell': cell, 'gateIssues': []}
    for name in ('launch.json', 'watch.json', 'stop.json'):
        path = os.path.join(cell_dir, name)
        if os.path.exists(path):
            result[name[:-5]] = load(path)
    probe_dir = os.path.join(cell_dir, 'probe')
    reports = [load(os.path.join(probe_dir, f)) for f in sorted(os.listdir(probe_dir)) if f.endswith('.json')] if os.path.isdir(probe_dir) else []
    server = next((r for r in reports if r.get('match')), None)
    clients = [r for r in reports if r is not server and r.get('clientNetwork')]
    watch = result.get('watch') or {}
    result['watchSummary'] = {
        k: watch.get(k)
        for k in ('state', 'completed', 'finishReason', 'completionKind', 'maxRound', 'scoredRounds', 'fourRoundsScored', 'naturalEndProof')
    }
    result['console'] = {
        r.get('participant'): {k: (r.get('console') or {}).get(k) for k in ('errors', 'exceptions', 'asserts', 'relayErrors', 'relayWarnings')}
        for r in reports
    }
    result['finals'] = {r.get('participant'): r.get('final') for r in reports}
    for r in reports:
        part = r.get('participant')
        if r.get('final') is not True:
            result['gateIssues'].append(f'{part}: final != true')
        c = r.get('console') or {}
        if any((c.get(k) or 0) != 0 for k in ('errors', 'exceptions', 'asserts', 'relayErrors')):
            result['gateIssues'].append(f'{part}: console errors/exceptions/asserts/relayErrors != 0')
    if server is None:
        result['gateIssues'].append('нет отчёта сервера')
        result['passedGates'] = False
        return result
    match = server['match']
    timeline = match.get('timeline') or []
    if (match.get('timelineDropped') or 0) != 0:
        result['gateIssues'].append('server: timelineDropped != 0')
    scored = sorted({e.get('round') for e in timeline if e.get('kind') == 'roundScored' and e.get('round') is not None})
    match_finished = bool(match.get('matchEnd'))
    if not (all(r in scored for r in (1, 2, 3, 4)) or (match_finished and len(scored) >= 4)):
        result['gateIssues'].append(f'недостаточно завершённых раундов: scored={scored}')
    frame = server.get('frame') or {}
    gc_valid = bool(frame.get('gcRecorderValid'))
    if not gc_valid:
        result['gateIssues'].append('server: gcRecorderValid != true')
    marker_valid = frame.get('markerValid') or {}
    windows = frame.get('windows') or []
    bots = match.get('bots') or 0
    result['bots'] = bots
    result['match'] = {
        'roundsScored': len(scored),
        'scoredRoundNumbers': scored,
        'matchFinished': match_finished,
        'matchEnd': match.get('matchEnd'),
        'scores': match.get('scores'),
        'timelineDropped': match.get('timelineDropped'),
        'shotsBots': match.get('shotsBots'),
        'hitsByBots': match.get('hitsByBots'),
        'kills': match.get('kills'),
    }
    result['frame'] = {
        label: frame_block(frame, label, windows, gc_valid, marker_valid)
        for label in ('Combat', 'Equipment', 'Countdown', 'Setup', 'Scoreboard', 'Warmup')
    }
    result['frame'] = {k: v for k, v in result['frame'].items() if v}
    result['gcRecorderValid'] = gc_valid
    result['markerValid'] = marker_valid
    result['serverNetwork'] = server_bytes(
        server.get('serverNetwork'), bots, ['Setup', 'Equipment', 'Countdown', 'Combat', 'Resolution', 'Scoreboard']
    )
    result['clients'] = [client_bytes(c) for c in clients]
    result['passedGates'] = len(result['gateIssues']) == 0
    return result


def compare_pairs(by_cell):
    comparisons = {}
    if 'H8' in by_cell and 'C8' in by_cell:
        h8_c = ((by_cell['H8'].get('frame') or {}).get('Combat') or {})
        c8_c = ((by_cell['C8'].get('frame') or {}).get('Combat') or {})
        h8_p95, c8_p95 = h8_c.get('p95'), c8_c.get('p95')
        comparisons['N8_C8_vs_H8'] = {
            'h8CombatP95Ms': h8_p95,
            'c8CombatP95Ms': c8_p95,
            'c8NotWorseThanH8': (c8_p95 <= h8_p95) if (isinstance(h8_p95, (int, float)) and isinstance(c8_p95, (int, float))) else None,
            'h8GcMeanBytesPerFrame': h8_c.get('gcMeanBytesPerFrame'),
            'c8GcMeanBytesPerFrame': c8_c.get('gcMeanBytesPerFrame'),
            'h8GcBytesPerSecond': h8_c.get('gcBytesPerSecond'),
            'c8GcBytesPerSecond': c8_c.get('gcBytesPerSecond'),
            'h8GcGen0Collections': h8_c.get('gcGen0Collections'),
            'c8GcGen0Collections': c8_c.get('gcGen0Collections'),
        }
    if 'H10' in by_cell and 'C10' in by_cell:
        h10_c = ((by_cell['H10'].get('frame') or {}).get('Combat') or {})
        c10_c = ((by_cell['C10'].get('frame') or {}).get('Combat') or {})
        comparisons['N10_C10_vs_H10'] = {
            'h10CombatP95Ms': h10_c.get('p95'),
            'c10CombatP95Ms': c10_c.get('p95'),
            'h10GcMeanBytesPerFrame': h10_c.get('gcMeanBytesPerFrame'),
            'c10GcMeanBytesPerFrame': c10_c.get('gcMeanBytesPerFrame'),
            'h10GcBytesPerSecond': h10_c.get('gcBytesPerSecond'),
            'c10GcBytesPerSecond': c10_c.get('gcBytesPerSecond'),
            'h10GcGen0Collections': h10_c.get('gcGen0Collections'),
            'c10GcGen0Collections': c10_c.get('gcGen0Collections'),
        }
    return comparisons


def row(r):
    combat = (r.get('frame') or {}).get('Combat') or {}
    m = r.get('match') or {}
    net = r.get('serverNetwork') or {}
    cl = r.get('clients') or []
    per_bot = ', '.join(f"{c['participant']} {c['bytesPerSecPerBot']}" for c in cl) or '-'
    per_human = ', '.join(f"{c['participant']} {c['bytesPerSecPerRemoteHuman']}" for c in cl) or '-'
    gate = 'PASS' if r.get('passedGates') else ('FAIL: ' + '; '.join(r.get('gateIssues') or []))
    return (
        '| {cell} | {bots} | {scored} ({kind}) | {p50}/{p95}/{mx} | {dur}/{frames} | {gc_f} / {gc_s} | {gen0} ({gen0_s}/s) '
        '| {sbot} | {pb} | {ph} | {gate} |'
    ).format(
        cell=r['cell'],
        bots=r.get('bots'),
        scored=m.get('roundsScored'),
        kind=(r.get('watchSummary') or {}).get('completionKind') or '-',
        p50=combat.get('p50'),
        p95=combat.get('p95'),
        mx=combat.get('max'),
        dur=combat.get('durationSeconds'),
        frames=combat.get('frames'),
        gc_f=combat.get('gcMeanBytesPerFrame'),
        gc_s=combat.get('gcBytesPerSecond'),
        gen0=combat.get('gcGen0Collections'),
        gen0_s=combat.get('gcGen0CollectionsPerSecond'),
        sbot=net.get('botBytesPerSecPerBotPerConn'),
        pb=per_bot,
        ph=per_human,
        gate=gate,
    )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--attempt', action='append', help='попытка; несколько — ячейка берётся из последней, где она есть')
    args = parser.parse_args()
    attempts = args.attempt or ['attempt-1']
    found = {}
    for attempt in attempts:
        root = os.path.join(REPORTS, 'runtime', attempt)
        for cell in CELL_ORDER:
            if os.path.isdir(os.path.join(root, cell)):
                found[cell] = (attempt, os.path.join(root, cell))
    if not found:
        print('нет прогонов cc3-perf в', attempts)
        return 2
    results = []
    by_cell = {}
    for cell in CELL_ORDER:
        if cell in found:
            item = analyze_cell(found[cell][1], cell)
            item['attempt'] = found[cell][0]
            results.append(item)
            by_cell[cell] = item
    verdict = {
        'schema': 'cc3-perf-verdict@1',
        'attempts': attempts,
        'cells': results,
        'comparisons': compare_pairs(by_cell),
    }
    os.makedirs(REPORTS, exist_ok=True)
    with open(os.path.join(REPORTS, 'verdict.json'), 'w', encoding='utf-8', newline='\n') as handle:
        json.dump(verdict, handle, ensure_ascii=False, indent=2)
    header = (
        '| Ячейка | Ботов | Засчитано раундов | Combat кадр p50/p95/max мс | Combat с/кадров '
        '| GC Combat Б/кадр / Б/с | GC gen0 сборок | Сервер Б/с на бота·получ. | Клиент Б/с на бота '
        '| Клиент Б/с на удал. человека | Гейты |'
    )
    lines = [header, '|' + '---|' * 11] + [row(r) for r in results]
    table = '\n'.join(lines)
    with open(os.path.join(REPORTS, 'table.md'), 'w', encoding='utf-8', newline='\n') as handle:
        handle.write(table + '\n')
    print(table)
    print('verdict:', os.path.join(REPORTS, 'verdict.json'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
