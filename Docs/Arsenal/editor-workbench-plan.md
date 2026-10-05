# Редактор арсенала

Единое окно **«Арсенал»** реализовано. Вход: `Tools/VR Battlegrounds/Arsenal/Редактор арсенала`.
Оно объединяет оружейные данные, станции и существующие маршруты сборки. Runtime-владельцы оружия,
сетевая авторизация и игровые меню остаются в своих системах.

| Вкладка | Что доступно | Область изменения |
|---|---|---|
| Каталог | Поиск по названию/ID, категория/источник, ссылки и баланс всех 20 WeaponInfo; упорядоченный draft ассортимента; назначение сохранённого ассортимента карте | Выбранные WeaponInfo/prefab, конкретный ArsenalPreset или MapData; общий bootstrap и регистрация отдельно в обслуживании |
| Станции | Общая игровая станция на 10 слотов (5/5), префаб станции лобби под полный ассортимент, якоря/карточки/габариты, стенд, запасные магазины, лобби и ниши активной карты | Конкретный префаб или захваченная сцена; массовая миграция Maps и Lobby явно отдельная |
| Сборка | Источники Hands/KINEMATION, чистая проверка, подготовка Read/Write, полный одиночный или явно выбранный набор, legacy bundle 3+3 | Оружие/магазин, generated Art, звук, позы и общая база; исходные ModelImporter .meta перечислены отдельно |
| Взаимодействие | Области хвата/приёма, напоминание, KIN motion и позы, внутренний патрон двух дробовиков | Узкие existing installer/importer routes; области общего материала и motion указаны до применения |
| Проверки | Структура каталога, чистая диагностика готовых KIN-префабов и явный экспорт снимка в tmp; SHA256, preflight, AndroidCompileGate | Диагностика без source constructor/reimport/Apply/SaveAssets; SHA worker читает файлы, Android пишет свой временный output |

## Правила работы

Выбор строки, вкладки, поиск и перерисовка не сохраняют ассеты и не выполняют `ConfigureAssets()`.
`WeaponId` и GUID определяют предмет; `DisplayName` служит подписью. Категория оружия не заменяет
`Pegboard/Shelf`. Порядок записей пресета сохраняется, поскольку задаёт индексы сетевых слотов.

Сохраняющая операция ставит подготовку плана в очередь, затем показывает входы, выходы и общие
зависимости. Применение доступно только после готового полного снимка SHA256/.meta. Изменение входов
после плана требует нового плана; повторный SHA перед writer выполняется в фоне.
Для набора рецептов чистый preflight проходит у всех участников до первого writer.
`ArsenalBuildPreflight` проверяет зависимости и роли источника; readiness KINEMATION читается через
`ReadableSourcePaths/RequireReadable`, без неявного reimport.

`ArsenalEditorActions` атомарно берёт собственную аренду общего Unity lock только по нажатию
пользователя. Уникальный token защищает cleanup от удаления заменённой чужой аренды. Prepare
держит аренду до завершения или отмены; Apply — на протяжении SHA-проверки, конечных guards,
writer и отчёта. Публичный `Actions.Run` сохраняет сигнатуру и синхронный контракт.
Чужой lock, Play Mode, компиляция/импорт, несохранённая сцена и
редактирование префаба блокируют writer. Истекший чужой lock не удаляется самим окном.
Несохранённые пользовательские native assets защищены; встроенные и импортированные shader assets
не выдаются за чужие изменения. Операция не сохраняет чужую dirty-сцену.

Сохранение пресета проверяет `PresetId`, оружие и магазин, уникальные ID/prefab, допустимые зоны
и вместимость карт-потребителей. Назначение карте меняет только ссылку `MapData`: станция сама
не расширяется. Сетевая регистрация проверяет GUID/NetworkIdentity до записи канонического менеджера.
Миграция/очистка используют сцену, захваченную при подготовке плана, а не поздний активный контекст.

После применения область результата показывает фактически изменённые файлы. При исключении видны
частичные изменения; это сообщение не означает автоматический откат всех существующих сборщиков.
После геометрии карт нужен общий `Bake Occlusion (all maps)`.

## Отзывчивость и отмена

`ArsenalFileSnapshot` читает полный набор файлов и .meta потоковым буфером 128 КиБ на managed worker
без Unity API. Нормализованные пути дедуплицируются без изменения SHA, missing markers и области
чтения. Workspace и immutable inputs захватываются на главном потоке. Окно показывает текущий путь,
число файлов, прочитанные байты и время; main thread не ждёт Task через блокирующий вызов.

