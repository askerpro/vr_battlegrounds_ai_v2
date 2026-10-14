# Детали адаптации

Дата: 2026-10-14.

Источник: `.claude/agents/manipulation-expert.md`. Образцы формата: `.codex/agents/weapon-system-expert.toml` и `.codex/agents/avatar-grip-expert.toml`.

Сохранены ответственность за хват, две руки, якоря, карманы, размещение, отпускание, лежащие предметы, сетевые взаимодействия, HapticService и InteractionFeedback; русский язык общения; загрузка пакета рабочего места `experts/manipulation`.

Поля Claude `model: opus` и `memory: project` не переносятся как настройки Codex. Доменный контекст остаётся в существующем рабочем месте эксперта; модель, разрешения и MCP наследуются. Для субагента ведущая сессия отвечает за статус задачи, интеграцию и пользовательскую приёмку.

Официальная схема: https://learn.chatgpt.com/docs/agent-configuration/subagents — проектные определения `.codex/agents/*.toml`, обязательные поля `name`, `description`, `developer_instructions`.

Проверка определения и пакета не подтверждает запуск отдельной сессии Codex. Unity, игровые исходники, ассеты и существующие задачи эксперта не меняются.
