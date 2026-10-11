# Сцены карт пересохранены под флоу арсенала, окклюзия перезапечена

Этап `arsenal-generator/composer-presentation` (gameplay) merged `eb0f09065ac9f29f7f73d3f78106a5e5cf39b8e3`,
принят пользователем 2026-10-09; коммит данных `4107a18bf1d47f0b1294ea9df782eacfc8744135`: Lobby, пять карт,
BotCombatStand и CommonArsenalReview без слотов и авторских полей, окклюзия всех карт перезапечена.
Evidence (хаб): worker 276 — EditMode отказы только чужие (TestMap2 LightingData, TrayFrontLip TestMap1 —
были до задачи); worker 266 — проба станций 26/26, хост Lobby→TestMap1→TestMap1→Lobby без ошибок, Android PASS.

Вывод для зоны: чужие этапы пишут все сцены и OcclusionCullingData целиком — дефекты TestMap2 LightingData и
TrayFrontLip остаются за level-design (backlog 1–2). Проверка в шлеме окклюзии после перезапекания — unknown.

Источники: хаб status (stage evidence/acceptance); `git show --stat 4107a18bf`.
