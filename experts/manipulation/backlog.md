Обновлено 2026-10-14 (сверка хаба и брокера в Codex). Кандидаты вне хаба; порядок — приоритет. §N — `Docs/manipulation/architecture.md`.

1. `anchored-ignore-guard` — влит с `grab-authority/pocket-active-hide` (`feae75b56`), принят пользователем. Пропуск объектов вне сцены + одна GameLog.Error на handle; null-проверка SDK75 включена. GREEN660: Android PASS и470/470, RED655 подтвердил чувствительность тестов. launch-infra7093/gameplay7091 уведомлены. Корень прежней утечки — разборка арсенала, gameplay.
2. `holster-calibration-scale` — влит29c966080, принят пользователем2026-10-15 «сразу вливай как есть». Все этапы grab-authority завершены. Worker711 Android PASS0, существующие79/79 PASS, R ecdc65f8c6; ручная заявка720 отменена из QUEUED. Новые регрессионные тесты кобур остаются кандидатом вне принятого выпуска; ручной runtime/Quest не подтверждён. Журнал2026-10-15-holster-calibration-scale.
3. `pocket-active-hide` — принят и влит `feae75b56`, база опубликована661 DONE. Storage/масштаб/события закреплены новыми тестами, GREEN660470/470. Более широкий641 сохраняет9 известных внешних отказов; не маскируются. Подробности — Readme/Details задачи.
4. `pocket-nonnetworked-items` — встроенный (не сетевой, без NetworkIdentity) магазин оружия, положенный в карман рукой, не попадает в `_pocketMagazines` (SyncList по netId) → опоздавший не видит его в кармане (стенд pocket-active-hide, тикет 592; не регрессия). Вариант: состояние кармана по UniqueId через снимок UXR вместо netId — с network.
5. `grabproxy-client-send` — при уничтожении тела клиенты шлют `IsGrabbable=False` для `GrabProxy` кармана (сервер отбрасывает: «Target component wasn't found»; прогоны 453/465/526/540) — синхронизируемый вызов не от автора.
6. `server-grab-validation` — сервер не проверяет правила хвата, кроме арсенала (`ServerRejectTake`); с network — §8.
7. `orphan-physics` — `UxrGrabManager.RemoveOrphanedManipulations` (сирота при уничтожении руки, патч 12/71) удаляет запись без событий и не возвращает `isKinematic`: предмет висит в воздухе (network 4303; у предметов нет NetworkTransform). После death-release-authority сирот на смерти нет; остаются обрыв соединения и пр.
8. `stale-rigidbody-sync` — после серверного отпускания при смерти клиент-жертва ещё дважды шлёт `UpdateRigidbody` со ссылкой на уничтоженную руку (сервер отбрасывает с ошибкой десериализации; прогоны 453 и 465): погасить отправку по отпусканию/уничтожению граббера.
9. `hand-release-owners` — пять мест освобождения рук свести в один путь — §8. Шестой путь: `WeaponEquipmentBinding.ReconcileSdkGrabs` (синтетический `ReleaseObject` из SyncVar-hook, на AK105/PPK) — ревью с network до активации claim на игровом оружии (запрос bots-fix/weapon-system 2026-10-11).
10. `grab-e2e` — хват на стенде (PlayMode-тестов нет), без второго писателя grab-state; с launch-infra `xr-input`/`interaction-scenarios`.
11. `feedback-quest-cost` — замер на Quest опроса готовности `InteractionFeedback`, замер не найден; с performance — `tasks/haptics-system/Details.md` п. 0.3.
12. `pocket-anchor-reentry` — `UxrMagazinePocket.OnAnchorPlaced` безопасен лишь из-за непересылки вложенных событий; закрепить — §8, Issue 38.
13. `grab-cache-lag` — кадр задержки `GrabbableHierarchyCache`; static `AllowHandTransfer` — §8.
14. `grip-pose-cache` — `UxrGrabPointInfo.GetGripPoseInfo` перебирает `UxrAvatar.GetPrefabGuidChain()` (yield, аллокации) на каждый вызов, у ботов 2+ раза за кадр: кеш записи на экземпляр аватара в Manipulation, без правки `UxrAvatar`; по замеру bots этапа 0 (4254), с performance.
15. `grip-proximity-root-check` — валидатор: точка хвата с `_grabProximityTransformUseSelf` и пустым align у записи игрового аватара (или default) меряет близость от корня предмета (F8 avatar-grip 4163, планшет у MEF); с avatar-grip.
16. `patch18-comment` — устаревший `PocketReadiness` в комментарии патча 18 `UxrGrabManager.cs` — §8.
17. `waveform-graph` — инспектор формы-графика; отложен пользователем — `decisions.md` 2026-10-09.

## Обещания другим агентам (на 2026-10-14)

- weapon-system7087 и network7085: уведомлены о вливании pocket-active-hide.
- bots-fix7089: уведомлён о позднем входе магазинов кармана; наши прежние блокеры закрыты.
- launch-infra7093: уведомлён о влитой защите AnchoredItemCollisionIgnore.
- gameplay7091: уведомлён о влитой null-проверке SDK75.
- avatar-ik (calibration-scale-events r1, ACK 5985): подписчик manipulation для кобур — локально на каждой машине; этап holster-calibration-scale после вливания их scale-events.
- avatar-grip (5678): зависимость item-clicks от hand-rig-quality/cyborg-grips выполнена (merged, сверено в хабе 2026-10-14); остаётся sources.
