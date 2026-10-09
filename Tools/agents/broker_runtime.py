"""Где лежит развёрнутый брокер редактора.

Исходники брокера — отдельный репозиторий F:/UnityProjects/agent-infra. Его deploy.py копирует
версию в <git-common-dir>/../.agent-state/editor-broker/runtime: каталог общий для всех worktree,
поэтому заглушки Tools/agents/*.py и инструменты проекта всегда работают на развёрнутой версии
без коммитов в продукт.
"""
import os
from pathlib import Path
import subprocess
import sys


def runtime_dir(start=None):
    """Runtime брокера для worktree, содержащего start (по умолчанию — этот файл)."""
    location = Path(start or __file__).resolve()
    folder = location if location.is_dir() else location.parent
    common = subprocess.check_output(["git", "-C", str(folder), "rev-parse", "--path-format=absolute",
                                      "--git-common-dir"], text=True,
                                      creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0).strip()
    return Path(common).parent / ".agent-state" / "editor-broker" / "runtime"


def require(start=None):
    runtime = runtime_dir(start)
    if not (runtime / "editor_broker").is_dir():
        raise RuntimeError(f"Брокер не развёрнут в {runtime}: python F:/UnityProjects/agent-infra/deploy.py "
                           "--project <worktree> (см. README agent-infra)")
    return runtime


def ensure_importable(start=None):
    """Сделать editor_broker (client_guard, git_state...) импортируемым из runtime."""
    runtime = str(require(start))
    if runtime not in sys.path:
        sys.path.insert(0, runtime)
    return runtime
