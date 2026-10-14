"""Разбор прогонов cc0-match (clip-cutover cc-0: база матча гибрида) -> reports/cc0-match/verdict.json и table.md.

Вход: tasks/bots-fix/reports/cc0-match/runtime/<attempt>/<run>/{launch.json,watch.json,probe/*.json}
(отчёты BotStandMatchProbe, схема bot-stand-match-probe@1). Только чтение входа; пишет verdict.json и table.md.
Запуск: python -I tasks/bots-fix/tools/cc0-match/analyze.py [--attempt attempt-1]
"""
import argparse
import json
import os
import sys
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
REPORTS = os.path.normpath(os.path.join(HERE, '..', '..', 'reports', 'cc0-match'))
RUN_ORDER = ['M1', 'M2', 'M3', 'M8', 'M10', 'S8', 'S8P']
BOT_MARKERS = ['BotDirector.Think', 'BotRuntimeController.Update', 'BotRuntimeController.LateUpdate', 'BotAvatarDriver.Apply',
               'BotHands.Tick', 'BotWeaponAim.Solve', 'BotGunner.Sees']


def load(path):
    with open(path, encoding='utf-8-sig') as handle:
        return json.load(handle)


def stat(block, key):
    return (block or {}).get(key)


def phases_of(timeline):
    """Смены фаз: [(t, round, phase)] и длительности фаз по раундам."""
    events = [e for e in timeline if e.get('kind') == 'phase']
    seq = [(e['t'], e.get('round'), e.get('phase')) for e in events]
    durations = defaultdict(list)
    for (t0, r0, p0), (t1, _, _) in zip(seq, seq[1:]):
        durations[p0].append(round(t1 - t0, 2))
    return seq, {k: {'n': len(v), 'mean': round(sum(v) / len(v), 2), 'max': max(v)} for k, v in durations.items()}


def economy_check(timeline, catalog):
    """Закупки против экономики: цена = каталог, delta = -цена, money - delta = предыдущий баланс игрока."""
    prices = {w['name']: w for w in catalog}
    balance = {}
    purchases, problems = [], []
    reasons = Counter()
    for e in timeline:
        kind = e.get('kind')
        if kind == 'balance':
            balance[e['player']] = e['money']
            continue
        if kind != 'money':
            continue
        reasons[e['reason']] += 1
        player, delta, money = e['player'], e['delta'], e['money']
        before = money - delta
        known = balance.get(player)
        if e['reason'] == 'Purchase':
            item = prices.get(e.get('detail'))
            price = item['price'] if item else e.get('price', -1)
            row = {'t': e['t'], 'player': player, 'round': e.get('round'), 'phase': e.get('phase'), 'weapon': e.get('detail'),
                   'category': item['category'] if item else e.get('category'), 'price': price, 'before': before, 'after': money}
            purchases.append(row)
            if price < 0 or delta != -price:
                problems.append(dict(row, problem='delta != -price'))
            if money < 0:
                problems.append(dict(row, problem='отрицательный баланс'))
            if known is not None and known != before and known != money:
                problems.append(dict(row, problem='баланс до покупки %s != известного %s' % (before, known)))
        balance[player] = money
    by_round = defaultdict(list)
    for p in purchases:
        by_round[p['round']].append('%s:%s' % (p['category'], p['weapon']))
    return {'purchases': len(purchases), 'problems': problems[:20], 'problemCount': len(problems), 'reasons': dict(reasons),
            'byRound': {str(k): dict(Counter(v)) for k, v in sorted(by_round.items(), key=lambda kv: (kv[0] is None, kv[0]))}}


