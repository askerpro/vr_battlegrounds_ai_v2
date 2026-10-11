# PocketHaptics снят с аватаров и удалён (этап pocket-removal), принято в шлеме

`9643a864c98b8645a25d31addbca11956cd6d25c` в origin/dev (хаб: `haptics-system/pocket-removal` merged; база worker —
тикет 429). Zone-ack avatar-ik 3501 с условием: `_avatarRenderers` и Local Disabled Game Objects не меняются.

- `PlayerBase`, `PlayerControllersCyborgAvatar`: компонент снят через Unity на worker (тикет 420); сериализация
  сошлась (умолчания `_legs`, `_perTypeLimit`, 5 stripped MeshRenderer без ссылок).
- `GhostAvatar.prefab` не пересохранялся: пересохранение ломает UXR `__prefabGuid`/`__isInPrefab` (241 шт.) и
  `NetworkIdentity._assetId`; висячая строка `m_RemovedComponents` безвредна. Урок: префаб-вариант призрака правится
  только `GhostAvatarBuilder`, не `SaveAsPrefabAsset` в изолированной сцене.
- Удалены `PocketHaptics.cs(.meta)`, имя в `GhostAvatarBuilder`, тест `На_корне_есть_хаптики_карманов`.
- Шлем (тикет 424, Lobby Host): карманы, арсенал, пол, гнездо — «все ок». Тикет 427: EditMode 105 — 102 passed,
  3 прежних красных `Телепорт_попадает_в_пол_карт`; AndroidCompileGate PASS.
- Уведомлены legs-ik, bots-fix, hand-rig-quality (их `after` снят).

Источники: `tasks/haptics-system/changelog/2026-10-11-pocket-removal.md`.
