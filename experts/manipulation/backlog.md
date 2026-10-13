Обновлено 2026-10-11. Кандидаты вне хаба; порядок — приоритет. §N — `Docs/manipulation/architecture.md`.

1. `pocket-active-hide` — карман прячет магазин `SetActive(false)` (`UxrMagazinePocket.StoreItem` ~226-237), Mirror не спавнит неактивные объекты опоздавшему → у него нет магазинов в карманах, достанный магазин невидим (network 5458). Прятать без деактивации: один владелец видимости, исключить из кандидатов хвата/LooseItems; перф Quest.
2. `grabproxy-client-send` — при уничтожении тела клиенты шлют `IsGrabbable=False` для `GrabProxy` кармана (сервер отбрасывает: «Target component wasn't found»; прогоны 453/465/526/540) — синхронизируемый вызов не от автора.
3. `server-grab-validation` — сервер не проверяет правила хвата, кроме арсенала (`ServerRejectTake`); с network — §8.
4. `orphan-physics` — `UxrGrabManager.RemoveOrphanedManipulations` (сирота при уничтожении руки, патч 12/71) удаляет запись без событий и не возвращает `isKinematic`: предмет висит в воздухе (network 4303; у предметов нет NetworkTransform). После death-release-authority сирот на смерти нет; остаются обрыв соединения и пр.
5. `stale-rigidbody-sync` — после серверного отпускания при смерти клиент-жертва ещё дважды шлёт `UpdateRigidbody` со ссылкой на уничтоженную руку (сервер отбрасывает с ошибкой десериализации; прогоны 453 и 465): погасить отправку по отпусканию/уничтожению граббера.
6. `hand-release-owners` — пять мест освобождения рук свести в один путь — §8. Шестой путь: `WeaponEquipmentBinding.ReconcileSdkGrabs` (синтетический `ReleaseObject` из SyncVar-hook, на AK105/PPK) — ревью с network до активации claim на игровом оружии (запрос bots-fix/weapon-system 2026-10-11).
7. `grab-e2e` — хват на стенде (PlayMode-тестов нет), без второго писателя grab-state; с launch-infra `xr-input`/`interaction-scenarios`.
8. `feedback-quest-cost` — замер на Quest опроса готовности `InteractionFeedback`, замер не найден; с performance — `tasks/haptics-system/Details.md` п. 0.3.
9. `pocket-anchor-reentry` — `UxrMagazinePocket.OnAnchorPlaced` безопасен лишь из-за непересылки вложенных событий; закрепить — §8, Issue 38.
10. `grab-cache-lag` — кадр задержки `GrabbableHierarchyCache`; static `AllowHandTransfer` — §8.
11. `grip-pose-cache` — `UxrGrabPointInfo.GetGripPoseInfo` перебирает `UxrAvatar.GetPrefabGuidChain()` (yield, аллокации) на каждый вызов, у ботов 2+ раза за кадр: кеш записи на экземпляр аватара в Manipulation, без правки `UxrAvatar`; по замеру bots этапа 0 (4254), с performance.
12. `grip-proximity-root-check` — валидатор: точка хвата с `_grabProximityTransformUseSelf` и пустым align у записи игрового аватара (или default) меряет близость от корня предмета (F8 avatar-grip 4163, планшет у MEF); с avatar-grip.
13. `patch18-comment` — устаревший `PocketReadiness` в комментарии патча 18 `UxrGrabManager.cs` — §8.
14. `waveform-graph` — инспектор формы-графика; отложен пользователем — `decisions.md` 2026-10-09.
