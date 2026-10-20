# Качество кистей и хватов MEF

**Задача/владелец:** `hand-rig-quality`, эксперт `avatar-grip`. **Статус:** закрыта 2026-10-20.
**Обновлено:** 2026-10-20 (закрытие задачи, перенос знаний в `Docs/`).
**Worktree:** `F:/CodexWorktrees/cyborg-hands`, ветка `codex/hands-rig-quality`.

## Задача закрыта 2026-10-20

**Решение пользователя 2026-10-20:** задача `hand-rig-quality` закрывается целиком. Направление
«анализ рига кисти MEF» закрыто. Направления 1 (автоанализ хвата), 3 (экспорт хватов из паков) и
4 (ручная доводка в редакторе поз) при возобновлении получают отдельные задачи; эта задача не возобновляется.

**Итог по этапам.**

- Влиты: `static-analyzer` (анализатор рига кисти 0.5), `expert-deployment` и `expert-registration`
  (эксперт `avatar-grip`), `mef-grip-audit` (аудит хватов MEF), `contact-metrics` (полосы контакта и
  правила грубых нарушений), `hand-graft-exp` (пересадка кисти SDK в скелет MEF), `sdk-hands-seam`,
  `cyborg-grips` (`d00f53667`, хваты `MEF_cyborg_hands`), `grip-extraction` (`e16b86b82`, Grip Extraction Tools
  и пилот), `grip-save-validation` (`846fa4bca`, нормализация `_assetId` после записи), `prefab-hierarchy`
  (иерархия `PlayerBase → CyborgHandsBase → Controllers/MEF → Black`, принята пользователем 2026-10-18).
- R2-этапы (`grip-extraction-r2-measure`, `grip-extraction-scoped-save`, `grip-extraction-r2`) и
  `grip-contact-authoring`: их код и ассеты пришли в `origin/dev` коммитом `1b2f88612` (2026-10-16) вне учёта
  хаба. R2 пользователь в шлеме (прогон 715) как готовый для продакшна не принял: неплотный обхват (G5).
  Часть поз R2 на dev в других версиях, чем результат 712 — сверка в [grip-extraction.md](../../Docs/grip-extraction.md).
- Не начаты и закрываются вместе с задачей: `grip-extraction-run` (R3–R5), `wrist-gap`, `grip-authoring`,
  `palm-validation`, `dynamic-probes`, `mef-correction`, `acceptance`, `documentation`.

**Поправки к записям ниже.**

- Прогон 772 (раздел «Текущая работа — SDK editor v3…»): прежнее утверждение «`GrabPoseCoverageTests` на всём
  боевом арсенале — PASS» неверно. Отчёт `reports/editmode-prefabs-test-report.json`: 624 теста, 568 PASS,
  56 FAIL, из них 43 — `GrabPoseCoverageTests` (жетон, служебные `*_SightReview`/`MKR9_NativeProbe_b1bbb91d`,
  у `PlayerControllersCyborgAvatar` на тот момент всё оружие). Разбор — G2 в
  [grip-known-issues.md](../../Docs/grip-known-issues.md).
- Билет 757: прежний `current_work` в `plan.json` ошибочно утверждал достижение численной цели
  (`numerical_targets_reached=true`), проверку UI (`UI_interaction_checked=true`) и два изменённых префаба.
  По `reports/sdk-contact-757-Review.md` и квитанции: цель не достигнута (Viper 8→9 из 15), UI не проверялся,
  изменённых ассетов нет. Запись «Последний native — 757» ниже верна.

**Где теперь знания** (актуальный источник; записи этого Readme — история):

- [hand-rig-quality-tool.md](../../Docs/hand-rig-quality-tool.md) — анализатор рига кисти (инструкция);
- [hand-rig-quality-mef-research.md](../../Docs/hand-rig-quality-mef-research.md) — итог исследования рига
  кисти MEF, направление закрыто; исходный отчёт —
  [source-mef-rig-research-2026-10-07.md](../hand-rig-analyzer-docs/source-mef-rig-research-2026-10-07.md);
- [grip-quality-analysis.md](../../Docs/grip-quality-analysis.md) — автоанализ хвата (направление 1);
- [grip-known-issues.md](../../Docs/grip-known-issues.md) — реестр дефектов хватов F1–F8, G1–G8;
- [grip-extraction.md](../../Docs/grip-extraction.md) — экспорт хватов из паков (направление 3);
- [grip-pose-editor.md](../../Docs/grip-pose-editor.md) — ручная доводка в редакторе поз (направление 4).

**Где отчёты.** `reports/` задачи ignored и вне Git; постоянное место — основной checkout
`F:/UnityProjects/Vr_Battlegrounds_ai/tasks/hand-rig-quality/reports/` (относительные ссылки `reports/...`
этого Readme работают из него). Перечень сохранённого, включая материалы из worker и scratchpad, —
`reports/_preserved-2026-10-20/MANIFEST.md`. Архивные Git-ветки `archive/avatar-grip/*` (локальные, в общем
`.git` продукта) защищают снимки R2 (695, 701, 712, 715), вход и результат 772 и вход 766; перечень — там же.

**Что открыто и куда.** Дефекты — в [реестре](../../Docs/grip-known-issues.md) (сводка со статусом на 2026-10-20
и направлением). Условия возобновления — разделы «Перед следующим применением» в
[grip-quality-analysis.md](../../Docs/grip-quality-analysis.md), [grip-extraction.md](../../Docs/grip-extraction.md),
[grip-pose-editor.md](../../Docs/grip-pose-editor.md) и «Перед первым использованием на новом аватаре» в
[hand-rig-quality-tool.md](../../Docs/hand-rig-quality-tool.md). Отложены: поддерживающая рука пистолета,
анимация курка.

## Журнал выполнения (история, до закрытия 2026-10-20)

Записи «Сейчас», «Текущий шаг», «Дальше» ниже описывают состояние на свою дату; итог — в разделе
«Задача закрыта 2026-10-20».

**Сейчас 2026-10-18 16:29:** пользователь принял ручную проверку иерархии/поз
и Play Mode и разрешил commit и вливание в origin/dev. Мелкие недочёты отложены
на следующую итерацию. Результат native807 сохранён через штатный recovery
собственной истёкшей аренды без force: `126ba84c11909e5364319e185b2eb24942eea1db`.
Все 55 изменённых путей доставлены в свой checkout точным Git diff; каждый blob
сверен с recovery snapshot, расхождений 0, затем выполнен штатный ack807.
Проверки цепочек обновлены после приёмки. Unity812 на `fa6d3c4d`:
19/19 AvatarHandPoseChain, 17/17 PrefabComposition, 60/63 AvatarLoadout;
итого 96/99, три отказа — Default-пол BotCombatStand вне teleport mask8.
Контроль815 на принятой базе `3a3a7340` воспроизвёл ту же причину у Controllers,
MEF с кистями киборга и прежних зарегистрированных MEF/Black (два job по два кейса).
Сцена и тест в нашем diff не меняются; этот отдельный дефект стенда отложен.
Ожидания тестов не ослаблены. Полные результаты: `test812-full.json` и
`baseline815-full.json` в `reports/prefab-hierarchy-20261018/`.
Обе аренды штатно DONE, paths/unexpected пусты. AutoSave восстановлен в исходное true.
Шесть независимых зональных ревью раунда2 — approve; замечания исправлены.
Последующие rebase не изменили source/assets diff. Фактические integration SHA
и состояние этапа ведутся в хабе (`status`, `task-brief-show`); старые этапы не закрыты.
Ревью выявило ошибку унаследованного TeamColorVariantBuilder: выбор поля
исправлен на UxrAvatar._prefabGuid (UxrComponent.__prefabGuid — другая metadata).
Полный scope плана дополнен изменёнными registry/team/архивными путями из WIP
и обязательными журналами зон; согласованная цель остаётся прежней.
Прежние записи ниже — история выполнения, а не актуальные блокировки.

