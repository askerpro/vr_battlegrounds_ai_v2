# lobby-decor-builders: сцена Lobby — источник правды, 2026-10-24

Для зоны:
- Решение пользователя: сцена Lobby правится вручную, сборщики для неё не нужны. Удалена вся папка
  `Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration/` (сборщики декора, ночной пресет `LobbyNightLighting`,
  `LobbyTentLightFixtures`, `LobbyTentFloorLightmap`, `LobbyLightmapUv`, бюджет Quest) и `Performance/LobbyGpuSceneTweaks`,
  а также `FieldCamp.prefab`, `LobbyZoneLayoutBinding`, `LobbyDetailTuner`, материалы `LobbyNightFloodFace/WindowGlow`.
  Меню «Lobby Decoration» и «Performance/Lobby GPU» больше нет. Сгенерированные ассеты сцены (`LobbyTentFloor.asset`,
  UV2 в импортёрах) остаются.
- Пресет ночи при запуске возвращал Realtime-свет — теперь его нет; свет правится в сцене, общие правила сторожат правила
  аудитора `lighting.*` и проверка всех карт `MapLightingAuditTests` (предупреждения `[Warn:lighting]`).
- Порядок перезапекания Lobby после правок света/геометрии: (при необходимости «Подсветка слотов» арсенала) → Window →
  Rendering → Lighting → Generate Lighting → сохранить сцену; прежний шаг «Ночь: подготовить запекание» отменён.
  `tasks/arsenal-slot-lighting/tools/lobby_slot_lighting_bake.cs` вызывает удалённый `PrepareBake()` — инструмент закрытой задачи.
- LOD вышек `.95/.85/.75/.65` перенесены из оверрайдов сцены в префаб `Watchtower_Lit` (три экземпляра в Lobby, другие
  карты его не используют).
- На новых правилах Lobby чиста. MilitaryBase (бывш. TestMap1), ServiceYard, ReferenceMap04 — реалтайм-источник и
  незапечённый свет (предупреждения); Warehouse и BlockStand (бывш. TestMap2/3) без источников и GI-объектов.
