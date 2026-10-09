"""Долговечные снимки журналов Editor/MPE без очистки консоли и повторов действий."""
import json
from datetime import datetime
from pathlib import Path
import re
import subprocess
import time


EDITOR_IDENTITY = ('new { ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id, '
                   'LogPath = UnityEngine.Application.consoleLogPath, '
                   'ProcessCreatedUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("o") }')
ERROR = re.compile(r'\b(?:[A-Za-z]*Exception|Error|Assertion failed|Assert failed|Fatal)\b', re.I)
# Исключение относится только к явным ссылкам на ресурсы освещения.
LIGHTING = re.compile(r'(?:LightingData\.asset|Lightmap[^\s]*\.(?:exr|png|asset)|LightingSettings\.asset)', re.I)


class ProbeLogs:
    def __init__(self, output, report, editor_root):
        self.directory = Path(output).with_name(Path(output).stem + '-logs')
        self.directory.mkdir(parents=True, exist_ok=True)
        self.report = report
        self.root = str(Path(editor_root).resolve())
        self.sources = {}
        self.peers = {}
        self.owner = report.get('owner')
        self.owned_runs = set()
        self.excluded_peers = {}
        self.last_poll = 0
        self.index = 0
        self.started = time.time()
        report['logs'] = {'directory': str(self.directory), 'snapshots': [], 'captureErrors': [],
                          'sourceLifetimes': [], 'ownershipEvents': [], 'integrityUncertainties': [],
                          'ownedRunIds': [], 'excludedHistoricalParticipants': [],
                          'probeStartedUnixSeconds': self.started,
                          'expectedParticipants': [], 'coverageComplete': False,
                          'errorFreeEstablished': False,
                          'reviewPolicy': 'Error candidates require review; only explicit lighting asset references are excluded.'}

    def identify_main(self, identity):
        pid = int(identity['ProcessId'])
        self.sources[pid] = {'ProcessId': pid, 'LogPath': identity.get('LogPath'),
                             'kind': 'worker-main', 'pathEvidence': 'Application.consoleLogPath',
                             'processCreated': identity.get('ProcessCreatedUtc')}

    def observe(self, state):
        if not isinstance(state, dict):
            return
        run_id = state.get('RunId')
        if self.owner and state.get('Owner') == self.owner and run_id:
            self.owned_runs.add(run_id)
            self.report['logs']['ownedRunIds'] = sorted(self.owned_runs)
        for peer in state.get('Participants', []) or []:
            pid = peer.get('ProcessId')
            if pid:
                if run_id not in self.owned_runs or peer.get('RunId') != run_id:
                    key = (run_id, peer.get('RunId'), peer.get('ParticipantId'), pid)
                    self.excluded_peers[key] = {'statusRunId': run_id, 'participantRunId': peer.get('RunId'),
                        'ParticipantId': peer.get('ParticipantId'), 'ProcessId': pid,
                        'reason': 'RunId was not acquired by this probe owner or heartbeat belongs to a different run'}
                    continue
                key = (state.get('RunId'), peer.get('ParticipantId'), peer.get('ProcessSessionId'), pid)
                self.peers[key] = dict(peer)
                self.sources.setdefault(pid, {'ProcessId': pid, 'kind': 'worker-participant'})
        self.report['logs']['expectedParticipants'] = list(self.peers.values())
        self.report['logs']['excludedHistoricalParticipants'] = list(self.excluded_peers.values())

    def _error(self, phase, message, **fields):
        self.report['logs']['captureErrors'].append({'phase': phase, 'time': time.time(),
                                                     'error': message, **fields})

    def _discover(self, phase):
        # CIM читает процессы; команда не управляет Editor и не печатает журнал.
        command = ("$ErrorActionPreference='Stop'; @(Get-CimInstance Win32_Process "
                   "-Filter \"Name = 'Unity.exe'\" | Select-Object ProcessId,CommandLine,"
                   "@{Name='ProcessCreatedUtc';Expression={$_.CreationDate.ToUniversalTime().ToString('o')}}) "
                   "| ConvertTo-Json -Compress")
        try:
            completed = subprocess.run(['powershell', '-NoProfile', '-Command', command],
                                       capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=15)
            if completed.returncode:
                raise RuntimeError(completed.stderr[:1200])
            records = json.loads(completed.stdout or '[]')
            if isinstance(records, dict):
                records = [records]
            for source in self.sources.values():
                source['liveAtDiscovery'] = False
            instances = {}
            for instance in Path(self.root).glob('Library/VP/*/Library/EditorInstance.json'):
                try:
                    identity = json.loads(instance.read_text(encoding='utf-8-sig'))
                    instances.setdefault(int(identity['process_id']), []).append(instance)
                except (OSError, ValueError, KeyError):
                    # Повреждённые/устаревшие каталоги сами по себе не доказывают наличие участника.
                    continue
            for record in records:
                pid = int(record['ProcessId'])
                args = record.get('CommandLine') or ''
                project = self._argument(args, 'projectPath')
                project = self._path(project) if project else None
                worker_match = project == self._path(self.root)
                clone = next((p for p in instances.get(pid, [])
                              if project == self._path(p.parent.parent)), None)
                # Нельзя добавлять чужой Editor до проверки принадлежности.
                if pid not in self.sources and not worker_match and clone is None:
                    continue
                source = self.sources.setdefault(pid, {'ProcessId': pid, 'kind': 'worker-mpe' if clone else 'worker-discovered'})
                created = record.get('ProcessCreatedUtc')
                previous = source.get('processCreated')
                if previous and created and self._created(previous) != self._created(created):
                    source['captureBlocked'] = True
                    source['lifecycle'] = 'pid-reused'
                    self._error(phase, 'PID reused; refusing log capture from replacement process', ProcessId=pid,
                                previousProcessCreated=previous, currentProcessCreated=created)
                    continue
                source['processCreated'] = created
                source['lifetimeId'] = str(pid) + ':' + str(created or 'unknown-created')
                if source.get('captureBlocked'):
                    continue
                source['liveAtDiscovery'] = True
                source['closedTailCompleteEstablished'] = False
                source['lastSeenLiveUnixSeconds'] = time.time()
                source.setdefault('firstSeenLiveUnixSeconds', source['lastSeenLiveUnixSeconds'])
                source['projectPath'] = project
                source['workerProjectPathMatch'] = worker_match
                match = re.search(r'(?:^|\s)-logFile\s+(?:"([^"]+)"|(\S+))', args, re.I)
                if match:
                    path = match.group(1) or match.group(2)
                    if path != '-':
                        source['LogPath'] = path
                        source['pathEvidence'] = 'process-command-line'
                if clone and not source.get('LogPath'):
                    source.update({'LogPath': str(clone.parent.parent / 'Logs' / 'Editor.log'),
                                   'pathEvidence': str(clone), 'kind': 'worker-mpe'})
            for source in self.sources.values():
                if not source.get('retired') and not source.get('captureBlocked'):
                    source['lifecycle'] = 'live' if source.get('liveAtDiscovery') else 'closed'
                    if not source.get('liveAtDiscovery'):
                        source.setdefault('firstNotSeenUnixSeconds', time.time())
            self._resolve_path_owners(phase)
        except Exception as error:
            self._error(phase, type(error).__name__ + ': ' + str(error), operation='process-discovery')

    def _uncertain(self, phase, reason, source, **fields):
        self.report['logs']['integrityUncertainties'].append({'phase': phase, 'time': time.time(),
            'reason': reason, 'ProcessId': source['ProcessId'], 'lifetimeId': source.get('lifetimeId'), **fields})

    def _resolve_path_owners(self, phase):
        owners = {}
        for source in self.sources.values():
            if source.get('LogPath') and source.get('liveAtDiscovery') and not source.get('captureBlocked') and not source.get('retired'):
                path = self._path(source['LogPath'])
                owners.setdefault(path, []).append(source)
        for path, candidates in owners.items():
            candidates.sort(key=lambda s: self._created(s['processCreated']) if s.get('processCreated') else float('-inf'))
            owner = candidates[-1]
            owner['pathGenerationId'] = path + ':' + owner.get('lifetimeId', str(owner['ProcessId']))
            for source in self.sources.values():
                if source is owner or not source.get('LogPath') or self._path(source['LogPath']) != path or source.get('retired'):
                    continue
                # Живой подтверждённый процесс нового поколения забирает путь старого журнала.
                source.update({'retired': True, 'lifecycle': 'superseded', 'retiredUnixSeconds': time.time(),
                               'supersededByPid': owner['ProcessId'], 'supersededByLifetimeId': owner.get('lifetimeId'),
                               'closedTailCompleteEstablished': source.get('closedTailCompleteEstablished', False)})
                event = {'phase': phase, 'time': time.time(), 'normalizedLogPath': path,
                         'previousPid': source['ProcessId'], 'newPid': owner['ProcessId'],
                         'previousLifetimeId': source.get('lifetimeId'), 'newLifetimeId': owner.get('lifetimeId'),
                         'previousLastCapturedByte': source.get('nextOffset'),
                         'previousStillLive': bool(source.get('liveAtDiscovery')),
                         'closedTailMayBeLost': not source['closedTailCompleteEstablished']}
                self.report['logs']['ownershipEvents'].append(event)
                if event['closedTailMayBeLost'] or event['previousStillLive']:
                    self._uncertain(phase, 'Log path superseded; uncaptured final bytes of previous lifetime cannot be recovered or attributed',
                                    source, normalizedLogPath=path, supersededByPid=owner['ProcessId'])

    @staticmethod
    def _argument(command, name):
        match = re.search(r'(?:^|\s)-' + name + r'\s+(?:"([^"]+)"|(\S+))', command, re.I)
        return (match.group(1) or match.group(2)) if match else None

    @staticmethod
    def _path(path):
        return str(Path(path).resolve()).replace('\\', '/').rstrip('/').casefold()

    @staticmethod
    def _created(value):
        return datetime.fromisoformat(value.replace('Z', '+00:00')).timestamp()

    def capture(self, phase, state=None, force=False):
        # Сбой диагностики не должен препятствовать finally/Cancel.
        try:
            self._capture(phase, state, force)
        except Exception as error:
            self._error(phase, type(error).__name__ + ': ' + str(error), operation='snapshot')
            self.report['logs']['coverageComplete'] = False

    def _capture(self, phase, state=None, force=False):
        self.observe(state)
        if not force and time.monotonic() - self.last_poll < 5:
            return
        self.last_poll = time.monotonic()
        self._discover(phase)
        for pid, source in list(self.sources.items()):
            if source.get('captureBlocked') or source.get('retired'):
                continue
            peers = [p for p in self.peers.values() if p.get('ProcessId') == pid]
            path = source.get('LogPath')
            if not path:
                self._error(phase, 'Log path unknown; default Editor.log cannot be attributed safely', ProcessId=pid)
                continue
            self.index += 1
            record = {'phase': phase, 'time': time.time(), 'source': dict(source), 'participants': peers,
                      'runIds': sorted({p.get('RunId') for p in peers if p.get('RunId')}),
                      'runBoundaryUncertain': True,
                      'boundaryReason': 'Byte offsets bound snapshots, not exact Unity run start/end; newly discovered logs include earlier data.'}
            try:
                log = Path(path)
                if not log.is_absolute():
                    raise ValueError('Relative log path has unknown process working directory')
                stat = log.stat()
                first = 'nextOffset' not in source
                created = source.get('processCreated')
                # Для нового процесса сохраняем весь запуск, для старого/неизвестного — хвост истории.
                created_after_probe = bool(created and self._created(created) > self.started)
                baseline = first and not created_after_probe
                start = source.get('nextOffset', max(0, stat.st_size - 131072) if baseline else 0)
                rotated = (stat.st_size < start or source.get('fileCreated') not in (None, stat.st_ctime_ns))
                if rotated:
                    if source.get('liveAtDiscovery') is False:
                        completed_tail = source.get('closedTailCompleteEstablished', False)
                        source.update({'retired': True, 'lifecycle': 'closed-file-generation-changed',
                                       'retiredUnixSeconds': time.time(), 'closedTailCompleteEstablished': completed_tail})
                        self.report['logs']['ownershipEvents'].append({'phase': phase, 'time': time.time(),
                            'normalizedLogPath': self._path(path), 'previousPid': pid, 'newPid': None,
                            'previousLifetimeId': source.get('lifetimeId'), 'newLifetimeId': None,
                            'previousLastCapturedByte': start, 'observedReplacementSize': stat.st_size,
                            'replacementOwnerUnknown': True, 'closedTailMayBeLost': not completed_tail})
                        if not completed_tail:
                            self._uncertain(phase, 'Closed process log rotated/truncated; final tail and replacement generation ownership unknown',
                                            source, previousLastCapturedByte=start, observedSize=stat.st_size)
                        continue
                    self._uncertain(phase, 'Live log rotated/truncated between snapshots; missing intermediate bytes cannot be excluded',
                                    source, previousLastCapturedByte=start, observedSize=stat.st_size)
                    start = 0
                destination = self.directory / ('%05d-pid-%s.log' % (self.index, pid))
                with log.open('rb') as stream, destination.open('wb') as saved:
                    stream.seek(start)
                    remaining = stat.st_size - start
                    while remaining > 0:
                        chunk = stream.read(min(1024 * 1024, remaining))
                        if not chunk:
                            break
                        saved.write(chunk)
                        remaining -= len(chunk)
                end = start + destination.stat().st_size
                source.update({'nextOffset': end, 'fileCreated': stat.st_ctime_ns})
                record.update({'savedPath': str(destination), 'startByte': start, 'endByte': end,
                               'sizeAtOpen': stat.st_size, 'completeAtOpen': end == stat.st_size, 'rotatedOrTruncated': rotated,
                               'containsPreProbeHistory': first or rotated, 'historicalBytesOmitted': start if baseline else 0,
                               'processCreatedAfterProbeStart': created_after_probe,
                               'startupCapturedFromByteZero': first and created_after_probe and start == 0,
                               'fileMayContainEarlierProcessHistory': bool(created and stat.st_ctime < self._created(created)),
                               'sourceModifiedNs': stat.st_mtime_ns})
                record['closedProcessTailSnapshot'] = source.get('liveAtDiscovery') is False
                record['processLifetimeEndUncertain'] = source.get('liveAtDiscovery') is False
                findings = []
                # Полный исходный снимок остаётся рядом, контекст ошибки не теряется.
                offset = start
                with destination.open('rb') as saved:
                    for line_number, raw in enumerate(saved, 1):
                        line = raw.decode('utf-8', errors='replace').rstrip()
                        if ERROR.search(line):
                            findings.append({'line': line_number, 'sourceByte': offset, 'text': line,
                                             'category': 'lighting-asset' if LIGHTING.search(line) else 'error-candidate',
                                             'excluded': bool(LIGHTING.search(line))})
                        offset += len(raw)
                errors_path = destination.with_suffix('.errors.json')
                errors_path.write_text(json.dumps(findings, ensure_ascii=False, indent=2), encoding='utf-8')
                record.update({'errorsPath': str(errors_path), 'errorCandidates': sum(not f['excluded'] for f in findings),
                               'lightingAssetCandidates': sum(f['excluded'] for f in findings)})
                if end != stat.st_size:
                    self._error(phase, 'Log changed or became unreadable during snapshot', ProcessId=pid, path=path)
                elif source.get('liveAtDiscovery') is False:
                    source['closedTailCompleteEstablished'] = True
                record['closedTailCompleteAtOpen'] = source.get('liveAtDiscovery') is False and end == stat.st_size
            except Exception as error:
                record['captureError'] = type(error).__name__ + ': ' + str(error)
                self._error(phase, record['captureError'], ProcessId=pid, path=path)
            self.report['logs']['snapshots'].append(record)
        self._coverage()
        self._save_manifest()

    def _coverage(self):
        logs = self.report['logs']
        covered = set()
        for snapshot in logs['snapshots']:
            if snapshot.get('completeAtOpen'):
                for peer in snapshot['participants']:
                    covered.add((peer.get('RunId'), peer.get('ParticipantId'), peer.get('ProcessSessionId'), peer.get('ProcessId')))
        logs['participantSnapshotsCovered'] = bool(self.sources) and all(key in covered for key in self.peers)
        logs['coverageComplete'] = logs['participantSnapshotsCovered'] and not logs['captureErrors'] and not logs['integrityUncertainties']
        logs['missingParticipants'] = [peer for key, peer in self.peers.items() if key not in covered]
        logs['sourceLifetimes'] = [dict(source) for source in self.sources.values()]

    def _save_manifest(self):
        (self.directory / 'manifest.json').write_text(json.dumps(self.report['logs'], ensure_ascii=False, indent=2), encoding='utf-8')
