# Детали: weapon-rebuild-sight-guarantee

## Состояние на 2026-10-22 (финал перед ревью)

- База — origin/dev 231ebb3c9 и новее. Код и пересобранный арсенал приняты пользователем:
  - арсенал — «да все в порядке» после просмотра на worker;
  - шарнир отдачи — «Проверено, принято»;
  - удаление баз SDK — «Принято».
- Прогоны:
  - [1](changelog/2026-10-22-arsenal-run1.md);
  - [2](changelog/2026-10-22-arsenal-run2.md);
  - [3](changelog/2026-10-22-arsenal-run3.md);
  - [4–5](changelog/2026-10-22-arsenal-run4-5.md);
  - [6–8 и удаление баз SDK](changelog/2026-10-22-sdk-bases-removal.md).
- Все 17 стволов пересобираются транзакционно. Магазины — самостоятельные префабы без геометрии и сэмплов SDK.
  Базы MagGun/MagMachinegun/MagShotgun и `Assets/Unused` удалены. Шарнир отдачи в руках — запястье держащей руки.
- Тесты добавлены после приёмки. Известные красные: покрытие хвата Cyborg у магазинов (регистрация поз отдельно).
- Осталось: коммит, heavy, ревью зон (weapon-system, avatar-grip, gameplay, manipulation, launch-infra), merge.
- Временный зонд `WeaponRebuildProbe` в коммит не входит; копия — `reports/probe/`.

Код этапов 1–2 и правки по прогонам описаны ниже и в changelog.

### Этап 1. Явный отказ шага прицела и откат пересборки
- `Gameplay/WeaponRebuildFinish.cs`: новый вход `Run(source, weaponPath, magazinePath, extraPaths, build)`. Порядок:
  1. Отказ, если есть чужие несохранённые ассеты. Внутренние шаги (MigrateWeapons, области, позы) ещё вызывают общий SaveAssets.
  2. `hadSight` — был ли у префаба прицел.
  3. Копия файлов (`WeaponRebuildTransaction.Begin`).
  4. Шаги сборщика, затем `Complete`.
  5. При любом исключении: `Rollback` и `InvalidOperationException` с причиной и отчётом отката; `GameLog.WeaponSystem.Error`.
  `Complete` стал private. Шаг прицела — `WeaponSightAuthoring.AfterBuild`; его отказ оборачивается в «прицел не подготовлен: …».
  Был прицел, а записи и спецификации нет — тоже отказ.
- `Gameplay/WeaponRebuildTransaction.cs` (новый): побайтная копия до сборки.
  - Что копируется: префабы ствола и магазина, все их зависимости в `Assets/` (кроме кода), заявленные пути. У `Assets/ThirdParty` — только `.meta`.
  - Откат:
    1. Вернуть изменённые байты.
    2. Удалить файлы и папки, которых не было в каталогах скопированных ассетов.
    3. Импортировать изменённое: магазин раньше оружия.
    4. Сверить ещё раз; остались dirty-объекты в памяти — сообщить.
  - Образец — откат `WeaponSystemAuthoring.Migrate`.
- `Weapons/Sights/WeaponSightAuthoring.cs`:
  - `AfterBuildLogged` удалён, вместо него `HasSight(path)`;
  - `ApplyNative` больше не ставит «сдвиг 0, пересвести»: нет записи, другая дистанция или отпечаток ShotSource не совпал — исключение до записи прицела;
  - отказ при несовпадении спецификации с мешем пака бросает `SightPartSplitter.Apply`; второй прицел — `Check` и `ApplyNative`; так было и раньше.
- Сборщики обёрнуты в `Run`:
  - `KinemationWeaponBuilder.Build`: шаги вынесены в `BuildSteps`; новый `RebuildOutputs(k, poses)` — арт и звуки ствола, Materials, Interaction, .meta исходников, TR15 optic, база поз при перезаписи;
  - `HandsPackWeaponBuilder.BuildFull`;
  - `HandsPackLegacyRebuild.RebuildAll`: каждый ствол набора — отдельный `Run`.

### Этап 2. Адресное сохранение (решение пользователя 2026-10-22: «пусть сохраняет только своё»)
Повод: окно арсенала остановило пересборку, потому что в редакторе был несохранён чужой материал пака
`M_Herrington_11-87_Police.mat` («этот сборщик использует SaveAssets»).
- Новый `Gameplay/OwnAssetSave`: `Save(scope)` и `Dirty(scope)`. Сохраняет или перечисляет несохранённые ассеты, чей путь
  совпадает с путём области или лежит в одной из её папок.
