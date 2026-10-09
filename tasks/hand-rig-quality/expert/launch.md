# Запуск эксперта по хватам

Роль: [avatar-grip-expert.md](avatar-grip-expert.md). База знаний:
[analysis.md](analysis.md), [sources.md](sources.md). Передача: [handoff.md](handoff.md).

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
