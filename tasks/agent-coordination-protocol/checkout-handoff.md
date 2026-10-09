# Перенос рабочего checkout сопровождающего

Дата: 2026-10-09. Разрешение: пользователь поручил создать worktree и продолжить
эту же сессию оттуда. Роль сопровождающего worker и agent-infra уже назначена пользователем.

Новый checkout: F:/UnityProjects/Vr_Battlegrounds_ai/.agent-state/worktrees/agent-coordination-protocol.
Ветка: codex/agent-coordination-protocol. Это отдельный linked Git worktree;
папка .agent-state исключена из продуктового Git. Основной checkout больше не является
местом разработки задачи. Общий хаб и runtime остаются прежними через git-common-dir.
Исходники agent-infra остаются отдельным репозиторием F:/UnityProjects/agent-infra;
его изменения вести в изолированных source worktree, развёртывать проверенный checkpoint.

Незакоммиченные прежние материалы основного checkout не удалять и не переносить:
они остаются в исходном месте. Исторические reports/source-worktrees остаются там же;
новые артефакты писать в tasks/agent-coordination-protocol нового checkout.

Перед передачей: docs-only checkpoint правила infra_issue публикуется обычным
verify/accept/merge-request/merge-execute. Подготовить новый Readme/plan с новым
worktree, проверить общий Git dir, owner, task-id и неизменность specs/истории этапов.
Сопровождающий выполняет собственный административный handoff через транзакцию
CoordinationStore после завершения своих этапов, с ожидаемой ревизией и событием.
Чужие записи, контракты и ACK не меняются. Это разовая административная операция,
не новый способ обычным агентам обходить register/ownership.

После перезапуска: проверить git-dir != git-common-dir, читать свой inbox/status;
проверить локальный plan и регистрацию, обновить native route из свежего discovery.
В этом клиенте штатный Monitor процесса с доставкой в контекст не обнаружен;
поэтому действуют обязательные проверки и штатные уведомления, без фонового daemon.
Сопровождение хаба не превращает linked checkout в аренду Unity worker:
доступ к общей инфраструктуре разрешён ролью, продуктовые Unity-операции идут по AGENTS.md.