- Общий `AssetDatabase.SaveAssets()` заменён на `OwnAssetSave.Save(<своё>)` на всём пути пересборки:
  - `WeaponRebuildFinish.Complete` — область транзакции;
  - `KinemationFixReview.Apply` — арт ствола, Interaction, оружие, магазин;
  - `WeaponInteractionReview.Apply` — префабы, Interaction, папки KINEMATION;
  - `WeaponSystemAuthoring.Migrate` — префабы, созданные ассеты, `Assets/Data/WeaponSystem`, Ejection;
  - `HandsPackPoseImporter.Import` — позы, база поз, аватары хвата, оружие;
  - `HandsPackLegacyRebuild` — `PrepareBaseline` и `CaptureDonors`; после preflight в `RebuildAll` — арт HandsPack.
    Хвостовой `SaveAssets` удалён.
- `WeaponRebuildTransaction.Scope` — область пересборки: скопированные ассеты без исходников паков, корни и заявленные пути.
  `Run` отказывает, только если несохранённое лежит в этой области. Чужие dirty-ассеты (материалы паков и т. п.) больше не мешают.
- Окно арсенала: у команд пересборки (KINEMATION, Hands, набор, 3+3) и у узких писателей (подсветка, механика,
  импорт поз KINEMATION/Hands) `savesAllAssets: false`. Проверка своей области `RequireCleanAssets(paths)` осталась.

### Решение пользователя 2026-10-22: режима «принудительно, пересвести позже» нет
Только отказ: любой сбой шага прицела отменяет пересборку и возвращает файлы. После смены ShotSource ствол сначала
пересводят на стенде. При закрытии задачи записать в `Docs/weapons/decisions.md`.
Unity-этап пользователь ведёт из новой сессии с cwd = этот worktree.

## Перенятые решения пользователя из «Ворктри и ветка по хватам» (2026-10-22)
Пользователь поручил забрать в эту задачу оружейную работу субагента той сессии и принятые там решения.
Источник — ответ сессии local_fafba547 от 2026-10-22.
- Сборщик не создаёт и не регистрирует позы и хваты аватаров. Записи хвата всех аватаров переносятся из текущего
  префаба (`WeaponGripCarryOver`, только KeepCurrent). Регистрация MEF — явная команда вкладки «Взаимодействие»,
  Cyborg — Grip Extraction или ручная доводка. Калибровочный хват MEF из сборщика удалён.
- **Точка отдачи и зоны подсветки — заново от ладони пака у всех стволов**, текущие значения не переносятся.
  AimLock и SupportGripRequiresMain — по расстоянию между ладонями пака (порог 0,10 м). Сдвиг пользователь принял
  по оценке 1–3 см. Фактически — 1,8–2,4 см (changelog прогона 2).
- Подсветка: центр `GrabHighlightPrimary` может сдвигаться, но меш области должен точно совпадать с мешем рукояти.
  Утверждено:
  - остров рукояти в меше корпуса, если он размером с рукоять (≤1,35× коробки, центр ≤6 см); иначе обрезка коробкой
    от ближайшей точки на треугольниках;
  - Support автоматов — коробка от честной точки; Support пистолетов — прежнее смещение;
  - preflight по всем 29 точкам (режим, площадь, доля), EditMode-тесты;
  - `WeaponInteractionReview.Recipe()` без Orig — по ладоням пака, без хватов аватаров.
  Офлайн-анализ: остров рукояти есть у AK105, Mk14, TR15, вероятно SRM12 и MKR9; нет у Uzi, Herrington, R08,
  Revolver, Viper, MP5K.
- Позы `_orig`:
  - имя `<Пак>_<Предмет>_<Точка>_orig`, папка `Assets/Art/HandPoses/Orig/<Пак>/`, без регистрации у аватаров;
    перезапись на месте только при изменении дескриптора;
  - пустые snap-точки `<Grabs>/Orig/<имя>_orig/Left|Right` в префабах оружия и магазина;
  - позы магазина и патрона — эксперимент; JSON источников;
  - экспорт — последний шаг сборки.
  Файл `PackOrigPoses` переносится в `Gameplay/`. MenuItem в меню Avatars убирается; в окне арсенала (вкладка
  «Сборка») — кнопка «Экспортировать позы рук» (и «… все стволы»). Лог `[PackOrigPoses]` и сообщение команды
  перечисляют, куда и что экспортировано.