**Сейчас 2026-10-18 15:45:** native807 r9 успешно сохранил целевую иерархию;
внешние44consumer/87полей/43override-набора прошли strict readback/rawguard.
Сохранены65поз, калибровка MEF1.72025037; bodySMR29/8/8,0missing bones.
Обе базы rig0; CyBase directPlayerBase, Controllers/MEF directCyBase, Black→MEF,
Ghost→Controllers. Orphan удалён nativeпослеproof, SDKHands перемещён в Unused.
Receipt `reports/prefab-hierarchy-20261018/ticket807-result.json`, SHA256
`82805c88930fae31dc30bb5f2d5c79679c196ec70478d7dac29137fa0b7df735`.
Visual3/3PNG без ошибок (`visual-r9/`), статический осмотр моделей проведён.
После delayed UxrUniqueIdPersister:53asset paths,0вне57outputs. Editor готов.
807 остаётся RUNNING по явному разрешению пользователя: открыты Avatar Workbench
и SDK Hand Pose Editor с рабочим Controllers,65поз, autoSave=false. Ручная проверка
пользователя идёт; accepted commit/новые tests пока не разрешены. Finish/receive
после её завершения и cleanup; source assets ещё находятся на worker.
Независимое статическое ревью:35weapon/tablet/SDK prefab;571authored grip строка
неизменна.31собственный prefab изменён только selectedAvatarPPtr;4SDK samples
дополнительно native-сериализовали defaults/physics schema (подробности SDK85).
Body inventory45/45 записей полностью совпадает до/после; это не runtime/Quest proof.
16:11 пользователь сообщил, что Play уже запущен; guard807 подтвердил is_playing=true,
responsive=true, tests=false. Агент Play не запускал/не перезапускал и не останавливает.
Ручная runtime-проверка пользователя продолжается; cleanup/finish только после неё.

**Продолжение 2026-10-18 15:30:** пользователь разрешил удалить orphan и запросил
ручную проверку иерархии/поз на worker после миграции. Native806 r8 завершён/получен,
delta[], result `7318fe838ee05ac8ee2faee62bbe1c2120a0509c`. Отказ ДО записи prefab:
postcondition DeleteAsset проверял исторический GUID cache. R9 проверяет live native
AssetPathToGUID(OnlyExistingAssets) и отсутствие обоих файлов, выводит все значения.
Ticket807 input `1581500ed27933a8941d6d53ea9bc9e756a50991`,57outputs. Guards внешних
PPtr/overrides и полный rollback сохраняются. Receipt806 `ticket806-result.json`,
SHA256 `bc1d43c1653469ce2bd8b96aa64736bdb01ae90fd742671aa1dfd3d5024bf8ec`.
Пользователь явно разрешил удерживать собственную аренду для императивной работы
и анализа при затруднениях пакета (параллельно работает один другой агент).
RUNNING lease/guard, собственные outputs, сохранность чужой работы остаются обязательны.

**Текущий шаг 2026-10-18:** native789 r6 завершён/получен, delta[], result
`5458c18ed41ae0576ac630292d483a5dd8e70b36`. PlayerBase LoadPrefabContents сохранил
IDs общих объектов: прежний отказ IndexThumbProximity пройден. Verify остановил
пакет позже, на несовпадении override-набора NonSDK `$/GameObject`; всё откатилось.
Receipt `reports/prefab-hierarchy-20261018/ticket789-result.json`, SHA256
`b55983cba4ff206296619702ae54ae1b6377377278be4953846878c912f06155`.
Подробности remap — `ticket789-outcome.txt`; требуется точный missing/extra diff
override-записей. Дополнительно проверяется сохранение Controllers root fileID:
он изменился, поэтому последующий строгий rawguard orphan пока не пройдёт.
Сборка r6 на свежих metadata собственной аренды:334sources/405refs,0errors/36warnings,
`reports/prefab-hierarchy-20261018/compile-playerbase-contents-r6-current/compile-result.json`.
Visual не запускался, пользовательская проверка открыта.
R7 ticket791 завершён/получен, delta[], result `f3b39da40809934a1e4ff0cf7ff1c8fff580dd04`.
Receipt `ticket791-result.json`, SHA256 `cae169fc39225745a482ae36ad9032bd63a76c4184ff36103ed1f4858a38cb46`.
Точная разница:727expected/727actual,406missing/406extra — только203пары служебных
UltimateXR `__isInPrefab` (1→0) и `__prefabGuid` (NonSDK→PlayerBase). Других отличий нет.
Ищется штатный владелец metadata/порядок native authoring; сравнение не ослабляется.
Актуальная сборка r7 в собственной аренде:334/405,0errors/37warnings.
R8 подготовлен: штатный SDK auto-ID scope с точным finally restore; UID/provenance
общих компонентов новой базы копируются из прежней CyborgHandsBase через owner API.
UID записываются как variant overrides и строго перечитываются после native save.
Транзакция завершается только после диагностики и cleanup preview сцены.
Offline compile334/405:0errors/36warnings (`compile-identity-r8-final/compile-result.json`).
Пользователь 2026-10-18 явно разрешил удаление DevicePrefabRegistry.asset/meta.
R8 включает native DeleteAsset после свежего proof и backup, затем новый снимок
активных потребителей. Raw guards не ослабляются, outputs57. До запуска assets исходные.
Смешанная offline-сборка до аренды снова показала только7bot API errors: metadata
от чужого checkpoint; это не результат компиляции r7 в Unity.

**Предыдущий отказ:** native787 r5 завершён/получен, delta[]. Он дошёл до
native save потребителей; итоговый Verify остановил пакет на изменившемся fileID
`PlayerBase_NonSdkHands: BigHandsIntegration/LeftHand/GrabberLeft/IndexThumbProximity`.
Полный rollback подтверждён, result `e1657f231f5b84edb31b38f97e9805395fe0baee`.
Receipt `reports/prefab-hierarchy-20261018/ticket787-result.json`,
SHA256 `cb0771adbef3435fda52288ea568b6d57355263499c35fc5a0a1974379dea90b`;
native plan `ticket787-reference-plan.json`.
Подтверждено: NonSDK не имеет added GameObjects; IndexThumbProximity inherited.
Следующий R6 проверяет корневое исправление: редактирование standalone PlayerBase
через LoadPrefabContents/Save в тот же путь; снимки всех потомков по-прежнему заранее.
Условный допуск drift не вводится, guards сохраняются. Альтернатива с inbound-index
отложена отдельным черновым artifact. Сохранение IDs пока не подтверждено native.
R6 ticket789 input `24c95d53f5b221c67b127a3de490c7958f1f944c`. Remapper
побайтово равен r5 (`21f613623e8aeb375c0de5546d8d52079857c3f83bcf825d5c23897cb1b0d01a`).
Первичная offline r6 была отложена из-за чужих metadata788; актуальная сборка
в собственной аренде789 приведена выше.
SavePrefab проверен: только canonical Mirror ID и native SaveAsPrefabAsset, без
переавторинга proximity. Visual r5 не запускался из-за отказа. Assets не приняты.

