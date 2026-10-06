# Оружие и арсенал: план checkpoint-коммита

> Для следующего исполнителя: выполнять шаги последовательно по `superpowers:executing-plans`. Новые агенты для упаковки не нужны. Этот документ — план сохранения, а не разрешение повторить Unity writers или признание игровых механик принятыми.

**Цель:** интегрировать весь актуальный подготовленный код нашей задачи, проверить итоговый проект и сохранить его вместе с отчётом, прогрессом и решениями в коммите, после которого другой агент продолжит без истории чата.

**Архитектура:** точный список принадлежащих задаче файлов, последовательная интеграция подготовленных пакетов в их реальные runtime/SDK/Editor/Tools targets и переносимый handoff-пакет. Архив патчей сохраняет историю проверки, но не заменяет применение. Отсутствующие зависимости и найденные дефекты исправляются до финального коммита.

**Технологии:** Git, адресный Git → Plastic post-commit хук, Unity 6/UltimateXR/Mirror, существующие временные probes.

**Основание:** поручение пользователя сохранить текущую работу; `AGENTS.md`, `Docs/version-control.md`, `Docs/tasks/weapon-readiness-feedback-design.md`, `Docs/tasks/arsenal-generator-plan.md`, `Docs/tasks/shotgun-per-shell-research.md`.

## Границы

- Сейчас уточняется план исполнения; staging и commit ещё не выполнялись. Обязательное уточнение пользователя: «весь непримененный код должен быть применен и уйти в коммит».
- Пользователь прямо поручил кодовый коммит текущей работы после её применения. Это разрешение на WIP-коммит по данному плану; повторное разрешение на него не запрашивается. Оно не означает human acceptance readiness/дробовиков/генератора и не разрешает назвать механики проверенными в шлеме.
- Готовность коммита требует применения всего актуального own code, включая необходимые исправления зависимостей. Документационный checkpoint или сохранённые `.patch`/`.cs.txt` без интеграции не выполняют поручение.
- Сохранять чужие изменения, Play Mode, leases и действующий Authored-арсенал. Не использовать `git add .`, commit всего workspace, stash/reset/clean, массовые переименования или отключение хуков.
- Постоянные gameplay tests не добавлять до human acceptance. Сохранение уже подготовленных диагностических материалов не означает разрешения переписать тесты.
- Не считать отсутствие агента завершением задачи. На момент инвентаризации `list_agents` показывает только root; продолжение опирается на файловые артефакты.

## Зафиксированное состояние при подготовке плана

Ветка `dev`, HEAD `49cac57289413629c3a1eead5e13bcb20a072a36`, индекс пуст. `git status --porcelain --untracked-files=all` содержит 1248 записей разных задач. Это не список нашего коммита. `core.hooksPath=.githooks`; хук автоматически пытается перенести точные файлы HEAD в Plastic.

| Направление | Что подтверждено | Что сохраняется незавершённым |
|---|---|---|
| Readiness / ручное досылание | Этапы 1–3 приняты root в границах отдельных actual proofs; этап 4 имеет частичный NoAction-срез | Полная миграция/physical producer readbacks, этап 5, Unity/шлем acceptance и этап 6. Старые manual55/MP5K63 не доказательство всей новой системы |
| Основа генератора | Task1 принят: freshness RED13/7 → GREEN13/0, bounds10/0, nested10/0, catalog7/0, matrix82/0, исторический Android PASS | Runtime identities/composer/bootstrap/Relay/admission, production cutover, Quest/artist fit. FullDemo.Style не объявлять подключённым |
| SDK lifecycle генератора | Подготовлен, но не применён eight-file пакет; correction RED0/16 → GREEN31/0, standalone SDK/Game compile0. Отдельно выделен seven-file SDK patch | Network retirement требует Root **и** Container в captured map Scene и actual unload barrier. Retention не является finite cleanup GREEN; in-place rebuild не принят |
| Prefab Mode preview | Исправленный TMP-пакет, standalone compile0, offline41/41; импортирован только собственный temporary baseline probe | Новые product `ArsenalPrefabStagePreview.cs` и `ArsenalDecorationDescriptorEditor.cs` отсутствуют. Native RED/GREEN не выполнены. SourceGO удерживается после guard incident |
| Поштучные дробовики | Prepared diff29 файлов / 6 добавлений; SDK/game/editor standalone0; admission model22/0, batch model39/0 | Пакет не применён. Native/Android/network/Quest/human acceptance не выполнены. Retirement OFF: предел 32 accepted shells/weapon до cleanup |