def deaths(timeline):
    kills = [e for e in timeline if e.get('kind') == 'kill']
    bodies = [e for e in timeline if e.get('kind') == 'body']
    ghosts = [b for b in bodies if b.get('ghost')]
    killed = set()
    respawned = 0
    for e in sorted(kills + bodies, key=lambda x: x['t']):
        if e['kind'] == 'kill':
            killed.add(e.get('victim'))
        elif not e.get('ghost') and e.get('session') in killed:
            respawned += 1
            killed.discard(e.get('session'))
    return {'kills': len(kills), 'botVictims': sum(1 for k in kills if k.get('victimBot')),
            'botKillers': sum(1 for k in kills if k.get('killerBot')), 'ghostBodies': len(ghosts),
            'bodies': len(bodies), 'liveBodiesAfterDeath': respawned}


def body_churn(timeline, bots):
    """Спавны тел по фазам: сколько тел (живых/призраков) на бота в каждой фазе/раунде — пересоздание на смене раунда."""
    by = defaultdict(lambda: [0, 0])
    for e in timeline:
        if e.get('kind') != 'body' or not e.get('bot'):
            continue
        key = '%s/%s' % (e.get('round'), e.get('phase'))
        by[key][1 if e.get('ghost') else 0] += 1
    rows = {k: {'live': v[0], 'ghost': v[1], 'perBot': round((v[0] + v[1]) / max(1, bots), 2)} for k, v in by.items()}
    transitions = [k for k in rows if k.endswith('/Resolution') or k.endswith('/Scoreboard') or k.endswith('/Setup')]
    return {'byRoundPhase': rows, 'roundTransitionKeys': sorted(transitions)}


def humans_in_rosters(timeline):
    bad = set()
    for e in timeline:
        if e.get('kind') in ('phase', 'mode'):
            for s in e.get('sessions') or []:
                if not s.get('bot') and s.get('inModeTeam'):
                    bad.add(s.get('name'))
    return sorted(bad)


def frame_block(frame, label):
    block = ((frame or {}).get('byLabel') or {}).get(label)
    if not block:
        return None
    fm, gc = block.get('frameMs') or {}, block.get('gcBytesPerFrame') or {}
    markers = block.get('markers') or {}
    return {'frames': block.get('frames'), 'p50': fm.get('p50'), 'p95': fm.get('p95'), 'max': fm.get('max'), 'mean': fm.get('mean'),
            'gcMeanB': gc.get('mean'), 'gcP95B': gc.get('p95'), 'gcCollections': block.get('gcCollections'),
            'markersMeanMs': {m: markers[m]['meanMs'] for m in BOT_MARKERS if m in markers}}


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
    return {'seconds': seconds, 'meanConnections': conns, 'botBytes': bot_total, 'humanBytes': human_total,
            'relayBytes': (cats.get('relay') or {}).get('bytes', 0), 'otherBytes': (cats.get('other') or {}).get('bytes', 0),
            'botBytesPerSecPerBotPerConn': round(bot_total / seconds / max(1, bots) / max(1.0, conns), 1) if seconds else None,
            'liveBotBytes': live.get('botAvatar', 0) + live.get('botSession', 0)}


def client_bytes(report):
    net = report.get('clientNetwork') or {}
    seconds = net.get('connectedSeconds') or 0
    own_avatar, own_session = net.get('ownAvatarNetId'), net.get('ownSessionNetId')
    per = defaultdict(int)
    ids = defaultdict(set)
    for row in net.get('byNetId') or []:
        if row['netId'] in (own_avatar, own_session):
            per['own'] += row['bytes']
            continue
        cat = row['category']
        per[cat] += row['bytes']
        ids[cat].add(row['netId'])
    bot_ids = len(ids['botSession']) or len(set(net.get('botAvatarIds') or []))
    remote_humans = len(ids['humanSession']) or len(ids['humanAvatar'])
    bot_bytes = per['botAvatar'] + per['botSession']
    human_bytes = per['humanAvatar'] + per['humanSession']
    return {'participant': report.get('participant'), 'seconds': seconds, 'ownRole': net.get('ownRole'), 'ownTeam': net.get('ownTeam'),
            'botSessions': bot_ids, 'remoteHumans': remote_humans,
            'bytesPerSecPerBot': round(bot_bytes / seconds / bot_ids, 1) if seconds and bot_ids else None,
            'bytesPerSecPerRemoteHuman': round(human_bytes / seconds / remote_humans, 1) if seconds and remote_humans else None,
            'bytesPerSecTotal': round((net.get('inBytesTotal') or 0) / seconds, 1) if seconds else None,
            'ownBytes': per['own']}


