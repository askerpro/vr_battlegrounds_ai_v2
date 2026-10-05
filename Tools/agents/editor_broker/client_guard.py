"""Общий допуск файловых клиентов: изолированный checkout или capability редактора.

Main checkout запрещён всегда. Offline bootstrap пакета допускается только явно;
снимок процессов доказывает отсутствие редактора в момент проверки, а не запрет
его последующего запуска человеком. Клиенты повторяют проверку перед записью.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import shlex
import subprocess
import sys
import time

if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from editor_broker.git_state import GitState
from editor_broker.service import canonical, default_state_dir, process_alive


def project_path_from_command(command):
    """Пустая/неопределённая identity Unity не является доказательством offline."""
    if not isinstance(command, str) or not command.strip():
        raise ValueError("Не удалось прочитать commandline Unity process")
    if os.name == "nt":
        import ctypes
        from ctypes import wintypes
        shell = ctypes.WinDLL("shell32", use_last_error=True)
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        shell.CommandLineToArgvW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(ctypes.c_int)]
        shell.CommandLineToArgvW.restype = ctypes.POINTER(wintypes.LPWSTR)
        kernel.LocalFree.argtypes = [wintypes.HLOCAL]
        kernel.LocalFree.restype = wintypes.HLOCAL
        count = ctypes.c_int()
        values = shell.CommandLineToArgvW(command, ctypes.byref(count))
        if not values:
            raise ValueError("Не удалось разобрать commandline Unity")
        try:
            arguments = [values[index] for index in range(count.value)]
        finally:
            kernel.LocalFree(values)
    else:
        arguments = shlex.split(command)
    indices = [index for index, value in enumerate(arguments) if value.casefold() == "-projectpath"]
    if len(indices) != 1 or indices[0] + 1 == len(arguments):
        raise ValueError("Unity process без однозначного -projectPath; offline не доказан")
    path = Path(arguments[indices[0] + 1])
    if not path.is_absolute():
        raise ValueError("Относительный projectPath Unity; offline не доказан")
    return path.resolve()


def unity_process_rows(pid=None):
    query = "Name='Unity.exe'"
    if pid is not None:
        if not isinstance(pid, int) or pid <= 0:
            raise ValueError("CIM не сообщил корректный PID Unity")
        query += " AND ProcessId=" + str(pid)
    script = ("$ErrorActionPreference='Stop'; "
              "[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false); "
              "$rows=@(Get-CimInstance Win32_Process -Filter \"" + query + "\" "
              "| Select-Object ProcessId,CommandLine); ConvertTo-Json -InputObject $rows -Compress")
    result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script],
                            capture_output=True, encoding="utf-8", errors="replace", timeout=10)
    if result.returncode:
        raise ValueError("CIM не подтвердил состояние Unity process; запись запрещена")
    try:
        rows = json.loads(result.stdout.lstrip("\ufeff"))
        if not isinstance(rows, list) or any(not isinstance(row, dict) for row in rows):
            raise ValueError("Неожиданный CIM process inventory")
        return rows
    except (json.JSONDecodeError, AttributeError, TypeError) as error:
        raise ValueError("Некорректный CIM process inventory; запись запрещена") from error


def unity_project_roots():
    """Read-only process inventory; никогда не завершает и не закрывает Unity."""
    if os.name == "nt":
        roots = []
        for row in unity_process_rows():
            pid, command = row.get("ProcessId"), row.get("CommandLine")
            # CIM может захватить уже исчезающий процесс при старте ImportWorker.
            for attempt in range(3):
                if isinstance(command, str) and command.strip():
                    roots.append(project_path_from_command(command))
                    break
                if not process_alive(pid):
                    break
                if attempt == 2:
                    raise ValueError("Живой Unity PID без commandline; offline не доказан")
                time.sleep(0.1)
                refreshed = unity_process_rows(pid)
                if refreshed and (len(refreshed) != 1 or refreshed[0].get("ProcessId") != pid):
                    raise ValueError("CIM вернул другую identity процесса")
                command = refreshed[0].get("CommandLine") if refreshed else None
        return roots
    if sys.platform.startswith("linux"):
        roots = []
        for process in Path("/proc").iterdir():
            if not process.name.isdigit():
                continue
            try:
                name = (process / "comm").read_text().strip()
                if name.casefold() not in ("unity", "unity.exe"):
                    continue
                args = (process / "cmdline").read_bytes().decode().split("\0")
                roots.append(project_path_from_command(shlex.join([arg for arg in args if arg])))
            except FileNotFoundError:
                continue
            except (OSError, UnicodeError) as error:
                raise ValueError("Не удалось определить проект процесса; offline не доказан") from error
        return roots
    raise ValueError("Нет проверяемого process inventory на этой платформе")


def broker_command(root, state, command, ticket=None, token=None):
    runtime = state / "runtime/editor-broker.py"
    script = runtime if runtime.is_file() else Path(__file__).resolve().parents[1] / "editor-broker.py"
    arguments = [sys.executable, str(script), "--repo", str(root), "--state-dir", str(state), command]
    if ticket is not None:
        arguments.extend(["--ticket", str(ticket), "--token", token])
    result = subprocess.run(arguments, capture_output=True, encoding="utf-8", errors="replace", timeout=30)
    try:
        reply = json.loads(result.stdout)
    except json.JSONDecodeError as error:
        raise ValueError("Контроллер не подтвердил доступ") from error
    if result.returncode or not reply.get("ok") or not isinstance(reply.get("result"), dict):
        raise ValueError("Контроллер отклонил доступ: " + str(reply.get("error", "guard failed")))
    return reply["result"]


class ClientGuard:
    def __init__(self, root, *, ticket=None, token=None, state_dir=None, offline_editor=False):
        self.root = Path(root).resolve()
        self.ticket, self.token = ticket, token
        self.offline_editor = offline_editor
        self.state_override = Path(state_dir).resolve() if state_dir else None

    def check(self):
        git = GitState(self.root)
        git_dir = git._git("rev-parse", "--absolute-git-dir").decode().strip()
        if not (self.root / ".git").is_file() or canonical(git_dir) == canonical(git.common_dir()):
            raise ValueError("Файловая запись в main checkout запрещена; нужен linked worktree")
        if bool(self.ticket) != bool(self.token) or (self.offline_editor and (self.ticket or self.token)):
            raise ValueError("Нужны ticket и token вместе; offline bootstrap исключает capability")
        default = default_state_dir(self.root)
        state = self.state_override or default
        if (self.state_override and canonical(state) != canonical(default)
                and (default / "config.json").is_file()):
            raise ValueError("Нельзя заменить зарегистрированный общий контроллер через state-dir")
        configs = []
        for location in dict.fromkeys([default, state]):
            config_path = location / "config.json"
            if config_path.exists():
                config = json.loads(config_path.read_text(encoding="utf-8"))
                if canonical(config.get("common_dir", "")) != canonical(git.common_dir()):
                    raise ValueError("Контроллер принадлежит другому Git repository")
                configs.append(config)
            elif location == state and self.state_override:
                raise ValueError("Явный state-dir не содержит инициализированного контроллера")
        if len({canonical(config["editor_root"]) for config in configs}) > 1:
            raise ValueError("Конфликт editor root между контроллерами")
        is_worker = any(canonical(config["editor_root"]) == canonical(self.root) for config in configs)
        if self.ticket or self.token:
            if not is_worker:
                raise ValueError("Capability разрешает только точный editor worker root")
            result = broker_command(self.root, state, "guard", self.ticket, self.token)
            if canonical(result.get("project_root", "")) != canonical(self.root):
                raise ValueError("Guard разрешил другой project root")
            return {"mode": "lease", "project_root": str(self.root)}
        if is_worker and not self.offline_editor:
            raise ValueError("Editor worker требует ticket/token или явный package bootstrap")
        if any(canonical(path) == canonical(self.root) for path in unity_project_roots()):
            raise ValueError("Unity process этого project root открыт; offline запись запрещена")
        if is_worker:
            status = broker_command(self.root, state, "status")
            if status.get("active") or status.get("offered") or any(
                    ticket.get("phase") == "QUEUED" for ticket in status.get("tickets", [])):
                raise ValueError("Package bootstrap запрещён при занятой очереди контроллера")
        return {"mode": "bootstrap" if self.offline_editor else "isolated", "project_root": str(self.root)}


def add_arguments(parser, *, bootstrap=False):
    parser.add_argument("--ticket")
    parser.add_argument("--token")
    parser.add_argument("--state-dir", type=Path)
    if bootstrap:
        parser.add_argument("--offline-editor", action="store_true")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, required=True)
    add_arguments(parser, bootstrap=True)
    args = parser.parse_args()
    try:
        result = ClientGuard(args.project, ticket=args.ticket, token=args.token,
                             state_dir=args.state_dir, offline_editor=args.offline_editor).check()
        print(json.dumps({"ok": True, "result": result}))
        return 0
    except Exception as error:
        print(json.dumps({"ok": False, "error": str(error)}, ensure_ascii=False))
        return 2


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main())