**Native-продолжение 2026-10-18:** MCP подключён. После rebase на `50c27ae71`
native781/782 завершены и получены; prefab delta обоих отказов пуст (781 добавил
только Unity-generated meta). 781 выявил preview-root ограничение Convert, исправлено
временным контейнером. 782 создал требуемые родители и остановлен guard изменившихся
внешних fileID. SourceBackup вернул исходники; никаких принятых изменений assets нет.
Реальные исходные показатели:65поз CyBase/MEF/Black; MEF/Black eyesBaseHeight1.72025037,
8 body renderers и0missing bones; Controllers1.83981407,29body renderers,0missing bones.
Архивы: `reports/prefab-hierarchy-20261018/ticket781-result.json` и `ticket782-result.json`.
Подготовлен native remap существующих полей/override-наборов и заранее снятый Ghost
Variant; exact broker outputs — `tools/cyborg-reference-outputs.json`. SDK reserve85.
Пакет r3: ticket784, input `bfe90b96ae62245e5fd8962179287dffa978f325`,
завершён и получен (`2e4516b73601b995467a415676f3cd52c0d0222d`). Native preflight
остановился ДО записи prefab: DevicePrefabRegistry.asset не предоставляет поля.
Его m_Script удалён в42aa82aec вместе с RolePrefabRegistry; выясняется статус orphan.
Delta784 — только meta remapper. Receipt `reports/prefab-hierarchy-20261018/ticket784-result.json`.
Native begin/guard: ready, не компилируется, compile errors отсутствуют. Повторная
offline-сборка после восстановления базовых metadata:334/405,0errors/36warnings,
`reports/prefab-hierarchy-20261018/compile-post784/compile-result.json`.
Пользовательская проверка открыта.
Read-only786 завершён и получен: input `7cb28c37853c17249fcd0644dbc806a14eb738f8`,
result `712a87234e0bfc513b396f9632dfe46a47428998`, delta[]. Native proof: script missing,
main=null,0valid objects,10225imported Assets/0inbound; asset/meta byte-identical.
Receipt `reports/prefab-hierarchy-20261018/ticket786-result.json`,
SHA256 `fe1cb98c532cc5d7ff6c47688157735c28ed5620318770006935b3129424826a`.
Попытка добавить исключение guard отклонена автоматической проверкой из-за отсутствия
реализации/фактического доказательства в предложенном действии. Исключение отключено;
миграция по-прежнему блокирует этот asset. Никакого YAML rewrite/удаления orphan нет.
Следующий безопасный вариант сохраняет orphan bytes без SerialObject remap при строгом
proof, но сохраняет исходный итоговый rawguard. Обе ссылки orphan обязаны сохранить
исходный Controllers root fileID/смысл; иначе rollback остаётся обязательным.
Повторная offline-сборка с обновлёнными чужим checkpoint runtime metadata дала
7 ошибок только в bot Editor-коде (BotCombatAssets/PoseAnimator); это смешанный
набор metadata, не результат сборки нашего checkpoint. Unity compilation проверяется
после штатного begin784. Предыдущий compile334/405 был без ошибок; тесты не менялись.

**Продолжение реструктуризации — 2026-10-18, Codex:** существующий WIP перенесён поверх
`origin/dev` `8dfccb580`; HEAD после rebase `ea76f68fa`. Worktree
`F:/CodexWorktrees/cyborg-hands`, владелец `avatar-grip`, задача `hand-rig-quality`.
Пользователь передал решение Antigravity: чистый `PlayerBase` → чистый
`CyborgHandsBase` (GUID `71b74b0d` и полный набор поз сохраняются) → две параллельные
ветки Controllers Cyborg и MEF cyborg player → Black. SDKHands архивируется.
Контракт: [cyborg-prefab-hierarchy-v2](contracts/cyborg-prefab-hierarchy-v2.md).
План: [продолжение](tools/cyborg-prefab-hierarchy-plan.md).

Статически обнаружены два незавершённых переноса: `CyborgHandsBase` всё ещё владеет
`MEF_Rig`, а `PlayerBase` сам содержит тело `Cyborg`. Общие `Camera Controller` и
`BigHandsIntegration` сохраняются, body bindings из общего каркаса выносятся.
Подготовлен native Unity writer с snapshots конечных аватаров, резервной копией,
проверкой внешних fileID и возвратом собственных outputs при отказе. Старые writers,
собиравшие MEF в общей базе, удалены. Canonical SDK alias теперь CyborgHandsBase.
Префабы в этом продолжении ещё не изменялись; новые/переписанные тесты не создавались.
Unity MCP tools отсутствуют; infra_issue `8986` передан сопровождающему через inbox,
его native route очищен. Самостоятельного ремонта/обхода и Unity-заявок нет.
Уточнение 2026-10-18: Desktop log текущего чата фиксирует в 04:44:26 МСК
`unityMCP status=failed`, `timed out awaiting tools/list after 30s`.
Rebase не менял `.codex/config.toml`, `.mcp.json` и proxy launcher; текущий
`codex mcp list` видит unityMCP enabled, elevated proxy `--help` exit0.
Конфигурация была обнаружена; причина отсутствующих tools — отказ startup discovery.
Один restart клиента допустим как повторная попытка подключения; tools/list и native
Unity readiness после него требуют отдельного подтверждения.
Offline-компиляция Editor assembly: **333 sources / 405 references, 0 errors / 36 warnings**.
Отчёт: `reports/prefab-hierarchy-20261018/compile-final/compile-result.json` и `compile.log`.
Статическое ревью обнаружило и подтвердило исправление root-Variant unpack; native
сохранение/ссылки/визуальное качество этим не проверены. Физические owned text files LF,
`git diff --check` без замечаний. Принятого commit/push нет.
Прежние PASS ниже относятся к старой иерархии и не принимают новую миграцию.

**Последний native — 757 DONE/received, 2026-10-15:** обе эталонные кисти15/15 материальных точек.
Viper v2 default priority0: контакт8/15→9/15, полная численная цель не достигнута; качество не принято.
1240 source hashes без изменений,17 artifacts SHA-проверены, worker освобождён.
Реальные изображения встроены в reports/sdk-contact-757-Review.md. SDK83 и product импортированы
в Unity без compile errors; UI-interaction/forward/Undo/runtime этим capture не проверялись.

**Native блокер 756 — 2026-10-15:** begin не дошёл до RUNNING:16 ошибок обратной ссылки
named HandPoseReview → default Editor. Capture/Apply/tests не запускались. Recovery завершён8741;
сохранены только8 Unity-generated meta из R d0e6e01aac9c1340090b7c55375ffbce295cad5a,
GUID перенесённого controller сохранён. Очередь освобождена сопровождающим.
Локально направление исправлено: controller в default GripAuthoring, named Review оставляет совместимый
фасад. Раздельно скомпилированы SDK108(0errors/21legacy warnings), Review24(0/0), product46(0/0,
включая diagnostic body). Это offline scope; готовится новый input/nonce r2 для Unity после recovery.

