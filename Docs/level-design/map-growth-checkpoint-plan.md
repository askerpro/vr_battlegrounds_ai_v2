# План коммита текущего состояния выращивателя

> Для следующего исполнителя: выполнять по шагам с `superpowers:executing-plans`. Это план подготовки снимка незавершённой работы, не разрешение начать новый функционал, сохранить чужую сцену или объявить фичу принятой.

**Цель:** сохранить свой код и точку продолжения в Git, а полный локальный отчёт — отдельно по правилам AGENTS.md, чтобы следующий агент не повторял подготовку и проверки.

**Архитектура сохранения:** один scoped Git-коммит текущего среза. В Git входят исходники, актуальные тематические документы и план с кратким статусом; raw, XML, screenshots, snapshots, временные probes и подробный ledger остаются локальным игнорируемым пакетом.

**Инструменты:** Git/Plastic, PowerShell, native apply_patch, Unity MCP только если позже отдельно разрешён проверочный пакет.

**Спецификация:** AGENTS.md, [версионный контроль](../version-control.md), [план реализации](map-growth-implementation-plan.md), [контракт выращивателя](map-growth.md).

## Проверенная исходная точка планирования

- Рабочая копия: `F:/UnityProjects/Vr_Battlegrounds_ai`, ветка `dev`.
- HEAD при подготовке плана: `49cac57289413629c3a1eead5e13bcb20a072a36`; он уже отличается от раннего checkpoint `55e40873`. Перед stage/commit снять актуальный HEAD снова.
- Изолированная подготовка: `C:/Users/asker/.codex/worktrees/map-grower/Vr_Battlegrounds_ai`, ветка `codex/map-grower`, BASE `edd6d5646c7d9f5ae9ee70894e6661ae9e18a544`.
- Исходный итоговый манифест `tmp/map-growth-preflight/transfer-manifest.json`: 102 пути, 66 own-source/metadata и 36 shared source/docs. При сверке нет отсутствующих файлов; изменились только общие Docs/README.md и Docs/CHANGELOG.md. Это не разрешает коммитить shared files целиком.
- Кандидатов с незакоммиченными изменениями по манифесту и делегированной правке теста — 103. Новый файл данного плана учитывается дополнительно. Точные пути и свежие hashes сохраняются в локальном `Docs/tasks/report/map-growth-01a0fa23/commit-plan-inventory.json`.
- Index при планировании пуст. Никакого stage/commit, Unity-прогона или сохранения сцены в рамках составления плана нет.

## Ограничения

- Не брать весь dirty checkout; не использовать `git add .`, широкие каталоги или `git add -f` отчётов.
- Shared-файлы собирать только своими доказанными hunks, сохраняя чужие записи и код. Предыдущий полный SHA служит аудитом интеграции, а не доказательством авторства всего файла.
- Префабы, карты, Asset Store/SDK, оружие, SRM materials и hand evidence в этот scope не добавлять.
- Не переносить старый BlockoutPainter из prep целиком поверх основного файла.
- Не переименовывать ветку и не переписывать чужие коммиты ради снимка.
- Генерируемые отчёты игнорируются Git. Следующий агент в новом clone получит код/план/краткий статус; для полного raw нужен отдельно сохранённый локальный пакет.
- Проверка новой игровой логики человеком и постоянные новые тесты остаются отдельно. Текущий запрос — подготовка коммита текущего состояния, а не принятие механики.

## 1. Зафиксировать точный scope и зависимости

- [ ] Перепроверить HEAD, index, статус своего prep и основного checkout перед началом сборки.
- [ ] Снять отдельный before guard чужого diff и не изменять его при stage/commit.
- [ ] Сверить 102-path манифест с current HEAD/worktree: отделить уже включённое в HEAD, свои новые файлы, свои hunks и чужие hunks.
- [ ] Использовать историю narrow transfers/patches и diff prep относительно BASE для проверки авторства shared изменений; текущий full-file hash не подменяет эту проверку.
- [ ] Включить делегированную правку `Assets/Tests/EditMode/Maps/PhysicalArenaColliderTests.cs`, сохранив явный статус NUnit после неё: pending.
- [ ] Проверить замыкание зависимостей предполагаемого коммита: новый код не должен полагаться на чужие незакоммиченные объявления/ассеты. При обнаружении записать точную зависимость и решить её scope до коммита; не включать чужую работу автоматически.

