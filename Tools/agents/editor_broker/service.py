"""Единственный владелец переключений постоянного worktree редактора."""
from __future__ import annotations

from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import uuid

from .git_state import GitState
from .queue import BrokerStore
from .unity import FileUnityAdapter, UnityOperationError, install_bridge


class BrokerError(RuntimeError):
    pass


def canonical(path):
    return os.path.normcase(str(Path(path).resolve()))


def exact_sha(value):
    if not isinstance(value, str) or not re.fullmatch(r"(?:[0-9a-f]{40}|[0-9a-f]{64})", value):
        raise BrokerError("Нужен полный неизменяемый SHA коммита, не имя ветки/HEAD")
    return value


def atomic_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    with temporary.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)


@contextmanager
def transaction_mutex(state_dir):
    """OS-lock автоматически отпускается при гибели процесса; журнал остаётся."""
    path = Path(state_dir) / "transaction.lock"
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a+b") as stream:
        if path.stat().st_size == 0:
            stream.write(b"0")
            stream.flush()
        stream.seek(0)
        if os.name == "nt":
            import msvcrt
            try:
                msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
            except OSError as error:
                raise BrokerError("Другой процесс завершает Unity/Git переход") from error
            try:
                yield
            finally:
                stream.seek(0)
                msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
        else:
            import fcntl
            try:
                fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except OSError as error:
                raise BrokerError("Другой процесс завершает Unity/Git переход") from error
            try:
                yield
            finally:
                fcntl.flock(stream, fcntl.LOCK_UN)


def default_state_dir(repo):
    common = Path(GitState(repo).common_dir()).resolve()
    return common.parent / ".agent-state" / "editor-broker"


def process_alive(pid):
    """Проверка Windows без os.kill: нестандартный сигнал там завершает процесс."""
    if not isinstance(pid, int) or pid <= 0:
        raise BrokerError("Неизвестен PID редактора; остановка не доказана")
    if os.name == "nt":
        import ctypes
        from ctypes import wintypes
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel.OpenProcess.restype = wintypes.HANDLE
        kernel.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
        kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        handle = kernel.OpenProcess(0x1000, False, pid)
        if not handle:
            if ctypes.get_last_error() == 87:
                return False
            raise BrokerError("Не удалось доказать остановку PID " + str(pid))
        try:
            exit_code = wintypes.DWORD()
            if not kernel.GetExitCodeProcess(handle, ctypes.byref(exit_code)):
                raise BrokerError("Не удалось прочитать состояние PID " + str(pid))
            return exit_code.value == 259
        finally:
            kernel.CloseHandle(handle)
    try:
        os.kill(pid, 0)
        return True
    except ProcessLookupError:
        return False
    except PermissionError:
        return True


