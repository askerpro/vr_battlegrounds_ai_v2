"""Постоянная FIFO-очередь редактора с адресными событиями и fencing аренды.

SQLite сериализует команды очереди. Внешний контроллер дополнительно держит
OS/file mutex на весь переход Unity/Git, а не только на изменение этой базы.
"""

from contextlib import contextmanager
import hmac
import json
import math
from pathlib import Path
import secrets
import sqlite3
import time


ACTIVE_PHASES = ("LEASED", "SWITCHING", "RUNNING", "CAPTURING", "RESTORING", "RECOVERY_REQUIRED")
TERMINAL_PHASES = ("DONE", "FAILED", "CANCELLED")
NEXT_PHASES = {
    "LEASED": ("SWITCHING", "RESTORING", "FAILED", "RECOVERY_REQUIRED"),
    "SWITCHING": ("RUNNING", "RESTORING", "RECOVERY_REQUIRED"),
    "RUNNING": ("CAPTURING", "RESTORING", "RECOVERY_REQUIRED"),
    "CAPTURING": ("RESTORING", "RECOVERY_REQUIRED"),
    "RESTORING": ("RECOVERY_REQUIRED",),
    "RECOVERY_REQUIRED": ("RESTORING",),
}
IMMUTABLE_FIELDS = frozenset(("id", "ticket_id", "owner", "base_sha", "input_sha",
                              "agent_root", "output_roots", "request_key", "token", "epoch",
                              "phase", "created_at", "updated_at", "offer_expires_at", "lease_expires_at"))


def _json(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False)


def _public(value):
    # Одинаковая фильтрация применяется также к вложенным журналам и событиям.
    if isinstance(value, dict):
        return {key: _public(item) for key, item in value.items() if key != "token"}
    if isinstance(value, list):
        return [_public(item) for item in value]
    return value


