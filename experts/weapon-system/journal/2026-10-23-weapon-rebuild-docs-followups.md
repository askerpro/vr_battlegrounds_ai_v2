# weapon-rebuild-docs-followups: автосведение родного прицела без записи, «Сбросить запись», хвосты ревью, 2026-10-23

Хвосты влитой задачи weapon-rebuild-sight-guarantee (решения пользователя 2026-10-23 в `Docs/weapons/decisions.md`).

Для зоны:
- `WeaponSightAuthoring.ApplyNative`: родной прицел без записи в `WeaponSightTable` (новый ствол) — автосведение
  решателем стенда (`SightAutoZeroSolver`, деталь в нейтраль, Z из настроек), проверка хода, запись в таблицу после
  сохранения префаба. Отпечаток ShotSource не совпал или другая Z — по-прежнему отказ с подсказкой.
- Стенд: кнопка «Сбросить запись» (только родной прицел, с подтверждением) — намеренная смена оси выстрела; следующая
  пересборка сводит как новый ствол. Для стволов с комплектом маршрута смены оси нет (у префаба был прицел).
- `WeaponSightTable.asset` в области транзакции пересборки — откат возвращает дописанную запись.
- `WeaponRebuildFinish.Complete`: `NetworkAssetIdNormalizer` после разметки тегов (защитно; на MP5K исправлять было нечего).
- Тест `WeaponSightPrefabTests.NativeSight_AutoZeroReproducesTableEntry` — автосведение повторяет принятые записи.

Проверка: `tasks/weapon-rebuild-docs-followups/changelog/2026-10-23-worker-check1.md`.