**Текущая работа — SDK editor v3 + X-Ray v4 + CyborgHandsBase (2026-10-16, влито в `origin/dev` `1b2f88612`) и перевод `MEF_cyborg_hands` в основные аватары команд (билет 772 DONE, ждёт проверки пользователя):**
- Влито fast-forward в `origin/dev` (`1b2f88612f563172db6c57249feae2f9d46d0491`, база worker обновлена в билете 771): рентген v4, анатомический IK-редактор контактов, дерево `PlayerBase_SdkHands -> CyborgHandsBase` и привязка всех 80 `UxrGrabbableObject` к `CyborgHandsBase`.
- В билете 772 (`R: b01d4c98a01f1996ac77dbade41d01d85e6c12cd`, worker освобождён сразу после проверок):
  1. Восстановлены полное тело MEF (6 `SkinnedMeshRenderer` LOD0..2 тела и головы), `LODGroup`, часы `WristWatch` на запястье, `TeamUniformColors`, `CorpseSource`, `_localDisabledGameObjects` (3 головы) и `_avatarRenderers` (6 мешей тела) на `MEF_cyborg_hands_Player.prefab` как чистом Prefab Variant от `CyborgHandsBase.prefab` (`PlayerBase_SdkHands -> CyborgHandsBase -> MEF_cyborg_hands_Player`).
  2. Выведены из `Experimental/` в основные `Assets/Prefabs/Player/MEF_cyborg_hands_Player.prefab` (GUID `2aa0ab6714e2413f9d829637b62df0c0` сохранён) и `Assets/Prefabs/Player/MEF_cyborg_hands/HandPoses/`.
  3. Создан второй цветовой вариант команды `Assets/Prefabs/Player/MEF_cyborg_hands_Player_Black.prefab` (GUID `60604d8891226f64592f880c9b2da883`) через штатный `TeamColorVariantBuilder` (`Assets/Editor/VR_Battlegrounds/Avatars/TeamColorVariantBuilder.cs`) как Prefab Variant от `MEF_cyborg_hands_Player.prefab` с заменой только 6 материалов LOD тела/головы (`MEF_Optimized.mat` → `MEF_Optimized_Black.mat`), и собраны оба рэгдолла трупов (`MEF_cyborg_hands_Player_Corpse.prefab`, `MEF_cyborg_hands_Player_Black_Corpse.prefab`).
  4. Обновлены `OptimizedMEF.asset` (`MEF_cyborg_hands_Player`), `OptimizedMEF_Black.asset` (`MEF_cyborg_hands_Player_Black`), `AvatarsRegistry.asset`, `Terrorists_Team.asset`, `CounterTerrorists_Team.asset` и `--- MANAGERS ---.prefab`.
  5. Неиспользуемые префабы MEF с оригинальными руками и экспериментальные ассеты перенесены с сохранением `.meta`/GUID в `Assets/Prefabs/Player/Unused/`.
  6. Прогнаны EditMode-проверки `VrBattlegrounds.Tests.Prefabs.*` (отчёт `reports/editmode-prefabs-test-report.json`) и `Persist UltimateXR Unique Ids`: `AvatarHandPoseChainTests`, `TeamAvatarsTests`, `TeamUniformTests`, `CorpseTests`, `AvatarRendererOwnershipTests`, `NetworkAssetIdOnDiskTests`, `RegisteredAvatarBodyTests` (все 7 проверок для `MEF_cyborg_hands_Player` и `MEF_cyborg_hands_Player_Black`) и `GrabPoseCoverageTests` на всём боевом арсенале для обоих новых основных аватаров. **Исправление 2026-10-20:**
     прежняя запись «— **PASS**» неверна. По отчёту прогон завершился `failed`: 624 теста, 568 PASS, 56 FAIL.
     43 отказа — `GrabPoseCoverageTests.У_каждой_точки_хвата_есть_хват_аватара`: жетон `DogTagPanel` и служебные
     `*_SightReview`/`MKR9_NativeProbe_b1bbb91d` у всех трёх аватаров на кисти Cyborg, а у
     `PlayerControllersCyborgAvatar` на тот момент всё оружие (29 из 43). Остальные 13 к хватам не относятся:
     телепорт на BotCombatStand (3), тесты паков HandsPack/Kinemation (7), `RegisteredAvatarBodyTests`,
     `UxrUniqueIdOnDiskTests`, `WeaponShotSoundTests` (по 1). Разбор — G2 в
     [grip-known-issues.md](../../Docs/grip-known-issues.md). Результат 772 через broker не получен
     (`accepted=false`) и заменён этапом `prefab-hierarchy`; снимок — ветка
     `archive/avatar-grip/mef-cyborg-promotion-772-result-unreceived`.
Контракт: contracts/sdk-hand-pose-contact-editing-v3.md.

**Текущее решение пользователя — 2026-10-15:** площадь отменена как критерий. Нужны автоматически
выбранные фиксированные опорные точки на ладонной коже в середине каждой фаланги и касание ±2мм.
Уточнение solver v2: конечные фаланги и ладонь тянутся к указанным материальным точкам объекта;
промежуточные касаются поверхности. DistalPriority0..1 default0 плавно ослабляет их штрафы,
при1 полностью отключает их, включая эквивалентный общий collision guard. Проникновения остаются
диагностикой. Контракт contracts/distal-palm-contact-ik-v2.md. Отдельный режим DistalPalmTargets,
bounded palm translation и окно шести object targets реализованы. После исправлений статического
ревью общая offline-компиляция42sources/406refs0/0; нативный solve и качество ещё не проверены.
Готовится режим одной кисти и проверенный зеркальный перенос второй. Наработки площади сохранены
byte-exact:24файла+SHA manifest в drafts/contact-area-20261015/; старые отчёты/PNG остаются в reports/.
Новый контракт contracts/phalanx-contact-points-v1.md. Прогон749 завершён и получен, worker освобождён:
правая кисть14/15 точек (missing thumb/proximal), зеркало11/15. Полный набор пока не готов.
Существующие HandMeshCaptureTests3/3 PASS; это не подтверждение качества нового метода.
Дальше: диагностика выбора/зеркала, предпросмотр исправленных точек, затем сравнение хвата до/после
с IK и пользовательская проверка. Новые/переписанные тесты, включая временные probes, отложены до неё.
Поддержка пистолета исключена из текущей оценки по решению пользователя: контакт с ведущей кистью
будет отдельным следующим шагом после подтверждения хороших хватов на опорных точках.
Отчёт с изображениями: reports/phalanx-contact-unity/749-Review.md.
Геометрическая диагностика755 DONE/received, Rf9e2aa0d64309f49a83e755ce37a5dfed579c6fc:
12native файлов SHA-проверены, assets/paths/unexpected пусты, worker освобождён.
Дамп геометрии и весов: reports/phalanx-contact-unity/geometry-20261015-r1/. Идёт разбор причины.
Fullskin collision — отдельный guard. Старые записи площади ниже относятся к сохранённому черновику.

## История R2 и предыдущих прототипов (история, до закрытия 2026-10-20)

Ниже сохранены результаты и ограничения прежних подходов; актуальные требования v2 и следующие
действия указаны выше и в plan.json/current_work. Площадь и прежние проверки не принимают v2.
После закрытия задачи актуальное состояние R2 — в «Задача закрыта 2026-10-20» и
[grip-extraction.md](../../Docs/grip-extraction.md).

