# haptics-system — единая система вибрации и отклика взаимодействий

Обновлено 2026-10-14 (сверка живого хаба, продолжение в Codex). Владелец: эксперт `manipulation`,
постоянный worktree `F:/CodexWorktrees/manipulation/Vr_Battlegrounds_ai`,
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
| `pocket-removal` — снять `PocketHaptics` с аватаров | влит `9643a864` |
| `sdk-routing` — перехват вибрации SDK (патч 65), запасные формы по типу | влит `7f0d19986`, принят в шлеме 2026-10-11 |
| `sources` — гибель, стена, часы, жест отладки, вид призрака на `HapticService` | planned, не начат; ждёт по приоритету пользователя (grab-authority раньше) |
| `item-clicks` — клики хвата SDK на общий клип, разовые события | planned; ждёт `sources`; `hand-rig-quality/cyborg-grips` уже merged в живом хабе (ред. 39) |
| `docs-transfer` — troubleshooting | после приёмки |

## Архитектура

Кратко — [architecture.md](../../Docs/manipulation/architecture.md) (§6 вибрация, §7 отклик), правила —
[invariants.md](../../Docs/manipulation/invariants.md), решения пользователя — [decisions.md](../../Docs/manipulation/decisions.md).
Модель смешивания и патч 65 — [Details.md](Details.md); постановка — [task.md](task.md); исследование — [research.md](research.md).

## Синхронизация

- weapon-system: контракт [haptics-api](contracts/haptics-api.md) rev 1 согласован; ACK их `weapon-grab-lifetime@1`
  (2026-10-10) при сохранении вызовов патча 67.
- `pocket-removal` идёт после `avatar-renderer-regression/asset-repair` (влит); bots-fix, hand-rig-quality, legs-ik — после нас.
- SDK-патчи UltimateXR: [65](changelog/2026-10-11-sdk-65.md), [66](changelog/2026-10-08-sdk-66.md), [67](changelog/2026-10-09-sdk-67.md).

## Проверка

Факты — [changelog/](changelog/): [interaction-feedback](changelog/2026-10-09-interaction-feedback.md),
[clips-interaction](changelog/2026-10-09-clips-interaction.md), [behavior-tests](changelog/2026-10-09-behavior-tests.md).

## Следующий шаг

Задача на паузе: по решению пользователя (2026-10-12/13) сначала `grab-authority`. Когда возьмём — `sources`:
перевести прямые вызовы SDK из `Pending` `HapticOwnershipTests` (`WallPassFeedback`, `WristDisplay`, `GhostViewEffect`
(+ устаревший комментарий про `PocketHaptics` ~74), `DebugGestureInput`) на `HapticService.Play/Begin` со своими клипами.
Перед `begin` — `zone-check`: нужны zone-ack gameplay, launch-infra, ui. Числа вибрации подбирает пользователь в шлеме.

## Ждёт пользователя

- Нет открытых вопросов. Приоритет `sources` относительно других работ — решение пользователя.

## Решения пользователя

См. [decisions.md](../../Docs/manipulation/decisions.md) «Вибрация» (2026-10-08…2026-10-11): один владелец мотора,
клип с формой, вибрация только своей руки, запасные формы по типу для вызовов SDK без формы (2026-10-11).

## Процесс (Codex)

Inbox обработан, адресных непрочитанных сообщений нет. Устаревший Claude-route очищен (epoch 3); межзадачная
доставка через canonical inbox. Вопросы пользователю — через доступный инструмент запроса ввода.
План зарегистрирован: r19, client=codex; этапы и зависимости сохранены.
`grab-authority/pocket-active-hide` принят и влит `feae75b56`, база661 DONE,
GREEN660470/470 и Android PASS; ранее отложенная приёмка больше не блокирует этот этап; см.
[grab-authority/Details.md](../grab-authority/Details.md). Это не завершение и не приёмка `haptics-system`.
