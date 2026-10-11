# Этап expert-registration влит: именованный Codex-агент avatar-grip-expert подключается в checkout

Коммит `0c1a7fbcc85d36235e4f2f9a2ab4f8b2bdf4653f` (2026-10-10) добавил
`.codex/agents/avatar-grip-expert.toml` и раздел «Именованный агент проекта» в `launch.md`; план
аудита и порядок этапов — `369bcb8d7e34f18a585d9a185942b179f891c41e` (2026-10-10).

Проверено (launch.md): Codex CLI 0.163.0-alpha.2, read-only ephemeral app-server создал дочерний поток
с `agentRole=avatar-grip-expert`, родительский turn `completed`; ребёнок назвал роль и стартовые
источники из конфигурации. Evidence `tasks/hand-rig-quality/reports/expert-runtime-summary.json` —
локальный, вне Git. Unity и аудит аватара не запускались.

Следом в хабе — `mef-grip-audit` (running, другая сессия).

Источники: `git show 0c1a7fbc`; `tasks/hand-rig-quality/expert/launch.md`; `tasks/hand-rig-quality/Readme.md` «Состояние».
