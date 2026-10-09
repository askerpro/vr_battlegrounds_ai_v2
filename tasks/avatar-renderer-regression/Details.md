# Доказательства и договорённости

## Причина

357a83f57 расширил Cyborg._avatarRenderers с 15 до 34 ссылок. 14 добавленных SkinnedMeshRenderer находятся под неактивной старой копией Cyborg/Ghost; ещё пять MeshRenderer живы и в этой задаче не удаляются из списка.

Сборщик FixAvatarRenderers.Setup считает все дочерние Renderer телом, исключая только UxrHandIntegration. RegisteredAvatarBodyTests.BodySkins повторяет это неверное допущение и требует включать старую копию тела. GetComponentInParent без includeInactive также пропускает выключенные ветви интеграции кистей.

В SDK UxrStateSyncImplementer.SyncCallDepth — общий статический счётчик. SetAvatarRenderMode вызывает BeginSync перед записью Renderer.enabled. Исключение на мёртвом объекте прерывает метод до EndSync, увеличивает общую глубину и объясняет mismatch на аватаре и последующих компонентах, включая TR15. Стек арсенала сам по себе не доказывает отдельную ошибку его авторства.

Точный runtime-вызов удаления старой копии не установлен. В этой задаче он не меняется. GhostAvatarBuilder.Strip отдельно описывает и удаляет именно старую копию Cyborg/Ghost при генерации настоящего GhostAvatar.

## Принятый результат, 2026-10-09

Пользователь принял исправление после Play Mode на worker и поручил освободить аренду и выполнить коммит. Финальный вариант — точный rollback массива Cyborg34→15, Ghost остаётся Untagged. Ниже сохранена история отклонённого варианта20/EditorOnly: его результаты не подтверждают финальный вариант.

Дополнительная причина регрессии: FixAvatarRenderers всегда пересобирал список из всех потомков, а тест полноты тела требовал включать все дочерние skins. Теперь непустой сериализованный список задаёт владение: Setup очищает невалидные refs, но не добавляет геометрию. Выбор первичного заполнения делается до очистки, чтобы повреждённый непустой список не превращался в произвольную пересборку. После пользовательской приёмки требование теста заменено проверками валидности, владения и полноты явных LOD; отдельная регрессия проверяет принятые15 Cyborg, повторное сохранение и удаление вспомогательного Ghost.

Аренда321, snapshot4e4b1ea2: реальный Cyborg с _started=true,15 живых refs в своей иерархии, не asset-ссылки, SyncCallDepth0. Реальный GhostAvatar после ServerEliminateSilently также _started=true,15 refs, missing0,depth0. Наблюдение после MapLoader.LoadMap(TestMap1) и MapReferee.GoLive без исключения; автоматическая фаза Combat не доказана. Пользователь повторно запустил Play Mode и сообщил об отсутствии ошибок рендереров. Последующее явное принятие пользователя закрывает ручную приёмку. Аренда321 finish/receive: result7684a665,paths[],unexpected[],accepted=true.

Оружие по-прежнему не стреляет: в logs четыре предупреждения Initialize Viper, T35. Передано weapon-system, причина нашей задачей не установлена. tools/request-runtime-cyborg.cs не выбрал Cyborg, потому что его нет в TeamRegistry; не считать этот инструмент доказательством runtime-выбора. Наблюдатель AvatarSpawned не дал захвата до Start; факты выше получены прямым чтением реальных экземпляров.

## Заключительные автоматические проверки

Аренда327, snapshot59d0431a: AvatarRendererOwnershipTests6/6 и GhostAvatarTests10/10. Job5a89692dccdd44d4ab63f78454b65589 потерял MCP callback после domain reload, но нативный XML16/16 Passed за10:06:33Z, SHA25601c4e89dec6b337fa79865d97e2e0304963b8132a0fa12995d00a35968745e0f. Файл reports/final-ownership-ghost-TestResults.xml. Отсутствие активного native run подтверждено guard, очищен только свой orphan job.

Первоначальный exact test_names без полного имени дал0 тестов; не засчитан. Regex group_names выбрал3. Cyborg прошёл, два MEF выявили ошибку нового теста: их LODGroup содержит MeshRenderer часов, которые не входят в управляемое тело. Проверка полноты LOD ограничена SkinnedMeshRenderer, как существующие проверки тела; MEF не изменялись. Промежуточные XML/MCP сохранены отдельно.

Аренда329, snapshot118b616e: после обрыва первой MCP-команды проверены старый native XML и свободный Editor (новый run не произошёл), затем выполнен job2bcc5bf1ca3f496c9525a865aae6156d. Три зарегистрированных аватара3/3 Passed. Native XML за10:13:41–10:13:42Z, SHA256d01ab8e188210a4bc85c13c602303b8f73eb02aaf93d5c4331497520e9a9e032; reports/final-body-TestResults.xml. Вся выбранная проверка19/19, компиляция Unity без ошибок. Полный suite/Android не запускались.

finish/receive327(result1d0bea24) и329(result397b86d2) завершены, paths/unexpected пусты, accepted=true. Временный helper321 завершился после guard-rejected. Чужой Tools/agents/unity_mcp_proxy.py сохранён вне коммита. Исторические инструменты расследования описаны в tools/Readme.md и не запускаются автоматически.

## Интеграция и ветка

Первый принятый коммит c90a72dc был создан в уже существовавшем detached checkout. Создана ветка codex/avatar-renderer-regression. После заявки a25499ad интегратор обнаружил DEPENDENCY_MERGE: after renderer-writer verified требует отдельно влить промежуточный checkpoint, который не прошёл runtime-приёмку. План переведён на единую принятую единицу asset-repair без внутреннего predecessor; внешний pocket-removal haptics по-прежнему следует после asset-repair, владельцу отправлено событие1658.

