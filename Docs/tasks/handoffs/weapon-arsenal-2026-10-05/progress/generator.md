# SDD ledger — plan: Docs/tasks/arsenal-generator-plan.md

## Управление

Пользователь 2026-10-04 прямо поручил запустить рефактор генерируемого арсенала. Root — менеджер/reviewer; продуктовый код выполняют специализированные агенты. Архитектура и продуктовый flow приняты ранее, повторное разрешение не требуется.

Ruling: используем согласованный общий checkout `add-guns` и открытый Unity workspace — здесь находятся необходимые совместные незакоммиченные зависимости. Отдельная копия не соответствует текущему Editor. Изоляция обеспечивается точным владением файлами, tmp drafts и последовательными Unity leases; чужие изменения сохраняются.

Ruling: выполняем актуальный порядок плана 0 → 1 → независимые 4/5; runtime 2/3 после upstream primitive handoff; 6 complete admission → 7 production cutover. Принятый pure resolver не ожидает завершения рефактора оружейной перезарядки, потому что её SDK/weapon files не входят в его write set.

Ruling: MapRunConfig/MapRunKey/MapRunScope уже существуют в Assets/Scripts/Maps/Runtime, вопреки старой строке design-only в документах. Их наличие не доказывает завершённый admission API. Не создавать shadow run types; перед runtime срезом нужен актуальный producer handoff и проверенный контракт.

Ruling: final visual handoff принят как вход для независимой подготовки. Root проверил manifest27/27, summary backend110/0, Inspector24/0, Android Passed/Errors=[], image20slots/315meshes/native workspace unchanged, fixtureResidue=false. Artist all20 fit=false, productionLinked=false, FullDemo.Style null; не выдавать этот checkpoint за финальную художественную компоновку или generated production. Исходные raw-domain-reload preservation limits сохраняются.

Ruling: permanent gameplay tests и кодовый commit только после пользовательской Unity/шлем приёмки; текущие проверки — временные probes, compile и actual readbacks. Production destructive migration недопустима до complete admission GREEN и независимого ревью.

## Preflight: согласованность этапов

| Задача | Согласованность и решение |
|---|---|
| 0 | Baseline/handoff до product writes; собственный временный Editor runner допускается под lease. Известный уже исправленный Card Ensure не объявлять RED. |
| 1 | Pure immutable description/metadata, Preset composition и Style poses единственные owners; минимальный StationKey binding без map lifetime. |
| 2 | Actual upstream run primitives и task1 GREEN до identity API; SDK patch только по отдельному доказанному отказу. |
| 3 | Installed generated arrays gated и dormant до admission; никакой ранней выдачи. |
| 4 | Единственный resolver, visual-only transient Prefab Mode overlay, unsaved artwork не перезагружать и не сохранять автоматически. |
| 5 | Resources/dry run без удаления production slots или opt-in; не scale artwork. |
| 6 | Bootstrap/network owner интегрирует actual run+registered refs; current snapshot не ждёт yet-unspawned avatars. |
| 7 | Complete admission GREEN до opt-in/remove/rename; GUID/external refs/addressed saves сохраняются. |
| 8 | Human acceptance до постоянных tests/commit; runtime/Quest/network пределы подтверждать отдельно. |

| Общая граница | Producer → consumer | Gate/владение |
|---|---|---|
| 0 → 1/4/5 | Native visual/source baseline → metadata/preview/resources | Root review реальных входов, не blanket proof raw8880. |
| 1 → 2/3/4/5/6 | Description/roles/layout hashes → identities/composer/preview/adapter | Immutable API один writer; no copied Preset/Style master. |
| 1 ↔ bootstrap | CompositionBinding StationKey → MapRoot refs | Generator owns binding; upstream owns MapRun lifetime и admission. |
| 2 → 3/6 | Expected UID assignments → local registrations/Relay | Dormant native proof/cleanup, no NI per generated slot. |
| 3 ↔ 4/6 | Runtime result и visual overlay → parity/admission | Один layout, разные materializers; no fake Ready. |
| 4 → 5 | Artist overlay/material/module edits → staged resources | Не сохранять transient objects в artwork. |
| 5/6 → 7 | Dry-run mapping + complete admission → production cutover | Fresh references/dirty checks before exact-owned writes. |
| 7 → 8 | Changed gameplay/artist flow → accepted regressions/release | Явное human acceptance. |

## Состояние

| Задача | Статус | Исполнитель / следующий gate |
|---|---|---|
| 0 | Принят как ограниченный вход task1: root actual JSON + manifest7/7 + Android Passed/Errors=[] | `tmp/arsenal-generator-proofs/stage0-root-review.md`; unloaded refs и UID ordering не доказаны, production migration не принята |
| 1 | Принят root как bounded foundation: freshness RED13/7→GREEN13/0; bounds10/0, nested compiler10/0, catalog readonly7/0, matrix82/0, Android PASS/console0; high re-review APPROVE, root SHA49/49 | `tmp/arsenal-generator-proofs/stage1-root-acceptance.md`; оба P2 закрыты, sourceUID/registration task2 и admission/production ещё впереди |
| 4 | Исправленный TMP preview/lease/formatter source-reviewed; compile0/offline41; root выдал адресный integration GO: native baseline → четыре source targets → same-input GREEN/Android/cleanup | `/root/manual_chambering_fix` исполняет конечные пакеты под own leases. Session-only Select/Queue между пакетами разрешён, UI берёт свою canonical lease; native результаты ещё pending, production Authored |
| 5 | После проверенного preview задачи4 | Staged resources/dry run без production opt-in |
| 2 | Исправленный TMP patch8files: correction RED0/16→GREEN31/0, SDK/Game standalone compile0; root SHA69/69/source8/8. Safe retirement удерживает allocations, полного finite cleanup ещё нет | `/root/readiness_stage3_review` (high) проверяет actual producer Scene/MapRun retirement barrier и same-scene cancel/rebuild; Network runtime SourceGO/admission ещё закрыты, без shadow authority |
| 3 | Ожидает native identity proof задачи2 и полного upstream lifetime/admission handoff | Существование run primitives не разрешает live activation/Ready |
| 6/7 | Ожидают upstream admission и isolated proof | Не мигрировать working Lobby заранее |
| 8 | После human acceptance | Permanent tests и разрешённый commit |

