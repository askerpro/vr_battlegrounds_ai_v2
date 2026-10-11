# EOL PhaseB, inbox --unread и readonly gate

Сопровождающий с 2026-10-11 — Claude (задача agent-coordination-protocol), Codex исчерпал лимит.

Runtime source ce950de88a7d27b51b27a71515e7309af1afc864 (agent-infra master), развёрнут
штатным deploy; backup прежнего c72db11 — runtime-versions/before-e2cea1dcbbdb4f6f8b21a471fc1687ee.

Состав source:
- `inbox --unread`: выдаёт непрочитанное по id ASC и в той же транзакции помечает выданное
  прочитанным; бюджет вывода `--max-bytes` 20000, `--limit` 50; актуальные контрактные и
  stage-уведомления выдаются сообщениями, прочая служебная история — `pagination.system_read`.
  Inbox — нотификации, не ACK; `ack-events` остаётся совместимостью.
- Readonly gate: Codex-форма `Get-Content <path>`/`-Raw` без permit; узкий `git fetch
  [<remote> [<branch>]]` до register с проверкой config/hooks; закрыты инъекции через Git Bash
  `\"` и типографские кавычки PowerShell; аварийный режим считает неразбираемый git чужим target.
- Register: ошибка называет ожидаемый `tasks/<id>/Readme.md`; `state_may_have_changed=false`
  для ошибок до записи; hint про escalation при readonly database в песочнице.
- EOL-защиты broker: отказ на неканонический вход A на request/begin до аренды; integrity
  всех serialized binary `.asset`; утилита `text_eol_rematerialize.py` (dry-run по умолчанию,
  CAS, `add --refresh`); `text_eol_check.py --range/--pre-push`.

Продукт (этот этап): PhaseB `.gitattributes` — Unity YAML, ProjectSettings, Packages manifest
и `tasks/**/*.md|json` в `eol=lf`; 36 serialized binary `.asset` — `binary`; мёртвые правила
env_packs удалены; зеркальный `.editorconfig`; read-only EOL-проверка в `.githooks/pre-push`;
launcher перематериализации; инструкции inbox/register/escalation.

Проверки: agent-infra полный набор 617 OK (skip12); два независимых ревью CLOSED (найденные P1
закрыты до выпуска). Индекс i/crlf=0 до и после; все 13 632 пути со сменой атрибутов дают тот же
blob (hash-object --path), LFS 1685 путей без изменений. Live: MCP initialize 1,55 с/48 tools,
`inbox --unread` на общем хабе. Игровая приёмка не выполнялась.