Origin/dev сдвинулся инфраструктурными1e169ec9 и ce765889. Rebase на ce765889 выполнен с autostash без конфликтов. Локальная инфраструктурная правка прокси совпала с штатно опубликованной1e169ec9; чужое изменение не вошло в наш коммит и не было отброшено. Проверено git diff --exit-code c90a72dc HEAD для сборщика, Cyborg.prefab, обоих тестовых файлов и meta — отличий нет. В новой базе менялись только AGENTS, инструкции, прокси и материалы infra; игровые исходники/ассеты не менялись. Фактические19/19 сохраняют применимость, новая игровая приёмка не нужна.

## История отклонённого решения

## Контракт

Используется стандартный EditorOnly для авторской вспомогательной геометрии. Отбор по имени Ghost или активности запрещён: настоящая геометрия призрака и постоянные неактивные LOD сохраняются. Модель контроллера под UxrHandIntegration принадлежит контроллеру, вложенный UxrAvatar — своим рендерерам.

## Порядок общего префаба

Событие хаба 1054 (ответ haptics-system на запрос1035): asset-repair этой задачи идёт первым; haptics-system revision10 добавил зависимость pocket-removal после avatar-renderer-regression/asset-repair verified. Их будущая правка снимает только пустой PocketHaptics и не меняет список рендереров. Кто вливает позже, выполняет rebase.

## Проверка

Четыре независимые NUnit-регрессии подготовлены до изменения сборщика. Актуальная база edcb79e8. RED checkpoint789c3df6, ticket298, job12951036e3264dfc8e42be735d5d835e: выполнено4, три отказа (EditorOnly, неактивная интеграция кистей, вложенный аватар), постоянный неактивный меш прошёл. Полный ответ: reports/red-mcp.json. Первоначальный потерянный ответ не повторялся вслепую: SessionState и Editor проверены, нового job не было.

GREEN checkpoint0b39a968: исправленный сборщик скомпилирован, его IsEditorOnly обнаружен в загруженной сборке worker. Ticket302/job331a097dfa5b41ba841c70411083a189 сообщил timeout инициализации120с и completed0 (reports/green1-mcp.json), но это ошибочный статус MCP: сопровождающий в событии1442 нашёл нативный XML4/4 Passed, start/end04:48:12Z внутри окна job. SHA25677d732db5d82d738a54e1d64dded408e6d044cd99f19a3daf76ec1167c53a5a6 независимо сверен; копия reports/green-native-TestResults.xml. Подтверждена потеря callback/observer после reload, инфраструктурный ремонт остаётся у сопровождающего. Ticket304 уже начал подготовку к моменту сообщения, но тесты в нём не запускались, finish/receive завершён.

В настоящем GhostAvatar.prefab старый GameObject8391245486851893293 указан в m_RemovedGameObjects (GUID исходного Cyborg8e0e06323e4754f479a9f19788554652): отдельный аватар призрака эту копию уже удаляет. На старом корне только Transform, внешних сериализованных ссылок на его GameObject в исходном префабе не обнаружено. Пять дополнительных постоянных MeshRenderer должны сохраниться; итоговый список20, не15.

Правила игровых тегов не относят встроенный EditorOnly к GameTags.Custom: GameTagsTool прямо сохраняет его, GameTagsTests допускает его на неигровых узлах. TagManager не меняется.

До пользовательской проверки текущие RegisteredAvatarBodyTests не переписываются. Их требование включать вспомогательное тело устарело; после приёмки BodySkins и отрицательная проверка списка должны отражать владение геометрией. Прямая резервная проверка существующих assertions (tools/run-existing-regressions.cs) отмечается отдельно от нативного Test Runner.

## Проверка исправленного префаба

Snapshot48e1ce7f17ad6d548a89279456f1979a0a06c90c, ticket305. Импорт прошёл без ошибок компиляции. tools/validate-cyborg.cs создал неактивный экземпляр под неактивным контейнером и удалил его в finally. Результат reports/asset-probe.json: serializedCount20, legacyRendererCount14, editorOnly=true, auxiliaryExcluded=true, rebuildPreservesSerializedSet=true, noDeadReferences=true, hideWorks/showWorks=true, depthBefore/depthAfter0, ghostRendererCount20 и живые ссылки. Это не Start/Play Mode/сетевой lifecycle.

GhostAvatarTests: jobc08c3d6d22a04076869d3ec9db48cbeb,10/10 Passed, reports/ghost-mcp.json. Prefabs_TagsMatchRules: job597fc8a4405747b086c360b5c6f7a3a1,1/1 Passed, reports/tags-mcp.json. Первоначальная комбинация group_names и test_names выбрала0 тестов (reports/asset-native-TestResults.xml); нулевой прогон не засчитан, после доказательства завершения native run очищен только собственный orphan job и группы запущены раздельно. Новых тестовых ожиданий после изменения gameplay не добавлено. Резервный прямой вызов assertions не понадобился.

Полный suite, Android, Play Mode, сетевой матч и Quest не запускались. Устаревший RegisteredAvatarBodyTests.Список_рендереров_UxrAvatar_без_пустых_и_со_всем_телом требует14 авторских рендереров; эту ожидаемую проблему нужно устранить после приёмки пользователя по AGENTS.md, вместе с отрицательной проверкой на авторскую ветку. Принятого коммита и push нет.
