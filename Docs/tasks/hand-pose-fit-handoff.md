# Hand Pose Fit — контрольная точка для продолжения

Task ID: `hand-pose-fit-2026-10-05`. Дата: 2026-10-05. Пользователь прямо поручил сохранить текущий незавершённый статус коммитом. Это checkpoint, не утверждение полной UI/Quest-приёмки.

## Начать новый чат

Прочитать этот файл, `Docs/hand-pose-fit-tool.md`, `Docs/plans/2026-10-05-sdk-preview-diagnostics-binding.md`, затем актуальный `AGENTS.md`. Продолжать проверку SDK-рентгена и оценщика хвата; не повторять импорт поз и уже принятый SDK skinning patch без нового дефекта.

Локальный итоговый пакет (игнорируется Git): [index.html](report/hand-pose-fit-2026-10-05/index.html). Другой чат на этой машине имеет доступ к нему; чистый clone Git не содержит JSON/PNG. Для переноса на другую машину скопировать `Docs/tasks/report/hand-pose-fit-2026-10-05/` и ранее сохранённый `Docs/tasks/report/hand-pose-fit-quality-2026-10-05/` вместе с checkout. Отчёты/архивы не добавлять в историю.

## Запрос пользователя и принятые решения

- Анализировать посадку MEF на оружии: зазоры, пересечения, контакт и изображения для визуальной оценки агентом. Контакт — **0–2 мм включительно**; это допуск близости, а не откалиброванный балл качества.
- SDK Cyborg и BigHands, включая неоружейные предметы, служат reference; сравнивались MEF, оригинальные руки оружейных паков, основной хват и поддержка.
- Диагностика встроена в штатный UltimateXR Hand Pose Editor: интерактивный SceneView xray и кнопка полного анализа/экспорта. GPU — приближённое поле/цвет/контактные запросы; полный CPU report — по кнопке.
- Пользователь **подтвердил, что исправленное SDK preview MEF выглядит отлично**. Новые UI-рентген, orbit/zoom и drag fingers после cache/binding patch ещё не приняты пользователем. Игровое wrist/controller/IK размещение и Quest отдельно.

## Что сделано

1. Editor-only анализатор: фактические меши, BVH, площади/расстояния по зонам/фалангам, нормали, contact patches, пересечения, local analytic volume/reliability, JSON/галереи/24 PNG. Отдельный runtime read-only capture после SDK update. Калибровка/holdout не подтверждают точность реальных хватов: многие случаи корректно дают unknown/abstain.
2. SDK patch44: callbacks GUI/context/close в `UxrHandPoseEditorWindow`, diagnostics clicks исключены из штатного auto-save/blend reset.
3. SDK patch45: `UxrPreviewHandGripMesh` использует существующий `UxrAvatarRig.UpdateHandUsingDescriptor` + Unity `BakeMesh(false)` на собственных plain transforms/SMR. Cached `TransformRelativeToHand` исходного пака больше не автор деформации. Сохранены API/rigid grabber frame/mesh identity; `MeshExt.ExtractSubMesh` получил совместимый overload с source indices. Gameplay pose assets/аватары не менялись.
4. `HandPoseSdkPreviewSession`: один frozen snapshot для overlay/GPU/CPU export; текущие unsaved bones читаются в transient Fixed descriptor, единственный SDK HandRenderer ставится на выбранный snap. Явные Grabbable/GrabPoint/side. Основной/поддерживающий хват отдельно; фиктивной второй кисти нет.
5. Geometry key учитывает mesh revisions, bones/matrices/rootBone/quality/blendshape, snap, LOD membership/settings. Камера и Renderer dirty revision от `forceRenderingOff` исключены. Object geometry/hash/GPU field переиспользуются при правке пальцев. Generation guard отвергает старый readback при A→B→A. Idle не требует постоянного SceneView repaint. Missing inputs освобождают cache и после анализа без xray.

## Проверено и где лежат доказательства