Состав по группам, с точным списком в inventory:

| Группа | Кандидаты |
|---|---|
| Собственные новые компоненты | `MapGrowth*.cs` и их metadata в LevelDesign; runtime `BlockoutGeneratedSet.cs` и metadata |
| Служебные компоненты среза | BlockoutContainerHierarchy, BlockoutMarkupMigration, BlockoutPositionHandles, MapEvaluationRunner/Work, MapGridBuildOperation, PositionImpactFingerprint, PositionRouteGeometry и metadata из манифеста |
| Общие точки интеграции | BlockoutMarkup/Painter/RegistryFactory/HeightGeometryEditor, BlockoutSectionGeometry, MapAnalyzer/MapCellFootprint/MapEvaluation/MapEvaluationScene/MapGrid/MapGridBuilder/MapSpatialMetrics, PositionImpactAnalysis/Analyzer/Model и PositionRouteAnalyzer — только собственные hunks |
| Правка старого ожидания | PhysicalArenaColliderTests: спавны принадлежат Gameplay конкретной карты |
| Документы | map-growth.md, map-growth-implementation-plan.md, blockout-editor.md, этот checkpoint plan; собственные записи README/CHANGELOG |

Новые файлы рядом с названными группами не включать по wildcard без проверки inventory.

## 2. Собрать локальный пакет отчётов

**Назначение:** `Docs/tasks/report/map-growth-01a0fa23/` — локальный, игнорируемый каталог.

- [ ] Скопировать `tmp/map-growth-preflight/` в `preflight/`, включая full final-evidence, официальный NUnit baseline, передаточные manifests, probes, canonical references, CSV, images и scripts. Сохранять вложенность, чтобы внутренние относительные ссылки продолжили работать.
- [ ] Скопировать `tmp/spawn-hierarchy-tests-20261005/` в `spawn-hierarchy-tests/`.
- [ ] Сохранить необходимый временный numeric harness из prep `tmp/map-growth-preflight/` в `preparation/`; исключить воспроизводимые build products bin/obj и внешние DLL. Записать исходный путь и зависимости harness, не обещать его переносимость без установки Unity/dependencies.
- [ ] Скопировать собственный подробный ledger `.superpowers/sdd/map-growth-implementation-plan/progress.md` из prep в `progress.md`.
- [ ] Создать локальные `index.md` и `status.json`: scope, BASE/HEAD, пути основного/prep checkout, реализованное, доказательства по версии, непроверенное, блокеры, следующий bounded пакет, запрещённые действия.
- [ ] Создать manifest SHA-256 всех файлов итогового пакета. Исправлять только локальные index links; исходные raw/XML не переписывать.
- [ ] Проверить существование ссылок, совпадение hash после копирования, UTF-8 и `git check-ignore`. В Git не включать пакет даже принудительно.

Минимальный сохраняемый контекст:

- Автор задаёт позиции/позы, один контакт пары с двумя направлениями, коридоры/порталы и стартовую фиксированную геометрию; блок не равен позиции.
- Реальная геометрия проверяет коллизии/обзор/прострел; клеточный след используется для поиска/маршрутов. Секции, материал и исходная толщина сохраняют смысл.
- Снимок/поиск/preview/Apply используют общий backend; default fixed, замена только явно выбранных generated-корней, Undo/Redo и source staleness.
- Сохранённый immutable verdict не заменяется редактируемым display report; full-ray saturation не считается свободным лучом.
- Экспериментальный LOS batching выключен по умолчанию; shot остаётся синхронным. Ускорение на коротком corpus не подтверждено.
- Не повторять выполненные переносы или копировать prep Painter целиком; не коммитить generated reports, не трогать чужой Play/dirty scene и соблюдать немедленный release.

## 3. Обновить tracked план и тематический статус

