# Player: реализованная структура

2026-10-18. Полный layout разрешён прямым решением пользователя и canonical9661; выполнен native Unity API в той же задаче. Имена файлов, GUID, content и Unity inheritance сохранены. Структура принята пользователем: canonical9743 от hand-rig-quality, «Принимаю структуру — обнови пути в тестах».

```text
Assets/Prefabs/Player/
  Session/
    PlayerSession.prefab
  Avatars/
    Bases/
      PlayerBase.prefab
      PlayerBase_NonSdkHands.prefab
    CyborgHands/
      Base/CyborgHandsBase.prefab
      Cyborg/PlayerControllersCyborgAvatar.prefab
      MEF/MEF_cyborg_hands_Player.prefab
      MEF/MEF_cyborg_hands_Player_Black.prefab
    Ghost/
      GhostAvatar.prefab
      GhostMaterial.mat
  HandPoses/
    CyborgHands/              30 проектных pose assets, общие Cyborg/MEF
  Equipment/
    Watches/                 WristWatch.prefab, WristWatch_HUD.prefab
    Pockets/                 MagazinePocket, Anchor_Back, Anchor_Hip_R
  Corpses/
    MEF/                     два существующих MEF corpse prefabs
  Unused/                    прежний архив и Heavy819
```

Фактический итог:58assets,133физических файла,17folder.meta. Root prefab файлов0; старые Ghost/Pockets/MEF-контейнер отсутствуют. Старый MEF root и его meta удалены только после native проверки пустоты.

## Идентичность и ссылки

14MoveAsset переместили46assets и3существующие папки (Ghost, Pockets, HandPoses), сохранив их folderGUID. 95физических переносимых файлов сохранили исходные blob/SHA256. 11новых parent folder GUID, созданных в821, сохранены в823. Полный исходный Player before821:123файла;122после823 идентичны по SHA256/GUID/fileID/prefab-source, единственное удаление — пустая MEF_cyborg_hands.meta. Heavy повторно не перемещался и byte-identical.

72ссылочных assets проверены native SerializedObject/GetDependencies:87 740свойств, ноль добавленных/удалённых свойств/dependencies и ноль byte changes. Охват включает AvatarData/Registry/Managers/grips, LegacyBuildDonors и vendor consumers; vendor только читался. [Сводка](reports/layout/native-deltas-summary.json), [семь avatar chains](reports/layout/avatar-chain.json), [полный transfer hash report](reports/layout/source-hashes-after.json).

Во время layout наследование сохранялось: PlayerBase→CyborgHandsBase; CyborgHandsBase→Controllers и MEF; MEF→Black; Controllers→Ghost; PlayerBase→NonSdkHands. Последующее прямое решение9851/9854 изменило только Ghost: native835 сделал его самостоятельным Regular с четырьмя локальными SDK позами; Unity parent отсутствует, SDK GUID chain содержит только Ghost. Живые ветки прежние. Registry/spawnables и механизм session/body replacement не менялись.

2026-10-19: ремонт и финальная проверка836 завершены:221passed0failed6ignored из227, compilationCS0. Автономное доведение до ревью/FF-публикации прямо разрешено9889/9893 без промежуточной ручной приёмки. Это не заявление физической/визуальной VR-валидации. Текущий результат и manual steps — Readme.md, полные native receipts — ignored reports/test-repair; подробная обоснованная canonicalization — contracts/controllers-consumers.md.

## Производственные пути и tests

Обновлён21Editor source, включая AvatarHandBases, PlayerBasePrefabBuilder, Ghost builder/migration, GripTargetProfile, watches/pockets/corpse builders и TeamColorVariantBuilder dynamic output. Два Cyborg/MEF профиля используют один общий CyborgPoseFolder. Старых production paths перемещённых assets найдено0. Runtime точных путей не содержал и не менялся; генераторы не запускались.

Неизменённые EditMode checks после layout: PrefabComposition17/17; AvatarLoadout60/63; exact NetworkAssetId catalogue1/1. Всего78/81, только3прежних BotCombatStand/Floor failures; Floor принадлежит bots-validation, в этой задаче не менялся. Fixture tests job `0f1fbeada33d40c7a708b2e236e2a392`, network job `cfe80b1e09614438bf7bf84a56c40685`. Компиляция Editor и refresh завершены, guard ready, Console error CS0, Play не запускался.

Полные terminal JSON и own Editor log — ignored reports/layout. XML экспортирует все81case из этих native JSON; исходный Unity ITestResult XML MCP tool не публикует. Это технические Editor/native доказательства; UserQA и Android/Quest/runtime качество не подменяются.

[Production updates и obsolete findings](contracts/editor-path-updates.md) сохраняют карту literal-path tests, которые при доставке823 намеренно не переписывались до UserQA. После приёмки9743 выполнен этап ниже. Unrelated legacy/defaults оставлены отдельными findings.

## Исполнение и checkpoints

- Heavy819: R`8874a93c`, перенесён раньше; GUID fefcf679beb2d0f42b6cce742988357e сохранён.
- 821: input`6b79640a`;11CreateFolder, затем известный terminal error первого MoveAsset (parent не импортирован внутри StartAssetEditing), успешных moves/delete0. Немедленные finish/receive, R`daef3613`. Ничего не replay вслепую; before evidence сохранено в artifacts/821 и reports/layout/attempt821.
- 822: OFFERED истёк во время обработки inbox/проверки полученных native folder metas; CANCELLED, аренда/Unity операции не выдавались.
- 823: input`6a99521c`; до batch проверены11записанных folderGUID, затем14MoveAsset и DeleteAsset доказанно пустого контейнера. R`32b72bd3adf7da51c29bce8e3f1fa1e7ae1ac3d4`, DONE/IMPORTED, unexpected/binary-invalid0. Cleanup/finish/receive завершены сразу после terminal checks; worker active=null.
- Начальные WIP/checkpoints3635ddc6/601c37af и backup stash3443b9da сохранены. Branch HEAD остаётся accepted base2e1b6a2f; accepted commit/merge/push не выполнялись.