class EditorBroker:
    @staticmethod
    def initialize(editor_root, state_dir, baseline_sha, install=True):
        baseline_sha = exact_sha(baseline_sha)
        root, state = Path(editor_root).resolve(), Path(state_dir).resolve()
        if state.is_relative_to(root):
            raise BrokerError("Журнал должен находиться вне редакторского worktree")
        git = GitState(root)
        if not (root / ".git").is_file():
            raise BrokerError("Редактору нужен отдельный linked worktree, не основной checkout")
        if git.head() != baseline_sha or not git.is_clean():
            raise BrokerError("Редакторский worktree должен быть чистым на принятой базе")
        from asset_pairs import audit
        pairs = audit(root, source="head")
        if not pairs["passed"]:
            raise BrokerError("База содержит .meta без версионируемого ассета; сначала исправить пары")
        state.mkdir(parents=True, exist_ok=True)
        with transaction_mutex(state):
            config_path = state / "config.json"
            config = {"version": 1, "editor_root": str(root), "baseline_sha": baseline_sha,
                      "common_dir": str(Path(git.common_dir()).resolve())}
            if config_path.exists():
                existing = json.loads(config_path.read_text(encoding="utf-8"))
                if any(existing.get(key) != value for key, value in config.items()):
                    raise BrokerError("Координация уже принадлежит другой базе/редактору")
                return existing
            if install:
                config["bridge"] = install_bridge(root, state)
            # runtime-копия остаётся вне гостевого checkout, включая CLI и шаблоны моста.
            runtime = state / "runtime"
            runtime.mkdir(exist_ok=True)
            sources = Path(__file__).resolve().parent
            shutil.copy2(sources.parent / "asset_pairs.py", runtime / "asset_pairs.py")
            shutil.copytree(sources, runtime / "editor_broker", dirs_exist_ok=True,
                            ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))
            bridge_sources = sources.parent / "unity_bridge"
            if bridge_sources.exists():
                shutil.copytree(bridge_sources, runtime / "unity_bridge", dirs_exist_ok=True,
                                ignore=shutil.ignore_patterns("__pycache__", "bin", "obj"))
            wrapper = sources.parent / "editor-broker.py"
            if wrapper.exists():
                shutil.copy2(wrapper, runtime / "editor-broker.py")
            atomic_json(config_path, config)
            BrokerStore(state)
            return config

    def __init__(self, state_dir, adapter=None):
        self.state = Path(state_dir).resolve()
        self.config = json.loads((self.state / "config.json").read_text(encoding="utf-8"))
        self.editor_root = Path(self.config["editor_root"]).resolve()
        self.git = GitState(self.editor_root)
        self.store = BrokerStore(self.state)
        self.adapter = adapter or FileUnityAdapter(self.editor_root, self.state)
        self._request_context = None
        self.adapter.on_request = self._record_intent
        if canonical(self.git.common_dir()) != canonical(self.config["common_dir"]):
            raise BrokerError("Изменился общий Git-репозиторий редактора")

    def _reload_config(self):
        current = json.loads((self.state / "config.json").read_text(encoding="utf-8"))
        if canonical(current["editor_root"]) != canonical(self.editor_root) or canonical(
                current["common_dir"]) != canonical(self.git.common_dir()):
            raise BrokerError("Регистрация контроллера изменилась вне разрешённой публикации базы")
        self.config = current

    def _record_intent(self, request_id, operation, arguments):
        if self._request_context is None:
            return
        ticket_id, token, role = self._request_context
        ticket = self.store.get(ticket_id)
        if ticket["phase"] == "RECOVERY_REQUIRED":
            raise BrokerError("Аренда истекла до отправки Unity request; требуется recovery")
        details = {"pending_request_id": request_id, "pending_operation": operation, "pending_role": role}
        if role == "original_park":
            details["park_request_id"] = request_id
        self.store.transition(ticket_id, token, ticket["phase"], **details)

    def _unity_call(self, ticket_id, token, operation, *arguments, role=None):
        self._request_context = (ticket_id, token, role or operation)
        try:
            return getattr(self.adapter, operation)(*arguments)
        finally:
            self._request_context = None

    @staticmethod
    def _snapshot_setup(snapshot):
        scenes = snapshot.get("scenes")
        if not isinstance(scenes, list):
            raise BrokerError("inspect не вернул конфигурацию сцен для durable возврата")
        return {"scenes": scenes, "prefab_path": snapshot.get("prefab_path")}

    def _ready(self, clean=True, allow_compile_errors=False, allow_busy=False):
        managed = self.config.get("bridge", {}).get("managed_files", {})
        for relative, expected in managed.items():
            path = self.editor_root / relative
            if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
                raise BrokerError("Изменился локальный мост: " + relative)
        snapshot = self.adapter.inspect()
        if canonical(snapshot.get("project_root", "")) != canonical(self.editor_root):
            raise BrokerError("Unity подключён к другому worktree")
        if not allow_busy and (not snapshot.get("ready") or any(snapshot.get(key) for key in
                ("is_playing", "is_compiling", "is_updating", "is_test_running"))):
            raise BrokerError("Unity занят: Play/compile/import/tests; операция не начата")
        stage = snapshot.get("prefab_stage") or {}
        if clean and (snapshot.get("dirty_scenes") or snapshot.get("dirty_assets") or
                      (isinstance(stage, dict) and any(stage.get(key) for key in
                       ("dirty", "is_dirty", "isDirty")))):
            raise BrokerError("Есть незаписанные сцены, ассеты или Prefab Stage")
        if snapshot.get("compile_errors") and not allow_compile_errors:
            raise BrokerError("Проверяемое состояние содержит ошибки компиляции")
        return snapshot

    def request(self, owner, base_sha, input_sha, agent_root, output_roots, request_key):
        with transaction_mutex(self.state):
            self._reload_config()
            return self._enqueue_request(owner, base_sha, input_sha, agent_root, output_roots, request_key)

    def _enqueue_request(self, owner, base_sha, input_sha, agent_root, output_roots, request_key):
        base_sha, input_sha = exact_sha(base_sha), exact_sha(input_sha)
        source = GitState(agent_root)
        if not (Path(agent_root).resolve() / ".git").is_file():
            raise BrokerError("Работа агента должна находиться в отдельном linked worktree")
        if canonical(source.common_dir()) != canonical(self.git.common_dir()):
            raise BrokerError("Агент и редактор принадлежат разным Git-репозиториям")
        if canonical(agent_root) == canonical(self.editor_root):
            raise BrokerError("Агент не должен писать в редакторский worktree")
        if base_sha != self.config["baseline_sha"] or not source.is_ancestor(base_sha, input_sha):
            raise BrokerError("Устаревшая или чужая база: обновить checkpoint вне редактора")
        # Проверка путей общая с Unity; даже fake-adapter не обходит ограничений.
        from .unity import _paths
        roots = _paths(self.editor_root, output_roots)
        return self.store.enqueue(owner, base_sha, input_sha, str(Path(agent_root).resolve()),
                                  roots, request_key)

    def claim(self, ticket_id, owner):
        with transaction_mutex(self.state):
            self._reload_config()
            self._ready()
            if self.git.head() != self.config["baseline_sha"] or not self.git.is_clean():
                raise BrokerError("Перед claim требуется чистый редактор на принятой базе")
            return self.store.claim(ticket_id, owner)

    def _fail(self, ticket_id, token, error):
        pending = getattr(self.adapter, "last_request_id", None)
        self.store.transition(ticket_id, token, "RECOVERY_REQUIRED",
                              failure=str(error), pending_request_id=pending)

    def begin(self, ticket_id, token):
        with transaction_mutex(self.state):
            self._reload_config()
            ticket = self.store.validate(ticket_id, token)
            if ticket["phase"] != "LEASED":
                raise BrokerError("begin требует подтверждённую LEASED заявку")
            try:
                if ticket["base_sha"] != self.config["baseline_sha"]:
                    raise BrokerError("База изменилась после постановки в очередь")
                if self.git.head() != ticket["base_sha"] or not self.git.is_clean():
                    raise BrokerError("Редакторские файлы изменились вне контроллера")
                snapshot = self._ready()
                self.store.transition(ticket_id, token, "SWITCHING", baseline_sha=ticket["base_sha"],
                                      original_setup=self._snapshot_setup(snapshot),
                                      process_id=snapshot.get("process_id"), session_id=snapshot.get("session_id"))
                setup = self._unity_call(ticket_id, token, "park", role="original_park")
                self.store.transition(ticket_id, token, "SWITCHING", original_setup=setup)
                self.git.switch_detached(ticket["input_sha"])
                self.store.transition(ticket_id, token, "SWITCHING", disk_switched=True)
                self._unity_call(ticket_id, token, "refresh")
                self._ready()
                return self.store.transition(ticket_id, token, "RUNNING")
            except Exception as error:
                self._fail(ticket_id, token, error)
                raise

    def guard(self, ticket_id, token):
        ticket = self.store.validate(ticket_id, token)
        if ticket["phase"] != "RUNNING" or self.git.head() != ticket["input_sha"]:
            raise BrokerError("MCP доступ требует своего RUNNING ticket и входного SHA")
        snapshot = self._ready(clean=False, allow_compile_errors=True, allow_busy=True)
        if snapshot.get("session_id") != ticket.get("details", {}).get("session_id"):
            error = BrokerError("Editor перезапущен; старая аренда требует recovery")
            self._fail(ticket_id, token, error)
            raise error
        return {"ticket_id": ticket_id, "epoch": ticket["epoch"],
                "project_root": str(self.editor_root), "input_sha": ticket["input_sha"],
                "session_id": snapshot.get("session_id"), "process_id": snapshot.get("process_id"),
                "ready": snapshot.get("ready"), "is_playing": snapshot.get("is_playing"),
                "is_compiling": snapshot.get("is_compiling"), "is_updating": snapshot.get("is_updating"),
                "is_test_running": snapshot.get("is_test_running")}

    def _artifacts(self, ticket_id, paths):
        manifest = []
        for relative in paths:
            path = Path(relative)
            if path.is_absolute() or ".." in path.parts or ":" in str(path):
                raise BrokerError("Артефакт должен быть относительным файлом внутри worker")
            source = self.editor_root / path
            if not source.is_file() or source.is_symlink() or not source.resolve().is_relative_to(self.editor_root):
                raise BrokerError("Не найден безопасный файл артефакта: " + str(path))
            if path.parts[0].casefold() in (".git", ".plastic", "library"):
                raise BrokerError("Кэши и VCS не являются выходными артефактами")
            content = source.read_bytes()
            destination = self.state / "artifacts" / str(ticket_id) / path
            destination.parent.mkdir(parents=True, exist_ok=True)
            with destination.open("xb") as stream:
                stream.write(content)
                stream.flush()
                os.fsync(stream.fileno())
            manifest.append({"path": path.as_posix(), "stored_path": str(destination),
                             "sha256": hashlib.sha256(content).hexdigest(), "bytes": len(content)})
        return manifest

    def _restore(self, ticket_id, token, captured, original_setup, delivery=None):
        delivery = delivery or captured
        self.store.transition(ticket_id, token, "RESTORING", result=delivery, recovery_snapshot=captured)
        self._ready(allow_compile_errors=True)
        self._unity_call(ticket_id, token, "park", role="cleanup_park")
        self.git.restore_captured(captured["sha"], self.config["baseline_sha"])
        self.store.transition(ticket_id, token, "RESTORING", disk_restored=True)
        self._unity_call(ticket_id, token, "refresh")
        if original_setup:
            self._unity_call(ticket_id, token, "restore", original_setup, role="restore_original")
        self._ready()
        if self.git.head() != self.config["baseline_sha"] or not self.git.is_clean():
            raise BrokerError("Возврат базы изменил файлы; новые участники заблокированы")
        return self.store.complete(ticket_id, token, result_sha=delivery["sha"], restored=True)

    def finish(self, ticket_id, token, artifact_paths=()):
        with transaction_mutex(self.state):
            self._reload_config()
            ticket = self.store.validate(ticket_id, token)
            if ticket["phase"] != "RUNNING":
                raise BrokerError("finish требует RUNNING заявку")
            try:
                self._ready(clean=False)
                self.store.transition(ticket_id, token, "CAPTURING")
                self._unity_call(ticket_id, token, "save_outputs", ticket["output_roots"])
                captured = self.git.capture_result(self.state, ticket["input_sha"],
                                                  ticket["output_roots"], "result-" + str(ticket_id))
                # Ref R существует до любых операций отката; получатель может быть offline.
                self.store.transition(ticket_id, token, "CAPTURING", result=captured)
                artifacts = self._artifacts(ticket_id, artifact_paths)
                self.store.transition(ticket_id, token, "CAPTURING", artifacts=artifacts)
                self._restore(ticket_id, token, captured, ticket["details"].get("original_setup"))
                return {**captured, "ticket_id": ticket_id, "artifacts": artifacts}
            except Exception as error:
                self._fail(ticket_id, token, error)
                raise

    def _settle_pending(self, ticket):
        details = ticket.get("details", {})
        identifiers = [details.get("park_request_id"), details.get("pending_request_id")]
        for request_id in dict.fromkeys(identifier for identifier in identifiers if identifier):
            if not hasattr(self.adapter, "wait_response"):
                raise BrokerError("Нельзя подтвердить ранее отправленный Unity request")
            try:
                result = self.adapter.wait_response(request_id)
            except UnityOperationError as error:
                # Архив/ответ уже проверен адаптером: известный отказ завершён, не replay.
                result = error.result
            except TimeoutError:
                request = self.adapter.mailbox / "requests" / (request_id + ".json")
                journal = self.adapter.mailbox / "journals" / (request_id + ".json")
                if request.exists() or journal.exists():
                    raise
                # Процесс погиб между durable intent и публикацией request: отправки не было.
                result = None
            if request_id == details.get("park_request_id") and details.get("original_setup") is None:
                if isinstance(result, dict) and isinstance(result.get("scenes"), list):
                    self.store.transition(ticket["id"], ticket["token"], ticket["phase"], original_setup=result)
                elif result is not None or self.git.head() != ticket["base_sha"]:
                    raise BrokerError("Ответ park не содержит исходную конфигурацию сцен")

    def recover(self, ticket_id, token):
        with transaction_mutex(self.state):
            self._reload_config()
            ticket = self.store.get(ticket_id)
            if ticket["token"] != token or ticket["phase"] not in (
                    "LEASED", "SWITCHING", "RUNNING", "CAPTURING", "RECOVERY_REQUIRED", "RESTORING"):
                raise BrokerError("recover требует token остановленной транзакции")
            try:
                self.store.transition(ticket_id, token, "RESTORING")
                ticket = self.store.get(ticket_id)
                self._settle_pending(ticket)
                ticket = self.store.get(ticket_id)
                self._ready(allow_compile_errors=True)
                details = ticket.get("details", {})
                if self.git.head() == self.config["baseline_sha"] and self.git.is_clean():
                    self.store.transition(ticket_id, token, "RESTORING")
                    self._unity_call(ticket_id, token, "refresh")
                    if details.get("original_setup"):
                        self._unity_call(ticket_id, token, "restore", details["original_setup"], role="restore_original")
                    self._ready()
                    return self.store.complete(ticket_id, token,
                        result_sha=(details.get("result") or {}).get("sha"), restored=True)
                previous_result = details.get("result") or {}
                if self.git.head() not in (ticket["input_sha"], previous_result.get("sha")):
                    raise BrokerError("HEAD не совпадает ни с входом, ни с базой; нужен разбор журнала")
                captured = self.git.capture_result(self.state, ticket["input_sha"],
                                                  ticket["output_roots"], "recovery-" + str(ticket_id))
                # Автоматическое получение аварийного результата запрещено до его ревью.
                captured["accepted"] = False
                delivery = previous_result or captured
                self.store.transition(ticket_id, token, "RESTORING", result=delivery, recovery_snapshot=captured)
                return self._restore(ticket_id, token, captured, details.get("original_setup"), delivery=delivery)
            except Exception as error:
                self._fail(ticket_id, token, error)
                raise

    def recover_stopped(self, ticket_id, token):
        """Только дисковое восстановление после доказанной гибели worker, без выдачи аренды."""
        with transaction_mutex(self.state):
            self._reload_config()
            ticket = self.store.get(ticket_id)
            if ticket["token"] != token or ticket["phase"] not in ("RECOVERY_REQUIRED", "RESTORING"):
                raise BrokerError("Дисковое восстановление требует token аварийной транзакции")
            details = ticket.get("details", {})
            if process_alive(details.get("process_id")):
                raise BrokerError("Редактор ещё работает; менять файлы под ним запрещено")
            heartbeat = self.state / "unity" / "heartbeat.json"
            if heartbeat.is_file():
                current = json.loads(heartbeat.read_text(encoding="utf-8-sig"))
                pid = current.get("process_id")
                if pid and pid != details.get("process_id") and process_alive(pid):
                    raise BrokerError("Worker уже перезапущен; требуется обычный recover")
            block = self.state / "unity" / "recovery-block.json"
            atomic_json(block, {"ticket_id": ticket_id, "reason": "disk recovery"})
            try:
                # Отменяем осиротевшие запросы при доказанно мёртвом процессе, сохраняя их.
                requests = self.state / "unity" / "requests"
                responses = self.state / "unity" / "responses"
                for request in requests.glob("*.json"):
                    response = responses / request.name
                    if not response.exists():
                        atomic_json(response, {"request_id": request.stem,
                            "project_root": str(self.editor_root), "ok": False,
                            "error": "Отменён после доказанной остановки editor process"})
                delivery = details.get("result")
                captured = delivery
                head = self.git.head()
                if head != self.config["baseline_sha"] or not self.git.is_clean():
                    if head not in (ticket["input_sha"], (captured or {}).get("sha")):
                        raise BrokerError("HEAD изменён вне контроллера; автоматический откат запрещён")
                    captured = self.git.capture_result(self.state, ticket["input_sha"],
                        ticket["output_roots"], "stopped-recovery-" + str(ticket_id))
                    captured["accepted"] = False
                    delivery = delivery or captured
                    self.store.transition(ticket_id, token, "RESTORING", result=delivery, recovery_snapshot=captured)
                    self.git.restore_captured(captured["sha"], self.config["baseline_sha"])
                self.store.transition(ticket_id, token, "RECOVERY_REQUIRED", disk_restored=True,
                                      pending_request_id=None,
                                      failure="База на диске возвращена; запустить worker и выполнить recover")
                return {"ticket_id": ticket_id, "disk_restored": True, "editor_released": False,
                        "result": delivery}
            finally:
                # Bridge после запуска увидит cancellation responses, не исполнит старые команды.
                block.unlink(missing_ok=True)

    def receive(self, ticket_id, owner, agent_root):
        receiver = hashlib.sha256(canonical(agent_root).encode("utf-8")).hexdigest()
        with transaction_mutex(self.state / "receivers" / receiver):
            return self._receive(ticket_id, owner, agent_root)

    def _receive(self, ticket_id, owner, agent_root):
        ticket = self.store.get(ticket_id)
        if ticket["owner"] != owner or ticket["phase"] != "DONE":
            raise BrokerError("Результат принадлежит другому владельцу или ещё не завершён")
        result = ticket.get("details", {}).get("result")
        if not result or not result.get("accepted"):
            raise BrokerError("Результат отсутствует либо требует ревью неожиданных/аварийных изменений")
        if canonical(agent_root) != canonical(ticket["agent_root"]):
            raise BrokerError("Получатель должен использовать свой зарегистрированный worktree")
        source = GitState(agent_root)
        if canonical(source.common_dir()) != canonical(self.git.common_dir()):
            raise BrokerError("Получатель принадлежит другому репозиторию")
        received = source.receive_result(ticket["input_sha"], result["sha"])
        self.store.ack_result(ticket_id, owner)
        return received

    def publish_base(self, sha, source_root):
        """Явная публикация уже принятого состояния, только при пустой очереди."""
        sha = exact_sha(sha)
        with transaction_mutex(self.state):
            self.config = json.loads((self.state / "config.json").read_text(encoding="utf-8"))
            source = GitState(source_root)
            if canonical(source.common_dir()) != canonical(self.git.common_dir()):
                raise BrokerError("Новая база принадлежит другому репозиторию")
            if not source.is_ancestor(self.config["baseline_sha"], sha):
                raise BrokerError("Новая база должна включать опубликованную прежнюю базу")
            status = self.store.status()
            if status.get("active") or status.get("offered") or any(
                    item["phase"] == "QUEUED" for item in status.get("tickets", [])):
                raise BrokerError("Сначала завершить или отменить все ожидающие и активные заявки")
            if self.git.head() != self.config["baseline_sha"] or not self.git.is_clean():
                raise BrokerError("Публикация требует чистый worker на текущей базе")
            snapshot = self._ready()
            old_base = self.config["baseline_sha"]
            ticket = self.store.enqueue("_broker-maintenance", old_base, sha, str(Path(source_root).resolve()),
                                         [], "publish-" + uuid.uuid4().hex)
            ticket = self.store.claim(ticket["id"], "_broker-maintenance")
            ticket_id, token = ticket["id"], ticket["token"]
            try:
                self.store.transition(ticket_id, token, "SWITCHING", original_setup=self._snapshot_setup(snapshot),
                    process_id=snapshot.get("process_id"), session_id=snapshot.get("session_id"),
                    publish_sha=sha, previous_base_sha=old_base)
                setup = self._unity_call(ticket_id, token, "park", role="original_park")
                self.store.transition(ticket_id, token, "SWITCHING", original_setup=setup)
                self.git.switch_detached(sha)
                self._unity_call(ticket_id, token, "refresh")
                self._ready()
                self._unity_call(ticket_id, token, "restore", setup, role="restore_original")
                self._ready()
                if self.git.head() != sha or not self.git.is_clean():
                    raise BrokerError("Новая база вызвала незапланированные изменения")
                self.store.transition(ticket_id, token, "RESTORING", disk_restored=True)
                self.config["baseline_sha"] = sha
                atomic_json(self.state / "config.json", self.config)
                self.store.complete(ticket_id, token, restored=True)
                return {"baseline_sha": sha, "previous_base_sha": old_base, "ticket_id": ticket_id}
            except Exception as error:
                self._fail(ticket_id, token, error)
                # Для аварийного maintenance token виден только локальному оператору.
                raise BrokerError(f"publish-base failed; recovery ticket={ticket_id}, token={token}: {error}") from error