Native AssetDatabase dependency closure остаётся полной и проходит по входам на отдельных Editor
updates. Один native-вызов нельзя прервать; он может задержать конкретную стадию. Подпись сообщает
последний завершённый вход и его измеренную длительность. Повторная подготовка из Repaint не запускается.

До writer доступны отмена и смена draft/вкладки; Play, импорт/компиляция, закрытие окна и reload
инвалидируют continuation. Перед writer на main thread повторяются scene handle/path, native dirty
read/output и editor guards. Global dirty scan блокирует чужое сохранение у маршрутов SaveAssets.
После старта существующего writer безопасная отмена без rollback не обещается. Ошибка сохраняется
вместе с actual output diff; при неудаче самого снимка отчёта явно указаны факт запуска writer и
неполный diff. После reload SessionState хранит только текст отчёта, никогда delegate или готовый план.

## Границы маршрутов

Поддержанного builder общей геометрии Common нет. «Полный ассортимент лобби» — исходный preset,
«Префаб станции лобби» — generated результат из сохранённого ассортимента и общей игровой станции.
Текущий маршрут: сохранить ассортимент, пересобрать префаб, обновить четыре станции лобби. При
рассогласовании окно объясняет: «Сначала пересоберите станцию по сохранённому ассортименту».
Старые пункты Arsenal не дублируют writers: работа начинается из окна,
публичные owner API сохранены. Общие GameTags, Occlusion и настройка аватара остаются в своих разделах.

Source rebuild Hands T-38 включает сборку, `ImportFor` и выравнивание отдачи. Source rebuild KIN включает
звук, geometry/magazine, позы/общую базу, recoil и motion/regions. Позы могут влиять на наследующие
аватары; общие KIN материалы, insertion geometry, normal-texture import settings и backing карточек
указываются как общие зависимости, а не скрываются под подписью «только оружие».

Browning Hi-Power, AR-15 и FABARM SDASS пересобираются фиксированным legacy bundle из immutable
шаблонов. Подготовка baseline создаёт Art/motion assets и не является чистой проверкой.
SDK-предметы без полного рецепта доступны в каталоге, но одиночная пересборка для них не обещается.
Анализатор прилегания кисти пока [исследование](../hand-pose-fit-analysis.md), не готовая проверка окна.

Принят следующий срез: один preset описывает станцию, сохранение запускает lock-aware очередь
генерации соответствующего prefab и адресного обновления связанных станций. Нужны debounce/revision,
детерминированные existing slot/component IDs и saves только своих outputs. Этот pipeline ещё
не реализован: срочное исправление SHA latency сохраняет текущие явные команды. OnValidate,
import callback и Repaint не являются writer-trigger; автоматическое сохранение чужих dirty scenes
и широкая миграция всех карт в этот срез не входят.

## Проверка и принятие

Срез отзывчивости: fresh Unity Editor assembly от 2026-10-04 06:36:11 UTC с новым worker скомпилирована
без ошибок. Чистый fixture подтвердил exact SHA/.meta/missing equality со старым чтением, case-path
dedup, отмену до Ready и инвалидирование при смене .meta. Actual очередь окна прошла 6/6: foreign
lease до тяжёлой работы, stale SHA до writer, cancel и own lease cleanup, stale scene, сохранение
writer exception с output diff, reload без auto-continue. За 9014 мс прошло 535 Editor updates;
единственный fixture writer изменял tmp, production writers 0. Артефакты:
`tmp/arsenal-ui-contract/snapshot-fixture/result.json`, `tmp/arsenal-ui-contract/responsive-window/result.json`.
Полный compiled worker через очередь самого окна прошёл фактические 1238 dependency paths,
2476 файлов/.meta и 3749932371 байт за 238452 мс. Прошло 30202 Editor updates, максимальная
пауза heartbeat 53.86 мс; прогресс в окне и отмена первого прохода подтверждены. Сцена осталась
неизменной, writer 0. Полное хеширование остаётся дорогим, но больше не удерживает main thread.
Пересчитанный native dependency closure совпадает с прежней полной областью 1238 путей, union
по входам совпадает с legacy-вызовом. Четыре отдельные стадии на прогретой AssetDatabase заняли
433/428/410/419 мс; это не гарантия длительности холодного native-вызова.
Артефакты: `tmp/arsenal-ui-contract/responsive-worker/result.json`, `tmp/arsenal-ui-contract/responsive-dependencies.json`.

