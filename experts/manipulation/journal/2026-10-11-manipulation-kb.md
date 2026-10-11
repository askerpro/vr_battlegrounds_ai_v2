# База знаний манипуляций перенесена в Docs/manipulation; профиль эксперта — адаптер awake

`081ec6777d6b47015c317c3a58ae296adcc32693` в origin/dev (хаб: этап `haptics-system/manipulation-kb` merged,
публикация базы worker — тикет 411). Только документация; код и ассеты не менялись.

- `architecture`, `decisions`, `invariants`, `sources` — из `tasks/haptics-system/expert/` в `Docs/manipulation/`;
  `handoff.md` — в `tasks/haptics-system/`.
- Инварианты сгруппированы (хват, якоря и карманы, сеть, SDK, отклик, вибрация), 29 пунктов вместо 15; номера
  сместились (патч 26 — п. 14, патч 67 — п. 16, один писатель мотора — п. 25).
- `.claude/agents/manipulation-expert.md` заменён адаптером из `tasks/expert-workspace-infra/handoff/manipulation-expert.md`
  (запросы expert-workspace-infra 3272/3279); доменные правила — в `experts/manipulation/AGENT.md`.
- Ссылки `AGENT.md` и `backlog.md` перенаправлены на `Docs/manipulation/`; `reviewed_sha` — `081ec677`.

Проверка: относительные ссылки и старые пути в `Docs/manipulation/*.md`, Readme и handoff задачи — 0 проблем
(`tasks/haptics-system/reports/2026-10-11-manipulation-kb-links.txt`, вне Git); CR в staged blobs — 0.