Числа выше описывают сохранённые проверки на их inputs/версии. Их нельзя переносить на нынешние DLL или весь checkout без сверки SHA. Во время root-проверки preview manifest изменились три imported `Library/ScriptAssemblies` DLL; historical compile не стал current Unity GREEN.

### Incident и восстановление

В preview wave guard вернул `passed=false`, Play/compile были активны, но batch ошибочно продолжил импорт собственного probe. Lease затем истекла и была занята другим владельцем. Человеческий Play не останавливался; сохранность всех чужих runtime объектов не доказана. Четыре product preview файла не применены.

`tmp/arsenal-generator-proofs/stage4-preview-20261005/recovery-20261005.md` описывает последующий read-only recovery: Editor idle, probe загружен, own fixture paths/scenes отсутствуют в ограниченном census; в консоли отмечен чужой CS1061 `HandPoseEditorCapture.cs:72`. Это historical snapshot, не текущая диагностика редактора. `recovery-hard-stop.cjs` отделяет guard от writer и отказывает при false/unknown/stale/lost lease. Новое продолжение начинается со свежего readback и root recovery gate.

## План состава коммита

Конечный список сначала оформляется в `OWNERSHIP.json`, а не выводится из широкого каталога. Для каждого пути: владелец/источник поручения, prepared/applied/verified, base/current SHA256, `.meta`/GUID, зависимости, допустимость inclusion. Перед финальным staging в этом списке не должно оставаться актуального own target со статусом только prepared. Общий файл с чужими hunks до их согласования не включать целиком.

| Пакет | Состав и правило отбора |
|---|---|
| Применённая работа | Только доказанные own paths/hunks readiness, arsenal foundation/presentation/editor и относящиеся SDK patches. Prefabs/data — только по адресному ownership/readback manifest. Изменения соседних ботов, аватаров/hand-fit, карт, mirror material, пакетов, настроек и чужих SDK hunks исключить |
| Подготовленная работа, обязательная к применению | Exact shotgun29 diff+manifest, generator SDK8 (SDK7 и Network — последовательные волны), исправленный preview и его dependency hunks, актуальные task2/SRM диагностические helpers. Применить к настоящим source targets, исправить недостающие зависимости/дефекты и проверить. В manifest указать переход prepared → applied → verified |
| Документация | Принятые дизайны/планы, текущие progress/status/решения, actual root acceptance, incident/recovery, constraints, краткий physical checklist и инструкция возобновления. Общие README/CHANGELOG/SDK patch docs — только собственные согласованные изменения |
| Не включать | Library/Temp/Logs, compiled DLL/EXE, кэши Python, полные Editor.log, массовые PNG, чужие артефакты, содержимое всего `tmp` или `.superpowers`, личные настройки/lease-файлы |

### Переносимый handoff-пакет

Предлагаемые пути, создаваемые при исполнении плана:

- `Docs/tasks/handoffs/weapon-arsenal-2026-10-05/STATUS.md` — фактический результат и неготовые механики.
- `Docs/tasks/handoffs/weapon-arsenal-2026-10-05/PROGRESS.md` — отдельные статусы readiness 1–6 и generator 0–8; shotgun waves.
- `Docs/tasks/handoffs/weapon-arsenal-2026-10-05/DECISIONS.md` — принятые owners, root rulings, отменённые ожидания и границы разрешений.
- `Docs/tasks/handoffs/weapon-arsenal-2026-10-05/PROOFS.md` — actual/model/source/standalone/native/Android/human отдельно, inputs/hashes/date и пределы каждого результата.
- `Docs/tasks/handoffs/weapon-arsenal-2026-10-05/NEXT.md` — точные первые действия и stop conditions следующего агента.
- `Docs/tasks/handoffs/weapon-arsenal-2026-10-05/OWNERSHIP.json` и `MANIFEST.json` — адресные пути, hashes, applied/draft, ссылки на evidence и base HEAD.
- `Tools/Handoffs/weapon-arsenal-2026-10-05/` — минимальные воспроизводящие scripts/JSON/log excerpts и исторические review patches. Актуальный runtime/SDK/Editor код сохраняется в его настоящих source paths; исторический patch не является его единственной копией.

Сейчас progress в `.superpowers/sdd/*/progress.md` и proofs/drafts в `tmp/` игнорируются Git. Нужные сведения переносить в перечисленные tracked пути, переписывая ссылки. Не менять global `.gitignore` и не force-add всё игнорируемое дерево. Абсолютные пути и transient task IDs не должны быть единственным способом продолжить работу.

