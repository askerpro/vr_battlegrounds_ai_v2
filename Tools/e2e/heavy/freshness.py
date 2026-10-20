"""Свежесть проверяемого кода относительно origin/dev (решение пользователя 2026-10-20).

Регрессию вносят только игровые пути: если origin/dev ушла вперёд лишь документами, tasks или Tools,
прогон остаётся доказательным (предупреждение); игровые изменения dev, которых нет в коде, — FAIL.
"""
from pathlib import Path

from . import verdict as V

GAME_PREFIXES = ("Assets/", "Packages/", "ProjectSettings/")
MAX_PATHS = 5


def drift(git, head, origin):
    """git(*args) → CompletedProcess в нужном репозитории. Возвращает словарь сравнения head с origin."""
    if not head or not origin:
        return {"known": False, "error": "нет HEAD или origin/dev"}
    contains = git("merge-base", "--is-ancestor", origin, head)
    if contains.returncode not in (0, 1):
        return {"known": False, "error": (contains.stderr or "").strip()[:300]}
    if contains.returncode == 0:
        return {"known": True, "contains": True, "behind": 0, "gamePaths": []}
    behind = git("rev-list", "--count", head + ".." + origin)
    changed = git("diff", "--name-only", head + "..." + origin)
    if behind.returncode or changed.returncode:
        return {"known": False, "error": ((behind.stderr or "") + (changed.stderr or "")).strip()[:300]}
    paths = [p for p in changed.stdout.splitlines() if p.startswith(GAME_PREFIXES)]
    return {"known": True, "contains": False, "behind": int(behind.stdout.strip() or 0), "gamePaths": paths}


def snapshot_tree(git, index_copy):
    """Tree текущих файлов без изменения HEAD и индекса: копия индекса (со stat-кешем, перехешируются
    только изменённые файлы) + add -A + write-tree. git(*args, env=...)."""
    index = git("rev-parse", "--path-format=absolute", "--git-path", "index").stdout.strip()
    if not index:
        return None, "не найден индекс"
    try:
        index_copy.write_bytes(Path(index).read_bytes())
    except OSError as error:
        return None, "копия индекса: " + str(error)
    env = {"GIT_INDEX_FILE": str(index_copy)}
    # Весь checkout: pathspec отсутствующего каталога — ошибка add; сравнение и так только по игровым путям.
    added = git("add", "-A", env=env)
    if added.returncode:
        return None, "git add: " + (added.stderr or "").strip()[:300]
    tree = git("write-tree", env=env)
    if tree.returncode:
        return None, "git write-tree: " + (tree.stderr or "").strip()[:300]
    return tree.stdout.strip(), None


def code_identity(git, worker_head, local_tree):
    """Игровые пути, которыми код worker отличается от текущих файлов ветки."""
    if not worker_head or not local_tree:
        return {"known": False, "error": "нет HEAD worker или снимка файлов"}
    diff = git("diff", "--name-only", worker_head, local_tree, "--", *[p.rstrip("/") for p in GAME_PREFIXES])
    if diff.returncode:
        return {"known": False, "error": (diff.stderr or "").strip()[:300]}
    return {"known": True, "paths": diff.stdout.splitlines()}


def identity_check(info, worker_head):
    name = "Worker прогнал текущие файлы ветки (игровые пути)"
    if not info.get("known"):
        return {"name": name, "verdict": V.INCONCLUSIVE, "detail": str(info.get("error"))}
    if not info["paths"]:
        return {"name": name, "verdict": V.PASS, "detail": "код worker " + str(worker_head)[:12]}
    shown = ", ".join(info["paths"][:MAX_PATHS]) + (" …" if len(info["paths"]) > MAX_PATHS else "")
    return {"name": name, "verdict": V.INCONCLUSIVE, "detail": "отличаются " + str(len(info["paths"])) + ": " + shown
            + " — вердикт не о текущих файлах; checkpoint и аренда от текущего состояния"}


def check(name, info, head, origin):
    """Проверка вердикта по результату drift."""
    if not info.get("known"):
        return {"name": name, "verdict": V.INCONCLUSIVE, "detail": str(info.get("error"))}
    base = "код " + str(head)[:12] + ", origin/dev " + str(origin)[:12]
    if info["contains"]:
        return {"name": name, "verdict": V.PASS, "detail": base}
    if not info["gamePaths"]:
        return {"name": name, "verdict": V.PASS, "detail": base + "; dev впереди на " + str(info["behind"])
                + " коммит(ов) без игровых путей — перед merge-request rebase"}
    shown = ", ".join(info["gamePaths"][:MAX_PATHS]) + (" …" if len(info["gamePaths"]) > MAX_PATHS else "")
    return {"name": name, "verdict": V.FAIL, "detail": base + "; в dev игровые изменения, которых нет в коде ("
            + str(len(info["gamePaths"])) + "): " + shown + " — rebase и повтор"}
