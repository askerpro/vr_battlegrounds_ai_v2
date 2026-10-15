# Preview contents сохраняют идентичность своего prefab

2026-10-15, network. Пользователь поручил исправить _assetId после опубликованного AvatarRoot-пакета.
Работа в своём linked worktree, mode off; очереди и Unity guards сохраняются. SDK номер Mirror5 зарезервирован.

## Причина и граница правки

OnValidate вызывает SetupIDs. Utils.IsPrefab использует IsPartOfPrefabAsset; LoadPrefabContents не является
persistent asset. Regular contents попадают в scene fallback, получают ложный sceneId и _assetId0. Variant
contents могут распознаться как scene object with prefab parent и получить ID исходной базы. При импорте
persistent asset SDK снова назначает канон собственного GUID: это чередование и создаёт перезапись.

Новая ветка EditorSceneManager.IsPreviewSceneObject стоит после существующих persistent/PrefabStage веток
и перед настоящими scene instances. Она присваивает только sceneId0 и сохраняет загруженный _assetId.
Source prefab варианта не определяет GUID самого варианта; scene.path не гарантирован при начальном OnValidate.
При открытом PrefabStage сохраняется существующая внешняя логика Mirror; новый guard не предшествует ей.
Обычные сцены и runtime fallback не меняются. Общие Utils, NetworkScenePostProcess и нормализатор не меняются.

Ограничение: сохранение корректного импортированного ID не является ремонтом старого raw0/ID базы, если он
уже записан в source. Такой случай остаётся у канонического persistent-import owner/существующего authoring.
Нормализатор ограничен Assets/Prefabs и заменой существующего YAML числа; он не должен маскировать probe.

## Подготовленный пакет

1. Точный immutable checkpoint исходного SDK; authoring probe на временном regular и variant с заранее
   корректным persistent ID. Прочитать _assetId через SerializedObject: публичный getter вызывает SetupIDs.
2. Снять raw значения после LoadPrefabContents, повторного OnValidate, SaveAsPrefabAsset и ForceUpdate;
   два no-edit цикла, variant ID отличен от базы, sceneId0, итоговый второй файл побайтово стабилен.
3. В собственной additive scene проверить loose identity и реальные prefab/variant instances: sceneId!=0,
   assetId0 у loose, собственный GUID source prefab у instances. PrefabStage проверяется отдельно либо отмечается skipped.
4. В finally выгрузить только свои contents, закрыть свою сцену, удалить fixture через AssetDatabase,
   подтвердить сцены/selection/preview count и отсутствие каталога. Сразу finish, до анализа/следующих правок.
5. Повторить тот же probe на SDK patch5; compilation/Android gate и существующий NetworkAssetIdOnDisk gate.
   Полные отчёты только reports/, в Git их не включать. Новые тесты и принятый коммит — после проверки пользователем.

Предыдущие три tracked правки network-sync-hardening сохранены в stash9ca534e67088c11de832f3aa2f78e0afaa95e507,
rebase6669 прошёл без конфликтов, stash применён с index и сохранён. Старые untracked MCP helpers не менялись.

## Фактические проверки 2026-10-15

Статическое ревью восьми добавленных строк: блокирующих замечаний нет; новая ветка находится внутри UNITY_EDITOR,
после PrefabStage и перед scene-instance веткой. Нормализатор и runtime-путь не изменены.
Исходный SDK checkpoint2095392a7, исправленный d648c9495. LF SHA256 исходника соответственно
74bd7d1951130aa61980a9eb47354858c577a90323f380e8417477a6f28879d1 и
be2d4c6add99b07a79b01476f71526b73b2967d2669854b30b2ee9a9ecf33772.

Первый ticket742 истёк в OFFERED без claim/Unity. Baseline ticket743 получил RUNNING628;
native execute_code отклонён до исполнения: Roslyn недоступен. Fixtures не создавались,
результат не является ни воспроизведением, ни успешной проверкой исправления. Ответ сохранён
в reports/baseline-743-mcp.json; finish завершён, result d775a88f4263b99ec55b419a6a660b6feef1c140, paths[].
Подготавливается то же тело диагностики для штатного CodeDom C#6. Инфраструктура не менялась.
Сопровождающий уведомлён через hub8371 и отдельное дополнение о Roslyn.

Probe адаптирован под C#6: 0 syntax/semantic ошибок, 7 предупреждений. Дополнительно managed static compile
всего Mirror/Core75 файлов с изменённым NetworkIdentity и UNITY_EDITOR: 0 ошибок, CS0649 один раз.
Это компиляция без emit, не фактическая Unity-проверка поведения.

Unity744/C#6 выполнил код, hash/root совпали; подготовка additive scene получила явный отказ из-за
единственной несохранённой пустой сцены broker. bodyCompleted=false, cleanupPassed=true; исходный и
исправленный SDK ещё не сравнивались. Полный reports/baseline-744.json, durable artifact сохранён finish;
result5465b2ec78ea2389662270b93308b13effee98c1, paths[]. Следующий пакет допускает Single только при
строгом пустом clean untitled guard и восстанавливает такое же пустое состояние; иные сцены не закрывает.

Перед следующим пакетом выполнены fetch/rebase origin/dev018e63d4f (изменения performance, без пересечения
с Mirror/authoring scope). Tracked WIP сохранён в stashf1d4c1487774740604ead2b2a1f534f0b57c017f,
применён с --index без конфликтов; stash и untracked материалы сохранены.

