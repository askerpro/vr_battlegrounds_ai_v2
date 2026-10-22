# 2026-10-22 — прогон 1 пересборки арсенала (worker, ticket 1029)

Ничего не закоммичено. Дампы, хеши и копии изменённых файлов — `../reports/run1/` (вне Git):
`<ствол>-{pre,post,changed,summary}.txt`, `compare-*.txt` (полное сравнение), `sem-*.txt` (`compare_dumps.py --semantic`).

## Перед прогоном

- Rebase на `origin/dev` a1b487666 (15 коммитов). Конфликты — в четырёх сборщиках и окне арсенала, с коммитом
  1b16cf21d («сборщик не регистрирует хваты аватаров, готовит позы и snap-точки `_orig`»). Взята логика dev:
  - режим `ReimportFromPack` сборщик отклоняет сам (`RequireBuilderMode`);
  - `PackOrigPoses.ExportForWeapon` перенесён внутрь `WeaponRebuildFinish.Run`: последний шаг после `Complete`,
    `Assets/Art/HandPoses/Orig` входит в копию. Отказ экспорта — отказ пересборки.
- `PoseReimportOutputs` удалён: перезаписи поз в сборке больше нет.
- Компиляция в worker без ошибок: `scriptCompilationFailed=false`, консоль чистая.
- MCP-прокси сессии шёл в worker `F:\CodexWorktrees\unity-editor-worker`, проверено через `execute_code`.

## Итог по стволам

| Ствол | Результат | После возврата зонда |
|---|---|---|
| R08, SRM12, MKR9 (kin) | собран, 16–46 с | 0 отличий |
| Scar, Revolver, Uzi, PPK, SniperRifle, MP5K (hands) | собран, ~10 с | 0 отличий |
| Viper, TR15, Herrington (kin) | явный отказ | 0 отличий |
| набор legacy | явный отказ на первом стволе BrowningHiPower | 0 отличий (см. дефект 3) |
| MP5K, `reimport = true` | явный отказ до копии: «сборка не регистрирует позы аватаров» | 0 отличий |

### Причина отказов Viper, TR15, Herrington, BrowningHiPower

`WeaponSystemAuthoring.Configure` отвечает: «Затвор без клипов допускает только ManualReturn/ReturnToRest».
Дефект порядка шагов существует с 3fd1ee9ad (2026-10-09, waves-f), правки задачи его не вызывают.
- `HandsPackWeaponBuilder.Build` в конце сразу настраивает хост WeaponSystem по профилю ствола.
- У стволов с профилем HoldOpen (Viper, TR15, BrowningHiPower) и у Herrington (ручное заряжание) клипы затвора
  (`WeaponMechanismVisuals`) ставятся позже: `KinemationFixReview` или `InstallMotion`.
- Без клипов `Configure` идёт по ветке «затвор без клипов» и отказывает.

До задачи такая пересборка оставляла перезаписанный магазин. Теперь откат возвращает его.

## Сравнение собранных стволов

Совпадают у всех девяти:
- прицел, сдвиг мушки, `SightFrontFrame/SightRearFrame`;
- меши `*_SightSplit/*_SightFront` (файлы не менялись);
- ShotSource, WeaponSystem, `UxrFirearmWeapon`, `UxrProjectileSource`, `RecoilAccumulator`, Rigidbody;
- `NetworkIdentity._assetId` оружия и магазина, баланс (WeaponInfo не менялся);
- записи хвата MEF и Cyborg, позы и база поз (файлы не менялись).

Меши KINEMATION по содержимому те же: хеш изменился только из-за CRLF рабочей копии. `MechanismMotion` — новый
`rid` managed reference.

Отличия:
1. **Тег корня оружия `Weapon` → `Untagged`** у всех, кроме MP5K. В Git MP5K уже `Untagged`: его пересобрали
   в 200236fe7. Сборщик пишет корень заново, а разметку тегов на этом пути делал только legacy. **Дефект, исправлен.**
2. **Точка отдачи `RecoilSourceAxes` и области подсветки `*Region.asset`** считаются по-новому, по центру ладони
   руки пака (1b16cf21d). Текущие префабы собраны по калибровочному хвату MEF. Сдвиг точки отдачи в метрах —
   1,8–2,4 см, в основном вправо по X. Первая оценка «0,5–11 см» была ошибочной: координаты взяты в масштабе корня
   без учёта его scale (у Uzi 0,21). Пересчёт — в changelog прогона 2. Это изменение dev, а не задачи.
3. **Snap-точки `Orig/*_orig`** и позы `Assets/Art/HandPoses/Orig/**` — новый шаг dev (1b16cf21d), объекты добавлены.
4. **Магазин:** исторические лишние объекты не воспроизводятся. Пропадают второй `ImpactSound` с `ImpactAudio`,
   неактивный `MagMachinegunGeo/GrabHighlight` и пара собственных `Grab (MEF_cyborg_hands)` с той же позой.
   Объяснение то же, что для AK105/Mk14/MP5K (`weapon-sight-calibration/changelog/2026-10-20-grip-rebuild-fix.md`).
5. **Вложенный магазин в оружии:** пропал override `_audioOnPlace = MagPlace`, остаток c56dd3e93. Появился второй
   неактивный `Mesh/GrabHighlight`, `_enableOnHandNear` указывает на него, первый никем не используется. У R08
   подсветка магазина переехала в `Mesh/MechanismVisual/GrabHighlight`.
6. Scar: пропал узел `Slide/Grabs/MEF_Base_Avatar/Main Grab Point`, на него нет ссылок.

## Дефекты рамки пересборки, найденные прогоном (исправлены в коде, прогон 2 не проведён)

1. **Откат оставлял в памяти несохранённые меши и области.** Например Viper: 13 ассетов. Общий SaveAssets
   записал бы их на диск. Теперь `WeaponRebuildTransaction` при `Begin` снимает список несохранённого
   (`OwnAssetSave.AllDirty`). Откат отбрасывает новое несохранённое (`OwnAssetSave.Discard` → `Resources.UnloadAsset`).
2. **Чужой dirty-материал пака.** После прогона в памяти остался несохранённым
   `ThirdParty/.../M_Herrington_11-87_Police.mat`, хотя при входе в аренду dirty-ассетов не было. Его меняет сам Unity
   при загрузке HDRP-материала пака, сборщик его не пишет. Это тот же материал, на котором раньше останавливалось
   окно арсенала. finish брокера отказал («чужой dirty-объект»), аренду закрыл `recover --force`.
   Теперь после успешной сборки `ReleaseForeign` отбрасывает несохранённое вне области сборки, появившееся за сборку,
   с предупреждением в лог.
3. **Набор legacy: движение preflight вне транзакции.** Preflight записал `MechanismMotion` всех трёх стволов
   (новый `rid`) до их пересборок, откат ствола их не возвращал. Теперь у движения каждого ствола своя копия.
   При отказе возвращается движение ещё не пересобранных стволов.
4. **Тег корня** (отличие 1): `WeaponRebuildFinish.Complete` размечает оружие и магазин через новый
   `GameTagsTool.ApplyToPrefab`, тело прежнего цикла `ApplyToPrefabs`.

## Инфраструктура

- Ticket 1029: checkpoint a9771a59 → claim → begin → guard. renew каждые 4 мин.
- `finish` отказал из-за чужого dirty-материала (дефект 2), аренда перешла в RECOVERY_REQUIRED. Исход прогона
  известен, диск worker чистый (`git status` пуст), отчёты скопированы. Закрыта `recover --force`.
  e2e-series уведомлён (11722).