Повторная проверка окна после импорта исправления: AndroidCompileGate PASS, Errors пуст,
Console 0 errors без очистки.
Все пять вкладок действительно перерисованы: SHA 834 файлов/.meta и 88 AssetDependencyHash сохранены,
путь/handle/корни/dirty/превью рабочей сцены не изменились. Контрактный прогон — 33/33, включая чистый
preflight 14/14 поддержанных рецептов. Нативный dirty Material, внедрённый
в диагностику, не был сохранён или изменён; 617 встроенных/импортированных объектов не стали ложным
блокером. Проверки outputs, всех read paths и stale SHA выполняются внутри аренды.
Счётчики рецептов и контрактов не складываются. Аудит 136 Editor MenuItem подтвердил один launcher
в Arsenal; четыре прежних публичных API сохранены и относятся к вкладке «Станции».
Снимки до/после пяти вкладок теперь берутся через async очередь; прогон занял 9983 мс,
1102 Editor updates. Артефакты: `tmp/arsenal-ui-contract/results.json`,
`tmp/arsenal-ui-contract/responsive-five-tabs/result.json`, `tmp/arsenal-ui-contract/responsive-android.json`.
Полные build/registration/migration writers этим прогоном не выполнялись. Итоговая регрессия/Bake и
последующий MP5K/Lobby-срез фиксируются в
[проверке T-39](../tasks/T-39-weapon-interaction-extension.md); эти результаты не заменяют применение
каждой destructive-команды на production-ассетах или приёмку UX в Unity.

Осталось принять пользователем окно, масштаб/оптику/карточки и оружейное взаимодействие в Unity/шлеме,
проверить доставку и владение запасом на двух живых клиентах. Новые ожидания игровой механики
закрепляются постоянными тестами после принятия по `AGENTS.md`.

## Полезные инструменты и совместимость маршрутов

После read-only ревью 15 оружейных MenuItem и root handoff сняты 14 дублирующих входов.
Единственный рабочий вход — `Tools/VR Battlegrounds/Arsenal/Редактор арсенала`. Общие меню карт,
тегов, аватаров и vendor tools остаются в своих подсистемах. Public builder/import/migration API
сохранены; удаление MenuItem не удаляет метод. `ImportAll()` больше не имеет прямой команды меню;
импортер и его public overloads не менялись.

`ArsenalWeaponDiagnostics.Capture(recipes)` читает готовые KIN-префабы: motion/bindings, grabs,
meshes и missing scripts. Отсутствующие prefab/component/mesh отображаются строками. Проверка
не создаёт `KinemationWeapon`, не вызывает `EnsureReadable`, Apply или SaveAssets.
Совместимый `KinemationFixReview.Report()` выводит результат через GameLog и больше не
перезаписывает исторический `Docs/tasks/artifacts/T-39-fixes-2026-10-03.json`.
Export — отдельная планируемая команда, пишет только `tmp/arsenal-weapon-diagnostics/kinemation.json`
и показывает время полученного снимка. Перерисовка не строит отчёт и не запускает export.

Низкоуровневые writer API требуют внешнюю own Unity lease, чистые inputs/outputs, preflight
всего набора и объявленную область. Signature compatibility не делает произвольный MCP/CI вызов
безопасным. Window предоставляет guards через существующую очередь и Actions.Run.
Source rebuild воспроизводит текущие geometry/pose/motion routes; баланс применяется отдельно
в Каталоге. Воспроизводимость новой readiness policy — отдельный producer integration этап:
source rebuild не подтверждает игровую готовность, второй post-build owner не добавляется.

Bootstrap «20/10» и canonical demo policy доступны как восстановление исходной конфигурации.
Развитие каталога использует явную регистрацию в каноническом менеджере; пользовательский preset
и будущий single-source generator имеют отдельных владельцев. Hands bundle 3+3 остаётся
поддержанным source rebuild; naming migration и подготовка baseline явно сервисные.

### Старый menu path → рабочий маршрут

В старых путях подразумевается префикс `Tools/VR Battlegrounds/`. Эти атрибуты удалены;
`execute_menu_item` по старой строке больше не работает. Операционные README/gameplay, T-38 и
`/add-weapon` обновлены. В ограниченном workspace поиске executable CI string callers не
найдены; неизвестные внешние потребители мигрируют по таблице. Automation использует
сохранившиеся API под внешней lease, а не имитацию нажатия writer кнопки.

