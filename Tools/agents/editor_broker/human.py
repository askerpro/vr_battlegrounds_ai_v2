"""Durable передача человеком; stash никогда не применяется повторно автоматически."""
import json
from pathlib import Path
import sys
import time
import uuid

from .queue import ACTIVE_PHASES
from .service import BrokerError, atomic_json, transaction_mutex

BACKUP_REFS = 'refs/editor-broker/human/'


class HumanHandoff:
    def __init__(self, broker):
        self.broker = broker
        self.path = broker.state / 'human-handoff.json'
        self.notice_path = broker.state / 'human-blocked.json'

    def read(self):
        return json.loads(self.path.read_text(encoding='utf-8')) if self.path.exists() else None

    def write(self, value):
        atomic_json(self.path, value)
        return value

    def _idle(self):
        return not any(t['phase'] in ACTIVE_PHASES for t in self.broker.store.status()['tickets'])

    def _drop_own_stash(self, sha):
        # Не drop по плавающему stash@{n} вслепую: индекс ищется по точному SHA
        # и сверяется повторно; содержимое остаётся в резервном ref.
        listing = self.broker.git._git('stash', 'list', '--format=%H').decode().split()
        if sha not in listing:
            return
        entry = 'stash@{' + str(listing.index(sha)) + '}'
        if self.broker.git._commit(entry) == sha:
            self.broker.git._git('stash', 'drop', '-q', entry)

    def release(self):
        b = self.broker
        with transaction_mutex(b.state):
            b._reload_config()
            b.store.set_paused(True)
            if not self._idle():
                raise BrokerError('Редактор занят агентом; передача человеком запрещена')
            previous = self.read()
            if previous and previous['stage'] != 'HUMAN':
                raise BrokerError('Незавершённая передача: сначала вернуть сохранённые изменения')
            b._ready()
            if b.git.head() != b.config['baseline_sha']:
                raise BrokerError('Человек изменил HEAD worker; нужна отдельная публикация базы')
            b._check_main(b.git.head())
            record = self.write({'stage': 'PREPARING', 'id': uuid.uuid4().hex,
                                 'base': b.git.head(), 'setup': None, 'stash_sha': None})
            try:
                # Как begin: сцены закрываются до изменения файлов под Unity.
                record['setup'] = b.adapter.park()
                self.write(record)
                if not b.git.is_clean():
                    b.git._git('stash', 'push', '--include-untracked', '-m', 'editor-human-' + record['id'])
                    sha = b.git._commit('refs/stash')
                    # Ref удерживает содержимое даже при ручном stash drop/clear.
                    b.git._git('update-ref', BACKUP_REFS + record['id'], sha)
                    record['stash_sha'] = sha
                    self.write(record)
                if not b.git.is_clean():
                    raise BrokerError('После stash worker не чист; очередь остаётся на паузе')
                b.adapter.refresh()
                b._ready()
                record['stage'] = 'AGENTS'
                self.write(record)
                self.notice_path.unlink(missing_ok=True)
                b.store.set_paused(False)
                return record
            except Exception as error:
                record['stage'] = 'RECOVERY_REQUIRED'
                record['error'] = str(error)
                self.write(record)
                raise

    def resume(self):
        b = self.broker
        # Pause SQLite до OS mutex: даже долгий finish уже не выдаст следующую аренду.
        b.store.set_paused(True)
        with transaction_mutex(b.state):
            b._reload_config()
            record = self.read()
            if record is None or record['stage'] == 'HUMAN':
                if not self._idle():
                    return {'stage': 'WAITING', 'reason': 'Текущая аренда завершается'}
                return record or {'stage': 'HUMAN'}
            if record['stage'] not in ('AGENTS', 'WAITING'):
                raise BrokerError('Неизвестный исход handoff; повторный stash apply запрещён')
            record['stage'] = 'WAITING'
            self.write(record)
            if not self._idle():
                return record
            if b.git.head() != record['base'] or b.config['baseline_sha'] != record['base']:
                raise BrokerError('База изменилась; stash сохранён, автоматический возврат запрещён')
            b._ready()
            if not b.git.is_clean():
                raise BrokerError('Worker изменился; stash сохранён, сначала разобрать чужие изменения')
            record['stage'] = 'APPLYING'
            self.write(record)
            try:
                if record['stash_sha']:
                    b.git._git('stash', 'apply', '--index', record['stash_sha'])
                b.adapter.refresh()
                if record.get('setup'):
                    b.adapter.restore(record['setup'])
                b._ready(clean=False)
                if record['stash_sha']:
                    self._drop_own_stash(record['stash_sha'])
                record['stage'] = 'HUMAN'
                self.write(record)
                return record
            except Exception as error:
                record['stage'] = 'RECOVERY_REQUIRED'
                record['error'] = str(error)
                self.write(record)
                raise

    def defer(self, minutes=0):
        """0 — отказ без срока до явной передачи; иначе очередь ждёт указанное время."""
        if not 0 <= minutes <= 1440:
            raise BrokerError('Отсрочка: от 0 до 1440 минут')
        with transaction_mutex(self.broker.state):
            if not self._idle():
                raise BrokerError('Отказ не прерывает активную аренду')
            # set_paused возвращает OFFERED в QUEUED с прежним id: агент остаётся первым.
            result = self.broker.store.set_paused(minutes == 0, minutes * 60)
            if self.notice_path.exists():
                notice = json.loads(self.notice_path.read_text(encoding='utf-8'))
                notice['acknowledged'] = True
                atomic_json(self.notice_path, notice)
            return result

    def clear(self):
        """Ручной разбор после RECOVERY_REQUIRED; резервный ref stash не удаляется."""
        b = self.broker
        with transaction_mutex(b.state):
            record = self.read()
            if record is None:
                return {'stage': 'HUMAN'}
            if record['stage'] not in ('HUMAN', 'RECOVERY_REQUIRED'):
                raise BrokerError('Очищать можно только завершённую или аварийную передачу')
            self.path.unlink()
            return {'stage': 'HUMAN', 'backup_ref': BACKUP_REFS + record['id'] if record.get('stash_sha') else None}

    def status(self):
        """Вызов панели: продолжает запрошенный человеком возврат, когда аренда закончилась."""
        record = self.read()
        if record and record['stage'] == 'WAITING' and self._idle():
            try:
                self.resume()
            except Exception as error:
                # До apply запись остаётся WAITING: следующий опрос повторит проверки.
                return self.snapshot(error=str(error))
        return self.snapshot()

    def snapshot(self, error=None):
        """Только чтение очереди; файл для Unity-панели пишется после любой команды CLI."""
        b = self.broker
        record = self.read() or {}
        tickets = b.store.status()['tickets']
        active = next((t for t in tickets if t['phase'] in ACTIVE_PHASES), None)
        queued = [t for t in tickets if t['phase'] in ('QUEUED', 'OFFERED')]
        notice = json.loads(self.notice_path.read_text(encoding='utf-8')) if self.notice_path.exists() else {}
        hold = b.store.hold()
        result = {'updated_unix_ms': int(time.time() * 1000), 'busy': bool(active),
                  'owner': active['owner'] if active else notice.get('owner', ''),
                  'phase': active['phase'] if active else record.get('stage', 'HUMAN'),
                  'handoff_stage': record.get('stage', ''),
                  'reason': notice.get('reason', ''), 'episode': notice.get('episode', ''),
                  'notify': bool(notice) and not notice.get('acknowledged', False) and bool(queued),
                  'waiting_count': len(queued), 'waiting_owners': [t['owner'] for t in queued],
                  'paused': hold['paused'], 'defer_until': hold['defer_until'],
                  'error': error or record.get('error', ''),
                  'python': sys.executable,
                  'cli': str(Path(b.state) / 'runtime' / 'editor-broker.py')}
        atomic_json(b.state / 'human-ui.json', result)
        return result