def analyze_run(run_dir, run):
    result = {'run': run}
    for name in ('launch.json', 'watch.json', 'stop.json'):
        path = os.path.join(run_dir, name)
        if os.path.exists(path):
            result[name[:-5]] = load(path)
    probe_dir = os.path.join(run_dir, 'probe')
    reports = [load(os.path.join(probe_dir, f)) for f in sorted(os.listdir(probe_dir)) if f.endswith('.json')] if os.path.isdir(probe_dir) else []
    server = next((r for r in reports if r.get('match')), None)
    clients = [r for r in reports if r is not server and r.get('clientNetwork')]
    watch = result.get('watch') or {}
    result['watchSummary'] = {k: watch.get(k) for k in ('state', 'stopReason', 'maxRound', 'liveSeconds', 'goLiveCalled', 'goLiveResult')}
    result['console'] = {r.get('participant'): {k: (r.get('console') or {}).get(k) for k in ('errors', 'exceptions', 'asserts', 'relayErrors', 'relayWarnings')}
                         for r in reports}
    result['consoleSamples'] = {r.get('participant'): (r.get('console') or {}).get('samples', [])[:6] for r in reports}
    result['finals'] = {r.get('participant'): r.get('final') for r in reports}
    if server is None:
        result['error'] = 'нет отчёта сервера'
        return result
    match = server['match']
    timeline = match.get('timeline') or []
    seq, durations = phases_of(timeline)
    bots = match.get('bots') or 0
    result['bots'] = bots
    result['match'] = {
        'phaseChanges': len(seq), 'rounds': max([r for _, r, _ in seq if r is not None] or [-1]),
        'roundsScored': sum(1 for e in timeline if e.get('kind') == 'roundScored'),
        'roundWinners': [e.get('winner') for e in timeline if e.get('kind') == 'roundScored'],
        'matchFinished': bool(match.get('matchEnd')), 'matchEnd': match.get('matchEnd'), 'scores': match.get('scores'),
        'phaseDurations': durations, 'firstPhases': [p for _, _, p in seq[:8]],
        'modeEvents': [e for e in timeline if e.get('kind') == 'mode'][:2],
        'humansInModeTeams': humans_in_rosters(timeline),
        'mapLoads': [(e['kind'], e.get('scene')) for e in timeline if e.get('kind', '').startswith('mapLoad')],
        'timelineDropped': match.get('timelineDropped')}
    for mode in result['match']['modeEvents']:
        mode.pop('sessions', None)
    result['economy'] = economy_check(timeline, match.get('weaponCatalog') or [])
    result['fire'] = {k: match.get(k) for k in ('shotsBots', 'shotsHumans', 'shotsUnknown', 'hitsByBots', 'hitsOnBots', 'kills', 'firearmsTracked')}
    result['deaths'] = deaths(timeline)
    result['bodyChurn'] = body_churn(timeline, bots)
    frame = server.get('frame') or {}
    result['frame'] = {label: frame_block(frame, label) for label in ('Combat', 'Equipment', 'Countdown', 'Setup', 'Scoreboard', 'Warmup')}
    result['frame'] = {k: v for k, v in result['frame'].items() if v}
    result['gcCollectionsTotal'] = sum((b or {}).get('gcCollections') or 0 for b in (frame.get('byLabel') or {}).values())
    result['markerValid'] = frame.get('markerValid')
    result['serverNetwork'] = server_bytes(server.get('serverNetwork'), bots, ['Setup', 'Equipment', 'Countdown', 'Combat', 'Resolution', 'Scoreboard'])
    result['clients'] = [client_bytes(c) for c in clients]
    return result


