# Превью содержимого станций арсенала в Edit Mode

2026-10-22. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/arsenal-station-scene-preview` (схема «задача = агент»). Зона — gameplay (арсенал).

## Цель

В Edit Mode (Scene View, без Play) в сцене карты (Lobby и др.) видно то же содержимое станций арсенала, что в Play: слоты, оружие, магазины, карточки, опоры. Игра не меняется.

## Решение

Сборщик целиком без Play работать не может: требует `Application.isPlaying`, назначает сетевые ID ролей, меняет
состояние стены (`InstallSlots`) и пресета (`Prepare`) в сцене; оружие и магазины выдаёт стена через Mirror
(`ReplenishSlotsWhere`, `ArsenalMagazineSupply.SpawnStock`). Поэтому сборка разделена:

- **Расчёт** (чистый, без объектов): `ArsenalStationComposer.PlanComposition(description, station)` → `ArsenalStationPlan`
  (на каждый слот: запись манифеста, ряд, раскладка, поза в ряду). Геометрия ряда — `ArsenalSlotRow.SlotLocalPoses`.
  Описание станции — `MapArsenalStationDescriber.Describe` (тот же вход, что у запуска).
- **Создание** — `PrepareComposition` строит слоты строго по плану (`ArsenalSlotRow.Mount`).
- Общие позы предметов и карточки: `ArsenalPresentationApplicator.ItemWorldPose`, `ApplyCard(Transform…)`,
  `ArsenalPriceTag.ApplyFrame/Text/TextColor`, опоры — `ArsenalSupportProjection.MaterializeSupportRole`.
- **Превью** (`Assets/Editor/VR_Battlegrounds/Arsenal/ArsenalStationScenePreview.cs`) вызывает только расчёт и рисует
  копии мешей/материалов/текста карточки. `DontSaveInEditor|DontSaveInBuild|NotEditable`, корни в корне сцены станции
  (не внутри префаба станции), удаляются при входе в Play, выгрузке сцены, перезагрузке скриптов.
- Превью всегда включено в интерактивном редакторе, переключателя нет (решение пользователя 2026-10-22: «оно должно
  быть всегда включено»); в batch-режиме не строится. Итог сборки и кнопка «Обновить» — в «Редакторе арсенала» →
  «Станции», меню `Tools/VR Battlegrounds/Arsenal/Обновить превью станций`. Отложенная пересборка — через
  `EditorApplication.update`, не delayCall (у редактора без фокуса очередь delayCall стоит). Открытие и закрытие сцен
  превью замечает по смене набора загруженных сцен (`LoadedScenes`), а не по `sceneOpened`: после прогона тестов Unity
  восстанавливает сцены без этого события (аренда 1032).
- Обновление (решение пользователя 2026-10-22: «баланс удобства и нагрузки, лёгкая реализация»): автоматически через
  0,5 с после последней правки входа (ObjectChangeEvents для пресета, раскладки, WeaponInfo, стиля, MapData, каталогов;
  импорт .asset этих типов и .prefab), при открытии сцены, выходе из Play, перезагрузке скриптов; кнопка «Обновить».

## Статус

- [x] Анализ сборщика, разделение расчёта и создания, превью, переключатель, автообновление.
- [x] Компиляция на worker (аренда 1021, снимок 5334cd4c): 0 ошибок, 2 предупреждения устаревшего API — исправлены после.
- [x] 2026-10-22, аренда 1027 (ветка перенесена на origin/dev a1b487666, снимок 5d73a67d, finish accepted, receive OK):
  - компиляция: 0 ошибок, 0 предупреждений; типы превью и `PlanComposition` загружены.
  - EditMode `Arsenal`, `ArsenalWall`, `Maps.(MapArsenalCompositionAdapterTests|MapBootstrap|MapRun)`: 159 = 156 прошли,
    1 упал — известный красный `ArsenalMapGeometryTests…TestMap1,8`, 2 пропущены (Ignore фикстуры при открытой Untitled).
    Новых красных нет. Отчёт — `reports/editmode-tests-1027.md`.
  - превью в Lobby: «1 станций, 17 слотов» (pegboard 9, shelf 8), 1702 объекта, все с DontSaveInEditor, дети скрыты
    в иерархии; компоненты только Transform/RectTransform/MeshFilter/MeshRenderer/TextMeshPro плюс служебные
    `TMP_SubMesh` ×17; `scene.isDirty` false до и после.
  - Play через PlayLaunch (Lobby, host): Ready, корней превью 0, `[MapBootstrap] Карта 'Lobby' готова`, 17 слотов получили
    оружие, исключений и `Arsenal.Generated` нет (`reports/play-lobby-run-0fdaef15-editorlog.txt`). Имена, порядок и
    локальные позы всех 17 слотов превью совпадают с Play. Cancel → Idle, CleanupPassed=true.
  - снимки: `reports/editmode-sceneview-lobby-arsenal.png`, `reports/editmode-positioned-lobby-arsenal.png` и
    `reports/playmode-positioned-lobby-arsenal.png` (одна поза камеры). Состав и расстановка совпадают. Отличие: в Play
    горит подсветка слотов и световая полоса полки, а превью копирует только меши, поэтому в нём темнее.
  - замечание: после выхода из Play превью на worker само не вернулось. `QueueRebuild` идёт через
    `EditorApplication.delayCall`, а на worker без фокуса очередь delayCall стоит вся (31 вызов, включая собственные
    вызовы Unity). Пересборка через `update` (пауза 0,5 с, путь правок ассетов) сработала: 1 станция, 17 слотов, сцена
    чистая. Если и у пользователя превью не вернётся после Play, нужно перевести `QueueRebuild` на тот же таймер `update`.
- [x] 2026-10-22, решение пользователя «превью должно быть всегда включено»: переключатель убран.
  Аренда 1032 (снимок 14cf5031): компиляция 0/0; Lobby открыта — превью само (1 станция, 17 слотов, 1702 объекта,
  все DontSaveInEditor, сцена чистая); Play → корней превью 0, 17 настоящих слотов; выход → превью вернулось само,
  CleanupPassed=true. EditMode `Arsenal`, `ArsenalWall`, `Maps.*`: 384 = 377 прошли, 5 упали — все из
  editmode-known-red (ArsenalMapGeometry TestMap1, Ambience, LevelDesignBlockTexture, LD20, LD23), 2 пропущены.
  Найдено: после тестов восстановленная Lobby осталась без превью (sceneOpened не пришёл) — исправлено отслеживанием
  набора сцен. Аренда 1037 (ветка на origin/dev 6f77bfa08, снимок 630a704e, finish accepted, receive OK): компиляция
  0/0; Lobby → превью само (1 станция, 17 слотов); те же 384 теста — 377/5 известных/2 skip; после тестов превью в
  Lobby вернулось само; Play → корней 0, 17 настоящих слотов; выход → превью вернулось, CleanupPassed=true, сцена чистая.
- [x] Приёмка пользователем 2026-10-22: «все ок можешь вливать» (принято по результатам проверки на worker).
- [x] Тесты `Assets/Tests/EditMode/Arsenal/ArsenalStationScenePreviewTests.cs`: ряд вешает слоты в позы своего расчёта
  (Center/Start/End); превью Lobby строится по плану (те же слоты, ряды и позы), план объектов не создаёт, копии
  только визуальные, DontSaveInEditor, сцена не dirty, Clear убирает корни. Аренда 1040: превью Lobby PASS, Center
  PASS, Start/End — ошибка ожидаемых чисел в тесте (третий слот 1,15/−0,15, не 1,1/−0,2), исправлено; набор
  Arsenal/ArsenalWall/Maps.* 388 = 379 + 7 (5 известных + 2 эти) + 2 skip. Аренда 1042 (снимок bdba1c79):
  `ArsenalStationScenePreviewTests` 4/4 PASS, AndroidCompileGate PASS (0 ошибок).
- [x] Docs/Arsenal/Arsenal_Code_Architecture_RU.md (план сборки, превью), журнал `experts/gameplay/journal/`.
- [x] Heavy не прогонялся — решение пользователя 2026-10-22 («может не будем гонять тяжелые тесты на превью правку
  арсенала»). Рантайм-изменение — только порядок расчёта сборщика; Play на worker проверен трижды (1027, 1032, 1037),
  позы 17 слотов совпадают с превью.
- [ ] Ревью зон gameplay и ui, вливание.

## Шаги приёмки (Unity пользователя)

Сначала обновите checkout до ветки `task/arsenal-station-scene-preview` с правками из рабочего дерева.

1. Откройте `Assets/Scenes/Lobby.unity` — ничего включать не нужно. В Scene View на станции должны появиться 9 стволов на pegboard и 8 на полке,
   а также магазины, карточки с именем и ценой и опоры. В Hierarchy появится один корень «[Превью арсенала] …», без детей.
   Клик по превью объекты не выбирает.
2. Запустите Play. Станция собирается как обычно, корня превью в Play нет. Сравните расстановку с Scene View.
   Выйдите из Play: превью должно вернуться само.
3. Поменяйте пресет арсенала или раскладку слота у какого-нибудь оружия (WeaponInfo). Примерно через полсекунды превью
   должно обновиться без кнопки. Отмените правку.
4. Сохраните Lobby (Ctrl+S) с видимым превью. `git diff Assets/Scenes/Lobby.unity` должен быть пустым.
5. Откройте другую карту со станциями (например, TestMap1): превью строится и там.

Этапы и области записи — в [plan.json](plan.json).