Baseline746, input7cf11d684447c1ddc1bce8114e3cfae17afe1784 на base018e63d4f: RED20 при bodyCompleted=true,
41 report rows, cleanupPassed=true, serviceSceneRestored=true. Regular load сразу дал assetId0 вместо
2076093091 и sceneId940970277; variant load дал ID базы2076093091 вместо собственного2432514881,
sceneId819263575. Нарушения повторились на save/import и во втором цикле. Loose scene и обе реальные
prefab instances прошли. Это фактическое воспроизведение причины вне нормализатора.
Полный reports/baseline-746.json, durable SHA256945c7bc158b3f95a1e9e502dcd1618c4317435c5171454df4ecd6a050931bb00;
finish resultfc15507f24df7e2f3ce44036a4186cd332d18fbe, paths[].

## Приёмка и оставшаяся граница

После зелёного fixed-пакета пользователь повторяет свой authoring-процесс для обычного prefab и variant:
загрузить contents, сохранить без правок, повторно импортировать и повторить цикл. _assetId должен оставаться
равным hash GUID собственного prefab/variant, diff без обратной замены на0/ID базы. Отдельно открыть PrefabStage
и убедиться в том же каноне; автоматический probe эту стадию не открывает и отмечает skipped.
У настоящего scene instance sceneId остаётся ненулевым. Эта задача не проверяет Play/spawn/network/Quest.

Существующий NetworkAssetIdOnDiskTests не переписан: его комментарий про неизбежное обнуление contents
описывает старую причину и после пользовательской приёмки потребует обновления. Само ожидание канонического
ID на диске остаётся действующим. Новых/переписанных тестов до такой приёмки нет.

## Исправленный пакет748

Fixed input e3353fc4441b413552aaf620bd3cdd071c443c2b, base018e63d4f, RUNNING633.
Ровно тот же probe, кроме runLabel и ожидаемого LF SHA исходника: GREEN, failureCount0, bodyCompleted=true,
41 report rows, cleanupPassed=true, serviceSceneRestored=true. Regular ID4045852443, variant ID1075032682,
оба соответствуют собственным GUID, sceneId0 в contents. Два цикла save/import и стабильность второго файла
прошли; loose scene и оба настоящих prefab instances прошли с sceneId!=0. PrefabStage skipped явно.
Полный reports/fixed-748.json; durable artifact SHA25666f8eeb0eb6604bd8c58e8f7a2796f4982723d8329d40d6c219fb81928a02b94.

AndroidCompileGate.Run: PASS, errors0; reports/android-fixed-748.json,
SHA2566d62bfa2c5605a1bcc443a025a90fd98abd5cc44d0a9d12d9bc64f14e573da5c.
Native run_tests сначала явно вернул refresh_required/no job; выполнен штатный refresh_unity, затем guard.
Единственный начатый job2f0102b378b440c198882183f00c061e завершён succeeded: existing NetworkAssetIdOnDiskTests
1/1 Passed, failed0, skipped0. Полный tool result в reports/editmode-fixed-748.json.
После завершения пакета сразу finish; result e369d6ef09c15f7692c0de017ac65735a0a2c919, paths[], unexpected[].
На момент748 новых/переписанных тестов не было; accepted commit/push не выполнялись.

## Пользовательская приёмка

2026-10-15 пользователь ответил «Принимаю результат Unity-проверок (рекомендуется)» на вопрос с явным
RED20/GREEN0, Android0, disk test1/1 и skipped PrefabStage. Приёмка относится к этому заявленному объёму,
она не создаёт отдельного доказательства ручного PrefabStage/Play/Quest. По AGENTS после приёмки добавляются
узкие регрессии regular/variant и обновляется исторический комментарий существующего disk test.
Прежнее поручение «коммить и вливай» выполняется для принятого исправления без повторного вопроса.

## Регрессии после приёмки

NetworkAssetIdOnDiskTests получил два TestCase regular/variant: канонический raw ID и sceneId0 после Load,
OnValidate, Save, Import; два цикла со стабильными save↔save/import↔import bytes. Fixtures вне Assets/Prefabs,
собственная preview scene, GUID/file guards cleanup; исходные active scene/dirty/selection сохранены.
Проверка всего продуктового каталога не ослаблена; исторический normalizer-only комментарий обновлён.
Статическое ревью блокеров не нашло. Стабильность между разными фазами save→import побайтово не заявляется.

Input609f9645c090036b45a686ee74e692202fcd16d8, Unity750/RUNNING635, native jobf31f8c92fde84162aad5376757a6a4ee:
**3/3 PASS**, failed0/skipped0, duration3.3101903s. Полный reports/editmode-regression-750.json.
Штатный refresh перед запуском, единственный начатый job. Сразу finish после полного результата,
result428f366f7f01c0c4d0b80a25a3c1401555a45c5e, paths[], unexpected[]. SDK не изменялся после748.

Register r3 включает тестовый путь. Begin собственного этапа вернул DEPENDENCY на planned ledger записи
network-sync-hardening/avatar-root-admission; фактический код-предшественник уже принят в origin/dev1e945.
Это пробел учёта, сообщение сопровождающему8463; прежние stages не переобозначены. Общий mode off сохраняет
legacy ff-публикацию принятого пакета. База worker обновляется только штатным request-base, без ручного rebase.

Перед push origin/dev сдвинулся018e63d→98eed6c1c только по AGENTS.md (приёмка поведения до новых тестов).
Rebase прошёл без конфликтов; SDK/тесты остались идентичны проверенному750 input. Прежний tracked WIP
network-sync-hardening сохранён в stasha685e3de7cbb7460fea329e1bf40aa2f7a707ad4, применён с --index,
backup сохранён, untracked MCP helpers не изменены. Это документационный сдвиг базы без новой runtime-проверки.
