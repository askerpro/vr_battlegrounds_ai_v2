# Фото-захват тира не на сервере без графики

2026-10-22. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/range-photo-headless` (схема «задача = агент»).

## Цель

Убрать RenderTexture.Create failed из лога сервера: ShootingRangePhotoCapture снимает фото попадания только на машине с графическим устройством.

## Причина

Quest e2e perf-stress Lobby с реальным трекингом (задача lobby-gpu-tour, сборка c7b8d6ab3,
`Tools/e2e/results/20261022-020611-quest-perf-stress-…` в worktree lobby-gpu-tour): пользователь взял MP5K
с арсенала и выстрелил в мишень тира. Снаряд UltimateXR симулирует каждая машина (см. `ShootingTarget`),
поэтому `ShootingTarget.ProjectileContact` пришёл и на Windows-сервере с `-nographics` (NullGfxDevice).
`ShootingRangePhotoCapture.RenderPending` вызвал `RenderPipeline.SubmitRenderRequest` — URP впервые создал
пайплайн, `RenderGraphDefaultResources` не смог создать RenderTexture: 2× `RenderTexture.Create failed`
и `Failed to set the active render target`. В замерных прогонах (поза `eye`) никто не стрелял — ошибки нет.

## Решение

`ShootingRangePhotoCapture.CanRender` = `SystemInfo.graphicsDeviceType != Null` (тот же критерий, что у
`WeaponEjectaPool` и `SceneDetailApplier`). Без графики захват не подписывается на контакты, `QueuePhoto`
возвращает false — студия, камера и RT не создаются; в лог одна строка Info. Хост и клиенты с графикой —
без изменений. Владелец состояния прежний (компонент захвата), второго писателя нет.

## Этапы

Этапы и области записи — в [plan.json](plan.json).

- [x] Причина найдена.
- [x] Исправление: checkpoint 973e0a117.
- [x] Сборка пары e2e/Quest 973e0a117: обе exit 0.
- [x] Quest e2e Lobby `-HeadPose off` с выстрелами в мишень: в логе сервера ошибки нет, вердикт по логам GREEN
      (итог INCONCLUSIVE — шлем снят за 1 с до конца фазы). Пользователь принял 2026-10-22: фото в шлеме есть.
- [x] Тест `ShootingRangePhotoCaptureTests` на критерий `CanRenderOn`; журнал level-design.
- [x] Ревью level-design: approve, zone ACK записан (заявка b151f47e750541cc, кандидат 984b9696e).
- [x] Heavy editor + EditMode, аренда 1014 (вход be0b0bff, база 30b90e08e): компиляция OK, сценарии 3/3 OK,
      EditMode 2919 — красных 19, `ShootingRangePhotoCaptureTests` среди них нет. Код 1 не из-за правки:
      origin/dev ушла вперёд (12 игровых файлов E2E) — нужен rebase и повтор; 2 «новых красных»
      (`GameTagsTests.Prefabs_TagsMatchRules`, `HandsPackWeaponTests…(Scar)`) — из dev, те же в heavy bots-input и e2e-series.
- [ ] Rebase на свежую origin/dev, повтор heavy, merge.

Подробно: [changelog/2026-10-22-fix.md](changelog/2026-10-22-fix.md).