## Текущие границы

- Разрешён начальный scope: новые generator файлы в Assets/Scripts/Arsenal, собственный temporary runner в Assets/Editor/VR_Battlegrounds/Arsenal и Tools/Probes/ArsenalGenerator; stage1 каталог/метаданные после baseline review.
- SDK, WeaponInfo, weapon/mag prefabs, readiness/feedback, avatars, экономика, supply mechanics, runtime MapBootstrap/Relay файлы не входят в начальный write set.
- Visual Stand/Style/module — canonical inputs. Не переставлять user art targets и не переписывать authoring implementation без отдельного scope.
- Unity/MCP outputs: full reports в tmp, compactUnityResult/max_output_tokens2000, passed отдельно от success; executionCompleted=true запрещает повтор writer.
- Каждый Unity пакет заранее подготовлен; own cleanup и следующим шагом release до анализа/документов/ожидания.

## Передача исполнения

2026-10-05: root проверил исправленные source guards preview и разрешил конечную интеграцию задачи4 существующему medium: собственный baseline runner/isolated fixture, meaningful native RED до source wave, два новых Editor source+meta и narrow ArsenalEditorActions/ArsenalPriceTag hunks, then same GREEN/Android/exact cleanup. Source/artifact hashes preview совпали; три imported Library DLL устарели относительно manifest, исполнителю поручено обновить compile/reference provenance до применения. SDK corrections source/artefacts подтверждены, но allocation retention не считается завершённым cleanup: high продолжает узкий actual MapRun/Scene barrier contract. Действующие арсеналы остаются Authored; task4 SourceGO не означает SDK/runtime/production GO.

2026-10-05: пользователь разрешил интегрировать готовый срез. Root source review обнаружил незакрытые риски: StateSave dummy callback может повторно записать membership после terminal disposal; receipt root teardown не защищён от foreign descendants; повтор generated publication не проверяет original graph/frame. Preview также использует component whitelist вместо exact descendant ownership, а подготовленный harness ещё не подключает одинаковые RED/GREEN fixture inputs к новому consumer. Эти corrections переданы существующим high/medium исполнителям. Новое разрешение пользователя не требуется; source integration будет передана после проверяемого исправления. Production migration/admission остаются отдельными gates.

2026-10-05: пользователь прямо поручил продолжать план. Старый `/root/weapons_validation_integration` и новый исполнитель недоступны из-за `agent thread limit reached`; это не считается передачей. Успешно возобновлены два существующих агента: high `/root/readiness_stage3_review` для узкого generic SDK lifecycle-контракта задачи2 и medium `/root/manual_chambering_fix` для независимой задачи4. Прежний readiness/SRM scope последнего остаётся замороженным; оружейные и SDK файлы не входят в его новое поручение. Оба сначала готовят own TMP drafts; root проверяет diff до продуктовых writes. Native identity/cleanup, preview lifecycle, admission и production ещё не подтверждены.

2026-10-05: прежний auto approval blocker task1 corrections снят прямым текущим подтверждением пользователя «да продолжай рефактор арсенала и исправления». После него normal native apply_patch принят; выполнены scoped Description correction и actual proofs, оба P2 закрыты independent high review/root acceptance. Обходов review или повторения completed native writer не было. Ожидания дополнительного разрешения на эти corrections больше нет; очередь дробовиков не менялась.

2026-10-04: foundation high review BLOCKED (два P2): stale prefab refs перед новым Capture и отсутствие actual occupied usableBounds/frame selection oracle. Root проверил findings в source и выдал узкий correction GO в `tmp/arsenal-generator-proofs/stage1-review-correction-root-ruling.md`; before-Capture native RED ещё нужен. Task1 не принят, runtime/preview/migration не передавать. Existing medium owner получает corrections, canonical assets сохраняются.

2026-10-04: первый запуск clean-context `arsenal_foundation_review` отклонён с `agent thread limit reached`. После завершения readiness executor turn повторный запуск успешно создал `/root/arsenal_foundation_review`, gpt-6.1-sol/high. Независимые read-only ревью foundation и readiness stage2 теперь выполняются отдельно; зависимые generator tasks не передавать до принятого результата foundation. Root final manifest:36/39 core совпали, изменились только общие Docs/README.md, Docs/CHANGELOG.md и progress после соседних/root updates; текущие source/meta/catalog/proofs совпадают, не перезаписывать чужие документы ради старого hash.

Новый clean-context `/root/arsenal_generator_implementation` не создан: инструмент вернул `agent thread limit reached`. Не считать это запуском. Root успешно возобновил завершившего visual authoring `/root/weapons_validation_integration` с отдельным generator-поручением и прежними gpt-6.1-sol/medium. Его canonical pose/prefab контекст полезен начальному baseline; reload/SDK задачи ему не передаются. Model/effort не повышались из-за лимита. Новый plan ledger отделяет scope от прежнего authoring.

Поручение: task0 actual baseline/handoff/report принят root в его границах; task1 source GO передан с profile/projection ruling в `tmp/arsenal-generator-proofs/stage0-root-review.md`. Product source task1, runtime 2/3, preview4/resources5, admission6 и migration7 не объявлены выполненными. Manual readiness owner и существующий bootstrap peer получили явные границы владения.