## Последовательность исполнения

### 1. Получить стабильный срез и ownership

- [ ] Повторно снять HEAD/branch, индекс, status, список агентов и lease. Если own Unity операция ещё идёт, дождаться её завершения/отчёта и штатного release; не прерывать чужую работу.
- [ ] Сопоставить candidate пути с root acceptance/agent manifests. Для shared файлов выделить own hunks и объяснить зависимости. Unknown ownership исключить из product selection и явно записать в отчёт.
- [ ] Собрать `OWNERSHIP.json` и сохранить исходные current hashes до подготовки staging. Не публиковать приведённые 1248 записей как approved set.

### 2. Интегрировать весь подготовленный код

- [ ] По manifests перечислить все актуальные prepared targets preview, SDK lifecycle, Network identities, дробовиков и диагностических helpers. Для каждого определить единственный итоговый вариант. Старые отклонённые revisions сохраняют статус historical/rejected с причиной; их не накладывать поверх исправленной версии того же метода.
- [ ] Сначала восстановить incident gate preview: fresh Editor/Console/source readback, literal passed guard, отдельный writer call, собственная lease и поддержание её во время фактического импорта. Устранить собственные integration blockers; чужие ошибки атрибутировать и согласовать с их владельцем.
- [ ] Preview wave: применить два новых Editor source+meta и narrow `ArsenalEditorActions.cs`/`ArsenalPriceTag.cs` hunks; исполнить valid fixture RED → same GREEN, preservation/cleanup и Android. Null dependency ports и недействующий baseline harness не могут быть конечным source состоянием.
- [ ] SDK wave: интегрировать исправленные семь Core source файлов с sdk-patches записью, сверить штатные weapon/snapshot paths и текущую компиляцию. Network identity target применять следующей волной с исправленным размещением Root+Container и контрактом actual map-scene retirement; недостающий producer/barrier завершить до принятия этого среза. Одного удержания allocations до конца Play недостаточно для заявления об исправленном lifecycle.
- [ ] Shotgun wave: rebase prepared29 target hunks на текущие SDK/readiness источники, применить SDK → game/network/supply → editor/authoring последовательными собственными lease-пакетами. Провести addressed FABARM/Herrington migration по preflight и exact fixture/readback. Автоматические builders не должны возвращать прежнюю magazine-as-shell механику.
- [ ] Актуальные own diagnostic helpers довести до безопасных исполняемых средств в соответствующем Editor/Tools маршруте. Unsafe SRM/identity fixture не запускать ради выполнения этого пункта: исправить её allocation/ownership/teardown prerequisites и сохранить честные runtime proof limits. Исторические faulty runners не становятся новым product owner.
- [ ] Все пересекающиеся `WeaponInfo`, supply, profile/controller, SDK и shared docs hunks интегрировать последовательно. После каждой волны обновлять current/base hashes последующих пакетов; не переписывать чужие изменения старым prepared файлом.
- [ ] Проверить `OWNERSHIP.json`: каждый актуальный prepared target находится в настоящем проекте и отражён в финальном diff. Если один срез пока заблокирован, продолжать независимые волны; финальный кодовый коммит всего запрошенного среза ещё не готов.

### 3. Сохранить итоговый контекст и необходимые доказательства

- [ ] Создать handoff-пакет; перенести progress и ключевые решения из ignored файлов. Сохранить stage1/stage3 root acceptance, generator task2/SRM NO_GO, SDK contract-v2/producer-retirement, preview incident/recovery и shotgun batch/admission limitations.
- [ ] Сохранить итоговый source manifest и минимальные evidence всех применённых волн. Исторические pre-apply manifests не выдавать за current `Assets`; у новых native результатов указать реальные outputs/inputs/cleanup.
- [ ] Подготовить к коммиту настоящие own applied source targets. Review patches и снимки входят только как вспомогательные материалы; пакет не переносит посторонние hunks workspace.
- [ ] Актуализировать тематические статусы: README сейчас содержит старые «этап 3 ожидает» и «дробовики ждут полного readiness»; окончательное ожидание отменено прямым запуском дробовиков. Не вернуть старую очередь/напоминание.
- [ ] В `NEXT.md` сохранить accepted single-source/fixed-size/fallback artist flow, единственный SDK M/C owner, server-arbitrated shell admission и закрытые production gates. Повторного design approval не требуется.

