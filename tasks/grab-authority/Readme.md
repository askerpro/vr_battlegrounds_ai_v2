# grab-authority — авторство хвата: сброс рук при смерти и удержание предмета у автора

Обновлено 2026-10-12. Владелец: эксперт `manipulation` (`claude --agent manipulation-expert`), постоянный worktree
`F:/CodexWorktrees/manipulation/Vr_Battlegrounds_ai`, ветка `haptics`. База знаний — [Docs/manipulation/](../../Docs/manipulation/invariants.md).

## Цель и мотивация

Решение пользователя 2026-10-12 «бот = обычный аватар» (bots-fix, `tasks/bots-fix/player-flow-plan.md`): бот идёт
штатным потоком игрока, хват SDK штатный, автор аватара без владельца — сервер. Два места системы хвата стали
блокерами (ревью 4188/4215, пользователь выбрал делать их первыми 2026-10-12):

1. **Сброс рук при смерти без автора.** `PlayerController.OnActorDied` → `PlayerGrabManager.ReleaseAllGrabbedObjects`
   зовёт синхронизируемый `ReleaseObject(..., true)` на каждой машине (класс Issue 23; инвариант 12). Возможны
   дубль и расхождение (клиент роняет предмет со своей скоростью). Блокер этапа 1 bots («смерть со сбросом»).
2. **Отставание удерживаемого предмета.** Сопротивление корня (AK105 0.03/0.05) считается на каждой машине в мировых
   координатах: ~5° при повороте 90°/с, а движение корня мимо `AvatarMoved` (у `UpdateExternally` событие игнорируется,
   `UxrGrabManager.cs:412`; прямую запись корня NT/BotBody событие не видит) не переносит оружие при телепорте и
   ходьбе. Страдают боты и удалённые игроки у наблюдателей.

## Этапы

| Этап | Суть | Состояние |
|---|---|---|
| `death-release-authority` | Прогон 2026-10-12: отпускание и сама смерть до клиентов не доходят ([факты](changelog/2026-10-12-death-probe.md)). Вариант C network 4303: реализация — в совместном этапе gameplay `death-body-defer/deferred-body` (наш zone-ack на `PlayerGrabManager`); у нас — ревью диффа хвата и прогон-проверка | остановлен в пользу `death-body-defer` |
| `held-motion` | SDK-патч (номер — `patch-reserve`): сопротивление только у автора держащего аватара и относительно его корня; ACK weapon-system по значениям в префабах; шлем с двумя клиентами | проектирование, параллельно с `death-body-defer` |

## Синхронизация

- bots-fix: их `pf-equipment` ставит `after` на `death-body-defer/deferred-body`; их этап `held-motion` снят (4228).
- weapon-system: значения сопротивления в префабах оружия — их; ACK до `held-motion`.
- network: ревью сетевой части (автор ownerless-аватара, поздний вход).

## Следующий шаг

Ревью диффа gameplay `death-body-defer/deferred-body` и прогон-проверка (как тикет 453); затем `held-motion`.