| Старый путь | Вкладка / действие | Сохранённый API |
|---|---|---|
| Gameplay/Apply Weapon Balance | Каталог → баланс выбранного / всего каталога | WeaponBalanceApplier.Apply / ApplyAll |
| Gameplay/Weapons/Apply Interaction Regions | Взаимодействие → подсветка и напоминание выбранного | WeaponInteractionReview.Apply / ApplyAll / Preflight |
| Gameplay/Build FABARM SDASS From Hands Pack | Сборка → набор Hands 3+3; одиночного FABARM route нет | HandsPackLegacyRebuild.RebuildAll, HandsPackWeaponBuilder.Build |
| Gameplay/Build T-38 Weapons From Hands Pack | Сборка → явный выбранный набор | HandsPackWeaponBuilder.BuildFull |
| Gameplay/Hands Pack Weapon Report | Сборка → проверить источник Hands | HandsPackWeapon.Report |
| Gameplay/Hands Pack/Rebuild Legacy Three | Сборка → набор Hands 3+3 | HandsPackLegacyRebuild.RebuildAll / PrepareBaseline |
| Gameplay/Kinemation/Build TR15 | Сборка → выбранный TR15 из источника | KinemationWeaponBuilder.Build(TR15) |
| Gameplay/Kinemation/Build SRM-12 | Сборка → выбранный Desert Tech SRS (ID SRM12) | KinemationWeaponBuilder.Build(SRM12) |
| Gameplay/Kinemation/Build All | Сборка → явный набор поддержанных KIN recipes | KinemationWeaponBuilder.All / Build |
| Gameplay/Kinemation/Weapon Report | Сборка → проверить источник KIN; готовый prefab — Проверки | Report(recipe) с RequireReadable перед constructor; ArsenalWeaponDiagnostics.Capture |
| Gameplay/Kinemation/Revolver Shot From R08 | Сборка → обслуживание → звук Hands Revolver | KinemationWeaponBuilder.MakeRevolverShot |
| Gameplay/Kinemation/Review Fixes | Проверки → диагностика выбранного / поддержанных KIN | KinemationFixReview.Report / ArsenalWeaponDiagnostics.Capture |
| Gameplay/Migrate Legacy Weapon Names | Каталог → обслуживание → восстановить старые имена | WeaponNamingMigration.Apply |
| Avatars/Hand Poses/Import Hands Pack Poses | Взаимодействие → позы выбранного Hands, shared base объявлена | HandsPackPoseImporter.ImportAll / ImportFor / Import |

Malformed writer input risks из inventory (баланс неполного prefab, магазин без mesh,
частичный motion Apply) UI срез не исправляет; новые unrestricted writers не добавлены.
Production builders/migrations, gameplay tests, headset/two-peer acceptance переносом не
выполняются.

### Проверка tool/UI среза 2026-10-04

Собственный Unity прогон: 18/18 contracts diagnostics/API/menu/dirty/foreign lease/export,
семь сценариев очереди (stale SHA/scene, отмена, ошибка собственного tmp writer,
reload, foreign lease и явный export). Пять настоящих вкладок сохранили SHA 836 файлов/.meta,
89 dependency hashes и состояние сцен; за 13948 мс редактор обработал 1668 updates.
Нативный dirty Material и 18 исходных ModelImporter .meta сохранены без изменений.
Временный fixture asset, preview scene и callbacks убраны; production writers не запускались.
Свежий собственный AndroidCompileGate: PASS, Errors[], состояние активной сцены сохранено.
Первый RPC Android не вернул результат; повтор через prepared delayCall дал результат,
поэтому отсутствие ответа первого вызова не считается green.

Исходные результаты: `tmp/arsenal-tools-migration/verification/diagnostics-contract.json`,
`verification/five-tabs/result.json`, `verification/window-guards/result.json`,
`verification/android.json` (пути verification относительны папке arsenal-tools-migration).
После guard fixtures в консоли остаются два ожидаемых отказа stale SHA/partial tmp writer;
это не ошибки компиляции и консоль не очищалась. Actual Play refusal ещё не прогнан:
read-only проверка должна сопровождать согласованный чужой Play пакет, не подменять Lobby
и не прерывать человека. Эти результаты не заменяют UX приёмку или readiness/visual проверки.
