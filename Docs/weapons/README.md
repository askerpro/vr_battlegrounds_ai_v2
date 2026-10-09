# Оружейная система — база знаний

Владелец — задача `weapon-system` (эксперт `weapon-system-expert`). Создана 2026-10-09 этапом `expert-kb`.
Эксперт запускается главной сессией: `claude --agent weapon-system-expert` (профиль —
[`.claude/agents/weapon-system-expert.md`](../../.claude/agents/weapon-system-expert.md)). Документы обновляются в
том же этапе, что меняет архитектуру; новое решение пользователя — строка в [decisions.md](decisions.md).

| Документ | О чём | Когда читать |
|---|---|---|
| [architecture.md](architecture.md) | Слои, типы, каналы и их владельцы, сеть, данные, авторинг | Любая задача по оружию |
| [invariants.md](invariants.md) | Правила, которые нельзя нарушать, и чем они закреплены | Перед проектированием и ревью |
| [decisions.md](decisions.md) | Решения пользователя с датами | Перед любым вопросом пользователю: ответ может уже быть |
| [vision.md](vision.md) | Куда идёт система, что считается «готово» | Оценка новой задачи |
| [roadmap.md](roadmap.md) | Очередь этапов и открытые развилки | Планирование |
| [workflow.md](workflow.md) | Как эксперт ведёт задачу: координация, Unity, делегирование, приёмка | Каждая задача |
| [sight-calibration.md](sight-calibration.md) | Калибровка прицелов: модель zero и формула смещения, защита ShotSource, ручной стенд, проверки, история 2026-10-05, открытые вопросы | Задачи по прицелам и ShotSource |
| [weapon-system-expert.md](../../.claude/agents/weapon-system-expert.md) | Профиль агента-эксперта | Запуск и правка эксперта |

Источники истины в коде: `Assets/Scripts/Weapons/Core/` (машина, таблица переходов),
`Assets/Scripts/Weapons/WeaponSystem/` (хост, порт, исполнители, данные),
`Assets/Editor/VR_Battlegrounds/Gameplay/WeaponSystemAuthoring.cs` (таблица стволов и writer).
Полная спецификация машины — `tasks/weapon-system/refactor-plan.md` (п. 3: оси, состояния, события, таблица,
инварианты И1–И13); учёт без копий — `tasks/weapon-system/ledger-single-source.md`.
