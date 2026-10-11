# haptics-system — единая система вибрации и отклика взаимодействий

Обновлено 2026-10-11. Владелец: эксперт `manipulation` (`.claude/agents/manipulation-expert.md`, запуск
`claude --agent manipulation-expert`), постоянный worktree `F:/CodexWorktrees/manipulation/Vr_Battlegrounds_ai`,
ветка `haptics`, owner в хабе — `manipulation`. База знаний — [Docs/manipulation/](../../Docs/manipulation/invariants.md),
передача — [handoff.md](handoff.md).

## Цель и мотивация

У вибромоторов контроллеров один владелец — `HapticService`; остальной код просит сыграть клип `UxrHapticClip` с общей
формой. Классы с приоритетом, вибрация только на руке локального игрока. Раньше писателей мотора было 15, а на Quest
каждый вызов обрывает текущую вибрацию. Отклик взаимодействий (готовность взять/положить, события) — один исполнитель
по данным SDK, одинаковый для карманов, якорей и свободных предметов.

## Статус

| Этап | Состояние |
|---|---|
| `clips-interaction` — клип с формой (SDK-патч 66), сервис, окно «Вибрация» | влит `4640ccd5` |
| `behavior-tests` — смешивание, покрытие отклика | влит `d009709c` |
| `interaction-feedback` — SDK-патч 67, `InteractionFeedback`, префаб-отклик, вторая рука | влит `df153b74`, принят в шлеме 2026-10-09 |
| `manipulation-expert` — эксперт и база знаний, `.claude/agents/` | влит `83d8f34d` |
| `manipulation-kb` — база знаний в `Docs/manipulation/`, инварианты по группам, профиль — адаптер awake | влит `081ec677` |
| `pocket-removal` — снять `PocketHaptics` с аватаров | принят в шлеме 2026-10-11, вливается |
| `sdk-routing` — перехват вибрации SDK (патч 65), форма в компонентах SDK | не начат |
| `sources` — гибель, стена, часы, жест отладки | не начат |
| `item-clicks` — клики хвата SDK на общий клип, разовые события | после `sdk-routing`, с weapon-system |
| `docs-transfer` — troubleshooting | после приёмки |

## Архитектура

Кратко — [architecture.md](../../Docs/manipulation/architecture.md) (§6 вибрация, §7 отклик), правила —
[invariants.md](../../Docs/manipulation/invariants.md), решения пользователя — [decisions.md](../../Docs/manipulation/decisions.md).
Модель смешивания и патч 65 — [Details.md](Details.md); постановка — [task.md](task.md); исследование — [research.md](research.md).

## Синхронизация

- weapon-system: контракт [haptics-api](contracts/haptics-api.md) rev 1 согласован; ACK их `weapon-grab-lifetime@1`
  (2026-10-10) при сохранении вызовов патча 67.
- `pocket-removal` идёт после `avatar-renderer-regression/asset-repair` (влит); bots-fix, hand-rig-quality, legs-ik — после нас.
- SDK-патчи UltimateXR: 65 (резерв, перехват), [66](changelog/2026-10-08-sdk-66.md), [67](changelog/2026-10-09-sdk-67.md).

## Проверка

Факты — [changelog/](changelog/): [interaction-feedback](changelog/2026-10-09-interaction-feedback.md),
[clips-interaction](changelog/2026-10-09-clips-interaction.md), [behavior-tests](changelog/2026-10-09-behavior-tests.md).

## Следующий шаг

Этап `sdk-routing` (патч 65, перехват вибрации SDK) — модель в [Details.md](Details.md) п. 5, план в [handoff.md](handoff.md).