- Свежая checkpoint verification: **63/63** actual NUnit methods напрямую в Editor + **AndroidCompileGate PASS**. Исходник воспроизводимого вызова: `Tools/hand-pose-fit/verify-editor-kernels.cs`. [Локальный результат](report/hand-pose-fit-2026-10-05/checkpoint-verification.json).
- SDK skinning: старый MEF Grip/Support Left/Right RMS85,6–144,3мм, max297,4мм; GREEN12/12 max0,000323мм. Постоянные parity tests15/15. Cyborg Fixed/Blend — контроль. [Локальные RED/GREEN и scripts](report/hand-pose-fit-2026-10-05/sdk-preview-runtime/).
- Binding/cache11/11, query state11/11, cleanup5/5, GPU30/30, math37/37, camera ownership14/14. Independent reviewer: Critical/Important нет.
- Performance fixture:100 unchanged checks→1 capture; initial189мс, median1,628мс/P951,686мс. Это **input polling**, не full SDK UI FPS. При изменении geometry первый capture всё ещё синхронный; оптимизировать дальше только по реальному профилю.
- Frozen export: CaptureCount1, overlay/report SHA одинаковый,25PNG,203 контактные пары; картинки просмотрены. Это read-only prefab current bind-pose bones на snap, **не benchmark авторского MEF хвата**. [Локальная галерея](report/hand-pose-fit-2026-10-05/galleries/sdk-binding-preview/index.html).
- Первые lag причины: actual scene Tick443мс, capture388мс + redundant object hash97мс, несколько LOD, hand/object расстояние1,81м. [Локальная диагностика](report/hand-pose-fit-2026-10-05/lag-diagnosis/findings.md).
- Ранние reference/comparison/quality JSON находятся в [локальном history](report/hand-pose-fit-2026-10-05/history/); ранее собранная [локальная quality галерея](report/hand-pose-fit-quality-2026-10-05/index.html) содержит195 отчётов и input snapshots. Некоторые legacy capture scripts всё ещё требуют Temp каталоги; восстанавливать input paths через archive-manifest, не считать отсутствие Temp отказом логики.
- Последний read_console после прошлого пакета дал транспортный отказ, не compiler error. Свежие63/63+Android выше — результат текущего checkpoint. Standard NUnit scene runner не применялся к dirty человеческой сцене.

## Продолжить здесь

1. Получить пользовательскую проверку новой панели на MEF_Optimized + AK105: main/support, Left/Right, правильная pose name, xray orbit/zoom, live drag fingers, видимые пары0–2мм. Точка должна иметь PositionAndRotation и поддерживать сторону. Панель показывает, если current pose отличается от назначенной.
2. Если есть лаг: сначала реальный EditorPerformanceTracker/Profiler и dirty key/capture count; не лечить случай отключением отдельных объектов. Не повторять каждую frame BakeMesh/hash/topology. Камера не должна менять key.
3. Проверить old async results/закрытие/удаление target/Play transition в реальном UI, сохранив Game camera и original renderer state. В native package generation и lifecycle зелёные, но full UI ещё открыт.
4. Для fresh reports выбирать actual SDK editor avatar/point/side. Не сравнивать отчёт о bind-pose fixture с реально принятым authored grip. Pose assets не сохранять из диагностики; capture/export читают frozen geometry.
5. Продолжить quality calibration только после review masks/contact regions/local volumes и runtime held two-hand sources. `analysis_incomplete`, unknown inside и NO_GO не подменять оценкой «плохой хват».

## Ограничения и дисциплина

Editor общий: prepared Unity operations только под `Tools/agents/unity-lock.sh`, release сразу после terminal result/cleanup, до анализа/документации. Не останавливать человеческий Play. Перед source import проверить Play/compile и SDK window: reload окна может вызвать штатный autosave.

Fixtures — собственные plain transforms/SMR/meshes; не создавать/активировать SDK Avatar/GrabManager/UID manager, не подменять singleton/private registry и не менять source prefab bones ради проверки. Read-only prefab native fixtures проверяют pipeline, не runtime game truth. Reports — локальные ignored packages; Source/meta/актуальные планы/контракты и этот handoff — в Git.

Checkpoint включает только HandPoseReview/связанные SDK patches и документацию. В workspace есть другие незакоммиченные оружейные/ботовые/карточные изменения; их не включать, не откатывать. Общие Docs содержат чужие pending sections: commit/index берёт только hand-pose sections. Git→Plastic hook при частичном документе обязан пропустить mirror из-за отличия working bytes от commit; не делать workspace-wide check-in.
