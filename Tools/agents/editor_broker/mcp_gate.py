"""Решение брокера по вызову Unity MCP: чтение свободно, изменение — только в своей аренде.

Защита от ошибок оркестрации, не от злоумышленника: все агенты работают под одним
пользователем и при желании могут обратиться к серверу MCP напрямую.
"""
from __future__ import annotations

import hashlib
from pathlib import Path

from .git_state import GitState
from .service import canonical

# Инструменты без побочных эффектов при любых аргументах.
READ_TOOLS = frozenset((
    "debug_request_context", "find_gameobjects", "find_in_file", "get_sha", "get_test_job",
    "manage_script_capabilities", "set_active_instance", "unity_docs", "unity_reflect",
    "validate_script",
))
# Смешанные инструменты: читающие action. Всё, чего здесь нет, считается изменением.
READ_ACTIONS = {
    "execute_code": frozenset(("get_history",)),
    "generate_audio": frozenset(("status", "list_providers")),
    "generate_image": frozenset(("status", "list_providers")),
    "generate_model": frozenset(("status", "list_providers")),
    "import_model": frozenset(("search", "preview", "status", "list_providers")),
    "manage_asset": frozenset(("search", "get_info", "get_components")),
    "manage_editor": frozenset(("telemetry_status", "telemetry_ping")),
    "manage_material": frozenset(("ping", "get_material_info")),
    "manage_prefabs": frozenset(("get_info", "get_hierarchy")),
    "manage_scene": frozenset(("get_hierarchy", "get_active", "get_build_settings",
                               "get_loaded_scenes", "validate")),
    "manage_tools": frozenset(("list_groups",)),
    "manage_ui": frozenset(("ping", "read", "get_visual_tree", "list")),
    "read_console": frozenset(("get",)),
}
PROTOCOL = "checkpoint → request → watch-ticket → claim → begin (Tools/agents/editor-broker.py)"


def classify(tool, arguments):
    if tool in READ_TOOLS:
        return "read"
    actions = READ_ACTIONS.get(tool)
    if actions is not None:
        action = (arguments or {}).get("action")
        # read_console без action по умолчанию читает журнал.
        if action in actions or (tool == "read_console" and action is None):
            return "read"
    return "write"


def instance_id(project_root):
    """Name@hash как в MCP for Unity: SHA1 от Application.dataPath, первые 16 символов."""
    data_path = Path(project_root).resolve().as_posix() + "/Assets"
    return Path(project_root).resolve().name + "@" + hashlib.sha1(data_path.encode("utf-8")).hexdigest()[:16]


def same_instance(target, instance):
    """Сервер принимает Name@hash либо префикс hash; сравниваем так же."""
    if not target:
        return False
    name, digest = instance.split("@", 1)
    value = str(target).strip()
    if "@" in value:
        return value.casefold() == instance.casefold()
    return len(value) >= 4 and digest.startswith(value.casefold())


def _is_main_checkout(root):
    return not (Path(root).resolve() / ".git").is_file()


def decide(broker, agent_root, tool, arguments=None, pinned=None):
    """Возвращает allow, unity_instance для подстановки и причину отказа."""
    arguments = arguments or {}
    kind = classify(tool, arguments)
    worker = instance_id(broker.editor_root)
    active = broker.store.status().get("active")
    own = bool(active and active["phase"] == "RUNNING" and
               canonical(active["agent_root"]) == canonical(agent_root))
    if own:
        if kind == "write":
            ticket = broker.store.get(active["id"])
            # guard сверяет HEAD входа и сессию Unity; при отказе вызов не уходит.
            broker.guard(ticket["id"], ticket["token"])
        # В своей аренде всё направляется в worker, даже если сессия закреплена иначе.
        return {"allow": True, "kind": kind, "unity_instance": worker, "ticket_id": active["id"]}
    if kind == "read":
        return {"allow": True, "kind": kind, "unity_instance": None}
    target = arguments.get("unity_instance") or pinned
    if _is_main_checkout(agent_root):
        # Сессия человека в основном checkout: свой Unity по умолчанию, worker — только через аренду.
        target = target or instance_id(GitState(agent_root).root)
        if not same_instance(target, worker):
            return {"allow": True, "kind": kind, "unity_instance": target}
    holder = (" Сейчас редактор у «" + active["owner"] + "» (" + active["phase"] + ").") if active else ""
    return {"allow": False, "kind": kind, "unity_instance": None,
            "reason": "Брокер: «" + tool + "» изменяет Unity и требует своей аренды в фазе RUNNING." +
                      holder + " Порядок: " + PROTOCOL + ". Чтение доступно без аренды."}
