Обновлено 2026-10-11. Кандидаты вне хаба; порядок — приоритет. §N — `Docs/manipulation/architecture.md`.

1. `death-release-authority` — `OnPlayerDied` освобождает руки с propagate на каждой машине без автора, возможен дубль; сначала стенд/лог — §8, Issue 23.
2. `server-grab-validation` — сервер не проверяет правила хвата, кроме арсенала (`ServerRejectTake`); с network — §8.
3. `hand-release-owners` — пять мест освобождения рук свести в один путь — §8. Шестой путь: `WeaponEquipmentBinding.ReconcileSdkGrabs` (синтетический `ReleaseObject` из SyncVar-hook, на AK105/PPK) — ревью с network до активации claim на игровом оружии (запрос bots-fix/weapon-system 2026-10-11).
4. `grab-e2e` — хват на стенде (PlayMode-тестов нет), без второго писателя grab-state; с launch-infra `xr-input`/`interaction-scenarios`.
5. `feedback-quest-cost` — замер на Quest опроса готовности `InteractionFeedback`, замер не найден; с performance — `tasks/haptics-system/Details.md` п. 0.3.
6. `pocket-anchor-reentry` — `UxrMagazinePocket.OnAnchorPlaced` безопасен лишь из-за непересылки вложенных событий; закрепить — §8, Issue 38.
7. `grab-cache-lag` — кадр задержки `GrabbableHierarchyCache`; static `AllowHandTransfer` — §8.
8. `grip-proximity-root-check` — валидатор: точка хвата с `_grabProximityTransformUseSelf` и пустым align у записи игрового аватара (или default) меряет близость от корня предмета (F8 avatar-grip 4163, планшет у MEF); с avatar-grip.
9. `patch18-comment` — устаревший `PocketReadiness` в комментарии патча 18 `UxrGrabManager.cs` — §8.
10. `waveform-graph` — инспектор формы-графика; отложен пользователем — `decisions.md` 2026-10-09.