[Manifest](contracts/layout-manifest.json), [202declaredoutputs](contracts/layout-outputs.json), [execution spec](contracts/layout-execution-spec.json) — аудит завершённого пакета; replay выключен.

LF проверен по142owned physical paths и142изменённым LF-blobs, нарушений0; три .txt helpers физически LF. Полный git diff-check даёт30trailing-space warnings в10новых native Unity folder.meta (пустые importer fields). Они сохранены без ручной YAML правки; code/docs/tools diff-check чистый. Отчёт — reports/layout/whitespace-check.json.

## UserQA

1. В Project проверить реальное дерево Player и отсутствие root prefabs/старых папок.
2. Открыть семь avatar prefabs и проверить источник/variant chain; компоненты и вложенные модели должны выглядеть как до переноса.
3. Просмотреть30проектных поз, часы, карманы, corpses и ссылки AvatarData/Registry/Managers/grips.
4. Проверить поиск новых путей в Editor tools без запуска генераторов.

UserQA структуры закрыта событием9743. В следующем этапе актуализированы15литералов в9тестовых файлах; смысл assertions прежний. Ни переносы, ни native snapshots, ни генераторы не повторяются. Отсутствующие ещё до layout Optimized_MEF_Player в SDK preview остаются отдельными findings. Ограниченная проверка описана в contracts/test-paths-spec.json; пол и прочий продукт не исправляются. Текущая задача не закрыта; accepted commit/merge/push не разрешены.

## Этап test-paths после приёмки9743

Изменены только литералы путей в AvatarHandPoseChainTests (7), GameModeWiringTests, AvatarRendererOwnershipTests, KinemationHandPoseTests, HandsPackHandPoseTests, RegisteredAvatarBodyTests, WristDisplayTests, GhostAvatarTests и SdkHandPreviewParityTests (по1). Итого 15 строк / 9 файлов. Assertions, TestCaseSource и источники pack poses не менялись. Общие Cyborg/MEF poses не подставлялись вместо исходных poses Assets/Art/HandPoses.

WIP до правок: checkpoint6e62abc6, stash42ef3f5c сохранён вместе с прежним backup; свежий fetch/rebase оставил origin/dev2e1b6a2f. Input42bb3794; собственный ticket825 прошёл FIFO, begin/guard, явный refresh/compile и один фактический job `de4ea1b6cc7741988477e3285cc37e0c`. Первый run_tests явно отказал до создания job с refresh_required; после компиляции job запущен один раз. Во время domain reload наблюдались transient no_unity_session/ping-not-answered; затем native вызовы восстановились без ремонта или смены клиента.

| Fixture | Passed | Failed | Skipped |
|---|---:|---:|---:|
| AvatarHandPoseChainTests | 19 | 0 | 0 |
| GameModeWiringTests | 18 | 0 | 0 |
| AvatarRendererOwnershipTests | 6 | 0 | 0 |
| KinemationHandPoseTests | 16 | 3 Invalid | 0 |
| HandsPackHandPoseTests | 13 | 3 Invalid | 0 |
| RegisteredAvatarBodyTests | 23 | 1 | 0 |
| WristDisplayTests | 8 | 0 | 0 |
| GhostAvatarTests | 9 | 1 | 0 |
| SdkHandPreviewParityTests | 8 | 7 Error | 0 |
| Всего | 120 | 15 | 0 |

Семь SDK Error приходятся на отсутствующий до layout Optimized_MEF_Player: четыре PreviewMatchesRuntime, две ScaledRigAndBlendShapeMatchRuntime и RefreshPreservesMeshIdentityForBothGrabPoints. Восемь Cyborg preview cases прошли. Шесть Invalid — по три параметризованных метода HandsPack/Kinemation с пустыми case sources: их тела не запускались, это не PASS геометрии/контакта. Эти sources выбирают зарегистрированных потомков NonSdkHands, а текущие игровые аватары идут через CyborgHandsBase. Оставшиеся два отказа показывают сплошные hitbox-colliders Ghost и пустой список скрываемой головы Controllers. Они наблюдаются на неизменённых assets; отдельного сравнения quality-прогона до layout в этом этапе нет, исправления продукта не выполнялись. Floor/AvatarLoadout в этом наборе не запускались; прежние три Floor failures из823 не пересчитывались.

Guard после тестов ready=true, compiling/playing/test-running=false, Console error CS 0. Cleanup/finish/receive выполнены сразу после сохранения полного native отчёта. Ticket825 DONE/received, R`daa7b8f3a8ef28e21c0f3e4c0e9bf865145fa8e4`, paths=[], unexpected=[], accepted=true означает только допустимость broker outputs. Все 133 физических файла Player сохранили raw blobs относительно input; 9 изменённых test sources физически LF и diff-check чистый.

Ignored reports/test-paths содержит полный native-terminal.json, raw native-response-bundle.json, summary.json с135результатами, guard/refresh/finish/receive receipts и local-audit.json. SHA256 native terminal `af1db0a393205e0e0b4124d21c4ca9fd741b78bc11f67e2d4b4df79fdf487c77` совпал с broker artifacts/825. XML не создавался. Начальная попытка архивирования большого wrapper была отклонена до исполнения по лимиту50000символов; полный structured native JSON сохранён двумя подтверждёнными последовательными записями, затем JSON разобран нативно. Неизвестных исходов и replay не было.
