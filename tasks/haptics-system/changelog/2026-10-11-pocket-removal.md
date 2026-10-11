# 2026-10-11 — pocket-removal, шаг 1: снятие PocketHaptics с префабов (черновик, не принят)

Исполнитель: субагент эксперта manipulation. Тикет брокера 420 (вход a43d5ac7b, база 17293aa3c),
результат R 87674dcd68cab9eae4f4546f8f14b6a6bb9f3d87, finish accepted=true, receive выполнен.

## Что сделано через Unity (LoadPrefabContents → DestroyImmediate → SaveAsPrefabAsset)
- PlayerBase.prefab: снят PocketHaptics с корня (fileID 2661479309573987799).
- PlayerControllersCyborgAvatar.prefab: снят added component 3705747643474500777.
- GhostAvatar.prefab: обычное пересохранение висячую строку m_RemovedComponents НЕ убрало;
  убрана `PrefabUtility.RemoveUnusedOverrides` (пробный прогон в памяти: removed 3→2, mods 751→751).

## Побочные изменения сериализации (не отброшены, решение за владельцем)
- PlayerBase: UxrMagazinePocket `_capacity: 4` → `_perTypeLimit: 3` (устаревшее поле, в коде только
  `_perTypeLimit = 3`); UxrStandardAvatarController дописал блок `_useNativeLegIK: 0` + `_legs` по
  умолчанию; пустой `m_Children:` → `m_Children: []`. Поведенчески нейтрально.
- Cyborg: удалены 5 stripped MeshRenderer без ссылок (531337332675431091, 531337333056520375,
  531337333910491149, 531337334047811625, 7188191905535236324). Нейтрально.
- GhostAvatar — ВРЕДНО: у 241 переопределения UXR `__isInPrefab 1→0` и `__prefabGuid f3401ffe…→8e0e0632…`
  (known-issues: LoadPrefabContents/SaveAsPrefabAsset в изолированной сцене); NetworkIdentity
  `_assetId` 2625578788 → 525190276 (target 8498019334250074400). UxrUniqueIdPersister в worker флаги не выровнял.

## Решение владельца (2026-10-11)
- GhostAvatar.prefab откатан к origin/dev (`git restore`): висячая строка `m_RemovedComponents` на снятый
  компонент — неиспользуемый override, рантайм не затрагивает; пересохранение призрака даёт вред выше.
- PlayerBase / Cyborg — сохранение Unity принято как есть (схождение сериализации; `_avatarRenderers` и
  Local Disabled Game Objects не тронуты — условие zone-ack avatar-ik 3501).
- Красные `Телепорт_попадает_в_пол_карт` ×3 — до этапа, не наши (BotCombatStand/Floor).

## Тесты EditMode (5 фикстур, 95 тестов)
- До снятия: 3 failed — `AvatarLoadoutTests.Телепорт_попадает_в_пол_карт` ×3 (BotCombatStand/Floor, слой Default).
- После: 6 failed — те же 3 + ожидаемые `На_корне_есть_хаптики_карманов` ×3. Остальное зелёное.
- `UxrUniqueIdOnDiskTests`, `GhostAvatarTests` не прогонялись.

## Проверка в шлеме (тикет 424, вход 02494baf7, база ac238e942)
- PlayLaunch Host, сцена Lobby, Quest Link: карманы (готовность, хват, укладка), стена арсенала, оружие на
  полу, гнездо магазина — пользователь: «все ок» (2026-10-11). Play остановлен, CleanupPassed=true.

## Шаг 3 (тикет 427, вход 1a79d0331)
- Удалены `Assets/Scripts/Interaction/PocketHaptics.cs(.meta)`; из `GhostAvatarBuilder.LivingOnlyComponents`
  и doc-комментария убрано имя; из `AvatarLoadoutTests` удалён `На_корне_есть_хаптики_карманов`
  (покрытие карманов откликом — `ManipulationFeedbackCoverageTests`).
- Компиляция Editor — без ошибок; `AndroidCompileGate` — PASS.
- EditMode, 8 фикстур (AvatarLoadout, PrefabComposition, AvatarRendererOwnership, GhostAvatar,
  InteractionFeedback, ManipulationFeedbackCoverage, UxrUniqueIdOnDisk, HapticOwnership): 105 тестов,
  102 passed, 3 failed — прежние `Телепорт_попадает_в_пол_карт` ×3 (BotCombatStand/Floor, не этап).
- Остаток: комментарий о `PocketHaptics` в `GhostViewEffect.cs:74` — файл этапа `sources`, правится там.