- Прочие поля `UxrGrabPointInfo` (snapMode, proximity, `_defaultGripPoseInfo`…) при пересборке не переносятся.
- `M_Herrington_11-87_Police.mat` Unity переписывает (очередь HDRP→URP). Пользователь дважды просил откатывать его
  к Git; на предложение закоммитить версию Unity ответа нет.
- Передаётся из той сессии, ещё не принято сюда: незакоммиченная работа субагента в `F:/CodexWorktrees/drafts-cleanup`
  (ветка `live/drafts-cleanup`, поверх коммита 28a298212):
  - подсветка по острову, `MeshIslands`, preflight и тесты;
  - перенос `PackOrigPoses` и кнопка экспорта.
  Пересборка AK105 в основном checkout — пробная, не итог.

## Решения пользователя после прогона 2 (2026-10-22)
- **Магазины паков — в этой задаче.** Пустой узел геометрии SDK (`MagMachinegunGeo`, `MagGunGeo`, `MagShotGunGeo`)
  из магазинов паков убрать целиком, вместе с записями хвата сэмпловых аватаров SDK.
  - Точку хвата Cyborg не переносим. У магазина — такая же snap-точка `_orig`, как у оружия
    (`Grabs/Orig/<Пак>_<Предмет>_Magazine_orig`).
  - Регистрация поз оружия и магазинов выполняется отдельно, по экспортированным позам рук.
  - Позы рук магазина экспортируются вместе с остальными; имя показывает, что поза магазинная.
- **Отказ HoldOpen и ручного заряжания — чинить в этой задаче:**
  - хост WeaponSystem при пересборке пишет общий конец после установки клипов затвора;
  - `HandsPackWeaponBuilder.Build` его не настраивает.
- **Точка отдачи:** пользователь спросил, где она должна быть физически: запястье держащей руки или ладонь.
  Разбор и вопрос — в чате; решение ещё не принято.

## Unity: как прогонять (этап 3)
- Временный зонд `Assets/Editor/VR_Battlegrounds/Gameplay/WeaponRebuildProbe.cs` (+ .meta). Лежит в рабочем дереве, **не коммитить**.
  - Вызов `WeaponRebuildProbe.Run(weapon, mode, reimport, outDir)`, mode: `kin` | `hands` | `legacy`.
  - Пишет `<tag>-pre.txt` и `<tag>-post.txt` (семантический дамп), `<tag>-changed.txt` (хеши зависимостей), `<tag>-files/` и `<tag>-summary.txt`.
  - После сборки сам возвращает файлы своей копией. При отказе сборки проверяет, что откат сборщика вернул всё.
- Сравнение: `python tasks/weapon-rebuild-sight-guarantee/tools/compare_dumps.py pre post`.
- Стволы:
  - kin: Viper, R08, Herrington, SRM12, MKR9, TR15;
  - hands: Scar, Revolver, Uzi, PPK, SniperRifle;
  - legacy: BrowningHiPower + AR15 (M16) + FabarmSDASS одним `RebuildAll`;
  - перезапись поз: один ствол hands (например, MP5K), `reimport = true`.
- outDir: `F:/CodexWorktrees/weapon-rebuild-sight-guarantee/tasks/weapon-rebuild-sight-guarantee/reports/run1`. Каталог в .gitignore.

## Инфраструктура (2026-10-22)
- Ticket 1016 (вход 4309bb71 из служебного worktree Claude Desktop) — finish без Unity-операций.
  MCP-прокси той сессии запущен из корня main и шёл бы в редактор пользователя (ответ сопровождающего, событие 11632).
- Unity-этап вести только из сессии с cwd = этот worktree. Порядок: checkpoint → request (`--agent-root` этого worktree) → `claim --wait` → begin.

## К следующему шагу (после приёмки)
- Rebase на свежую dev (57f935031 и новее). В c6874461e — ручная доводка хватов TR15 для CyborgHandsBase
  (snap-точки записей, поза `Kinemation_TR15_Support`). Перед merge пересобрать TR15 зондом и сверить записи и
  snap-точки до/после: перенос должен сохранить их как есть.
- `GameTagsTests.Prefabs_TagsMatchRules` на dev красный (AK105, Mk14, MP5K без тега `Weapon`). Пересборка его
  восстанавливает; данные префабов исправятся, когда арсенал пересоберут и закоммитят.