**Решение пользователя после715:** «текущую работу как готовую для продакшна не принимаю».
Часто отсутствует плотный обхват рукояти всей поверхностью кисти и пальцев. Автоматический curl
и SDK-band `ok` не считаются критерием качества. Новых accepted commit/merge нет.
Пользовательские ориентиры: Viper, TR15 с глушителем, SRS Desert Tech, AK107 (точные prefab соответствия уточняются).
У ориентиров тоже остаются небольшие проникновения; анимация курка явно отложена.
Пользователь поручил реализацию IK ладони/подушечек субагенту и предложил метрику площади контакта.
Текущий этап `grip-contact-authoring`: фиксированная разметка ладони и пяти пальцев,
доля площади в пределах ±2мм, отдельные зазоры/проникновения и проверка всей кожи при IK.
Ядро метрики, ограниченный solver, opt-in source capture и окно разметки реализованы;
offline-компиляция пройдена, пробы метрики30/30, solver12/12 и anchor/source/collision16/16.
Связка solver с реальным BakeMesh и явными материальными якорями реализована.
Технические Unity-проверки прототипа описаны ниже; runtime/Quest, анатомическая разметка и соответствие source/target ещё не проверены;
контактные цели нельзя считать валидными без явного соответствия материальных участков.
Контракт: contracts/grip-contact-authoring-v1.md. Новый отчёт встраивает картинки до/после прямо в текст.
Подробности: changelog/2026-10-15-r2-human-rejection.md; новый visual review — reports/r2-human-review-715/Review.md.
Последняя игровая проверка715: HEAD/база `04fc9de88`; технические прогоны733/735 описаны ниже.
Общая offline-компиляция39 исходников с обоими smoke-пакетами прошла без ошибок/предупреждений.
Этап `grip-contact-authoring` running. 726 CANCELLED до запуска. Повтор733 DONE/received,
input`e2a795a7040ca892d0bff6c03cc128e1c96d6b26`, base`47c0102778d00b6a06f6c3eceb96aff2e425493d`,
R`5c276808f9473eefa092c9c5c42fae4605639b0a`:14 автоматически созданных meta, unexpected=[],2 native артефакта.
Unity import/compile исходников733 без ошибок; существующие HandMeshCaptureTests **3/3 PASS**,0 failed/skipped.
IK-smoke FAILED до первого check: общий renderer кисти в нескольких LOD ошибочно отклонён валидатором.
Нативный completed receipt получен после transport timeout120s; код не повторяли. Worker освобождён.
LOD-проверка исправлена в исходнике, smoke-скрипты сохранены в C#6; свежая offline-компиляция39/406 —0/0.
Эти правки ещё не проверены в Unity. Source/mask-smoke не исполнялся: Roslyn отсутствует, а auto-review
отклонил переход на auto/C#6 как replay после другого Session terminated. Пользователь явно разрешил
новый preview-прогон `contact-authoring-reapproval-20261015-r1`: временные копии, без Apply и сохранения поз/префабов.
Новый прогон735 DONE/received: input442ced07fee157da9d5baf35be902f462bacd802,
R2bf118ea334ec7d2526e07be071aff43ad01bc68, paths=[], unexpected=[]. IK **12/12 PASS**
на реальной BakeMesh-коже; существующие HandMeshCaptureTests **3/3 PASS**, cleanup PASS.
Source/mask FAIL: Viper — MeshFilter отсутствует у muzzle flash; Uzi — guard scale узла Rectangle001.
1482 pinned inputs без изменений. Mask API/render не достигнуты из-за source fail, новых PNG нет.
Оба MCP timeout120 завершились нативными отчётами, без повторов. Worker освобождён.
Исправлены safe mesh predicate Viper и scale guard Uzi: проверяются используемые transforms
и все их предки, посторонний sibling Line Rectangle001 больше не блокирует capture.
Uniform/no-shear/reflection guard на потребляемых данных сохранён. Mask callback использует
resolved prefab анализатора; source и mask smoke разделены. Offline39/4060/0, Unity-повтор ещё впереди.
739 истёк до claim, Unity не запускался. Тот же source/mask input240ffc95c7aa8e784a6945d6dc71b31ff2d0051b
выполнен740 наbase6669db54ff2629c716c2678f67f2c23841c60c93.740DONE/received,
Rf6e61e628d268126a2346d928dbda8ab7f91ad94, paths/unexpected пусты. Uzi source и Viper2mask cases PASS;
общий smoke FAIL3completed/7failure entries: Viper source nonuniform String и Uzi target1degenerate triangle.
1482inputs unchanged; cleanupPASS. Native198.585052s, timeout300 вернул ответ, replay не было.
2реальных PNG встроены в reports/contact-authoring-integration-20261015/740-Review.md;
это synthetic mask preview, не анатомическая разметка и не before/after исправления хвата.
IK12/12 повторять не требуется. Инфраструктура не изменялась.
Свежий отчёт: reports/contact-authoring-integration-20261015/735-Review.md; infra_issue8307.
План: tools/contact-authoring-unity-plan.json. Отчёт: reports/contact-authoring-integration-20261015/733-Review.md.
Accepted commit/merge не выполнялись. Отчёт: reports/contact-authoring-integration-20261015/Summary.md.
R2 Apply704 выполнил 17 точек/34 руки; отдельная проверка объяснила все 97 замечаний старого валидатора,
не меняя исходный отчёт. Weapon-system подтвердил сохранность механики и идентификаторов (7799).
Применены 24 рекомендованных curl для 14 рук/7 поз. Попытка705 остановилась до Apply:
JSON Integer/Float не совпали при строгом сравнении. Исправлены только три числовых литерала;
аудит v5 с фактическим Newtonsoft 13 PASS. Correction712 DONE/received R`1a71e6af6274dcb02071ea8be876ecd547a3d598`:
17 точек/34 руки, 34 Fit+PNG; validation PASS, изменены ровно7 поз, неожиданностей нет.
GUID/identity/nonGrip/foreignDirty изменений нет, 9 weapon prefabs не менялись во втором проходе.
Existing IDs/hand-chain **17 PASS / 0 FAIL / 0 SKIP**, cleanup PASS; worker освобождён.
Static Fit: gross19→10, notable7→4, ok8→20; это не приёмка контакта. Финальное weapon-owner review
конкретного R`1a71e6af…` завершено без блокеров (7901). Ответ avatar-ik (7892),
принятие в Unity/шлеме и accepted commit остаются открытыми.
Сравнение фактического mesh: pad-excess улучшился у24/24 пальцев; full finger depth выросла у16/24
(порог сводки0,05мм), pad depth — у21/24. Требуется визуальная доводка/проверка; автоматически не принимать.
Независимый immutable review51/51PASS. Подробности: reports/r2-fit-comparison-712/Comparison.md и Assessment.md.
В хабе `grip-extraction-r2=verified` для R`1a71e6af…` только в техническом scope;
evidence reports/r2-verified-712.json явно фиксирует humanAccepted=false/contactAccepted=false.
Общий следующий этап `grip-extraction-run` (R3–R5) остаётся planned до merged R2.
Ручная игровая проверка715 завершена: пользователь сообщил «Закончил, есть замечания»; приёмки нет.
Input8a1f531e (runtime assets побайтово равны R712), PlayLaunch Offline→Lobby, VR host/OculusXR active.
PlayLaunch.Cancel выполнен один раз; Idle/Ownerempty/Playingfalse/CleanupPassedtrue,
nativeMirror server/client выключены, профиль восстановлен.715DONE/received R61a6f3368053b627db2464f8ce29832deb269ec0,
paths/unexpected пусты,10полныхотчётовархивированы. Пользователь отклонил production-приёмку из-за неплотного обхвата;
ассеты проверка не меняла. Дальнейшие R2 Apply и R3–R5 не запускаются до пересмотра подхода.
Принятая правка `53b512cf6` (ранее `a9db66196` / `1dca4d3f5` / `4a11f5391`) — исправление `_assetId` в Grip Extraction;
**Unity Apply Viper PASS** на базе 97a320c88; новый прогон683: **56 PASS / 1 FAIL / 57**, полный Result сохранён.
Идентификаторы и цепочки поз PASS; FAIL — пустой список отключения своей головы у старого Cyborg.
677 — init failure, 680 и старые635/647 UNKNOWN сохранены; пользователь принял вливание save 2026-10-15;
Правка влита fast-forward в `origin/dev 846fa4bca`; хаб подтвердил merged (7558), content_drift=[].
FIFO request-base692 DONE (7620), принятая правка опубликована в worker; затем база1812dc5bf.