class BrokerStore:
    def __init__(self, state_dir, clock=time.time, offer_seconds=60, lease_seconds=900):
        self.state_dir = Path(state_dir).resolve()
        self.state_dir.mkdir(parents=True, exist_ok=True)
        self.path = self.state_dir / "broker.sqlite3"
        self.clock = clock
        self.offer_seconds = self._duration(offer_seconds)
        self.lease_seconds = self._duration(lease_seconds)
        with self._connection() as connection:
            connection.executescript("""
                CREATE TABLE IF NOT EXISTS tickets (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    owner TEXT NOT NULL, request_key TEXT NOT NULL, payload TEXT NOT NULL,
                    phase TEXT NOT NULL, token TEXT, epoch INTEGER NOT NULL DEFAULT 0,
                    created_at REAL NOT NULL, updated_at REAL NOT NULL,
                    offer_expires_at REAL, lease_expires_at REAL,
                    details TEXT NOT NULL DEFAULT '{}', result_sha TEXT, result_status TEXT,
                    UNIQUE(owner, request_key)
                );
                CREATE TABLE IF NOT EXISTS events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, owner TEXT NOT NULL,
                    ticket_id INTEGER NOT NULL, kind TEXT NOT NULL, phase TEXT NOT NULL,
                    created_at REAL NOT NULL, details TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS events_owner_cursor ON events(owner, id);
                CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
                INSERT OR IGNORE INTO metadata(key, value) VALUES ('epoch', 0);
                INSERT OR IGNORE INTO metadata(key, value) VALUES ('paused', 0);
                INSERT OR IGNORE INTO metadata(key, value) VALUES ('defer_until', 0);
            """)

    @staticmethod
    def _duration(value):
        value = float(value)
        if not math.isfinite(value) or value <= 0:
            raise ValueError("Срок должен быть конечным положительным числом")
        return value

    @contextmanager
    def _connection(self, deadline=None):
        timeout = 30 if deadline is None else max(0, deadline - time.monotonic())
        connection = sqlite3.connect(self.path, timeout=timeout, isolation_level=None)
        connection.row_factory = sqlite3.Row
        try:
            yield connection
        finally:
            connection.close()

    @contextmanager
    def _transaction(self, deadline=None):
        with self._connection(deadline) as connection:
            self._busy_deadline(connection, deadline)
            connection.execute("BEGIN IMMEDIATE")
            try:
                now = float(self.clock())
                if not math.isfinite(now):
                    raise ValueError("Часы вернули некорректное время")
                self._advance(connection, now)
                connection.execute("SAVEPOINT command")
                try:
                    yield connection, now
                except Exception:
                    # Отказ validate/claim не отменяет уже обнаруженную просрочку.
                    connection.execute("ROLLBACK TO command")
                    connection.execute("RELEASE command")
                    self._busy_deadline(connection, deadline)
                    connection.commit()
                    raise
                connection.execute("RELEASE command")
                self._busy_deadline(connection, deadline)
                connection.commit()
            except BaseException:
                if connection.in_transaction:
                    connection.rollback()
                raise

    @staticmethod
    def _busy_deadline(connection, deadline):
        if deadline is not None:
            # COMMIT тоже может ждать чужого reader; повторный полный timeout
            # нарушил бы общий бюджет wait. Значение — только вычисленное число.
            remaining_ms = max(0, int((deadline - time.monotonic()) * 1000))
            connection.execute(f"PRAGMA busy_timeout = {remaining_ms}")

    @staticmethod
    def _ticket(row):
        result = dict(row)
        result.update(json.loads(result.pop("payload")))
        result["details"] = json.loads(result["details"])
        result["ticket_id"] = result["id"]
        return result

    def _get(self, connection, ticket_id):
        row = connection.execute("SELECT * FROM tickets WHERE id = ?", (ticket_id,)).fetchone()
        if row is None:
            raise ValueError("Неизвестная заявка")
        return self._ticket(row)

    def _event(self, connection, ticket_id, kind, now, details=None):
        ticket = self._get(connection, ticket_id)
        connection.execute(
            "INSERT INTO events(owner,ticket_id,kind,phase,created_at,details) VALUES(?,?,?,?,?,?)",
            (ticket["owner"], ticket_id, kind, ticket["phase"], now,
             _json(_public(ticket["details"] if details is None else details))))

    def _set_phase(self, connection, ticket_id, phase, now, **values):
        values.update(phase=phase, updated_at=now)
        # Имена колонок задаются только внутренними вызовами; данные параметризованы.
        assignments = ", ".join(f"{name} = ?" for name in values)
        connection.execute(f"UPDATE tickets SET {assignments} WHERE id = ?",
                           (*values.values(), ticket_id))
        self._event(connection, ticket_id, phase, now)

    def _advance(self, connection, now):
        active = connection.execute("SELECT * FROM tickets WHERE phase IN (?,?,?,?,?,?) ORDER BY id LIMIT 1",
                                    ACTIVE_PHASES).fetchone()
        if active is not None:
            if active["phase"] != "RECOVERY_REQUIRED" and active["lease_expires_at"] <= now:
                self._set_phase(connection, active["id"], "RECOVERY_REQUIRED", now)
            return
        values = dict(connection.execute("SELECT key,value FROM metadata"))
        if values.get("paused") or values.get("defer_until", 0) > now:
            return
        offered = connection.execute("SELECT * FROM tickets WHERE phase = 'OFFERED' ORDER BY id LIMIT 1").fetchone()
        if offered is not None:
            if offered["offer_expires_at"] > now:
                return
            self._set_phase(connection, offered["id"], "CANCELLED", now,
                            details=_json({**json.loads(offered["details"]), "reason": "offer_expired"}))
        queued = connection.execute("SELECT id FROM tickets WHERE phase = 'QUEUED' ORDER BY id LIMIT 1").fetchone()
        if queued is not None:
            self._set_phase(connection, queued["id"], "OFFERED", now,
                            offer_expires_at=now + self.offer_seconds)

    @staticmethod
    def _owner(ticket, owner):
        if ticket["owner"] != owner:
            raise ValueError("Заявка принадлежит другому владельцу")

    def _fence(self, connection, ticket_id, token, recovery=False):
        ticket = self._get(connection, ticket_id)
        if not isinstance(token, str) or not ticket["token"] or not hmac.compare_digest(ticket["token"], token):
            raise ValueError("Неверный токен аренды")
        if ticket["phase"] not in ACTIVE_PHASES:
            raise RuntimeError("Аренда уже завершена")
        if ticket["phase"] == "RECOVERY_REQUIRED" and not recovery:
            raise RuntimeError("Требуется проверенное восстановление редактора")
        return ticket

    def enqueue(self, owner, base_sha, input_sha, agent_root, output_roots, request_key):
        for value in (owner, base_sha, input_sha, agent_root, request_key):
            if not isinstance(value, str) or not value.strip():
                raise ValueError("Поля заявки должны быть непустыми строками")
        if isinstance(output_roots, (str, bytes)):
            raise ValueError("Выходные пути должны быть списком")
        roots = list(output_roots)
        if any(not isinstance(root, str) or not root.strip() for root in roots):
            raise ValueError("Выходные пути должны быть непустыми строками; пустой список запрещает запись выходов")
        payload = _json(dict(base_sha=base_sha, input_sha=input_sha, agent_root=agent_root, output_roots=roots))
        with self._transaction() as (connection, now):
            prior = connection.execute("SELECT * FROM tickets WHERE owner = ? AND request_key = ?",
                                       (owner, request_key)).fetchone()
            if prior is not None:
                if prior["payload"] != payload:
                    raise ValueError("Ключ идемпотентности уже закреплён за другим содержимым")
                return _public(self._ticket(prior))
            cursor = connection.execute(
                "INSERT INTO tickets(owner,request_key,payload,phase,created_at,updated_at) VALUES(?,?,?,'QUEUED',?,?)",
                (owner, request_key, payload, now, now))
            ticket_id = cursor.lastrowid
            self._event(connection, ticket_id, "QUEUED", now)
            self._advance(connection, now)
            return _public(self._get(connection, ticket_id))

    def claim(self, ticket_id, owner):
        with self._transaction() as (connection, now):
            values = dict(connection.execute("SELECT key,value FROM metadata"))
            if values.get("paused") or values.get("defer_until", 0) > now:
                raise RuntimeError("Человек приостановил передачу редактора")
            ticket = self._get(connection, ticket_id)
            self._owner(ticket, owner)
            if ticket["phase"] != "OFFERED":
                raise RuntimeError("Очередь ещё не предложила эту заявку")
            connection.execute("UPDATE metadata SET value = value + 1 WHERE key = 'epoch'")
            epoch = connection.execute("SELECT value FROM metadata WHERE key = 'epoch'").fetchone()[0]
            self._set_phase(connection, ticket_id, "LEASED", now,
                            token=secrets.token_urlsafe(32), epoch=epoch,
                            lease_expires_at=now + self.lease_seconds, offer_expires_at=None)
            return self._get(connection, ticket_id)

    def is_paused(self):
        return self.hold()["paused"]

    def hold(self):
        """Пауза человеком или действующая отсрочка; истёкшая отсрочка не держит очередь."""
        with self._connection() as connection:
            values = dict(connection.execute("SELECT key,value FROM metadata"))
        defer_until = values.get("defer_until", 0)
        if defer_until <= float(self.clock()):
            defer_until = 0
        return {"paused": bool(values.get("paused")), "defer_until": defer_until,
                "held": bool(values.get("paused")) or bool(defer_until)}

    def set_paused(self, paused, defer_seconds=0):
        if not math.isfinite(defer_seconds) or defer_seconds < 0:
            raise ValueError("Отсрочка должна быть конечной и неотрицательной")
        with self._transaction() as (connection, now):
            connection.execute("UPDATE metadata SET value=? WHERE key='paused'", (int(paused),))
            connection.execute("UPDATE metadata SET value=? WHERE key='defer_until'",
                               (math.ceil(now + defer_seconds) if defer_seconds else 0,))
            # Сохраняем исходный id и порядок FIFO; оффер не истекает во время отказа.
            for row in connection.execute("SELECT id FROM tickets WHERE phase='OFFERED'").fetchall():
                self._set_phase(connection, row[0], "QUEUED", now, offer_expires_at=None)
            return {"paused": bool(paused), "defer_until": math.ceil(now + defer_seconds) if defer_seconds else 0}

    def validate(self, ticket_id, token):
        with self._transaction() as (connection, _):
            return self._fence(connection, ticket_id, token)

    def renew(self, ticket_id, token):
        with self._transaction() as (connection, now):
            self._fence(connection, ticket_id, token)
            connection.execute("UPDATE tickets SET lease_expires_at = ?, updated_at = ? WHERE id = ?",
                               (now + self.lease_seconds, now, ticket_id))
            self._event(connection, ticket_id, "RENEWED", now)
            return self._get(connection, ticket_id)

    def transition(self, ticket_id, token, phase, **details):
        if IMMUTABLE_FIELDS.intersection(details):
            raise ValueError("Переход не может менять поля заявки и аренды")
        with self._transaction() as (connection, now):
            ticket = self._fence(connection, ticket_id, token, recovery=True)
            if phase != ticket["phase"] and phase not in NEXT_PHASES.get(ticket["phase"], ()):
                raise RuntimeError(f"Недопустимый переход {ticket['phase']} -> {phase}")
            values = {"details": _json({**ticket["details"], **details})}
            if ticket["phase"] == "RECOVERY_REQUIRED":
                values["lease_expires_at"] = now + self.lease_seconds
            self._set_phase(connection, ticket_id, phase, now, **values)
            if phase in TERMINAL_PHASES:
                self._advance(connection, now)
            return self._get(connection, ticket_id)

    def complete(self, ticket_id, token, result_sha=None, restored=True):
        if not isinstance(restored, bool):
            raise ValueError("restored должен быть булевым")
        if result_sha is not None and (not isinstance(result_sha, str) or not result_sha):
            raise ValueError("SHA результата должен быть непустой строкой")
        with self._transaction() as (connection, now):
            ticket = self._fence(connection, ticket_id, token, recovery=not restored)
            if restored and ticket["phase"] not in ("LEASED", "RESTORING"):
                raise RuntimeError("Завершение требует проверенного RESTORING")
            if ticket["result_sha"] and result_sha and ticket["result_sha"] != result_sha:
                raise ValueError("Закреплённый результат нельзя подменить")
            result_sha = result_sha or ticket["result_sha"]
            self._set_phase(connection, ticket_id, "DONE" if restored else "RECOVERY_REQUIRED", now,
                            result_sha=result_sha, result_status="AVAILABLE" if result_sha else None)
            if restored:
                self._advance(connection, now)
            return self._get(connection, ticket_id)

    def cancel(self, ticket_id, owner):
        with self._transaction() as (connection, now):
            ticket = self._get(connection, ticket_id)
            self._owner(ticket, owner)
            if ticket["phase"] == "CANCELLED":
                return _public(ticket)
            if ticket["phase"] not in ("QUEUED", "OFFERED"):
                raise RuntimeError("Активную аренду нельзя освободить отменой")
            self._set_phase(connection, ticket_id, "CANCELLED", now)
            self._advance(connection, now)
            return _public(self._get(connection, ticket_id))

    def get(self, ticket_id):
        """Внутренний API сервиса: содержит токен для восстановления под mutex."""
        with self._transaction() as (connection, _):
            return self._get(connection, ticket_id)

    def status(self):
        with self._transaction() as (connection, _):
            tickets = [_public(self._ticket(row)) for row in connection.execute("SELECT * FROM tickets ORDER BY id")]
            active = next((ticket for ticket in tickets if ticket["phase"] in ACTIVE_PHASES), None)
            offered = next((ticket for ticket in tickets if ticket["phase"] == "OFFERED"), None)
            recovery = bool(active and active["phase"] == "RECOVERY_REQUIRED")
            return dict(blocked=recovery, recovery_required=recovery, active=active, offered=offered, tickets=tickets)

    @staticmethod
    def _events(connection, owner, after=0, ticket_id=None):
        query = "SELECT * FROM events WHERE owner = ? AND id > ?"
        parameters = [owner, after]
        if ticket_id is not None:
            query += " AND ticket_id = ?"
            parameters.append(ticket_id)
        result = []
        for row in connection.execute(query + " ORDER BY id", parameters):
            event = dict(row)
            event["details"] = json.loads(event["details"])
            result.append(_public(event))
        return result

    def events(self, owner, after=0):
        with self._transaction() as (connection, _):
            return self._events(connection, owner, after)

    def wait(self, ticket_id, owner, timeout=60):
        timeout = float(timeout)
        if not math.isfinite(timeout) or not 0 <= timeout <= 60:
            raise ValueError("Ожидание ограничено интервалом 0..60 секунд")
        deadline = time.monotonic() + timeout
        while True:
            try:
                with self._transaction(deadline) as (connection, _):
                    ticket = self._get(connection, ticket_id)
                    self._owner(ticket, owner)
                    result = _public(ticket)
                    result["events"] = self._events(connection, owner, ticket_id=ticket_id)
                    blocked = connection.execute("SELECT id FROM tickets WHERE phase = 'RECOVERY_REQUIRED' LIMIT 1").fetchone()
                    if blocked is not None and ticket["phase"] == "QUEUED":
                        result["blocked_by_recovery"] = True
                        result["blocking_ticket_id"] = blocked["id"]
                        return result
                    if ticket["phase"] != "QUEUED" or time.monotonic() >= deadline:
                        return result
            except sqlite3.OperationalError as error:
                code = getattr(error, "sqlite_errorcode", None)
                # Python 3.10 ещё не предоставляет sqlite_errorcode и SQLITE_*
                # константы; узнаём только известные сообщения занятости.
                busy = ((code & 255) in (5, 6) if code is not None else
                        str(error) in ("database is locked", "database table is locked", "database schema is locked"))
                if not busy:
                    raise
                if time.monotonic() >= deadline:
                    # Нельзя выдать прежний OFFERED как подтверждённое резервирование
                    # или раскрыть журнал до проверки владельца в транзакции.
                    return dict(id=ticket_id, ticket_id=ticket_id, phase="BUSY", busy=True,
                                timed_out=True, blocked=True, events=[])
            # Сон не удерживает SQLite; резервирование уже задано FIFO.
            time.sleep(min(0.05, max(0, deadline - time.monotonic())))

    def ack_result(self, ticket_id, owner):
        with self._transaction() as (connection, now):
            ticket = self._get(connection, ticket_id)
            self._owner(ticket, owner)
            if ticket["phase"] != "DONE" or not ticket["result_sha"]:
                raise RuntimeError("Результат ещё не готов к подтверждению")
            if ticket["result_status"] == "IMPORTED":
                return _public(ticket)
            connection.execute("UPDATE tickets SET result_status = 'IMPORTED', updated_at = ? WHERE id = ?",
                               (now, ticket_id))
            self._event(connection, ticket_id, "IMPORTED", now)
            return _public(self._get(connection, ticket_id))