### 4. Проверить интегрированный проект и восстановимость

- [ ] Все относительные ссылки handoff разрешаются в будущий commit/package. Предыдущие `tmp` ссылки либо переведены, либо явно обозначены историческими внешними inputs.
- [ ] Hash manifest совпадает с итоговыми применёнными targets; prepared-only актуальных targets нет. Нужные новые Unity ассеты/скрипты сопровождаются exact `.meta`, foreign GUID не заимствуются.
- [ ] Проверить восстановимость настоящего финального diff на указанной базе без записи в чужой checkout. Apply-check одного архивного patch не заменяет fresh imported-source verification.
- [ ] Получить свежую Unity compilation/Android проверку на последних integrated hashes и релевантные native integration/readbacks под own lease. Старые standalone/model GREEN не закрывают этот gate. Конкретный blocker записывается и устраняется; финальный коммит не объявляется готовым с неприменённым срезом.
- [ ] Постоянные gameplay tests оставить после human acceptance; временные диагностические проверки, источник каждого результата и неподтверждённые Quest/live-client сценарии перечислить в отчёте.

### 5. Подготовить reviewable staging

- [ ] Подготовить точные product paths вместе с документационным handoff-пакетом. Сохранить итоговый staged diff/stat и перечень excluded foreign paths. Отдельный docs-only commit не заменяет обязательный кодовый commit.
- [ ] Итоговый коммит: `wip: интегрировать подготовленный код оружия и арсенала`. Включить весь применённый own source/data/meta и статусный пакет. Отметить открытый human acceptance в body; это не release и не выполнение всех будущих этапов.
- [ ] До выполнения commit: `git diff --cached --check`, проверка списка staged paths, сравнение staged/current bytes и Unity lease/state. Смешанный shared file с оставшимися unstaged hunks требует отдельного согласования состава; hook иначе пропустит Plastic mirror.

### 6. Выполнить commit и адресную проверку Plastic

- [ ] При исполнении этого плана применить уточнённое поручение пользователя без повторного запроса WIP-разрешения. На текущем шаге сохранён только уточнённый план; фактический commit ещё не выполнен.
- [ ] Выполнить точный Git commit, сохранить SHA/message/file manifest и результат `.githooks/post-commit`. Git success не означает Plastic success.
- [ ] Read-only проверить `cm status --nochanges` и нужный changeset через `cm find`. Mirror обязан содержать только paths данного HEAD и соответствующие bytes; не делать отдельный check-in всего workspace.
- [ ] Если mirror пропущен/отказан, записать причину. Не повторять hook после изменения HEAD, рассчитывая перенести старый commit: он берёт только текущий HEAD.
- [ ] Проверить, что остальные working-tree изменения сохранены. Список foreign pending paths — факт сохранности, а не повод включить их в commit.

### 7. Передать продолжение

- [ ] В финальном отчёте: commit SHA, точный scope, Git/Plastic раздельно, путь `NEXT.md`, полный applied manifest, что verified/pending, реальные ограничения и human acceptance checklist. Актуального prepared-only кода в запрошенном срезе не осталось.
- [ ] Первый этап нового агента: прочесть `AGENTS.md` → `STATUS/DECISIONS/OWNERSHIP/NEXT` → проверить HEAD/свои source hashes и Editor state. Не возобновлять всё по одному слову «completed».
- [ ] Preview/SDK/shotgun source интеграцию и её подтверждения передать как результат данного исполнения, а не как неисполненную очередь следующему агенту. Если работа прервана раньше коммита, честно сохранить промежуточный статус с перечнем оставшихся targets.
- [ ] Runtime generator: отдельно описать состояние дальнейших composer/admission/production задач, не подменять native completion событием dispatch и не объявлять WIP-коммит готовым gameplay cutover.
- [ ] Дальнейшее продолжение: совместная физическая приёмка readiness/дробовиков, ещё не реализованные пункты generator plan, затем разрешённые постоянные gameplay tests. Старое ожидание запуска дробовиков и напоминание не восстанавливать.

## Критерий готовности checkpoint

Весь актуальный подготовленный own code интегрирован в настоящие проектные targets и включён в коммит с `.meta`/data/SDK documentation. Другой агент получает исходники, проверенный integrated manifest, отчёт и точный дальнейший gate, а не задачу применить оставленные черновики. В отчёте нет «задача завершена», «проверено в шлеме» или «Plastic синхронизирован» без соответствующего доказательства.
