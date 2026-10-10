# Передача: haptics-system → manipulation-expert

2026-10-10. Передаёт: Claude Code сессия задачи `haptics-system` (worktree `F:/CodexWorktrees/haptics/Vr_Battlegrounds_ai`,
ветка `haptics`, owner в хабе — `haptics`). Статус этапов — [Readme](../Readme.md) и `coordination.py status`.

## Влито в origin/dev

- `clips-interaction` (`4640ccd5`): `HapticService`/`HapticMixer`/`UnityXRHapticDevice`, SDK-патч 66 (форма в клипе),
  8 форм, окно «Вибрация», `HapticOwnershipTests`, `HapticWaveformTests`.
- `behavior-tests` (`d009709c`): `HapticMixerTests`, `ManipulationFeedbackCoverageTests`.
- `interaction-feedback` (`df153b74`): SDK-патч 67, `InteractionFeedback`/`HapticPlayer`/конфиг/override/
  `NetworkedFeedback`, `Feedback_GrabReady`, вторая рука 0.5, `InteractionFeedbackTests`. Принято в шлеме 2026-10-09:
  карманы, оружие на полу, стена арсенала, гнездо магазина. Удалены `HapticRoles`, `HapticOverride`,
  `InteractionHaptics`, `AnchorReadiness`.
- Публикация базы worker с `df153b74` — тикет сопровождающего 268 (QUEUED на момент записи).

## Следующие этапы (plan.json, ревизия 11)

1. **`pocket-removal`** — снять пустой `PocketHaptics` (GUID скрипта `e14c4d0d52ad32e499e71bb1dd2250c7`):
   - `PlayerBase.prefab`: строка `- component: {fileID: 2661479309573987799}` (~68022) и блок MonoBehaviour (~68685);
   - `PlayerControllersCyborgAvatar.prefab`: `addedObject: {fileID: 3705747643474500777}` в `m_AddedComponents` (~6806)
     и блок (~7311);
   - `Ghost/GhostAvatar.prefab`: `m_RemovedComponents` строка с `3705747643474500777` (~3017);
   - удалить `Interaction/PocketHaptics.cs(.meta)`, строку `"PocketHaptics"` в `GhostAvatarBuilder.cs` (~51, и doc ~22),
     проверку `PocketHaptics` в `AvatarLoadoutTests.cs` (~602–612) с комментарием «вибрацию карманов даёт InteractionFeedback».
   - Порядок согласован: после `avatar-renderer-regression/asset-repair` (влит `f33e5f4e`); bots-fix, hand-rig-quality,
     legs-ik стоят `after` нас. У legs-ik незавлитая строка `_legs.poseSet` в Cyborg — rebase на них.
   - Лучше править через Unity на worker (снять компонент, сохранить префаб) до удаления скрипта, затем удалить скрипт;
     или текстом по списку выше — тогда проверить `PrefabCompositionTests`, `AvatarLoadoutTests`, компиляцию.
   - Проверка в шлеме: карманы вибрируют как прежде.
2. **`sdk-routing`** — патч 65: перехват `UxrControllerInput.SendHapticFeedback` в сервис, форма в компонентах SDK;
   `IWeaponRecoilHapticsOwner` гасит отдачу SDK по стволу; убрать `WeaponHapticOutput` из `Pending` по готовности
   weapon-system. Модель — [Details.md п. 5](../Details.md).
3. **`sources`** — гибель (`Death`), стена (`WallPassFeedback`), часы (`WristDisplay`), жест отладки, `GhostViewEffect`.
4. **`item-clicks`** — клики хвата SDK (`UxrManipulationHapticFeedback`) на 6 префабах → общий клип; разовые
   «взят/уложен/отпущен» в `InteractionFeedbackConfig`. По согласованию с weapon-system.
5. **`docs-transfer`** — `Docs/troubleshooting.md` («Карман не вибрирует» — устарело), перенос KB в продуктовые Docs.

## Открытые вопросы и обязательства

- Контракт `haptics-api` rev 1 (weapon-system → `HapticService.Play(clip, grabber, role, gain)`) — согласован.
- `weapon-grab-lifetime@1` — ACK дан 2026-10-10 с условием сохранить вызовы патча 67.
- Кандидаты на проверку — [architecture.md §8](architecture.md) (дубль освобождения рук при смерти и др.).
- Отложено: инспектор формы-графика с перетаскиванием точек.