- [ ] В `map-growth-implementation-plan.md` привести начало и checkbox к текущему состоянию: первый прототип реализован; отдельные gates всё ещё открыты. Добавить краткую «точку продолжения».
- [ ] В map-growth.md/README оставить текущий контракт и краткие принятые выводы, убрать ложную готовность/переносимый raw либо устаревшие состояния.
- [ ] Указать локальный пакет как **локальный отчёт**, не как файл, доступный в Git clone.
- [ ] В CHANGELOG добавить краткую запись о сохранённом срезе, а не журнал всей дискуссии.
- [ ] Сохранить следующие статусы явно:
  - Android/Native Apply20, golden17, ownership13 подтверждены на соответствующем срезе; версии и raw — в локальном пакете.
  - Старый стандартный baseline: 51 case, 50 Passed / 1 Failed / 0 Skipped. Единственный отказ — прежнее Environment-ожидание спавнов.
  - Ожидание заменено проверкой шести сцен и Gameplay; Android после правки прошёл, NUnit пяти fixtures после правки — **56 cases pending**.
  - Последний блокирующий снимок: dirty Lobby/чужой lease; это старый снимок, следующему агенту надо заново проверить состояние.
  - Полный EditMode assembly gate, полный 20×200 × 1/2/4/8 × 3 benchmark и отдельные waiting-physics/hit-buffer counterexamples не закрыты.
  - Unity/Quest принятие, постоянные тесты новой логики и последующий релиз не завершены.

## 4. Собрать index и проверить checkpoint

- [ ] Stage только явно перечисленные own files и собственные hunks shared files. Не изменять worktree для удаления чужих правок.
- [ ] Просмотреть `git diff --cached --stat`, `--name-status`, `--check` и полный scoped diff. Raw/reports/Library/Temp/чужие файлы в index отсутствуют.
- [ ] Сверить staged source с последними доказательствами: исключённый чужой hunk может изменить компилируемость. Нативный PASS полного worktree не выдавать за PASS staged snapshot.
- [ ] Указать документально пределы проверки index snapshot. Если нужен новый isolated compile gate, подготовить его отдельно; общий Unity Editor для него не захватывать без prepared пакета и актуального lease.
- [ ] Проверить, что tracked план содержит достаточно инструкций продолжения без tmp/chat history, а локальный report package собран полностью.

## 5. Фактический коммит после утверждения плана

**Предлагаемый заголовок:** `feat(level-design): сохранить текущий прототип выращивателя карт`

Описание должно перечислять результат — визуальная разметка, поиск, общий отчёт, preview/Apply/замена,
и прямо назвать незавершённые gates. Не использовать формулировку «все тесты прошли» или «фича завершена».

- [ ] Выполнить один scoped commit по проверенному index; дополнительные незапланированные файлы не stage.
- [ ] Записать фактический commit SHA в локальном status/index и проверить `git show --stat`/состав.
- [ ] Сравнить чужой diff до/после; свой незакоммиченный остаток перечислить явно.
- [ ] Обработать штатный post-commit Git→Plastic hook по его фактическому результату. Если staged-only shared hunks оставили в том же файле чужие unstaged изменения, hook может пропустить весь перенос — это ожидаемая защитная проверка, не успешная синхронизация.
- [ ] Plastic проверять независимо; не заменять пропуск check-in всего workspace и не обещать sync до фактического readback. Отдельный перенос — только по точному авторизованному манифесту.

## Первый шаг следующего агента

Читать AGENTS.md → текущий план реализации/контракт → локальный index/status, если пакет доступен.
Затем сверить SHA своего checkout, чужой diff и свежие lease/Play/dirty.
Ближайший проверочный пакет — 56 старых NUnit cases после сохранения Lobby пользователем и освобождения редактора.
Не сохранять/очищать человеческую сцену ради тестов. После прогона capture/cleanup и немедленный release;
только затем анализ actual results и дальнейший scope. Общий assembly gate и полный benchmark остаются отдельными пакетами.

**На этапе этого плана:** подготовлен только план и его локальный inventory. Коммит не выполняется.