## Цель

Кисти и хваты аватаров: MEF и новый `MEF_cyborg_hands` (кисть Cyborg в скелете MEF — основной путь
по решению пользователя). Анализатор контакта Hand Pose Fit даёт грубые нарушения и листы ревью;
Grip Extraction Tools переносит хват с кисти на кисть; финальная доводка поз — за человеком.

## Состояние на 2026-10-14 (история, до закрытия 2026-10-20)

Итог и открытые вопросы — в разделе «Задача закрыта 2026-10-20».

Влито (этапы `merged`): static-analyzer, expert-deployment, expert-registration, mef-grip-audit,
contact-metrics, hand-graft-exp, sdk-hands-seam, cyborg-grips (`d00f53667`), grip-extraction (`e16b86b82`).
- **Аватар `MEF_cyborg_hands`** — третий скин террористов (`Assets/Prefabs/Player/Experimental/MEF_cyborg_hands/`,
  Base GUID `71b74b0d…`). Записи хвата на 42 префабах; 30 поз оружия перекодированы с пястными
  (`HandPoses/`), базовые позы и поза по умолчанию — Cyborg. Шлем: «обе проблемы исправлены».
- **Grip Extraction Tools** (`Assets/Editor/VR_Battlegrounds/Avatars/GripExtraction/`, вход
  `GripExtraction.RunJson`): источник — клипы паков, цель — профиль (MEF / MEF_cyborg_hands), опора —
  запястье или центр ладони. R0 PASS. Пилот на Scar, MP5K, AK105, MKR9, SRM12, Viper влит;
  шлем: «стало гораздо лучше, всё сделанное можно выпускать».
