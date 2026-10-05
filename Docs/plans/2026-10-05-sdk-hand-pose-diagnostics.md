# Диагностика в штатном Hand Pose Editor

Поручение пользователя: начать реализацию после согласования интерфейса и GPU-подсветки.

## Контракт

В SDK Hand Pose Editor доступны переключатель рентгена и анализ текущей позы с выгрузкой JSON/24 изображений. Текущая форма кисти читается через transient descriptor из bones, включая unsaved rotations, и применяется исправленным SDK preview core на выбранном Grabbable/GrabPoint/side. Единственный HandRenderer; geometry/export принадлежат одному frozen session cache. Видимая диагностическая геометрия и экспорт имеют один fingerprint. Результат устаревает при изменении входа. Выгрузки: UserReports/HandPoseFit, вне Assets и Git.

SDK предоставляет callbacks GUI, завершённого обновления текущего контекста и закрытия окна. Сам анализатор, GPU и SceneView lifecycle принадлежат проектному Editor-модулю. SDK не зависит от него; наш asmdef ссылается на UltimateXR.Editor.

GPU строит кэшированное поле расстояний из BVH существующего FitSurface. Расстояния в метрах object-root без повторного scale. Только доказанно замкнутая однокомпонентная поверхность имеет знак; для остальных поле unsigned, inside=unknown. Ошибка трёхлинейной интерполяции задаётся половиной диагонали voxel. Контакт 0–2 мм включительно; пограничные/неопределённые значения жёлтые, уверенное вложение красное. Поле строится порциями по Z, максимум 8 млн voxel/512 по оси. Область вокруг кисти редактируется; точки вне неё явно не оценены. Слишком крупное поле не выдаёт успешный live analysis, простой рентген остаётся доступным.

SceneView рисует собственные временные meshes/materials. На время SceneView camera исходные Renderer и SDK surrogate proxies подавляются через forceRenderingOff; завершение кадра условно возвращает исходное значение. Вложенная Game camera получает исходное значение, Hierarchy/SceneVisibility не меняются. Renderer.enabled, sharedMaterial, PropertyBlock и transforms не переписываются. Toggle off, закрытие, Play/reload освобождают только свои ресурсы. Нет новых SDK/avatar GameObjects, Play, чужих singleton/UID overrides, SaveAssets, изменения poses/prefabs.

CPU-ядро полного анализа остаётся эталоном. Источник editor_live_pose не утверждает runtime/Quest совпадение или фактическую поддержку одним grabbable. Контекст и pose serialization/fingerprint сохраняются. Анализ не сохраняет сам pose asset; штатный SDK AutoSave работает независимо.

## Пакеты исполнения

1. Подготовить source/Editor baseline и временный RED, подтверждающий отсутствие текущей GPU/live диагностики. Проверить GPU API/поддержку только чтением. Lease release после terminal результата.
2. Добавить read-only editor snapshot и ленивую PreviewRenderUtility, export BVH без второго builder, metadata и callbacks SDK, Editor assembly dependency, ignored output path.
3. GPU field compute + shader, CPU field owner/chunked build/query, pure quality/limit helpers. Не обещать FPS до измерения.
4. SDK diagnostics controller: Object/side/settings, idle snapshots, SceneView overlay/contact pairs, fingerprint/staleness, full CPU export из текущего snapshot. Полные вычисления только по кнопке.
5. В собственном коротком lease применить подготовленные source edits/import/compile; actual console/Android gate. Не держать lease во время написания/анализа.
6. Native диагностические fixtures без SDK компонентов: GPU/CPU distances/sign/error, live renderer readback/pose fingerprint, frozen export JSON/PNG, teardown/foreign preservation. Существующие применимые geometry tests. Постоянные игровые tests и avatar expectations не менять.
7. Документация SDK patch/инструмент/README/CHANGELOG, scoped diff/остатки. Коммит только после пользовательской Unity UI-приёмки; runtime two-held/real quality/Quest остаются отдельно.

## Приёмка пользователя

В штатном SDK окне выбрать аватар/позу, предмет и сторону; включить рентген, менять пальцы/Blend и вращать SceneView. Проверить отсутствие непрозрачного surrogate поверх рентгена, неизменность Game View и материалов/исходных transforms. Проверить unknown на открытом оружии и контакты на пригодной поверхности; после анализа JSON/24 стандартных PNG и дополнительный текущий ракурс относятся к текущему snapshot, после правки результат устарел. Выключение/закрытие возвращает обычное отображение.

## Полученные проверки

2026-10-05: native GPU fixture — 30/30 (дистанции/знак, граница 2 мм, unknown/open mesh, лимит области, реальные цвета shader, концы пар на настоящих гранях, frozen CPU JSON/24 PNG и 25-й ракурс с OtherHand). Предметный endpoint сначала дал RED 2/5; после перехода от SDF gradient к GPU BVH Nearest — 5/5 GREEN. Native lifecycle state — 7/7: report fingerprint не меняет fingerprint старых meshes, запрос немедленного capture и атомарная очистка field/query/contacts. Native camera scope — 14/14 (HideAndDontSave renderer, вложенная Game camera, cleanup, чужое forceRenderingOff, материалы/PropertyBlock, SceneVisibility, dirty/selection/Undo). Реальный URP render — 5/5: SceneView-пиксель фона RGB 8/8/8, Game-пиксель исходного красного меша 237/0/0, значения Renderer возвращаются после обеих камер. AndroidCompileGate — PASS, ошибок HandPose в Console нет. Существующие 37 математических NUnit cases вызваны напрямую в загруженных Unity assemblies: 37/37; это не прогон штатного NUnit runner. Runner не запускался поверх несохранённой пользовательской Lobby.

Артефакты находятся в игнорируемом `tmp/hand-fit-sdk-live-20261005/`: `gpu-native-result.json`, `gpu-endpoint-red.json`, `live-state-result.json`, `camera-scope-result.json`, `camera-render-result.json`, `math-regression.json`, `android-gate.json`. Малая GPU fixture не измеряет производительность редактора на оружии/Quest. Read-only capture реального сценового avatar/grabbable в этом пакете недоступен; пользовательские SDK UI/held/runtime/Quest проверки остаются открытыми. Повторное независимое read-only ревью: Critical/Important не осталось, машинный срез готов к UI-приёмке.

PNG с источником editor_live_pose подписывается EDITOR FRAME; runtime-подпись остаётся у игрового источника. Новая сборка импортирована после завершения человеческого Play; Stop агент не вызывал. Native texture-label fixture подтвердил разные подписи Editor/runtime (`caption-result.json`), GPU 30/30 и lifecycle 7/7 повторены на загруженном source, свежий Android PASS. Финальное чтение MCP Console вернуло транспортный отказ; перед этим ошибок HandPose было 0, native вызовы и Android gate отработали на новой сборке. Editor state после пакета ready, Play/compile/domain-reload=false, замок освобождён.

## Актуальный binding/cache срез

Пользователь принял MEF preview skinning. Новый [binding/cache срез](2026-10-05-sdk-preview-diagnostics-binding.md) импортирован:11/11 binding,11/11 query state,5/5 cleanup, GPU30/30/math37/37/camera14/14/Android PASS. Median idle input check1,628 мс, CaptureCount1/100. Export25PNG с одинаковым fingerprint, контакты просмотрены. UI orbit/zoom/drag acceptance ещё открыта; старый отказ CPU polling/непривязанной руки выше описывает предыдущий срез.
