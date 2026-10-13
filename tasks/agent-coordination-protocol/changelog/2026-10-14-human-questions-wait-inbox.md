# Вопросы человеку через AskUserQuestion, wait-inbox и отметка inbox с `--as`

Долгоживущие сессии экспертов спрашивали пользователя обычным текстом примерно в 15 раз чаще,
чем через AskUserQuestion; такой вопрос тонул среди пробуждений Monitor и уведомлений субагентов
(до 44 пробуждений и 20 ч до ответа). Пока висит AskUserQuestion, ход не завершён и пробуждения
встают в очередь. Решение пользователя: Stop-хук возвращает агента, на доске полоса «Ждут вашего
ответа», уведомления ОС не нужны.

Runtime source agent-infra master eb4ab1d (`agent_coordination/human_questions.py`, CLI
`questions`, полоса доски, `wait-inbox`, режим `inbox-binding` warn|enforce). Описание кандидата —
agent-infra `docs/product-candidate-human-questions.md`.

Продукт (этот коммит):
- `Tools/agents/coordination-hook.py` — канонический launcher agent-infra (blob 3b4696ddcee6,
  прежний 5fd654917c1c): события вопросов (Stop, UserPromptSubmit, SessionStart, SessionEnd,
  AskUserQuestion) передаются runtime при любом mode, только если в нём есть
  `human_questions.py`; наружу — только проверенное решение Stop `{"decision":"block"}`; любой
  сбой — exit 0 без вывода. PreToolUse — прежним путём (off-shortcut, gate при enforced).
- `.claude/settings.json` (blob bee74d1c8433 от f4907803d851): matcher AskUserQuestion в
  PreToolUse/PostToolUse/PostToolUseFailure, SessionStart `startup|resume|clear`, UserPromptSubmit,
  Stop, SessionEnd; прежние хуки сохранены.
- Правила: `AGENTS.md`, `.agents/rules/agent_coordination.md` (раздел «Штатный Monitor inbox»
  заменён разделами «Inbox и ожидание событий» и «Вопросы пользователю»),
  `.agents/rules/project-workflows.md`, `experts/COMMON.md`: `inbox --unread --as "<адрес>"`,
  ожидание событий фоновой командой `wait-inbox`, `watch-inbox` устарел, без будильников и
  Monitor-циклов; вопросы пользователю только через AskUserQuestion.

Порядок выпуска: сначала штатный deploy runtime eb4ab1d (до него `wait-inbox` и `--as` в общем
runtime отсутствуют, на 2026-10-14 развёрнут fc30065), затем этот коммит через merge в `origin/dev`,
затем локальный rebase владельцев. Новый launcher со старым runtime безопасен: события вопросов в
runtime не передаются. Settings без нового launcher-а не выпускать.

Проверки: `json.tool` settings OK; Tools/agents/tests 15 OK, Tools/experts/tests 18 OK; smoke
launcher (PreToolUse при mode=off, мусорный вход) — exit 0 без вывода; `git diff --check` и
`check_text_eol.py --staged` чисто. Проверка клиента (новая сессия Claude Code эксперта: Stop
возвращает ход на «Делать?», записи `questions --open`) — после выпуска. Игровая приёмка не требуется.
