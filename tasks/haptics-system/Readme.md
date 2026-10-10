# haptics-system — единая система вибрации и отклика взаимодействий

Обновлено 2026-10-10. Владелец: агент `manipulation-expert` (`.claude/agents/manipulation-expert.md`, запуск
`claude --agent manipulation-expert`), worktree `F:/CodexWorktrees/haptics/Vr_Battlegrounds_ai`, ветка `haptics`,
owner в хабе — `haptics`. База знаний эксперта — [expert/](expert/handoff.md).

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
| `manipulation-expert` — эксперт и база знаний (`expert/`, `.claude/agents/`) | в работе 2026-10-10 |
| `pocket-removal` — снять `PocketHaptics` с аватаров | порядок согласован; после эксперта |
| `sdk-routing` — перехват вибрации SDK (патч 65), форма в компонентах SDK | не начат |
| `sources` — гибель, стена, часы, жест отладки | не начат |
| `item-clicks` — клики хвата SDK на общий клип, разовые события | после `sdk-routing`, с weapon-system |
| `docs-transfer` — troubleshooting, перенос KB в Docs | после приёмки |

## Архитектура

Кратко — [expert/architecture.md](expert/architecture.md) (§6 вибрация, §7 отклик), правила —
[expert/invariants.md](expert/invariants.md), решения пользователя — [expert/decisions.md](expert/decisions.md).
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

Влить этап `manipulation-expert` (документация). Затем `pocket-removal` по плану в [expert/handoff.md](expert/handoff.md).