[Подробно](Details.md#grip-extraction-tools-и-план-извлечения-хватов--2026-10-13-решение-пользователя).

## Дальше — следующий шаг (история, до закрытия 2026-10-20)

План не выполняется: задача закрыта; открытые дефекты — в [реестре](../../Docs/grip-known-issues.md).

1. **Принятая правка влита `846fa4bca`** (GripRecordWriter: после `SaveAsPrefabAsset` —
   `NetworkAssetIdNormalizer.Normalize` + сверка `_assetId` на диске с `NetworkIdentity.AssetGuidToUint`).
   Apply Viper647 PASS, идентификаторы и цепочки поз683 PASS; пользователь: «тогда вливаем и переходим к run extraction?».
   Единственный FAIL старого Cyborg передан avatar-ik (7525), не переобъявляется PASS.
   Fast-forward подтверждён, FIFO request-base692 подан; **условие weapon-system 6388** выполнено.
2. **MKR9, основная точка, левая рука — кисть мимо рукояти** (подтверждено в шлеме; это и F4 у MEF).
   Разобрать зеркалирование источника для левой руки в `PackClipGripSource`, перезаписать.
3. **`grip-extraction-run`**: пакет R2 Measure подготовлен — 17 точек/34 руки/9 стволов → MEF_cyborg_hands;
   R3 — записи Cyborg → MEF_cyborg_hands (SDK-оружие, магазины, патроны, планшет, жетон); R4/R5 — по
   решению пользователя. Порядок прохода: apply с опорой по ладони + анализ → только `Recommended`
   подсказки (curl, сдвиг по нормали; руку «мимо рукояти» не догибать) → второй apply → листы ревью → шлем.
   Перед begin: zone-ack avatar-ik на `MEF_cyborg_hands/**`; до коммита — список и diff weapon-system.
4. Убрать `MEF_sdk_hands` из `Assets/Data/Teams/Terrorists_Team.asset` (решение 2026-10-14; реестр аватаров и
   префабы оставить; проверить `TeamAvatarsTests`; правка через Unity, зона avatar-ik — уведомить).
5. После: `wrist-gap` (зазор 2–3 см + материал «уступает кисти»), `grip-authoring` (ручные точки
   подушечек как источник Grip Extraction), palm-validation, dynamic-probes, mef-correction.

## Возобновление Codex — 2026-10-14 (история, до закрытия 2026-10-20)

- Хаб: `hand-rig-quality`, owner `avatar-grip`; десять этапов `merged`, включая save-validation7558.
  `grip-save-validation`: после release683 получена приёмка пользователя 2026-10-15;
  Apply PASS на прежней базе; свежие EditMode 56/57, Cyborg own-head FAIL остаётся отдельным дефектом.
  Приёмка касается сохранения `_assetId`; merged7558, SHA846fa4bca, content_drift=[], dependency_gaps=[].
  `grip-extraction-run` и последующие этапы остаются `planned`.
  **`grip-extraction-r2-measure` verified:** Unity695 durable PASS17точек/34руки/0записей,
  2400hashes unchanged; maxPalm0,0009мм/maxRuntime0°. Input9d35a289/base1812dc5bf.
  MCPквитанция потеряна после30с; C# продолжил работу и завершился, повторного Run не было.
  Full validation SHA256 `c0c977649c1b68af7f1e67d75e1c0f45b4ec8f3165918237a9135f438256d30d`.
  Cleanup/nativeidle PASS, DONE/received R`aac0b5d5`,paths/unexpected=[];3JSON заархивированы695.
  Запрос и проверяющее тело: tools/r2-measure-request.json,
  tools/run-r2-measure.cs.txt. Проверка hashes,17/34/9,written0,palm≤0,5мм,runtime/world≤0,5°,
  пястные дескрипторы; затем native guard/cleanup и release. Measure не даёт Fit/contact/Quest acceptance.
  Avatar-ik conditional ACK7563: только R2 HandPoses, GUID сохраняются, Player/Base/meta и rig неизменны;
  RegisterPoses=false. Weapon-system7571: Measure readonly разрешён; write-ACK только на девять точных префабов R2.
  Выделены `grip-extraction-r2-measure` (task artifacts, после save merged) и `grip-extraction-r2`
  (девять weapon prefabs и17 существующих pose assets; после Measure verified).
  Общий `grip-extraction-run` сохраняет будущие R3–R5 и зависит от R2 merged; их scope не принимается заранее.
  Begin широкого run обнаружил UNORDERED_OVERLAP haptics-system/item-clicks; порядок Apply запрошен в хаб.
  MKR9/R3/UI исключены из текущего R2.
  Активные узкие ACK: avatar-ik7601, weapon-system7631rev2; initial Apply+Analyze/Fit разрешён
  после verified Measure, отдельный pre-save contact не требуется. Contact/corrections/IDs/ownerdiff/Quest — до приёмки.
  Haptics7627: запись девяти стволов/HandPoses не пересекает их Tablet_Base/Haptics; broadrun пока отдельно.
  Подготовлен initial Apply пакет tools/r2-apply-request.json + run-r2-apply.cs.txt;
  static review без блокеров, Roslyn semantic0errors. В698 helper остановил Apply до Run:
  persistent dirty shaderassets в worker + два глобальных SaveAssets в GripExtraction.Write (193/219).
  Новые R2 ассеты не записаны;698DONE/received R07d5ef0d,paths/unexpected=[],cleanupPASS.
  R2 возвращён planned. Новый scoped-save planned: сохранить только pose assets текущего запроса,
  убрать финальный SaveAll (weapon/registry уже сохраняются своими writers). Пользователь согласовал дизайн 2026-10-15;
  правка и повторный R2 Apply+Fit в работе, новые тесты до пользовательской проверки не пишутся.
  Scoped-save verified на input0cb68d2f: Unity701 refresh/compile и Android gate PASS0errors,
  existing EditMode17/17PASS (IDs4 + hand-chain13), cleanupPASS; DONE/received R56057601,paths/unexpected=[].
  702 остановлен до Run: persistent dirty object без файлового пути; DONE/received Rd5881d37,
  cleanupPASS,paths/unexpected=[]. Guard уточнён: pathless остаются под per-instance/dirty проверкой,
  все непустые foreign paths/meta — под SHA256. Повторный Apply+Fit готовится, записи R2 ещё нет.
  703 также остановлен до Run на виртуальном `Library/unity default resources` без meta;
  cleanupPASS,DONE/received R2b1218ec,paths/unexpected=[]. Guard теперь сравнивает disk-state:
  hash существующего файла либо его отсутствие (null), включая meta; появление/удаление/изменение запрещено.
  Строгие 2400 Measure-входов не ослаблены; dirtytargets и все instance/path/flags проверяются как прежде.
  Полный preflight.json пишется до отказов, новый повтор Apply+Fit готовится.
  **704 Apply+Fit выполнен:** statusok17точек/34руки,34Fit+PNG; 2400 prehash и2522posthash,
  изменены9weaponprefabs,17posebytesunchanged; unexpected/GUID/identity/foreigndirty=[] (501objects).
  Исходный validator false только97nonGripChanges: ошибки путиGrabs и JSONоболочкиMonoBehaviour.
  Отдельный offline YAML/snapshot proof97/97PASS,unexplained0, повторён principal; originalfalse сохранён.
  Fit19gross/7notable/8ok,staticapproximate; отобраны14рук/24curlв7позах,12manual/8unchanged,
  normalmapempty (две подсказки-21,7мм вне15мм). Второй проход712 выполнен, результаты ниже.
  Cleanup/nativeidle PASS;704DONE/received R13ef52db,8JSONarchived,acceptedcommit/Questacceptance нет.
  **712 correction Apply+Fit PASS:** 24curl/14рук/7поз,17точек/34руки,34Fit+PNG;
  inputc7ebbfec/base04fc9de88, result1a71e6af,11полныхартефактовархивированы,DONE/received.
  validatione3737c85: unexpected/GUID/identity/nonGrip/foreignDirty пусты;9weaponbytesunchanged.
  Existing17tests PASS/0FAIL/0SKIP, cleanupPASS; staticFit10gross/4notable/20ok.
  Сравнение фактического контакта и owner review открыты; принятого коммита нет.
  Временная очистка чужих dirty flags и сохранение загруженных weaponprefabs через SaveAssetIfDirty исключены.
  Общий mode off; очередь и аренды Unity сохранены.
- Старый маршрут Claude очищен (epoch 15). Свой Codex route опубликован после свежего desktop listing
  (актуальный epoch 24, `codex-app-threads`, thread `01a13af7-d0e0-7382-b1f7-c0f78057e8ed`).
  Межзадачная доставка — canonical inbox; native-уведомление дополняет его и не означает ACK.
- Network подтвердил событием 6647: общий помощник сохранения и патч Mirror остаются после
  `network-sync-hardening`; локальная нормализация не ждёт их реализации.
- Пять существующих EditMode fixtures выполнены в683 после runtime f6387f9 и явной подготовки Unity:
  NetworkAssetId 1/1, UxrUniqueIdOnDisk 1/1, Stability 2/2, AvatarHandPoseChain 13/13,
  RegisteredAvatarBody 39/40. FAIL: `Своя_голова_выключается` для `PlayerControllersCyborgAvatar` —
  `UxrMirrorAvatar.LocalDisabledGameObjects` пуст. Это результат EditMode, не наблюдение в шлеме.
  Cleanup PASS; DONE/received, result `ccf4a155`, paths/unexpected пусты. [Итоги](changelog/2026-10-15-tests-683.md).
- MKR9: статически подтверждено отражение противоположной руки по неверной оси корпуса (Body X
  направлена вверх оружия). Unity numeric A/B PASS: main Left сдвиг 208,0405 мм/180°, support Right
  97,4297 мм/180°; шесть controls неизменны, runtime/world и pose/current 0°, 34 asset/meta hashes
  совпали. Контакт/BakeMesh/Play/Quest ещё не проверены; изменённые хваты не сохранены.
  [Локальный разбор](reports/resume-2026-10-14/mkr9-diagnosis.md).
- Узкий `grip-save-validation`: ticket 628 отменён без аренды; ticket **635 DONE/received**,
  input `1e89792b`, result `c4f463d5`, `paths=[]`, `unexpected=[]`; editor освобождён.
  Avatar-ik zone-ack подтверждён событием 6727 (две позы), weapon-system — 6843 (Viper).
  Оба ACK узкие: неизменные байты трёх ассетов, идентификаторы и существующие проверки;
  на широкий `grip-extraction-run` они не распространяются.
  Встроенная Unity MCP-сессия вернула `Session terminated` на resources/list. Общий hub отвечает;
  новый [клиент штатного прокси](tools/mcp-client.py) прошёл initialize/tools-list (48 инструментов),
  без вмешательства в shared infra. **Unity Measure Viper PASS:** две точки, четыре руки,
  максимумы align/поз/runtime-world — 0 мм/0°, байты трёх ассетов совпали; `_assetId=1889608562`.
  [Локальный отчёт](reports/resume-2026-10-14/save-validation/save-validation.json).
  Пакет [tools/validate-grip-save.cs.txt](tools/validate-grip-save.cs.txt)
  по умолчанию выполняет Measure; Apply разрешён только после обоих свежих ACK.
- Решение по TeamData передано avatar-ik (6689/6729): владелец готовит отдельный этап после своего
  текущего checkpoint, префабы и реестр остаются. Наша заявка этот ассет не меняет.
- В Grip Extraction реализован только источник `pack`; для R3 (`avatar-record`) адаптер ещё нужен.
  Он должен сохранить независимо заданные обе кисти Cyborg; нынешний одно-ручный `GripSample`
  зеркалит вторую и теряет authored differences. [Source prerequisites](reports/resume-2026-10-14/r3-source-prerequisites.md).
  Уведомление 6897 от grab-authority: четыре известных coverage failure двух магазинов у
  MEF_sdk_hands_Player/Cyborg сохраняются; это результат чужого worker641, не проверка нашего WIP.
  Частично сверена карта инструментов; `reviewed_sha` не повышен до полной сверки зоны.
- Порядок `grip-save-validation → grip-extraction-run` зарегистрирован (ревизия 41), прежний database-lock
  не удерживает локальную работу. Общий хаб обслуживает maintainer; readiness 6805 обработан.
- **EditMode UNKNOWN:** ответ `run_tests` потерян при disconnect/reload до выдачи job_id.
  Native guard подтвердил `tests/compile/import/play=false`, test-probe отсутствует; это cleanup,
  не результат тестов. Новый запуск не выполнялся. Infra_issue **6809** передан сопровождающему;
  далее — read-only native SessionState, затем новая проверка после установления исхода.
- Следующий пакет: witness загруженного normalizer, native metadata собственного тестового запроса,
  Apply принятого Viper и числовая A/B-проба MKR9 на временных клонах. Изменённый хват MKR9 не сохраняется;
  неизвестный тестовый job не повторяется. Проба статически компилируется против worker DLL (exit 0),
  Ticket **647 DONE/received**, input `ac5adbe7`, base `97a320c88`, result `aefb9fc6`, paths/unexpected=[].
  Compiled normalizer witness=true; Apply Viper PASS (4 руки, 3 byte hashes unchanged, assetId1889608562).
  Fresh EditMode request647 после Apply также потерял ответ до job_id. Native до/после: current job empty,
  пять metadata jobs старше запроса, собственного нового job нет. Итог UNKNOWN, retry/clear_stuck не было.
  Native cleanup PASS: testsRunning=false, probe отсутствует. Worker освобождён; повторный infra_issue
  **6972** направлен agent-coordination-protocol. Отчёты `reports/resume-2026-10-14/`, [итоги](changelog/2026-10-14-save-validation.md).
- Policy7160 обработана: worker/broker/MCP infra_issue сразу в хаб, затем внутреннее уведомление
  проверенному чату сопровождающего без дополнительного вопроса пользователю. По issue6972
  native notification доставлена `сопровождающий` (`01a13af0-1b49-75c0-9258-37dcbe9ddab2`).
  Recovery664 завершён (7202). Worker_ready7257 обработан: билет сопровождающего667 DONE,
  база B54673ba4 восстановлена; unity-mcp SDK patch2 проверен на native prebuild24,9с/полном Result.
  Это инфраструктурная проверка; прежние UNKNOWN635/647 сохраняются.
- **Отказ resource listing:** после readiness штатный `list_mcp_resources(server="unityMCP")`
  в 2026-10-15 01:15 МСК всё ещё вернул `Mcp error32600: Session terminated`.
  Infra_issue7266 направлен в хаб и доставлен проверенному чату сопровождающего.
  [Отказ](reports/native-mcp-session-2026-10-15.md). Первоначальный вывод о полном отказе MCP
  исправлен: в новой попытке штатные tools/call read_console/execute_code/run_tests/get_test_job работали.
  Ошибка resources/list не запрещает доступные независимые Unity операции.
- Policy7257: самостоятельно не чинить и не обходить infra/worker/broker/proxy/hooks/MCP.
  Ремонт — только явно назначенный сопровождающий. Временный `tools/mcp-client.py` после
  этой политики не запускается. Read-only status/log/help и независимая avatar-подготовка
  разрешены. [Маршрут MKR9 contact-проверки](reports/mkr9-contact-package-2026-10-15.md) подготовлен
  read-only: actual BakeMesh(false), публичные FromGeometry/RenderEditorView, одинаковые камеры,
  mapping/scale self-roundtrip и right control. Одноразовый адаптер ещё не написан/не исполнен;
  ассеты не менялись.
  Собственные gameplay acceptance и zone ACK сохраняются.
- **Попытка по прямому поручению пользователя — 677:** input23557f15, base8eb877915,
  DONE/received, result4f1df746, paths/unexpected=[]. Новый job `8c540cf8a16c4c4e8ded80aba0e707ac`
  получен штатным run_tests. Первый get_test_job — reconnect timeout120с/7попыток; после свежего
  guard повторный status-poll того же ID успешен: terminal `failed`, `completed=0`, `total=null`,
  `result=null`, `Test job failed to initialize (tests did not start within timeout)`.
  Это зарегистрированный сбой инициализации, не результаты игровых assertions и не replay635/647.
  Native cleanup PASS: testsRunning=false, probeAbsent=true; worker/admission освобождены.
  Infra_issue7387 отправлен в хаб и native сопровождающему. [Итоги](changelog/2026-10-15-tests-677.md).
- **Ещё одна прямая пользовательская попытка — 680:** inputd62864c7, base8eb877915,
  DONE/received, resultb1d17468, paths/unexpected=[]. Штатные request/claim/begin/guard/preflight PASS.
  run_tests тех же пяти fixtures потерял ответ: plugin sessionb83fb05d-aaa2-4f17-a125-e6da328f4278
  disconnected while awaiting command_result, job_id отсутствует. UNKNOWN, hint retry не выполнялся.
  Native guard/cleanup PASS: tests/compile/import/Play=false, probeAbsent=true. Worker/admission освобождены.
  Infra_issue7446 в хабе и подтверждённом native канале сопровождающего. [Итоги](changelog/2026-10-15-tests-680.md).

## Ждёт пользователя (история, до закрытия 2026-10-20)

R4/R5 закрыты вместе с задачей; при возобновлении — в задаче направления 3.

- R4 (заменить хваты MEF посадкой по ладони) и R5 (покрытие Cyborg/BigHands) — решение после R2/R3.
- Удалить вручную `C:\renders` (агент удалять вне workspace не может).

## Решения пользователя

- 2026-10-12: «анализ даёт грубые нарушения и удобные скрины… финальная настройка за человеком».
- 2026-10-12: пересадка кисти SDK в один скелет — основной подход; у команды минимум один аватар.
- 2026-10-13: «рука киборга гораздо лучше big hands» — основа `MEF_cyborg_hands`; шов у манжеты — позже.
- 2026-10-13: «да текущее вливаем фикс по манжетам сделаем позже — сейчас важнее дать новому аватару хваты».
- 2026-10-13: универсальный инструмент — «конвеер общий а киборг меф и оригинальная рука из сдк являются
  параметрами»; название «grip extraction tools + план по экстракшну»; опора — центр ладони клипа пака.
- 2026-10-13: «стало гораздо лучше… всё, что сделано уже можно выпускать»; MKR9 левая мимо — подтверждено.
- 2026-10-14: `_assetId` — «все 3» предложения владельцам; пилот починить вручную и влить, не ждать фикса.
- 2026-10-14: `MEF_sdk_hands` — убрать из команды террористов (основа — `MEF_cyborg_hands`; префабы оставить как эксперимент).
- 2026-10-14: патч Mirror у network — после их текущих работ (network-sync-hardening); до него нормализуем у себя.
- 2026-10-20: задача закрывается целиком; анализ рига кисти закрыт, направления 1, 3, 4 при возобновлении —
  отдельными задачами; знания — в `Docs/`, отчёты — в основном checkout вне Git.

## Риски и ловушки

- `PrefabUtility.LoadPrefabContents`/`SaveAsPrefabAsset` сбрасывает `NetworkIdentity._assetId` (→0),
  у вариантов аватара — `__prefabGuid`/`__isInPrefab` UltimateXR; сторож по загруженному ассету этого
  не видит. После записи сверять diff и гонять `NetworkAssetIdOnDiskTests`.
- Позы без пястных на кисти с пястными в рантайме SDK выкручивают пальцы (179°) — писать позы на риге
  цели (Grip Extraction делает), проверять «рантайм против редактора».
- Анализатор не видит неправдоподобную кисть и «мимо рукояти» — смотреть листы ревью глазами.
- Красные тесты, не от этой задачи: телепорт на BotCombatStand, `CorpseTests` у MEF_cyborg_hands
  (кости пальцев после пересадки), `GrabPoseCoverageTests` (намеренно), `HandsPack/KinemationHandPoseTests`
  на MEF (левая рука) — см. бэклог.
- Play агентам — только через PlayLaunch; аренды ≤20 мин, цикл продления ограничен 90 мин.

## Координация (Codex) (история, до закрытия 2026-10-20)

Входящие: `python Tools/agents/coordination.py inbox --owner avatar-grip --unread --as "<свой адрес>"`;
при недоступном native discovery маршрут очищается, чужой адрес не используется. Вопросы пользователю
в Codex — через доступный инструмент запроса ввода, с фиксацией незакрытого решения выше.
Обещания другим агентам — `experts/avatar-grip/backlog.md` (раздел «Обязательства»).

[Details](Details.md) · [plan.json](plan.json) · [Роль эксперта](expert/avatar-grip-expert.md) ·
[Инструкция](tool.md) (указатель на [Docs](../../Docs/hand-rig-quality-tool.md)) · журнал `experts/avatar-grip/journal/`.