def row(r):
    combat = (r.get('frame') or {}).get('Combat') or {}
    m = r.get('match') or {}
    e = r.get('economy') or {}
    d = r.get('deaths') or {}
    f = r.get('fire') or {}
    errs = ', '.join('%s:%s/%s/%s' % (p, c.get('errors'), c.get('exceptions'), c.get('relayErrors')) for p, c in (r.get('console') or {}).items())
    cl = r.get('clients') or []
    per_bot = ', '.join('%s %s' % (c['participant'], c['bytesPerSecPerBot']) for c in cl) or '-'
    per_human = ', '.join('%s %s' % (c['participant'], c['bytesPerSecPerRemoteHuman']) for c in cl) or '-'
    net = r.get('serverNetwork') or {}
    return ('| {run} | {bots} | {rounds}/{scored} {fin} {scores} | {purch} (ошибок {probs}) | {sb}/{hb} | {kills}/{ghosts}/{resp} '
            '| {p50}/{p95}/{mx} | {gc}/{gcc} | {sbot} | {pb} | {ph} | {errs} | {stop} |').format(
        run=r['run'], bots=r.get('bots'), rounds=m.get('rounds'), scored=m.get('roundsScored'),
        fin='конец' if m.get('matchFinished') else 'стоп', scores=json.dumps(m.get('scores'), ensure_ascii=False),
        purch=e.get('purchases'), probs=e.get('problemCount'), sb=f.get('shotsBots'), hb=f.get('hitsByBots'),
        kills=d.get('kills'), ghosts=d.get('ghostBodies'), resp=d.get('liveBodiesAfterDeath'),
        p50=combat.get('p50'), p95=combat.get('p95'), mx=combat.get('max'), gc=combat.get('gcMeanB'), gcc=r.get('gcCollectionsTotal'),
        sbot=net.get('botBytesPerSecPerBotPerConn'), pb=per_bot, ph=per_human, errs=errs,
        stop=(r.get('watchSummary') or {}).get('stopReason'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--attempt', action='append', help='попытка; несколько — прогон берётся из последней, где он есть')
    args = parser.parse_args()
    attempts = args.attempt or ['attempt-1']
    found = {}
    for attempt in attempts:
        root = os.path.join(REPORTS, 'runtime', attempt)
        for r in RUN_ORDER:
            if os.path.isdir(os.path.join(root, r)):
                found[r] = (attempt, os.path.join(root, r))
    if not found:
        print('нет прогонов в', attempts)
        return 2
    results = []
    for r in RUN_ORDER:
        if r in found:
            item = analyze_run(found[r][1], r)
            item['attempt'] = found[r][0]
            results.append(item)
    verdict = {'attempts': attempts, 'runs': results}
    with open(os.path.join(REPORTS, 'verdict.json'), 'w', encoding='utf-8', newline='\n') as handle:
        json.dump(verdict, handle, ensure_ascii=False, indent=1)
    header = ('| Прогон | N | раунды/засчитано, конец, счёт | закупки | выстрелы/попадания ботов | смерти/призраки/возрождения '
              '| Combat кадр p50/p95/max мс | GC Combat Б/кадр / сборок | сервер Б/с на бота·получателя | клиент Б/с на бота '
              '| клиент Б/с на удалённого человека | ошибки err/exc/relay | стоп |')
    lines = [header, '|' + '---|' * 13] + [row(r) for r in results]
    table = '\n'.join(lines)
    with open(os.path.join(REPORTS, 'table.md'), 'w', encoding='utf-8', newline='\n') as handle:
        handle.write(table + '\n')
    print(table)
    print('verdict:', os.path.join(REPORTS, 'verdict.json'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
