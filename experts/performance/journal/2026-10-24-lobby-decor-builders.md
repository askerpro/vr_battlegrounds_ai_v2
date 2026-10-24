# lobby-decor-builders: общие правила света аудитора, 2026-10-24

Для зоны:
- Новые правила (`Rules/BakedLightingRules.cs`), цели — по компонентам и флагам, без путей карты; LightmapSettings/
  Lightmapping читаются у активной сцены (`BakedLighting.WithActive`):
  `lighting.realtime-light` (любая карта), `lighting.scene-not-baked`, `lighting.dynamic-in-gi` (Rigidbody/Animator/
  NetworkIdentity — порог-список), `mesh.lightmapped-without-uv2`, `lighting.zone-without-probes` (пол арены сеткой 2 м,
  места у стен арсенала, спавны на 1,2 м; порог 3 м — QPM-LIGHT-009).
- Фиксеры (`Fixers/BakedLightingFixers.cs`): цель — `GlobalObjectId` объекта сцены (имена светильников повторяются),
  правка через Undo + запись переопределений, сцена сохраняется. `LightmapUv2Fixer` не трогает импорт общего FBX
  (QPM-LIGHT-003): копия меша с UV2 в `<папка сцены>/<сцена>/LightmapMeshes/`, MeshFilter сцены — на копию, коллайдер
  остаётся на исходном меше; `.asset`-меш разворачивается на месте.
- Правило `scene.procedural-skybox` удалено: A/B 2026-10-20 — −0,05 мс (решение пользователя); QPM-SKY-001 оставлен как
  рекомендация. Манифест: QPM-LIGHT-005 переписан (сцена — источник правды), -006, новый -009, сопоставление и статус.
- `EmissionOffFixer.OwnedElsewhere`/`BakedLitMaterialFixer.OwnedElsewhere` сохранены: материалы приняты и правятся вручную.
- Известное поведение раннера: открытие сцены раннером помечает `SceneVisibilityState`, `GuardDirty` выгружает его через
  `Resources.UnloadAsset` и Unity пишет Error — тест открывает сцену сам до прогона. Кандидат в исправление ядра.
- `EnvironmentRoots` фиксеров материалов всё ещё перечисляет папки Lobby — не универсально, кандидат на отдельную задачу.
