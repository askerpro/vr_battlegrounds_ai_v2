# Запуск эксперта по хватам

Роль: [avatar-grip-expert.md](avatar-grip-expert.md). База знаний:
[analysis.md](analysis.md), [sources.md](sources.md). Передача: [handoff.md](handoff.md).

## Именованный агент проекта

Конфигурация: [.codex/agents/avatar-grip-expert.toml](../../../.codex/agents/avatar-grip-expert.toml).
Codex обнаруживает её в checkout проекта как агента `avatar-grip-expert`;
это отдельный механизм от CLI-профиля ниже. Файл задаёт name, description и
developer_instructions. Модель, effort, MCP, sandbox и approvals наследуются
от вызывающей сессии; роль не расширяет права и не выдаёт Unity-аренду.

Пример поручения principal: «Привлеки avatar-grip-expert для аудита хвата MEF
на планшете; только исследование, верни измерения/ограничения и пути доказательств».
Выбор модели/effort и область работы задаются по AGENTS.md. Подагент не присваивает
владение задачей и не публикует route principal как собственный.

После получения этой версии origin/dev запускайте новую сессию Codex в своём
обновлённом checkout. Старые сессии не считать проверенными на перечитывание
новых конфигураций. При проверке различать обнаружение имени, загрузку инструкций
дочерней сессии и выполнение предметного аудита.

Формат: [OpenAI Docs — Custom agents](https://learn.chatgpt.com/docs/agent-configuration/subagents#custom-agents).

Проверка 2026-10-10 на Codex CLI0.163.0-alpha.2: отдельный read-only ephemeral
app-server создал дочерний поток с `agentRole=avatar-grip-expert`; родительский turn
завершился `completed`. Ребёнку не копировали предметные инструкции: он назвал
роль, стартовые источники и ограничения из конфигурации. Native-метаданные —
в локальном [reports/expert-runtime-summary.json](../reports/expert-runtime-summary.json).
Это проверка подключения и инструкций, без запуска Unity или аудита аватара.
Офлайн debug prompt-input и config/read сами по себе каталог ролей не доказывают.

## Отдельный CLI-профиль

Профиль для Codex CLI0.163.0-alpha.2: [avatar-grip-expert.config.toml](avatar-grip-expert.config.toml).
Он задаёт только дополнительные предметные инструкции; модель, MCP, sandbox и approvals
наследуются от действующей конфигурации. Глобальные и проектные AGENTS остаются обязательными.

Профиль устанавливается отдельным файлом `<CODEX_HOME>/avatar-grip-expert.config.toml`
(по умолчанию `C:/Users/asker/.codex/avatar-grip-expert.config.toml`). При существующем
отличающемся профиле сначала сохранить его, не перезаписывать автоматически.

```powershell
codex.cmd -C "F:\CodexWorktrees\hands-rig-quality\Vr_Battlegrounds_ai" -p avatar-grip-expert
```

Новая сессия должна назвать роль, реальные доступные инструменты анализа, ограничения
текущих отчётов и следующий этап. Она проверяет привязку checkout, обновляет собственный
native route по свежему listing и читает inbox; старый principal к этому моменту завершает
допуск. Один checkout — один главный писатель. Роль сама не выдаёт допуск на изменение ассетов.

Для новой задачи через native create_thread роль загружается стартовым указанием прочитать
avatar-grip-expert.md и handoff.md. Такой запуск не выбирает CLI-профиль через `-p`;
роль загружается из файла, а существующая конфигурация клиента сохраняется.

Формат отдельных профилей и developer_instructions проверен по официальной документации:
[Profiles](https://learn.chatgpt.com/docs/config-file/config-advanced#profiles),
[Configuration Reference](https://learn.chatgpt.com/docs/config-file/config-reference).
Не использовать устаревший `[profiles.avatar-grip-expert]` в общем config.toml.

Локальный профиль установлен в C:/Users/asker/.codex/avatar-grip-expert.config.toml.
validate_profile.py подтвердил TOML, набор ключей и точное совпадение с шаблоном.
codex.cmd -p avatar-grip-expert --help завершился с кодом0; это проверка CLI, не проверка
содержимого модельного контекста. Используем .cmd: PowerShell блокирует codex.ps1.
